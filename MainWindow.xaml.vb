'主窗体代码
Imports System.Runtime.InteropServices
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.Timers
Imports System.Windows.Interop
Imports Microsoft.Win32
Imports WindowSelector
Imports Timer = System.Timers.Timer



Public Class MainWindow1

    'git id
    Public Const id As String = "2b246a3c"

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

    Dim savedkeys As New List(Of Key)

    Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs)
        '判断系统版本是否低于win10 1809以防bug
        Dim osVersion As Version = Environment.OSVersion.Version
        If osVersion.Major < 10 OrElse (osVersion.Major = 10 AndAlso osVersion.Build < 17763) Then
            Hide()
            ShowExpdlg("错误：本程序不支持当前操作系统。要使用本程序，请使用Windows 10 1809或更高版本。", "")
        End If
        '判断是否为测试版
        If My.Application.Info.Version.Revision <> 0 Then
            ProductName.Content = "键鼠管家测试版"
            Title = "键鼠管家测试版"
            WelcomeText.Content = "欢迎参与键鼠管家测试版的测试！"
            TestTip.Visibility = Visibility.Visible
        End If
        Try
            '检测设置并初始化控件状态
            If ReadSetting("DoAutoSwitchTheme", 1) = 1 Then '默认是跟随系统
                Dim isDarkMode As Boolean = IsDarkModeEnabled()
                SwitchTheme(isDarkMode)
                Combobox1.SelectedIndex = 0
            ElseIf ReadSetting("IsDarkMode", 0) = 0 Then
                SwitchTheme(False)
                Combobox1.SelectedIndex = 1
            Else
                SwitchTheme(True)
                Combobox1.SelectedIndex = 2
            End If
            'todo:去悬浮窗实现自动收缩等功能
            '加载悬浮窗状态
            Select Case ReadSetting("FloatingWinShowState", 1)
                Case 0 '始终隐藏
                    FloatingWindowState = 0
                    Combobox2.SelectedIndex = 1
                    FloatingWindow.Instance.Hide()
                Case 1 '始终显示
                    FloatingWindowState = 1
                    Combobox2.SelectedIndex = 0
                    FloatingWindow.Instance.Show()
                Case 2 '自动收缩
                    FloatingWindowState = 2
                    Combobox2.SelectedIndex = 2
                    FloatingWindow.Instance.Show()
                Case Else
                    ShowMyMessage("加载悬浮窗状态时失败！")
            End Select
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
            '加载连点设置
            Textbox2.Visibility = Visibility.Hidden
            If ReadSetting("DoClickSettingSaved", 0) = 1 Then
                If ReadSetting("ClickInterval", "") = "" Then
                    Return
                End If
                Textbox1.Text = ReadSetting("ClickInterval", "")
                If ReadSetting("DoRandomOffsetOfClickSpeed", 0) = 1 Then
                    CheckBox1.IsChecked = True
                End If
                If ReadSetting("DoRandomOffsetOfClickPosition", 0) = 1 Then
                    CheckBox2.IsChecked = True
                End If
                If ReadSetting("DoCustomizeCursorPos", 0) = 1 Then
                    CheckBox3.IsChecked = True
                    Textbox2.Visibility = Visibility.Visible
                    Textbox2.Text = ReadSetting("CursorPosition", "")
                Else
                    Textbox2.Visibility = Visibility.Hidden
                End If
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
                If LoadKeyData(ClickKeys_str) IsNot New List(Of Key) From {Key.None} Then
                    savedkeys = LoadKeyData(ClickKeys_str)
                Else
                    ShowMyMessage("无法加载连点设置。")
                End If
            End If
            '加载快捷键设置
            If ReadSetting("StopActHotkeys", "") <> "" Then
                KeyTextbox3.Text = ReadSetting("StopActHotkeys", "")
                Dim StopActHotkeys_str = ReadSetting("StopActHotkeys", "")
                If LoadKeyData(StopActHotkeys_str) IsNot New List(Of Key) From {Key.None} Then
                    stopActHotkeys = ConvertKeyLogToVirtualKeyCodes(LoadKeyData(StopActHotkeys_str))
                Else
                    ShowMyMessage("无法加载快捷键设置。")
                End If
            End If
        Catch ex As Exception
            Hide()
            ShowExpdlg("错误6：程序在初始化时读取设置出现问题，请尝试删除所有位于HKEY_CURRENT_USER\SOFTWARE\LCS\keyboardmgr的设置，如仍不能解决问题，请联系LCS。", ex.Message)
        End Try
        VerLabel.Content = "版本号：" & My.Application.Info.Version.Major & "." & My.Application.Info.Version.Minor & "." & My.Application.Info.Version.Build & "." & My.Application.Info.Version.Revision
        GitID.Content = "Git ID：" & id
        floatingWindow.Show() '弹出悬浮窗
        Pinicon_Set()
        '移除最大化按钮
        Dim hwnd As IntPtr = New WindowInteropHelper(Me).Handle
        Dim style As Integer = GetWindowLong(hwnd, GWL_STYLE)
        SetWindowLong(hwnd, GWL_STYLE, style And Not WS_MAXIMIZEBOX)
    End Sub


    Public Sub Pinicon_Set()
        '在代码里设置pinButton图标，防止图标不显示
        Dim resourceDictionary As New ResourceDictionary With {
           .Source = New Uri("pack://application:,,,/keyboardmgr2;Component/resource/" & If(isDarkTheme, "DarkTheme.xaml", "LightTheme.xaml"), UriKind.Absolute)
       }
        Dim pinIcon As Canvas = resourceDictionary("Icon.Pin")
        pinButton.Content = pinIcon
    End Sub

    Public Sub New()
        InitializeComponent()
        AddHandler SystemEvents.UserPreferenceChanged, AddressOf OnUserPreferenceChanged
        InitializeTextBoxKeyHandler(KeyTextbox1)
        InitializeTextBoxKeyHandler(KeyTextbox2)
        InitializeTextBoxKeyHandler(KeyTextbox3)
        _instance = Me
    End Sub

    '增加属性，方便访问
    Public Shared ReadOnly Property Instance() As MainWindow1
        Get
            Return _instance
        End Get
    End Property

    Private Sub OnUserPreferenceChanged(sender As Object, e As UserPreferenceChangedEventArgs)
        '检查用户是否在程序打开时切换了系统深浅色模式
        If e.Category = UserPreferenceCategory.General Then
            '检测是否设置为自动跟随系统主题
            If ReadSetting("DoAutoSwitchTheme", 0) = 1 Then
                '判断当前是否启用深色模式
                Dim isDarkMode As Boolean = IsDarkModeEnabled()
                '切换主题到深色或浅色模式
                SwitchTheme(isDarkMode)
                WriteSetting("IsDarkMode", isDarkMode)
            End If
        End If
    End Sub
    '欢迎界面的底部链接
    Private Sub Button_Click_1(sender As Object, e As RoutedEventArgs)
        Process.Start("http://lcs.info.gf/")
    End Sub

    Private Sub Button_Click_2(sender As Object, e As RoutedEventArgs)
        Process.Start("https://qm.qq.com/q/SIZ1MaTKoe")
    End Sub

    Private Sub MainWindow1_Closing(sender As Object, e As ComponentModel.CancelEventArgs) Handles MyBase.Closing
        Visibility = Visibility.Hidden
        e.Cancel = True
    End Sub

    Private Sub ToggleButton_Click(sender As Object, e As RoutedEventArgs)
        Dim resourceDictionary As New ResourceDictionary With {
            .Source = New Uri("pack://application:,,,/keyboardmgr2;Component/resource/" & If(isDarkTheme, "DarkTheme.xaml", "LightTheme.xaml"), UriKind.Absolute)
        }
        If pinButton.IsChecked = True Then
            Topmost = True
            Dim unpinIcon As Canvas = resourceDictionary("Icon.Unpin")
            pinButton.Content = unpinIcon
            pinButton.Foreground = New SolidColorBrush(Colors.White)
        Else
            Topmost = False
            Dim pinIcon As Canvas = resourceDictionary("Icon.Pin")
            pinButton.Content = pinIcon
            pinButton.Foreground = New SolidColorBrush(Colors.Black)
        End If
    End Sub



