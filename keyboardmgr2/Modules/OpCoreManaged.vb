'兼容（托管）操作内核：旧版 System.Timers.Timer + mouse_event/keybd_event 实现，
'作为原生 OpCore DLL 不可用时的回退，提供键鼠操作与连点循环接口。
'行为与旧版保持一致：鼠标连点支持随机速度/坐标偏移，键盘连点发送组合键。
Imports System.Timers

Module OpCoreManaged

    Private timerMouse As Timer
    Private timerKeys As Timer

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
        Return (timerMouse IsNot Nothing AndAlso timerMouse.Enabled) OrElse
               (timerKeys IsNot Nothing AndAlso timerKeys.Enabled) OrElse
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
        timerMouse = New Timer(baseIntervalMs)
        timerMouse.AutoReset = True
        AddHandler timerMouse.Elapsed, AddressOf TimerMouse_Elapsed
        timerMouse.Start()
    End Sub

    Public Sub StartKeyClick(intervalMs As Integer, keys As UShort())
        StopAll()
        baseIntervalMs = Math.Max(1, intervalMs)
        keyList.Clear()
        If keys IsNot Nothing Then keyList.AddRange(keys)
        timerKeys = New Timer(baseIntervalMs)
        timerKeys.AutoReset = True
        AddHandler timerKeys.Elapsed, AddressOf TimerKeys_Elapsed
        timerKeys.Start()
    End Sub

    Public Sub StartMouseHold(button As Integer)
        StopAll()
        heldMouseButton = button
        If button = 2 Then
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0)
        Else
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
        End If
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
        If timerMouse IsNot Nothing Then
            RemoveHandler timerMouse.Elapsed, AddressOf TimerMouse_Elapsed
            timerMouse.Stop()
            timerMouse.Dispose()
            timerMouse = Nothing
        End If
        If timerKeys IsNot Nothing Then
            RemoveHandler timerKeys.Elapsed, AddressOf TimerKeys_Elapsed
            timerKeys.Stop()
            timerKeys.Dispose()
            timerKeys = Nothing
        End If
        If isKeyboardHeld Then
            SendKeyCombinationUp(heldKeyList)
            heldKeyList.Clear()
            isKeyboardHeld = False
        End If
        If isMouseHeld Then
            If heldMouseButton = 2 Then
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0)
            Else
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
            End If
            isMouseHeld = False
            heldMouseButton = 0
        End If
        isSpeedRandomOffset = False
        isPosRandomOffset = False
    End Sub

    Private Sub TimerMouse_Elapsed(sender As Object, e As ElapsedEventArgs)
        If isSpeedRandomOffset Then
            timerMouse.Interval = Math.Max(1, baseIntervalMs + rnd.Next(-10, 11))
        End If
        Dim P As POINTAPI
        GetCursorPos(P)
        If isPosRandomOffset Then
            P.x = baseX + rnd.Next(-15, 16)
            P.y = baseY + rnd.Next(-15, 16)
            SetCursorPosition(P.x, P.y)
        End If
        If mouseButton = 2 Then
            mouse_event(MOUSEEVENTF_RIGHTDOWN, P.x, P.y, 0, 0)
            mouse_event(MOUSEEVENTF_RIGHTUP, P.x, P.y, 0, 0)
        Else
            mouse_event(MOUSEEVENTF_LEFTDOWN, P.x, P.y, 0, 0)
            mouse_event(MOUSEEVENTF_LEFTUP, P.x, P.y, 0, 0)
        End If
    End Sub

    Private Sub TimerKeys_Elapsed(sender As Object, e As ElapsedEventArgs)
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
            If button = 2 Then
                mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, 0)
                mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, 0)
            Else
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0)
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0)
            End If
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
