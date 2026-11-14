'高精度可中断等待：高分辨率可等待定时器（Win10 1803+）负责粗等待，最后约 1ms 用自旋补偿。
'不支持高分辨率定时器的系统回退为普通可等待定时器。等待可被 Cancel() 立即打断。
'线程模型：一个实例只被一个"所有者线程"用于 WaitUntil 并由它 Dispose；Cancel() 可从任意线程调用。
'Cancel 与 Dispose 并发是安全的：二者都在 stateLock 内检查/修改 disposed，句柄不会在 Set 期间被关闭。
Imports System.Runtime.InteropServices
Imports System.Threading

Public NotInheritable Class HighResolutionWaiter
    Implements IDisposable

    Private Const CREATE_WAITABLE_TIMER_HIGH_RESOLUTION As UInteger = &H2UI
    Private Const TIMER_ALL_ACCESS As UInteger = &H1F0003UI
    Private Const WAIT_OBJECT_0 As UInteger = 0UI
    Private Const INFINITE As UInteger = &HFFFFFFFFUI
    Private Shared ReadOnly TicksPerMs As Double = Diagnostics.Stopwatch.Frequency / 1000.0

    Private ReadOnly timerHandle As IntPtr
    Private ReadOnly isHighResolution As Boolean
    Private ReadOnly cancelEvent As New ManualResetEvent(False)
    Private ReadOnly stateLock As New Object()
    '末段自旋阈值（Stopwatch 刻度）。高分辨率定时器唤醒误差约 0.5ms，只需自旋 ~1ms；
    '普通定时器受系统时钟分辨率（常见 15.6ms）影响，粗等待提前量取 2ms，避免整段自旋。
    Private ReadOnly spinThresholdTicks As Long
    Private disposed As Boolean
    Private cancelled As Boolean

    ''' <param name="spinMs">末段自旋时长（毫秒，0 表示不自旋，仅靠定时器；适合对 CPU 敏感、对亚毫秒精度不敏感的场景）。</param>
    Public Sub New(Optional spinMs As Double = 1.0)
        Dim handle As IntPtr = CreateWaitableTimerExW(IntPtr.Zero, Nothing, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS)
        If handle <> IntPtr.Zero Then
            isHighResolution = True
        Else
            '旧系统不支持高分辨率标志
            handle = CreateWaitableTimerExW(IntPtr.Zero, Nothing, 0UI, TIMER_ALL_ACCESS)
        End If
        timerHandle = handle
        Dim effectiveSpinMs As Double = Math.Max(0.0, spinMs)
        '普通定时器唤醒可能迟到一个时钟周期，自旋补偿无意义且耗 CPU：只在要求自旋时保留最多 2ms
        If Not isHighResolution AndAlso effectiveSpinMs > 0 Then effectiveSpinMs = Math.Min(effectiveSpinMs, 2.0)
        spinThresholdTicks = CLng(effectiveSpinMs * TicksPerMs)
    End Sub

    Public ReadOnly Property IsCancelled As Boolean
        Get
            Return Volatile.Read(cancelled)
        End Get
    End Property

    '可从任意线程调用；对已释放的实例是空操作
    Public Sub Cancel()
        '读写取消事件与 Dispose 用同一把锁，避免 Set 与 Close 并发。
        SyncLock stateLock
            If disposed OrElse cancelled Then Return
            cancelled = True
            cancelEvent.Set()
        End SyncLock
    End Sub

    ''' <summary>等待到 clock 的绝对刻度 targetTicks（Stopwatch 刻度）。返回 False 表示被取消。</summary>
    Public Function WaitUntil(clock As Diagnostics.Stopwatch, targetTicks As Long) As Boolean
        Do
            If Volatile.Read(cancelled) Then Return False
            SyncLock stateLock
                If disposed Then Return False
            End SyncLock
            Dim remaining As Long = targetTicks - clock.ElapsedTicks
            If remaining <= 0 Then Return True
            If remaining <= spinThresholdTicks Then
                SpinUntil(clock, targetTicks)
                Return Not Volatile.Read(cancelled)
            End If
            If Not CoarseWait(remaining - spinThresholdTicks) Then Return False
        Loop
    End Function

    Private Function CoarseWait(stopwatchTicks As Long) As Boolean
        If Volatile.Read(cancelled) Then Return False
        SyncLock stateLock
            If disposed Then Return False
        End SyncLock
        If timerHandle = IntPtr.Zero Then
            Dim ms As Integer = CInt(Math.Max(1L, Math.Min(Integer.MaxValue, CLng(stopwatchTicks / TicksPerMs))))
            Return Not cancelEvent.WaitOne(ms)
        End If
        '相对时间以 100ns 为单位，负值表示相对
        Dim hundredNs As Long = CLng(stopwatchTicks * (10000000.0 / Diagnostics.Stopwatch.Frequency))
        If hundredNs < 1 Then hundredNs = 1
        Dim dueTime As Long = -hundredNs
        If Not SetWaitableTimer(timerHandle, dueTime, 0, IntPtr.Zero, IntPtr.Zero, False) Then
            Dim ms As Integer = CInt(Math.Max(1L, hundredNs \ 10000L))
            Return Not cancelEvent.WaitOne(ms)
        End If
        '所有者线程独占 Dispose，因此等待期间句柄一定有效
        Dim waitHandles As IntPtr() = {timerHandle, cancelEvent.SafeWaitHandle.DangerousGetHandle()}
        Dim result As UInteger
        Try
            result = WaitForMultipleObjects(2UI, waitHandles, False, INFINITE)
        Catch ex As ObjectDisposedException
            Return False
        End Try
        If result = WAIT_OBJECT_0 + 1UI Then
            CancelWaitableTimer(timerHandle)
            Return False
        End If
        Return True
    End Function

    '末段自旋：前半段 Yield 让出时间片，最后约 0.2ms 纯自旋，降低低间隔档的 CPU 占用
    Private Sub SpinUntil(clock As Diagnostics.Stopwatch, targetTicks As Long)
        Dim pureSpinTicks As Long = CLng(0.2 * TicksPerMs)
        Do
            If Volatile.Read(cancelled) Then Return
            Dim remaining As Long = targetTicks - clock.ElapsedTicks
            If remaining <= 0 Then Return
            If remaining > pureSpinTicks Then
                Thread.Yield()
            Else
                Thread.SpinWait(20)
            End If
        Loop
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        SyncLock stateLock
            If disposed Then Return
            disposed = True
            If timerHandle <> IntPtr.Zero Then CloseHandle(timerHandle)
            cancelEvent.Dispose()
        End SyncLock
    End Sub

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Private Shared Function CreateWaitableTimerExW(lpTimerAttributes As IntPtr, lpTimerName As String, dwFlags As UInteger, dwDesiredAccess As UInteger) As IntPtr
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function SetWaitableTimer(hTimer As IntPtr, ByRef pDueTime As Long, lPeriod As Integer, pfnCompletionRoutine As IntPtr, lpArgToCompletionRoutine As IntPtr, fResume As Boolean) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CancelWaitableTimer(hTimer As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function WaitForMultipleObjects(nCount As UInteger, lpHandles As IntPtr(), bWaitAll As Boolean, dwMilliseconds As UInteger) As UInteger
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Shared Function CloseHandle(hObject As IntPtr) As Boolean
    End Function
End Class
