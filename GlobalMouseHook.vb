'全局鼠标钩子类，仅用于窗体选取时捕获鼠标动作
Imports System.Runtime.InteropServices

Public Class GlobalMouseHook

    '鼠标钩子委托
    Private Delegate Function HookCallback(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr

    '鼠标钩子句柄
    Private mouseHook As IntPtr = IntPtr.Zero

    '鼠标钩子回调函数
    Private mouseDelegate As HookCallback

    '安装钩子
    Public Sub InstallHook()
        mouseDelegate = New HookCallback(AddressOf MouseHookProc)
        mouseHook = SetHook(mouseDelegate)
    End Sub

    '卸载钩子
    Public Sub UninstallHook()
        UnhookWindowsHookEx(mouseHook)
    End Sub

    '设置钩子
    Private Function SetHook(proc As HookCallback) As IntPtr
        Using curProcess As Process = Process.GetCurrentProcess()
            Using curModule As ProcessModule = curProcess.MainModule
                Return SetWindowsHookEx(WH_MOUSE_LL, proc, GetModuleHandle(curModule.ModuleName), 0)
            End Using
        End Using
    End Function

    '鼠标钩子回调函数
    Private Function MouseHookProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        If nCode >= 0 AndAlso wParam = CType(WM_LBUTTONDOWN, IntPtr) Then
            Dim hookStruct As MSLLHOOKSTRUCT = Marshal.PtrToStructure(Of MSLLHOOKSTRUCT)(lParam)
            '处理左键点击事件            
            Dim p As New GlobalMouseHook.POINT With {.x = hookStruct.pt.x, .y = hookStruct.pt.y}
            '转换为函数需要的point
            Dim systemPoint As New Windows.Point(Convert.ToDouble(p.x), Convert.ToDouble(p.y))
            Dim hWnd As IntPtr = WindowFromPoint(systemPoint)
            ' 检查句柄是否有效
            If hWnd <> IntPtr.Zero Then
                RaiseEvent WindowSelected(hWnd)
                '只有在成功选取窗体后才停止选取状态
                StopSelection()
            End If
        End If
        Return CallNextHookEx(mouseHook, nCode, wParam, lParam)
    End Function

    Private Const WH_MOUSE_LL As Integer = 14
    Private Const WM_LBUTTONDOWN As Integer = &H201

    <StructLayout(LayoutKind.Sequential)>
    Private Structure POINT
        Public x As Integer
        Public y As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MSLLHOOKSTRUCT
        Public pt As POINT
        Public mouseData As UInt32
        Public flags As UInt32
        Public time As UInt32
        Public dwExtraInfo As IntPtr
    End Structure

    'Windows API 函数声明
    <DllImport("kernel32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function GetModuleHandle(lpModuleName As String) As IntPtr
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function SetWindowsHookEx(idHook As Integer, lpfn As HookCallback, hMod As IntPtr, dwThreadId As Integer) As IntPtr
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function UnhookWindowsHookEx(hHook As IntPtr) As <MarshalAs(UnmanagedType.Bool)> Boolean
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto, SetLastError:=True)>
    Private Shared Function CallNextHookEx(hHook As IntPtr, nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
    End Function

    '自定义事件用于传递选定的窗口句柄
    Public Event WindowSelected(hWnd As IntPtr)


    '停止选择的公共方法
    Public Sub StopSelection()
        UninstallHook()
    End Sub
End Class
