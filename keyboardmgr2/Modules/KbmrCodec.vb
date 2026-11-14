'Kbmr 录制文件编解码（.kbmr 二进制格式）：
'明文文件头（Magic/版本/采样频率/分辨率/DPI 缩放/时长/事件数/时间戳）+ Deflate 压缩事件流 + SHA256 校验。
'事件流：VarInt 增量时间戳 + 事件类型 + 载荷（键盘虚拟键码 / 鼠标按钮 / 坐标增量 / 滚轮增量）。
'格式版本 2 起，文件头的分辨率与鼠标事件坐标位于同一坐标空间（像素），鼠标按键事件也记录点击位置；
'版本 1 记录的是 WPF DIP 且丢失点击坐标，读取时无法还原，分辨率标记为未知（0），回放保持 1:1。
Imports System.IO
Imports System.IO.Compression
Imports System.Security.Cryptography
Imports System.Text

Public Module KbmrCodec
    Private ReadOnly Magic As Byte() = Encoding.ASCII.GetBytes("KBMR")
    Private ReadOnly Utf8 As Encoding = New UTF8Encoding(False)
    '当前写出的格式版本；读取时同时兼容版本 1
    Private Const FormatVersion As UShort = 2
    Private Const LegacyFormatVersion As UShort = 1
    '单边分辨率上限，只用于识别明显损坏的文件头（不限制合法的高分屏）
    Private Const MaximumScreenDimension As Integer = 32768
    '鼠标坐标绝对值上限，只用于识别损坏/恶意文件
    Private Const MaximumCoordinate As Integer = 1000000
    Public Const MaximumFileSize As Integer = 128 * 1024 * 1024
    Public Const MaximumEventCount As Integer = 2000000
    Public Const MaximumDurationMs As Long = 24L * 60 * 60 * 1000

    '只校验并解析明文头部，不解压事件流。历史列表启动时使用，文件选择后再调用 Deserialize。
    Public Function ReadMetadata(fileBytes As Byte(), Optional name As String = Nothing, Optional filePath As String = Nothing) As KbmrRecording
        Dim minimumHeaderSize As Integer = HeaderSizeFor(LegacyFormatVersion)
        If fileBytes Is Nothing OrElse fileBytes.Length < minimumHeaderSize OrElse fileBytes.Length > MaximumFileSize Then
            Throw New InvalidDataException("录制文件大小无效。")
        End If
        Using input As New MemoryStream(fileBytes, False)
            Using reader As New BinaryReader(input, Encoding.UTF8, True)
                If Not BytesEqual(reader.ReadBytes(Magic.Length), Magic) Then Throw New InvalidDataException("这不是键鼠管家录制文件。")
                Dim version As UShort = reader.ReadUInt16()
                If version <> FormatVersion AndAlso version <> LegacyFormatVersion Then Throw New InvalidDataException("不支持该录制文件版本。")
                If input.Length < HeaderSizeFor(version) Then Throw New InvalidDataException("录制文件大小无效。")
                Dim result As New KbmrRecording With {.Name = name, .FilePath = filePath}
                result.SampleIntervalMs = reader.ReadInt32()
                Dim width As Integer = reader.ReadInt32()
                Dim height As Integer = reader.ReadInt32()
                If version = FormatVersion Then result.DpiScalePermille = reader.ReadInt32()
                result.DurationMs = reader.ReadInt64()
                Dim eventCount As Integer = reader.ReadInt32()
                Dim createdTicks As Long = reader.ReadInt64()
                Dim compressedLength As Integer = reader.ReadInt32()
                If result.SampleIntervalMs < 1 OrElse result.SampleIntervalMs > 1000 Then Throw New InvalidDataException("录制文件的鼠标采样频率无效。")
                If result.DurationMs < 0 OrElse result.DurationMs > MaximumDurationMs Then Throw New InvalidDataException("录制文件的时长无效。")
                If eventCount < 1 OrElse eventCount > MaximumEventCount Then Throw New InvalidDataException("录制文件的事件数量无效。")
                If compressedLength < 0 OrElse compressedLength <> fileBytes.Length - input.Position - 32 Then Throw New InvalidDataException("录制文件长度无效。")
                If createdTicks < 0 OrElse createdTicks > DateTime.MaxValue.Ticks Then createdTicks = 0
                result.CreatedUtc = New DateTime(createdTicks, DateTimeKind.Utc)
                If version = FormatVersion AndAlso width > 0 AndAlso height > 0 Then
                    result.ScreenWidth = width
                    result.ScreenHeight = height
                End If
                result.SetMetadataOnly(eventCount)
                Return result
            End Using
        End Using
    End Function

    Public Function ReadMetadata(filePath As String) As KbmrRecording
        Dim info As New FileInfo(filePath)
        If Not info.Exists OrElse info.Length < HeaderSizeFor(LegacyFormatVersion) OrElse info.Length > MaximumFileSize Then
            Throw New InvalidDataException("录制文件大小无效。")
        End If
        Using input As New FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
            Using reader As New BinaryReader(input, Encoding.UTF8, True)
                If Not BytesEqual(reader.ReadBytes(Magic.Length), Magic) Then Throw New InvalidDataException("这不是键鼠管家录制文件。")
                Dim version As UShort = reader.ReadUInt16()
                If version <> FormatVersion AndAlso version <> LegacyFormatVersion Then Throw New InvalidDataException("不支持该录制文件版本。")
                If info.Length < HeaderSizeFor(version) Then Throw New InvalidDataException("录制文件大小无效。")
                Dim result As New KbmrRecording With {.Name = Path.GetFileNameWithoutExtension(filePath), .FilePath = filePath}
                result.SampleIntervalMs = reader.ReadInt32()
                Dim width As Integer = reader.ReadInt32()
                Dim height As Integer = reader.ReadInt32()
                If version = FormatVersion Then result.DpiScalePermille = reader.ReadInt32()
                result.DurationMs = reader.ReadInt64()
                Dim eventCount As Integer = reader.ReadInt32()
                Dim createdTicks As Long = reader.ReadInt64()
                Dim compressedLength As Integer = reader.ReadInt32()
                If result.SampleIntervalMs < 1 OrElse result.SampleIntervalMs > 1000 Then Throw New InvalidDataException("录制文件的鼠标采样频率无效。")
                If result.DurationMs < 0 OrElse result.DurationMs > MaximumDurationMs Then Throw New InvalidDataException("录制文件的时长无效。")
                If eventCount < 1 OrElse eventCount > MaximumEventCount Then Throw New InvalidDataException("录制文件的事件数量无效。")
                If compressedLength < 0 OrElse compressedLength <> info.Length - input.Position - 32 Then Throw New InvalidDataException("录制文件长度无效。")
                If createdTicks < 0 OrElse createdTicks > DateTime.MaxValue.Ticks Then createdTicks = 0
                result.CreatedUtc = New DateTime(createdTicks, DateTimeKind.Utc)
                If version = FormatVersion AndAlso width > 0 AndAlso height > 0 Then
                    result.ScreenWidth = width
                    result.ScreenHeight = height
                End If
                result.SetMetadataOnly(eventCount)
                Return result
            End Using
        End Using
    End Function

    Public Function Serialize(recording As KbmrRecording) As Byte()
        ValidateRecording(recording)

        'T19：事件流直接写入 Deflate（单流，省去未压缩 payload 的整块拷贝）；Fastest 级别显著缩短停止录制耗时，格式不变
        Dim compressed As Byte()
        Using compressedStream As New MemoryStream()
            Using deflate As New DeflateStream(compressedStream, CompressionLevel.Fastest, True),
                  bufferedDeflate As New BufferedStream(deflate, 64 * 1024),
                  writer As New BinaryWriter(bufferedDeflate, Utf8, True)
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
                            '版本 2 起同时记录点击位置（旧格式录制没有该信息，用标志位标记）
                            writer.Write(item.Button)
                            writer.Write(CByte(If(item.HasPosition, 1, 0)))
                            If item.HasPosition Then
                                WriteVarInt(writer, ZigZag(item.X - lastX))
                                WriteVarInt(writer, ZigZag(item.Y - lastY))
                                lastX = item.X
                                lastY = item.Y
                            End If
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
                writer.Flush()
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
                writer.Write(recording.DpiScalePermille)
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
        '两个版本的公共头长度相同，先按最小长度校验，读出格式版本后再校验完整头长度
        Dim minimumHeaderSize As Integer = HeaderSizeFor(LegacyFormatVersion)
        If fileBytes Is Nothing OrElse fileBytes.Length < minimumHeaderSize OrElse fileBytes.Length > MaximumFileSize Then
            Throw New InvalidDataException("录制文件大小无效。")
        End If

        Dim recording As New KbmrRecording()
        Dim version As UShort
        Dim recordedWidth As Integer
        Dim recordedHeight As Integer
        Dim eventCount As Integer
        Dim compressed As Byte()
        Using input As New MemoryStream(fileBytes, False)
            Using reader As New BinaryReader(input, Encoding.UTF8, True)
                Dim actualMagic As Byte() = reader.ReadBytes(Magic.Length)
                If Not BytesEqual(actualMagic, Magic) Then Throw New InvalidDataException("这不是键鼠管家录制文件。")
                version = reader.ReadUInt16()
                If version <> FormatVersion AndAlso version <> LegacyFormatVersion Then Throw New InvalidDataException("不支持该录制文件版本。")
                If input.Length < HeaderSizeFor(version) Then Throw New InvalidDataException("录制文件大小无效。")
                recording.SampleIntervalMs = reader.ReadInt32()
                recordedWidth = reader.ReadInt32()
                recordedHeight = reader.ReadInt32()
                '版本 2 起文件头多一个 DPI 缩放字段
                If version = FormatVersion Then recording.DpiScalePermille = reader.ReadInt32()
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
                '坐标增量用 Long 累加，避免损坏文件造成 Integer 溢出
                Dim lastX As Long = 0
                Dim lastY As Long = 0
                For index As Integer = 1 To eventCount
                    Dim delta As Long = ReadVarIntLong(reader)
                    lastOffset += delta
                    If lastOffset < 0 OrElse lastOffset > MaximumDurationMs Then Throw New InvalidDataException("录制文件的事件时间戳无效。")
                    Dim kindValue As Byte = reader.ReadByte()
                    Dim item As New KbmrEvent With {.OffsetMs = lastOffset, .Kind = CType(kindValue, KbmrEventKind)}
                    Select Case item.Kind
                        Case KbmrEventKind.KeyDown, KbmrEventKind.KeyUp
                            Dim vkValue As ULong = ReadVarInt(reader)
                            If vkValue > UShort.MaxValue Then Throw New InvalidDataException("录制文件包含无效的按键。")
                            item.Vk = CUShort(vkValue)
                        Case KbmrEventKind.MouseDown, KbmrEventKind.MouseUp
                            item.Button = reader.ReadByte()
                            If version = FormatVersion Then
                                item.HasPosition = reader.ReadByte() <> 0
                                If item.HasPosition Then
                                    item.X = ReadCoordinate(reader, lastX)
                                    item.Y = ReadCoordinate(reader, lastY)
                                End If
                            Else
                                '版本 1 没有记录点击坐标
                                item.HasPosition = False
                            End If
                        Case KbmrEventKind.MouseMove
                            item.X = ReadCoordinate(reader, lastX)
                            item.Y = ReadCoordinate(reader, lastY)
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
        '旧格式的屏宽高是 WPF DIP，无法还原录制时的像素分辨率（并且没有记录点击坐标），
        '统一标记为“未知”（0），回放时不参与分辨率缩放，保持原来的 1:1 行为。
        If version = LegacyFormatVersion Then
            recording.ScreenWidth = 0
            recording.ScreenHeight = 0
            recording.DpiScalePermille = 1000
        Else
            recording.ScreenWidth = recordedWidth
            recording.ScreenHeight = recordedHeight
            If Not (recording.ScreenWidth = 0 AndAlso recording.ScreenHeight = 0) AndAlso
               (recording.ScreenWidth < 1 OrElse recording.ScreenWidth > MaximumScreenDimension OrElse
                recording.ScreenHeight < 1 OrElse recording.ScreenHeight > MaximumScreenDimension) Then
                Throw New InvalidDataException("录制文件的分辨率无效。")
            End If
            If recording.DpiScalePermille < 250 OrElse recording.DpiScalePermille > 8000 Then recording.DpiScalePermille = 1000
        End If
        Return recording
    End Function

    '文件头长度（不含压缩事件流与校验和）：版本 2 比版本 1 多一个 Int32 的 DPI 缩放字段
    Private Function HeaderSizeFor(version As UShort) As Integer
        Return Magic.Length + 2 + 4 + 4 + 4 + If(version = FormatVersion, 4, 0) + 8 + 4 + 8 + 4 + 32
    End Function

    '读取 VarInt 并转成 Long；超出 Int64 范围视为损坏文件
    Private Function ReadVarIntLong(reader As BinaryReader) As Long
        Dim value As ULong = ReadVarInt(reader)
        If value > CULng(Long.MaxValue) Then Throw New InvalidDataException("录制文件数据无效。")
        Return CLng(value)
    End Function

    '读取一个坐标增量并累加到坐标上；越界时按损坏文件处理，避免 Integer 溢出
    Private Function ReadCoordinate(reader As BinaryReader, ByRef coordinate As Long) As Integer
        coordinate += UnZigZag(ReadVarInt(reader))
        If coordinate < -MaximumCoordinate OrElse coordinate > MaximumCoordinate Then
            Throw New InvalidDataException("录制文件包含无效的鼠标坐标。")
        End If
        Return CInt(coordinate)
    End Function

    Private Sub ValidateRecording(recording As KbmrRecording)
        If recording Is Nothing OrElse recording.Events Is Nothing OrElse recording.Events.Count < 1 Then Throw New InvalidDataException("录制内容为空。")
        If recording.Events.Count > MaximumEventCount Then Throw New InvalidDataException("录制事件数量超过上限。")
        If recording.SampleIntervalMs < 1 OrElse recording.SampleIntervalMs > 1000 Then Throw New InvalidDataException("鼠标采样频率超出允许范围。")
        If recording.DurationMs < 0 OrElse recording.DurationMs > MaximumDurationMs Then Throw New InvalidDataException("录制时长超出允许范围。")
        If Not (recording.ScreenWidth = 0 AndAlso recording.ScreenHeight = 0) AndAlso
           (recording.ScreenWidth < 1 OrElse recording.ScreenWidth > MaximumScreenDimension OrElse
            recording.ScreenHeight < 1 OrElse recording.ScreenHeight > MaximumScreenDimension) Then
            Throw New InvalidDataException("录制分辨率无效。")
        End If
        If recording.DpiScalePermille < 250 OrElse recording.DpiScalePermille > 8000 Then
            '缺少或异常的系统缩放值不影响回放，写文件时回退为 100%
            recording.DpiScalePermille = 1000
        End If
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
        'ZigZag 编码的源值一定是 Integer，超出范围说明文件损坏
        If value > UInteger.MaxValue Then Throw New InvalidDataException("录制文件数据无效。")
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
