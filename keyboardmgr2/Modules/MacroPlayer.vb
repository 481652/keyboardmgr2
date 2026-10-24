'录制回放引擎：后台线程按时间轴重放事件，支持倍速、循环、鼠标移动插值平滑与回放时阻止用户输入。
Imports System.Threading

Module MacroPlayer
    Public Event PlaybackStopped(recording As KbmrRecording)

    Private playbackThread As Thread
    Private stopFlag As Integer
    Private playbackGeneration As Integer
    Private playing As Boolean
    Private currentRecording As KbmrRecording
    Private ReadOnly stateLock As New Object()
    Private ReadOnly inputStateLock As New Object()
    Private ReadOnly pressedKeys As New HashSet(Of UShort)()
    Private ReadOnly pressedMouseButtons As New HashSet(Of Integer)()

    Public ReadOnly Property IsPlaying As Boolean
        Get
            SyncLock stateLock
                Return playing
            End SyncLock
        End Get
    End Property

    Public Sub StartPlayback(recording As KbmrRecording, speed As Double, loopPlayback As Boolean, smoothMove As Boolean, blockInput As Boolean)
        If recording Is Nothing OrElse recording.Events Is Nothing OrElse recording.Events.Count = 0 Then Return
        StopPlayback()
        If speed <= 0 Then speed = 1.0
        Dim generation As Integer = Threading.Interlocked.Increment(playbackGeneration)
        Threading.Interlocked.Exchange(stopFlag, 0)
        SyncLock inputStateLock
            pressedKeys.Clear()
            pressedMouseButtons.Clear()
        End SyncLock
        SyncLock stateLock
            playing = True
            currentRecording = recording
        End SyncLock
        playbackThread = New Thread(Sub() PlaybackLoop(recording, speed, loopPlayback, smoothMove, blockInput, generation)) With {.IsBackground = True, .Name = "KbmrPlayer"}
        playbackThread.Start()
    End Sub

    Public Sub StopPlayback()
        Threading.Interlocked.Exchange(stopFlag, 1)
        Dim generation As Integer = Threading.Volatile.Read(playbackGeneration)
        Dim thread As Thread = playbackThread
        If thread IsNot Nothing AndAlso thread.IsAlive AndAlso thread IsNot Thread.CurrentThread Then
            thread.Join()
        End If
        ReleasePlaybackInputs()
        playbackThread = Nothing
        FinishPlayback(generation)
    End Sub

    Private Sub FinishPlayback(generation As Integer)
        If generation <> Threading.Volatile.Read(playbackGeneration) Then Return
        ReleasePlaybackInputs()
        Dim shouldRaise As Boolean = False
        Dim stoppedRecording As KbmrRecording = Nothing
        SyncLock stateLock
            If generation = Threading.Volatile.Read(playbackGeneration) AndAlso playing Then
                playing = False
                shouldRaise = True
                stoppedRecording = currentRecording
                currentRecording = Nothing
            End If
        End SyncLock
        ScriptHost.ForceUnblockInput()
        If shouldRaise Then RaiseEvent PlaybackStopped(stoppedRecording)
    End Sub

    Private Sub PlaybackLoop(recording As KbmrRecording, speed As Double, loopPlayback As Boolean,
                             smoothMove As Boolean, blockInput As Boolean, generation As Integer)
        Try
            If blockInput Then ScriptHost.SetBlockInput(True)
            Dim maxLoops As Integer = If(loopPlayback, 10000, 1)
            For iteration As Integer = 1 To maxLoops
                Dim clock As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
                For Each item As KbmrEvent In EnumeratePlaybackEvents(recording, smoothMove)
                    If IsPlaybackStopped(generation) Then Return
                    Dim target As Long = CLng(item.OffsetMs / speed)
                    While clock.ElapsedMilliseconds < target
                        If IsPlaybackStopped(generation) Then Return
                        Dim remain As Long = target - clock.ElapsedMilliseconds
                        Thread.Sleep(CInt(Math.Min(remain, 10)))
                    End While
                    ExecuteEvent(item, generation)
                Next
                If IsPlaybackStopped(generation) Then Return
            Next
        Catch
        Finally
            FinishPlayback(generation)
        End Try
    End Sub

    Private Function IsPlaybackStopped(generation As Integer) As Boolean
        Return generation <> Threading.Volatile.Read(playbackGeneration) OrElse Threading.Volatile.Read(stopFlag) <> 0
    End Function

    Private Sub ExecuteEvent(item As KbmrEvent, generation As Integer)
        Select Case item.Kind
            Case KbmrEventKind.KeyDown
                SyncLock inputStateLock
                    If IsPlaybackStopped(generation) Then Return
                    Dim sent As Boolean = OpEngine.KeyDown(item.Vk)
                    If IsPlaybackStopped(generation) Then
                        If sent Then OpEngine.KeyUp(item.Vk)
                    ElseIf sent Then
                        pressedKeys.Add(item.Vk)
                    End If
                End SyncLock
            Case KbmrEventKind.KeyUp
                SyncLock inputStateLock
                    If IsPlaybackStopped(generation) Then Return
                    If OpEngine.KeyUp(item.Vk) Then pressedKeys.Remove(item.Vk)
                End SyncLock
            Case KbmrEventKind.MouseDown
                MouseButtonEvent(item.Button, True, item.X, item.Y, generation)
            Case KbmrEventKind.MouseUp
                MouseButtonEvent(item.Button, False, item.X, item.Y, generation)
            Case KbmrEventKind.MouseMove
                If Not IsPlaybackStopped(generation) Then OpEngine.MouseMove(item.X, item.Y)
            Case KbmrEventKind.Wheel
                If Not IsPlaybackStopped(generation) Then UserInputHandler.SendMouseWheelInput(item.Delta)
        End Select
    End Sub

    Private Sub MouseButtonEvent(button As Byte, down As Boolean, x As Integer, y As Integer, generation As Integer)
        SyncLock inputStateLock
            If IsPlaybackStopped(generation) Then Return
            If (button = 1 OrElse button = 2 OrElse button = 3) AndAlso x >= 0 AndAlso y >= 0 Then OpEngine.MouseMove(x, y)
            UserInputHandler.SendMouseButtonInput(CInt(button), down)
            If down AndAlso Not IsPlaybackStopped(generation) Then
                pressedMouseButtons.Add(CInt(button))
            ElseIf down Then
                UserInputHandler.SendMouseButtonInput(CInt(button), False)
            Else
                pressedMouseButtons.Remove(CInt(button))
            End If
        End SyncLock
    End Sub

    '录制可能在 KeyUp/MouseUp 之前结束；确保回放异常、停止或自然结束都不会留下输入状态。
    Private Sub ReleasePlaybackInputs()
        SyncLock inputStateLock
            For Each vk As UShort In pressedKeys
                OpEngine.KeyUp(vk)
            Next
            pressedKeys.Clear()

            For Each button As Integer In pressedMouseButtons
                UserInputHandler.SendMouseButtonInput(button, False)
            Next
            pressedMouseButtons.Clear()
        End SyncLock
    End Sub

    '按需生成平滑中间点，避免为大型录制复制整份事件列表。
    Private Iterator Function EnumeratePlaybackEvents(recording As KbmrRecording, smooth As Boolean) As IEnumerable(Of KbmrEvent)
        Dim previousMove As KbmrEvent = Nothing
        For Each item As KbmrEvent In recording.Events
            If smooth AndAlso item.Kind = KbmrEventKind.MouseMove Then
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
                                Yield New KbmrEvent With {
                                    .Kind = KbmrEventKind.MouseMove,
                                    .OffsetMs = previousMove.OffsetMs + CLng(gap * ratio),
                                    .X = CInt(previousMove.X + dx * ratio),
                                    .Y = CInt(previousMove.Y + dy * ratio)
                                }
                            Next
                        End If
                    End If
                End If
                previousMove = item
            Else
                If smooth Then previousMove = Nothing
            End If
            Yield item
        Next
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
