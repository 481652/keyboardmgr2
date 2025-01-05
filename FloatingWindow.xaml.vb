Imports System.Runtime.InteropServices
Imports System.Timers
Imports System.Windows.Forms
Imports System.Windows.Interop
Imports System.Windows.Media.Animation


Public Class FloatingWindow
    Inherits Window
    Private Shared _instance As FloatingWindow

#Region "Hide in ALT+TAB"
    '使用API来防止在ALT+TAB中显示
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowLong(hwnd As IntPtr, nIndex As Integer, dwNewLong As Integer) As Integer
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function GetWindowLong(hwnd As IntPtr, nIndex As Integer) As Integer
    End Function

    Private Const GWL_EXSTYLE As Integer = -20
    Private Const WS_EX_TOOLWINDOW As Integer = &H80
    Private Const WS_EX_APPWINDOW As Integer = &H40000

    Private slideDown As DoubleAnimation
    Private slideUp As DoubleAnimation
    Private isClosing As Boolean = False

#End Region


#Region "GlobalHotkey"
    'todo:完善注册全局快捷键
    Private Const WM_HOTKEY As Integer = &H312 '定义热键消息


    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function RegisterHotKey(hWnd As IntPtr, id As Integer, fsModifiers As Integer, vk As Integer) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function UnregisterHotKey(hWnd As IntPtr, id As Integer) As Boolean
    End Function

    Private Structure NativeMessage
        Public HWnd As IntPtr
        Public Msg As Integer
        Public WParam As IntPtr
        Public LParam As IntPtr
        Public Result As IntPtr
    End Structure

    Private Function WndProc(hwnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr, ByRef handled As Boolean) As IntPtr
        Dim hwndMsg As NativeMessage
        hwndMsg.HWnd = hwnd
        hwndMsg.Msg = msg
        hwndMsg.WParam = wParam
        hwndMsg.LParam = lParam
        hwndMsg.Result = IntPtr.Zero
        If hwndMsg.Msg = WM_HOTKEY Then
            Dim hotkeyId As Int32 = CType(hwndMsg.WParam, Int32)
            '下面的处理总感觉不太优雅，有待改进
            Select Case hotkeyId
                Case 9000 '停止操作
                    StopActions()
                Case 9001
                Case 9002
                Case 9003
                Case 9004
                Case 9005
                Case 9006
                Case Else
            End Select
        End If

        Return IntPtr.Zero
    End Function

    Private hotkeynum As Integer = 0

    Public Sub RegisterGlobalHotkey(hotkey As List(Of Byte), hotkeyid As Integer) '注册全局热键,hotkeyid为热键ID,范围为9000-9006,调用时注意对应上面的处理过程
        If hotkey.Count > 2 Or hotkey.Count = 1 Then
            ShowMyMessage("快捷键非法，请重新设置")
            Return
        End If
        Try
            Select Case hotkey(0) '处理键修饰符,这里的hotkey为虚拟键码
                Case ConvertKeyToVirtualKeyCode(Key.LWin)
                    RegisterHotKey(New WindowInteropHelper(Me).Handle, hotkeyid, 8, hotkey(1))
                Case ConvertKeyToVirtualKeyCode(Key.RWin)
                    RegisterHotKey(New WindowInteropHelper(Me).Handle, hotkeyid, 8, hotkey(1))
                Case ConvertKeyToVirtualKeyCode(Key.LeftCtrl)
                    RegisterHotKey(New WindowInteropHelper(Me).Handle, hotkeyid, 2, hotkey(1))
                Case ConvertKeyToVirtualKeyCode(Key.RightCtrl)
                    RegisterHotKey(New WindowInteropHelper(Me).Handle, hotkeyid, 2, hotkey(1))
                Case ConvertKeyToVirtualKeyCode(Key.LeftAlt)
                    RegisterHotKey(New WindowInteropHelper(Me).Handle, hotkeyid, 1, hotkey(1))
                Case ConvertKeyToVirtualKeyCode(Key.RightAlt)
                    RegisterHotKey(New WindowInteropHelper(Me).Handle, hotkeyid, 1, hotkey(1))
                Case ConvertKeyToVirtualKeyCode(Key.LeftShift)
                    RegisterHotKey(New WindowInteropHelper(Me).Handle, hotkeyid, 4, hotkey(1))
                Case ConvertKeyToVirtualKeyCode(Key.RightShift）
                    RegisterHotKey(New WindowInteropHelper(Me).Handle, hotkeyid, 4, hotkey(1))
                Case Else
                    RegisterHotKey(New WindowInteropHelper(Me).Handle, hotkeyid, 0, hotkey(0)) '如果是其它的则无修饰符
            End Select
            hotkeynum += 1
        Catch ex As Exception
            StopActions() '先停止操作
            ShowExpdlg("错误8：程序无法注册快捷键，可能是快捷键非法，请更换快捷键。", ex.Message)
        End Try
    End Sub

    Public Sub UnregisterGlobalHotkey() '注销所有全局热键
        For i As Integer = 9000 To 9000 + hotkeynum
            UnregisterHotKey(New WindowInteropHelper(Me).Handle, i)
        Next
    End Sub

#End Region

#Region "Topmost"
    '置顶当前窗体
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowPos(ByVal hWnd As IntPtr, ByVal hWndInsertAfter As IntPtr, ByVal X As Integer, ByVal Y As Integer, ByVal cx As Integer, ByVal cy As Integer, ByVal uFlags As UInteger) As Boolean
    End Function

    Dim hwnd As IntPtr = New System.Windows.Interop.WindowInteropHelper(Me).Handle
    Private ReadOnly HWND_TOPMOST As IntPtr = New IntPtr(-1)
    Private ReadOnly HWND_NOTOPMOST As IntPtr = New IntPtr(-2)
    Private Const SWP_NOMOVE As UInteger = &H2
    Private Const SWP_NOSIZE As UInteger = &H1

    Public Sub SetWindowTopMost()
        Dim hWnd_ As IntPtr = hwnd
        SetWindowPos(hWnd_, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE Or SWP_NOSIZE)
    End Sub

    Public Sub SetWindowNotTopMost()
        Dim hWnd_ As IntPtr = hwnd
        SetWindowPos(hWnd_, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE Or SWP_NOSIZE)
    End Sub

#End Region

    '增加属性，方便访问
    Public Shared ReadOnly Property Instance() As FloatingWindow
        Get
            Return _instance
        End Get
    End Property

    Public Sub New()
        InitializeComponent()
        Width = 800
        Height = 50
        WindowStyle = WindowStyle.None
        AllowsTransparency = True
        Background = Brushes.Transparent
        Top = -Height
        Left = (SystemParameters.WorkArea.Width - Width) / 2
        WindowStartupLocation = WindowStartupLocation.Manual

        '初始化动画
        slideDown = New DoubleAnimation() With {
            .From = -Me.Height,
            .To = 0,
            .Duration = New Duration(TimeSpan.FromSeconds(0.5)),
            .EasingFunction = New CubicEase() With {.EasingMode = EasingMode.EaseOut}
        }

        slideUp = New DoubleAnimation() With {
            .From = 0,
            .To = -Me.Height,
            .Duration = New Duration(TimeSpan.FromSeconds(0.5)),
            .EasingFunction = New CubicEase() With {.EasingMode = EasingMode.EaseIn}
        }
        _instance = Me
    End Sub

    Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs)
        '窗体加载时播放弹出动画
        BeginAnimation(TopProperty, slideDown)
        '置托盘图标
        Dim trayIcon As New MyTrayicon
    End Sub

    Private Sub Window_Closing(sender As Object, e As System.ComponentModel.CancelEventArgs)
        If Not isClosing Then
            '取消默认关闭行为
            e.Cancel = True
            isClosing = True

            '播放缩回动画
            AddHandler slideUp.Completed, Sub()
                                              '动画完成后关闭窗体
                                              Close()
                                          End Sub
            BeginAnimation(TopProperty, slideUp)
        End If
        UnregisterGlobalHotkey()
    End Sub
    Protected Overrides Sub OnSourceInitialized(e As EventArgs)
        MyBase.OnSourceInitialized(e)

        '设置窗体样式，防止在ALT+TAB中显示
        Dim hwnd As IntPtr = New System.Windows.Interop.WindowInteropHelper(Me).Handle
        Dim exStyle As Integer = GetWindowLong(hwnd, GWL_EXSTYLE)
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle Or WS_EX_TOOLWINDOW And Not WS_EX_APPWINDOW)
        '设置消息过滤器
        Dim hwndSource As HwndSource = HwndSource.FromVisual(Me)
        hwndSource.AddHook(AddressOf WndProc)
    End Sub

    Private Sub Button_Click(sender As Object, e As RoutedEventArgs) '退出
        StopApp()
    End Sub

    Public Sub StopApp() '退出程序方法
        Close()
        Dim timer As New System.Timers.Timer(1000) '在悬浮窗缩回后0.5秒时退出
        AddHandler timer.Elapsed, AddressOf Timer_Elapsed
        timer.AutoReset = False
        timer.Enabled = True
        timer.Start()
    End Sub

    Private Sub Timer_Elapsed(sender As Object, e As ElapsedEventArgs)
        Environment.Exit(0)
    End Sub

    Private Sub StopButton_Click(sender As Object, e As RoutedEventArgs)
        StopActions()
    End Sub

    Private Sub StopActions() '停止连点连发等
        MainWindow1.Instance.StopClick()
        MainWindow1.Instance.Show()
        FloatingWindow_Reset()
    End Sub

    Private Sub Image_MouseUp(sender As Object, e As MouseButtonEventArgs) '显示主窗体
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Show()
        MainWindow1.Instance.Activate()
    End Sub

    Public Sub FloatingWindow_Reset()
        titleLabel.Content = "键鼠管家"
        stopButton.Visibility = Visibility.Hidden
        clickButton.Visibility = Visibility.Visible
        sendButton.Visibility = Visibility.Visible
    End Sub

    Public Sub FloatingWindowEvent_Click()
        titleLabel.Content = "键鼠管家-连点中"
        stopButton.Visibility = Visibility.Visible
        clickButton.Visibility = Visibility.Hidden
        sendButton.Visibility = Visibility.Hidden
        RegisterGlobalHotkey(New List(Of Byte) From {162, 120}, 9000)
    End Sub

    Private Sub ClickButton_Click(sender As Object, e As RoutedEventArgs) '连点
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Activate()
        MainWindow1.Instance.TabControl1.SelectedIndex = 1
    End Sub

    Private Sub SendButton_Click(sender As Object, e As RoutedEventArgs) '连发
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Show()
        MainWindow1.Instance.Activate()
        MainWindow1.Instance.TabControl1.SelectedIndex = 2
    End Sub


End Class