
'键鼠操作相关功能。
Imports System.Runtime.InteropServices
Imports System.Windows.Automation



Module UserInputHandler
    '实现KeyTextBox的功能
#Region "KeyTextbox"

    '初始化TextBox的键盘事件处理
    Public Sub InitializeTextBoxKeyHandler(textBox As TextBox)
        AddHandler textBox.PreviewKeyDown, AddressOf TextBox_PreviewKeyDown
    End Sub

    Private shared_keylog As List(Of Key)

    Private Sub TextBox_PreviewKeyDown(sender As Object, e As KeyEventArgs)
        Dim textBox As TextBox = CType(sender, TextBox)
        '记录按键的列表
        Dim keylog As New List(Of Key)
        '记录按键
        Dim keyPressed_Str As String = e.Key.ToString()
        Dim keyPressed As Key = e.Key
        keylog.Add(e.Key)
        If (Keyboard.Modifiers And ModifierKeys.Control) = ModifierKeys.Control Then
            keyPressed_Str = "Ctrl+" & keyPressed_Str
            keylog.Add(Key.LeftCtrl)
        End If
        If (Keyboard.Modifiers And ModifierKeys.Shift) = ModifierKeys.Shift Then
            keyPressed_Str = "Shift+" & keyPressed_Str
            keylog.Add(Key.LeftShift)
        End If
        If (Keyboard.Modifiers And ModifierKeys.Alt) = ModifierKeys.Alt Then
            keyPressed_Str = "Alt+" & keyPressed_Str
            keylog.Add(Key.LeftAlt)
        End If
        If (Keyboard.Modifiers And ModifierKeys.Windows) = ModifierKeys.Windows Then
            keyPressed_Str = "Win+" & keyPressed_Str
            keylog.Add(Key.LWin)
        End If
        '微调，去除重复的修饰键名
        keyPressed_Str = keyPressed_Str.Replace("+LeftCtrl", "").Replace("+RightCtrl", "").Replace("+LeftAlt", "").Replace("+RightAlt", "").Replace("+LeftShift", "").Replace("+RightShift", "").Replace("+System", "").Replace("LWin", "Win").Replace("RWin", "Win").Replace("Return", "Enter")
        textBox.Text = keyPressed_Str
        shared_keylog = keylog
        e.Handled = True
    End Sub

    '使用时只需调用这个函数即可
    Public Function GetKeyLog() As List(Of Key)
        Return shared_keylog
    End Function
#End Region



    '模拟winform里的sendkeys，统一改用 SendInput 注入键盘事件
