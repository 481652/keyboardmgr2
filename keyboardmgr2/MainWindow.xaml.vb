'主窗体代码
Imports System.Globalization
Imports System.IO
Imports System.IO.Compression
Imports System.Linq
Imports System.Net.Http
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Windows.Interop
Imports System.Windows.Media
Imports System.Windows.Media.Animation
Imports System.Windows.Threading
Imports Microsoft.Win32
Imports WindowSelector



Public Class MainWindow1

#Region "DllImports&Veriables"

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

    <DllImport("user32.dll")>
    Private Shared Function IsIconic(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function ShowWindowAsync(hWnd As IntPtr, nCmdShow As Integer) As Boolean
    End Function

    <DllImport("user32.dll")>
    Private Shared Function BringWindowToTop(hWnd As IntPtr) As Boolean
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECT
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <DllImport("user32.dll")>
    Private Shared Function GetWindowRect(hWnd As IntPtr, ByRef lpRect As RECT) As Boolean
    End Function

    Private Const SW_RESTORE As Integer = 9

    '尝试把目标窗口激活到前台（最小化时先还原），返回最终是否处于前台
    Private Function TryActivateWindow(hwnd As IntPtr) As Boolean
        If hwnd = IntPtr.Zero OrElse Not IsWindow(hwnd) Then Return False
        Try
            If IsIconic(hwnd) Then ShowWindowAsync(hwnd, SW_RESTORE)
            For attempt As Integer = 1 To 4
                If GetForegroundWindow() = hwnd Then Return True
                SetForegroundWindow(hwnd)
                BringWindowToTop(hwnd)
                Threading.Thread.Sleep(60)
            Next
        Catch
        End Try
        Return GetForegroundWindow() = hwnd
    End Function

    Dim savedkeys As New List(Of Key)

    '窗口位置记忆
    Private ReadOnly windowPlacementTimer As New DispatcherTimer(DispatcherPriority.Background)
    Private Const WindowPlacementSaveDelayMilliseconds As Integer = 800
    '实验性功能
    Public isExperimentalFeatureEnabled As Boolean = ReadSetting("IsExperimentalFeatureEnabled", 0) = 1

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
        GitID.Text = "Git ID：获取中..."
        LoadLatestCommitId()
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
        '创建第一条“连发”项目
        SaveCurrentItem()
        sendPhrases.Add(RapidFireItem.CreateText(""))
        currentSendIndex = sendPhrases.Count - 1
        UpdateItemDisplay()
        '检查实验性功能（脚本）
        If isExperimentalFeatureEnabled Then
            ShowMyMessage("注意：您已启用实验性功能，可能会导致程序不稳定或异常，请谨慎使用。")
        Else
            '隐藏一切跟脚本有关的控件
            floatingWindow.scriptButton.Visibility = Visibility.Collapsed
            Dim scriptTab As TabItem = TabControl1.Items(5)
            scriptTab.Visibility = Visibility.Collapsed
            BtnRecordExport.Visibility = Visibility.Collapsed
            '同时托盘右键也会隐藏，代码见MyTrayicon.vb
        End If
    End Sub

    Private Async Sub LoadLatestCommitId()
        Using httpClient As HttpClient = UpdateModule.CreateHttpClient()
            Try
                Dim commitId As String = Await UpdateModule.GetReleaseCommitIdAsync(httpClient, My.Application.Info.Version)
                GitID.Text = If(commitId Is Nothing, "Git ID：未发布", "Git ID：" & commitId)
            Catch
                GitID.Text = "Git ID：获取失败"
            End Try
        End Using
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
        WriteSetting("DoClickHold", 0)
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
        InitializeOpCoreSettings()
        InitializeHotkeySettings()
        InitializeRecordSettings()
        InitializeScriptSettings()
        UpdateItemDisplay()
        ApplyStartupTabSelection()
        '开机自启有两种方式且互斥：计划任务（登录时提权启动）优先于注册表启动项。
        '两者同时存在会导致登录时启动两次，先启动的未提权实例会抢占单实例，导致提权的那份直接退出。
        Dim useTaskMode As Boolean = ReadSetting("DoRegisterAutoStartTask", 0) = 1
        Dim useRunKeyMode As Boolean = ReadSetting("DoAutoStart", 0) = 1
        If useTaskMode Then
            DoAutoStartCheckbox.IsChecked = True
            DoRegisterAutoStartTaskComboBox.SelectedIndex = 0
            If useRunKeyMode Then
                '清理历史遗留的重复注册
                WriteSetting("DoAutoStart", 0)
            End If
            Try
                UpdateAutoStartRegistration(False)
            Catch
            End Try
            '只有任务确实缺失/过期时才会请求提权，正常情况下这里什么都不做
            LaunchOnSetupModule.EnsureTaskRegistered()
        ElseIf useRunKeyMode Then
            DoAutoStartCheckbox.IsChecked = True
            DoRegisterAutoStartTaskComboBox.SelectedIndex = 1
            Try
                UpdateAutoStartRegistration(True)
            Catch
                MessageBox.Show("注册开机启动项失败。", "失败", MessageBoxButton.OK, MessageBoxImage.Error)
            End Try
        Else
            DoAutoStartCheckbox.IsChecked = False
            DoRegisterAutoStartTaskComboBox.SelectedIndex = 1
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
            CheckBox4.IsChecked = ReadSetting("DoClickHold", 0) = 1
            If CheckBox4.IsChecked = True Then CheckBox1.IsChecked = False
            CheckBox1.IsEnabled = CheckBox4.IsChecked <> True
            UpdateClickIntervalVisibility()
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
            '加载自定义键连点键值（空键值属于正常默认状态，不提示）
            Dim ClickKeys_str = ReadSetting("ClickKeys", "")
            savedkeys = LoadKeyData(ClickKeys_str)
            If ClickKeys_str <> "" AndAlso IsNoneKeyList(savedkeys) Then
                ShowMyMessage("无法加载连点设置。")
            End If
        End If
    End Sub

    Private Sub InitializeOpCoreSettings()
        '迁移旧版“连点内核”设置键到“操作内核”
        If ReadSetting("OpCoreEngine", -1).Equals(-1) Then
            WriteSetting("OpCoreEngine", ReadSetting("ClickCoreEngine", 0))
            DeleteSetting("ClickCoreEngine")
        End If

        '加载操作内核选项、连发发送键与发送方式选项，并初始化操作内核分发层
        Dim engineMode As Integer = 0
        If Not Integer.TryParse(ReadSetting("OpCoreEngine", 0).ToString(), engineMode) OrElse engineMode < 0 OrElse engineMode > 2 Then engineMode = 0
        Combobox3.SelectedIndex = engineMode

        Dim sendKey As Integer = 0
        If Not Integer.TryParse(ReadSetting("RapidFireSendKey", 0).ToString(), sendKey) OrElse sendKey < 0 OrElse sendKey > 1 Then sendKey = 0
        Combobox4.SelectedIndex = sendKey
        rapidFireSendKey = sendKey

        Dim sendMethod As Integer = 0
        If Not Integer.TryParse(ReadSetting("RapidFireSendMethod", 0).ToString(), sendMethod) OrElse sendMethod < 0 OrElse sendMethod > 2 Then sendMethod = 0
        Combobox5.SelectedIndex = sendMethod
        rapidFireSendMethod = sendMethod

        OpEngine.Initialize()
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
        '加载录制开关热键
        Dim recordHotkeyStr As String = ReplaceReservedHotkey("RecordHotkeys", "Ctrl+F4", reservedHotkeyReplaced)
        If recordHotkeyStr <> "" Then
            KeyTextbox7.Text = recordHotkeyStr
            Dim keyList = LoadKeyData(recordHotkeyStr)
            recordHotkeys = ConvertKeyLogToVirtualKeyCodes(keyList)
            If IsNoneKeyList(keyList) Then
                ShowMyMessage("无法加载录制快捷键设置。")
            End If
        Else
            recordHotkeyStr = "Ctrl+F4"
            WriteSetting("RecordHotkeys", recordHotkeyStr)
            KeyTextbox7.Text = recordHotkeyStr
            recordHotkeys = ConvertKeyLogToVirtualKeyCodes(LoadKeyData(recordHotkeyStr))
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
        '注册录制开关热键
        If recordHotkeys.Count > 0 Then
            floatingWindow.RegisterGlobalHotkey(recordHotkeys, 9005)
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
        AddHandler MacroPlayer.PlaybackStopped, AddressOf MacroPlayer_PlaybackStopped
        AddHandler ScriptRunner.OutputReceived, AddressOf ScriptRunner_OutputReceived
        AddHandler ScriptRunner.StateChanged, AddressOf ScriptRunner_StateChanged
        InitializeTextBoxKeyHandler(KeyTextbox1)
        InitializeTextBoxKeyHandler(KeyTextbox2)
        InitializeTextBoxKeyHandler(KeyTextbox3)
        InitializeTextBoxKeyHandler(KeyTextbox4)
        InitializeTextBoxKeyHandler(KeyTextbox5)
        InitializeTextBoxKeyHandler(KeyTextbox6)
        InitializeTextBoxKeyHandler(KeyTextbox7)
        windowPlacementTimer.Interval = TimeSpan.FromMilliseconds(WindowPlacementSaveDelayMilliseconds)
        AddHandler windowPlacementTimer.Tick, AddressOf WindowPlacementTimer_Tick
        AddHandler LocationChanged, AddressOf MainWindow_LocationOrSizeChanged
        AddHandler SizeChanged, AddressOf MainWindow_LocationOrSizeChanged
        RestoreWindowPlacement()
        _instance = Me
    End Sub

    '恢复上次保存的窗口位置与大小（位置不在任何屏幕内时忽略，回退到默认居中）
    Public Sub RestoreWindowPlacement()
        Dim savedLeft As Double
        Dim savedTop As Double
        Dim savedWidth As Double
        Dim savedHeight As Double
        If Not TryReadWindowPlacement(savedLeft, savedTop, savedWidth, savedHeight) Then Return
        If savedWidth < MinWidth Then savedWidth = MinWidth
        If savedHeight < MinHeight Then savedHeight = MinHeight
        If Not IsWindowPlacementVisible(New System.Windows.Rect(savedLeft, savedTop, savedWidth, savedHeight)) Then Return
        WindowStartupLocation = WindowStartupLocation.Manual
        Me.Left = savedLeft
        Me.Top = savedTop
        Me.Width = savedWidth
        Me.Height = savedHeight
    End Sub

    Private Function TryReadWindowPlacement(ByRef savedLeft As Double, ByRef savedTop As Double, ByRef savedWidth As Double, ByRef savedHeight As Double) As Boolean
        Dim leftValue As Object = ReadSetting("MainWindowLeft", Nothing)
        Dim topValue As Object = ReadSetting("MainWindowTop", Nothing)
        Dim widthValue As Object = ReadSetting("MainWindowWidth", Nothing)
        Dim heightValue As Object = ReadSetting("MainWindowHeight", Nothing)
        If leftValue Is Nothing OrElse topValue Is Nothing OrElse widthValue Is Nothing OrElse heightValue Is Nothing Then Return False
        Return Double.TryParse(leftValue.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, savedLeft) AndAlso
               Double.TryParse(topValue.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, savedTop) AndAlso
               Double.TryParse(widthValue.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, savedWidth) AndAlso
               Double.TryParse(heightValue.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, savedHeight)
    End Function

    '标题栏至少要有一部分落在虚拟屏幕内，避免显示器变更后窗口停在屏幕外
    Private Function IsWindowPlacementVisible(bounds As System.Windows.Rect) As Boolean
        If bounds.Width <= 0 OrElse bounds.Height <= 0 Then Return False
        Dim virtualScreen As New System.Windows.Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                      SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight)
        Dim titleBar As New System.Windows.Rect(bounds.Left, bounds.Top, Math.Min(bounds.Width, 120), Math.Min(bounds.Height, 30))
        Return virtualScreen.IntersectsWith(titleBar)
    End Function

    Private Sub MainWindow_LocationOrSizeChanged(sender As Object, e As EventArgs)
        windowPlacementTimer.Stop()
        windowPlacementTimer.Start()
    End Sub

    Private Sub WindowPlacementTimer_Tick(sender As Object, e As EventArgs)
        windowPlacementTimer.Stop()
        SaveWindowPlacement()
    End Sub

    Private Sub SaveWindowPlacement()
        Dim bounds As System.Windows.Rect = If(WindowState = WindowState.Normal,
                                New System.Windows.Rect(Left, Top, ActualWidth, ActualHeight),
                                RestoreBounds)
        If bounds.IsEmpty OrElse Not IsWindowPlacementVisible(bounds) Then Return
        WriteSetting("MainWindowLeft", bounds.Left.ToString(CultureInfo.InvariantCulture))
        WriteSetting("MainWindowTop", bounds.Top.ToString(CultureInfo.InvariantCulture))
        WriteSetting("MainWindowWidth", bounds.Width.ToString(CultureInfo.InvariantCulture))
        WriteSetting("MainWindowHeight", bounds.Height.ToString(CultureInfo.InvariantCulture))
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
        '保存录制开关热键
        If KeyTextbox7.Text <> "" Then
            WriteSetting("RecordHotkeys", KeyTextbox7.Text)
        End If
        '保存录制选项
        Dim recordSampleRate As Integer
        If Integer.TryParse(TxtRecordSampleRate.Text, recordSampleRate) AndAlso recordSampleRate >= 1 AndAlso recordSampleRate <= 1000 Then
            WriteSetting("RecordSampleRateMs", recordSampleRate)
        End If
        WriteSetting("RecordPlaybackSmooth", If(ChkPlaybackSmooth.IsChecked, 1, 0))
        WriteSetting("RecordPlaybackLoop", If(ChkPlaybackLoop.IsChecked, 1, 0))
        WriteSetting("RecordPlaybackBlockInput", If(ChkPlaybackBlockInput.IsChecked, 1, 0))
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
        '检测开机自启（两种方式互斥：计划任务 / 注册表启动项，避免登录时启动两次）
        Try
            If DoAutoStartCheckbox.IsChecked = True Then
                Select Case DoRegisterAutoStartTaskComboBox.SelectedIndex
                    Case 0
                        '计划任务方式：登录时以最高权限启动，同时清掉注册表启动项
                        WriteSetting("DoRegisterAutoStartTask", 1)
                        WriteSetting("DoAutoStart", 0)
                        UpdateAutoStartRegistration(False)
                        If LaunchOnSetupModule.IsRunningAsAdministrator() Then
                            LaunchOnSetupModule.RegisterTask()
                        Else
                            '交由提权后的新实例执行（带 --task=register）
                            LaunchOnSetupModule.RestartToRequestAdmin("register")
                        End If
                    Case 1
                        '注册表启动项方式：写完启动项后清掉可能存在的计划任务
                        WriteSetting("DoAutoStart", 1)
                        WriteSetting("DoRegisterAutoStartTask", 0)
                        UpdateAutoStartRegistration(True)
                        If LaunchOnSetupModule.TaskExists() Then
                            If LaunchOnSetupModule.IsRunningAsAdministrator() Then
                                LaunchOnSetupModule.EraseTask()
                            Else
                                LaunchOnSetupModule.RestartToRequestAdmin("delete")
                            End If
                        End If
                End Select
            Else
                WriteSetting("DoAutoStart", 0)
                WriteSetting("DoRegisterAutoStartTask", 0)
                '先完成不需要提权的清理，需要时才重启提权删除计划任务
                UpdateAutoStartRegistration(False)
                If LaunchOnSetupModule.TaskExists() Then
                    If LaunchOnSetupModule.IsRunningAsAdministrator() Then
                        LaunchOnSetupModule.EraseTask()
                    Else
                        LaunchOnSetupModule.RestartToRequestAdmin("delete")
                    End If
                End If
            End If
        Catch ex As Exception
            ShowMyMessage("设置开机自启出错，错误内容：" & ex.Message)
        End Try

        '保存操作内核选项（立即生效）
        If Combobox3.SelectedIndex < 0 OrElse Combobox3.SelectedIndex > 2 Then
            ShowMyMessage("无法保存设置：请选择操作内核。")
            Return
        End If
        WriteSetting("OpCoreEngine", Combobox3.SelectedIndex)
        OpEngine.Initialize()
        '保存连发发送键选项（立即生效）
        If Combobox4.SelectedIndex < 0 OrElse Combobox4.SelectedIndex > 1 Then
            ShowMyMessage("无法保存设置：请选择连发发送键。")
            Return
        End If
        WriteSetting("RapidFireSendKey", Combobox4.SelectedIndex)
        rapidFireSendKey = Combobox4.SelectedIndex
        '保存连发发送方式选项（立即生效）
        If Combobox5.SelectedIndex < 0 OrElse Combobox5.SelectedIndex > 2 Then
            ShowMyMessage("无法保存设置：请选择连发发送方式。")
            Return
        End If
        WriteSetting("RapidFireSendMethod", Combobox5.SelectedIndex)
        rapidFireSendMethod = Combobox5.SelectedIndex
        ThemeModule.UpdateWindowBackdrops()
    End Sub

    Private Shared Sub UpdateAutoStartRegistration(enabled As Boolean)
        Dim appPath As String = Process.GetCurrentProcess().MainModule.FileName
        Using runKey As RegistryKey = Registry.CurrentUser.CreateSubKey("SOFTWARE\Microsoft\Windows\CurrentVersion\Run")
            If enabled Then
                runKey.SetValue("keyboardmgr2", Chr(34) & appPath & Chr(34) & " --autostart")
            ElseIf runKey.GetValue("keyboardmgr2") IsNot Nothing Then
                runKey.DeleteValue("keyboardmgr2")
            End If
        End Using
        Debug.WriteLine("Auto-start registration updated: " & enabled.ToString())
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
            Tuple.Create("主界面显示/隐藏快捷键", KeyTextbox6.Text),
            Tuple.Create("录制开关快捷键", KeyTextbox7.Text)
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

