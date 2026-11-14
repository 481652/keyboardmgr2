Option Strict On
'连发流水线（P0 / F-01）：把窗口激活、UIA 文本写入、发送按钮调用、剪贴板图片粘贴等阻塞操作
'从 WPF UI 线程移到专用 STA 后台线程（UI Automation 与 OLE 剪贴板都要求 STA）。
'- 节拍：Stopwatch 绝对期限（本条开始时刻 + 间隔）+ 高分辨率可等待定时器，不改系统定时器分辨率；
'  单条处理超过间隔时，下一条在完成后立即发送（旧版是"发完再等完整间隔"，导致实际周期 = 处理耗时 + 间隔）。
'- 停止：RequestStop 置位取消令牌，所有等待都同时等待该令牌，可立即打断。
'- 结果：Completed 事件在后台线程触发，订阅方负责封送回 UI 线程。
Imports System.Runtime.InteropServices
Imports System.Threading
Imports System.Windows
Imports System.Windows.Media.Imaging
Imports Stopwatch = System.Diagnostics.Stopwatch

Public Enum SendCompletionReason
    Finished
    Stopped
    TargetWindowClosed
    SendError
End Enum

Public NotInheritable Class SendOptions
    Public Property Items As IReadOnlyList(Of RapidFireItem)
    Public Property TargetWindow As IntPtr
    Public Property IntervalMs As Integer
    Public Property LoopMode As Boolean
    Public Property UseShiftEnter As Boolean
    '0=快捷键，1=点击发送按钮，2=自动（先按钮后快捷键）
    Public Property SendMethod As Integer
    Public Property UseInputBox As Boolean
    Public Property InputBoxX As Integer
    Public Property InputBoxY As Integer
End Class

