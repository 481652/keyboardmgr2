'录制捕获：全局键盘钩子 + 鼠标钩子捕获按键/按钮/滚轮事件，独立线程按用户设置的频率采样鼠标位置。
'事件时间戳均相对于录制起点（Stopwatch 毫秒）。
Imports System.Runtime.InteropServices
Imports System.Threading

Module InputRecorder

    Private Delegate Function HookProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr

    Private keyboardHook As IntPtr = IntPtr.Zero
    Private mouseHook As IntPtr = IntPtr.Zero
    Private keyboardProc As HookProc
    Private mouseProc As HookProc

    Private events As List(Of KbmrEvent)
    Private ReadOnly eventsLock As New Object()
    Private ReadOnly watch As New Diagnostics.Stopwatch()
    Private samplerThread As Thread
    Private sampling As Boolean
    Private recording As Boolean
    Private sampleInterval As Integer = 15
    Private lastSampleX As Integer
    Private lastSampleY As Integer

    Public ReadOnly Property IsRecording As Boolean
        Get
            Return recording
        End Get
    End Property

    Public Sub StartRecording(sampleIntervalMs As Integer)
        If recording Then Return
        sampleInterval = Math.Max(1, Math.Min(1000, sampleIntervalMs))
        events = New List(Of KbmrEvent)()
        watch.Restart()
        Dim point As New POINTAPI
        If GetCursorPos(point) Then
            lastSampleX = point.x
            lastSampleY = point.y
        Else
            lastSampleX = 0
            lastSampleY = 0
        End If
        AddEvent(KbmrEventKind.MouseMove, 0, 0, lastSampleX, lastSampleY, 0)
        recording = True
        sampling = True
        InstallHooks()
        samplerThread = New Thread(AddressOf SamplerLoop) With {.IsBackground = True, .Name = "KbmrMouseSampler"}
        samplerThread.Start()
    End Sub

    Public Function StopRecording() As KbmrRecording
        If Not recording Then Return Nothing
        recording = False
        sampling = False
        UninstallHooks()
        If samplerThread IsNot Nothing Then
            samplerThread.Join(1000)
            samplerThread = Nothing
        End If
        watch.Stop()
        Dim result As New KbmrRecording With {
            .Name = "录制",
            .SampleIntervalMs = sampleInterval,
            .ScreenWidth = CInt(Math.Round(SystemParameters.PrimaryScreenWidth)),
            .ScreenHeight = CInt(Math.Round(SystemParameters.PrimaryScreenHeight)),
            .DurationMs = watch.ElapsedMilliseconds,
            .CreatedUtc = DateTime.UtcNow
        }
        SyncLock eventsLock
            result.Events.AddRange(events)
        End SyncLock
        events = Nothing
        Return result
    End Function

    Private Sub SamplerLoop()
        While sampling
            Thread.Sleep(sampleInterval)
            If Not sampling Then Exit While
            Dim point As New POINTAPI
            If GetCursorPos(point) Then
                If point.x <> lastSampleX OrElse point.y <> lastSampleY Then
                    lastSampleX = point.x
                    lastSampleY = point.y
                    AddEvent(KbmrEventKind.MouseMove, 0, 0, point.x, point.y, 0)
                End If
            End If
        End While
    End Sub

    Private Sub AddEvent(kind As KbmrEventKind, vk As UShort, button As Byte, x As Integer, y As Integer, delta As Integer)
        Dim item As New KbmrEvent With {
            .OffsetMs = watch.ElapsedMilliseconds,
            .Kind = kind,
            .Vk = vk,
            .Button = button,
            .X = x,
            .Y = y,
            .Delta = delta
        }
        SyncLock eventsLock
            If events IsNot Nothing AndAlso events.Count < KbmrCodec.MaximumEventCount Then events.Add(item)
        End SyncLock
    End Sub

    Private Sub InstallHooks()
        keyboardProc = New HookProc(AddressOf KeyboardHookProc)
        mouseProc = New HookProc(AddressOf MouseHookProc)
        Using currentProcess As Process = Process.GetCurrentProcess()
            Using currentModule As ProcessModule = currentProcess.MainModule
                Dim moduleHandle As IntPtr = GetModuleHandle(currentModule.ModuleName)
                keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, keyboardProc, moduleHandle, 0)
                mouseHook = SetWindowsHookEx(WH_MOUSE_LL, mouseProc, moduleHandle, 0)
            End Using
        End Using
    End Sub

    Private Sub UninstallHooks()
        If keyboardHook <> IntPtr.Zero Then
            UnhookWindowsHookEx(keyboardHook)
            keyboardHook = IntPtr.Zero
        End If
        If mouseHook <> IntPtr.Zero Then
            UnhookWindowsHookEx(mouseHook)
            mouseHook = IntPtr.Zero
        End If
        keyboardProc = Nothing
        mouseProc = Nothing
    End Sub

    Private Function KeyboardHookProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        If nCode >= 0 AndAlso recording Then
            Try
                Dim data As KBDLLHOOKSTRUCT = Marshal.PtrToStructure(Of KBDLLHOOKSTRUCT)(lParam)
                If (data.flags And LLKHF_INJECTED) = 0 Then
                    Dim message As Integer = wParam.ToInt32()
                    If message = WM_KEYDOWN OrElse message = WM_SYSKEYDOWN Then
                        AddEvent(KbmrEventKind.KeyDown, CUShort(data.vkCode And &HFFUI), 0, 0, 0, 0)
                    ElseIf message = WM_KEYUP OrElse message = WM_SYSKEYUP Then
                        AddEvent(KbmrEventKind.KeyUp, CUShort(data.vkCode And &HFFUI), 0, 0, 0, 0)
                    End If
                End If
            Catch
            End Try
        End If
        Return CallNextHookEx(keyboardHook, nCode, wParam, lParam)
    End Function

    Private Function MouseHookProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        If nCode >= 0 AndAlso recording Then
            Try
                Dim data As MSLLHOOKSTRUCT = Marshal.PtrToStructure(Of MSLLHOOKSTRUCT)(lParam)
                If (data.flags And LLMHF_INJECTED) = 0 Then
                    Select Case wParam.ToInt32()
                        Case WM_LBUTTONDOWN
                            AddEvent(KbmrEventKind.MouseDown, 0, 1, data.pt.x, data.pt.y, 0)
                        Case WM_LBUTTONUP
                            AddEvent(KbmrEventKind.MouseUp, 0, 1, data.pt.x, data.pt.y, 0)
                        Case WM_RBUTTONDOWN
                            AddEvent(KbmrEventKind.MouseDown, 0, 2, data.pt.x, data.pt.y, 0)
                        Case WM_RBUTTONUP
                            AddEvent(KbmrEventKind.MouseUp, 0, 2, data.pt.x, data.pt.y, 0)
                        Case WM_MBUTTONDOWN
                            AddEvent(KbmrEventKind.MouseDown, 0, 3, data.pt.x, data.pt.y, 0)
                        Case WM_MBUTTONUP
                            AddEvent(KbmrEventKind.MouseUp, 0, 3, data.pt.x, data.pt.y, 0)
                        Case WM_XBUTTONDOWN, WM_XBUTTONUP
                            Dim button As Byte = If(((data.mouseData >> 16) And &HFFFFUI) = 1UI, CByte(4), CByte(5))
                            Dim kind As KbmrEventKind = If(wParam.ToInt32() = WM_XBUTTONDOWN, KbmrEventKind.MouseDown, KbmrEventKind.MouseUp)
                            AddEvent(kind, 0, button, data.pt.x, data.pt.y, 0)
                        Case WM_MOUSEWHEEL
                            Dim raw As Integer = CInt((data.mouseData >> 16) And &HFFFFUI)
                            If raw > 32767 Then raw -= 65536
                            AddEvent(KbmrEventKind.Wheel, 0, 0, data.pt.x, data.pt.y, raw)
                    End Select
                End If
            Catch
            End Try
        End If
        Return CallNextHookEx(mouseHook, nCode, wParam, lParam)
    End Function

    Private Const WH_KEYBOARD_LL As Integer = 13
    Private Const WH_MOUSE_LL As Integer = 14
    Private Const WM_KEYDOWN As Integer = &H100
    Private Const WM_KEYUP As Integer = &H101
    Private Const WM_SYSKEYDOWN As Integer = &H104
    Private Const WM_SYSKEYUP As Integer = &H105
    Private Const WM_LBUTTONDOWN As Integer = &H201
    Private Const WM_LBUTTONUP As Integer = &H202
    Private Const WM_RBUTTONDOWN As Integer = &H204
    Private Const WM_RBUTTONUP As Integer = &H205
    Private Const WM_MBUTTONDOWN As Integer = &H207
    Private Const WM_MBUTTONUP As Integer = &H208
    Private Const WM_MOUSEWHEEL As Integer = &H20A
    Private Const WM_XBUTTONDOWN As Integer = &H20B
    Private Const WM_XBUTTONUP As Integer = &H20C
    Private Const LLKHF_INJECTED As UInteger = &H10UI
    Private Const LLMHF_INJECTED As UInteger = &H1UI

    <StructLayout(LayoutKind.Sequential)>
    Private Structure POINTAPI
        Public x As Integer
        Public y As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure KBDLLHOOKSTRUCT
        Public vkCode As UInteger
        Public scanCode As UInteger
        Public flags As UInteger
        Public time As UInteger
        Public dwExtraInfo As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MSLLHOOKSTRUCT
        Public pt As POINTAPI
        Public mouseData As UInteger
        Public flags As UInteger
        Public time As UInteger
        Public dwExtraInfo As IntPtr
    End Structure

    <DllImport("kernel32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Function GetModuleHandle(lpModuleName As String) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function SetWindowsHookEx(idHook As Integer, lpfn As HookProc, hMod As IntPtr, dwThreadId As Integer) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function UnhookWindowsHookEx(hHook As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function CallNextHookEx(hHook As IntPtr, nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function GetCursorPos(ByRef lpPoint As POINTAPI) As Boolean
    End Function

End Module
