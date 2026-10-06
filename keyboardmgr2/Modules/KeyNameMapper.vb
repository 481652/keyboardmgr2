'按键名称与虚拟键码互转：供脚本命令（KeyTap 'CTRL' 等）与录制导出使用。
Public Module KeyNameMapper
    Private ReadOnly nameToVk As New Dictionary(Of String, UShort)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly vkToName As New Dictionary(Of UShort, String)

    Sub New()
        AddKey("CTRL", &H11US)
        AddKey("CONTROL", &H11US)
        AddKey("LCTRL", &HA2US)
        AddKey("LCONTROL", &HA2US)
        AddKey("RCTRL", &HA3US)
        AddKey("RCONTROL", &HA3US)
        AddKey("SHIFT", &H10US)
        AddKey("LSHIFT", &HA0US)
        AddKey("RSHIFT", &HA1US)
        AddKey("ALT", &H12US)
        AddKey("MENU", &H12US)
        AddKey("LALT", &HA4US)
        AddKey("RALT", &HA5US)
        AddKey("WIN", &H5BUS)
        AddKey("LWIN", &H5BUS)
        AddKey("RWIN", &H5CUS)
        AddKey("ENTER", &HDUS)
        AddKey("RETURN", &HDUS)
        AddKey("TAB", &H9US)
        AddKey("SPACE", &H20US)
        AddKey("ESC", &H1BUS)
        AddKey("ESCAPE", &H1BUS)
        AddKey("BACKSPACE", &H8US)
        AddKey("BKSP", &H8US)
        AddKey("DELETE", &H2EUS)
        AddKey("DEL", &H2EUS)
        AddKey("INSERT", &H2DUS)
        AddKey("INS", &H2DUS)
        AddKey("HOME", &H24US)
        AddKey("END", &H23US)
        AddKey("PAGEUP", &H21US)
        AddKey("PGUP", &H21US)
        AddKey("PAGEDOWN", &H22US)
        AddKey("PGDN", &H22US)
        AddKey("UP", &H26US)
        AddKey("DOWN", &H28US)
        AddKey("LEFT", &H25US)
        AddKey("RIGHT", &H27US)
        AddKey("CAPSLOCK", &H14US)
        AddKey("NUMLOCK", &H90US)
        AddKey("SCROLLLOCK", &H91US)
        AddKey("PRINTSCREEN", &H2CUS)
        AddKey("PRTSC", &H2CUS)
        AddKey("PAUSE", &H13US)
        AddKey("APPS", &H5DUS)
        AddKey("NUMPAD0", &H60US)
        AddKey("NUMPAD1", &H61US)
        AddKey("NUMPAD2", &H62US)
        AddKey("NUMPAD3", &H63US)
        AddKey("NUMPAD4", &H64US)
        AddKey("NUMPAD5", &H65US)
        AddKey("NUMPAD6", &H66US)
        AddKey("NUMPAD7", &H67US)
        AddKey("NUMPAD8", &H68US)
        AddKey("NUMPAD9", &H69US)
        AddKey("MULTIPLY", &H6AUS)
        AddKey("ADD", &H6BUS)
        AddKey("SUBTRACT", &H6DUS)
        AddKey("DECIMAL", &H6EUS)
        AddKey("DIVIDE", &H6FUS)

        AddKey("SEMICOLON", &HBAUS)
        AddKey(";", &HBAUS, "SEMICOLON")
        AddKey("EQUALS", &HBBUS)
        AddKey("=", &HBBUS, "EQUALS")
        AddKey("COMMA", &HBCUS)
        AddKey(",", &HBCUS, "COMMA")
        AddKey("MINUS", &HBDUS)
        AddKey("-", &HBDUS, "MINUS")
        AddKey("PERIOD", &HBEUS)
        AddKey(".", &HBEUS, "PERIOD")
        AddKey("SLASH", &HBFUS)
        AddKey("/", &HBFUS, "SLASH")
        AddKey("GRAVE", &HC0US)
        AddKey("`", &HC0US, "GRAVE")
        AddKey("LBRACKET", &HDBUS)
        AddKey("[", &HDBUS, "LBRACKET")
        AddKey("BACKSLASH", &HDCUS)
        AddKey("\", &HDCUS, "BACKSLASH")
        AddKey("RBRACKET", &HDDUS)
        AddKey("]", &HDDUS, "RBRACKET")
        AddKey("QUOTE", &HDEUS)
        AddKey("'", &HDEUS, "QUOTE")

        For index As Integer = 0 To 23
            AddKey("F" & (index + 1), CUShort(&H70 + index))
        Next
        For code As Integer = AscW("A"c) To AscW("Z"c)
            AddKey(ChrW(code).ToString(), CUShort(code))
        Next
        For code As Integer = AscW("0"c) To AscW("9"c)
            AddKey(ChrW(code).ToString(), CUShort(code))
        Next
    End Sub

    Private Sub AddKey(name As String, vk As UShort, Optional canonical As String = Nothing)
        nameToVk(name) = vk
        If canonical Is Nothing Then canonical = name
        If Not vkToName.ContainsKey(vk) Then vkToName(vk) = canonical
    End Sub

    Public Function ParseKeyName(name As String) As UShort
        If String.IsNullOrWhiteSpace(name) Then Return 0
        Dim trimmed As String = name.Trim()
        Dim vk As UShort
        If nameToVk.TryGetValue(trimmed, vk) Then Return vk
        If trimmed.Length = 1 Then
            Dim upper As String = Char.ToUpperInvariant(trimmed(0)).ToString()
            If nameToVk.TryGetValue(upper, vk) Then Return vk
        End If
        If trimmed.StartsWith("VK_0X", StringComparison.OrdinalIgnoreCase) Then
            Dim value As Integer
            If Integer.TryParse(trimmed.Substring(5), Globalization.NumberStyles.HexNumber, Globalization.CultureInfo.InvariantCulture, value) AndAlso value > 0 AndAlso value <= 254 Then
                Return CUShort(value)
            End If
        End If
        Return 0
    End Function

    Public Function FormatKeyName(vk As UShort) As String
        Dim name As String = Nothing
        If vkToName.TryGetValue(vk, name) Then Return name
        Return "VK_0x" & vk.ToString("X2")
    End Function
End Module