#Region "ListBoxContextMenu"
    'listbox的右键菜单

    '右键点击时先选中指针下的项，使右键菜单作用于正确的目标
    Private Sub RecordList_PreviewMouseRightButtonDown(sender As Object, e As MouseButtonEventArgs)
        Dim container As ListBoxItem = FindListBoxItemContainer(RecordList, e.OriginalSource)
        If container IsNot Nothing Then container.IsSelected = True
    End Sub

    Private Sub SendItemList_PreviewMouseRightButtonDown(sender As Object, e As MouseButtonEventArgs)
        Dim container As ListBoxItem = FindListBoxItemContainer(SendItemList, e.OriginalSource)
        If container IsNot Nothing Then container.IsSelected = True
    End Sub

    '录制列表右键菜单
    Private Sub RecordContextMenu_Opened(sender As Object, e As RoutedEventArgs)
        Dim menu As ContextMenu = TryCast(sender, ContextMenu)
        If menu Is Nothing Then Return
        Dim isRecording As Boolean = InputRecorder.IsRecording
        Dim isPlaying As Boolean = MacroPlayer.IsPlaying
        Dim hasSelection As Boolean = currentRecordingIndex >= 0 AndAlso currentRecordingIndex < recordings.Count
        SetMenuVisibility(mnuRecordStart, Not isRecording AndAlso Not isPlaying)
        SetMenuVisibility(mnuRecordStop, isRecording)
        SetMenuVisibility(mnuRecordPlay, hasSelection AndAlso Not isRecording AndAlso Not isPlaying)
        SetMenuVisibility(mnuRecordStopPlay, isPlaying)
        SetMenuVisibility(mnuRecordSave, hasSelection)
        SetMenuVisibility(mnuRecordOpen, Not isRecording AndAlso Not isPlaying)
        SetMenuVisibility(mnuRecordRename, hasSelection AndAlso Not isRecording)
        SetMenuVisibility(mnuRecordDelete, hasSelection AndAlso Not isRecording)
        SetMenuVisibility(mnuRecordExport, hasSelection)
        RefreshMenuSeparators(menu)
        ApplyMenuBackdropAsync(menu)
    End Sub

    '连发列表右键菜单
    Private Sub SendItemContextMenu_Opened(sender As Object, e As RoutedEventArgs)
        Dim menu As ContextMenu = TryCast(sender, ContextMenu)
        If menu Is Nothing Then Return
        Dim hasSelection As Boolean = currentSendIndex >= 0 AndAlso currentSendIndex < sendPhrases.Count
        Dim canAdd As Boolean = sendPhrases.Count < RapidFirePresetCodec.MaximumItemCount
        '没有选中项时提供新增；选中项时提供上移/下移/删除/在后面插入。
        SetMenuVisibility(mnuSendAdd, Not hasSelection)
        SetMenuVisibility(mnuSendAddImage, Not hasSelection AndAlso canAdd)
        SetMenuVisibility(mnuSendMoveUp, hasSelection AndAlso currentSendIndex > 0)
        SetMenuVisibility(mnuSendMoveDown, hasSelection AndAlso currentSendIndex < sendPhrases.Count - 1)
        SetMenuVisibility(mnuSendDelete, hasSelection)
        SetMenuVisibility(mnuSendInsertText, hasSelection AndAlso canAdd)
        SetMenuVisibility(mnuSendInsertImage, hasSelection AndAlso canAdd)
        RefreshMenuSeparators(menu)
        ApplyMenuBackdropAsync(menu)
    End Sub

    Private Sub SetMenuVisibility(item As Control, visible As Boolean)
        If item Is Nothing Then Return
        item.Visibility = If(visible, Visibility.Visible, Visibility.Collapsed)
    End Sub

    '收起多余的分隔线
    Private Sub RefreshMenuSeparators(menu As ContextMenu)
        If menu Is Nothing Then Return
        Dim items As ItemCollection = menu.Items
        Dim seenVisibleItem As Boolean = False
        Dim lastWasSeparator As Boolean = False
        For index As Integer = 0 To items.Count - 1
            Dim separator As Separator = TryCast(items(index), Separator)
            If separator IsNot Nothing Then
                If Not seenVisibleItem OrElse lastWasSeparator Then
                    separator.Visibility = Visibility.Collapsed
                Else
                    separator.Visibility = Visibility.Visible
                    lastWasSeparator = True
                End If
            Else
                Dim menuItem As MenuItem = TryCast(items(index), MenuItem)
                If menuItem IsNot Nothing AndAlso menuItem.Visibility = Visibility.Visible Then
                    seenVisibleItem = True
                    lastWasSeparator = False
                End If
            End If
        Next

        Dim seenVisibleFromEnd As Boolean = False
        For index As Integer = items.Count - 1 To 0 Step -1
            Dim separator As Separator = TryCast(items(index), Separator)
            If separator IsNot Nothing Then
                If Not seenVisibleFromEnd Then separator.Visibility = Visibility.Collapsed
            Else
                Dim menuItem As MenuItem = TryCast(items(index), MenuItem)
                If menuItem IsNot Nothing AndAlso menuItem.Visibility = Visibility.Visible Then seenVisibleFromEnd = True
            End If
        Next
    End Sub

    '菜单弹出后再应用云母，确保此时弹窗的 HWND 已创建
    Private Sub ApplyMenuBackdropAsync(menu As ContextMenu)
        Dispatcher.BeginInvoke(New Action(Sub() ThemeModule.UpdateMenuBackdrop(menu)))
    End Sub

    Private Function FindListBoxItemContainer(listBox As ListBox, source As Object) As ListBoxItem
        Dim current As DependencyObject = TryCast(source, DependencyObject)
        While current IsNot Nothing AndAlso Not TypeOf current Is ListBoxItem
            If TypeOf current Is Visual Then
                current = VisualTreeHelper.GetParent(current)
            Else
                current = LogicalTreeHelper.GetParent(current)
            End If
        End While
        Return TryCast(current, ListBoxItem)
    End Function
