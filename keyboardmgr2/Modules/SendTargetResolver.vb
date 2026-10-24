Imports System.Diagnostics
Imports System.Runtime.InteropServices
Imports System.Text

Public Module SendTargetResolver

    Public Structure SendTargetInfo
        Public Hwnd As IntPtr
        Public ProcessId As UInteger
        Public ClassName As String
        Public Title As String
    End Structure

    Private Const GA_ROOT As UInteger = 2
    Private Const GWL_EXSTYLE As Integer = -20
    Private Const WS_EX_TOOLWINDOW As Integer = &H80
    Private Const SW_RESTORE As Integer = 9
    Private Const PROCESS_QUERY_LIMITED_INFORMATION As UInteger = &H1000
    Private Const TOKEN_QUERY As UInteger = &H8
    Private Const TokenElevation As Integer = 20
    Private Const ERROR_ACCESS_DENIED As Integer = 5

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure POINT
        Public X As Integer
        Public Y As Integer
    End Structure

    <DllImport("user32.dll")>
    Private Function GetAncestor(hWnd As IntPtr, gaFlags As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Function IsWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function IsWindowVisible(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function GetShellWindow() As IntPtr
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, EntryPoint:="FindWindowW")>
    Private Function FindWindow(lpClassName As String, lpWindowName As String) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Function GetWindowLong(hWnd As IntPtr, nIndex As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Function GetWindowRect(hWnd As IntPtr, ByRef lpRect As RECT) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function GetClientRect(hWnd As IntPtr, ByRef lpRect As RECT) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function ScreenToClient(hWnd As IntPtr, ByRef lpPoint As POINT) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function ClientToScreen(hWnd As IntPtr, ByRef lpPoint As POINT) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function BringWindowToTop(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function IsIconic(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Function ShowWindowAsync(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Sub SwitchToThisWindow(hWnd As IntPtr, fAltTab As Boolean)
    End Sub

    <DllImport("user32.dll")>
    Private Function AttachThreadInput(idAttach As UInteger, idAttachTo As UInteger, fAttach As Boolean) As Boolean
    End Function

    <DllImport("kernel32.dll")>
    Private Function GetCurrentThreadId() As UInteger
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, EntryPoint:="GetClassNameW")>
    Private Function GetClassName(hWnd As IntPtr, lpClassName As StringBuilder, nMaxCount As Integer) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, EntryPoint:="GetWindowTextW")>
    Private Function GetWindowText(hWnd As IntPtr, lpString As StringBuilder, nMaxCount As Integer) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Unicode, EntryPoint:="GetWindowTextLengthW")>
    Private Function GetWindowTextLength(hWnd As IntPtr) As Integer
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function OpenProcess(dwDesiredAccess As UInteger, <MarshalAs(UnmanagedType.Bool)> bInheritHandle As Boolean, dwProcessId As UInteger) As IntPtr
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function OpenProcessToken(ProcessHandle As IntPtr, DesiredAccess As UInteger, ByRef TokenHandle As IntPtr) As Boolean
    End Function

    <DllImport("advapi32.dll", SetLastError:=True)>
    Private Function GetTokenInformation(TokenHandle As IntPtr, TokenInformationClass As Integer, ByRef TokenInformation As Integer, TokenInformationLength As Integer, ByRef ReturnLength As Integer) As Boolean
    End Function

    <DllImport("kernel32.dll", SetLastError:=True)>
    Private Function CloseHandle(hObject As IntPtr) As Boolean
    End Function

    Public Function GetRootWindow(hWnd As IntPtr) As IntPtr
        If hWnd = IntPtr.Zero Then Return IntPtr.Zero
        Try
            Dim root As IntPtr = GetAncestor(hWnd, GA_ROOT)
            If root <> IntPtr.Zero Then Return root
        Catch
        End Try
        Return hWnd
    End Function

    Public Function GetWindowProcessId(hWnd As IntPtr) As UInteger
        If hWnd = IntPtr.Zero Then Return 0
        Dim processId As UInteger = 0
        GetWindowThreadProcessId(hWnd, processId)
        Return processId
    End Function

    Public Function GetWindowClassName(hWnd As IntPtr) As String
        If hWnd = IntPtr.Zero Then Return String.Empty
        Try
            Dim builder As New StringBuilder(256)
            If GetClassName(hWnd, builder, builder.Capacity) > 0 Then Return builder.ToString()
        Catch
        End Try
        Return String.Empty
    End Function

    Public Function GetWindowTitleText(hWnd As IntPtr) As String
        If hWnd = IntPtr.Zero Then Return String.Empty
        Try
            Dim length As Integer = GetWindowTextLength(hWnd)
            If length <= 0 Then Return String.Empty
            Dim builder As New StringBuilder(Math.Min(length, 512) + 1)
            GetWindowText(hWnd, builder, builder.Capacity)
            Return builder.ToString()
        Catch
        End Try
        Return String.Empty
    End Function

    Public Function IsOwnProcessWindow(hWnd As IntPtr) As Boolean
        If hWnd = IntPtr.Zero Then Return False
        Dim processId As UInteger = GetWindowProcessId(hWnd)
        If processId = 0 Then Return False
        Return processId = CUInt(Process.GetCurrentProcess().Id)
    End Function

    Private Function IsShellWindow(hWnd As IntPtr) As Boolean
        If hWnd = IntPtr.Zero Then Return False
        Try
            If hWnd = GetShellWindow() Then Return True
            If hWnd = FindWindow("Progman", Nothing) Then Return True
            If hWnd = FindWindow("WorkerW", Nothing) Then Return True
            If hWnd = FindWindow("Shell_TrayWnd", Nothing) Then Return True
        Catch
        End Try
        Return False
    End Function

    Public Function IsEligibleTargetWindow(hWnd As IntPtr, allowToolWindow As Boolean) As Boolean
        If hWnd = IntPtr.Zero Then Return False
        Dim root As IntPtr = GetRootWindow(hWnd)
        If root = IntPtr.Zero OrElse Not IsWindow(root) Then Return False
        If Not IsWindowVisible(root) Then Return False
        If IsOwnProcessWindow(root) Then Return False
        If IsShellWindow(root) Then Return False
        Dim rect As RECT
        If Not GetWindowRect(root, rect) Then Return False
        If rect.Right - rect.Left < 40 OrElse rect.Bottom - rect.Top < 30 Then Return False
        If Not allowToolWindow AndAlso (GetWindowLong(root, GWL_EXSTYLE) And WS_EX_TOOLWINDOW) <> 0 Then Return False
        Return True
    End Function

    Public Function TryGetWindowInfo(hWnd As IntPtr, ByRef info As SendTargetInfo) As Boolean
        Dim root As IntPtr = GetRootWindow(hWnd)
        If root = IntPtr.Zero OrElse Not IsWindow(root) Then Return False
        info.Hwnd = root
        info.ProcessId = GetWindowProcessId(root)
        info.ClassName = GetWindowClassName(root)
        info.Title = GetWindowTitleText(root)
        Return True
    End Function

    Public Function IsSameTargetWindow(hWnd As IntPtr, expectedProcessId As UInteger, expectedClassName As String) As Boolean
        If hWnd = IntPtr.Zero OrElse Not IsEligibleTargetWindow(hWnd, False) Then Return False
        If GetWindowProcessId(hWnd) <> expectedProcessId Then Return False
        If Not String.IsNullOrEmpty(expectedClassName) Then
            If Not String.Equals(GetWindowClassName(hWnd), expectedClassName, StringComparison.OrdinalIgnoreCase) Then Return False
        End If
        Return True
    End Function

    Public Function IsForegroundTarget(hWnd As IntPtr) As Boolean
        Dim target As IntPtr = GetRootWindow(hWnd)
        Dim foreground As IntPtr = GetForegroundWindow()
        If target = IntPtr.Zero OrElse foreground = IntPtr.Zero Then Return False
        Return GetRootWindow(foreground) = target
    End Function

    Public Function TryActivateWindow(hWnd As IntPtr) As Boolean
        Dim target As IntPtr = GetRootWindow(hWnd)
        If target = IntPtr.Zero OrElse Not IsWindow(target) Then Return False
        Try
            If IsIconic(target) Then ShowWindowAsync(target, SW_RESTORE)

            For attempt As Integer = 1 To 4
                If GetForegroundWindow() = target Then Return True
                If Not SetForegroundWindow(target) Then
                    ForceForegroundWindow(target)
                Else
                    BringWindowToTop(target)
                End If
                Threading.Thread.Sleep(60)
                If GetForegroundWindow() = target Then Return True
            Next

            ForceForegroundWindow(target)
            Threading.Thread.Sleep(60)
        Catch
        End Try
        Return GetForegroundWindow() = target
    End Function

    Private Sub ForceForegroundWindow(hWnd As IntPtr)
        Dim currentThreadId As UInteger = GetCurrentThreadId()
        Dim targetProcessId As UInteger = 0
        Dim targetThreadId As UInteger = GetWindowThreadProcessId(hWnd, targetProcessId)
        Dim foreground As IntPtr = GetForegroundWindow()
        Dim foregroundProcessId As UInteger = 0
        Dim foregroundThreadId As UInteger = 0
        If foreground <> IntPtr.Zero Then foregroundThreadId = GetWindowThreadProcessId(foreground, foregroundProcessId)
        Dim attachedTarget As Boolean = False
        Dim attachedForeground As Boolean = False
        Try
            If targetThreadId <> 0 AndAlso targetThreadId <> currentThreadId Then
                attachedTarget = AttachThreadInput(currentThreadId, targetThreadId, True)
            End If
            If foregroundThreadId <> 0 AndAlso foregroundThreadId <> currentThreadId AndAlso foregroundThreadId <> targetThreadId Then
                attachedForeground = AttachThreadInput(currentThreadId, foregroundThreadId, True)
            End If
            SetForegroundWindow(hWnd)
            BringWindowToTop(hWnd)
            SwitchToThisWindow(hWnd, True)
        Finally
            If attachedForeground Then AttachThreadInput(currentThreadId, foregroundThreadId, False)
            If attachedTarget Then AttachThreadInput(currentThreadId, targetThreadId, False)
        End Try
    End Sub

    Public Function TryScreenToWindowClient(hWnd As IntPtr, screenX As Integer, screenY As Integer, ByRef clientX As Integer, ByRef clientY As Integer) As Boolean
        Dim target As IntPtr = GetRootWindow(hWnd)
        If target = IntPtr.Zero OrElse Not IsWindow(target) Then Return False
        Dim point As New POINT With {.X = screenX, .Y = screenY}
        If Not ScreenToClient(target, point) Then Return False
        clientX = point.X
        clientY = point.Y
        Return True
    End Function

    Public Function TryWindowClientToScreen(hWnd As IntPtr, clientX As Integer, clientY As Integer, ByRef screenX As Integer, ByRef screenY As Integer) As Boolean
        Dim target As IntPtr = GetRootWindow(hWnd)
        If target = IntPtr.Zero OrElse Not IsWindow(target) Then Return False
        Dim point As New POINT With {.X = clientX, .Y = clientY}
        If Not ClientToScreen(target, point) Then Return False
        screenX = point.X
        screenY = point.Y
        Return True
    End Function

    Public Function IsPointInWindowClient(hWnd As IntPtr, clientX As Integer, clientY As Integer) As Boolean
        Dim target As IntPtr = GetRootWindow(hWnd)
        If target = IntPtr.Zero Then Return False
        Dim rect As RECT
        If Not GetClientRect(target, rect) Then Return False
        Return clientX >= 0 AndAlso clientY >= 0 AndAlso clientX < rect.Right AndAlso clientY < rect.Bottom
    End Function

    Public Function IsTargetLikelyElevated(hWnd As IntPtr) As Boolean
        Dim targetProcessId As UInteger = GetWindowProcessId(hWnd)
        If targetProcessId = 0 Then Return False
        Dim currentProcessId As UInteger = CUInt(Process.GetCurrentProcess().Id)
        If targetProcessId = currentProcessId Then Return False
        If IsProcessElevated(currentProcessId, False) Then Return False
        Return IsProcessElevated(targetProcessId, True)
    End Function

    Private Function IsProcessElevated(processId As UInteger, treatAccessDeniedAsElevated As Boolean) As Boolean
        Dim processHandle As IntPtr = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, processId)
        If processHandle = IntPtr.Zero Then
            Return treatAccessDeniedAsElevated AndAlso Marshal.GetLastWin32Error() = ERROR_ACCESS_DENIED
        End If
        Dim tokenHandle As IntPtr = IntPtr.Zero
        Try
            If Not OpenProcessToken(processHandle, TOKEN_QUERY, tokenHandle) Then Return False
            Dim elevated As Integer = 0
            Dim returnedLength As Integer = 0
            If Not GetTokenInformation(tokenHandle, TokenElevation, elevated, Marshal.SizeOf(GetType(Integer)), returnedLength) Then Return False
            Return elevated <> 0
        Finally
            If tokenHandle <> IntPtr.Zero Then CloseHandle(tokenHandle)
            CloseHandle(processHandle)
        End Try
    End Function

End Module