#Region "SaveSettings"

    Private Sub Button_Click(sender As Object, e As RoutedEventArgs) '保存设置
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
                FloatingWindowState = 1
                If isFloatingWindowFolded = True Then
                    floatingWindow.Unfold()
                End If
            Case 1
                WriteSetting("FloatingWinShowState", 0) '始终隐藏
                ShowMyMessage("注意：悬浮窗隐藏后您只能使用快捷键来控制部分程序功能！")
                FloatingWindowState = 0
            Case 2
                WriteSetting("FloatingWinShowState", 2) '自动收缩
                ShowMyMessage("注意：把鼠标移到悬浮窗上就可以展开它")
                FloatingWindowState = 2
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
        'todo:实现自定义快捷键
        If KeyTextbox3.Text <> "" Then
            WriteSetting("StopActHotkeys", KeyTextbox3.Text)
        Else
            ShowMyMessage("无法保存设置：没有指定一个或多个快捷键")
            Return
        End If
    End Sub
    'todo:检测热键冲突和在注册表编辑器中打开设置

#End Region



    Public Sub ShowWindow()
        Show()
    End Sub



#Region "ClickAndSend"
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
    Public timer1 As New Timer
    Public timer2 As New Timer
    Public timer3 As New Timer
    Private Sub Button_Click_3(sender As Object, e As RoutedEventArgs) '开始连点
        If Textbox1.Text = "" Then
            ShowMyMessage("没有指定发送间隔")
            Return
        ElseIf Textbox1.Text > 0 Then
            clickTime = Textbox1.Text '此处隐式转换
            If CheckBox1.IsChecked = True Then '速度偏移
                isSpeedRandomOffset = True
            End If
            If CheckBox2.IsChecked = True Then '位置偏移
                isPosRandomOffset = True
            End If
            If CheckBox3.IsChecked = True Then '自定义鼠标位置
                If Textbox2.Text = "" Or Textbox2.Text = "," Then
                    ShowMyMessage("没有指定自定义鼠标位置")
                    Return
                Else
                    Dim cursorPos As String() = Textbox2.Text.Split(",") '坐标形式：（横坐标,纵坐标）
                    Dim x As Integer = cursorPos(0)
                    Dim y As Integer = cursorPos(1)
                    SetCursorPosition(x, y)
                    ShowMyMessage("已确定鼠标位置：" & x & "," & y & "请勿移动鼠标！")
                End If
            End If
            If RadioButton1.IsChecked = True Then '左键连点
                AddHandler timer1.Elapsed, AddressOf Timer1_Elapsed
                timer1.Interval = clickTime
                timer1.AutoReset = True
                timer1.Enabled = True
                timer1.Start()
                Hide()
                floatingWindow.FloatingWindowEvent_Click() '悬浮窗状态更新
            ElseIf RadioButton2.IsChecked = True Then '右键连点 
                AddHandler timer2.Elapsed, AddressOf Timer2_Elapsed
                timer2.Interval = clickTime
                timer2.AutoReset = True
                timer2.Enabled = True
                timer2.Start()
                Hide()
                floatingWindow.FloatingWindowEvent_Click() '悬浮窗状态更新
            ElseIf GetKeyLog() IsNot Nothing Or savedkeys.Count > 0 Then '自定义键连点
                If clickTime > 50 Then
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
            timer1.Interval = clickTime + randomSpeed
        End If
        Dim P As POINTAPI
        GetCursorPos(P)
        If isPosRandomOffset = True Then
            P.x += random.Next(-15, 16)
            P.y += random.Next(-15, 16)
            SetCursorPosition(P.x, P.y)
        End If
        mouse_event(MOUSEEVENTF_LEFTDOWN, P.x.ToString, P.y.ToString, 0, 0)
        mouse_event(MOUSEEVENTF_LEFTUP, P.x.ToString, P.y.ToString, 0, 0)
    End Sub
    Private Sub Timer2_Elapsed(sender As Object, e As ElapsedEventArgs)
        '右键连点
        If isSpeedRandomOffset = True Then
            Dim randomSpeed As Integer = random.Next(-10, 11)
            timer2.Interval = clickTime + randomSpeed
        End If
        Dim P As POINTAPI
        GetCursorPos(P)
        If isPosRandomOffset = True Then
            P.x += random.Next(-15, 16)
            P.y += random.Next(-15, 16)
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

        GC.Collect()
    End Sub

