'Kbmr 录制文件编解码（.kbmr 二进制格式）：
'明文文件头（Magic/版本/采样频率/分辨率/时长/事件数/时间戳）+ Deflate 压缩事件流 + SHA256 校验。
'事件流：VarInt 增量时间戳 + 事件类型 + 载荷（键盘虚拟键码 / 鼠标按钮 / 坐标增量 / 滚轮增量）。
Imports System.IO
Imports System.IO.Compression
Imports System.Security.Cryptography
Imports System.Text

Public Module KbmrCodec
    Private ReadOnly Magic As Byte() = Encoding.ASCII.GetBytes("KBMR")
    Private Const FormatVersion As UShort = 1
    Public Const MaximumFileSize As Integer = 128 * 1024 * 1024
    Public Const MaximumEventCount As Integer = 2000000
    Public Const MaximumDurationMs As Long = 24L * 60 * 60 * 1000
    Private Const HeaderSize As Integer = 74

    Public Function Serialize(recording As KbmrRecording) As Byte()
        Using output As New MemoryStream()
            SerializeToStream(recording, output)
            Return output.ToArray()
        End Using
    End Function

    '先流式写入同目录临时文件，成功后替换目标，避免失败时破坏已有录制。
    Public Sub WriteFile(recording As KbmrRecording, filePath As String)
        ValidateRecording(recording)
        Dim targetPath As String = Path.GetFullPath(filePath)
        Dim tempPath As String = targetPath & "." & Guid.NewGuid().ToString("N") & ".tmp"
        Try
            Using output As New FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 65536)
                SerializeToStream(recording, output)
            End Using
            If File.Exists(targetPath) Then
                File.Replace(tempPath, targetPath, Nothing)
            Else
                File.Move(tempPath, targetPath)
            End If
        Finally
            If File.Exists(tempPath) Then File.Delete(tempPath)
        End Try
    End Sub

    '输出流由调用者持有，须可读写和定位。文件调用直接落盘并顺序读取校验，不缓存完整压缩载荷。
    Private Sub SerializeToStream(recording As KbmrRecording, output As Stream)
        ValidateRecording(recording)
        output.SetLength(0)
        output.Position = 0
        output.Write(New Byte(HeaderSize - 1) {}, 0, HeaderSize)
        Using deflate As New DeflateStream(output, CompressionLevel.Optimal, True)
            Using writer As New BinaryWriter(deflate, Encoding.UTF8, True)
                Dim lastOffset As Long = 0
                Dim lastX As Integer = 0
                Dim lastY As Integer = 0
                For Each item As KbmrEvent In recording.Events
                    Dim offset As Long = Math.Max(lastOffset, Math.Max(0, item.OffsetMs))
                    WriteVarInt(writer, CULng(offset - lastOffset))
                    lastOffset = offset
                    writer.Write(CByte(item.Kind))
                    Select Case item.Kind
                        Case KbmrEventKind.KeyDown, KbmrEventKind.KeyUp
                            WriteVarInt(writer, CULng(item.Vk))
                        Case KbmrEventKind.MouseDown, KbmrEventKind.MouseUp
                            writer.Write(item.Button)
                        Case KbmrEventKind.MouseMove
                            WriteVarInt(writer, ZigZag(item.X - lastX))
                            WriteVarInt(writer, ZigZag(item.Y - lastY))
                            lastX = item.X
                            lastY = item.Y
                        Case KbmrEventKind.Wheel
                            WriteVarInt(writer, ZigZag(item.Delta))
                        Case Else
                            Throw New InvalidDataException("录制包含未知的事件类型。")
                    End Select
                Next
            End Using
        End Using
        If output.Length > MaximumFileSize Then Throw New InvalidDataException("录制文件超过 128 MiB。")
        Dim compressedLength As Integer = CInt(output.Length - HeaderSize)

        Dim checksum As Byte()
        output.Position = HeaderSize
        Using sha256 As SHA256 = SHA256.Create()
            checksum = sha256.ComputeHash(output)
        End Using

        output.Position = 0
        Using writer As New BinaryWriter(output, Encoding.UTF8, True)
            writer.Write(Magic)
            writer.Write(FormatVersion)
            writer.Write(recording.SampleIntervalMs)
            writer.Write(recording.ScreenWidth)
            writer.Write(recording.ScreenHeight)
            writer.Write(recording.DurationMs)
            writer.Write(recording.Events.Count)
            writer.Write(recording.CreatedUtc.Ticks)
            writer.Write(compressedLength)
            writer.Write(checksum)
        End Using
        output.Position = output.Length
    End Sub

    Public Function Deserialize(fileBytes As Byte()) As KbmrRecording
        If fileBytes Is Nothing Then Throw New InvalidDataException("录制文件大小无效。")
        Using input As New MemoryStream(fileBytes, False)
            Return DecodeRecording(input)
        End Using
    End Function

    Public Function Deserialize(filePath As String) As KbmrRecording
        Using input As FileStream = File.OpenRead(filePath)
            Dim recording As KbmrRecording = Nothing
            Dim eventCount As Integer
            Dim deflate As DeflateStream = OpenEventStream(input, recording, eventCount)
            Using deflate
                recording.Events = ReadEvents(deflate, recording, eventCount, eventCount)
            End Using
            recording.EventsLoaded = True
            Return recording
        End Using
    End Function

    '仅保留预览所需事件，其余事件流式解析以校验格式并避免构造完整事件列表。
    Public Function DeserializePreview(filePath As String, previewLimit As Integer) As KbmrRecording
        If previewLimit < 0 Then Throw New ArgumentOutOfRangeException(NameOf(previewLimit))
        Using input As FileStream = File.OpenRead(filePath)
            Dim metadata As KbmrRecording = Nothing
            Dim eventCount As Integer
            Dim deflate As DeflateStream = OpenEventStream(input, metadata, eventCount)
            Using deflate
                metadata.Events = ReadEvents(deflate, metadata, eventCount, previewLimit)
            End Using
            metadata.EventsLoaded = False
            Return metadata
        End Using
    End Function

    Private Function DecodeRecording(input As Stream) As KbmrRecording
        Dim recording As KbmrRecording = Nothing
        Dim eventCount As Integer
        Dim deflate As DeflateStream = OpenEventStream(input, recording, eventCount)
        Using deflate
            recording.Events = ReadEvents(deflate, recording, eventCount, eventCount)
        End Using
        recording.EventsLoaded = True
        Return recording
    End Function

    Private Function ReadEvents(stream As Stream, recording As KbmrRecording, eventCount As Integer, previewLimit As Integer) As List(Of KbmrEvent)
        Dim result As New List(Of KbmrEvent)(Math.Min(eventCount, previewLimit))
        Using reader As New BinaryReader(stream, Encoding.UTF8, True)
            Dim lastOffset As Long = 0
            Dim lastX As Integer = 0
            Dim lastY As Integer = 0
            For index As Integer = 0 To eventCount - 1
                Dim item As KbmrEvent = ReadEvent(reader, lastOffset, lastX, lastY, index < previewLimit)
                If item IsNot Nothing Then result.Add(item)
            Next
            If stream.ReadByte() <> -1 Then Throw New InvalidDataException("录制文件包含无法识别的数据。")
        End Using
        recording.EventCount = eventCount
        Return result
    End Function

    Private Function OpenEventStream(input As Stream, ByRef recording As KbmrRecording, ByRef eventCount As Integer) As DeflateStream
        If Not input.CanSeek OrElse input.Length < HeaderSize OrElse input.Length > MaximumFileSize Then
            Throw New InvalidDataException("录制文件大小无效。")
        End If

        recording = New KbmrRecording()
        Using reader As New BinaryReader(input, Encoding.UTF8, True)
            If Not BytesEqual(reader.ReadBytes(Magic.Length), Magic) Then Throw New InvalidDataException("这不是键鼠管家录制文件。")
            If reader.ReadUInt16() <> FormatVersion Then Throw New InvalidDataException("不支持该录制文件版本。")
            recording.SampleIntervalMs = reader.ReadInt32()
            recording.ScreenWidth = reader.ReadInt32()
            recording.ScreenHeight = reader.ReadInt32()
            recording.DurationMs = reader.ReadInt64()
            eventCount = reader.ReadInt32()
            Dim createdTicks As Long = reader.ReadInt64()
            Dim compressedLength As Integer = reader.ReadInt32()
            If recording.SampleIntervalMs < 1 OrElse recording.SampleIntervalMs > 1000 Then Throw New InvalidDataException("录制文件的鼠标采样频率无效。")
            If recording.DurationMs < 0 OrElse recording.DurationMs > MaximumDurationMs Then Throw New InvalidDataException("录制文件的时长无效。")
            If eventCount < 1 OrElse eventCount > MaximumEventCount Then Throw New InvalidDataException("录制文件的事件数量无效。")
            If compressedLength < 0 OrElse compressedLength <> input.Length - input.Position - 32 Then Throw New InvalidDataException("录制文件长度无效。")
            If createdTicks < 0 OrElse createdTicks > DateTime.MaxValue.Ticks Then createdTicks = 0
            recording.CreatedUtc = New DateTime(createdTicks, DateTimeKind.Utc)
            Dim expectedChecksum As Byte() = reader.ReadBytes(32)
            If expectedChecksum.Length <> 32 Then Throw New InvalidDataException("录制文件校验数据不完整。")
            Dim compressedOffset As Long = input.Position
            Using sha256 As SHA256 = SHA256.Create()
                If Not BytesEqual(expectedChecksum, sha256.ComputeHash(input)) Then Throw New InvalidDataException("录制文件校验失败，内容可能已损坏。")
            End Using
            input.Position = compressedOffset
        End Using
        Return New DeflateStream(input, CompressionMode.Decompress, True)
    End Function

    Private Function ReadEvent(reader As BinaryReader, ByRef lastOffset As Long, ByRef lastX As Integer,
                               ByRef lastY As Integer, capture As Boolean) As KbmrEvent
        lastOffset += CLng(ReadVarInt(reader))
        Dim kind As KbmrEventKind = CType(reader.ReadByte(), KbmrEventKind)
        Dim item As KbmrEvent = If(capture, New KbmrEvent With {.OffsetMs = lastOffset, .Kind = kind}, Nothing)
        Select Case kind
            Case KbmrEventKind.KeyDown, KbmrEventKind.KeyUp
                Dim vk As UShort = CUShort(ReadVarInt(reader))
                If capture Then item.Vk = vk
            Case KbmrEventKind.MouseDown, KbmrEventKind.MouseUp
                Dim button As Byte = reader.ReadByte()
                If capture Then item.Button = button
            Case KbmrEventKind.MouseMove
                lastX += UnZigZag(ReadVarInt(reader))
                lastY += UnZigZag(ReadVarInt(reader))
                If capture Then
                    item.X = lastX
                    item.Y = lastY
                End If
            Case KbmrEventKind.Wheel
                Dim delta As Integer = UnZigZag(ReadVarInt(reader))
                If capture Then item.Delta = delta
            Case Else
                Throw New InvalidDataException("录制包含未知的事件类型。")
        End Select
        Return item
    End Function

    '只读取录制文件头，用于启动时快速建立历史列表。
    Public Function ReadMetadata(filePath As String) As KbmrRecording
        If String.IsNullOrWhiteSpace(filePath) Then Throw New ArgumentException("录制文件路径不能为空。", NameOf(filePath))
        Using input As FileStream = File.OpenRead(filePath)
            If input.Length < HeaderSize OrElse input.Length > MaximumFileSize Then Throw New InvalidDataException("录制文件大小无效。")
            Using reader As New BinaryReader(input, Encoding.UTF8, True)
                If Not BytesEqual(reader.ReadBytes(Magic.Length), Magic) Then Throw New InvalidDataException("这不是键鼠管家录制文件。")
                If reader.ReadUInt16() <> FormatVersion Then Throw New InvalidDataException("不支持该录制文件版本。")

                Dim result As New KbmrRecording With {
                    .SampleIntervalMs = reader.ReadInt32(),
                    .ScreenWidth = reader.ReadInt32(),
                    .ScreenHeight = reader.ReadInt32(),
                    .DurationMs = reader.ReadInt64(),
                    .EventCount = reader.ReadInt32()
                }
                Dim createdTicks As Long = reader.ReadInt64()
                Dim compressedLength As Integer = reader.ReadInt32()
                If result.SampleIntervalMs < 1 OrElse result.SampleIntervalMs > 1000 Then Throw New InvalidDataException("录制文件的鼠标采样频率无效。")
                If result.DurationMs < 0 OrElse result.DurationMs > MaximumDurationMs Then Throw New InvalidDataException("录制文件的时长无效。")
                If result.EventCount < 1 OrElse result.EventCount > MaximumEventCount Then Throw New InvalidDataException("录制文件的事件数量无效。")
                If compressedLength < 0 OrElse compressedLength <> input.Length - input.Position - 32 Then Throw New InvalidDataException("录制文件长度无效。")
                If createdTicks < 0 OrElse createdTicks > DateTime.MaxValue.Ticks Then createdTicks = 0
                result.CreatedUtc = New DateTime(createdTicks, DateTimeKind.Utc)
                Return result
            End Using
        End Using
    End Function

    Private Sub ValidateRecording(recording As KbmrRecording)
        If recording Is Nothing OrElse recording.Events Is Nothing OrElse recording.Events.Count < 1 Then Throw New InvalidDataException("录制内容为空。")
        If recording.Events.Count > MaximumEventCount Then Throw New InvalidDataException("录制事件数量超过上限。")
        If recording.SampleIntervalMs < 1 OrElse recording.SampleIntervalMs > 1000 Then Throw New InvalidDataException("鼠标采样频率超出允许范围。")
        If recording.DurationMs < 0 OrElse recording.DurationMs > MaximumDurationMs Then Throw New InvalidDataException("录制时长超出允许范围。")
    End Sub

    Private Sub WriteVarInt(writer As BinaryWriter, value As ULong)
        Do
            Dim current As Byte = CByte(value And &H7FUL)
            value >>= 7
            If value <> 0 Then current = current Or &H80
            writer.Write(current)
        Loop While value <> 0
    End Sub

    Private Function ReadVarInt(reader As BinaryReader) As ULong
        Dim result As ULong = 0
        Dim shift As Integer = 0
        Do
            Dim current As Byte
            Try
                current = reader.ReadByte()
            Catch ex As EndOfStreamException
                Throw New InvalidDataException("录制文件数据不完整。", ex)
            End Try
            result = result Or (CULng(current And &H7F) << shift)
            If (current And &H80) = 0 Then Return result
            shift += 7
            If shift > 63 Then Throw New InvalidDataException("录制文件数据无效。")
        Loop
    End Function

    Private Function ZigZag(value As Integer) As ULong
        Return CULng((CLng(value) << 1) Xor (CLng(value) >> 31))
    End Function

    Private Function UnZigZag(value As ULong) As Integer
        Return CInt(CLng(value >> 1) Xor -CLng(value And 1UL))
    End Function

    Private Function BytesEqual(left As Byte(), right As Byte()) As Boolean
        If left Is Nothing OrElse right Is Nothing OrElse left.Length <> right.Length Then Return False
        Dim difference As Integer = 0
        For index As Integer = 0 To left.Length - 1
            difference = difference Or (left(index) Xor right(index))
        Next
        Return difference = 0
    End Function
End Module
