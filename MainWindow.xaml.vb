'主窗体代码
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net.Http
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Timers
Imports System.Windows.Interop
Imports System.Windows.Media
Imports System.Windows.Media.Animation
Imports System.Windows.Threading
Imports Microsoft.Win32
Imports WindowSelector
Imports Timer = System.Timers.Timer



Public Class MainWindow1

#Region "DllImports&Veriables"

    'git id
    Public Const id As String = "10df9b1b"
    Private floatingWindow As New FloatingWindow
    Private Shared _instance As MainWindow1
    '移除最大化按钮
    <DllImport("user32.dll", SetLastError:=True)>
    Private Shared Function SetWindowLong(hWnd As IntPtr, nIndex As Integer, dwNewLong As Integer) As Integer
    End Function
    Private Const GWL_STYLE As Integer = -16
    Private Const WS_MAXIMIZEBOX As Integer = &H10000 '最大化按钮的样式

    <DllImport("user32.dll")>
    Private Shared Function GetWindowLong(hWnd As IntPtr, nIndex As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetForegroundWindow() As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Shared Function SetForegroundWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function IsWindow(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function GetWindowThreadProcessId(hWnd As IntPtr, ByRef processId As UInteger) As UInteger
    End Function

    Dim savedkeys As New List(Of Key)

#End Region

#Region "InitializeProgram"

    Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs)
        '判断系统版本是否低于win10 1809以防bug
        Dim osVersion As Version = Environment.OSVersion.Version
        If osVersion.Major < 10 OrElse (osVersion.Major = 10 AndAlso osVersion.Build < 17763) Then
            Hide()
            ShowExpdlg("错误1：本程序不支持当前操作系统。要使用本程序，请使用Windows 10 1809或更高版本。", "")
        End If
        '判断是否为测试版
        If My.Application.Info.Version.Revision <> 0 Then
            ProductName.Text = "键鼠管家测试版"
            Title = "键鼠管家测试版"
            WelcomeText.Content = "欢迎参与键鼠管家测试版的测试！"
            TestTip.Visibility = Visibility.Visible
        End If
        If ReadSetting("isSettingsReady", "0") = 0 Then
            '如果没有设置，则创建默认设置
            CreateDefaultSettings()
        End If
        Try
            InitializeSettings()
        Catch ex As Exception
            Hide()
            ShowExpdlg("错误6：程序在初始化时读取设置出现问题，请尝试删除所有位于HKEY_CURRENT_USER\SOFTWARE\LCS\keyboardmgr的设置，如仍不能解决问题，请联系LCS。", ex.Message & vbLf & ex.StackTrace)
        End Try
        ThemeModule.ApplyWindowBackdrop(Me, RootGrid, ThemeModule.isMicaEnabled AndAlso ThemeModule.IsWindows11_22H2OrLater())
        VerLabel.Text = "版本号：" & My.Application.Info.Version.Major & "." & My.Application.Info.Version.Minor & "." & My.Application.Info.Version.Build & "." & My.Application.Info.Version.Revision
        GitID.Text = "Git ID：" & id
        '首次显示用于创建 HWND、消息钩子和托盘图标，再应用实际显示模式
        floatingWindow.Show()
        floatingWindow.ApplyDisplayMode(FloatingWindowState)
        '在悬浮窗 HWND 和消息钩子创建后注册热键
        RegisterAllHotkeys()
        Pinicon_Set()
        '移除最大化按钮
        Dim hwnd As IntPtr = New WindowInteropHelper(Me).Handle
        Dim style As Integer = GetWindowLong(hwnd, GWL_STYLE)
        SetWindowLong(hwnd, GWL_STYLE, style And Not WS_MAXIMIZEBOX)
    End Sub

    Public Sub HandleExternalRequest(filePath As String)
        ShowInTaskbar = True
        Show()
        WindowState = WindowState.Normal
        Activate()
        SetForegroundWindow(New WindowInteropHelper(Me).Handle)
        If String.IsNullOrEmpty(filePath) Then
            ApplyStartupTabSelection()
            Return
        End If
        TabControl1.SelectedIndex = 2
        OpenRapidFirePreset(filePath, False)
    End Sub

    Private Shared Sub CreateDefaultSettings()
        WriteSetting("isSettingsReady", 1)
        WriteSetting("DoAutoSwitchTheme", 1)
        WriteSetting("IsDarkMode", 0)
        WriteSetting("DoAutoStart", 0)
        WriteSetting("FloatingWinShowState", 1)
        WriteSetting("DoRandomOffsetOfClickSpeed", 0)
        WriteSetting("DoCustomizeCursorPos", 0)
        WriteSetting("ClickInterval", "10")
        WriteSetting("ClickMode", "LeftClick")
        WriteSetting("ClickKeys", "")
        WriteSetting("StopActHotkeys", "Ctrl+G")
        WriteSetting("StartupTabIndex", 0)
    End Sub

    Private Sub InitializeSettings()
        '检测设置并初始化控件状态
        InitializeThemeSettings()
        InitializeFloatingWindowSettings()
        InitializeLoafSettings()
        InitializeClickSettings()
        InitializeHotkeySettings()
        UpdateItemDisplay()
        ApplyStartupTabSelection()
        If ReadSetting("DoAutoStart", 0) = 1 Then
            DoAutoStartCheckbox.IsChecked = True
            Try
                UpdateAutoStartRegistration(True)
            Catch
                '旧版自启项迁移失败不应阻止程序启动，用户应用设置时会看到错误。
            End Try
        Else
            DoAutoStartCheckbox.IsChecked = False
        End If
    End Sub

    Private Sub ApplyStartupTabSelection()
        Dim startupTabIndex As Integer
        If Not Integer.TryParse(ReadSetting("StartupTabIndex", 0).ToString(), startupTabIndex) OrElse startupTabIndex < 0 OrElse startupTabIndex >= TabControl1.Items.Count Then startupTabIndex = 0
        StartupTabComboBox.SelectedIndex = startupTabIndex
        TabControl1.SelectedIndex = startupTabIndex
    End Sub

    Private Sub InitializeThemeSettings()
        ThemeModule.isMicaEnabled = ReadSetting("IsMicaEnabled", 0) = 1 AndAlso ThemeModule.IsWindows11_22H2OrLater()
        IsMicaEnabledCheckbox.IsChecked = ThemeModule.isMicaEnabled
        Select Case ReadSetting("DoAutoSwitchTheme", 1)
            Case 1
                Dim isDarkMode As Boolean = IsDarkModeEnabled()
                SwitchTheme(isDarkMode)
                Combobox1.SelectedIndex = 0
            Case 0
                If ReadSetting("IsDarkMode", 0) = 0 Then
                    SwitchTheme(False)
                    Combobox1.SelectedIndex = 1
                Else
                    SwitchTheme(True)
                    Combobox1.SelectedIndex = 2
                End If
        End Select
    End Sub

    Private Sub InitializeFloatingWindowSettings()
        '加载悬浮窗状态
        Select Case ReadSetting("FloatingWinShowState", 1)
            Case 0 '始终隐藏
                FloatingWindowState = 0
                Combobox2.SelectedIndex = 1
            Case 1 '始终显示
                FloatingWindowState = 1
                Combobox2.SelectedIndex = 0
            Case 2 '自动收缩
                FloatingWindowState = 2
                Combobox2.SelectedIndex = 2
            Case Else
                ShowMyMessage("加载悬浮窗状态时失败！")
        End Select
    End Sub

    Private Sub InitializeLoafSettings()
        '加载摸鱼设置
        If ReadSetting("IsLoafEnabled", 0) = 1 Then
            LoafToggle.IsChecked = True
            isLoafEnabled = True
            LoafGrid.Visibility = Visibility.Visible
        Else
            LoafToggle.IsChecked = False
            isLoafEnabled = False
            LoafGrid.Visibility = Visibility.Hidden
        End If
        '加载已保存的工作窗口
        LoafModule.LoadWorkWindow()
        If LoafModule.WorkWindowHwnd <> IntPtr.Zero Then
            Dim savedTitle As String = LoafModule.GetSavedWorkWindowTitle()
            If Not String.IsNullOrEmpty(savedTitle) Then
                SelectedWindowHwnd.Content = "选取的窗体：" & savedTitle
            Else
                SelectedWindowHwnd.Content = "选取的窗体句柄：" & LoafModule.WorkWindowHwnd.ToString()
            End If
        End If
    End Sub

    Private Sub InitializeClickSettings()
        '加载连点设置
        Textbox2.Visibility = Visibility.Hidden
        If ReadSetting("DoClickSettingSaved", 0) = 1 Then
            If ReadSetting("ClickInterval", "") = "" Then
                Return
            End If
            Textbox1.Text = ReadSetting("ClickInterval", "")
            CheckBox1.IsChecked = ReadSetting("DoRandomOffsetOfClickSpeed", 0) = 1
            CheckBox2.IsChecked = ReadSetting("DoRandomOffsetOfClickPosition", 0) = 1
            CheckBox3.IsChecked = ReadSetting("DoCustomizeCursorPos", 0) = 1
            Textbox2.Visibility = If(CheckBox3.IsChecked, Visibility.Visible, Visibility.Hidden)
            Textbox2.Text = ReadSetting("CursorPosition", "")
            Select Case ReadSetting("ClickMode", "LeftClick")
                Case "LeftClick"
                    RadioButton1.IsChecked = True
                Case "RightClick"
                    RadioButton2.IsChecked = True
                Case "KeyboardClick"
                    RadioButton3.IsChecked = True
                Case Else
                    ShowMyMessage("加载连点模式时失败！")
            End Select
            KeyTextbox1.Text = ReadSetting("ClickKeys", "")
            '加载自定义键连点键值
            Dim ClickKeys_str = ReadSetting("ClickKeys", "")
            savedkeys = LoadKeyData(ClickKeys_str)
            If IsNoneKeyList(savedkeys) Then
                ShowMyMessage("无法加载连点设置。")
            End If
        End If
    End Sub

    Private Sub InitializeHotkeySettings()
        '加载快捷键设置
        Dim reservedHotkeyReplaced As Boolean = False
        Dim StopActHotkeys_str As String = ReplaceReservedHotkey("StopActHotkeys", "Ctrl+G", reservedHotkeyReplaced)
        If StopActHotkeys_str <> "" Then
            KeyTextbox3.Text = StopActHotkeys_str
            Dim keyList = LoadKeyData(StopActHotkeys_str)
            stopActHotkeys = ConvertKeyLogToVirtualKeyCodes(keyList)
            If IsNoneKeyList(keyList) Then
                ShowMyMessage("无法加载快捷键设置。")
            End If
        Else
            WriteSetting("StopActHotkeys", "Ctrl+G")
        End If
        '加载摸鱼快捷键设置（始终加载，只要热键非空，与 IsLoafEnabled 无关）
        Dim loafHotkeys_str As String = ReplaceReservedHotkey("LoafHotkeys", "Shift+Z", reservedHotkeyReplaced)
        If loafHotkeys_str <> "" Then
            KeyTextbox2.Text = loafHotkeys_str
            Dim keyList = LoadKeyData(loafHotkeys_str)
            loafHotkeys = ConvertKeyLogToVirtualKeyCodes(keyList)
            If IsNoneKeyList(keyList) Then
                ShowMyMessage("无法加载摸鱼快捷键设置。")
            End If
        Else
            WriteSetting("LoafHotkeys", "Shift+Z")
        End If
        '加载连点开关热键
        Dim clickHotkeyStr As String = ReplaceReservedHotkey("StopClickHotkeys", "Ctrl+F1", reservedHotkeyReplaced)
        If clickHotkeyStr <> "" Then
            KeyTextbox4.Text = clickHotkeyStr
            Dim keyList = LoadKeyData(clickHotkeyStr)
            stopClickHotkeys = ConvertKeyLogToVirtualKeyCodes(keyList)
            If IsNoneKeyList(keyList) Then
                ShowMyMessage("无法加载连点快捷键设置。")
            End If
        Else
            clickHotkeyStr = "Ctrl+F1"
            WriteSetting("StopClickHotkeys", clickHotkeyStr)
            KeyTextbox4.Text = clickHotkeyStr
            stopClickHotkeys = ConvertKeyLogToVirtualKeyCodes(LoadKeyData(clickHotkeyStr))
        End If
        '加载连发开关热键
        Dim sendHotkeyStr As String = ReplaceReservedHotkey("RapidFireHotkeys", "Ctrl+F2", reservedHotkeyReplaced)
        If sendHotkeyStr <> "" Then
            KeyTextbox5.Text = sendHotkeyStr
            Dim keyList = LoadKeyData(sendHotkeyStr)
            rapidFireHotkeys = ConvertKeyLogToVirtualKeyCodes(keyList)
            If IsNoneKeyList(keyList) Then
                ShowMyMessage("无法加载连发快捷键设置。")
            End If
        Else
            sendHotkeyStr = "Ctrl+F2"
            WriteSetting("RapidFireHotkeys", sendHotkeyStr)
            KeyTextbox5.Text = sendHotkeyStr
            rapidFireHotkeys = ConvertKeyLogToVirtualKeyCodes(LoadKeyData(sendHotkeyStr))
        End If
        '迁移旧的预留热键3，并作为主界面显示/隐藏热键。
        Dim toggleMainStr As String = ReadSetting("ToggleMainWindowHotkeys", "").ToString()
        If toggleMainStr = "" Then toggleMainStr = ReadSetting("CustomHotkeys3", "Ctrl+F3").ToString()
        If toggleMainStr = "" Then toggleMainStr = "Ctrl+F3"
        If IsReservedSystemHotkey(toggleMainStr) Then
            toggleMainStr = "Ctrl+F3"
            reservedHotkeyReplaced = True
        End If
        WriteSetting("ToggleMainWindowHotkeys", toggleMainStr)
        DeleteSetting("CustomHotkeys3")
        DeleteSetting("CustomHotkeys4")
        If toggleMainStr <> "" Then
            KeyTextbox6.Text = toggleMainStr
            Dim keyList = LoadKeyData(toggleMainStr)
            toggleMainWindowHotkeys = ConvertKeyLogToVirtualKeyCodes(keyList)
            If IsNoneKeyList(keyList) Then
                ShowMyMessage("无法加载主界面显示快捷键设置。")
            End If
        End If
        If reservedHotkeyReplaced Then
            ShowMyMessage("检测到设置中包含系统常用快捷键，已自动恢复为对应的默认快捷键。")
        End If
    End Sub

    '在 InitializeSettings 之后调用，统一注册所有全局热键
    Private Sub RegisterAllHotkeys()
        '注册终止任务热键
        If stopActHotkeys.Count > 0 Then
            floatingWindow.RegisterGlobalHotkey(stopActHotkeys, 9000)
        End If
        '注册摸鱼热键
        If isLoafEnabled AndAlso loafHotkeys.Count > 0 Then
            floatingWindow.RegisterGlobalHotkey(loafHotkeys, 9001)
        End If
        '注册连点开关热键
        If stopClickHotkeys.Count > 0 Then
            floatingWindow.RegisterGlobalHotkey(stopClickHotkeys, 9002)
        End If
        '注册连发开关热键
        If rapidFireHotkeys.Count > 0 Then
            floatingWindow.RegisterGlobalHotkey(rapidFireHotkeys, 9003)
        End If
        If toggleMainWindowHotkeys.Count > 0 Then
            floatingWindow.RegisterGlobalHotkey(toggleMainWindowHotkeys, 9004)
        End If
    End Sub

    Public Sub Pinicon_Set()
        pinButton.SetResourceReference(ContentControl.ContentProperty, "Icon.Pin")
    End Sub

    Private Sub TabControl1_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If e.Source IsNot TabControl1 OrElse Not IsLoaded OrElse Not SystemParameters.ClientAreaAnimation Then Return

        Dim selectedTab = TryCast(TabControl1.SelectedItem, TabItem)
        Dim page = If(selectedTab Is Nothing, Nothing, TryCast(selectedTab.Content, FrameworkElement))
        Dim previousTab = If(e.RemovedItems.Count > 0, TryCast(e.RemovedItems(0), TabItem), Nothing)
        If page Is Nothing OrElse previousTab Is Nothing Then Return

        Dim duration = New Duration(TimeSpan.FromMilliseconds(280))
        Dim easing = New CubicEase() With {.EasingMode = EasingMode.EaseOut}
        Dim previousIndex = TabControl1.Items.IndexOf(previousTab)
        Dim offset = If(TabControl1.SelectedIndex > previousIndex, 40.0, -40.0)
        Dim translate = TryCast(page.RenderTransform, TranslateTransform)
        If translate Is Nothing Then
            translate = New TranslateTransform()
            page.RenderTransform = translate
        End If

        page.Opacity = 1
        translate.X = 0
        page.BeginAnimation(OpacityProperty, New DoubleAnimation(0, 1, duration) With {
                            .EasingFunction = easing, .FillBehavior = FillBehavior.Stop})
        translate.BeginAnimation(TranslateTransform.XProperty, New DoubleAnimation(offset, 0, duration) With {
                                 .EasingFunction = easing, .FillBehavior = FillBehavior.Stop})
    End Sub

    Public Sub New()
        InitializeComponent()
        AddHandler timerSend.Tick, AddressOf TimerSend_Tick
        AddHandler sendStartTimer.Tick, AddressOf SendStartTimer_Tick
        AddHandler sendCompletionTimer.Tick, AddressOf SendCompletionTimer_Tick
        AddHandler imagePasteTimer.Tick, AddressOf ImagePasteTimer_Tick
        AddHandler SystemEvents.UserPreferenceChanged, AddressOf OnUserPreferenceChanged
        InitializeTextBoxKeyHandler(KeyTextbox1)
        InitializeTextBoxKeyHandler(KeyTextbox2)
        InitializeTextBoxKeyHandler(KeyTextbox3)
        InitializeTextBoxKeyHandler(KeyTextbox4)
        InitializeTextBoxKeyHandler(KeyTextbox5)
        InitializeTextBoxKeyHandler(KeyTextbox6)
        _instance = Me
    End Sub

    '增加属性，方便访问
    Public Shared ReadOnly Property Instance() As MainWindow1
        Get
            Return _instance
        End Get
    End Property

#End Region

#Region "Settings"
    '保存设置
    Private Sub Button_Click(sender As Object, e As RoutedEventArgs)
        Dim reservedHotkeyName As String = GetReservedHotkeyName()
        If reservedHotkeyName <> "" Then
            ShowMyMessage("无法保存设置：" & reservedHotkeyName & "使用了系统常用快捷键，请更换后重试。")
            Return
        End If
        Select Case Combobox1.SelectedIndex
            Case 0
                Dim isDarkMode As Boolean = IsDarkModeEnabled()
                SwitchTheme(isDarkMode)
                WriteSetting("DoAutoSwitchTheme", 1)
            Case 1
                WriteSetting("IsDarkMode", 0)
                WriteSetting("DoAutoSwitchTheme", 0)
                SwitchTheme(False)
            Case 2
                WriteSetting("IsDarkMode", 1)
                WriteSetting("DoAutoSwitchTheme", 0)
                SwitchTheme(True)
            Case Else
                Hide()
                ShowExpdlg("错误2：程序控件状态不正常，可能是程序处于测试版或已被篡改！", "")
        End Select
        Select Case Combobox2.SelectedIndex
            Case 0
                WriteSetting("FloatingWinShowState", 1) '始终显示
                floatingWindow.ApplyDisplayMode(1)
            Case 1
                WriteSetting("FloatingWinShowState", 0) '始终隐藏
                ShowMyMessage("注意：悬浮窗隐藏后您只能使用快捷键来控制部分程序功能！")
                floatingWindow.ApplyDisplayMode(0)
            Case 2
                WriteSetting("FloatingWinShowState", 2) '自动收缩
                ShowMyMessage("注意：把鼠标移到悬浮窗上就可以展开它")
                floatingWindow.ApplyDisplayMode(2)
            Case Else
                Hide()
                ShowExpdlg("错误2：程序控件状态不正常，可能是程序处于测试版或已被篡改！", "")
        End Select
        If DoFloatingWindowTopmost.IsChecked = True Then
            WriteSetting("IsFloatingWinTopmost", 1)
            FloatingWindow.Instance.SetWindowTopMost()
        Else
            WriteSetting("IsFloatingWinTopmost", 0)
            FloatingWindow.Instance.SetWindowNotTopMost()
        End If
        If KeyTextbox3.Text <> "" Then
            '检测与其他软件的热键冲突（仅当热键变更时检测，避免与已注册的自身热键误判）
            Dim oldStopHotkey As String = ReadSetting("StopActHotkeys", "").ToString()
            If KeyTextbox3.Text <> oldStopHotkey Then
                If Not TestHotkeyStringAvailability(KeyTextbox3.Text) Then
                    ShowMyMessage("警告：终止任务快捷键可能已被其他程序占用！请更换快捷键或关闭占用该快捷键的程序，更改将在重启后生效。")
                End If
            End If
            WriteSetting("StopActHotkeys", KeyTextbox3.Text)
        Else
            ShowMyMessage("无法保存设置：没有指定一个或多个快捷键")
            Return
        End If
        '检测内部热键冲突
        If CheckHotkeyConflict(KeyTextbox3.Text, KeyTextbox2.Text) Then
            ShowMyMessage("警告：终止任务快捷键与摸鱼快捷键相同，可能导致冲突！")
        End If
        '保存连点开关热键
        If KeyTextbox4.Text <> "" Then
            WriteSetting("StopClickHotkeys", KeyTextbox4.Text)
        End If
        '保存连发开关热键
        If KeyTextbox5.Text <> "" Then
            WriteSetting("RapidFireHotkeys", KeyTextbox5.Text)
        End If
        '保存主界面显示/隐藏热键
        If KeyTextbox6.Text <> "" Then
            WriteSetting("ToggleMainWindowHotkeys", KeyTextbox6.Text)
        End If
        If StartupTabComboBox.SelectedIndex < 0 OrElse StartupTabComboBox.SelectedIndex >= TabControl1.Items.Count Then
            ShowMyMessage("无法保存设置：请选择程序启动时显示的选项卡。")
            Return
        End If
        WriteSetting("StartupTabIndex", StartupTabComboBox.SelectedIndex)
        If IsMicaEnabledCheckbox.IsChecked = True Then
            WriteSetting("IsMicaEnabled", 1)
            ThemeModule.isMicaEnabled = True
        Else
            WriteSetting("IsMicaEnabled", 0)
            ThemeModule.isMicaEnabled = False
        End If
        '检测开机自启
        Try
            If DoAutoStartCheckbox.IsChecked = True Then
                WriteSetting("DoAutoStart", 1)
                UpdateAutoStartRegistration(True)
            Else
                WriteSetting("DoAutoStart", 0)
                UpdateAutoStartRegistration(False)
            End If
        Catch ex As Exception
            ShowMyMessage("设置开机自启出错，错误内容：" & ex.Message)
        End Try
        ThemeModule.UpdateWindowBackdrops()
    End Sub

    Private Shared Sub UpdateAutoStartRegistration(enabled As Boolean)
        Dim appPath As String = System.Reflection.Assembly.GetExecutingAssembly().Location
        Using runKey As RegistryKey = Registry.CurrentUser.CreateSubKey("SOFTWARE\Microsoft\Windows\CurrentVersion\Run")
            If enabled Then
                runKey.SetValue("keyboardmgr2", Chr(34) & appPath & Chr(34) & " --autostart")
            ElseIf runKey.GetValue("keyboardmgr2") IsNot Nothing Then
                runKey.DeleteValue("keyboardmgr2")
            End If
        End Using
    End Sub
    '检测热键冲突（内部：两个热键是否相同）
    Private Function CheckHotkeyConflict(key1 As String, key2 As String) As Boolean
        If String.IsNullOrEmpty(key1) OrElse String.IsNullOrEmpty(key2) Then Return False
        Return key1.Trim().Equals(key2.Trim(), StringComparison.OrdinalIgnoreCase)
    End Function

    Private Function GetReservedHotkeyName() As String
        Dim hotkeys = {
            Tuple.Create("终止任务快捷键", KeyTextbox3.Text),
            Tuple.Create("连点开关快捷键", KeyTextbox4.Text),
            Tuple.Create("连发开关快捷键", KeyTextbox5.Text),
            Tuple.Create("主界面显示/隐藏快捷键", KeyTextbox6.Text)
        }
        For Each hotkey In hotkeys
            If IsReservedSystemHotkey(hotkey.Item2) Then Return hotkey.Item1
        Next
        Return ""
    End Function

    '检测热键是否被其他程序占用（外部冲突），返回 True 表示可用
    Private Function TestHotkeyStringAvailability(hotkeyStr As String) As Boolean
        If String.IsNullOrWhiteSpace(hotkeyStr) Then Return False
        Dim keyList = LoadKeyData(hotkeyStr)
        If IsNoneKeyList(keyList) Then Return False
        Dim vkList = ConvertKeyLogToVirtualKeyCodes(keyList)
        Return FloatingWindow.Instance.TestHotkeyAvailability(vkList)
    End Function

    Private Sub IsMicaEnabled_Checked(sender As Object, e As RoutedEventArgs) Handles IsMicaEnabledCheckbox.Checked
        '检测系统版本是否低于 Win11 22H2（DWMWA_SYSTEMBACKDROP_TYPE 需要 build 22621+）
        If ReadSetting("IsMicaEnabled", 0) = 0 Then
            If Not ThemeModule.IsWindows11_22H2OrLater() Then
                ShowMyMessage("当前 Windows 版本暂不支持云母效果（需要 Win11 22H2 或更高版本）。")
                IsMicaEnabledCheckbox.IsChecked = False
                Return
            Else
                '云母会在应用设置时立即更新。
            End If
        End If
    End Sub

#End Region

#Region "Click"
    '连点
    Private Sub Button_Click_7(sender As Object, e As RoutedEventArgs) '保存连点设置
        Dim Keys As New List(Of UShort) From {}
        If Textbox1.Text = "" Then
            ShowMyMessage("无法保存设置：没有指定发送间隔")
            Return
        End If
        WriteSetting("DoClickSettingSaved", 1)
        WriteSetting("DoRandomOffsetOfClickSpeed", If(CheckBox1.IsChecked = True, 1, 0))
        WriteSetting("DoRandomOffsetOfClickPosition", If(CheckBox2.IsChecked = True, 1, 0))
        If CheckBox3.IsChecked = True Then
            WriteSetting("DoCustomizeCursorPos", 1)
            WriteSetting("CursorPosition", Textbox2.Text)
        Else
            WriteSetting("DoCustomizeCursorPos", 0)
        End If
        WriteSetting("ClickInterval", Textbox1.Text)
        If RadioButton1.IsChecked = True Then
            WriteSetting("ClickMode", "LeftClick")
        ElseIf RadioButton2.IsChecked = True Then
            WriteSetting("ClickMode", "RightClick")
        ElseIf RadioButton3.IsChecked = True Then
            WriteSetting("ClickMode", "KeyboardClick")
            If KeyTextbox1.Text <> "" Then
                WriteSetting("ClickKeys", KeyTextbox1.Text)
            Else
                ShowMyMessage("无法保存设置：没有指定要连点的键")
                Return
            End If
            If Textbox1.Text <= 50 Then
                ShowMyMessage("无法保存设置：键盘按键连点需要发送间隔大于50！")
                Textbox1.Text = 60
                Return
            End If
        Else
            Hide()
            ShowExpdlg("错误2：程序控件状态不正常，可能是程序处于测试版或已被篡改！", "")
        End If
    End Sub

    Private Sub CheckBox3_Click(sender As Object, e As RoutedEventArgs)
        If CheckBox3.IsChecked = True Then
            Textbox2.Visibility = Visibility.Visible
        Else
            Textbox2.Visibility = Visibility.Hidden
        End If
    End Sub

    Private Sub Textbox2_PreviewTextInput(sender As Object, e As TextCompositionEventArgs) '使用正则表达式检测部分textbox，让其只支持坐标输入

        Dim regex As New Regex("^[0-9]*,?[0-9]*$") '匹配一组数字，用逗号隔开，允许逗号后跟数字但不是必须的
        Dim textBox As TextBox = CType(sender, TextBox)
        Dim currentText As String = textBox.Text
        Dim selectionStart As Integer = textBox.SelectionStart
        Dim selectionLength As Integer = textBox.SelectionLength
        Dim newText As String

        '如果有选中的文本，则替换选中的文本
        If selectionLength > 0 Then
            newText = currentText.Substring(0, selectionStart) & e.Text & currentText.Substring(selectionStart + selectionLength)
        Else
            '否则在光标位置插入文本
            newText = currentText.Substring(0, selectionStart) & e.Text & currentText.Substring(selectionStart)
        End If

        '检查新的文本是否符合正则表达式
        If Not regex.IsMatch(newText) Then
            e.Handled = True
        End If

    End Sub

    Private Sub TextBox_PreviewTextInput(sender As Object, e As TextCompositionEventArgs) '使用正则表达式检测部分textbox，让其仅支持数字输入

        Dim regex As New Regex("[^\d]") '使用\d匹配任何数字
        '检查输入的字符是否符合正则表达式规则
        If regex.IsMatch(e.Text) Then
            e.Handled = True
        Else
            Dim textBox As TextBox = CType(sender, TextBox)
            Dim newText As String = textBox.Text.Insert(textBox.SelectionStart, e.Text)
            Dim number As Integer
            '尝试将新文本转换为整数
            If Integer.TryParse(newText, number) Then
                If number < 1 OrElse number > 1000000 Then '设定范围
                    e.Handled = True
                End If
            Else
                e.Handled = True
            End If
        End If

    End Sub

    Private Sub TextBox_PreviewKeyDown(sender As Object, e As KeyEventArgs)
        If e.Key = Key.Space Then
            e.Handled = True
        End If
    End Sub

    Private Sub CommandBinding_CanExecute1(sender As Object, e As CanExecuteRoutedEventArgs) '禁止粘贴
        e.CanExecute = False
        e.Handled = True
    End Sub

    Dim sendKeys As New List(Of UShort) From {}
    Dim numberofKeys As Short
    Dim key1 As UShort
    Dim clickTime As Integer = 10
    Dim isSpeedRandomOffset As Boolean = False
    Dim isPosRandomOffset As Boolean = False
    Dim clickBasePosition As POINTAPI
    Public timer1 As New Timer
    Public timer2 As New Timer
    Public timer3 As New Timer
    Private Sub Button_Click_3(sender As Object, e As RoutedEventArgs) '开始连点
        If isClicking Then
            StopClick()
            Return
        End If
        If isSending Then StopSend()
        If Textbox1.Text = "" Then
            ShowMyMessage("没有指定发送间隔")
            Return
        ElseIf Textbox1.Text > 0 Then
            clickTime = Textbox1.Text '此处隐式转换
            isSpeedRandomOffset = CheckBox1.IsChecked = True
            isPosRandomOffset = CheckBox2.IsChecked = True
            If CheckBox3.IsChecked = True Then '自定义鼠标位置
                If Textbox2.Text = "" Or Textbox2.Text = "," Then
                    ShowMyMessage("没有指定自定义鼠标位置")
                    Return
                Else
                    Dim cursorPos As String() = Textbox2.Text.Split(",") '坐标形式：（横坐标,纵坐标）
                    Dim x As Integer = cursorPos(0)
                    Dim y As Integer = cursorPos(1)
                    SetCursorPosition(x, y)
                    clickBasePosition.x = x
                    clickBasePosition.y = y
                    ShowMyMessage("已确定鼠标位置：" & x & "," & y & "请勿移动鼠标！")
                End If
            End If
            If isPosRandomOffset AndAlso CheckBox3.IsChecked <> True Then GetCursorPos(clickBasePosition)
            If RadioButton1.IsChecked = True Then '左键连点
                AddHandler timer1.Elapsed, AddressOf Timer1_Elapsed
                timer1.Interval = clickTime
                timer1.AutoReset = True
                timer1.Enabled = True
                timer1.Start()
                isClicking = True
                Hide()
                floatingWindow.FloatingWindowEvent_Click() '悬浮窗状态更新
            ElseIf RadioButton2.IsChecked = True Then '右键连点 
                AddHandler timer2.Elapsed, AddressOf Timer2_Elapsed
                timer2.Interval = clickTime
                timer2.AutoReset = True
                timer2.Enabled = True
                timer2.Start()
                isClicking = True
                Hide()
                floatingWindow.FloatingWindowEvent_Click() '悬浮窗状态更新
            ElseIf GetKeyLog() IsNot Nothing OrElse savedkeys.Count > 0 Then '自定义键连点
                If clickTime > 50 Then
                    '清空上一次遗留的按键，避免多次启动时累积导致 Count 超过 4 而失效
                    sendKeys.Clear()
                    If savedkeys.Count = 0 Then '这里判断有没有保存键
                        For Each value As Byte In ConvertKeyLogToVirtualKeyCodes(GetKeyLog)
                            sendKeys.Add(Convert.ToByte(value))
                        Next
                    Else
                        For Each value As Byte In ConvertKeyLogToVirtualKeyCodes(savedkeys)
                            sendKeys.Add(Convert.ToByte(value))
                        Next
                    End If
                    Select Case sendKeys.Count
                        Case 1
                            numberofKeys = 1
                            key1 = sendKeys(0)
                        Case 2
                            numberofKeys = 2
                        Case 3
                            numberofKeys = 3
                        Case 4
                            numberofKeys = 4
                        Case Else
                            ShowMyMessage("无法发送该（快捷）键")
                            Return
                    End Select
                    timer3.Interval = clickTime
                    AddHandler timer3.Elapsed, AddressOf Timer3_Elapsed
                    timer3.AutoReset = True
                    timer3.Enabled = True
                    timer3.Start()
                    isClicking = True
                    Hide()
                    floatingWindow.FloatingWindowEvent_Click() '悬浮窗状态更新
                Else
                    ShowMyMessage("键盘按键连点需要发送间隔大于50！")
                    Textbox1.Text = 60
                    Return
                End If
            Else
                ShowMyMessage("没有指定要发送的键")
                Return
            End If
        Else
            ShowMyMessage("间隔时间不能小于等于0哦")
            Return
        End If
    End Sub

    Dim random As New Random
    Private Sub Timer1_Elapsed(sender As Object, e As ElapsedEventArgs)
        '左键连点
        If isSpeedRandomOffset = True Then
            Dim randomSpeed As Integer = random.Next(-10, 11)
            timer1.Interval = Math.Max(1, clickTime + randomSpeed)
        End If
        Dim P As POINTAPI
        GetCursorPos(P)
        If isPosRandomOffset = True Then
            P.x = clickBasePosition.x + random.Next(-15, 16)
            P.y = clickBasePosition.y + random.Next(-15, 16)
            SetCursorPosition(P.x, P.y)
        End If
        mouse_event(MOUSEEVENTF_LEFTDOWN, P.x.ToString, P.y.ToString, 0, 0)
        mouse_event(MOUSEEVENTF_LEFTUP, P.x.ToString, P.y.ToString, 0, 0)
    End Sub
    Private Sub Timer2_Elapsed(sender As Object, e As ElapsedEventArgs)
        '右键连点
        If isSpeedRandomOffset = True Then
            Dim randomSpeed As Integer = random.Next(-10, 11)
            timer2.Interval = Math.Max(1, clickTime + randomSpeed)
        End If
        Dim P As POINTAPI
        GetCursorPos(P)
        If isPosRandomOffset = True Then
            P.x = clickBasePosition.x + random.Next(-15, 16)
            P.y = clickBasePosition.y + random.Next(-15, 16)
            SetCursorPosition(P.x, P.y)
        End If
        mouse_event(MOUSEEVENTF_RIGHTDOWN, P.x.ToString, P.y.ToString, 0, 0)
        mouse_event(MOUSEEVENTF_RIGHTUP, P.x.ToString, P.y.ToString, 0, 0)
    End Sub
    Private Sub Timer3_Elapsed(sender As Object, e As ElapsedEventArgs)
        '自定义键连点
        Select Case numberofKeys
            Case 1
                SendKey(key1, True)
                SendKey(key1, False)
            Case 2
                SendKeyCombination(sendKeys)
            Case 3
                SendKeyCombination(sendKeys)
            Case 4
                SendKeyCombination(sendKeys)
            Case Else
                ShowExpdlg("错误2：程序变量状态不正常，可能是程序处于测试版或已被篡改！", "")
        End Select

    End Sub

    Public Sub StopClick()

        RemoveHandler timer1.Elapsed, AddressOf Timer1_Elapsed
        timer1.Stop()
        RemoveHandler timer2.Elapsed, AddressOf Timer2_Elapsed
        timer2.Stop()
        RemoveHandler timer3.Elapsed, AddressOf Timer3_Elapsed
        timer3.Stop()
        timer1.AutoReset = False
        timer2.AutoReset = False
        timer3.AutoReset = False
        timer1.Enabled = False
        timer2.Enabled = False
        timer3.Enabled = False
        isClicking = False
        isSpeedRandomOffset = False
        isPosRandomOffset = False
        floatingWindow.FloatingWindow_Reset()
        GC.Collect()
    End Sub

#End Region

#Region "Loaf"
    '摸鱼
    Dim isLoafEnabled As Boolean = False
    Private Sub Button_Click_4(sender As Object, e As RoutedEventArgs) '保存摸鱼设置
        If IsReservedSystemHotkey(KeyTextbox2.Text) Then
            ShowMyMessage("无法保存设置：摸鱼快捷键使用了系统常用快捷键，请更换后重试。")
            Return
        End If
        WriteSetting("IsLoafEnabled", If(LoafToggle.IsChecked, 1, 0))
        If KeyTextbox2.Text <> "" Then
            '检测与其他软件的热键冲突（仅当热键变更时检测）
            Dim oldLoafHotkey As String = ReadSetting("LoafHotkeys", "").ToString()
            If KeyTextbox2.Text <> oldLoafHotkey Then
                If Not TestHotkeyStringAvailability(KeyTextbox2.Text) Then
                    ShowMyMessage("警告：摸鱼快捷键可能已被其他程序占用！请更换快捷键或关闭占用该快捷键的程序。")
                End If
            End If
            WriteSetting("LoafHotkeys", KeyTextbox2.Text)
            '即时更新热键：卸载旧热键、解析新热键、重新注册（如果摸鱼已启用）
            UnregisterLoafHotkey()
            Dim newKeyList = LoadKeyData(KeyTextbox2.Text)
            If Not IsNoneKeyList(newKeyList) Then
                loafHotkeys = ConvertKeyLogToVirtualKeyCodes(newKeyList)
            End If
            If isLoafEnabled Then
                TryRegisterLoafHotkey()
            End If
        Else
            ShowMyMessage("无法保存设置：没有指定摸鱼快捷键")
            Return
        End If
        If CheckHotkeyConflict(KeyTextbox2.Text, KeyTextbox3.Text) Then
            ShowMyMessage("警告：摸鱼快捷键与终止任务快捷键相同，可能导致冲突！")
        End If
    End Sub

    Private Sub UnregisterLoafHotkey()
        floatingWindow.UnregisterSingleHotkey(9001)
    End Sub

    Private Sub TryRegisterLoafHotkey()
        If loafHotkeys.Count > 0 Then
            floatingWindow.RegisterGlobalHotkey(loafHotkeys, 9001)
        End If
    End Sub

    Private Sub CheckBox_Click(sender As Object, e As RoutedEventArgs)
        If LoafToggle.IsChecked = True Then
            isLoafEnabled = True
            LoafGrid.Visibility = Visibility.Visible
            TryRegisterLoafHotkey()
        Else
            If LoafModule.InLoafMode Then LoafModule.ExitLoafMode()
            isLoafEnabled = False
            LoafGrid.Visibility = Visibility.Hidden
            UnregisterLoafHotkey()
        End If
    End Sub

    Private Sub LoafHelpButton_Click(sender As Object, e As RoutedEventArgs) '显示帮助
        Dim helps As New List(Of String) From {
            "摸鱼工具箱可以让您使用一组快捷键即可快速调整窗口，使用方法如下：",
            "1. 点击""选取窗体""按钮，选择您的工作窗口（如Excel、Word等工作软件）。",
            "2. 启用摸鱼功能，设置一组快捷键，点击""应用""保存设置。",
            "3. 当您需要摸鱼时，按下快捷键，所有其他窗口将被最小化，工作窗口将被前置到屏幕最上方。",
            "4. 再次按下快捷键，可恢复之前被最小化的所有窗口。",
            "这个功能类似于某些软件的老板键功能，但摸鱼工具箱所提供的快捷键是全局的。",
            "注意：此功能仅在本程序运行时有效。快捷键设置更改后需重启程序生效。"
        }
        ShowHelp(helps, "摸鱼工具箱帮助")
    End Sub

    Private Sub Button_Click_5(sender As Object, e As RoutedEventArgs) '选取窗体
        Try
            Dim selectorForm As New WindowSelectorDlg()
            If selectorForm.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
                Dim selectedHwnd As IntPtr = selectorForm.SelectedWindowHwnd
                If selectedHwnd = IntPtr.Zero Then
                    ShowMyMessage("未选取有效窗体，请重试。")
                    Return
                End If
                '保存工作窗口到注册表
                LoafModule.SaveWorkWindow(selectedHwnd)
                '显示窗口标题（比句柄更直观）
                Dim title As String = LoafModule.GetWindowTitle(selectedHwnd)
                If Not String.IsNullOrEmpty(title) Then
                    SelectedWindowHwnd.Content = "选取的窗体：" & title
                Else
                    SelectedWindowHwnd.Content = "选取的窗体句柄：" & selectedHwnd.ToString()
                End If
                ShowMyMessage("工作窗口已保存。按下摸鱼快捷键即可一键切换。")
            End If
        Catch ex As Exception
            Hide()
            ShowExpdlg("错误9：程序选取窗体时遇到错误，可能是LCS窗体选取器（WindowSelector.dll）丢失！", ex.Message & vbLf & ex.StackTrace)
        End Try
    End Sub













#End Region

#Region "Send"
    '连发
    Dim sendPhrases As New List(Of RapidFireItem)
    Dim currentSendIndex As Integer = -1
    Dim sendLoop As Boolean = False
    Private ReadOnly timerSend As New DispatcherTimer(DispatcherPriority.Normal)
    Dim isSending As Boolean = False
    Dim isClicking As Boolean = False
    Private isUpdatingSendEditor As Boolean = False
    Private sendItemPreviews As New List(Of String)
    Private activeSendPhrases As New List(Of RapidFireItem)
    Private sendCursor As Integer = 0
    Private sendTargetWindow As IntPtr = IntPtr.Zero
    Private ReadOnly sendStartTimer As New DispatcherTimer(DispatcherPriority.Normal)
    Private ReadOnly sendCompletionTimer As New DispatcherTimer(DispatcherPriority.Normal)
    Private sendTargetRetryCount As Integer = 0
    Private sendIntervalMilliseconds As Integer = 100
    Private ReadOnly sendIntervalWatch As New Diagnostics.Stopwatch()
    Private ReadOnly imagePasteTimer As New DispatcherTimer(DispatcherPriority.Normal)
    Private imagePastePending As Boolean = False
    Private imageClipboardReady As Boolean = False
    Private imagePasteReadyForEnter As Boolean = False
    Private clipboardRetryCount As Integer = 0
    Private imageFocusRetryCount As Integer = 0
    Private activeImageClipboardData As DataObject
    Private ReadOnly imageSendWatch As New Diagnostics.Stopwatch()
    Private Const MaximumClipboardRetryCount As Integer = 20
    Private Const MaximumImageFocusRetryCount As Integer = 10
    Private Const MaximumImageSendMilliseconds As Integer = 5000

    Private Sub UpdateItemDisplay()
        isUpdatingSendEditor = True
        SendItemList.ItemsSource = Nothing
        sendItemPreviews = sendPhrases.Select(Function(item, index) $"{index + 1}. {GetSendItemPreview(item)}").ToList()
        SendItemList.ItemsSource = sendItemPreviews
        TxtItemCounter.Text = $"{sendPhrases.Count} 项"

        If sendPhrases.Count = 0 Then
            currentSendIndex = -1
            RichTextBox1.Document.Blocks.Clear()
            BtnDeleteItem.IsEnabled = False
            BtnMoveItemUp.IsEnabled = False
            BtnMoveItemDown.IsEnabled = False
            SendEditorWatermark.Visibility = Visibility.Visible
            SendImageEditor.Visibility = Visibility.Collapsed
            RichTextBox1.Visibility = Visibility.Visible
            isUpdatingSendEditor = False
            Return
        End If
        If currentSendIndex < 0 Then currentSendIndex = 0
        If currentSendIndex >= sendPhrases.Count Then currentSendIndex = sendPhrases.Count - 1
        SendItemList.SelectedIndex = currentSendIndex
        Dim currentItem As RapidFireItem = sendPhrases(currentSendIndex)
        RichTextBox1.Document.Blocks.Clear()
        If currentItem.Kind = RapidFireItemKind.Image Then
            RichTextBox1.Visibility = Visibility.Collapsed
            SendEditorWatermark.Visibility = Visibility.Collapsed
            SendImageEditor.Visibility = Visibility.Visible
            SendImagePreview.Source = LoadBitmap(currentItem.ImageBytes)
            SendImageFileName.Text = If(currentItem.FileName, "图片")
        Else
            RichTextBox1.Visibility = Visibility.Visible
            SendImageEditor.Visibility = Visibility.Collapsed
            SendImagePreview.Source = Nothing
            RichTextBox1.Document.Blocks.Add(New Paragraph(New Run(If(currentItem.Text, String.Empty))))
            SendEditorWatermark.Visibility = If(String.IsNullOrEmpty(currentItem.Text), Visibility.Visible, Visibility.Collapsed)
        End If
        BtnDeleteItem.IsEnabled = True
        BtnMoveItemUp.IsEnabled = currentSendIndex > 0
        BtnMoveItemDown.IsEnabled = currentSendIndex < sendPhrases.Count - 1
        isUpdatingSendEditor = False
    End Sub

    Private Function GetSendItemPreview(item As RapidFireItem) As String
        If item.Kind = RapidFireItemKind.Image Then Return "[图片] " & If(item.FileName, "未命名图片")
        Dim preview As String = If(item.Text, String.Empty).Replace(vbCr, " ").Replace(vbLf, " ").Trim()
        If preview.Length = 0 Then Return "未命名条目"
        If preview.Length > 18 Then Return preview.Substring(0, 18) & "…"
        Return preview
    End Function

    '热键触发的连点开关：已启动则停止，未启动则启动
    Public Sub ToggleClick()
        If isClicking Then
            StopClick()
        Else
            Button_Click_3(Nothing, Nothing)
        End If
    End Sub

    '热键触发的连发开关：已启动则停止，未启动则启动
    Public Sub ToggleSend()
        If isSending Then
            StopSend()
        Else
            BtnSendStart_Click(Nothing, Nothing)
        End If
    End Sub

    Private Sub SaveCurrentItem()
        If currentSendIndex >= 0 AndAlso currentSendIndex < sendPhrases.Count AndAlso sendPhrases(currentSendIndex).Kind = RapidFireItemKind.Text Then
            Dim textRange As New TextRange(RichTextBox1.Document.ContentStart, RichTextBox1.Document.ContentEnd)
            Dim text As String = textRange.Text
            If text.EndsWith(vbCrLf, StringComparison.Ordinal) Then text = text.Substring(0, text.Length - vbCrLf.Length)
            sendPhrases(currentSendIndex).Text = text
        End If
    End Sub

    Private Sub SendItemList_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If isUpdatingSendEditor OrElse SendItemList.SelectedIndex < 0 OrElse SendItemList.SelectedIndex = currentSendIndex Then Return
        SaveCurrentItem()
        currentSendIndex = SendItemList.SelectedIndex
        UpdateItemDisplay()
    End Sub

    Private Sub RichTextBox1_TextChanged(sender As Object, e As TextChangedEventArgs)
        If isUpdatingSendEditor Then Return
        SaveCurrentItem()
        Dim textRange As New TextRange(RichTextBox1.Document.ContentStart, RichTextBox1.Document.ContentEnd)
        SendEditorWatermark.Visibility = If(String.IsNullOrWhiteSpace(textRange.Text), Visibility.Visible, Visibility.Collapsed)
        If currentSendIndex >= 0 AndAlso currentSendIndex < sendItemPreviews.Count Then
            sendItemPreviews(currentSendIndex) = $"{currentSendIndex + 1}. {GetSendItemPreview(sendPhrases(currentSendIndex))}"
            SendItemList.Items.Refresh()
        End If
    End Sub

    Private Sub BtnAddItem_Click(sender As Object, e As RoutedEventArgs)
        SaveCurrentItem()
        sendPhrases.Add(RapidFireItem.CreateText(String.Empty))
        currentSendIndex = sendPhrases.Count - 1
        UpdateItemDisplay()
        RichTextBox1.Focus()
    End Sub

    Private Sub BtnAddImage_Click(sender As Object, e As RoutedEventArgs)
        SaveCurrentItem()
        If sendPhrases.Count >= RapidFirePresetCodec.MaximumItemCount Then
            ShowMyMessage("连发预设最多支持10000个条目。")
            Return
        End If
        Dim dialog As New OpenFileDialog With {
            .Title = "插入图片",
            .Filter = "图片文件 (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|所有文件 (*.*)|*.*",
            .CheckFileExists = True,
            .Multiselect = False
        }
        If dialog.ShowDialog(Me) <> True Then Return
        Try
            If New FileInfo(dialog.FileName).Length >= RapidFirePresetCodec.MaximumFileSize Then Throw New InvalidDataException("图片文件必须小于 16 MiB。")
            Dim imageBytes As Byte() = File.ReadAllBytes(dialog.FileName)
            If imageBytes.Length = 0 Then Throw New InvalidDataException("图片文件为空。")
            LoadBitmap(imageBytes)
            Dim item As New RapidFireItem With {.Kind = RapidFireItemKind.Image, .FileName = Path.GetFileName(dialog.FileName), .ImageBytes = imageBytes}
            Dim insertIndex As Integer = If(currentSendIndex >= 0, currentSendIndex + 1, sendPhrases.Count)
            sendPhrases.Insert(insertIndex, item)
            currentSendIndex = insertIndex
            UpdateItemDisplay()
        Catch ex As Exception
            ShowMyMessage("无法插入图片：" & ex.Message)
        End Try
    End Sub

    Private Sub BtnMoveItemUp_Click(sender As Object, e As RoutedEventArgs)
        MoveSendItem(-1)
    End Sub

    Private Sub BtnMoveItemDown_Click(sender As Object, e As RoutedEventArgs)
        MoveSendItem(1)
    End Sub

    Private Sub MoveSendItem(offset As Integer)
        SaveCurrentItem()
        If currentSendIndex < 0 Then Return
        Dim targetIndex As Integer = currentSendIndex + offset
        If targetIndex < 0 OrElse targetIndex >= sendPhrases.Count Then Return
        Dim item As RapidFireItem = sendPhrases(currentSendIndex)
        sendPhrases.RemoveAt(currentSendIndex)
        sendPhrases.Insert(targetIndex, item)
        currentSendIndex = targetIndex
        UpdateItemDisplay()
    End Sub

    Private Sub BtnDeleteItem_Click(sender As Object, e As RoutedEventArgs)
        If sendPhrases.Count = 0 OrElse currentSendIndex < 0 Then Return
        sendPhrases.RemoveAt(currentSendIndex)
        If sendPhrases.Count = 0 Then
            currentSendIndex = -1
            RichTextBox1.Document.Blocks.Clear()
        Else
            If currentSendIndex >= sendPhrases.Count Then currentSendIndex = sendPhrases.Count - 1
        End If
        UpdateItemDisplay()
    End Sub

    Private Sub BtnSendStart_Click(sender As Object, e As RoutedEventArgs)
        If isSending Then
            '停止连发
            StopSend()
            Return
        End If
        If isClicking Then StopClick()
        SaveCurrentItem()
        If sendPhrases.Count = 0 OrElse Not sendPhrases.Any(AddressOf IsSendableItem) Then
            ShowMyMessage("没有连发内容可发送")
            Return
        End If
        '验证间隔
        Dim interval As Integer = 100
        If Not Integer.TryParse(TxtSendInterval.Text, interval) OrElse interval < 50 OrElse interval > 1000000 Then
            ShowMyMessage("发送间隔需为50到1000000之间的整数（毫秒）")
            Return
        End If
        If sendPhrases.Count > 10000 Then
            ShowMyMessage("连发预设最多支持10000个条目。")
            Return
        End If
        sendLoop = ChkSendLoop.IsChecked.GetValueOrDefault(False)
        '启动发送
        activeSendPhrases = sendPhrases.Where(AddressOf IsSendableItem).Select(AddressOf CloneSendItem).ToList()
        sendCursor = 0
        isSending = True
        BtnSendStart.Content = "停止连发"
        sendIntervalMilliseconds = interval
        timerSend.Interval = TimeSpan.FromMilliseconds(sendIntervalMilliseconds)
        timerSend.Stop()
        Hide()
        floatingWindow.FloatingWindowEvent_Send()

        '等待主窗口完成隐藏，让 Windows 恢复之前的外部前台窗口。
        sendTargetRetryCount = 0
        sendStartTimer.Stop()
        sendStartTimer.Interval = TimeSpan.FromMilliseconds(200)
        sendStartTimer.Start()
    End Sub

    Private Sub SendStartTimer_Tick(sender As Object, e As EventArgs)
        sendStartTimer.Stop()
        If Not isSending Then Return

        Dim candidateWindow As IntPtr = GetForegroundWindow()
        Dim candidateProcessId As UInteger = 0
        If candidateWindow <> IntPtr.Zero Then GetWindowThreadProcessId(candidateWindow, candidateProcessId)
        If candidateWindow = IntPtr.Zero OrElse candidateProcessId = CUInt(Diagnostics.Process.GetCurrentProcess().Id) Then
            sendTargetRetryCount += 1
            If sendTargetRetryCount < 10 Then
                sendStartTimer.Start()
                Return
            End If
            StopSend()
            Show()
            Activate()
            ShowMyMessage("无法确定连发目标窗口，请先切换到目标窗口后使用连发热键，或重新点击开始。")
            Return
        End If

        sendTargetWindow = candidateWindow
        SendNextPhrase()
        If isSending AndAlso Not imagePastePending Then timerSend.Start()
    End Sub

    Private Sub TimerSend_Tick(sender As Object, e As EventArgs)
        'DispatcherTimer 按单次计时使用，确保上一条发送完成后再等待完整间隔。
        timerSend.Stop()
        Dim remainingMilliseconds As Long = sendIntervalMilliseconds - sendIntervalWatch.ElapsedMilliseconds
        If remainingMilliseconds > 0 Then
            timerSend.Interval = TimeSpan.FromMilliseconds(remainingMilliseconds)
            timerSend.Start()
            Return
        End If
        SendNextPhrase()
        If isSending AndAlso Not imagePastePending Then
            timerSend.Interval = TimeSpan.FromMilliseconds(sendIntervalMilliseconds)
            timerSend.Start()
        End If
    End Sub

    Private Sub SendNextPhrase()
        If sendCursor >= activeSendPhrases.Count Then
            If sendLoop Then
                sendCursor = 0
            Else
                CompleteSend()
                Return
            End If
        End If
        If sendTargetWindow = IntPtr.Zero OrElse Not IsWindow(sendTargetWindow) Then
            StopSend()
            ShowMyMessage("连发目标窗口已关闭，任务已停止。")
            Return
        End If
        SetForegroundWindow(sendTargetWindow)
        Dim itemToSend As RapidFireItem = activeSendPhrases(sendCursor)
        Try
            If itemToSend.Kind = RapidFireItemKind.Image Then
                imagePastePending = True
                imageClipboardReady = False
                imagePasteReadyForEnter = False
                clipboardRetryCount = 0
                imageFocusRetryCount = 0
                imageSendWatch.Restart()
                TryStartImagePaste(itemToSend)
                Return
            ElseIf Not String.IsNullOrWhiteSpace(itemToSend.Text) Then
                UserInputHandler.SendUnicodeText(itemToSend.Text)
                UserInputHandler.SendEnterKey()
                sendIntervalWatch.Restart()
            End If
        Catch ex As Exception
            StopSend()
            ShowMyMessage("无法发送连发条目：" & ex.Message)
            Return
        End Try
        sendCursor += 1
        If Not sendLoop AndAlso sendCursor >= activeSendPhrases.Count Then CompleteSend()
    End Sub

    Private Sub TryStartImagePaste(item As RapidFireItem)
        Try
            SetImageClipboardData(item.ImageBytes)
            imageClipboardReady = True
            imagePasteTimer.Stop()
            imagePasteTimer.Interval = TimeSpan.FromMilliseconds(100)
            imagePasteTimer.Start()
        Catch ex As ExternalException
            clipboardRetryCount += 1
            If clipboardRetryCount >= MaximumClipboardRetryCount Then
                StopSend()
                ShowMyMessage("无法使用剪贴板，其他程序可能持续占用剪贴板。请稍后重试。")
                Return
            End If
            imagePasteTimer.Stop()
            imagePasteTimer.Interval = TimeSpan.FromMilliseconds(50)
            imagePasteTimer.Start()
        Catch ex As Exception
            StopSend()
            ShowMyMessage("无法发送图片：" & ex.Message)
        End Try
    End Sub

    Private Sub ImagePasteTimer_Tick(sender As Object, e As EventArgs)
        imagePasteTimer.Stop()
        If Not isSending OrElse Not imagePastePending Then Return
        If imageSendWatch.ElapsedMilliseconds >= MaximumImageSendMilliseconds Then
            StopSend()
            ShowMyMessage("图片发送等待超时，任务已停止。请确认目标输入框支持使用 Ctrl+V 粘贴图片。")
            Return
        End If
        If Not imagePasteReadyForEnter Then
            If sendCursor >= activeSendPhrases.Count Then
                StopSend()
                Return
            End If
            If Not imageClipboardReady Then
                TryStartImagePaste(activeSendPhrases(sendCursor))
                Return
            End If
            If sendTargetWindow = IntPtr.Zero OrElse Not IsWindow(sendTargetWindow) Then
                StopSend()
                ShowMyMessage("连发目标窗口已关闭，任务已停止。")
                Return
            End If
            If GetForegroundWindow() <> sendTargetWindow Then
                SetForegroundWindow(sendTargetWindow)
                imageFocusRetryCount += 1
                If imageFocusRetryCount >= MaximumImageFocusRetryCount Then
                    StopSend()
                    ShowMyMessage("无法将焦点切换到连发目标窗口，图片发送已停止。")
                    Return
                End If
                imagePasteTimer.Interval = TimeSpan.FromMilliseconds(50)
                imagePasteTimer.Start()
                Return
            End If
            Try
                UserInputHandler.SendPasteShortcut()
            Catch ex As ComponentModel.Win32Exception
                StopSend()
                ShowMyMessage(ex.Message)
                Return
            End Try
            imagePasteReadyForEnter = True
            imagePasteTimer.Interval = TimeSpan.FromMilliseconds(500)
            imagePasteTimer.Start()
            Return
        End If
        Try
            UserInputHandler.SendEnterKey()
        Catch ex As ComponentModel.Win32Exception
            StopSend()
            ShowMyMessage(ex.Message)
            Return
        End Try
        imagePastePending = False
        imageClipboardReady = False
        imagePasteReadyForEnter = False
        clipboardRetryCount = 0
        imageFocusRetryCount = 0
        activeImageClipboardData = Nothing
        imageSendWatch.Reset()
        sendIntervalWatch.Restart()
        sendCursor += 1
        If Not sendLoop AndAlso sendCursor >= activeSendPhrases.Count Then
            CompleteSend()
        ElseIf isSending Then
            timerSend.Interval = TimeSpan.FromMilliseconds(sendIntervalMilliseconds)
            timerSend.Start()
        End If
    End Sub

    Private Sub CompleteSend()
        StopSend()
        '让最后一次 Enter 先由目标程序处理，再恢复并激活主窗口。
        sendCompletionTimer.Stop()
        imagePasteTimer.Stop()
        imagePastePending = False
        imageClipboardReady = False
        imagePasteReadyForEnter = False
        clipboardRetryCount = 0
        imageFocusRetryCount = 0
        activeImageClipboardData = Nothing
        imageSendWatch.Reset()
        sendCompletionTimer.Interval = TimeSpan.FromMilliseconds(150)
        sendCompletionTimer.Start()
    End Sub

    Private Sub SendCompletionTimer_Tick(sender As Object, e As EventArgs)
        sendCompletionTimer.Stop()
        TabControl1.SelectedIndex = 2
        Show()
        WindowState = WindowState.Normal
        Activate()
    End Sub

    Public Sub StopSend()
        timerSend.Stop()
        sendStartTimer.Stop()
        sendCompletionTimer.Stop()
        imagePasteTimer.Stop()
        imagePastePending = False
        imageClipboardReady = False
        imagePasteReadyForEnter = False
        clipboardRetryCount = 0
        imageFocusRetryCount = 0
        activeImageClipboardData = Nothing
        imageSendWatch.Reset()
        isSending = False
        activeSendPhrases.Clear()
        sendCursor = 0
        sendTargetWindow = IntPtr.Zero
        sendTargetRetryCount = 0
        sendIntervalWatch.Reset()
        BtnSendStart.Content = "开始连发"
        floatingWindow.FloatingWindow_Reset()
    End Sub

    Private Sub BtnSendSave_Click(sender As Object, e As RoutedEventArgs)
        SaveCurrentItem()
        If sendPhrases.Count = 0 OrElse Not sendPhrases.Any(AddressOf IsSendableItem) Then
            ShowMyMessage("没有连发内容可保存")
            Return
        End If
        Dim interval As Integer
        If Not Integer.TryParse(TxtSendInterval.Text, interval) OrElse interval < 50 OrElse interval > 1000000 Then
            ShowMyMessage("发送间隔需为50到1000000之间的整数（毫秒）")
            Return
        End If
        If sendPhrases.Count > 10000 Then
            ShowMyMessage("连发预设最多支持10000个条目。")
            Return
        End If
        Dim dialog As New SaveFileDialog With {
            .Title = "保存连发预设",
            .Filter = "LCS新版列表连发文件 (*.lcslst2)|*.lcslst2|所有文件 (*.*)|*.*",
            .DefaultExt = ".lcslst2",
            .AddExtension = True,
            .OverwritePrompt = True,
            .FileName = "连发预设"
        }
        If dialog.ShowDialog(Me) <> True Then Return

        Dim preset As New RapidFirePreset With {
            .IntervalMilliseconds = interval,
            .SendLoop = ChkSendLoop.IsChecked.GetValueOrDefault(False),
            .Items = sendPhrases.Select(AddressOf CloneSendItem).ToList()
        }
        Dim tempFile As String = dialog.FileName & "." & Guid.NewGuid().ToString("N") & ".tmp"
        Try
            File.WriteAllBytes(tempFile, RapidFirePresetCodec.Serialize(preset))
            If File.Exists(dialog.FileName) Then
                File.Replace(tempFile, dialog.FileName, Nothing)
            Else
                File.Move(tempFile, dialog.FileName)
            End If
            ShowMyMessage("连发预设已保存到文件。")
        Catch ex As Exception
            ShowMyMessage("无法保存连发预设：" & ex.Message)
        Finally
            Try
                If File.Exists(tempFile) Then File.Delete(tempFile)
            Catch
                '目标文件保存结果不应被临时文件清理失败覆盖。
            End Try
        End Try
    End Sub

    Private Sub BtnSendOpen_Click(sender As Object, e As RoutedEventArgs)
        Dim dialog As New OpenFileDialog With {
            .Title = "打开连发预设",
            .Filter = "LCS列表连发文件 (*.lcslst2;*.lcslst)|*.lcslst2;*.lcslst|新版二进制列表 (*.lcslst2)|*.lcslst2|经典版列表 (*.lcslst)|*.lcslst|所有文件 (*.*)|*.*",
            .DefaultExt = ".lcslst2",
            .CheckFileExists = True,
            .CheckPathExists = True,
            .Multiselect = False
        }
        If dialog.ShowDialog(Me) <> True Then Return
        OpenRapidFirePreset(dialog.FileName, True)
    End Sub

    Private Sub OpenRapidFirePreset(filePath As String, showSuccessMessage As Boolean)
        Try
            Dim fileInfo As New FileInfo(filePath)
            If fileInfo.Length > RapidFirePresetCodec.MaximumFileSize Then Throw New InvalidDataException("文件超过 16 MiB。")
            Dim isLegacy As Boolean = String.Equals(Path.GetExtension(filePath), ".lcslst", StringComparison.OrdinalIgnoreCase)
            Dim preset As RapidFirePreset
            If isLegacy Then
                Dim legacyItems As List(Of RapidFireItem) = File.ReadAllText(filePath, New UTF8Encoding(False, True)).Split(New String() {"★"}, StringSplitOptions.None).Select(Function(text) RapidFireItem.CreateText(text)).ToList()
                If legacyItems.Count < 1 OrElse legacyItems.Count > RapidFirePresetCodec.MaximumItemCount Then Throw New InvalidDataException("经典版条目数量无效。")
                Dim currentInterval As Integer = 100
                Integer.TryParse(TxtSendInterval.Text, currentInterval)
                preset = New RapidFirePreset With {
                    .IntervalMilliseconds = Math.Max(50, Math.Min(1000000, currentInterval)),
                    .SendLoop = ChkSendLoop.IsChecked.GetValueOrDefault(False),
                    .Items = legacyItems
                }
            Else
                preset = RapidFirePresetCodec.Deserialize(File.ReadAllBytes(filePath))
            End If

            For Each item As RapidFireItem In preset.Items.Where(Function(value) value.Kind = RapidFireItemKind.Image)
                LoadBitmap(item.ImageBytes)
            Next

            If isSending Then StopSend()
            sendPhrases.Clear()
            sendPhrases.AddRange(preset.Items)
            currentSendIndex = 0
            TxtSendInterval.Text = preset.IntervalMilliseconds.ToString(Globalization.CultureInfo.InvariantCulture)
            ChkSendLoop.IsChecked = preset.SendLoop
            UpdateItemDisplay()
            If showSuccessMessage Then ShowMyMessage("连发预设已打开。")
        Catch ex As Exception
            ShowMyMessage("无法打开连发预设：" & ex.Message)
        End Try
    End Sub

    Private Shared Function IsSendableItem(item As RapidFireItem) As Boolean
        Return item IsNot Nothing AndAlso ((item.Kind = RapidFireItemKind.Image AndAlso item.ImageBytes IsNot Nothing AndAlso item.ImageBytes.Length > 0) OrElse (item.Kind = RapidFireItemKind.Text AndAlso Not String.IsNullOrWhiteSpace(item.Text)))
    End Function

    Private Shared Function CloneSendItem(item As RapidFireItem) As RapidFireItem
        Return New RapidFireItem With {
            .Kind = item.Kind,
            .Text = item.Text,
            .FileName = item.FileName,
            .ImageBytes = If(item.ImageBytes Is Nothing, Nothing, DirectCast(item.ImageBytes.Clone(), Byte()))
        }
    End Function

    Private Shared Function LoadBitmap(imageBytes As Byte()) As BitmapSource
        If imageBytes Is Nothing OrElse imageBytes.Length = 0 Then Throw New InvalidDataException("图片内容为空。")
        Using stream As New MemoryStream(imageBytes, False)
            Dim bitmap As New BitmapImage()
            bitmap.BeginInit()
            bitmap.CacheOption = BitmapCacheOption.OnLoad
            bitmap.StreamSource = stream
            bitmap.EndInit()
            bitmap.Freeze()
            Return bitmap
        End Using
    End Function

    Private Sub SetImageClipboardData(imageBytes As Byte())
        Dim bitmap As BitmapSource = LoadBitmap(imageBytes)
        Dim pngBytes As Byte()
        Using pngStream As New MemoryStream()
            Dim encoder As New PngBitmapEncoder()
            encoder.Frames.Add(BitmapFrame.Create(bitmap))
            encoder.Save(pngStream)
            pngBytes = pngStream.ToArray()
        End Using

        activeImageClipboardData = New DataObject()
        activeImageClipboardData.SetImage(bitmap)
        activeImageClipboardData.SetData("PNG", New MemoryStream(pngBytes, False), False)
        'copy=False 避免 OLE 同步持久化被第三方剪贴板监听器无限阻塞。
        Clipboard.SetDataObject(activeImageClipboardData, False)
    End Sub

#End Region

#Region "Others"
    Private Sub OnUserPreferenceChanged(sender As Object, e As UserPreferenceChangedEventArgs)
        If Not Dispatcher.CheckAccess() Then
            Dispatcher.BeginInvoke(New Action(Of Object, UserPreferenceChangedEventArgs)(AddressOf OnUserPreferenceChanged), sender, e)
            Return
        End If
        '检查用户是否在程序打开时切换了系统主题或强调色
        If e.Category = UserPreferenceCategory.General Then
            '检测是否设置为自动跟随系统主题
            If ReadSetting("DoAutoSwitchTheme", 0) = 1 Then
                '判断当前是否启用深色模式
                Dim isDarkMode As Boolean = IsDarkModeEnabled()
                '切换主题到深色或浅色模式
                SwitchTheme(isDarkMode)
                WriteSetting("IsDarkMode", If(isDarkMode, 1, 0))
            End If
        End If
        If e.Category = UserPreferenceCategory.Color OrElse e.Category = UserPreferenceCategory.General Then
            ApplySystemAccentColor()
        End If
    End Sub
    '欢迎界面的底部链接
    Private Sub Button_Click_1(sender As Object, e As RoutedEventArgs)
        Process.Start("https://sysbbs.cn/")
    End Sub

    Private Sub Button_Click_2(sender As Object, e As RoutedEventArgs)
        Process.Start("https://qm.qq.com/q/SIZ1MaTKoe")
    End Sub

    Private Sub MainWindow1_Closing(sender As Object, e As ComponentModel.CancelEventArgs) Handles MyBase.Closing
        Visibility = Visibility.Hidden
        e.Cancel = True
    End Sub

    Private Sub ToggleButton_Click(sender As Object, e As RoutedEventArgs)
        If pinButton.IsChecked = True Then
            Topmost = True
            pinButton.SetResourceReference(ContentControl.ContentProperty, "Icon.Unpin")
        Else
            Topmost = False
            pinButton.SetResourceReference(ContentControl.ContentProperty, "Icon.Pin")
        End If
    End Sub

    Public Sub ShowWindow()
        ShowInTaskbar = True
        Show()
    End Sub

    Public Sub ToggleMainWindowVisibility()
        If IsVisible AndAlso WindowState <> WindowState.Minimized Then
            Hide()
            Return
        End If

        ShowInTaskbar = True
        Show()
        WindowState = WindowState.Normal
        Activate()
        SetForegroundWindow(New WindowInteropHelper(Me).Handle)
    End Sub

    '检查更新
    Private Async Sub UpdButton_Click(sender As Object, e As RoutedEventArgs)
        UpdateButton.IsEnabled = False
        UpdateProgressRing.Visibility = Visibility.Visible
        Using httpClient As HttpClient = UpdateModule.CreateHttpClient()
            Dim zipPath As String = Nothing
            Dim extractPath As String = Nothing
            Try
                Dim release As UpdateRelease = Await UpdateModule.GetLatestReleaseAsync(httpClient)
                Dim latestVersion As Version = UpdateModule.GetReleaseVersion(release)
                Dim currentVersion As Version = My.Application.Info.Version
                If latestVersion <= currentVersion Then
                    ShowMyMessage("当前已是最新版本")
                    Return
                End If

                Dim asset As UpdateAsset = UpdateModule.GetZipAsset(release)
                Dim answer As MsgBoxResult = MsgBox(
                    "检测到新版本，是否立即下载并启动？" & vbCrLf &
                    "当前版本：" & currentVersion.ToString() & vbCrLf &
                    "最新版本：" & latestVersion.ToString(),
                    MsgBoxStyle.YesNo Or MsgBoxStyle.Question,
                    "更新提示")
                If answer <> MsgBoxResult.Yes Then
                    Return
                End If
                Dim updateRoot As String = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LCS", "keyboardmgr2", "updates")
                Directory.CreateDirectory(updateRoot)
                zipPath = Path.Combine(updateRoot, "update-" & Guid.NewGuid().ToString("N") & ".zip")
                extractPath = Path.Combine(updateRoot, latestVersion.ToString() & "-" & Guid.NewGuid().ToString("N"))
                Await UpdateModule.DownloadUpdateAsync(httpClient, New Uri(asset.browser_download_url, UriKind.Absolute), zipPath)
                UpdateModule.VerifyFileSha256(zipPath, asset.digest)
                UpdateModule.ExtractUpdateSafely(zipPath, extractPath)
                Dim executablePath As String = UpdateModule.FindUpdateExecutable(extractPath)

                Dim currentProcessId As Integer = Diagnostics.Process.GetCurrentProcess().Id
                Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo With {
                    .FileName = executablePath,
                    .Arguments = "--wait-for-pid=" & currentProcessId.ToString(Globalization.CultureInfo.InvariantCulture),
                    .WorkingDirectory = Path.GetDirectoryName(executablePath),
                    .UseShellExecute = True
                })
                FloatingWindow.Instance.StopApp()
            Catch ex As HttpRequestException
                ShowMyMessage("获取更新失败：无法从GitHub拉取更新，请检查您的网络环境" & vbCrLf & ex.Message)
            Catch ex As TaskCanceledException
                ShowMyMessage("获取更新失败：从GitHub拉取更新超时，请检查您的网络环境。")
            Catch ex As Exception
                If extractPath IsNot Nothing AndAlso Directory.Exists(extractPath) Then
                    Try
                        Directory.Delete(extractPath, True)
                    Catch
                    End Try
                End If
                ShowMyMessage("获取更新失败：" & ex.Message)
            Finally
                If zipPath IsNot Nothing AndAlso File.Exists(zipPath) Then
                    Try
                        File.Delete(zipPath)
                    Catch
                    End Try
                End If
                UpdateButton.IsEnabled = True
                UpdateProgressRing.Visibility = Visibility.Collapsed
            End Try
        End Using
    End Sub

    Private Sub UpdDatabtn_Click(sender As Object, e As RoutedEventArgs)
        Try
            Process.Start("https://sysbbs.cn/d/512")
        Catch ex As Exception
            ShowMyMessage("无法打开更新日志：" & ex.Message)
        End Try
    End Sub

#End Region

End Class