Public NotInheritable Class SendPipeline
    Public Event Completed(sessionId As Integer, reason As SendCompletionReason, message As String)

    Private ReadOnly gate As New Object()
    Private worker As Thread
    Private pendingJob As SendOptions
    Private pendingSession As Integer
    Private activeCts As CancellationTokenSource
    Private sessionCounter As Integer
    '最近一次放到剪贴板的图片 DataObject（copy=False 时剪贴板只持有代理，目标程序延迟取数时由本线程响应）。
    '用字段保活到下一次写剪贴板，避免局部变量被回收后延迟渲染失败（旧版 MainWindow 同样用字段保活）。
    '只在流水线线程上读写。
    Private clipboardImageData As DataObject

    Private Const MaximumClipboardRetryCount As Integer = 20
    Private Const MaximumImageFocusRetryCount As Integer = 10
    Private Const MaximumImageSendMilliseconds As Integer = 5000

    '提交一次连发任务；若已有任务在执行则先取消。返回会话号，用于在 Completed 中识别过期结果。
    Public Function Start(options As SendOptions) As Integer
        If options Is Nothing OrElse options.Items Is Nothing OrElse options.Items.Count = 0 Then
            Throw New ArgumentException("连发内容为空。", NameOf(options))
        End If
        SyncLock gate
            If activeCts IsNot Nothing Then activeCts.Cancel()
            sessionCounter += 1
            pendingJob = options
            pendingSession = sessionCounter
            activeCts = New CancellationTokenSource()
            If worker Is Nothing OrElse Not worker.IsAlive Then
                worker = New Thread(AddressOf WorkerMain) With {.IsBackground = True, .Name = "RapidFireSendPipeline"}
                worker.SetApartmentState(ApartmentState.STA)
                worker.Start()
            End If
            Monitor.PulseAll(gate)
            Return sessionCounter
        End SyncLock
    End Function

    '请求停止当前任务（非阻塞，可在任意线程调用）
    Public Sub RequestStop()
        SyncLock gate
            pendingJob = Nothing
            If activeCts IsNot Nothing Then activeCts.Cancel()
        End SyncLock
    End Sub

    Private Sub WorkerMain()
        OleInitialize(IntPtr.Zero)
        Dim timer As IntPtr = CreateWaitableTimerExW(IntPtr.Zero, Nothing, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS)
        If timer = IntPtr.Zero Then timer = CreateWaitableTimerExW(IntPtr.Zero, Nothing, 0UI, TIMER_ALL_ACCESS)
        Try
            Do
                Dim job As SendOptions
                Dim cts As CancellationTokenSource
                Dim sessionId As Integer
                SyncLock gate
                    While pendingJob Is Nothing
                        Monitor.Wait(gate)
                    End While
                    job = pendingJob
                    cts = activeCts
                    sessionId = pendingSession
                    pendingJob = Nothing
                End SyncLock

                Dim message As String = Nothing
                Dim reason As SendCompletionReason
                Try
                    reason = RunJob(job, timer, cts.Token, message)
                Catch ex As Exception
                    reason = SendCompletionReason.SendError
                    message = "无法发送连发条目：" & ex.Message
                End Try

                SyncLock gate
                    If activeCts Is cts Then activeCts = Nothing
                End SyncLock
                cts.Dispose()

                Try
                    RaiseEvent Completed(sessionId, reason, message)
                Catch
                    '订阅方异常不能让流水线线程退出
                End Try
            Loop
        Finally
            If timer <> IntPtr.Zero Then CloseHandle(timer)
        End Try
    End Sub

    Private Function RunJob(job As SendOptions, timer As IntPtr, ct As CancellationToken, ByRef message As String) As SendCompletionReason
        UserInputHandler.ResetUiaCache()
        Dim intervalTicks As Long = CLng(Math.Max(1, job.IntervalMs) * (Stopwatch.Frequency / 1000.0))
        Dim index As Integer = 0
        Dim nextDue As Long = Stopwatch.GetTimestamp() '首条立即发送
        Do
            If Not WaitUntil(nextDue, timer, ct) Then Return SendCompletionReason.Stopped
            If Not IsWindow(job.TargetWindow) Then
                message = "连发目标窗口已关闭，任务已停止。"
                Return SendCompletionReason.TargetWindowClosed
            End If

            Dim started As Long = Stopwatch.GetTimestamp()
            If Not SendItem(job.Items(index), job, timer, ct, message) Then
                Return If(ct.IsCancellationRequested, SendCompletionReason.Stopped, SendCompletionReason.SendError)
            End If

            index += 1
            If index >= job.Items.Count Then
                If Not job.LoopMode Then Return SendCompletionReason.Finished
                index = 0
            End If

            '周期从本条开始时刻计算：处理耗时计入间隔，不再叠加；处理超过间隔时下一条立即发送（不补发、不堆积）
            nextDue = started + intervalTicks
            Dim finished As Long = Stopwatch.GetTimestamp()
            If nextDue < finished Then nextDue = finished
        Loop
    End Function

    Private Function SendItem(item As RapidFireItem, job As SendOptions, timer As IntPtr, ct As CancellationToken, ByRef message As String) As Boolean
        If item.Kind = RapidFireItemKind.Image Then Return SendImage(item, job, timer, ct, message)
        Try
            '激活失败不能继续：否则文本和 Enter 会落到当前前台窗口（可能是任意其他程序）
            If Not EnsureForeground(job.TargetWindow, timer, ct) Then
                If ct.IsCancellationRequested Then Return False
                If Not IsWindow(job.TargetWindow) Then
                    message = "连发目标窗口已关闭，任务已停止。"
                Else
                    message = "无法将焦点切换到连发目标窗口，任务已停止，以免把内容发送到其他窗口。"
                End If
                Return False
            End If
            If job.UseInputBox Then
                UserInputHandler.InsertTextToTarget(item.Text, job.TargetWindow, job.InputBoxX, job.InputBoxY, ct)
            Else
                UserInputHandler.InsertTextToTarget(item.Text, job.TargetWindow, Integer.MinValue, Integer.MinValue, ct)
            End If
            If ct.IsCancellationRequested Then Return False
            '写入文本期间焦点可能被用户或其他程序抢走：按 Enter 前再确认一次
            If GetForegroundWindow() <> job.TargetWindow Then
                message = "发送前连发目标窗口失去焦点，任务已停止，以免把 Enter 发送到其他窗口。"
                Return False
            End If
            Return PressSend(job, message)
        Catch ex As Exception
            message = "无法发送连发条目：" & ex.Message
            Return False
        End Try
    End Function

    '图片条目：写剪贴板（重试）→ 确认目标在前台 → Ctrl+V → 等待目标处理 → 发送
    Private Function SendImage(item As RapidFireItem, job As SendOptions, timer As IntPtr, ct As CancellationToken, ByRef message As String) As Boolean
        Dim deadline As Long = Stopwatch.GetTimestamp() + CLng(MaximumImageSendMilliseconds * (Stopwatch.Frequency / 1000.0))
        Try
            Dim clipboardRetries As Integer = 0
            Do
                Try
                    clipboardImageData = SetImageClipboard(item.ImageBytes)
                    Exit Do
                Catch ex As ExternalException
                    clipboardRetries += 1
                    If clipboardRetries >= MaximumClipboardRetryCount Then
                        message = "无法使用剪贴板，其他程序可能持续占用剪贴板。请稍后重试。"
                        Return False
                    End If
                    If Not Delay(50, timer, ct) Then Return False
                End Try
            Loop

            If Not Delay(100, timer, ct) Then Return False

            Dim focusRetries As Integer = 0
            While GetForegroundWindow() <> job.TargetWindow
                If Stopwatch.GetTimestamp() >= deadline Then
                    message = "图片发送等待超时，任务已停止。请确认目标输入框支持使用 Ctrl+V 粘贴图片。"
                    Return False
                End If
                If Not IsWindow(job.TargetWindow) Then
                    message = "连发目标窗口已关闭，任务已停止。"
                    Return False
                End If
                SetForegroundWindow(job.TargetWindow)
                focusRetries += 1
                If focusRetries >= MaximumImageFocusRetryCount Then
                    message = "无法将焦点切换到连发目标窗口，图片发送已停止。"
                    Return False
                End If
                If Not Delay(50, timer, ct) Then Return False
            End While

            UserInputHandler.SendPasteShortcut()
            If Not Delay(500, timer, ct) Then Return False
            If Stopwatch.GetTimestamp() >= deadline Then
                message = "图片发送等待超时，任务已停止。请确认目标输入框支持使用 Ctrl+V 粘贴图片。"
                Return False
            End If
            If GetForegroundWindow() <> job.TargetWindow Then
                message = "发送前连发目标窗口失去焦点，任务已停止，以免把 Enter 发送到其他窗口。"
                Return False
            End If
            Return PressSend(job, message)
        Catch ex As ComponentModel.Win32Exception
            message = ex.Message
            Return False
        Catch ex As Exception
            message = "无法发送图片：" & ex.Message
            Return False
        End Try
    End Function

    '与旧版 MainWindow.SetImageClipboardData 一致：位图 + PNG 两种格式；copy=False 避免被剪贴板监听器阻塞。
    '本线程为 STA 且在等待期间持续泵消息，可以响应其他程序的延迟取数请求。
    '返回写入剪贴板的 DataObject，调用方必须保活（见 clipboardImageData）。
    Private Shared Function SetImageClipboard(imageBytes As Byte()) As DataObject
        If imageBytes Is Nothing OrElse imageBytes.Length = 0 Then Throw New IO.InvalidDataException("图片内容为空。")
        Dim bitmap As New BitmapImage()
        Using stream As New IO.MemoryStream(imageBytes, False)
            bitmap.BeginInit()
            bitmap.CacheOption = BitmapCacheOption.OnLoad
            bitmap.StreamSource = stream
            bitmap.EndInit()
        End Using
        bitmap.Freeze()

        Dim pngBytes As Byte()
        Using pngStream As New IO.MemoryStream()
            Dim encoder As New PngBitmapEncoder()
            encoder.Frames.Add(BitmapFrame.Create(bitmap))
            encoder.Save(pngStream)
            pngBytes = pngStream.ToArray()
        End Using

        Dim data As New DataObject()
        data.SetImage(bitmap)
        data.SetData("PNG", New IO.MemoryStream(pngBytes, False), False)
        Clipboard.SetDataObject(data, False)
        Return data
    End Function

    '与旧版 MainWindow.SendPhrase 一致
    Private Shared Function PressSend(job As SendOptions, ByRef message As String) As Boolean
        Select Case job.SendMethod
            Case 1
                If UserInputHandler.TryInvokeSendButton(job.TargetWindow) Then Return True
                message = "未在目标窗口中找到""发送""按钮，任务已停止。可改用""快捷键""或""自动""发送方式。"
                Return False
            Case 2
                If Not UserInputHandler.TryInvokeSendButton(job.TargetWindow) Then UserInputHandler.SendSendKey(job.UseShiftEnter)
                Return True
            Case Else
                UserInputHandler.SendSendKey(job.UseShiftEnter)
                Return True
        End Select
    End Function

    '目标已在前台时不做任何等待；否则最多尝试 4 次，每次等待 60ms（可被停止打断）
    Private Shared Function EnsureForeground(hwnd As IntPtr, timer As IntPtr, ct As CancellationToken) As Boolean
        If GetForegroundWindow() = hwnd Then Return True
        If IsIconic(hwnd) Then ShowWindowAsync(hwnd, SW_RESTORE)
        For attempt As Integer = 1 To 4
            SetForegroundWindow(hwnd)
            BringWindowToTop(hwnd)
            If Not Delay(60, timer, ct) Then Return False
            If GetForegroundWindow() = hwnd Then Return True
        Next
        Return False
    End Function

    Private Shared Function Delay(milliseconds As Integer, timer As IntPtr, ct As CancellationToken) As Boolean
        Dim due As Long = Stopwatch.GetTimestamp() + CLng(milliseconds * (Stopwatch.Frequency / 1000.0))
        Return WaitUntil(due, timer, ct)
    End Function

    '等待到绝对时间戳 due（Stopwatch 刻度）；期间泵送本线程消息，取消令牌置位时立即返回 False
    Private Shared Function WaitUntil(due As Long, timer As IntPtr, ct As CancellationToken) As Boolean
        Dim cancelHandle As IntPtr = ct.WaitHandle.SafeWaitHandle.DangerousGetHandle()
        Do
            If ct.IsCancellationRequested Then Return False
            Dim remaining As Long = due - Stopwatch.GetTimestamp()
            If remaining <= 0 Then Return True

            Dim waitHandles() As IntPtr
            Dim timeoutMs As UInteger
            Dim remaining100ns As Long = CLng(remaining * (10000000.0 / Stopwatch.Frequency))
            Dim dueTime As Long = -Math.Max(1L, remaining100ns)
            If timer <> IntPtr.Zero AndAlso SetWaitableTimer(timer, dueTime, 0, IntPtr.Zero, IntPtr.Zero, False) Then
                waitHandles = {cancelHandle, timer}
                timeoutMs = INFINITE
            Else
                waitHandles = {cancelHandle}
                timeoutMs = CUInt(Math.Max(1L, (remaining100ns + 9999) \ 10000))
            End If

            Dim result As UInteger = MsgWaitForMultipleObjectsEx(CUInt(waitHandles.Length), waitHandles, timeoutMs, QS_ALLINPUT, MWMO_INPUTAVAILABLE)
            If result = WAIT_OBJECT_0 Then Return False
            If result = WAIT_OBJECT_0 + CUInt(waitHandles.Length) Then
                PumpMessages()
            ElseIf result = WAIT_FAILED Then
                Thread.Sleep(1)
            End If
        Loop
    End Function

    Private Shared Sub PumpMessages()
        Dim msg As NativeMessage
        While PeekMessageW(msg, IntPtr.Zero, 0UI, 0UI, PM_REMOVE)
            TranslateMessage(msg)
            DispatchMessageW(msg)
        End While
    End Sub

