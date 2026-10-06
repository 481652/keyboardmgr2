'悬浮窗代码及快捷键注册与处理
Imports System.Runtime.InteropServices
Imports System.Timers
Imports System.Windows.Forms
Imports System.Windows.Interop
Imports System.Windows.Media.Animation

#Const WIDTH = 800
#Const HEIGHT = 50


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
    Private Const WS_EX_NOACTIVATE As Integer = &H8000000

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativePoint
        Public X As Integer
        Public Y As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure NativeRect
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <DllImport("user32.dll")>
    Private Shared Function GetCursorPos(ByRef point As NativePoint) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowRect(hwnd As IntPtr, ByRef rect As NativeRect) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetWindowPos(hwnd As IntPtr, insertAfter As IntPtr, x As Integer, y As Integer,
                                         width As Integer, height As Integer, flags As UInteger) As Boolean
    End Function

    Private Const SWP_NOSIZE As UInteger = &H1
    Private Const SWP_NOZORDER As UInteger = &H4
    Private Const SWP_NOACTIVATE As UInteger = &H10

#End Region


#Region "GlobalHotkey"
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
                Case 9001 '摸鱼
                    LoafModule.ToggleLoafMode()
                Case 9002 '连点开关（热键按下时切换连点启动/停止）
                    MainWindow1.Instance.ToggleClick()
                Case 9003 '连发开关（热键按下时切换连发启动/停止）
                    MainWindow1.Instance.ToggleSend()
                Case 9004 '显示/隐藏主界面
                    MainWindow1.Instance.ToggleMainWindowVisibility()
                Case 9005 '录制开关（热键按下时切换录制开始/停止）
                    MainWindow1.Instance.ToggleRecording()
                Case Else
            End Select
        End If

        Return IntPtr.Zero
    End Function

    '托盘图标实例，作为字段持有以便退出时释放、避免托盘残留幽灵图标
    Private trayIcon As MyTrayicon
    Private hotkeynum As Integer = 0
    Private Const TEMP_TEST_HOTKEY_ID As Integer = 9999 '临时测试用ID，不与9000-9004冲突

    '修饰符常量
    Private Const MOD_ALT As Integer = &H1
    Private Const MOD_CONTROL As Integer = &H2
    Private Const MOD_SHIFT As Integer = &H4
    Private Const MOD_WIN As Integer = &H8

    '将热键字节列表解析为(修饰符, 虚拟键码)，解析失败返回 Nothing
    '支持任意数量的修饰键(0~4个)+单个主键，最多5个键
    Private Function ParseHotkeyBytes(hotkey As List(Of Byte)) As Tuple(Of Integer, Integer)
        If hotkey Is Nothing OrElse hotkey.Count = 0 OrElse hotkey.Count > 5 Then Return Nothing
        '验证所有虚拟键码在合法范围内
        For Each vk In hotkey
            If vk < 1 OrElse vk > 254 Then Return Nothing
        Next
        Dim modifier As Integer = 0
        Dim vkCode As Integer = 0
        For Each vk In hotkey
            Select Case vk
                'Win 键（左右通用）
                Case ConvertKeyToVirtualKeyCode(Key.LWin), ConvertKeyToVirtualKeyCode(Key.RWin)
                    modifier = modifier Or MOD_WIN
                'Ctrl 键（左右分开处理）
                Case ConvertKeyToVirtualKeyCode(Key.LeftCtrl), ConvertKeyToVirtualKeyCode(Key.RightCtrl)
                    modifier = modifier Or MOD_CONTROL
                'Alt 键（左右分开处理）
                Case ConvertKeyToVirtualKeyCode(Key.LeftAlt), ConvertKeyToVirtualKeyCode(Key.RightAlt)
                    modifier = modifier Or MOD_ALT
                'Shift 键（左右通用）
                Case ConvertKeyToVirtualKeyCode(Key.LeftShift), ConvertKeyToVirtualKeyCode(Key.RightShift)
                    modifier = modifier Or MOD_SHIFT
                Case Else
                    '非修饰键 → 主键（保留最后一个，以防有多个）
                    vkCode = vk
            End Select
        Next
        '必须有至少一个主键
        If vkCode = 0 Then Return Nothing
        Return Tuple.Create(modifier, vkCode)
    End Function

    '测试热键是否可被注册（即是否与其他软件冲突），返回 True 表示可用
    Public Function TestHotkeyAvailability(hotkey As List(Of Byte)) As Boolean
        Dim parsed = ParseHotkeyBytes(hotkey)
        If parsed Is Nothing Then Return False
        Dim hwnd As IntPtr = New WindowInteropHelper(Me).Handle
        '尝试注册到临时ID，成功则立即注销
        Dim success As Boolean = RegisterHotKey(hwnd, TEMP_TEST_HOTKEY_ID, parsed.Item1, parsed.Item2)
        If success Then
            UnregisterHotKey(hwnd, TEMP_TEST_HOTKEY_ID)
            Return True
        End If
        Return False
    End Function

    Public Sub RegisterGlobalHotkey(hotkey As List(Of Byte), hotkeyid As Integer) '注册全局热键，ID范围为9000-9004。
        '空列表静默跳过（不在启动时弹错误框）
        If hotkey Is Nothing OrElse hotkey.Count = 0 Then Return
        '验证快捷键合法性
        Dim parsed = ParseHotkeyBytes(hotkey)
        If parsed Is Nothing Then
            ShowMyMessage("快捷键非法，请重新设置")
            Return
        End If
        Try
            Dim hwnd As IntPtr = New WindowInteropHelper(Me).Handle
            '先注销可能已注册的同ID热键（避免重复注册时误判为冲突）
            UnregisterHotKey(hwnd, hotkeyid)
            '注册热键
            Dim success As Boolean = RegisterHotKey(hwnd, hotkeyid, parsed.Item1, parsed.Item2)
            If Not success Then
                '注册失败，热键已被其他软件占用
                Dim errCode As Integer = Marshal.GetLastWin32Error()
                ShowMyMessage("无法注册快捷键：该快捷键已被其他程序占用（系统错误码：" & errCode & "），请更换快捷键后重试。")
                Return
            End If
            hotkeynum += 1
        Catch ex As Exception
            StopActions() '先停止操作
            ShowExpdlg("错误8：程序无法注册快捷键，可能是快捷键非法，请更换快捷键。", ex.Message & vbLf & ex.StackTrace)
        End Try
    End Sub

    '注销单个热键（按ID），用于功能关闭/切换时即时解除注册
    Public Sub UnregisterSingleHotkey(hotkeyid As Integer)
        Dim hwnd As IntPtr = New WindowInteropHelper(Me).Handle
        UnregisterHotKey(hwnd, hotkeyid)
        If hotkeynum > 0 Then hotkeynum -= 1
    End Sub

    Public Sub UnregisterGlobalHotkey() '注销所有全局热键
        '注销所有可能已注册的热键ID（9000-9005）
        For i As Integer = 9000 To 9005
            UnregisterHotKey(New WindowInteropHelper(Me).Handle, i)
        Next
        hotkeynum = 0
    End Sub

