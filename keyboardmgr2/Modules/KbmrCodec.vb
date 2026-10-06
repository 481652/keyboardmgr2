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

    Public Function Serialize(recording As KbmrRecording) As Byte()
        ValidateRecording(recording)

        Dim payload As Byte()
        Using payloadStream As New MemoryStream()
            Using writer As New BinaryWriter(payloadStream, Encoding.UTF8, True)
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
            payload = payloadStream.ToArray()
        End Using

        Dim compressed As Byte()
        Using compressedStream As New MemoryStream()
            Using deflate As New DeflateStream(compressedStream, CompressionLevel.Optimal, True)
                deflate.Write(payload, 0, payload.Length)
            End Using
            compressed = compressedStream.ToArray()
        End Using

        Dim checksum As Byte()
        Using sha256 As SHA256 = SHA256.Create()
            checksum = sha256.ComputeHash(compressed)
        End Using

        Using output As New MemoryStream()
            Using writer As New BinaryWriter(output, Encoding.UTF8, True)
                writer.Write(Magic)
                writer.Write(FormatVersion)
                writer.Write(recording.SampleIntervalMs)
                writer.Write(recording.ScreenWidth)
                writer.Write(recording.ScreenHeight)
                writer.Write(recording.DurationMs)
                writer.Write(recording.Events.Count)
                writer.Write(recording.CreatedUtc.Ticks)
                writer.Write(compressed.Length)
                writer.Write(checksum)
                writer.Write(compressed)
            End Using
            If output.Length > MaximumFileSize Then Throw New InvalidDataException("录制文件超过 128 MiB。")
            Return output.ToArray()
        End Using
    End Function

    Public Function Deserialize(fileBytes As Byte()) As KbmrRecording
        Dim headerSize As Integer = Magic.Length + 2 + 4 + 4 + 4 + 8 + 4 + 8 + 4 + 32
        If fileBytes Is Nothing OrElse fileBytes.Length < headerSize OrElse fileBytes.Length > MaximumFileSize Then
            Throw New InvalidDataException("录制文件大小无效。")
        End If

        Dim recording As New KbmrRecording()
        Dim eventCount As Integer
        Dim compressed As Byte()
        Using input As New MemoryStream(fileBytes, False)
            Using reader As New BinaryReader(input, Encoding.UTF8, True)
                Dim actualMagic As Byte() = reader.ReadBytes(Magic.Length)
                If Not BytesEqual(actualMagic, Magic) Then Throw New InvalidDataException("这不是键鼠管家录制文件。")
                Dim version As UShort = reader.ReadUInt16()
                If version <> FormatVersion Then Throw New InvalidDataException("不支持该录制文件版本。")
                recording.SampleIntervalMs = reader.ReadInt32()
                recording.ScreenWidth = reader.ReadInt32()
                recording.ScreenHeight = reader.ReadInt32()
                recording.DurationMs = reader.ReadInt64()
                eventCount = reader.ReadInt32()
                Dim createdTicks As Long = reader.ReadInt64()
                If createdTicks < 0 OrElse createdTicks > DateTime.MaxValue.Ticks Then createdTicks = 0
                recording.CreatedUtc = New DateTime(createdTicks, DateTimeKind.Utc)
                Dim compressedLength As Integer = reader.ReadInt32()
                If recording.SampleIntervalMs < 1 OrElse recording.SampleIntervalMs > 1000 Then Throw New InvalidDataException("录制文件的鼠标采样频率无效。")
                If recording.DurationMs < 0 OrElse recording.DurationMs > MaximumDurationMs Then Throw New InvalidDataException("录制文件的时长无效。")
                If eventCount < 1 OrElse eventCount > MaximumEventCount Then Throw New InvalidDataException("录制文件的事件数量无效。")
                If compressedLength < 0 OrElse compressedLength <> input.Length - input.Position - 32 Then Throw New InvalidDataException("录制文件长度无效。")
                Dim expectedChecksum As Byte() = reader.ReadBytes(32)
                compressed = reader.ReadBytes(compressedLength)
                Using sha256 As SHA256 = SHA256.Create()
                    If Not BytesEqual(expectedChecksum, sha256.ComputeHash(compressed)) Then Throw New InvalidDataException("录制文件校验失败，内容可能已损坏。")
                End Using
            End Using
        End Using

        Dim payload As Byte()
        Using compressedStream As New MemoryStream(compressed, False), payloadStream As New MemoryStream()
            Using deflate As New DeflateStream(compressedStream, CompressionMode.Decompress)
                Dim buffer(8191) As Byte
                Do
                    Dim bytesRead As Integer = deflate.Read(buffer, 0, buffer.Length)
                    If bytesRead = 0 Then Exit Do
                    payloadStream.Write(buffer, 0, bytesRead)
                    If payloadStream.Length > MaximumFileSize Then Throw New InvalidDataException("解压后的录制内容过大。")
                Loop
            End Using
            payload = payloadStream.ToArray()
        End Using

        Using payloadStream As New MemoryStream(payload, False)
            Using reader As New BinaryReader(payloadStream, Encoding.UTF8, True)
                Dim lastOffset As Long = 0
                Dim lastX As Integer = 0
                Dim lastY As Integer = 0
                For index As Integer = 1 To eventCount
                    Dim delta As Long = CLng(ReadVarInt(reader))
                    lastOffset += delta
                    Dim kindValue As Byte = reader.ReadByte()
                    Dim item As New KbmrEvent With {.OffsetMs = lastOffset, .Kind = CType(kindValue, KbmrEventKind)}
                    Select Case item.Kind
                        Case KbmrEventKind.KeyDown, KbmrEventKind.KeyUp
                            item.Vk = CUShort(ReadVarInt(reader))
                        Case KbmrEventKind.MouseDown, KbmrEventKind.MouseUp
                            item.Button = reader.ReadByte()
                        Case KbmrEventKind.MouseMove
                            lastX += UnZigZag(ReadVarInt(reader))
                            lastY += UnZigZag(ReadVarInt(reader))
                            item.X = lastX
                            item.Y = lastY
                        Case KbmrEventKind.Wheel
                            item.Delta = UnZigZag(ReadVarInt(reader))
                        Case Else
                            Throw New InvalidDataException("录制包含未知的事件类型。")
                    End Select
                    recording.Events.Add(item)
                Next
                If payloadStream.Position <> payloadStream.Length Then Throw New InvalidDataException("录制文件包含无法识别的数据。")
            End Using
        End Using
        Return recording
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
            If reader.BaseStream.Position >= reader.BaseStream.Length Then Throw New InvalidDataException("录制文件数据不完整。")
            Dim current As Byte = reader.ReadByte()
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
