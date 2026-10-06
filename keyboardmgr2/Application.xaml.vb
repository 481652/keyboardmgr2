Imports System.IO
Imports System.IO.Pipes
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Threading
Imports System.Windows.Threading
Imports Microsoft.Win32

Class Application
    Private Const ActivateCommand As String = "__ACTIVATE__"
    Private Const AutoStartArgument As String = "--autostart"
    Private instanceMutex As Mutex
    Private isPrimaryInstance As Boolean
    Private pipeName As String

    <DllImport("shell32.dll")>
    Private Shared Sub SHChangeNotify(eventId As Integer, flags As UInteger, item1 As IntPtr, item2 As IntPtr)
    End Sub

    Private Const SHCNE_ASSOCCHANGED As Integer = &H8000000
    Private Const SHCNF_IDLIST As UInteger = 0

    Private Sub Application_Startup(sender As Object, e As StartupEventArgs)
        '注册全局异常处理，避免未处理异常导致进程直接崩溃
        AddHandler AppDomain.CurrentDomain.UnhandledException, AddressOf OnUnhandledException
        Try
            UpdateModule.WaitForRequestedProcess()
            If UpdateModule.ApplyUpdateIfRequested() Then
                Shutdown()
                Return
            End If
            UpdateModule.CleanupUpdateIfRequested()
        Catch ex As Exception
            MessageBox.Show("自动更新失败：" & ex.Message, "更新失败", MessageBoxButton.OK, MessageBoxImage.Error)
            Shutdown()
            Return
        End Try
        Dim userId As String = WindowsIdentity.GetCurrent().User.Value
        pipeName = "keyboardmgr2.singleinstance." & userId
        Dim mutexName As String = "Local\keyboardmgr2.singleinstance." & userId
        '提权重启的新实例带着 --elevated 标记：旧实例正在退出，它应当接管而不是被当成“第二实例”
        Dim isElevatedRelaunch As Boolean = HasCommandLineArgument(LaunchOnSetupModule.ElevatedArgument)
        LaunchOnSetupModule.PendingTaskAction = GetTaskActionArgument()

        Dim createdNew As Boolean
        instanceMutex = New Mutex(True, mutexName, createdNew)

        If Not createdNew AndAlso isElevatedRelaunch Then
            '等待旧实例让出单实例权（最多 15 秒），否则新实例会立刻退出，看起来像“启动失败”
            Dim deadline As DateTime = DateTime.UtcNow.AddSeconds(15)
            While Not createdNew AndAlso DateTime.UtcNow < deadline
                Thread.Sleep(150)
                instanceMutex.Dispose()
                instanceMutex = New Mutex(True, mutexName, createdNew)
            End While
        End If

        isPrimaryInstance = createdNew

        Dim requestedFile As String = GetRequestedListFile()
        Dim isAutoStart As Boolean = HasCommandLineArgument(AutoStartArgument)
        If Not isPrimaryInstance Then
            '登录时若程序已经运行，不应把原本隐藏的主窗口弹出来。
            If Not isAutoStart Then ForwardRequest(If(requestedFile, ActivateCommand))
            Shutdown()
            Return
        End If

        RegisterListFileAssociations()
        Dim useSystemTheme As Boolean = ReadSetting("DoAutoSwitchTheme", 1) = 1
        SwitchTheme(If(useSystemTheme, IsDarkModeEnabled(), ReadSetting("IsDarkMode", 0) = 1))
        Dim window As New MainWindow1()
        MainWindow = window
        '开机自启要“静默进托盘”：Show() 是为了创建 HWND、消息钩子和托盘图标，
        '但它期间会触发 Window_Loaded（读设置、注册热键等），窗口只要可见就会白屏闪一下
        '（云母背景由 DWM 绘制，WPF 的 Opacity=0 挡不住它）。这里先把窗口挪到屏幕外，隐藏后再挪回来。
        Dim hideAfterShow As Boolean = isAutoStart AndAlso requestedFile Is Nothing
        Dim savedStartupLocation As WindowStartupLocation = window.WindowStartupLocation
        Dim savedShowInTaskbar As Boolean = window.ShowInTaskbar
        Dim savedShowActivated As Boolean = window.ShowActivated
        If hideAfterShow Then
            window.WindowStartupLocation = WindowStartupLocation.Manual
            window.Left = -32000
            window.Top = -32000
            window.ShowInTaskbar = False
            window.ShowActivated = False
            window.Opacity = 0
        End If
        window.Show()
        StartPipeServer()
        If requestedFile IsNot Nothing Then
            window.HandleExternalRequest(requestedFile)
        ElseIf hideAfterShow Then
            window.Hide()
            window.Opacity = 1
            window.ShowActivated = savedShowActivated
            window.ShowInTaskbar = savedShowInTaskbar
            window.WindowStartupLocation = savedStartupLocation
            If savedStartupLocation = WindowStartupLocation.Manual Then
                '记忆了窗口位置：重新套用保存的位置，而不是让它停在屏幕外
                window.RestoreWindowPlacement()
            Else
                '本来就是 CenterScreen：显式算好居中位置，免得下次从托盘显示时窗口还在屏幕外
                window.Left = SystemParameters.WorkArea.Left + Math.Max(0, (SystemParameters.WorkArea.Width - window.ActualWidth) / 2)
                window.Top = SystemParameters.WorkArea.Top + Math.Max(0, (SystemParameters.WorkArea.Height - window.ActualHeight) / 2)
            End If
        End If

        '主界面就绪后再执行提权重启时请求的任务操作（注册/删除开机自启任务）
        RunPendingTaskAction()
    End Sub

    Private Sub Application_Exit(sender As Object, e As ExitEventArgs)
        If isPrimaryInstance AndAlso instanceMutex IsNot Nothing Then
            Try
                instanceMutex.ReleaseMutex()
            Catch
            End Try
        End If
        instanceMutex?.Dispose()
    End Sub

    ''' <summary>
    ''' 提权重启前调用：释放并销毁单实例互斥体，让带 --elevated 的新实例能够成为主实例。
    ''' </summary>
    Public Shared Sub ReleaseSingleInstanceForRestart()
        Dim app As Application = TryCast(Current, Application)
        If app IsNot Nothing Then
            app.ReleaseSingleInstance()
        End If
    End Sub

    Private Sub ReleaseSingleInstance()
        '置为非主实例会让命名管道服务循环一并结束
        isPrimaryInstance = False
        If instanceMutex IsNot Nothing Then
            Try
                instanceMutex.ReleaseMutex()
            Catch
            End Try
            Try
                instanceMutex.Dispose()
            Catch
            End Try
            instanceMutex = Nothing
        End If
    End Sub

    '解析命令行里的 --task=register / --task=delete
    Private Function GetTaskActionArgument() As String
        Dim prefix As String = LaunchOnSetupModule.TaskActionArgumentPrefix
        For Each argument As String In Environment.GetCommandLineArgs().Skip(1)
            If argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) Then
                Return argument.Substring(prefix.Length).Trim().ToLowerInvariant()
            End If
        Next
        Return Nothing
    End Function

    '执行提权重启时请求的任务操作：schtasks 需要管理员权限，必须在提权后的实例里做
    Private Sub RunPendingTaskAction()
        Select Case LaunchOnSetupModule.PendingTaskAction
            Case "register"
                LaunchOnSetupModule.RegisterTask()
            Case "delete"
                LaunchOnSetupModule.EraseTask()
        End Select
    End Sub

    'UI 线程未处理异常：记录日志并弹窗提示，避免进程直接崩溃
    Private Sub Application_DispatcherUnhandledException(sender As Object, e As DispatcherUnhandledExceptionEventArgs) Handles Me.DispatcherUnhandledException
        Dim ex As Exception = e.Exception
        LogException(ex)
        Try
            ShowExpdlg("程序遇到未处理的异常：" & ex.Message, ex.ToString())
        Catch
            Try
                MessageBox.Show("程序遇到未处理的异常：" & ex.Message, "键鼠管家", MessageBoxButton.OK, MessageBoxImage.Error)
            Catch
            End Try
        End Try
        e.Handled = True
    End Sub

    '非 UI 线程未处理异常：记录日志，尽力避免进程崩溃
    Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        Dim ex As Exception = TryCast(e.ExceptionObject, Exception)
        If ex Is Nothing Then
            ex = New Exception("未知异常：" & If(e.ExceptionObject Is Nothing, "（无异常对象）", e.ExceptionObject.ToString()))
        End If
        LogException(ex)
        Try
            MessageBox.Show("程序遇到未处理的异常：" & ex.Message & vbCrLf & "详细信息已记录到 error.log。", "键鼠管家", MessageBoxButton.OK, MessageBoxImage.Error)
        Catch
        End Try
    End Sub

    '把异常详情追加写入 %LOCALAPPDATA%\keyboardmgr2\error.log
    Private Shared Sub LogException(ex As Exception)
        Try
            Dim dir As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "keyboardmgr2")
            Directory.CreateDirectory(dir)
            Dim logPath As String = Path.Combine(dir, "error.log")
            Using sw As New StreamWriter(logPath, True, System.Text.Encoding.UTF8)
                sw.WriteLine("[{0}] {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), ex.ToString())
                sw.WriteLine()
            End Using
        Catch
        End Try
    End Sub

    Private Function GetRequestedListFile() As String
        Dim arguments As String() = Environment.GetCommandLineArgs()
        For Each argument As String In arguments.Skip(1)
            Dim extension As String = Path.GetExtension(argument)
            If String.Equals(extension, ".lcslst", StringComparison.OrdinalIgnoreCase) OrElse
               String.Equals(extension, ".lcslst2", StringComparison.OrdinalIgnoreCase) Then Return Path.GetFullPath(argument)
        Next
        Return Nothing
    End Function

    Private Function HasCommandLineArgument(expectedArgument As String) As Boolean
        Return Environment.GetCommandLineArgs().Skip(1).Any(Function(argument) String.Equals(argument, expectedArgument, StringComparison.OrdinalIgnoreCase))
    End Function

    Private Sub ForwardRequest(request As String)
        Try
            Using client As New NamedPipeClientStream(".", pipeName, PipeDirection.Out)
                client.Connect(3000)
                Using writer As New BinaryWriter(client, Text.Encoding.UTF8, True)
                    writer.Write(request)
                    writer.Flush()
                End Using
            End Using
        Catch
            '首个实例可能仍处于初始化阶段；防多开仍然优先于创建第二套窗口。
        End Try
    End Sub

    Private Sub StartPipeServer()
        Threading.Tasks.Task.Run(AddressOf PipeServerLoop)
    End Sub

    Private Sub PipeServerLoop()
        While isPrimaryInstance
            Try
                Using server As New NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None)
                    server.WaitForConnection()
                    Dim request As String
                    Using reader As New BinaryReader(server, Text.Encoding.UTF8, True)
                        request = reader.ReadString()
                    End Using
                    Dispatcher.BeginInvoke(New Action(
                        Sub()
                            Dim main As MainWindow1 = TryCast(MainWindow, MainWindow1)
                            If main Is Nothing Then Return
                            main.HandleExternalRequest(If(request = ActivateCommand, Nothing, request))
                        End Sub))
                End Using
            Catch
                If isPrimaryInstance Then Thread.Sleep(100)
            End Try
        End While
    End Sub

    Private Sub RegisterListFileAssociations()
        Try
            Dim executablePath As String = Diagnostics.Process.GetCurrentProcess().MainModule.FileName
            RegisterFileType(".lcslst", "keyboardmgr2.lcslst", "LCS经典列表连发文件", executablePath)
            RegisterFileType(".lcslst2", "keyboardmgr2.lcslst2", "LCS新版列表连发文件", executablePath)
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero)
        Catch
            '文件关联失败不应阻止主程序启动。
        End Try
    End Sub

    Private Sub RegisterFileType(extension As String, progId As String, description As String, executablePath As String)
        Using extensionKey As RegistryKey = Registry.CurrentUser.CreateSubKey("Software\Classes\" & extension)
            extensionKey.SetValue("", progId)
        End Using
        Using progIdKey As RegistryKey = Registry.CurrentUser.CreateSubKey("Software\Classes\" & progId)
            progIdKey.SetValue("", description)
        End Using
        Using iconKey As RegistryKey = Registry.CurrentUser.CreateSubKey("Software\Classes\" & progId & "\DefaultIcon")
            iconKey.SetValue("", """" & executablePath & """,0")
        End Using
        Using commandKey As RegistryKey = Registry.CurrentUser.CreateSubKey("Software\Classes\" & progId & "\shell\open\command")
            commandKey.SetValue("", """" & executablePath & """ ""%1""")
        End Using
    End Sub
End Class