#End Region

#Region "Topmost"
    '置顶当前窗体

    Public Sub SetWindowTopMost()
        Topmost = True
        JellyBounce(-2, 250, 3)
    End Sub

    Public Sub SetWindowNotTopMost()
        Topmost = False
        JellyBounce(-2, 250, 3)
    End Sub

#End Region

    '增加属性，方便访问
    Public Shared ReadOnly Property Instance() As FloatingWindow
        Get
            Return _instance
        End Get
    End Property

    Public Sub Unfold()
        CancelFoldTimer()
        If FloatingWindowState = 0 Then Return
        If Not IsVisible Then Show()
        MoveTo(0, False,
            Sub()
                Dispatcher.BeginInvoke(New Action(
                    Sub()
                        If FloatingWindowState = 2 AndAlso Not IsPointerOverWindow() Then ScheduleFold()
                    End Sub))
            End Sub)
    End Sub

    Public Sub ApplyDisplayMode(mode As Byte)
        FloatingWindowState = mode
        CancelFoldTimer()
        If mode = 2 Then
            pointerPollTimer.Change(0, 100)
        Else
            pointerPollTimer.Change(Threading.Timeout.Infinite, Threading.Timeout.Infinite)
        End If
        Select Case mode
            Case 0
                animationVersion += 1
                BeginAnimation(TopProperty, Nothing)
                Top = FoldedTop
                foldTargeted = True
                isFloatingWindowFolded = True
                Hide()
            Case 1
                If Not IsVisible Then Show()
                MoveTo(0, False)
            Case 2
                If Not IsVisible Then Show()
                MoveTo(0, False,
                    Sub()
                        Dispatcher.BeginInvoke(New Action(
                            Sub()
                                If FloatingWindowState = 2 AndAlso Not IsPointerOverWindow() Then ScheduleFold()
                            End Sub))
                    End Sub)
        End Select
    End Sub