#Region "Sendkeys"

    Public Sub SendKey(key As Byte, isPress As Boolean)
        SendVirtualKeyInput(CUShort(key), isPress)
    End Sub

    Public Sub SendKeyCombination(keys As List(Of UShort))
        SendKeyCombinationDown(keys)
        Threading.Thread.Sleep(50) ' 添加延时确保按键事件被识别
        SendKeyCombinationUp(keys)
    End Sub

    Public Sub SendKeyCombinationDown(keys As List(Of UShort))
        For Each key In keys
            If IsModifierKey(key) Then
                SendKey(key, True)
            End If
        Next
        ' 按下其余键
        For Each key In keys
            If Not IsModifierKey(key) Then
                SendKey(key, True)
            End If
        Next
    End Sub

    Public Sub SendKeyCombinationUp(keys As List(Of UShort))
        For index As Integer = keys.Count - 1 To 0 Step -1
            SendKey(keys(index), False)
        Next
    End Sub

    Private Function IsModifierKey(key As UShort) As Boolean
        Return key = &HA0 OrElse key = &HA1 OrElse key = &HA2 OrElse key = &HA3 OrElse
               key = &HA4 OrElse key = &HA5 OrElse key = &H10 OrElse key = &H11 OrElse
               key = &H12 OrElse key = &H5B OrElse key = &H5C
    End Function

    <StructLayout(LayoutKind.Sequential)>
    Private Structure KeyboardInput
        Public VirtualKey As UShort
        Public ScanCode As UShort
        Public Flags As UInteger
        Public Time As UInteger
        Public ExtraInfo As IntPtr
    End Structure

    <StructLayout(LayoutKind.Explicit)>
    Private Structure InputUnion
        <FieldOffset(0)>
        Public Keyboard As KeyboardInput
        <FieldOffset(0)>
        Public Mouse As MouseInput
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure MouseInput
        Public X As Integer
        Public Y As Integer
        Public MouseData As UInteger
        Public Flags As UInteger
        Public Time As UInteger
        Public ExtraInfo As IntPtr
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure Input
        Public Type As UInteger
        Public Data As InputUnion
    End Structure

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function SendInput(inputCount As UInteger, inputs() As Input, inputSize As Integer) As UInteger
    End Function

    Public Sub SendUnicodeText(text As String)
        Const INPUT_KEYBOARD As UInteger = 1
        Const KEYEVENTF_KEYUP As UInteger = &H2
        Const KEYEVENTF_UNICODE As UInteger = &H4

        Dim inputList As New List(Of Input)
        Dim previousWasCarriageReturn As Boolean = False
        For Each character As Char In text
            If character = ControlChars.Cr OrElse character = ControlChars.Lf Then
                If Not (character = ControlChars.Lf AndAlso previousWasCarriageReturn) Then
                    AddVirtualKeyInputs(inputList, 13)
                End If
                previousWasCarriageReturn = character = ControlChars.Cr
                Continue For
            End If
            previousWasCarriageReturn = False
            If character = ControlChars.Tab Then
                AddVirtualKeyInputs(inputList, 9)
                Continue For
            End If

            Dim keyDown As New Input With {.Type = INPUT_KEYBOARD}
            keyDown.Data.Keyboard.ScanCode = Convert.ToUInt16(character)
            keyDown.Data.Keyboard.Flags = KEYEVENTF_UNICODE
            inputList.Add(keyDown)
            Dim keyUp As Input = keyDown
            keyUp.Data.Keyboard.Flags = KEYEVENTF_UNICODE Or KEYEVENTF_KEYUP
            inputList.Add(keyUp)
        Next

        SendInputEvents(inputList)
    End Sub

    Public Sub SendEnterKey()
        Dim inputList As New List(Of Input)
        AddVirtualKeyInputs(inputList, 13)
        SendInputEvents(inputList)
    End Sub

    Public Sub SendPasteShortcut()
        Dim inputList As New List(Of Input)
        Const VK_CONTROL As UShort = &H11
        Const VK_V As UShort = &H56
        Const INPUT_KEYBOARD As UInteger = 1
        Const KEYEVENTF_KEYUP As UInteger = &H2

        inputList.Add(New Input With {.Type = INPUT_KEYBOARD, .Data = New InputUnion With {.Keyboard = New KeyboardInput With {.VirtualKey = VK_CONTROL}}})
        AddVirtualKeyInputs(inputList, VK_V)
        inputList.Add(New Input With {.Type = INPUT_KEYBOARD, .Data = New InputUnion With {.Keyboard = New KeyboardInput With {.VirtualKey = VK_CONTROL, .Flags = KEYEVENTF_KEYUP}}})
        SendInputEvents(inputList)
    End Sub

    Private Sub SendInputEvents(inputList As List(Of Input))
        If inputList.Count = 0 Then Return
        For Each inputEvent As Input In inputList
            Dim inputs() As Input = {inputEvent}
            If SendInput(1, inputs, Marshal.SizeOf(GetType(Input))) <> 1 Then
                Throw New ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "无法向目标窗口发送按键")
            End If
        Next
    End Sub

    Private Sub AddVirtualKeyInputs(inputList As List(Of Input), virtualKey As UShort)
        Const INPUT_KEYBOARD As UInteger = 1
        Const KEYEVENTF_KEYUP As UInteger = &H2
        Dim keyDown As New Input With {.Type = INPUT_KEYBOARD}
        keyDown.Data.Keyboard.VirtualKey = virtualKey
        inputList.Add(keyDown)
        Dim keyUp As Input = keyDown
        keyUp.Data.Keyboard.Flags = KEYEVENTF_KEYUP
        inputList.Add(keyUp)
    End Sub

    '统一使用 SendInput 注入键鼠事件（录制回放与脚本回放共用）
    Public Sub SendVirtualKeyInput(vk As UShort, down As Boolean)
        Const INPUT_KEYBOARD As UInteger = 1
        Const KEYEVENTF_KEYUP As UInteger = &H2
        Dim item As New Input With {.Type = INPUT_KEYBOARD}
        item.Data.Keyboard.VirtualKey = vk
        If Not down Then item.Data.Keyboard.Flags = KEYEVENTF_KEYUP
        SendInputRaw({item})
    End Sub

    '通过 SendInput 注入鼠标按键（1=左 2=右 3=中 4=侧键1 5=侧键2）
    Public Sub SendMouseButtonInput(button As Integer, down As Boolean)
        Const INPUT_MOUSE As UInteger = 0
        Dim flag As UInteger
        Dim data As UInteger = 0
        Select Case button
            Case 1
                flag = CUInt(If(down, MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP))
            Case 2
                flag = CUInt(If(down, MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP))
            Case 3
                flag = CUInt(If(down, MOUSEEVENTF_MIDDLEDOWN, MOUSEEVENTF_MIDDLEUP))
            Case 4
                flag = CUInt(If(down, MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP))
                data = 1UI
            Case 5
                flag = CUInt(If(down, MOUSEEVENTF_XDOWN, MOUSEEVENTF_XUP))
                data = 2UI
            Case Else
                Return
        End Select
        Dim item As New Input With {.Type = INPUT_MOUSE}
        item.Data.Mouse.Flags = flag
        item.Data.Mouse.MouseData = data
        SendInputRaw({item})
    End Sub

    '通过 SendInput 注入滚轮事件，delta 为滚轮格数 * 120（向前为正、向后为负）
    Public Sub SendMouseWheelInput(delta As Integer)
        Const INPUT_MOUSE As UInteger = 0
        Dim item As New Input With {.Type = INPUT_MOUSE}
        item.Data.Mouse.Flags = MOUSEEVENTF_WHEEL
        item.Data.Mouse.MouseData = CUInt(CLng(delta) And &HFFFFFFFFL)
        SendInputRaw({item})
    End Sub

    Private Sub SendInputRaw(items() As Input)
        If items Is Nothing OrElse items.Length = 0 Then Return
        SendInput(CUInt(items.Length), items, Marshal.SizeOf(GetType(Input)))
    End Sub

    '连发“发送键”：Enter 或 Shift+Enter（Shift 按住期间发送 Enter）
    Public Sub SendSendKey(useShiftEnter As Boolean)
        If useShiftEnter Then
            SendKey(VK_SHIFT, True)
            Try
                SendEnterKey()
            Finally
                SendKey(VK_SHIFT, False)
            End Try
        Else
            SendEnterKey()
        End If
    End Sub

    Private Const VK_SHIFT As Byte = &H10