#End Region


#Region "Loaf"
    '摸鱼
    Dim isLoafEnabled As Boolean = False
    Private Sub Button_Click_4(sender As Object, e As RoutedEventArgs) '保存摸鱼设置
        WriteSetting("IsLoafEnabled", If(LoafToggle.IsChecked, 1, 0))
    End Sub

    Private Sub CheckBox_Click(sender As Object, e As RoutedEventArgs)
        If LoafToggle.IsChecked = True Then
            isLoafEnabled = True
            LoafGrid.Visibility = Visibility.Visible
        Else
            isLoafEnabled = False
            LoafGrid.Visibility = Visibility.Hidden
        End If
    End Sub

    Private Sub LoafHelpButton_Click(sender As Object, e As RoutedEventArgs) '显示帮助
        Dim helps As New List(Of String) From {
            "摸鱼工具箱可以让您使用一组快捷键即可快速调整窗口，使用方法如下：",
            "当摸鱼功能开启时，您可以按下一组您指定的快捷键，即可前台显示您所指定的窗口（比如您工作用的窗口），并隐藏您的其他窗口（如您用来摸鱼的窗口），从而方便您摸鱼不被老板发现（doge)",
            "这个功能类似于某些软件的老板键功能，但摸鱼工具箱所提供的快捷键是全局的。",
            "注意：此功能仅在本程序运行时有效。"
        }
        ShowHelp(helps, "摸鱼工具箱帮助")
    End Sub

    Private Sub Button_Click_5(sender As Object, e As RoutedEventArgs) '选取窗体
        Dim selectorForm As New WindowSelectorDlg()
        If selectorForm.ShowDialog() = System.Windows.Forms.DialogResult.OK Then
            Dim selectedHwnd As IntPtr = selectorForm.SelectedWindowHwnd
            SelectedWindowHwnd.Content = "选取的窗体句柄：" & selectedHwnd.ToString
        End If

    End Sub





#End Region

End Class