#End Region

#Region "Click"
    '连点
    Private Sub Button_Click_7(sender As Object, e As RoutedEventArgs) '保存连点设置
        Dim Keys As New List(Of UShort) From {}
        If CheckBox4.IsChecked <> True AndAlso Textbox1.Text = "" Then
            ShowMyMessage("无法保存设置：没有指定发送间隔")
            Return
        End If
        WriteSetting("DoClickSettingSaved", 1)
        WriteSetting("DoRandomOffsetOfClickSpeed", If(CheckBox1.IsChecked = True, 1, 0))
        WriteSetting("DoRandomOffsetOfClickPosition", If(CheckBox2.IsChecked = True, 1, 0))
        WriteSetting("DoClickHold", If(CheckBox4.IsChecked = True, 1, 0))
        If CheckBox3.IsChecked = True Then
            WriteSetting("DoCustomizeCursorPos", 1)
            WriteSetting("CursorPosition", Textbox2.Text)
        Else
            WriteSetting("DoCustomizeCursorPos", 0)
        End If
        If Textbox1.Text <> "" Then WriteSetting("ClickInterval", Textbox1.Text)
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

    '长按与速度偏移互斥，与间隔设置互斥
    Private Sub CheckBox1_Click(sender As Object, e As RoutedEventArgs)
        If CheckBox1.IsChecked = True AndAlso CheckBox4.IsChecked = True Then
            CheckBox4.IsChecked = False
            UpdateClickIntervalVisibility()
        End If
        CheckBox4.IsEnabled = Not CheckBox1.IsChecked
    End Sub

    Private Sub CheckBox4_Click(sender As Object, e As RoutedEventArgs)
        If CheckBox4.IsChecked = True AndAlso CheckBox1.IsChecked = True Then
            CheckBox1.IsChecked = False
        End If
        CheckBox1.IsEnabled = Not CheckBox4.IsChecked
        UpdateClickIntervalVisibility()
    End Sub

    Private Sub UpdateClickIntervalVisibility()
        Dim visibility As Visibility = If(CheckBox4.IsChecked = True, Visibility.Collapsed, Visibility.Visible)
        IntervalLabel.Visibility = visibility
        Textbox1.Visibility = visibility
        IntervalUnitLabel.Visibility = visibility
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

    Dim isMouseHoldPending As Boolean = False
    Dim isKeyboardHoldPending As Boolean = False
    Private rnd As New Random

    '显示连点按键设置
    Private Sub RadioButton1_Checked(sender As Object, e As RoutedEventArgs) Handles RadioButton1.Checked
        If KeyPressStackPanel IsNot Nothing Then KeyPressStackPanel.Visibility = Visibility.Collapsed
        If CheckBox4 IsNot Nothing Then CheckBox4.IsEnabled = True
    End Sub
    Private Sub RadioButton2_Checked(sender As Object, e As RoutedEventArgs) Handles RadioButton2.Checked
        If KeyPressStackPanel IsNot Nothing Then KeyPressStackPanel.Visibility = Visibility.Collapsed
        If CheckBox4 IsNot Nothing Then CheckBox4.IsEnabled = True
    End Sub
    Private Sub RadioButton3_Checked(sender As Object, e As RoutedEventArgs) Handles RadioButton3.Checked
        If KeyPressStackPanel IsNot Nothing Then KeyPressStackPanel.Visibility = Visibility.Visible
        If CheckBox4 IsNot Nothing Then CheckBox4.IsEnabled = True
    End Sub
    Private Sub Button_Click_3(sender As Object, e As RoutedEventArgs) '开始连点
        If isClicking Then
            StopClick()
            Return
        End If
        If isSending Then StopSend()
        Dim isLongPress As Boolean = CheckBox4.IsChecked = True
        Dim intervalMs As Integer = 0
        If Not isLongPress Then
            If Textbox1.Text = "" Then
                ShowMyMessage("没有指定发送间隔")
                Return
            End If
            If Not Integer.TryParse(Textbox1.Text, intervalMs) OrElse intervalMs <= 0 Then
                ShowMyMessage("间隔时间不能小于等于0哦")
                Return
            End If
        End If

        Dim speedOffset As Boolean = CheckBox1.IsChecked = True
        Dim posOffset As Boolean = CheckBox2.IsChecked = True
        '键盘长按：按下并保持
        If isLongPress AndAlso RadioButton3.IsChecked = True Then
            Dim holdKeys As List(Of UShort) = PrepareClickKeysList()
            If holdKeys Is Nothing Then Return
            isKeyboardHoldPending = True
            isClicking = True
            Hide()
            floatingWindow.FloatingWindowEvent_Click()
            '等待启动按钮自身的按键抬起事件完成，避免它立即释放模拟的长按
            Dispatcher.BeginInvoke(New Action(Sub() BeginKeyboardHoldCore(holdKeys)), DispatcherPriority.Background)
            Return
        End If
        '准备鼠标基准坐标
        Dim baseX As Integer = 0
        Dim baseY As Integer = 0
        If CheckBox3.IsChecked = True Then '自定义鼠标位置
            If Textbox2.Text = "" OrElse Textbox2.Text = "," Then
                ShowMyMessage("没有指定自定义鼠标位置")
                Return
            Else
                Dim cursorPos As String() = Textbox2.Text.Split(",") '坐标形式：（横坐标,纵坐标）
                baseX = Integer.Parse(cursorPos(0))
                baseY = Integer.Parse(cursorPos(1))
                SetCursorPosition(baseX, baseY)
                ShowMyMessage("已确定鼠标位置：" & baseX & "," & baseY & "请勿移动鼠标！若要停止请按快捷键")
            End If
        ElseIf posOffset OrElse isLongPress Then
            Dim currentPos As POINTAPI
            GetCursorPos(currentPos)
            baseX = currentPos.x
            baseY = currentPos.y
        End If
        If isLongPress AndAlso posOffset Then
            baseX += rnd.Next(-15, 16)
            baseY += rnd.Next(-15, 16)
            SetCursorPosition(baseX, baseY)
        End If

        If isLongPress Then '鼠标长按：按下并保持
            Dim button As Integer = If(RadioButton1.IsChecked = True, 1, 2)
            isMouseHoldPending = True
            isClicking = True
            Hide()
            floatingWindow.FloatingWindowEvent_Click()
            '等待启动按钮自身的鼠标抬起事件完成，避免它立即释放模拟的长按
            Dispatcher.BeginInvoke(New Action(Sub() BeginMouseHoldCore(button)), DispatcherPriority.Background)
        ElseIf RadioButton1.IsChecked = True Then '左键连点
            OpEngine.StartMouseClick(intervalMs, speedOffset, posOffset, baseX, baseY, 1)
            isClicking = True
            Hide()
            floatingWindow.FloatingWindowEvent_Click() '悬浮窗状态更新
        ElseIf RadioButton2.IsChecked = True Then '右键连点 
            OpEngine.StartMouseClick(intervalMs, speedOffset, posOffset, baseX, baseY, 2)
            isClicking = True
            Hide()
            floatingWindow.FloatingWindowEvent_Click() '悬浮窗状态更新
        ElseIf GetKeyLog() IsNot Nothing OrElse savedkeys.Count > 0 Then '自定义键连点
            Dim clickKeys As List(Of UShort) = PrepareClickKeysList()
            If clickKeys Is Nothing Then Return
            OpEngine.StartKeyClick(intervalMs, clickKeys.ToArray())
            isClicking = True
            Hide()
            floatingWindow.FloatingWindowEvent_Click() '悬浮窗状态更新
        Else
            ShowMyMessage("没有指定要发送的键")
            Return
        End If
    End Sub

    Private Sub BeginMouseHoldCore(button As Integer)
        If Not isClicking OrElse Not isMouseHoldPending Then Return
        isMouseHoldPending = False
        OpEngine.StartMouseHold(button)
    End Sub

    Private Sub BeginKeyboardHoldCore(keys As List(Of UShort))
        If Not isClicking OrElse Not isKeyboardHoldPending Then Return
        isKeyboardHoldPending = False
        OpEngine.StartKeyHold(keys.ToArray())
    End Sub

    Private Function PrepareClickKeysList() As List(Of UShort)
        Dim keys As New List(Of UShort)
        If IsNoneKeyList(savedkeys) Then '没有有效的已保存按键时使用当前输入
            If GetKeyLog() Is Nothing Then
                ShowMyMessage("没有指定要发送的键")
                Return Nothing
            End If
            For Each value As Byte In ConvertKeyLogToVirtualKeyCodes(GetKeyLog)
                keys.Add(value)
            Next
        Else
            For Each value As Byte In ConvertKeyLogToVirtualKeyCodes(savedkeys)
                keys.Add(value)
            Next
        End If
        If keys.Count < 1 OrElse keys.Count > 4 Then
            ShowMyMessage("无法发送该（快捷）键")
            Return Nothing
        End If
        Return keys
    End Function

    Public Sub StopClick()
        isMouseHoldPending = False
        isKeyboardHoldPending = False
        OpEngine.StopAll()
        isClicking = False
        floatingWindow.FloatingWindow_Reset()
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
    Dim rapidFireSendKey As Integer = 0 '0=Enter，1=Shift+Enter
    Dim rapidFireSendMethod As Integer = 0 '0=快捷键，1=点击发送按钮，2=自动（先按钮后快捷键）
    Private sendTargetWindowManual As IntPtr = IntPtr.Zero '手动选取的连发目标窗口（会话内记忆）
    Private sendTargetWindowCandidate As IntPtr = IntPtr.Zero '启动连发时的前台窗口候选
    Private inputBoxCaptured As Boolean = False '是否已点选输入框位置
    Private inputBoxRelativeX As Integer = 0 '输入框相对目标窗口左上角的坐标
    Private inputBoxRelativeY As Integer = 0
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

    '添加连发项
    Private Sub BtnAddItem_Click(sender As Object, e As RoutedEventArgs)
        SaveCurrentItem()
        sendPhrases.Add(RapidFireItem.CreateText(String.Empty))
        currentSendIndex = sendPhrases.Count - 1
        UpdateItemDisplay()
        RichTextBox1.Focus()
    End Sub

    '在当前项后面插入文字项（右键菜单使用）
    Private Sub BtnInsertTextAfterItem_Click(sender As Object, e As RoutedEventArgs)
        SaveCurrentItem()
        If sendPhrases.Count >= RapidFirePresetCodec.MaximumItemCount Then
            ShowMyMessage("连发预设最多支持10000个条目。")
            Return
        End If
        Dim insertIndex As Integer = If(currentSendIndex >= 0, currentSendIndex + 1, sendPhrases.Count)
        sendPhrases.Insert(insertIndex, RapidFireItem.CreateText(String.Empty))
        currentSendIndex = insertIndex
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
        '记录启动时的前台窗口（若不属于本程序）作为目标候选，提升自动识别成功率
        Dim startFg As IntPtr = GetForegroundWindow()
        Dim startFgPid As UInteger = 0
        If startFg <> IntPtr.Zero Then GetWindowThreadProcessId(startFg, startFgPid)
        If startFg <> IntPtr.Zero AndAlso startFgPid <> CUInt(Diagnostics.Process.GetCurrentProcess().Id) Then
            sendTargetWindowCandidate = startFg
        Else
            sendTargetWindowCandidate = IntPtr.Zero
        End If
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

        '优先使用手动选取的目标窗口
        If sendTargetWindowManual <> IntPtr.Zero Then
            If IsWindow(sendTargetWindowManual) Then
                sendTargetWindow = sendTargetWindowManual
                SendNextPhrase()
                If isSending AndAlso Not imagePastePending Then timerSend.Start()
                Return
            End If
            '手动选取的窗口已关闭，恢复为自动识别
            sendTargetWindowManual = IntPtr.Zero
            If SelectedSendTargetLabel IsNot Nothing Then SelectedSendTargetLabel.Text = "目标窗口：自动识别"
        End If

        '其次使用启动时记录的窗口候选
        If sendTargetWindowCandidate <> IntPtr.Zero Then
            If IsWindow(sendTargetWindowCandidate) Then
                sendTargetWindow = sendTargetWindowCandidate
                SendNextPhrase()
                If isSending AndAlso Not imagePastePending Then timerSend.Start()
                Return
            End If
            sendTargetWindowCandidate = IntPtr.Zero
        End If

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
            ShowMyMessage("无法确定连发目标窗口，请先切换到目标窗口后使用连发热键，或点击""手动选取""按钮指定。")
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

    '根据选项发送连发“发送键”：Enter 或 Shift+Enter
    Private Sub SendSendKeyForSetting()
        UserInputHandler.SendSendKey(rapidFireSendKey = 1)
    End Sub

    '按“连发发送方式”执行发送：快捷键 / 点击发送按钮 / 自动（先按钮后快捷键）
    Private Sub SendPhrase()
        Select Case rapidFireSendMethod
            Case 1 '点击发送按钮
                If Not UserInputHandler.TryInvokeSendButton(sendTargetWindow) Then
                    StopSend()
                    ShowMyMessage("未在目标窗口中找到""发送""按钮，任务已停止。可改用""快捷键""或""自动""发送方式。")
                End If
            Case 2 '自动：先按钮后快捷键
                If Not UserInputHandler.TryInvokeSendButton(sendTargetWindow) Then
                    SendSendKeyForSetting()
                End If
            Case Else '快捷键
                SendSendKeyForSetting()
        End Select
    End Sub

    '选取连发目标窗口（会话内记忆，窗口关闭后自动恢复为自动识别）
    Private Sub BtnPickSendTarget_Click(sender As Object, e As RoutedEventArgs)
        Try
            Dim selectorForm As New WindowSelectorDlg()
            If selectorForm.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
                Dim selectedHwnd As IntPtr = selectorForm.SelectedWindowHwnd
                If selectedHwnd = IntPtr.Zero Then
                    ShowMyMessage("未选取有效窗体，请重试。")
                    Return
                End If
                sendTargetWindowManual = selectedHwnd
                inputBoxCaptured = False
                Dim title As String = LoafModule.GetWindowTitle(selectedHwnd)
                If SelectedSendTargetLabel IsNot Nothing Then
                    If Not String.IsNullOrEmpty(title) Then
                        SelectedSendTargetLabel.Text = "目标窗口：" & title
                    Else
                        SelectedSendTargetLabel.Text = "目标窗口句柄：" & selectedHwnd.ToString()
                    End If
                End If
                ShowMyMessage("连发目标窗口已指定。开始连发时将自动向该窗口发送。")
            End If
        Catch ex As Exception
            ShowExpdlg("错误9：程序选取窗体时遇到错误，可能是LCS窗体选取器（WindowSelector.dll）丢失！", ex.Message & vbLf & ex.StackTrace)
        End Try
    End Sub

    '点选输入框
    Private Sub BtnCaptureInputBox_Click(sender As Object, e As RoutedEventArgs)
        If isSending Then StopSend()
        AddHandler UserInputHandler.PointCaptured, AddressOf OnInputBoxCaptured
        UserInputHandler.StartSelection()
        Hide()
    End Sub

    Private Sub OnInputBoxCaptured(x As Integer, y As Integer, hwnd As IntPtr)
        RemoveHandler UserInputHandler.PointCaptured, AddressOf OnInputBoxCaptured
        UserInputHandler.StopSelection()
        If hwnd = IntPtr.Zero Then
            Show()
            ShowMyMessage("未捕获到点击位置，请重试。")
            Return
        End If
        Dim rect As RECT
        If GetWindowRect(hwnd, rect) Then
            inputBoxRelativeX = x - rect.Left
            inputBoxRelativeY = y - rect.Top
            inputBoxCaptured = True
            sendTargetWindowManual = hwnd
            If SelectedSendTargetLabel IsNot Nothing Then SelectedSendTargetLabel.Text = "目标窗口：已选取（输入框位置已记录）"
            Show()
            Activate()
            ShowMyMessage("输入框位置已记录。开始连发时将点击该位置输入文本。")
        Else
            Show()
            ShowMyMessage("无法获取窗口位置，点选失败，请重试。")
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
        TryActivateWindow(sendTargetWindow)
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
                If inputBoxCaptured Then
                    UserInputHandler.InsertTextToTarget(itemToSend.Text, sendTargetWindow, inputBoxRelativeX, inputBoxRelativeY)
                Else
                    UserInputHandler.InsertTextToTarget(itemToSend.Text, sendTargetWindow)
                End If
                SendPhrase()
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
            SendPhrase()
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
        '让最后一次 Enter 先由目标程序处理，再恢复并激活主窗口
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
        'copy=False 避免 OLE 同步持久化被第三方剪贴板监听器无限阻塞
        Clipboard.SetDataObject(activeImageClipboardData, False)
    End Sub