#End Region



    '直接向目标文本框插入文本（连发文本条目用）：
    '优先 UI Automation 直接写入，其次 WM_SETTEXT，最后回退 SendInput 逐字符模拟。
    '指定 targetHwnd 时以该窗口内的控件为准（不依赖前台焦点），否则使用当前前台/焦点控件。
#Region "DirectTextInsert"

    Public Function InsertTextToTarget(text As String, Optional targetHwnd As IntPtr = Nothing, Optional inputPointX As Integer = Integer.MinValue, Optional inputPointY As Integer = Integer.MinValue) As Boolean
        If String.IsNullOrEmpty(text) Then Return True

        If targetHwnd <> IntPtr.Zero Then
            '0. 用户点选过输入框位置：直接点击该位置后模拟键盘输入（最可靠）
            If inputPointX <> Integer.MinValue AndAlso TryClickPointAndType(text, targetHwnd, inputPointX, inputPointY) Then Return True
            '1. UI Automation：目标窗口内定位编辑控件并直接写入
            If TrySetTextViaUia(text, targetHwnd) Then Return True
            '2. WM_SETTEXT：向目标窗口线程的焦点控件直接设置文本
            If TrySetTextViaWmSettext(text, targetHwnd) Then Return True
            '3. 点击定位：找到可能的输入框，点击获得焦点后模拟键盘输入（兼容不支持 UIA 写入的应用）
            If TryClickAndType(text, targetHwnd) Then Return True
            '4. 回退：操作内核模拟键盘输入（依赖目标已激活且已有焦点）
            Return OpEngine.TypeText(text)
        End If

        '未指定目标窗口：沿用全局焦点控件
        If TrySetTextViaUia(text, IntPtr.Zero) Then Return True
        If TrySetTextViaWmSettext(text, IntPtr.Zero) Then Return True
        Return OpEngine.TypeText(text)
    End Function

    '按用户点选的窗口内相对坐标点击并输入文本
    Private Function TryClickPointAndType(text As String, targetHwnd As IntPtr, relX As Integer, relY As Integer) As Boolean
        Try
            Dim rect As RECTAPI
            If Not GetWindowRectApi(targetHwnd, rect) Then Return False
            Dim ax As Integer = rect.Left + relX
            Dim ay As Integer = rect.Top + relY
            If Not OpEngine.MouseClick(1, ax, ay) Then Return False
            Threading.Thread.Sleep(80)
            Return OpEngine.TypeText(text)
        Catch
            Return False
        End Try
    End Function

    Private Function TrySetTextViaUia(text As String, targetHwnd As IntPtr) As Boolean
        Try
            If targetHwnd <> IntPtr.Zero Then
                Dim window As AutomationElement = AutomationElement.FromHandle(targetHwnd)
                If window Is Nothing Then Return False
                '优先目标窗口内的焦点控件，其次在窗口子树中查找最可能是输入框的控件
                Dim focusHwnd As IntPtr = GetFocusedControlHwnd(targetHwnd)
                If focusHwnd <> IntPtr.Zero Then
                    Dim focused As AutomationElement = AutomationElement.FromHandle(focusHwnd)
                    If focused IsNot Nothing AndAlso TrySetElementValue(focused, text) Then Return True
                End If
                Dim candidate As AutomationElement = FindInputElement(window)
                If candidate Is Nothing Then Return False
                Return TrySetElementValue(candidate, text)
            End If

            Dim focusedGlobal As AutomationElement = AutomationElement.FocusedElement
            If focusedGlobal Is Nothing Then Return False
            If TrySetElementValue(focusedGlobal, text) Then Return True
            Dim conditionGlobal As New PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, True)
            Dim candidateGlobal As AutomationElement = focusedGlobal.FindFirst(TreeScope.Element Or TreeScope.Subtree, conditionGlobal)
            If candidateGlobal Is Nothing Then Return False
            Return TrySetElementValue(candidateGlobal, text)
        Catch
            Return False
        End Try
    End Function

    '在窗口子树中找出最可能是“主输入框”的控件：
    '候选为 Edit/Document 控件或支持 ValuePattern 的控件，
    '按 可聚焦/可写值/控件类型/多行高度/靠窗口底部/面积 加权评分，避免选中顶部搜索框。
    Private Function FindInputElement(window As AutomationElement) As AutomationElement
        Try
            Dim orCondition As New OrCondition(
                New PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit),
                New PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Document),
                New PropertyCondition(AutomationElement.IsValuePatternAvailableProperty, True))
            Dim elements As AutomationElementCollection = window.FindAll(TreeScope.Subtree, orCondition)
            If elements Is Nothing OrElse elements.Count = 0 Then Return Nothing
            Dim winRect As System.Windows.Rect = Nothing
            Try
                winRect = window.Current.BoundingRectangle
            Catch
            End Try
            Dim best As AutomationElement = Nothing
            Dim bestScore As Double = -1
            For Each el As AutomationElement In elements
                Try
                    Dim cur As AutomationElement.AutomationElementInformation = el.Current
                    If Not cur.IsEnabled OrElse cur.IsOffscreen Then Continue For
                    Dim rect As System.Windows.Rect = cur.BoundingRectangle
                    If rect.IsEmpty OrElse rect.Width < 30 OrElse rect.Height < 15 Then Continue For
                    Dim hasWritableValue As Boolean = False
                    Dim vp As Object = Nothing
                    If el.TryGetCurrentPattern(ValuePattern.Pattern, vp) AndAlso vp IsNot Nothing Then
                        Dim valuePattern As ValuePattern = CType(vp, ValuePattern)
                        hasWritableValue = Not valuePattern.Current.IsReadOnly
                    End If
                    Dim score As Double = rect.Width * rect.Height
                    If cur.IsKeyboardFocusable Then score *= 10
                    If cur.ControlType Is ControlType.Edit Then score *= 30
                    If cur.ControlType Is ControlType.Document Then score *= 15
                    If hasWritableValue Then score *= 20
                    '多行输入框加权；窄而矮的控件大概率是搜索框，降权
                    If rect.Height >= 60 Then score *= 5
                    If rect.Height <= 45 AndAlso rect.Width < 400 Then score *= 0.15
                    '主输入框通常在窗口下部：越靠底部越优先，顶部降权
                    If Not winRect.IsEmpty AndAlso winRect.Height > 0 Then
                        Dim bottomRatio As Double = (rect.Bottom - winRect.Top) / winRect.Height
                        If bottomRatio > 0.55 Then
                            score *= 8
                        ElseIf bottomRatio < 0.2 Then
                            score *= 0.5
                        End If
                    End If
                    If score > bestScore Then
                        bestScore = score
                        best = el
                    End If
                Catch
                End Try
            Next
            Return best
        Catch
            Return Nothing
        End Try
    End Function

    '点击输入框中心获得焦点，再用操作内核模拟键盘输入（适用于不支持 UIA 写入的应用）
    Private Function TryClickAndType(text As String, targetHwnd As IntPtr) As Boolean
        Try
            Dim window As AutomationElement = AutomationElement.FromHandle(targetHwnd)
            If window Is Nothing Then Return False
            Dim inputElement As AutomationElement = FindInputElement(window)
            If inputElement Is Nothing Then Return False
            Dim rect As System.Windows.Rect = inputElement.Current.BoundingRectangle
            If rect.IsEmpty OrElse rect.Width < 10 OrElse rect.Height < 10 Then Return False
            Dim cx As Integer = CInt(rect.X + rect.Width / 2)
            Dim cy As Integer = CInt(rect.Y + rect.Height / 2)
            If Not OpEngine.MouseClick(1, cx, cy) Then Return False
            Threading.Thread.Sleep(80)
            Return OpEngine.TypeText(text)
        Catch
            Return False
        End Try
    End Function

    Private Function TrySetElementValue(element As AutomationElement, text As String) As Boolean
        Try
            Dim pattern As Object = Nothing
            If element.TryGetCurrentPattern(ValuePattern.Pattern, pattern) AndAlso pattern IsNot Nothing Then
                Dim valuePattern As ValuePattern = CType(pattern, ValuePattern)
                If Not valuePattern.Current.IsReadOnly Then
                    valuePattern.SetValue(text)
                    '把焦点切到该控件，保证后续按 Enter 等发送键生效
                    Try
                        element.SetFocus()
                    Catch
                    End Try
                    Return True
                End If
            End If
        Catch
        End Try
        Return False
    End Function

    Private Function TrySetTextViaWmSettext(text As String, targetHwnd As IntPtr) As Boolean
        Try
            Dim hwndFocus As IntPtr
            If targetHwnd <> IntPtr.Zero Then
                hwndFocus = GetFocusedControlHwnd(targetHwnd)
            Else
                hwndFocus = GetForegroundFocusedControl()
            End If
            If hwndFocus = IntPtr.Zero Then Return False
            SendMessageText(hwndFocus, WM_SETTEXT, IntPtr.Zero, text)
            Return True
        Catch
            Return False
        End Try
    End Function

    '获取指定窗口所在线程的焦点控件句柄
    Private Function GetFocusedControlHwnd(hwnd As IntPtr) As IntPtr
        Try
            If hwnd = IntPtr.Zero Then Return IntPtr.Zero
            Dim processId As UInteger = 0
            Dim threadId As UInteger = GetWindowThreadProcessIdApi(hwnd, processId)
            If threadId = 0 Then Return IntPtr.Zero
            Dim info As New GUITHREADINFO
            info.cbSize = Marshal.SizeOf(GetType(GUITHREADINFO))
            If GetGUIThreadInfo(threadId, info) Then Return info.hwndFocus
            Return IntPtr.Zero
        Catch
            Return IntPtr.Zero
        End Try
    End Function

    Private Function GetForegroundFocusedControl() As IntPtr
        Try
            Return GetFocusedControlHwnd(GetForegroundWindowApi())
        Catch
            Return IntPtr.Zero
        End Try
    End Function

    Private Const WM_SETTEXT As Integer = &HC

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECTAPI
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure GUITHREADINFO
        Public cbSize As Integer
        Public flags As Integer
        Public hwndActive As IntPtr
        Public hwndFocus As IntPtr
        Public hwndCapture As IntPtr
        Public hwndMenuOwner As IntPtr
        Public hwndMoveSize As IntPtr
        Public hwndCaret As IntPtr
        Public rcCaret As RECTAPI
    End Structure

    <DllImport("user32.dll")>
    Private Function GetGUIThreadInfo(idThread As UInteger, ByRef pgui As GUITHREADINFO) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="GetForegroundWindow")>
    Private Function GetForegroundWindowApi() As IntPtr
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowThreadProcessId")>
    Private Function GetWindowThreadProcessIdApi(hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger
    End Function

    <DllImport("user32.dll", EntryPoint:="SendMessageW", CharSet:=CharSet.Unicode)>
    Private Function SendMessageText(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As String) As IntPtr
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowRect")>
    Private Function GetWindowRectApi(hWnd As IntPtr, ByRef lpRect As RECTAPI) As Boolean
    End Function

    '在目标窗口中查找常见名称的“发送”按钮并通过 UIA 调用（Invoke 优先，其次点击按钮中心）。
    '找到并触发返回 True，否则返回 False。
    Public Function TryInvokeSendButton(hWnd As IntPtr) As Boolean
        If hWnd = IntPtr.Zero Then Return False
        Try
            Dim window As AutomationElement = AutomationElement.FromHandle(hWnd)
            If window Is Nothing Then Return False
            Dim condition As New PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button)
            Dim buttons As AutomationElementCollection = window.FindAll(TreeScope.Subtree, condition)
            If buttons Is Nothing OrElse buttons.Count = 0 Then Return False
            For Each button As AutomationElement In buttons
                Dim name As String = ""
                Try
                    name = If(button.Current.Name, "")
                Catch
                End Try
                If Not IsSendButtonName(name) Then Continue For
                Dim pattern As Object = Nothing
                If button.TryGetCurrentPattern(InvokePattern.Pattern, pattern) AndAlso pattern IsNot Nothing Then
                    DirectCast(pattern, InvokePattern).Invoke()
                    Return True
                End If
                Dim rect As System.Windows.Rect = Nothing
                Try
                    rect = button.Current.BoundingRectangle
                Catch
                End Try
                If Not rect.IsEmpty AndAlso rect.Width > 0 AndAlso rect.Height > 0 Then
                    OpEngine.MouseClick(1, CInt(rect.X + rect.Width / 2), CInt(rect.Y + rect.Height / 2))
                    Return True
                End If
            Next
        Catch
        End Try
        Return False
    End Function

    Private Function IsSendButtonName(name As String) As Boolean
        If String.IsNullOrWhiteSpace(name) Then Return False
        Dim keywords As String() = {"发送", "发送消息", "回复", "确定", "发表", "发布", "评论", "提交", "Send", "Submit", "Post"}
        For Each keyword As String In keywords
            If name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0 Then Return True
        Next
        Return False
    End Function

