'操作内核分发层：根据用户选项与原生 DLL 可用性，在原生(C++)与兼容(托管)内核间选择。
'选项 OpCoreEngine：0=自动（优先原生），1=原生内核，2=兼容内核。
'统一提供键鼠操作接口与连点循环接口。
Module OpEngine

    Private _useNative As Boolean = False

    Public Sub Initialize()
        Dim mode As Integer = 0
        Integer.TryParse(ReadSetting("OpCoreEngine", 0).ToString(), mode)
        If mode = 1 Then
            '强制原生：加载失败则提示并回退兼容
            _useNative = True
            If Not OpCoreNative.IsAvailable() Then
                ShowMyMessage("原生操作内核（OpCore 组件）加载失败，已自动回退到兼容内核。")
                _useNative = False
            End If
        ElseIf mode = 2 Then
            '强制兼容
            _useNative = False
        Else
            '自动：优先原生，不可用则兼容
            _useNative = OpCoreNative.IsAvailable()
        End If
    End Sub

    Public ReadOnly Property UsesNativeCore() As Boolean
        Get
            Return _useNative
        End Get
    End Property

#Region "连点循环"

    Public Sub StartMouseClick(intervalMs As Integer, randomSpeedOffset As Boolean, randomPosOffset As Boolean, baseX As Integer, baseY As Integer, mouseButton As Integer)
        If _useNative Then
            OpCoreNative.StartMouseClick(intervalMs, randomSpeedOffset, randomPosOffset, baseX, baseY, mouseButton)
        Else
            OpCoreManaged.StartMouseClick(intervalMs, randomSpeedOffset, randomPosOffset, baseX, baseY, mouseButton)
        End If
    End Sub

    Public Sub StartKeyClick(intervalMs As Integer, keys As UShort())
        If _useNative Then
            OpCoreNative.StartKeyClick(intervalMs, keys)
        Else
            OpCoreManaged.StartKeyClick(intervalMs, keys)
        End If
    End Sub

    Public Sub StartMouseHold(mouseButton As Integer)
        If _useNative Then
            OpCoreNative.StartMouseHold(mouseButton)
        Else
            OpCoreManaged.StartMouseHold(mouseButton)
        End If
    End Sub

    Public Sub StartKeyHold(keys As UShort())
        If _useNative Then
            OpCoreNative.StartKeyHold(keys)
        Else
            OpCoreManaged.StartKeyHold(keys)
        End If
    End Sub

    Public Sub StopAll()
        If _useNative Then
            OpCoreNative.StopCore()
        Else
            OpCoreManaged.StopAll()
        End If
    End Sub

    Public Function IsRunning() As Boolean
        If _useNative Then
            Return OpCoreNative.IsRunning() <> 0
        End If
        Return OpCoreManaged.IsRunning()
    End Function

#End Region

#Region "通用键鼠操作"

    Public Function MouseMove(x As Integer, y As Integer) As Boolean
        If _useNative Then
            Return OpCoreNative.MouseMove(x, y) = 0
        End If
        Return OpCoreManaged.MouseMove(x, y)
    End Function

    'x/y 为负时在当前光标位置点击
    Public Function MouseClick(mouseButton As Integer, x As Integer, y As Integer) As Boolean
        If _useNative Then
            Return OpCoreNative.MouseClick(mouseButton, x, y) = 0
        End If
        Return OpCoreManaged.MouseClick(mouseButton, x, y)
    End Function

    Public Function KeyTap(vk As UShort) As Boolean
        If _useNative Then
            Return OpCoreNative.KeyTap(vk) = 0
        End If
        Return OpCoreManaged.KeyTap(vk)
    End Function

    Public Function KeyDown(vk As UShort) As Boolean
        If _useNative Then
            Return OpCoreNative.KeyDown(vk) = 0
        End If
        Return OpCoreManaged.KeyDown(vk)
    End Function

    Public Function KeyUp(vk As UShort) As Boolean
        If _useNative Then
            Return OpCoreNative.KeyUp(vk) = 0
        End If
        Return OpCoreManaged.KeyUp(vk)
    End Function

    Public Function KeyCombo(keys As UShort()) As Boolean
        If keys Is Nothing OrElse keys.Length < 1 Then Return False
        If _useNative Then
            Return OpCoreNative.KeyCombo(keys) = 0
        End If
        Return OpCoreManaged.KeyCombo(keys)
    End Function

    Public Function TypeText(text As String) As Boolean
        If _useNative Then
            Return OpCoreNative.TypeText(text) = 0
        End If
        Return OpCoreManaged.TypeText(text)
    End Function

#End Region

End Module
