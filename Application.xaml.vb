Imports System.IO
Imports System.IO.Pipes
Imports System.Runtime.InteropServices
Imports System.Security.Principal
Imports System.Threading
Imports Microsoft.Win32

Class Application
    Private Const ActivateCommand As String = "__ACTIVATE__"
    Private instanceMutex As Mutex
    Private isPrimaryInstance As Boolean
    Private pipeName As String

    <DllImport("shell32.dll")>
    Private Shared Sub SHChangeNotify(eventId As Integer, flags As UInteger, item1 As IntPtr, item2 As IntPtr)
    End Sub

    Private Const SHCNE_ASSOCCHANGED As Integer = &H8000000
    Private Const SHCNF_IDLIST As UInteger = 0

    Private Sub Application_Startup(sender As Object, e As StartupEventArgs)
        Dim userId As String = WindowsIdentity.GetCurrent().User.Value
        pipeName = "keyboardmgr2.singleinstance." & userId
        Dim createdNew As Boolean
        instanceMutex = New Mutex(True, "Local\keyboardmgr2.singleinstance." & userId, createdNew)
        isPrimaryInstance = createdNew

        Dim requestedFile As String = GetRequestedListFile()
        If Not isPrimaryInstance Then
            ForwardRequest(If(requestedFile, ActivateCommand))
            Shutdown()
            Return
        End If

        RegisterListFileAssociations()
        Dim window As New MainWindow1()
        MainWindow = window
        window.Show()
        StartPipeServer()
        If requestedFile IsNot Nothing Then window.HandleExternalRequest(requestedFile)
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
        If arguments.Length < 2 Then Return Nothing
        Dim extension As String = Path.GetExtension(arguments(1))
        If Not String.Equals(extension, ".lcslst", StringComparison.OrdinalIgnoreCase) AndAlso
           Not String.Equals(extension, ".lcslst2", StringComparison.OrdinalIgnoreCase) Then Return Nothing
        Return Path.GetFullPath(arguments(1))
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