#End Region



    '取虚拟键码
#Region "VirtualKey"
    Public Function ConvertKeyLogToVirtualKeyCodes(keylog As List(Of Key)) As List(Of Byte)
        Dim virtualKeyCodes As New List(Of Byte)
        For Each part As Key In keylog
            virtualKeyCodes.Add(CType(KeyInterop.VirtualKeyFromKey(part), Byte))
        Next
        Return virtualKeyCodes
    End Function

    Public Function ConvertKeyToVirtualKeyCode(key As Key) As Byte
        Dim virtualKeyCode As Byte
        virtualKeyCode = CType(KeyInterop.VirtualKeyFromKey(key), Byte)
        Return virtualKeyCode
    End Function


#End Region



    '连点相关声明
#Region "MouseInput"
    '鼠标事件标志（统一经 SendInput 注入）
    Public Const MOUSEEVENTF_LEFTDOWN = &H2 '模拟鼠标左键按下
    Public Const MOUSEEVENTF_LEFTUP = &H4 '模拟鼠标左键释放
    Public Const MOUSEEVENTF_RIGHTDOWN = &H8 '模拟鼠标右键按下
    Public Const MOUSEEVENTF_RIGHTUP = &H10 '模拟鼠标右键释放
    Public Const MOUSEEVENTF_MIDDLEDOWN = &H20 '模拟鼠标中键按下
    Public Const MOUSEEVENTF_MIDDLEUP = &H40 '模拟鼠标中键释放
    Public Const MOUSEEVENTF_XDOWN = &H80 '模拟鼠标侧键按下
    Public Const MOUSEEVENTF_XUP = &H100 '模拟鼠标侧键释放
    Public Const MOUSEEVENTF_WHEEL = &H800 '模拟鼠标滚轮
    Public Declare Function GetCursorPos Lib "user32" (ByRef lpPoint As POINTAPI) As Long '全屏坐标声明
    Public Structure POINTAPI '声明坐标变量
        Public x As Integer '声明坐标变量为32位
        Public y As Integer '声明坐标变量为32位
    End Structure