#Region "Animation"

    Private isClosing As Boolean = False
    Private animationVersion As Integer = 0
    Private foldTargeted As Boolean = True
    Private pointerPollTimer As Threading.Timer
    Private foldDelayTimer As Threading.Timer
    Private floatingHwnd As IntPtr = IntPtr.Zero
    Private pointerWasOver As Boolean = False
    Private isJellyBouncePlaying As Boolean = False

    Private ReadOnly Property FoldedTop As Double
        Get
            Dim windowHeight As Double = If(ActualHeight > 0, ActualHeight, Height)
            Return -windowHeight + 8
        End Get
    End Property

    Public Sub New()
        InitializeComponent()
        pointerPollTimer = New Threading.Timer(AddressOf PointerPollTimer_Tick, Nothing, Threading.Timeout.Infinite, Threading.Timeout.Infinite)
        Width = 800
        Height = 50
        WindowStyle = WindowStyle.None
        AllowsTransparency = False
        Top = FoldedTop
        Left = (SystemParameters.WorkArea.Width - Width) / 2
        WindowStartupLocation = WindowStartupLocation.Manual
        _instance = Me
    End Sub

    Private Sub MoveTo(targetTop As Double, folded As Boolean, Optional completed As Action = Nothing, Optional delay As TimeSpan = Nothing)
        Dim currentTop As Double = Top
        foldTargeted = folded
        animationVersion += 1
        Dim currentVersion As Integer = animationVersion
        BeginAnimation(TopProperty, Nothing)
        Top = currentTop

        If Math.Abs(currentTop - targetTop) < 0.5 AndAlso delay = TimeSpan.Zero Then
            Top = targetTop
            isFloatingWindowFolded = folded
            completed?.Invoke()
            Return
        End If

        Dim animation As New DoubleAnimation() With {
            .From = currentTop,
            .To = targetTop,
            .Duration = New Duration(TimeSpan.FromMilliseconds(350)),
            .BeginTime = delay,
            .FillBehavior = FillBehavior.Stop,
            .EasingFunction = New CubicEase() With {.EasingMode = EasingMode.EaseInOut}
        }
        AddHandler animation.Completed,
            Sub()
                If currentVersion <> animationVersion Then Return
                BeginAnimation(TopProperty, Nothing)
                Top = targetTop
                isFloatingWindowFolded = folded
                completed?.Invoke()
            End Sub
        BeginAnimation(TopProperty, animation)
    End Sub

    Public Sub JellyBounce(wSqeezePercent As Double, duration As Double, springiness As Integer)
        If isJellyBouncePlaying Then Return
        isJellyBouncePlaying = True

        Dim center As Double = ActualHeight + ActualWidth / 2

        Dim rStart As Double = ActualWidth
        Dim lStart As Double = Left
        Dim targetR As Double = rStart * (1 + wSqeezePercent / 100)
        Dim targetL As Double = lStart - (targetR - rStart) / 2

        Dim timeSpan As TimeSpan = TimeSpan.FromMilliseconds(duration)

        Dim easeR As New DoubleAnimation(rStart, targetR, timeSpan) With {
            .EasingFunction = New BackEase With {
                .EasingMode = EasingMode.EaseIn
            }
        }

        Dim easeBackR As New DoubleAnimation(targetR, rStart, timeSpan) With {
            .EasingFunction = New BackEase With {
                .EasingMode = EasingMode.EaseOut
            }
        }

        Dim easeL As New DoubleAnimation(lStart, targetL, timeSpan) With {
            .EasingFunction = New BackEase With {
                .EasingMode = EasingMode.EaseIn
            }
        }

        Dim easeBackL As New DoubleAnimation(targetL, lStart, timeSpan) With {
            .EasingFunction = New BackEase With {
                .EasingMode = EasingMode.EaseOut
            }
        }

        AddHandler easeR.Completed, Sub()
                                        BeginAnimation(WidthProperty, easeBackR)
                                        BeginAnimation(LeftProperty, easeBackL)
                                    End Sub

        AddHandler easeBackR.Completed, Sub()
                                            BeginAnimation(WidthProperty, Nothing)
                                            BeginAnimation(LeftProperty, Nothing)
                                            Width = 800 ' IDE常量打不出来，这大小写检查没力气
                                            Height = 50 ' 手动标一下当WIDTH, HEIGHT使了
                                            isJellyBouncePlaying = False
                                        End Sub
        BeginAnimation(WidthProperty, easeR)
        BeginAnimation(LeftProperty, easeL)
    End Sub
