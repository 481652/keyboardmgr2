'兼容（托管）操作内核：System.Timers.Timer + SendInput 实现，
'作为原生 OpCore DLL 不可用时的回退，提供键鼠操作与连点循环接口。
'行为与旧版保持一致：鼠标连点支持随机速度/坐标偏移，键盘连点发送组合键。
'T15：连点循环改为专用线程 + HighResolutionWaiter 绝对期限调度（原 System.Timers.Timer 精度锁定约 15.6ms）
Imports System.Threading

Module OpCoreManaged

    Private worker As Thread
    Private waiter As HighResolutionWaiter

    Private baseIntervalMs As Integer = 10
    Private isSpeedRandomOffset As Boolean = False
    Private isPosRandomOffset As Boolean = False
    Private baseX As Integer = 0
    Private baseY As Integer = 0
    Private mouseButton As Integer = 1

    Private keyList As New List(Of UShort)

    Private isMouseHeld As Boolean = False
    Private heldMouseButton As Integer = 0
    Private isKeyboardHeld As Boolean = False
    Private heldKeyList As New List(Of UShort)

    Private rnd As New Random

    Public Function IsRunning() As Boolean
        Dim thread As Thread = worker
        Return (thread IsNot Nothing AndAlso thread.IsAlive) OrElse
               isMouseHeld OrElse isKeyboardHeld
    End Function

    Public Sub StartMouseClick(intervalMs As Integer, randomSpeedOffset As Boolean, randomPosOffset As Boolean, bx As Integer, by As Integer, button As Integer)
        StopAll()
        baseIntervalMs = Math.Max(1, intervalMs)
        isSpeedRandomOffset = randomSpeedOffset
        isPosRandomOffset = randomPosOffset
        baseX = bx
        baseY = by
        mouseButton = button
        StartLoop(AddressOf MouseTick, AddressOf MouseIntervalMs, "ManagedMouseClick")
    End Sub

    Public Sub StartKeyClick(intervalMs As Integer, keys As UShort())
        StopAll()
        If keys Is Nothing OrElse keys.Length < 1 Then Return
        baseIntervalMs = Math.Max(1, intervalMs)
        keyList.Clear()
        keyList.AddRange(keys)
        StartLoop(AddressOf KeyTick, Function() baseIntervalMs, "ManagedKeyClick")
    End Sub

    Public Sub StartMouseHold(button As Integer)
        StopAll()
        heldMouseButton = button
        SendMouseButtonInput(If(button = 2, 2, 1), True)
        isMouseHeld = True
    End Sub

    Public Sub StartKeyHold(keys As UShort())
        StopAll()
        heldKeyList.Clear()
        If keys IsNot Nothing Then heldKeyList.AddRange(keys)
        SendKeyCombinationDown(heldKeyList)
        isKeyboardHeld = True
    End Sub

    Public Sub StopAll()
        Dim currentWaiter As HighResolutionWaiter = waiter
        waiter = Nothing
        If currentWaiter IsNot Nothing Then currentWaiter.Cancel()
        Dim thread As Thread = worker
        worker = Nothing
        '等待被取消后立即退出；tick 中可能正在发送（SendInput 是同步调用），最多等那一拍完成。
        '后台线程 Join 阻塞 UI 不佳，但 StopAll 调用频率低（用户切换功能/停止）且 tick 很快（几毫秒），
        '放弃 Join 会让 worker 在 StopAll 返回后继续跑完当前 tick，与"立即停止"的语义不符。
        If thread IsNot Nothing AndAlso thread IsNot Thread.CurrentThread Then thread.Join(200)
        If isKeyboardHeld Then
            SendKeyCombinationUp(heldKeyList)
            heldKeyList.Clear()
            isKeyboardHeld = False
        End If
        If isMouseHeld Then
            SendMouseButtonInput(If(heldMouseButton = 2, 2, 1), False)
            isMouseHeld = False
            heldMouseButton = 0
        End If
        isSpeedRandomOffset = False
        isPosRandomOffset = False
    End Sub

    Private Sub StartLoop(tick As Action, nextIntervalMs As Func(Of Integer), name As String)
        '间隔 ≥10ms 时不自旋，降低 CPU 占用；<10ms 时自旋 1ms 保证精度（普通定时器会限制为 ≤2ms）
        Dim spinMs As Double = If(nextIntervalMs() >= 10, 0.0, 1.0)
        Dim loopWaiter As New HighResolutionWaiter(spinMs)
        waiter = loopWaiter
        worker = New Thread(Sub() ClickLoop(loopWaiter, tick, nextIntervalMs)) With {
            .IsBackground = True, .Name = name}
        worker.Start()
    End Sub

    'T15：绝对期限调度（nextDue += interval）；落后超过一个周期则跳拍，不补发积压的点击
    Private Sub ClickLoop(loopWaiter As HighResolutionWaiter, tick As Action, nextIntervalMs As Func(Of Integer))
        Try
            Dim clock As Diagnostics.Stopwatch = Diagnostics.Stopwatch.StartNew()
            Dim ticksPerMs As Double = Diagnostics.Stopwatch.Frequency / 1000.0
            Dim nextDue As Long = CLng(nextIntervalMs() * ticksPerMs)
            Do
                If Not loopWaiter.WaitUntil(clock, nextDue) Then Exit Do
                tick()
                Dim intervalTicks As Long = CLng(nextIntervalMs() * ticksPerMs)
                nextDue += intervalTicks
                Dim now As Long = clock.ElapsedTicks
                If now - nextDue > intervalTicks Then nextDue = now + intervalTicks
            Loop
        Catch
            '发送失败等异常：结束循环，等同停止
        Finally
            loopWaiter.Dispose()
        End Try
    End Sub

    Private Function MouseIntervalMs() As Integer
        If isSpeedRandomOffset Then Return Math.Max(1, baseIntervalMs + rnd.Next(-10, 11))
        Return baseIntervalMs
    End Function

    Private Sub MouseTick()
        If isPosRandomOffset Then
            SetCursorPosition(baseX + rnd.Next(-15, 16), baseY + rnd.Next(-15, 16))
        End If
        SendMouseButtonInput(mouseButton, True)
        SendMouseButtonInput(mouseButton, False)
    End Sub

    Private Sub KeyTick()
        If keyList.Count = 1 Then
            SendKey(keyList(0), True)
            SendKey(keyList(0), False)
        ElseIf keyList.Count >= 2 Then
            SendKeyCombination(keyList)
        End If
    End Sub

    Public Function MouseMove(x As Integer, y As Integer) As Boolean
        Try
            SetCursorPosition(x, y)
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Function MouseClick(button As Integer, x As Integer, y As Integer) As Boolean
        Try
            If x >= 0 AndAlso y >= 0 Then SetCursorPosition(x, y)
            SendMouseButtonInput(button, True)
            SendMouseButtonInput(button, False)
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Function KeyTap(vk As UShort) As Boolean
        Try
            SendKey(vk, True)
            SendKey(vk, False)
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Function KeyDown(vk As UShort) As Boolean
        Try
            SendKey(vk, True)
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Function KeyUp(vk As UShort) As Boolean
        Try
            SendKey(vk, False)
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Function KeyCombo(keys As UShort()) As Boolean
        If keys Is Nothing OrElse keys.Length < 1 Then Return False
        Try
            SendKeyCombination(New List(Of UShort)(keys))
            Return True
        Catch
            Return False
        End Try
    End Function

    Public Function TypeText(text As String) As Boolean
        If String.IsNullOrEmpty(text) Then Return True
        Try
            SendUnicodeText(text)
            Return True
        Catch
            Return False
        End Try
    End Function

End Module
