Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Security.Cryptography
Imports System.Text

Public Class RapidFirePreset
    Public Property IntervalMilliseconds As Integer
    Public Property SendLoop As Boolean
    Public Property Items As List(Of String)
End Class

Public Module RapidFirePresetCodec
    Private ReadOnly Magic As Byte() = Encoding.ASCII.GetBytes("LCSLST2")
    Private Const FormatVersion As UShort = 1
    Public Const MaximumFileSize As Integer = 16 * 1024 * 1024
    Public Const MaximumItemCount As Integer = 10000

    Public Function Serialize(preset As RapidFirePreset) As Byte()
        ValidatePreset(preset)

        Dim payload As Byte()
        Using payloadStream As New MemoryStream()
            Using writer As New BinaryWriter(payloadStream, New UTF8Encoding(False, True), True)
                writer.Write(preset.IntervalMilliseconds)
                writer.Write(preset.SendLoop)
                writer.Write(preset.Items.Count)
                For Each item As String In preset.Items
                    Dim itemBytes As Byte() = New UTF8Encoding(False, True).GetBytes(item)
                    writer.Write(itemBytes.Length)
                    writer.Write(itemBytes)
                Next
            End Using
            If payloadStream.Length > MaximumFileSize Then Throw New InvalidDataException("预设内容超过 16 MiB。")
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
                writer.Write(compressed.Length)
                writer.Write(checksum)
                writer.Write(compressed)
            End Using
            If output.Length > MaximumFileSize Then Throw New InvalidDataException("预设文件超过 16 MiB。")
            Return output.ToArray()
        End Using
    End Function

    Public Function Deserialize(fileBytes As Byte()) As RapidFirePreset
        If fileBytes Is Nothing OrElse fileBytes.Length < Magic.Length + 2 + 4 + 32 OrElse fileBytes.Length > MaximumFileSize Then
            Throw New InvalidDataException("文件大小无效。")
        End If

        Dim compressed As Byte()
        Using input As New MemoryStream(fileBytes, False)
            Using reader As New BinaryReader(input, Encoding.UTF8, True)
                Dim actualMagic As Byte() = reader.ReadBytes(Magic.Length)
                If Not BytesEqual(actualMagic, Magic) Then Throw New InvalidDataException("这不是 LCSLST2 列表文件。")
                Dim version As UShort = reader.ReadUInt16()
                If version <> FormatVersion Then Throw New InvalidDataException("不支持该 LCSLST2 格式版本。")
                Dim compressedLength As Integer = reader.ReadInt32()
                If compressedLength < 0 OrElse compressedLength <> input.Length - input.Position - 32 Then Throw New InvalidDataException("文件长度无效。")
                Dim expectedChecksum As Byte() = reader.ReadBytes(32)
                compressed = reader.ReadBytes(compressedLength)
                Using sha256 As SHA256 = SHA256.Create()
                    If Not BytesEqual(expectedChecksum, sha256.ComputeHash(compressed)) Then Throw New InvalidDataException("文件校验失败，内容可能已损坏。")
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
                    If payloadStream.Length > MaximumFileSize Then Throw New InvalidDataException("解压后的预设内容过大。")
                Loop
            End Using
            payload = payloadStream.ToArray()
        End Using

        Dim preset As New RapidFirePreset With {.Items = New List(Of String)()}
        Using payloadStream As New MemoryStream(payload, False)
            Using reader As New BinaryReader(payloadStream, New UTF8Encoding(False, True), True)
                preset.IntervalMilliseconds = reader.ReadInt32()
                preset.SendLoop = reader.ReadBoolean()
                Dim count As Integer = reader.ReadInt32()
                If count < 1 OrElse count > MaximumItemCount Then Throw New InvalidDataException("条目数量无效。")
                For index As Integer = 0 To count - 1
                    Dim byteLength As Integer = reader.ReadInt32()
                    If byteLength < 0 OrElse byteLength > payloadStream.Length - payloadStream.Position Then Throw New InvalidDataException("条目长度无效。")
                    preset.Items.Add(New UTF8Encoding(False, True).GetString(reader.ReadBytes(byteLength)))
                Next
                If payloadStream.Position <> payloadStream.Length Then Throw New InvalidDataException("文件包含无法识别的数据。")
            End Using
        End Using
        ValidatePreset(preset)
        Return preset
    End Function

    Private Sub ValidatePreset(preset As RapidFirePreset)
        If preset Is Nothing OrElse preset.Items Is Nothing Then Throw New InvalidDataException("预设内容为空。")
        If preset.IntervalMilliseconds < 50 OrElse preset.IntervalMilliseconds > 1000000 Then Throw New InvalidDataException("发送间隔超出允许范围。")
        If preset.Items.Count < 1 OrElse preset.Items.Count > MaximumItemCount Then Throw New InvalidDataException("条目数量无效。")
        If preset.Items.Any(Function(item) item Is Nothing) Then Throw New InvalidDataException("条目列表包含无效内容。")
        If Not preset.Items.Any(Function(item) Not String.IsNullOrWhiteSpace(item)) Then Throw New InvalidDataException("预设中没有可发送的内容。")
    End Sub

    Private Function BytesEqual(left As Byte(), right As Byte()) As Boolean
        If left Is Nothing OrElse right Is Nothing OrElse left.Length <> right.Length Then Return False
        Dim difference As Integer = 0
        For index As Integer = 0 To left.Length - 1
            difference = difference Or (left(index) Xor right(index))
        Next
        Return difference = 0
    End Function
End Module
