'录制捕获：全局键盘钩子 + 鼠标钩子捕获按键/按钮/滚轮事件，独立线程按用户设置的频率采样鼠标位置。
'事件时间戳均相对于录制起点（Stopwatch 毫秒）。
Imports System.Collections.Concurrent
Imports System.Runtime.InteropServices
Imports System.Threading

Module InputRecorder

    Private Delegate Function HookProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr

    Private keyboardHook As IntPtr = IntPtr.Zero
    Private mouseHook As IntPtr = IntPtr.Zero
    Private keyboardProc As HookProc
    Private mouseProc As HookProc

    Private events As ConcurrentQueue(Of KbmrEvent)
    Private eventCount As Integer
    Private wasTruncated As Boolean
    Private ReadOnly watch As New Diagnostics.Stopwatch()
    Private samplerThread As Thread
    Private sampling As Boolean
    Private recording As Boolean
    Private sampleInterval As Integer = 15
    Private lastSampleX As Integer
    Private lastSampleY As Integer
    '开始录制时的屏幕像素尺寸与 DPI 缩放：录制中途用户可能改分辨率，文件头以开始时刻为准
    Private startScreenWidth As Integer = 1
    Private startScreenHeight As Integer = 1
    Private startDpiScalePermille As Integer = 1000

    Public ReadOnly Property IsRecording As Boolean
        Get
            Return recording
        End Get
    End Property

    Public Sub StartRecording(sampleIntervalMs As Integer)
        If recording Then Return
        sampleInterval = Math.Max(1, Math.Min(1000, sampleIntervalMs))
        startScreenWidth = ScreenMetrics.GetPrimaryScreenWidth()
        startScreenHeight = ScreenMetrics.GetPrimaryScreenHeight()
        startDpiScalePermille = ScreenMetrics.GetDpiScalePermille()
        eventCount = 0
        wasTruncated = False
        events = New ConcurrentQueue(Of KbmrEvent)()
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

    Public Async Function StopRecordingAsync() As Threading.Tasks.Task(Of KbmrRecording)
        If Not recording Then Return Nothing
        recording = False
        sampling = False
        UninstallHooks()
        Dim stoppedSampler As Thread = samplerThread
        samplerThread = Nothing
        watch.Stop()
        '记录与事件坐标同一坐标空间的像素分辨率，以及系统缩放（用于显示）
        Dim queue As ConcurrentQueue(Of KbmrEvent) = events
        events = Nothing
        Dim capturedDuration As Long = watch.ElapsedMilliseconds
        Dim wasTruncatedSnapshot As Boolean = wasTruncated
        Dim result As KbmrRecording = Await Threading.Tasks.Task.Run(
            Function()
                '事件复制、稳定排序和热键残留裁剪也在线程池执行，避免长录制停止时卡 UI。
                If stoppedSampler IsNot Nothing Then stoppedSampler.Join(1000)
                Dim completed As New KbmrRecording With {
                    .Name = "录制", .SampleIntervalMs = sampleInterval,
                    .ScreenWidth = Math.Max(1, startScreenWidth), .ScreenHeight = Math.Max(1, startScreenHeight),
                    .DpiScalePermille = startDpiScalePermille, .DurationMs = capturedDuration, .CreatedUtc = DateTime.UtcNow
                }
                If queue IsNot Nothing Then
                    Dim collected As KbmrEvent() = queue.ToArray()
                    Dim ordered As List(Of KbmrEvent) = collected.OrderBy(Function(e) e.OffsetMs).ToList()
                    completed.Events.AddRange(TrimHotkeyArtifacts(ordered))
                End If
                Return completed
            End Function)
        LastRecordingTruncated = wasTruncatedSnapshot
        Return result
    End Function

    '最近一次录制是否因达到事件上限（KbmrCodec.MaximumEventCount）而被截断
    Public Property LastRecordingTruncated As Boolean

    '去掉开始/停止录制热键留下的残片：
    '- 开头"只有抬起没有按下"的键（按下发生在录制开始前，即开始录制热键）；
    '- 结尾"只有按下没有抬起"的键（停止录制热键：热键触发时录制已结束，抬起没被录到）。
    '后者若保留，回放到末尾会注入停止热键（RegisterHotKey 对注入输入同样生效），
    '导致循环回放在第一遍结束时被自己停掉，并且修饰键会一直处于按下状态。
    Friend Function TrimHotkeyArtifacts(ordered As List(Of KbmrEvent)) As List(Of KbmrEvent)
        Dim pressed As New HashSet(Of UShort)
        Dim leadingOrphanUps As New HashSet(Of Integer)
        Dim lastDownIndex As New Dictionary(Of UShort, Integer)
        For index As Integer = 0 To ordered.Count - 1
            Dim item As KbmrEvent = ordered(index)
            If item.Kind = KbmrEventKind.KeyDown Then
                pressed.Add(item.Vk)
                lastDownIndex(item.Vk) = index
            ElseIf item.Kind = KbmrEventKind.KeyUp Then
                If Not pressed.Remove(item.Vk) Then leadingOrphanUps.Add(index)
            End If
        Next
        '结束时仍处于按下状态的键：删掉它们从最后一次按下开始的所有按下事件（含自动重复）
        Dim trailingDowns As New HashSet(Of Integer)
        For Each vk As UShort In pressed
            Dim firstStuck As Integer = lastDownIndex(vk)
            '向前找到这一段连续按住的起点（自动重复会产生多个 KeyDown）
            For index As Integer = firstStuck To 0 Step -1
                Dim item As KbmrEvent = ordered(index)
                If item.Vk <> vk Then Continue For
                If item.Kind = KbmrEventKind.KeyUp Then Exit For
                If item.Kind = KbmrEventKind.KeyDown Then trailingDowns.Add(index)
            Next
        Next
        If leadingOrphanUps.Count = 0 AndAlso trailingDowns.Count = 0 Then Return ordered
        Dim trimmed As New List(Of KbmrEvent)(ordered.Count)
        For index As Integer = 0 To ordered.Count - 1
            If leadingOrphanUps.Contains(index) OrElse trailingDowns.Contains(index) Then Continue For
            trimmed.Add(ordered(index))
        Next
        '录制至少保留一个事件（起点的鼠标位置），否则无法保存
        If trimmed.Count = 0 Then Return ordered
        Return trimmed
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

    'T17：钩子线程与采样线程只做无锁入队，停止录制时再一次性收集
    Private Sub AddEvent(kind As KbmrEventKind, vk As UShort, button As Byte, x As Integer, y As Integer, delta As Integer)
        Dim queue As ConcurrentQueue(Of KbmrEvent) = events
        If queue Is Nothing Then Return
        '先占位计数，超过上限则记录截断（T19：停止时提示，而非静默丢弃）
        If Interlocked.Increment(eventCount) > KbmrCodec.MaximumEventCount Then
            Interlocked.Decrement(eventCount)
            wasTruncated = True
            Return
        End If
        queue.Enqueue(New KbmrEvent With {
            .OffsetMs = watch.ElapsedMilliseconds,
            .Kind = kind,
            .Vk = vk,
            .Button = button,
            .X = x,
            .Y = y,
            .Delta = delta
        })
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

    'T17：先判 wParam 再读结构体；只读取需要的字段，避免整结构 PtrToStructure 装箱
    Private Function KeyboardHookProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        If nCode >= 0 AndAlso recording Then
            Dim message As Integer = wParam.ToInt32()
            Dim kind As KbmrEventKind
            Dim interesting As Boolean = True
            If message = WM_KEYDOWN OrElse message = WM_SYSKEYDOWN Then
                kind = KbmrEventKind.KeyDown
            ElseIf message = WM_KEYUP OrElse message = WM_SYSKEYUP Then
                kind = KbmrEventKind.KeyUp
            Else
                interesting = False
            End If
            If interesting Then
                Try
                    'KBDLLHOOKSTRUCT: vkCode @0, scanCode @4, flags @8
                    Dim flags As Integer = Marshal.ReadInt32(lParam, 8)
                    If (flags And CInt(LLKHF_INJECTED)) = 0 Then
                        Dim vkCode As Integer = Marshal.ReadInt32(lParam, 0)
                        AddEvent(kind, CUShort(vkCode And &HFF), 0, 0, 0, 0)
                    End If
                Catch
                End Try
            End If
        End If
        Return CallNextHookEx(keyboardHook, nCode, wParam, lParam)
    End Function

    Private Function MouseHookProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        If nCode >= 0 AndAlso recording Then
            Dim message As Integer = wParam.ToInt32()
            '鼠标移动由采样线程负责，这里直接放行（最高频的消息，零开销）
            If message = WM_MOUSEMOVE Then Return CallNextHookEx(mouseHook, nCode, wParam, lParam)
            Try
                Dim data As MSLLHOOKSTRUCT = Marshal.PtrToStructure(Of MSLLHOOKSTRUCT)(lParam)
                If (data.flags And LLMHF_INJECTED) = 0 Then
                    Select Case message
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
    Private Const WM_MOUSEMOVE As Integer = &H200
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