#End Region



    '设置鼠标位置
#Region "SetCursorPos"

    <DllImport("user32.dll", SetLastError:=True)>
    Private Sub SetCursorPos(x As Integer, y As Integer)
    End Sub


    '入参 (x, y) 为屏幕绝对坐标。SetCursorPos 本身接收屏幕坐标，
    '不能先经 ScreenToClient 转成客户区坐标，否则定位错误。
    Public Sub SetCursorPosition(x As Integer, y As Integer)
        SetCursorPos(x, y)
    End Sub


#End Region


    '设置鼠标指针
#Region "SetCursor"
    <DllImport("user32.dll", SetLastError:=True)>
    Private Function SetCursor(hCursor As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function LoadCursor(hInstance As IntPtr, lpCursorName As Integer) As IntPtr
    End Function
#End Region


    '窗体选取功能
#Region "WindowSelector"
    Private Const WM_NCLBUTTONDOWN As Integer = &HA1
    Private Const HTCAPTION As Integer = 2

    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Function SendMessage(hWnd As IntPtr, Msg As Integer, wParam As Integer, lParam As Integer) As Integer
    End Function

    <DllImport("user32.dll", CharSet:=CharSet.Auto)>
    Private Function ReleaseCapture() As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function GetCursorPos(ByRef lpPoint As Point) As Boolean
    End Function

    '注意：Win32 WindowFromPoint 按值接收 8 字节 POINT(两个 32 位整数)，
    '不能用 System.Windows.Point(两个 Double,16 字节)，否则 marshalling 数据错位。
    <DllImport("user32.dll", SetLastError:=True)>
    Public Function WindowFromPoint(pt As POINTAPI) As IntPtr
    End Function

    Private Const IDC_HAND As Integer = 32649

    Public Event WindowSelected(hWnd As IntPtr)
    Public Event PointCaptured(x As Integer, y As Integer, hWnd As IntPtr)

    Private mouseHook As New GlobalMouseHook()

    Public Sub StartSelection()
        AddHandler mouseHook.WindowSelected, AddressOf MouseHook_WindowSelected
        mouseHook.InstallHook()
        Dim handCursor As IntPtr = LoadCursor(IntPtr.Zero, IDC_HAND)
        SetCursor(handCursor)
    End Sub

    Public Sub StopSelection()
        RemoveHandler mouseHook.WindowSelected, AddressOf MouseHook_WindowSelected
        mouseHook.UninstallHook()
        '恢复默认鼠标指针样式
        SetCursor(IntPtr.Zero)
    End Sub

    Private Sub MouseHook_WindowSelected(hWnd As IntPtr, x As Integer, y As Integer)
        RaiseEvent WindowSelected(hWnd)
        RaiseEvent PointCaptured(x, y, hWnd)
    End Sub
#End Region



End Module
