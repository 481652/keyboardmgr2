'摸鱼功能：Boss Key 模式，一键隐藏所有窗口并前置工作窗口
Imports System.Runtime.InteropServices
Imports System.Text

Module LoafModule

#Region "Win32 API"
    <DllImport("user32.dll", SetLastError:=True)>
    Private Function EnumWindows(lpEnumFunc As EnumWindowsProc, lParam As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function IsWindowVisible(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function GetWindowText(hWnd As IntPtr, lpString As StringBuilder, nMaxCount As Integer) As Integer
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function GetWindowTextLength(hWnd As IntPtr) As Integer
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function ShowWindow(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function IsIconic(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function GetShellWindow() As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function IsWindow(hWnd As IntPtr) As Boolean
    End Function

    Private Delegate Function EnumWindowsProc(hWnd As IntPtr, lParam As IntPtr) As Boolean

    '窗口显示命令
    Private Const SW_MINIMIZE As Integer = 6
    Private Const SW_RESTORE As Integer = 9
    Private Const SW_SHOWNORMAL As Integer = 1
#End Region

#Region "State"
    Private _isInLoafMode As Boolean = False
    Private _minimizedWindows As New List(Of IntPtr)
    Private _workWindowHwnd As IntPtr = IntPtr.Zero

    '当前是否处于摸鱼模式
    Public ReadOnly Property InLoafMode As Boolean
        Get
            Return _isInLoafMode
        End Get
    End Property

    '工作窗口句柄
    Public Property WorkWindowHwnd As IntPtr
        Get
            Return _workWindowHwnd
        End Get
        Set(value As IntPtr)
            _workWindowHwnd = value
        End Set
    End Property
#End Region

#Region "Window Title"
    '获取窗口标题
    Public Function GetWindowTitle(hWnd As IntPtr) As String
        If hWnd = IntPtr.Zero Then Return ""
        Dim titleLen As Integer = GetWindowTextLength(hWnd)
        If titleLen = 0 Then Return ""
        Dim sb As New StringBuilder(titleLen + 1)
        GetWindowText(hWnd, sb, sb.Capacity)
        Return sb.ToString()
    End Function
#End Region

#Region "Loaf Mode"
    '切换摸鱼模式
    Public Sub ToggleLoafMode()
        If _isInLoafMode Then
            ExitLoafMode()
        Else
            EnterLoafMode()
        End If
    End Sub

    '进入摸鱼模式：最小化所有其他窗口，前置工作窗口
    Public Sub EnterLoafMode()
        If _workWindowHwnd = IntPtr.Zero Then
            ShowMyMessage("未设置工作窗口，请先在摸鱼设置中选取窗体。")
            Return
        End If
        If Not IsWindow(_workWindowHwnd) Then
            ShowMyMessage("工作窗口已关闭或不可用，请重新选取窗体。")
            Return
        End If

        '获取本程序窗口句柄，枚举时跳过
        Dim mainWindowHwnd As IntPtr = IntPtr.Zero
        Dim floatingWindowHwnd As IntPtr = IntPtr.Zero
        Try
            mainWindowHwnd = New System.Windows.Interop.WindowInteropHelper(MainWindow1.Instance).Handle
        Catch
        End Try
        Try
            floatingWindowHwnd = New System.Windows.Interop.WindowInteropHelper(FloatingWindow.Instance).Handle
        Catch
        End Try

        Dim shellWindow As IntPtr = GetShellWindow()
        _minimizedWindows.Clear()

        '枚举所有可见的顶级窗口，最小化除工作窗口和本程序窗口外的所有窗口
        EnumWindows(Function(hWnd, lParam)
                        '跳过不可见窗口
                        If Not IsWindowVisible(hWnd) Then Return True
                        '跳过工作窗口
                        If hWnd = _workWindowHwnd Then Return True
                        '跳过本程序窗口
                        If hWnd = mainWindowHwnd OrElse hWnd = floatingWindowHwnd Then Return True
                        '跳过桌面
                        If hWnd = shellWindow Then Return True
                        '跳过无标题窗口（通常是系统窗口）
                        If GetWindowTextLength(hWnd) = 0 Then Return True
                        '跳过已最小化的窗口
                        If IsIconic(hWnd) Then Return True

                        '获取窗口标题，过滤系统特殊窗口
                        Dim titleLen As Integer = GetWindowTextLength(hWnd)
                        Dim sb As New StringBuilder(titleLen + 1)
                        GetWindowText(hWnd, sb, sb.Capacity)
                        Dim title As String = sb.ToString()
                        If title = "Program Manager" Then Return True
                        If title = "设置" Then Return True
                        If title = "Windows Input Experience" Then Return True

                        '最小化该窗口
                        ShowWindow(hWnd, SW_MINIMIZE)
                        _minimizedWindows.Add(hWnd)
                        Return True
                    End Function, IntPtr.Zero)

        '将工作窗口前置
        If IsIconic(_workWindowHwnd) Then
            ShowWindow(_workWindowHwnd, SW_RESTORE)
        End If
        SetForegroundWindow(_workWindowHwnd)

        _isInLoafMode = True
    End Sub

    '退出摸鱼模式：恢复所有被最小化的窗口
    Public Sub ExitLoafMode()
        For Each hWnd In _minimizedWindows
            If IsWindow(hWnd) Then
                ShowWindow(hWnd, SW_RESTORE)
            End If
        Next
        _minimizedWindows.Clear()
        _isInLoafMode = False
    End Sub
#End Region

#Region "Settings"
    '保存工作窗口到注册表
    Public Sub SaveWorkWindow(hwnd As IntPtr)
        _workWindowHwnd = hwnd
        WriteSetting("LoafWorkWindowHwnd", hwnd.ToInt64().ToString())
        WriteSetting("LoafWorkWindowTitle", GetWindowTitle(hwnd))
    End Sub

    '从注册表加载工作窗口
    Public Sub LoadWorkWindow()
        Dim hwndStr As String = ReadSetting("LoafWorkWindowHwnd", "0").ToString()
        Dim hwndVal As Long
        If Long.TryParse(hwndStr, hwndVal) AndAlso hwndVal <> 0 Then
            Dim hwnd As New IntPtr(hwndVal)
            If IsWindow(hwnd) Then
                _workWindowHwnd = hwnd
            Else
                '窗口已不存在，清除设置
                _workWindowHwnd = IntPtr.Zero
                WriteSetting("LoafWorkWindowHwnd", "0")
            End If
        End If
    End Sub

    '获取已保存的工作窗口标题（用于UI显示）
    Public Function GetSavedWorkWindowTitle() As String
        Return ReadSetting("LoafWorkWindowTitle", "").ToString()
    End Function
#End Region

End Module
