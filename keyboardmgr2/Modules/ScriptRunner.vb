'PowerShell 脚本运行器：在独立 STA 线程中运行脚本，支持停止、输出重定向与结束回调。
Imports System.Management.Automation
Imports System.Management.Automation.Runspaces
Imports System.Threading

Module ScriptRunner
    Public Event OutputReceived(text As String)
    Public Event StateChanged(running As Boolean)

    Private powerShell As PowerShell
    Private runnerThread As Thread
    Private running As Boolean
    Private isStopRequested As Boolean

    Public ReadOnly Property IsRunning As Boolean
        Get
            Return running
        End Get
    End Property

    Public ReadOnly Property StopRequested As Boolean
        Get
            Return isStopRequested
        End Get
    End Property

    Public Sub RunScript(code As String)
        If running Then Return
        running = True
        isStopRequested = False
        RaiseEvent StateChanged(True)
        runnerThread = New Thread(AddressOf RunThread) With {.IsBackground = True, .Name = "KbmrScriptRunner"}
        runnerThread.SetApartmentState(ApartmentState.STA)
        runnerThread.Start(code)
    End Sub

    Public Sub StopScript()
        If Not running Then Return
        isStopRequested = True
        Dim instance As PowerShell = powerShell
        If instance IsNot Nothing Then
            Try
                instance.BeginStop(Nothing, Nothing)
            Catch
            End Try
        End If
    End Sub

    Private Sub RunThread(state As Object)
        Dim code As String = DirectCast(state, String)
        Dim instance As PowerShell = Nothing
        Try
            instance = PowerShell.Create(BuildSessionState())
            powerShell = instance
            AddHandler instance.Streams.Information.DataAdded, AddressOf Information_DataAdded
            AddHandler instance.Streams.Error.DataAdded, AddressOf Error_DataAdded
            instance.AddScript(code).AddCommand("Out-String").AddParameter("Stream", True)
            Dim results As System.Collections.ObjectModel.Collection(Of PSObject) = instance.Invoke()
            For Each item As PSObject In results
                If item IsNot Nothing Then Emit(item.ToString())
            Next
        Catch ex As PipelineStoppedException
            Emit("脚本已停止。")
        Catch ex As Exception
            Emit("脚本运行出错：" & ex.Message)
        Finally
            Try
                If instance IsNot Nothing Then instance.Dispose()
            Catch
            End Try
            powerShell = Nothing
            ScriptHost.ForceUnblockInput()
            running = False
            RaiseEvent StateChanged(False)
        End Try
    End Sub

    Private Sub Information_DataAdded(sender As Object, e As DataAddedEventArgs)
        Try
            Dim collection As PSDataCollection(Of InformationRecord) = DirectCast(sender, PSDataCollection(Of InformationRecord))
            Dim record As InformationRecord = collection(e.Index)
            If record IsNot Nothing Then Emit(record.MessageData.ToString())
        Catch
        End Try
    End Sub

    Private Sub Error_DataAdded(sender As Object, e As DataAddedEventArgs)
        Try
            Dim collection As PSDataCollection(Of ErrorRecord) = DirectCast(sender, PSDataCollection(Of ErrorRecord))
            Dim record As ErrorRecord = collection(e.Index)
            If record IsNot Nothing Then Emit("错误：" & record.ToString())
        Catch
        End Try
    End Sub

    Private Sub Emit(text As String)
        RaiseEvent OutputReceived(text)
    End Sub

    Private Function BuildSessionState() As InitialSessionState
        Dim sessionState As InitialSessionState = InitialSessionState.CreateDefault2()
        sessionState.Variables.Add(New SessionStateVariableEntry("KM", New ScriptHost(), "键鼠管家脚本宿主"))
        Dim functions As New List(Of SessionStateFunctionEntry) From {
            New SessionStateFunctionEntry("Delay", "param([int]$ms) $KM.Delay($ms)"),
            New SessionStateFunctionEntry("Sleep", "param([int]$ms) $KM.Delay($ms)"),
            New SessionStateFunctionEntry("Move", "param([int]$x,[int]$y) $KM.Move($x,$y)"),
            New SessionStateFunctionEntry("Click", "param([int]$x,[int]$y,[string]$button='Left') $KM.Click($x,$y,$button)"),
            New SessionStateFunctionEntry("DoubleClick", "param([int]$x,[int]$y,[string]$button='Left') $KM.DoubleClick($x,$y,$button)"),
            New SessionStateFunctionEntry("MouseDown", "param([string]$button='Left') $KM.MouseDown($button)"),
            New SessionStateFunctionEntry("MouseUp", "param([string]$button='Left') $KM.MouseUp($button)"),
            New SessionStateFunctionEntry("Wheel", "param([int]$delta) $KM.Wheel($delta)"),
            New SessionStateFunctionEntry("Drag", "param([int]$x1,[int]$y1,[int]$x2,[int]$y2,[string]$button='Left',[int]$durationMs=300) $KM.Drag($x1,$y1,$x2,$y2,$button,$durationMs)"),
            New SessionStateFunctionEntry("KeyDown", "param([string]$key) $KM.KeyDown($key)"),
            New SessionStateFunctionEntry("KeyUp", "param([string]$key) $KM.KeyUp($key)"),
            New SessionStateFunctionEntry("KeyTap", "param([string]$key) $KM.KeyTap($key)"),
            New SessionStateFunctionEntry("Combo", "param([string]$combo) $KM.Combo($combo)"),
            New SessionStateFunctionEntry("TypeText", "param([string]$text) $KM.TypeText($text)"),
            New SessionStateFunctionEntry("BlockInput", "param([bool]$block) $KM.BlockInput($block)"),
            New SessionStateFunctionEntry("Get-CursorPos", "$KM.GetCursorPos()"),
            New SessionStateFunctionEntry("Get-PixelColor", "param([int]$x,[int]$y) $KM.GetPixelColor($x,$y)"),
            New SessionStateFunctionEntry("Get-CursorColor", "$KM.GetCursorColor()"),
            New SessionStateFunctionEntry("Save-ScreenRegion", "param([int]$x,[int]$y,[int]$w,[int]$h,[string]$path) $KM.SaveScreenRegion($x,$y,$w,$h,$path)"),
            New SessionStateFunctionEntry("Get-Windows", "param([string]$Title='*',[string]$Class='*') $KM.GetWindows($Title,$Class,$true)"),
            New SessionStateFunctionEntry("Set-ForegroundWindow", "param([long]$hwnd) $KM.SetForegroundWindow($hwnd)"),
            New SessionStateFunctionEntry("Get-ForegroundWindow", "$KM.GetForegroundWindow()"),
            New SessionStateFunctionEntry("Get-WindowRect", "param([long]$hwnd) $KM.GetWindowRect($hwnd)"),
            New SessionStateFunctionEntry("Close-Window", "param([long]$hwnd) $KM.CloseWindow($hwnd)"),
            New SessionStateFunctionEntry("Move-Window", "param([long]$hwnd,[int]$x,[int]$y,[int]$w,[int]$h) $KM.MoveWindow($hwnd,$x,$y,$w,$h)"),
            New SessionStateFunctionEntry("Set-ClipboardText", "param([string]$text) $KM.SetClipboardText($text)"),
            New SessionStateFunctionEntry("Get-ClipboardText", "$KM.GetClipboardText()"),
            New SessionStateFunctionEntry("Start-App", "param([string]$path,[string]$arguments='') $KM.StartApp($path,$arguments)")
        }
        For Each entry As SessionStateFunctionEntry In functions
            sessionState.Commands.Add(entry)
        Next
        Return sessionState
    End Function
End Module
