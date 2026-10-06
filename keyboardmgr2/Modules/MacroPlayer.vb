'录制回放引擎：后台线程按时间轴重放事件，支持倍速、循环、鼠标移动插值平滑与回放时阻止用户输入。
Imports System.Threading

Module MacroPlayer
    Public Event PlaybackStopped()

    Private playbackThread As Thread
    Private stopFlag As Boolean
    Private playing As Boolean
    Private ReadOnly stateLock As New Object()

    Public ReadOnly Property IsPlaying As Boolean
        Get
            Return playing
        End Get
    End Property

    Public Sub StartPlayback(recording As KbmrRecording, speed As Double, loopPlayback As Boolean, smoothMove As Boolean, blockInput As Boolean)
        If recording Is Nothing OrElse recording.Events Is Nothing OrElse recording.Events.Count = 0 Then Return
        StopPlayback()
        If speed <= 0 Then speed = 1.0
        stopFlag = False
        playing = True
        Dim plan As List(Of KbmrEvent) = BuildPlaybackEvents(recording, smoothMove)
        playbackThread = New Thread(Sub() PlaybackLoop(plan, speed, loopPlayback, blockInput)) With {.IsBackground = True, .Name = "KbmrPlayer"}
        playbackThread.Start()
    End Sub

    Public Sub StopPlayback()
        stopFlag = True
        Dim thread As Thread = playbackThread
        If thread IsNot Nothing AndAlso thread.IsAlive AndAlso thread IsNot Thread.CurrentThread Then
            thread.Join(2000)
        End If
        playbackThread = Nothing
        FinishPlayback()
    End Sub

    Private Sub FinishPlayback()
        Dim shouldRaise As Boolean = False
        SyncLock stateLock
            If playing Then
                playing = False
                shouldRaise = True
            End If
        End SyncLock
        ScriptHost.ForceUnblockInput()
        If shouldRaise Then RaiseEvent PlaybackStopped()
    End Sub

    Private Sub PlaybackLoop(plan As List(Of KbmrEvent), speed As Double, loopPlayback As Boolean, blockInput As Boolean)
        If blockInput Then ScriptHost.SetBlockInput(True)
        Try
            Dim maxLoops As Integer = If(loopPlayback, 10000, 1)
            For iteration As Integer = 1 To maxLoops
                Dim clock As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
                For Each item As KbmrEvent In plan
                    If stopFlag Then Return
                    Dim target As Long = CLng(item.OffsetMs / speed)
                    While clock.ElapsedMilliseconds < target
                        If stopFlag Then Return
                        Dim remain As Long = target - clock.ElapsedMilliseconds
                        Thread.Sleep(CInt(Math.Min(remain, 10)))
                    End While
                    ExecuteEvent(item)
                Next
                If stopFlag Then Return
            Next
        Catch
        Finally
            FinishPlayback()
        End Try
    End Sub

    Private Sub ExecuteEvent(item As KbmrEvent)
        Select Case item.Kind
            Case KbmrEventKind.KeyDown
                OpEngine.KeyDown(item.Vk)
            Case KbmrEventKind.KeyUp
                OpEngine.KeyUp(item.Vk)
            Case KbmrEventKind.MouseDown
                MouseButtonEvent(item.Button, True, item.X, item.Y)
            Case KbmrEventKind.MouseUp
                MouseButtonEvent(item.Button, False, item.X, item.Y)
            Case KbmrEventKind.MouseMove
                OpEngine.MouseMove(item.X, item.Y)
            Case KbmrEventKind.Wheel
                UserInputHandler.mouse_event(UserInputHandler.MOUSEEVENTF_WHEEL, 0, 0, item.Delta, 0)
        End Select
    End Sub

    Private Sub MouseButtonEvent(button As Byte, down As Boolean, x As Integer, y As Integer)
        If (button = 1 OrElse button = 2 OrElse button = 3) AndAlso x >= 0 AndAlso y >= 0 Then OpEngine.MouseMove(x, y)
        Select Case button
            Case 1
                UserInputHandler.mouse_event(If(down, UserInputHandler.MOUSEEVENTF_LEFTDOWN, UserInputHandler.MOUSEEVENTF_LEFTUP), 0, 0, 0, 0)
            Case 2
                UserInputHandler.mouse_event(If(down, UserInputHandler.MOUSEEVENTF_RIGHTDOWN, UserInputHandler.MOUSEEVENTF_RIGHTUP), 0, 0, 0, 0)
            Case 3
                UserInputHandler.mouse_event(If(down, UserInputHandler.MOUSEEVENTF_MIDDLEDOWN, UserInputHandler.MOUSEEVENTF_MIDDLEUP), 0, 0, 0, 0)
            Case 4
                UserInputHandler.mouse_event(If(down, UserInputHandler.MOUSEEVENTF_XDOWN, UserInputHandler.MOUSEEVENTF_XUP), 0, 0, 1, 0)
            Case 5
                UserInputHandler.mouse_event(If(down, UserInputHandler.MOUSEEVENTF_XDOWN, UserInputHandler.MOUSEEVENTF_XUP), 0, 0, 2, 0)
        End Select
    End Sub

    '将相邻采样点之间的鼠标移动线性插值为若干中间点，使低采样频率下的回放更平滑。
    Private Function BuildPlaybackEvents(recording As KbmrRecording, smooth As Boolean) As List(Of KbmrEvent)
        If Not smooth Then Return recording.Events
        Dim result As New List(Of KbmrEvent)
        Dim previousMove As KbmrEvent = Nothing
        For Each item As KbmrEvent In recording.Events
            If item.Kind = KbmrEventKind.MouseMove Then
                If previousMove IsNot Nothing AndAlso item.OffsetMs > previousMove.OffsetMs Then
                    Dim gap As Long = item.OffsetMs - previousMove.OffsetMs
                    Dim dx As Integer = item.X - previousMove.X
                    Dim dy As Integer = item.Y - previousMove.Y
                    Dim distance As Double = Math.Sqrt(CDbl(dx) * dx + CDbl(dy) * dy)
                    If distance >= 4.0 AndAlso gap >= 8 AndAlso gap <= 500 Then
                        Dim maxByDistance As Integer = CInt(Math.Floor(distance / 8.0))
                        Dim maxByTime As Integer = CInt(Math.Floor(gap / 4.0))
                        Dim steps As Integer = Math.Min(Math.Min(maxByDistance, maxByTime), 20)
                        If steps > 1 Then
                            For moveStep As Integer = 1 To steps - 1
                                Dim ratio As Double = moveStep / CDbl(steps)
                                result.Add(New KbmrEvent With {
                                    .Kind = KbmrEventKind.MouseMove,
                                    .OffsetMs = previousMove.OffsetMs + CLng(gap * ratio),
                                    .X = CInt(previousMove.X + dx * ratio),
                                    .Y = CInt(previousMove.Y + dy * ratio)
                                })
                            Next
                        End If
                    End If
                End If
                result.Add(item)
                previousMove = item
            Else
                result.Add(item)
                previousMove = Nothing
            End If
        Next
        Return result
    End Function