#End Region

#Region "Record"
    '录制
    Private recordings As New List(Of KbmrRecording)
    Private currentRecordingIndex As Integer = -1

    Private Sub InitializeRecordSettings()
        Dim rate As Integer = 15
        Integer.TryParse(ReadSetting("RecordSampleRateMs", 15).ToString(), rate)
        If rate < 1 OrElse rate > 1000 Then rate = 15
        TxtRecordSampleRate.Text = rate.ToString(Globalization.CultureInfo.InvariantCulture)
        ChkPlaybackSmooth.IsChecked = ReadSetting("RecordPlaybackSmooth", 1) = 1
        ChkPlaybackLoop.IsChecked = ReadSetting("RecordPlaybackLoop", 0) = 1
        ChkPlaybackBlockInput.IsChecked = ReadSetting("RecordPlaybackBlockInput", 0) = 1
        Combobox6.SelectedIndex = 1
        '载入自动保存的历史录制
        recordings = RecordingStore.LoadAll()
        currentRecordingIndex = -1
        RefreshRecordList()
        UpdateRecordButtons()
    End Sub

    '开始录制
    Private Sub Button_Click_6(sender As Object, e As RoutedEventArgs)
        StartRecording()
    End Sub

    Public Sub StartRecording()
        If InputRecorder.IsRecording Then Return
        If MacroPlayer.IsPlaying Then MacroPlayer.StopPlayback()
        If ScriptRunner.IsRunning Then
            ShowMyMessage("脚本正在运行，请先停止脚本后再开始录制。")
            Return
        End If
        Dim rate As Integer
        If Not Integer.TryParse(TxtRecordSampleRate.Text, rate) OrElse rate < 1 OrElse rate > 1000 Then
            ShowMyMessage("鼠标采样频率需为1到1000之间的整数（毫秒）。")
            Return
        End If
        WriteSetting("RecordSampleRateMs", rate)
        InputRecorder.StartRecording(rate)
        RecordStatusLabel.Text = "正在录制……按""停止录制""或录制快捷键结束。"
        UpdateRecordButtons()
        Hide()
        floatingWindow.FloatingWindowEvent_Record()
    End Sub

    '停止录制
    Private Sub Button_Click_8(sender As Object, e As RoutedEventArgs)
        StopRecordingAndSave()
    End Sub

    Public Sub StopRecordingAndSave()
        If Not InputRecorder.IsRecording Then Return
        Dim result As KbmrRecording = InputRecorder.StopRecording()
        If result Is Nothing Then Return
        result.Name = "录制 " & DateTime.Now.ToString("MM-dd HH:mm:ss")
        '自动保存到历史目录，使录制在重启后仍然保留
        If Not RecordingStore.Save(result) Then
            ShowMyMessage("录制已完成，但自动保存历史失败，请使用""另存为""导出到文件。")
        End If
        recordings.Add(result)
        currentRecordingIndex = recordings.Count - 1
        RefreshRecordList()
        RecordStatusLabel.Text = "录制完成：" & result.Events.Count & " 个事件，" & (result.DurationMs / 1000.0).ToString("0.0") & " 秒。"
        UpdateRecordButtons()
        Show()
        floatingWindow.FloatingWindow_Reset()
    End Sub

    Public Sub ToggleRecording()
        If InputRecorder.IsRecording Then
            StopRecordingAndSave()
        Else
            StartRecording()
        End If
    End Sub

    '删除录制
    Private Sub Button_Click_9(sender As Object, e As RoutedEventArgs)
        If currentRecordingIndex < 0 OrElse currentRecordingIndex >= recordings.Count Then Return
        '同时删除自动保存的历史文件
        RecordingStore.Delete(recordings(currentRecordingIndex))
        recordings.RemoveAt(currentRecordingIndex)
        If recordings.Count = 0 Then
            currentRecordingIndex = -1
        ElseIf currentRecordingIndex >= recordings.Count Then
            currentRecordingIndex = recordings.Count - 1
        End If
        RefreshRecordList()
        UpdateRecordButtons()
    End Sub

    '重命名录制
    'todo：写一个通用的重命名对话框（fluent风格），支持输入验证和非法字符过滤
    Private Sub BtnRecordRename_Click(sender As Object, e As RoutedEventArgs)
        If currentRecordingIndex < 0 OrElse currentRecordingIndex >= recordings.Count Then Return
        Dim item As KbmrRecording = recordings(currentRecordingIndex)
        Dim newName As String = InputBox("请输入新的录制名称：", "重命名录制", item.Name)
        If String.IsNullOrWhiteSpace(newName) Then Return
        If Not RecordingStore.Rename(item, newName) Then
            ShowMyMessage("重命名失败，请检查名称是否为空或包含非法字符。")
            Return
        End If
        RefreshRecordList()
        RecordStatusLabel.Text = "已重命名为：" & item.Name
    End Sub

    Private Sub RefreshRecordList()
        RecordList.Items.Clear()
        For Each item As KbmrRecording In recordings
            RecordList.Items.Add(item.Name)
        Next
        If currentRecordingIndex >= 0 AndAlso currentRecordingIndex < RecordList.Items.Count Then
            RecordList.SelectedIndex = currentRecordingIndex
        End If
        UpdateRecordContent()
    End Sub

    Private Sub UpdateRecordContent()
        If currentRecordingIndex < 0 OrElse currentRecordingIndex >= recordings.Count Then
            RecordContent.Text = ""
            Return
        End If
        Dim item As KbmrRecording = recordings(currentRecordingIndex)
        Dim builder As New StringBuilder()
        builder.AppendLine("名称：" & item.Name)
        builder.AppendLine("事件数：" & item.Events.Count)
        builder.AppendLine("时长：" & (item.DurationMs / 1000.0).ToString("0.000") & " 秒")
        builder.AppendLine("鼠标采样频率：" & item.SampleIntervalMs & " ms")
        builder.AppendLine("录制分辨率：" & item.ScreenWidth & " x " & item.ScreenHeight)
        builder.AppendLine("录制时间：" & item.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"))
        builder.AppendLine()
        builder.AppendLine("事件预览：")
        Dim previewCount As Integer = Math.Min(item.Events.Count, 300)
        For index As Integer = 0 To previewCount - 1
            builder.AppendLine(FormatKbmrEvent(item.Events(index)))
        Next
        If item.Events.Count > previewCount Then builder.AppendLine("……（仅显示前 " & previewCount & " 个事件）")
        RecordContent.Text = builder.ToString()
    End Sub

    Private Function FormatKbmrEvent(item As KbmrEvent) As String
        Dim time As String = (item.OffsetMs / 1000.0).ToString("0.000")
        Select Case item.Kind
            Case KbmrEventKind.KeyDown
                Return $"[{time}] 按下 {KeyNameMapper.FormatKeyName(item.Vk)}"
            Case KbmrEventKind.KeyUp
                Return $"[{time}] 抬起 {KeyNameMapper.FormatKeyName(item.Vk)}"
            Case KbmrEventKind.MouseDown
                Return $"[{time}] 鼠标按下 ({item.X},{item.Y}) {MouseButtonName(item.Button)}"
            Case KbmrEventKind.MouseUp
                Return $"[{time}] 鼠标抬起 ({item.X},{item.Y}) {MouseButtonName(item.Button)}"
            Case KbmrEventKind.MouseMove
                Return $"[{time}] 移动 ({item.X},{item.Y})"
            Case KbmrEventKind.Wheel
                Return $"[{time}] 滚轮 {item.Delta}"
            Case Else
                Return $"[{time}] 未知事件"
        End Select
    End Function

    Private Function MouseButtonName(button As Byte) As String
        Select Case button
            Case 2
                Return "右键"
            Case 3
                Return "中键"
            Case 4
                Return "侧键1"
            Case 5
                Return "侧键2"
            Case Else
                Return "左键"
        End Select
    End Function

    Private Sub RecordList_SelectionChanged(sender As Object, e As SelectionChangedEventArgs)
        If RecordList.SelectedIndex < 0 Then Return
        currentRecordingIndex = RecordList.SelectedIndex
        UpdateRecordContent()
        UpdateRecordButtons()
    End Sub

    Private Sub RecordList_MouseDoubleClick(sender As Object, e As MouseButtonEventArgs)
        If RecordList.SelectedIndex >= 0 Then BtnRecordPlay_Click(Nothing, Nothing)
    End Sub

    Private Sub BtnRecordPlay_Click(sender As Object, e As RoutedEventArgs)
        If currentRecordingIndex < 0 OrElse currentRecordingIndex >= recordings.Count Then
            ShowMyMessage("请先选择一条录制。")
            Return
        End If
        If InputRecorder.IsRecording Then StopRecordingAndSave()
        If ScriptRunner.IsRunning Then
            ShowMyMessage("脚本正在运行，请先停止脚本后再回放录制。")
            Return
        End If
        If isClicking Then StopClick()
        If isSending Then StopSend()
        Dim item As KbmrRecording = recordings(currentRecordingIndex)
        If item.Events.Count = 0 Then
            ShowMyMessage("该录制没有内容。")
            Return
        End If
        Dim speed As Double = 1.0
        Select Case Combobox6.SelectedIndex
            Case 0
                speed = 0.5
            Case 2
                speed = 2.0
            Case 3
                speed = 4.0
        End Select
        MacroPlayer.StartPlayback(item, speed, ChkPlaybackLoop.IsChecked.GetValueOrDefault(False),
                                  ChkPlaybackSmooth.IsChecked.GetValueOrDefault(True),
                                  ChkPlaybackBlockInput.IsChecked.GetValueOrDefault(False))
        RecordStatusLabel.Text = "正在回放……按""停止播放""或终止任务快捷键可结束。"
        UpdateRecordButtons()
        Hide（）
        floatingWindow.FloatingWindowEvent_Playback()
    End Sub

    Private Sub BtnRecordStopPlay_Click(sender As Object, e As RoutedEventArgs)
        MacroPlayer.StopPlayback()
    End Sub

    Private Sub MacroPlayer_PlaybackStopped()
        Dispatcher.BeginInvoke(New Action(Sub()
                                              RecordStatusLabel.Text = "回放已停止或结束。"
                                              UpdateRecordButtons()
                                              Show()
                                              floatingWindow.FloatingWindow_Reset()
                                          End Sub))
    End Sub

    Private Sub BtnRecordSave_Click(sender As Object, e As RoutedEventArgs)
        If currentRecordingIndex < 0 OrElse currentRecordingIndex >= recordings.Count Then Return
        Dim dialog As New SaveFileDialog With {
            .Title = "保存录制",
            .Filter = "键鼠管家录制文件 (*.kbmr)|*.kbmr|所有文件 (*.*)|*.*",
            .DefaultExt = ".kbmr",
            .AddExtension = True,
            .OverwritePrompt = True,
            .FileName = "录制"
        }
        If dialog.ShowDialog(Me) <> True Then Return
        Try
            File.WriteAllBytes(dialog.FileName, KbmrCodec.Serialize(recordings(currentRecordingIndex)))
            ShowMyMessage("录制已保存到文件。")
        Catch ex As Exception
            ShowMyMessage("无法保存录制：" & ex.Message)
        End Try
    End Sub

    Private Sub BtnRecordOpen_Click(sender As Object, e As RoutedEventArgs)
        Dim dialog As New OpenFileDialog With {
            .Title = "打开录制",
            .Filter = "键鼠管家录制文件 (*.kbmr)|*.kbmr|所有文件 (*.*)|*.*",
            .CheckFileExists = True,
            .CheckPathExists = True,
            .Multiselect = False
        }
        If dialog.ShowDialog(Me) <> True Then Return
        Try
            Dim result As KbmrRecording = KbmrCodec.Deserialize(File.ReadAllBytes(dialog.FileName))
            result.Name = Path.GetFileNameWithoutExtension(dialog.FileName)
            '将打开的录制纳入历史目录，方便下次直接使用
            RecordingStore.Save(result)
            recordings.Add(result)
            currentRecordingIndex = recordings.Count - 1
            RefreshRecordList()
            UpdateRecordButtons()
            ShowMyMessage("录制已打开并加入历史录制。")
        Catch ex As Exception
            ShowMyMessage("无法打开录制：" & ex.Message)
        End Try
    End Sub

    Private Sub BtnRecordExport_Click(sender As Object, e As RoutedEventArgs) '待实现
        If currentRecordingIndex < 0 OrElse currentRecordingIndex >= recordings.Count Then Return
        currentScriptPath = ""
        currentScriptHash = ""
        currentScriptTrusted = False
        TxtScriptTrustStatus.Text = "由录制生成（未信任）"
        TabControl1.SelectedIndex = 5
        ShowMyMessage("已生成脚本，可点击""运行""直接执行。")
    End Sub

    Private Sub UpdateRecordButtons()
        Dim isRecording As Boolean = InputRecorder.IsRecording
        Dim isPlaying As Boolean = MacroPlayer.IsPlaying
        BtnRecordStart.IsEnabled = Not isRecording AndAlso Not isPlaying
        BtnRecordStop.IsEnabled = isRecording
        BtnRecordPlay.IsEnabled = currentRecordingIndex >= 0 AndAlso Not isRecording AndAlso Not isPlaying
        BtnRecordStopPlay.IsEnabled = isPlaying
        BtnRecordSave.IsEnabled = currentRecordingIndex >= 0
        BtnRecordOpen.IsEnabled = Not isRecording AndAlso Not isPlaying
        BtnRecordDelete.IsEnabled = currentRecordingIndex >= 0 AndAlso Not isRecording
        BtnRecordRename.IsEnabled = currentRecordingIndex >= 0 AndAlso Not isRecording
        BtnRecordExport.IsEnabled = currentRecordingIndex >= 0
    End Sub


#End Region

#Region "Script"
    '脚本（实验性功能）
    Private currentScriptPath As String = ""
    Private currentScriptHash As String = ""
    Private currentScriptTrusted As Boolean = False
    Private scriptContent As String = ""

    Private Sub InitializeScriptSettings()
        TxtScriptOutput.Text = ""
        TxtScriptTrustStatus.Text = "未加载脚本"
        UpdateScriptButtons()
    End Sub

    Private Sub BtnScriptRun_Click(sender As Object, e As RoutedEventArgs) '脚本改成加载脚本，不要编辑器
        If ScriptRunner.IsRunning Then
            ScriptRunner.StopScript()
            Return
        End If
        Dim code As String = scriptContent
        If String.IsNullOrWhiteSpace(code) Then
            ShowMyMessage("请先加载脚本。")
        Return
        End If
        Dim analysis As ScriptAnalysisResult = ScriptSecurity.Analyze(code)
        If analysis.SyntaxErrors.Count > 0 Then
            ShowMyMessage("脚本存在语法错误，无法运行：" & vbCrLf & String.Join(vbCrLf, analysis.SyntaxErrors.Take(5)))
            Return
        End If
        Dim hash As String = ScriptSecurity.ComputeTextHash(code)
        If Not ScriptSecurity.IsTrusted(hash) Then
            If Not PromptScriptTrust(analysis, If(currentScriptPath, "(编辑器中的脚本)"), hash) Then Return
        End If
        currentScriptHash = hash
        currentScriptTrusted = ScriptSecurity.IsTrusted(hash)
        AppendScriptOutput("=== 开始运行脚本 " & DateTime.Now.ToString("HH:mm:ss") & " ===")
        Hide()
        ScriptRunner.RunScript(code)
    End Sub

    Private Function PromptScriptTrust(analysis As ScriptAnalysisResult, sourcePath As String, hash As String) As Boolean
        Dim dialog As New ScriptSecurityWindow(analysis, sourcePath, hash)
        dialog.Owner = Me
        dialog.ShowDialog()
        If dialog.Allowed AndAlso dialog.TrustRequested Then
            ScriptSecurity.TrustScript(hash, sourcePath)
        End If
        Return dialog.Allowed
    End Function

    Private Sub BtnScriptOpen_Click(sender As Object, e As RoutedEventArgs)
        Dim dialog As New OpenFileDialog With {
            .Title = "加载脚本",
            .Filter = "PowerShell 脚本 (*.ps1)|*.ps1|文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*",
            .CheckFileExists = True,
            .CheckPathExists = True,
            .Multiselect = False
        }
        If dialog.ShowDialog(Me) <> True Then Return
        Try
            Dim info As New FileInfo(dialog.FileName)
            If info.Length > 4 * 1024 * 1024 Then Throw New InvalidDataException("脚本文件不能超过 4 MiB。")
            Dim code As String = File.ReadAllText(dialog.FileName)
            Dim analysis As ScriptAnalysisResult = ScriptSecurity.Analyze(code)
            Dim hash As String = ScriptSecurity.ComputeFileHash(dialog.FileName)
            If Not ScriptSecurity.IsTrusted(hash) Then
                If Not PromptScriptTrust(analysis, dialog.FileName, hash) Then Return
            End If
            If analysis.SyntaxErrors.Count > 0 Then
                ShowMyMessage("警告：脚本存在语法错误，已加载但无法运行：" & vbCrLf & String.Join(vbCrLf, analysis.SyntaxErrors.Take(5)))
            End If
            scriptContent = code
            currentScriptPath = dialog.FileName
            currentScriptHash = hash
            currentScriptTrusted = ScriptSecurity.IsTrusted(hash)
            UpdateScriptTrustStatus()
            AppendScriptOutput("已加载脚本：" & dialog.FileName)
        Catch ex As Exception
            ShowMyMessage("无法加载脚本：" & ex.Message)
        End Try
    End Sub

    Private Sub BtnScriptClear_Click(sender As Object, e As RoutedEventArgs)
        currentScriptPath = ""
        currentScriptHash = ""
        currentScriptTrusted = False
        scriptContent = ""
        TxtScriptOutput.Clear()
        UpdateScriptTrustStatus()
    End Sub

    Private Sub BtnScriptHelp_Click(sender As Object, e As RoutedEventArgs)
        Dim helps As New List(Of String) From {
            """脚本""页可以直接运行 PowerShell 5.1 脚本，并使用键鼠管家提供的自动化命令。",
            "",
            "常用命令：",
            "  Delay 500                     等待 500 毫秒",
            "  Move 100 200                  移动鼠标到 (100,200)",
            "  Click 100 200 [Left]          点击，按钮可省略（Left/Right/Middle/X1/X2）",
            "  DoubleClick 100 200           双击",
            "  MouseDown / MouseUp Right     按住 / 松开鼠标",
            "  Wheel 120                     滚轮（正数向上）",
            "  Drag 100 100 300 300          从 (100,100) 拖到 (300,300)",
            "  KeyDown CTRL / KeyUp CTRL     按下 / 抬起按键",
            "  KeyTap ENTER                  单击按键",
            "  Combo 'Ctrl+Shift+A'          发送组合键",
            "  TypeText '你好'               输入文本（支持中文）",
            "  BlockInput $true/$false       阻止 / 恢复用户输入（Ctrl+Alt+Del 可强制解除）",
            "  Get-CursorPos                 获取鼠标位置（.X / .Y）",
            "  Get-PixelColor 100 200        读取屏幕像素颜色（返回 #RRGGBB）",
            "  Get-CursorColor               读取鼠标指针所在像素颜色",
            "  Save-ScreenRegion 0 0 800 600 'C:\\shot.png'   保存屏幕区域截图",
            "  Get-Windows [-Title '*记事本*'] [-Class '*']    枚举可见窗口",
            "  Set-ForegroundWindow $hwnd    激活窗口；Close-Window / Move-Window 同理",
            "  Get-ClipboardText / Set-ClipboardText '文本'    读写剪贴板",
            "  Start-App 'notepad.exe'       启动程序",
            "",
            "安全机制：",
            "  导入或首次运行脚本时会显示安全提醒，列出脚本将执行的操作。",
            "  勾选""信任此脚本""后按文件哈希记住；文件被修改后会再次提醒。",
            "  脚本以当前用户权限运行，请只运行来源可信的脚本。",
            "  快捷键：默认 Ctrl+F4 开始/停止录制；Ctrl+G 终止全部任务。"
        }
        ShowHelp(helps, "脚本帮助")
    End Sub

    Private Sub BtnScriptClearTrust_Click(sender As Object, e As RoutedEventArgs)
        Dim answer As MsgBoxResult = MsgBox("确定要清空所有已信任的脚本记录吗？清空后所有脚本都会重新弹出安全提醒。",
                                            MsgBoxStyle.YesNo Or MsgBoxStyle.Question, "清空信任列表")
        If answer <> MsgBoxResult.Yes Then Return
        ScriptSecurity.ClearTrustedScripts()
        currentScriptTrusted = False
        UpdateScriptTrustStatus()
        ShowMyMessage("信任列表已清空。")
    End Sub

    Private Sub ScriptRunner_OutputReceived(text As String)
        Dispatcher.BeginInvoke(New Action(Sub() AppendScriptOutput(text)))
    End Sub

    Private Sub ScriptRunner_StateChanged(running As Boolean)
        Dispatcher.BeginInvoke(New Action(Sub()
                                              UpdateScriptButtons()
                                              UpdateScriptTrustStatus()
                                          End Sub))
    End Sub

    Private Sub AppendScriptOutput(text As String)
        TxtScriptOutput.AppendText(text & vbCrLf)
        TxtScriptOutput.ScrollToEnd()
    End Sub

    Private Sub UpdateScriptButtons()
        If ScriptRunner.IsRunning Then
            BtnScriptRun.Content = ChrW(&HE71A)
            BtnScriptRun.ToolTip = "停止运行"
            TxtScriptTrustStatus.Text = "运行中……"
        Else
            BtnScriptRun.Content = ChrW(&HE768)
            BtnScriptRun.ToolTip = "运行"
            UpdateScriptTrustStatus()
        End If
        BtnScriptOpen.IsEnabled = Not ScriptRunner.IsRunning
        BtnScriptClear.IsEnabled = Not ScriptRunner.IsRunning
        BtnScriptClearTrust.IsEnabled = Not ScriptRunner.IsRunning
    End Sub

    Private Sub UpdateScriptTrustStatus()
        If ScriptRunner.IsRunning Then
            TxtScriptTrustStatus.Text = "运行中……"
            Return
        End If
        If String.IsNullOrEmpty(currentScriptHash) Then
            TxtScriptTrustStatus.Text = "未加载脚本"
        ElseIf currentScriptTrusted OrElse ScriptSecurity.IsTrusted(currentScriptHash) Then
            TxtScriptTrustStatus.Text = "已信任（哈希匹配）"
        Else
            TxtScriptTrustStatus.Text = "未信任"
        End If
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
                Dim updateRoot As String = UpdateModule.GetUpdateRoot()
                Directory.CreateDirectory(updateRoot)
                zipPath = Path.Combine(updateRoot, "update-" & Guid.NewGuid().ToString("N") & ".zip")
                extractPath = Path.Combine(updateRoot, latestVersion.ToString() & "-" & Guid.NewGuid().ToString("N"))
                Await UpdateModule.DownloadUpdateAsync(httpClient, New Uri(asset.browser_download_url, UriKind.Absolute), zipPath)
                UpdateModule.VerifyFileSha256(zipPath, asset.digest)
                UpdateModule.ExtractUpdateSafely(zipPath, extractPath)
                Dim executablePath As String = UpdateModule.FindUpdateExecutable(extractPath)

                Dim currentProcess As Diagnostics.Process = Diagnostics.Process.GetCurrentProcess()
                Diagnostics.Process.Start(New Diagnostics.ProcessStartInfo With {
                    .FileName = executablePath,
                    .Arguments = UpdateModule.BuildApplyUpdateArguments(currentProcess.MainModule.FileName, extractPath, currentProcess.Id),
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