#End Region

#Region "Detecting mouse"
    Private Sub CancelFoldTimer()
        foldDelayTimer?.Dispose()
        foldDelayTimer = Nothing
        'animationVersion += 1
        'Dim currentTop As Double = Top
        'BeginAnimation(TopProperty, Nothing)
        'Top = currentTop
    End Sub

    Private Sub ScheduleFold()
        If FloatingWindowState <> 2 OrElse IsPointerOverWindow() Then Return
        foldDelayTimer?.Dispose()
        foldDelayTimer = New Threading.Timer(
            Sub()
                If floatingHwnd = IntPtr.Zero OrElse IsPointerOverWindow() Then Return
                Dim rect As NativeRect
                If GetWindowRect(floatingHwnd, rect) Then
                    'Dim foldedY As Integer = -(rect.Bottom - rect.Top) + 8
                    'SetWindowPos(floatingHwnd, IntPtr.Zero, rect.Left, foldedY, 0, 0, SWP_NOSIZE Or SWP_NOZORDER Or SWP_NOACTIVATE)
                    foldTargeted = True
                    Dispatcher.BeginInvoke(New Action(
                        Sub()
                            'animationVersion += 1
                            'BeginAnimation(TopProperty, Nothing)
                            'Top = FoldedTop
                            'isFloatingWindowFolded = True
                            MoveTo(-40, True)
                        End Sub))
                End If
            End Sub, Nothing, TimeSpan.FromSeconds(3), Threading.Timeout.InfiniteTimeSpan)
    End Sub

    Private Sub PointerPollTimer_Tick(state As Object)
        If floatingHwnd = IntPtr.Zero Then Return
        Dim pointerOver As Boolean = IsPointerOverWindow()
        If pointerOver = pointerWasOver Then Return
        pointerWasOver = pointerOver

        If Not pointerOver Then
            Dispatcher.BeginInvoke(New Action(
                Sub()
                    If FloatingWindowState = 2 AndAlso Not foldTargeted Then ScheduleFold()
                End Sub))
            Return
        End If

        Dispatcher.BeginInvoke(New Action(
            Sub()
                If FloatingWindowState = 2 Then
                    MoveTo(0, False)
                End If
            End Sub))
    End Sub

    Private Function IsPointerOverWindow() As Boolean
        Dim point As NativePoint
        Dim rect As NativeRect
        If floatingHwnd = IntPtr.Zero OrElse Not GetCursorPos(point) OrElse Not GetWindowRect(floatingHwnd, rect) Then Return False
        Return point.X >= rect.Left AndAlso point.X < rect.Right AndAlso point.Y >= rect.Top AndAlso point.Y < rect.Bottom
    End Function

    Private Sub Window_MouseEnter(sender As Object, e As Input.MouseEventArgs)
        If FloatingWindowState = 2 Then
            CancelFoldTimer()
        End If
    End Sub

    Private Sub Window_MouseLeave(sender As Object, e As Input.MouseEventArgs)
        '鼠标离开窗体
        If FloatingWindowState = 2 Then
            ScheduleFold()
        End If
    End Sub
