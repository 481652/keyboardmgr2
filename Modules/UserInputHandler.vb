
'键鼠操作相关功能。
Imports System.Runtime.InteropServices

Module UserInputHandler
#Region "KeyTextbox"


    '初始化TextBox的键盘事件处理
    Public Sub InitializeTextBoxKeyHandler(textBox As TextBox)
        AddHandler textBox.PreviewKeyDown, AddressOf TextBox_PreviewKeyDown
    End Sub
    Private shared_keylog As List(Of Key)
    '处理TextBox的PreviewKeyDown事件
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
        End If
        If (Keyboard.Modifiers And ModifierKeys.Alt) = ModifierKeys.Alt Then
            keyPressed_Str = "Alt+" & keyPressed_Str
            keylog.Add(Key.LeftAlt)
        End If
        If (Keyboard.Modifiers And ModifierKeys.Windows) = ModifierKeys.Windows Then
            keyPressed_Str = "Win+" & keyPressed_Str
            keylog.Add(Key.LWin)
        End If
        '微调，去除重复键
        keyPressed_Str = keyPressed_Str.Replace("+LeftCtrl", "").Replace("+RightCtrl", "").Replace("+LeftShift", "").Replace("+System", "").Replace("LWin", "Win").Replace("RWin", "Win").Replace("Return", "Enter")
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
        For Each key In keys
            If key = &HA2 OrElse key = &HA0 OrElse key = &H12 Then ' Ctrl, Shift, Alt
                SendKey(key, True)
            End If
        Next
        ' 按下其余键
        For Each key In keys
            If key <> &HA2 AndAlso key <> &HA0 AndAlso key <> &H12 Then ' 排除修饰键
                SendKey(key, True)
            End If
        Next
        Threading.Thread.Sleep(50) ' 添加延时确保按键事件被识别
        ' 释放所有按键
        For Each key In keys
            SendKey(key, False)
        Next
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
    Public Declare Function ScreenToClient Lib "user32.dll" (hwnd As Integer, ByRef lpPoint As POINTAPI) As Integer '窗口坐标声明
    Public Structure POINTAPI '声明坐标变量
        Public x As Integer '声明坐标变量为32位
        Public y As Integer '声明坐标变量为32位
    End Structure
#End Region

    '设置鼠标位置
#Region "SetCursorPos"

    <DllImport("user32.dll", SetLastError:=True)>
        Private Function GetForegroundWindow() As IntPtr
        End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Function ScreenToClient(hWnd As IntPtr, ByRef lpPoint As POINTAPI) As Boolean
    End Function

    <DllImport("user32.dll", SetLastError:=True)>
    Private Sub SetCursorPos(x As Integer, y As Integer)
    End Sub


    Public Sub SetCursorPosition(x As Integer, y As Integer)
        Dim pt As New POINTAPI
        pt.x = x
        pt.y = y
        Dim hWnd As IntPtr = GetForegroundWindow()
        If ScreenToClient(hWnd, pt) Then
            SetCursorPos(pt.x, pt.y)
        End If
    End Sub


#End Region




End Module
