'录制历史自动保存：
'把每条录制以 .kbmr 文件存放在 %LOCALAPPDATA%\keyboardmgr2\recordings 下，
'文件名即录制名称，从而在重启后仍保留历史录制，并支持重命名与删除。
Imports System.IO
Imports System.Linq
Imports System.Collections.Concurrent

Public Module RecordingStore
    Private ReadOnly pendingSaves As New ConcurrentDictionary(Of String, Byte())(StringComparer.OrdinalIgnoreCase)

    '历史录制存放目录，不存在时自动创建。
    Public ReadOnly Property RootFolder As String
        Get
            Dim basePath As String = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "keyboardmgr2", "recordings")
            Directory.CreateDirectory(basePath)
            Return basePath
        End Get
    End Property

    '仅读取每个文件的明文头，不解压事件流。无法读取/损坏的头部会被跳过。
    Public Function LoadAll() As List(Of KbmrRecording)
        Dim list As New List(Of KbmrRecording)
        For Each filePath As String In Directory.EnumerateFiles(RootFolder, "*.kbmr")
            Try
                Dim item As KbmrRecording = KbmrCodec.ReadMetadata(filePath)
                list.Add(item)
            Catch
                '忽略损坏文件，避免单个文件影响整个历史列表。
            End Try
        Next
        Return list.OrderBy(Function(item) item.CreatedUtc).ToList()
    End Function

    '按需解码所选录制；返回已填充事件的实例，保持同一对象引用。
    Public Function LoadEvents(recording As KbmrRecording) As Boolean
        If recording Is Nothing Then Return False
        If recording.EventsLoaded Then Return True
        SyncLock recording
            If recording.EventsLoaded Then Return True
            If HasPendingSave(recording) AndAlso (GetPendingBytes(recording) Is Nothing OrElse GetPendingBytes(recording).Length <= 1) Then Return False
            Return LoadEventsCore(recording)
        End SyncLock
    End Function

    Private Function LoadEventsCore(recording As KbmrRecording) As Boolean
        Dim pending As Byte() = GetPendingBytes(recording)
        If pending IsNot Nothing AndAlso pending.Length > 1 Then
            Try
                recording.Events = KbmrCodec.Deserialize(pending).Events
                Return True
            Catch
                Return False
            End Try
        End If
        If String.IsNullOrEmpty(recording.FilePath) OrElse Not File.Exists(recording.FilePath) Then Return False
        Try
            Dim decoded As KbmrRecording = KbmrCodec.Deserialize(File.ReadAllBytes(recording.FilePath))
            recording.Events = decoded.Events
            recording.DurationMs = decoded.DurationMs
            recording.SampleIntervalMs = decoded.SampleIntervalMs
            recording.ScreenWidth = decoded.ScreenWidth
            recording.ScreenHeight = decoded.ScreenHeight
            recording.DpiScalePermille = decoded.DpiScalePermille
            recording.CreatedUtc = decoded.CreatedUtc
            Return True
        Catch
            Return False
        End Try
    End Function

    ''' <summary>
    ''' 在资源管理器中定位录制文件；录制没有对应文件（例如自动保存失败）时改为打开录制目录。
    ''' 成功启动资源管理器返回 True。
    ''' </summary>
    Public Function RevealInExplorer(recording As KbmrRecording) As Boolean
        Try
            If recording IsNot Nothing AndAlso Not String.IsNullOrEmpty(recording.FilePath) AndAlso File.Exists(recording.FilePath) Then
                '资源管理器的 /select, 参数会打开父目录并选中该文件
                Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo(
                    "explorer.exe",
                    "/select," & ControlChars.Quote & recording.FilePath & ControlChars.Quote) With {.UseShellExecute = True})
                Return True
            End If
            Dim folder As String = RootFolder
            Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo(
                "explorer.exe",
                ControlChars.Quote & folder & ControlChars.Quote) With {.UseShellExecute = True})
            Return True
        Catch
            Return False
        End Try
    End Function

    '把录制保存到历史目录。新录制自动生成不冲突的文件名，
    '并把最终使用的名称写回录制对象。
    Public Function Save(recording As KbmrRecording) As Boolean
        If recording Is Nothing Then Return False
        Try
            Dim pendingBytes As Byte() = GetPendingBytes(recording)
            If HasPendingSave(recording) AndAlso (pendingBytes Is Nothing OrElse pendingBytes.Length <= 1) Then Return False
            If Not recording.EventsLoaded Then
                If Not LoadEvents(recording) Then Return False
            End If
            Dim targetPath As String = recording.FilePath
            If String.IsNullOrEmpty(targetPath) Then
                targetPath = BuildUniquePath(recording.Name, Nothing)
            End If
            Dim bytes As Byte() = If(pendingBytes IsNot Nothing AndAlso pendingBytes.Length > 1, pendingBytes, KbmrCodec.Serialize(recording))
            Dim temporaryPath As String = targetPath & ".tmp"
            File.WriteAllBytes(temporaryPath, bytes)
            If File.Exists(targetPath) Then
                File.Replace(temporaryPath, targetPath, Nothing)
            Else
                File.Move(temporaryPath, targetPath)
            End If
            recording.FilePath = targetPath
            recording.Name = Path.GetFileNameWithoutExtension(targetPath)
            pendingSaves.TryRemove(targetPath, Nothing)
            Return True
        Catch
            Return False
        End Try
    End Function

    '把录制事件快照并排队后台写入。返回时序列化字节已在内存，真正的压缩/IO 由 worker 完成。
    Public Function QueueSave(recording As KbmrRecording, completion As Action(Of Boolean)) As Boolean
        If recording Is Nothing OrElse recording.Events Is Nothing OrElse recording.Events.Count = 0 Then Return False
        Try
            Dim targetPath As String = recording.FilePath
            If String.IsNullOrEmpty(targetPath) Then targetPath = BuildUniquePath(recording.Name, Nothing)
            'StopRecording 已结束写入事件，录制不可变；worker 直接序列化，避免为快照在 UI 线程复制数百万事件。
            '预留路径；序列化/压缩也必须在 worker 上执行，不能仅把文件写入移出 UI。
            pendingSaves(targetPath) = New Byte() {0}
            recording.FilePath = targetPath
            recording.Name = Path.GetFileNameWithoutExtension(targetPath)
            Threading.Tasks.Task.Run(
                Sub()
                    Dim success As Boolean = False
                    Try
                        Dim snapshot As Byte() = Nothing
                        SyncLock recording
                            snapshot = KbmrCodec.Serialize(recording)
                        End SyncLock
                        pendingSaves(targetPath) = snapshot
                        Dim temporaryPath As String = targetPath & ".tmp"
                        File.WriteAllBytes(temporaryPath, snapshot)
                        If File.Exists(targetPath) Then
                            File.Replace(temporaryPath, targetPath, Nothing)
                        Else
                            File.Move(temporaryPath, targetPath)
                        End If
                        recording.FilePath = targetPath
                        recording.Name = Path.GetFileNameWithoutExtension(targetPath)
                        success = True
                    Catch
                        Try
                            If File.Exists(targetPath & ".tmp") Then File.Delete(targetPath & ".tmp")
                        Catch
                        End Try
                    Finally
                        pendingSaves.TryRemove(targetPath, Nothing)
                        Try
                            completion?.Invoke(success)
                        Catch
                        End Try
                    End Try
                End Sub)
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Function HasPendingSave(recording As KbmrRecording) As Boolean
        Return recording IsNot Nothing AndAlso Not String.IsNullOrEmpty(recording.FilePath) AndAlso pendingSaves.ContainsKey(recording.FilePath)
    End Function

    Public Function GetPendingBytes(recording As KbmrRecording) As Byte()
        If recording Is Nothing OrElse String.IsNullOrEmpty(recording.FilePath) Then Return Nothing
        Dim bytes As Byte() = Nothing
        If pendingSaves.TryGetValue(recording.FilePath, bytes) Then Return bytes
        Return Nothing
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
            If pendingSaves.ContainsKey(recording.FilePath) Then Return
            If File.Exists(recording.FilePath) Then File.Delete(recording.FilePath)
            If File.Exists(recording.FilePath & ".tmp") Then File.Delete(recording.FilePath & ".tmp")
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
