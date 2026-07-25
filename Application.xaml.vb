Imports System.IO
Imports System.IO.Pipes
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Threading
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
        WaitForPreviousVersionIfRequested()
        Dim userId As String = WindowsIdentity.GetCurrent().User.Value
        pipeName = "keyboardmgr2.singleinstance." & userId
        Dim createdNew As Boolean
        instanceMutex = New Mutex(True, "Local\keyboardmgr2.singleinstance." & userId, createdNew)
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
        If isAutoStart AndAlso requestedFile Is Nothing Then
            window.ShowActivated = False
            window.Opacity = 0
        End If
        window.Show()
        StartPipeServer()
        If requestedFile IsNot Nothing Then
            window.HandleExternalRequest(requestedFile)
        ElseIf isAutoStart Then
            window.Hide()
            window.Opacity = 1
            window.ShowActivated = True
        End If
    End Sub

    Private Sub WaitForPreviousVersionIfRequested()
        Const prefix As String = "--wait-for-pid="
        Dim argument As String = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(Function(value) value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        If argument Is Nothing Then Return
        Dim processId As Integer
        If Not Integer.TryParse(argument.Substring(prefix.Length), processId) OrElse processId <= 0 Then Return
        Try
            Diagnostics.Process.GetProcessById(processId).WaitForExit(30000)
        Catch ex As ArgumentException
            '旧进程已经退出。
        End Try
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
