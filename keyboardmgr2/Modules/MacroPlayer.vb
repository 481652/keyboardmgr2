'录制回放引擎：后台线程按时间轴重放事件，支持倍速、循环、鼠标移动插值平滑、回放时阻止用户输入，
'以及录制分辨率与当前屏幕不一致时按比例缩放鼠标坐标。
Imports System.Threading

Module MacroPlayer
    Public Event PlaybackStopped()

    Private playbackThread As Thread
    Private playing As Boolean
    '每次回放一个会话号 + 一个等待器；停止信号挂在等待器上（按会话隔离），
    '这样旧线程即使在 StopPlayback 返回后才退出，也不会影响紧接着开始的新回放
    Private session As Integer
    Private waiter As HighResolutionWaiter
    Private ReadOnly stateLock As New Object()

    Public ReadOnly Property IsPlaying As Boolean
        Get
            Return playing
        End Get
    End Property

    ''' <summary>
    ''' 开始回放。
    ''' transform：录制坐标到当前屏幕坐标的变换（PlaybackTransform.Identity 表示按原始像素坐标回放），
    ''' 用于录制分辨率与当前分辨率不一致时按比例还原落点。
    ''' </summary>
    Public Sub StartPlayback(recording As KbmrRecording, speed As Double, loopCount As Integer, loopGapMs As Integer, smoothMove As Boolean, blockInput As Boolean,
                             transform As PlaybackTransform)
        If recording Is Nothing OrElse recording.Events Is Nothing OrElse recording.Events.Count = 0 Then Return
        StopPlayback()
        If speed <= 0 Then speed = 1.0
        '参数异常时回退为恒等变换，避免用无效比例把坐标算飞
        If Double.IsNaN(transform.ScaleX) OrElse Double.IsInfinity(transform.ScaleX) OrElse transform.ScaleX <= 0 OrElse
           Double.IsNaN(transform.ScaleY) OrElse Double.IsInfinity(transform.ScaleY) OrElse transform.ScaleY <= 0 Then
            transform = PlaybackTransform.Identity
        End If
        Dim playbackWaiter As New HighResolutionWaiter()
        Dim sessionId As Integer
        SyncLock stateLock
            session += 1
            sessionId = session
            waiter = playbackWaiter
            playing = True
        End SyncLock
        'T22：插值计划在回放线程上生成，避免长录制在 UI 线程上构建数十万个中间点
        Dim source As KbmrRecording = recording
        If loopCount < 0 Then loopCount = 1
        playbackThread = New Thread(Sub() PlaybackLoop(sessionId, source, smoothMove, speed, loopCount, loopGapMs, blockInput, playbackWaiter, transform)) With {
            .IsBackground = True, .Name = "KbmrPlayer", .Priority = ThreadPriority.AboveNormal}
        playbackThread.Start()
    End Sub

    Public Sub StopPlayback()
        Dim currentWaiter As HighResolutionWaiter
        Dim sessionId As Integer
        SyncLock stateLock
            currentWaiter = waiter
            sessionId = session
        End SyncLock
        'T13/T22：取消会立即打断等待，回放线程在当前事件发送后退出；
        '只做短暂 Join（不再阻塞 UI 最多 2 秒），状态由会话号保证一致
        If currentWaiter IsNot Nothing Then currentWaiter.Cancel()
        Dim thread As Thread = playbackThread
        If thread IsNot Nothing AndAlso thread.IsAlive AndAlso thread IsNot Thread.CurrentThread Then
            thread.Join(100)
        End If
        playbackThread = Nothing
        FinishPlayback(sessionId)
    End Sub

    Private Sub FinishPlayback(sessionId As Integer)
        Dim shouldRaise As Boolean = False
        SyncLock stateLock
            '旧会话的线程迟到退出时不得改动新会话的状态
            If sessionId <> session Then Return
            waiter = Nothing
            If playing Then
                playing = False
                shouldRaise = True
            End If
        End SyncLock
        ScriptHost.ForceUnblockInput()
        If shouldRaise Then RaiseEvent PlaybackStopped()
    End Sub

    '回放每开始一遍时触发（后台线程）：iteration 从 1 开始；total = 0 表示无限循环
    Public Event IterationStarted(iteration As Integer, total As Integer)

    'T22：每个事件按"回放起点 + 偏移/倍速"的绝对目标时间调度（Stopwatch 刻度），
    '高精度可等待定时器粗等待 + 末段自旋补偿，误差不随事件数累积。
    'loopCount：1 = 播放一次；N = 播放 N 遍；0 = 无限循环直到停止。
    'loopGapMs：两遍之间的间隔（毫秒，不受倍速影响）。
    Private Sub PlaybackLoop(sessionId As Integer, recording As KbmrRecording, smoothMove As Boolean, speed As Double,
                             loopCount As Integer, loopGapMs As Integer, blockInput As Boolean, playbackWaiter As HighResolutionWaiter,
                             transform As PlaybackTransform)
        Dim held As New HeldInputs()
        Try
            '旧版本保存的录制结尾可能带着停止录制热键的"按下"，回放时会注入热键把自己停掉；这里同样裁掉
            Dim plan As List(Of KbmrEvent) = InputRecorder.TrimHotkeyArtifacts(BuildPlaybackEvents(recording, smoothMove))
            If playbackWaiter.IsCancelled Then Return
            If blockInput Then ScriptHost.SetBlockInput(True)
            Dim ticksPerMs As Double = Diagnostics.Stopwatch.Frequency / 1000.0 / speed
            Dim gapTicks As Long = CLng(Math.Max(0, loopGapMs) * (Diagnostics.Stopwatch.Frequency / 1000.0))
            Dim iteration As Integer = 0
            Do
                iteration += 1
                RaiseEvent IterationStarted(iteration, loopCount)
                Dim clock As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
                For Each item As KbmrEvent In plan
                    Dim targetTicks As Long = CLng(item.OffsetMs * ticksPerMs)
                    If Not playbackWaiter.WaitUntil(clock, targetTicks) Then Return
                    ExecuteEvent(item, held, transform)
                Next
                '每一遍结束都松开仍被按住的键/按钮，避免下一遍开始时状态错乱
                held.ReleaseAll()
                If loopCount > 0 AndAlso iteration >= loopCount Then Exit Do
                If gapTicks > 0 Then
                    Dim gapClock As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
                    If Not playbackWaiter.WaitUntil(gapClock, gapTicks) Then Return
                End If
                If playbackWaiter.IsCancelled Then Return
            Loop
        Catch
        Finally
            '中途停止时同样释放，防止修饰键/鼠标按钮卡在按下状态
            Try
                held.ReleaseAll()
            Catch
            End Try
            FinishPlayback(sessionId)
            playbackWaiter.Dispose()
        End Try
    End Sub

    '记录回放过程中被按下但尚未抬起的键与鼠标按钮
    Private NotInheritable Class HeldInputs
        Private ReadOnly keys As New HashSet(Of UShort)
        Private ReadOnly buttons As New HashSet(Of Byte)

        Public Sub Track(item As KbmrEvent)
            Select Case item.Kind
                Case KbmrEventKind.KeyDown
                    keys.Add(item.Vk)
                Case KbmrEventKind.KeyUp
                    keys.Remove(item.Vk)
                Case KbmrEventKind.MouseDown
                    buttons.Add(item.Button)
                Case KbmrEventKind.MouseUp
                    buttons.Remove(item.Button)
            End Select
        End Sub

        Public Sub ReleaseAll()
            For Each vk As UShort In keys
                OpEngine.KeyUp(vk)
            Next
            keys.Clear()
            For Each button As Byte In buttons
                UserInputHandler.SendMouseButtonInput(CInt(button), False)
            Next
            buttons.Clear()
        End Sub
    End Class

    Private Sub ExecuteEvent(item As KbmrEvent, held As HeldInputs, transform As PlaybackTransform)
        Select Case item.Kind
            Case KbmrEventKind.KeyDown
                OpEngine.KeyDown(item.Vk)
            Case KbmrEventKind.KeyUp
                OpEngine.KeyUp(item.Vk)
            Case KbmrEventKind.MouseDown
                MouseButtonEvent(item.Button, True, item.HasPosition, item.X, item.Y, transform)
            Case KbmrEventKind.MouseUp
                MouseButtonEvent(item.Button, False, item.HasPosition, item.X, item.Y, transform)
            Case KbmrEventKind.MouseMove
                OpEngine.MouseMove(transform.MapX(item.X), transform.MapY(item.Y))
            Case KbmrEventKind.Wheel
                UserInputHandler.SendMouseWheelInput(item.Delta)
        End Select
        held.Track(item)
    End Sub

    '点击前先把光标移到录制的点击位置；HasPosition=False（旧格式没有记录点击坐标）时沿用当前光标位置
    Private Sub MouseButtonEvent(button As Byte, down As Boolean, hasPosition As Boolean, x As Integer, y As Integer, transform As PlaybackTransform)
        If hasPosition Then OpEngine.MouseMove(transform.MapX(x), transform.MapY(y))
        UserInputHandler.SendMouseButtonInput(CInt(button), down)
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
