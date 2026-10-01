
'全局鼠标钩子类， 仅用于窗体选取时捕获鼠标动作
Imports System.Runtime.InteropServices

Public Class GlobalMouseHook

    '鼠标钩子委托
    Private Delegate Function HookCallback(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr

    '鼠标钩子句柄
    Private mouseHook As IntPtr = IntPtr.Zero

    '鼠标钩子回调函数（作为类字段持有，在 InstallHook 期间保持引用，防止被 GC 回收）
    Private mouseDelegate As HookCallback

    '安装钩子
    Public Sub InstallHook()
        mouseDelegate = New HookCallback(AddressOf MouseHookProc)
        mouseHook = SetHook(mouseDelegate)
        'mouseDelegate 已是类字段，钩子存续期间不会被 GC，无需额外 GCHandle.Alloc
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

    Private Function MouseHookProc(nCode As Integer, wParam As IntPtr, lParam As IntPtr) As IntPtr
        If nCode >= 0 Then
            If wParam = CType(WM_LBUTTONDOWN, IntPtr) Then
                '将 lParam 转换为 MSLLHOOKSTRUCT 结构体
                Dim hookStruct As MSLLHOOKSTRUCT = Marshal.PtrToStructure(Of MSLLHOOKSTRUCT)(lParam)
                '访问 hookStruct 中的字段，使用 8 字节的 POINTAPI 传给 WindowFromPoint
                Dim p As New POINTAPI With {.x = hookStruct.pt.x, .y = hookStruct.pt.y}
                Dim hWnd As IntPtr = WindowFromPoint(p)
                '检查句柄是否有效
                If hWnd <> IntPtr.Zero Then
                    RaiseEvent WindowSelected(hWnd, hookStruct.pt.x, hookStruct.pt.y)
                    StopSelection()
                End If
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

    '自定义事件用于传递选定的窗口句柄与点击坐标
    Public Event WindowSelected(hWnd As IntPtr, x As Integer, y As Integer)

    '停止选择的公共方法
    Public Sub StopSelection()
        UninstallHook()
    End Sub
End Class
