
'键鼠操作相关功能。
Imports System.Runtime.InteropServices



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



    '模拟winform里的sendkeys,使用keybd_event函数
#Region "Sendkeys"

    <DllImport("user32.dll", SetLastError:=True)>
    Private Sub keybd_event(bVk As Byte, bScan As Byte, dwFlags As UInteger, dwExtraInfo As UInteger)
    End Sub

    Public Sub SendKey(key As Byte, isPress As Boolean)
        Dim flags As UInteger = If(isPress, 0, &H2) ' &H2 表示 KEYEVENTF_KEYUP
        keybd_event(key, 0, flags, 0)
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
    '此段内容（连点）可以复用，因此完全由老版本移植上来
    Declare Sub mouse_event Lib "user32" (dwFlags As Long, dx As Long, dy As Long, cButtons As Long, dwExtraInfo As Long)
    Public Const MOUSEEVENTF_LEFTDOWN = &H2 '模拟鼠标左键按下
    Public Const MOUSEEVENTF_LEFTUP = &H4 '模拟鼠标左键释放
    Public Const MOUSEEVENTF_RIGHTDOWN = &H8 '模拟鼠标右键按下
    Public Const MOUSEEVENTF_RIGHTUP = &H10 '模拟鼠标右键释放
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

    Private Sub MouseHook_WindowSelected(hWnd As IntPtr)
        RaiseEvent WindowSelected(hWnd)
    End Sub
#End Region



End Module