#End Region


    Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs)
        '置托盘图标（作为字段持有，退出时 Dispose 防止托盘残留幽灵图标）
        trayIcon = New MyTrayicon

        ApplyDisplayMode(FloatingWindowState)
        If ReadSetting("IsFloatingWinTopmost", 0) = 1 Then
            MainWindow1.Instance.DoFloatingWindowTopmost.IsChecked = True
            FloatingWindow.Instance.SetWindowTopMost()
        Else
            MainWindow1.Instance.DoFloatingWindowTopmost.IsChecked = False
            FloatingWindow.Instance.SetWindowNotTopMost()
        End If
        ThemeModule.ApplyWindowBackdrop(Me, AnimatedBorder, isMicaEnabled AndAlso ThemeModule.IsWindows11_22H2OrLater(), True)
    End Sub

    Private Sub Window_Closing(sender As Object, e As System.ComponentModel.CancelEventArgs)
        If Not isClosing Then
            '取消默认关闭行为
            e.Cancel = True
            isClosing = True

            CancelFoldTimer()
            MoveTo(-Height, True, Sub() Close())
        End If
        UnregisterGlobalHotkey()
    End Sub
    Protected Overrides Sub OnSourceInitialized(e As EventArgs)
        MyBase.OnSourceInitialized(e)

        '设置窗体样式，防止在ALT+TAB中显示
        Dim hwnd As IntPtr = New System.Windows.Interop.WindowInteropHelper(Me).Handle
        floatingHwnd = hwnd
        Dim exStyle As Integer = GetWindowLong(hwnd, GWL_EXSTYLE)
        SetWindowLong(hwnd, GWL_EXSTYLE, (exStyle Or WS_EX_TOOLWINDOW Or WS_EX_NOACTIVATE) And Not WS_EX_APPWINDOW)
        '设置消息过滤器
        Dim hwndSource As HwndSource = HwndSource.FromVisual(Me)
        hwndSource.AddHook(AddressOf WndProc)
    End Sub

    Private Sub Button_Click(sender As Object, e As RoutedEventArgs) '退出
        StopApp()
    End Sub

    Public Sub StopApp() '退出程序方法
        If LoafModule.InLoafMode Then LoafModule.ExitLoafMode()
        Close()
        Dim timer As New System.Timers.Timer(1000) '在悬浮窗缩回后0.5秒时退出
        AddHandler timer.Elapsed, AddressOf Timer_Elapsed
        timer.AutoReset = False
        timer.Enabled = True
        timer.Start()
    End Sub

    Private Sub Timer_Elapsed(sender As Object, e As ElapsedEventArgs)
        trayIcon?.Dispose()
        Environment.Exit(0)
    End Sub

    Private Sub StopButton_Click(sender As Object, e As RoutedEventArgs)
        StopActions()
    End Sub

    Private Sub StopActions() '停止连点连发等
        If LoafModule.InLoafMode Then LoafModule.ExitLoafMode()
        MainWindow1.Instance.StopClick()
        MainWindow1.Instance.StopSend()
        MainWindow1.Instance.StopRecordingAndSave()
        MacroPlayer.StopPlayback()
        ScriptRunner.StopScript()
        ScriptHost.ForceUnblockInput()
        MainWindow1.Instance.Show()
        FloatingWindow_Reset()
    End Sub

    Private Sub Image_MouseUp(sender As Object, e As MouseButtonEventArgs) '显示主窗体
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Show()
        MainWindow1.Instance.Activate()
        Dim hWnd As IntPtr = New WindowInteropHelper(Me).Handle
    End Sub

    Public Sub FloatingWindow_Reset()
        titleLabel.Content = "键鼠管家"
        stopButton.Visibility = Visibility.Hidden
        clickButton.Visibility = Visibility.Visible
        sendButton.Visibility = Visibility.Visible
    End Sub

    Public Sub FloatingWindowEvent_Send()
        titleLabel.Content = "键鼠管家-连发中"
        stopButton.Visibility = Visibility.Visible
        clickButton.Visibility = Visibility.Hidden
        sendButton.Visibility = Visibility.Hidden
        If FloatingWindowState <> 0 Then
            If Not IsVisible Then Show()
            Unfold()
        End If
    End Sub

    Public Sub FloatingWindowEvent_Click()
        titleLabel.Content = "键鼠管家-连点中"
        stopButton.Visibility = Visibility.Visible
        clickButton.Visibility = Visibility.Hidden
        sendButton.Visibility = Visibility.Hidden
        If FloatingWindowState <> 0 Then Unfold()
        RegisterGlobalHotkey(stopActHotkeys, 9000)
    End Sub

    Private Sub ClickButton_Click(sender As Object, e As RoutedEventArgs) '连点
        MainWindow1.Instance.ShowInTaskbar = True
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Activate()
        MainWindow1.Instance.TabControl1.SelectedIndex = 1
    End Sub

    Private Sub SendButton_Click(sender As Object, e As RoutedEventArgs) '连发
        MainWindow1.Instance.ShowInTaskbar = True
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Show()
        MainWindow1.Instance.Activate()
        MainWindow1.Instance.TabControl1.SelectedIndex = 2
    End Sub

    Private Sub RecordButton_Click(sender As Object, e As RoutedEventArgs) '录制
        MainWindow1.Instance.ShowInTaskbar = True
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Show()
        MainWindow1.Instance.Activate()
        MainWindow1.Instance.TabControl1.SelectedIndex = 4
    End Sub

    Private Sub ScriptButton_Click(sender As Object, e As RoutedEventArgs) '脚本
        MainWindow1.Instance.ShowInTaskbar = True
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Show()
        MainWindow1.Instance.Activate()
        MainWindow1.Instance.TabControl1.SelectedIndex = 5
    End Sub
End Class