#Region "Win32"
    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativeMessage
        Public hwnd As IntPtr
        Public message As UInteger
        Public wParam As IntPtr
        Public lParam As IntPtr
        Public time As UInteger
        Public x As Integer
        Public y As Integer
    End Structure

    Private Const CREATE_WAITABLE_TIMER_HIGH_RESOLUTION As UInteger = &H2UI
    Private Const TIMER_ALL_ACCESS As UInteger = &H1F0003UI
    Private Const INFINITE As UInteger = &HFFFFFFFFUI
    Private Const WAIT_OBJECT_0 As UInteger = 0UI
    Private Const WAIT_FAILED As UInteger = &HFFFFFFFFUI
    Private Const QS_ALLINPUT As UInteger = &H4FFUI
    Private Const MWMO_INPUTAVAILABLE As UInteger = &H4UI
    Private Const PM_REMOVE As UInteger = &H1UI
    Private Const SW_RESTORE As Integer = 9

    <DllImport("ole32.dll")>
    Private Shared Function OleInitialize(pvReserved As IntPtr) As Integer
    End Function

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Private Shared Function CreateWaitableTimerExW(lpTimerAttributes As IntPtr, lpTimerName As String, dwFlags As UInteger, dwDesiredAccess As UInteger) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function SetWaitableTimer(hTimer As IntPtr, ByRef pDueTime As Long, lPeriod As Integer, pfnCompletionRoutine As IntPtr, lpArgToCompletionRoutine As IntPtr, fResume As Boolean) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(hObject As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function MsgWaitForMultipleObjectsEx(nCount As UInteger, pHandles As IntPtr(), dwMilliseconds As UInteger, dwWakeMask As UInteger, dwFlags As UInteger) As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Shared Function PeekMessageW(ByRef lpMsg As NativeMessage, hWnd As IntPtr, wMsgFilterMin As UInteger, wMsgFilterMax As UInteger, wRemoveMsg As UInteger) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function TranslateMessage(ByRef lpMsg As NativeMessage) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function DispatchMessageW(ByRef lpMsg As NativeMessage) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function BringWindowToTop(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsIconic(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowWindowAsync(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function
#End Region
End Class