End Module

'录制导出：将录制事件转换为可直接在“脚本”页运行的 PowerShell 脚本。
Public Module MacroScriptExporter
    Public Function Generate(recording As KbmrRecording) As String
        Dim builder As New Text.StringBuilder()
        builder.AppendLine("# 由键鼠管家录制导出（" & recording.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") & "）")
        builder.AppendLine("# 事件数：" & recording.Events.Count & "，时长：" & (recording.DurationMs / 1000.0).ToString("0.0") & " 秒，鼠标采样频率：" & recording.SampleIntervalMs & " ms")
        builder.AppendLine()
        Dim lastOffset As Long = 0
        Dim lastEmittedMove As KbmrEvent = Nothing
        For Each item As KbmrEvent In recording.Events
            If item.Kind = KbmrEventKind.MouseMove Then
                If lastEmittedMove IsNot Nothing Then
                    Dim gap As Long = item.OffsetMs - lastEmittedMove.OffsetMs
                    Dim distance As Double = Math.Sqrt(CDbl(item.X - lastEmittedMove.X) ^ 2 + CDbl(item.Y - lastEmittedMove.Y) ^ 2)
                    If gap < 40 AndAlso distance < 6.0 Then Continue For
                End If
                AppendDelay(builder, item.OffsetMs - lastOffset)
                builder.AppendLine("Move " & item.X & " " & item.Y)
                lastOffset = item.OffsetMs
                lastEmittedMove = item
            Else
                AppendDelay(builder, item.OffsetMs - lastOffset)
                lastOffset = item.OffsetMs
                Select Case item.Kind
                    Case KbmrEventKind.KeyDown
                        builder.AppendLine("KeyDown '" & KeyNameMapper.FormatKeyName(item.Vk) & "'")
                    Case KbmrEventKind.KeyUp
                        builder.AppendLine("KeyUp '" & KeyNameMapper.FormatKeyName(item.Vk) & "'")
                    Case KbmrEventKind.MouseDown
                        builder.AppendLine("MouseDown '" & ButtonName(item.Button) & "'")
                    Case KbmrEventKind.MouseUp
                        builder.AppendLine("MouseUp '" & ButtonName(item.Button) & "'")
                    Case KbmrEventKind.Wheel
                        builder.AppendLine("Wheel " & item.Delta)
                End Select
            End If
        Next
        Return builder.ToString()
    End Function

    Private Sub AppendDelay(builder As Text.StringBuilder, milliseconds As Long)
        If milliseconds > 0 Then builder.AppendLine("Delay " & milliseconds)
    End Sub

    Private Function ButtonName(button As Byte) As String
        Select Case button
            Case 2
                Return "Right"
            Case 3
                Return "Middle"
            Case 4
                Return "X1"
            Case 5
                Return "X2"
            Case Else
                Return "Left"
        End Select
    End Function
End Module
