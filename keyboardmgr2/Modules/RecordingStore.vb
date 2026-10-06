'录制历史自动保存：
'把每条录制以 .kbmr 文件存放在 %LOCALAPPDATA%\keyboardmgr2\recordings 下，
'文件名即录制名称，从而在重启后仍保留历史录制，并支持重命名与删除。
Imports System.IO
Imports System.Linq

Public Module RecordingStore

    '历史录制存放目录，不存在时自动创建。
    Private ReadOnly Property RootFolder As String
        Get
            Dim basePath As String = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "keyboardmgr2", "recordings")
            Directory.CreateDirectory(basePath)
            Return basePath
        End Get
    End Property

    '读取全部历史录制，按录制时间升序排列。无法读取的文件会被跳过。
    Public Function LoadAll() As List(Of KbmrRecording)
        Dim list As New List(Of KbmrRecording)
        For Each filePath As String In Directory.EnumerateFiles(RootFolder, "*.kbmr")
            Try
                Dim item As KbmrRecording = KbmrCodec.Deserialize(File.ReadAllBytes(filePath))
                item.Name = Path.GetFileNameWithoutExtension(filePath)
                item.FilePath = filePath
                list.Add(item)
            Catch
                '忽略损坏文件，避免单个文件影响整个历史列表。
            End Try
        Next
        Return list.OrderBy(Function(item) item.CreatedUtc).ToList()
    End Function

    '把录制保存到历史目录。新录制自动生成不冲突的文件名，
    '并把最终使用的名称写回录制对象。
    Public Function Save(recording As KbmrRecording) As Boolean
        If recording Is Nothing Then Return False
        Try
            Dim targetPath As String = recording.FilePath
            If String.IsNullOrEmpty(targetPath) Then
                targetPath = BuildUniquePath(recording.Name, Nothing)
            End If
            File.WriteAllBytes(targetPath, KbmrCodec.Serialize(recording))
            recording.FilePath = targetPath
            recording.Name = Path.GetFileNameWithoutExtension(targetPath)
            Return True
        Catch
            Return False
        End Try
    End Function

    '重命名录制（同时重命名对应的历史文件）。
    Public Function Rename(recording As KbmrRecording, newName As String) As Boolean
        If recording Is Nothing Then Return False
        Dim safeName As String = SanitizeName(newName)
        If String.IsNullOrEmpty(safeName) Then Return False
        If String.Equals(safeName, recording.Name, StringComparison.Ordinal) Then Return True
        Try
            Dim targetPath As String = BuildUniquePath(safeName, recording.FilePath)
            If Not String.IsNullOrEmpty(recording.FilePath) AndAlso File.Exists(recording.FilePath) Then
                File.Move(recording.FilePath, targetPath)
            Else
                File.WriteAllBytes(targetPath, KbmrCodec.Serialize(recording))
            End If
            recording.FilePath = targetPath
            recording.Name = Path.GetFileNameWithoutExtension(targetPath)
            Return True
        Catch
            Return False
        End Try
    End Function

    '删除录制对应的历史文件。
    Public Sub Delete(recording As KbmrRecording)
        If recording Is Nothing OrElse String.IsNullOrEmpty(recording.FilePath) Then Return
        Try
            If File.Exists(recording.FilePath) Then File.Delete(recording.FilePath)
        Catch
        End Try
        recording.FilePath = Nothing
    End Sub

    '生成不与现有文件冲突的路径；excludePath 用于重命名时忽略自身。
    Private Function BuildUniquePath(baseName As String, excludePath As String) As String
        Dim safeName As String = SanitizeName(baseName)
        If String.IsNullOrEmpty(safeName) Then safeName = "录制"
        Dim candidate As String = safeName
        Dim index As Integer = 2
        Do
            Dim candidatePath As String = Path.Combine(RootFolder, candidate & ".kbmr")
            If Not File.Exists(candidatePath) OrElse String.Equals(candidatePath, excludePath, StringComparison.OrdinalIgnoreCase) Then
                Return candidatePath
            End If
            candidate = safeName & " (" & index.ToString(Globalization.CultureInfo.InvariantCulture) & ")"
            index += 1
        Loop
    End Function

    '清理文件名中的非法字符并限制长度。
    Public Function SanitizeName(name As String) As String
        If String.IsNullOrWhiteSpace(name) Then Return ""
        Dim invalid As Char() = Path.GetInvalidFileNameChars()
        Dim builder As New Text.StringBuilder()
        For Each ch As Char In name.Trim()
            builder.Append(If(Array.IndexOf(invalid, ch) >= 0, "_"c, ch))
        Next
        'Windows 文件名不允许以句点或空格结尾。
        Dim result As String = builder.ToString().Trim().TrimEnd("."c, " "c)
        If result.Length > 80 Then result = result.Substring(0, 80).TrimEnd("."c, " "c)
        Return result
    End Function

End Module
