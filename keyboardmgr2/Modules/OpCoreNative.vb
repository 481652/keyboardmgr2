'原生操作内核（OpCore DLL）P/Invoke 封装：提供键鼠操作与连点循环接口。
'DLL 按进程位数命名：OpCore64.dll（64 位进程）/ OpCore32.dll（32 位进程），
'两个文件都放在程序目录下，运行时按位数自动选择。
Imports System.Runtime.InteropServices

Module OpCoreNative

#Region "DllImports"

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_StartMouseClick", CallingConvention:=CallingConvention.StdCall)>
    Private Function StartMouseClick64(intervalMs As Integer, randomSpeedOffset As Integer, randomPosOffset As Integer, baseX As Integer, baseY As Integer, mouseButton As Integer) As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_StartMouseClick", CallingConvention:=CallingConvention.StdCall)>
    Private Function StartMouseClick32(intervalMs As Integer, randomSpeedOffset As Integer, randomPosOffset As Integer, baseX As Integer, baseY As Integer, mouseButton As Integer) As Integer
    End Function

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_StartKeyClick", CallingConvention:=CallingConvention.StdCall)>
    Private Function StartKeyClick64(intervalMs As Integer, keys As UShort(), keyCount As Integer) As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_StartKeyClick", CallingConvention:=CallingConvention.StdCall)>
    Private Function StartKeyClick32(intervalMs As Integer, keys As UShort(), keyCount As Integer) As Integer
    End Function

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_StartMouseHold", CallingConvention:=CallingConvention.StdCall)>
    Private Function StartMouseHold64(button As Integer) As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_StartMouseHold", CallingConvention:=CallingConvention.StdCall)>
    Private Function StartMouseHold32(button As Integer) As Integer
    End Function

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_StartKeyHold", CallingConvention:=CallingConvention.StdCall)>
    Private Function StartKeyHold64(keys As UShort(), keyCount As Integer) As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_StartKeyHold", CallingConvention:=CallingConvention.StdCall)>
    Private Function StartKeyHold32(keys As UShort(), keyCount As Integer) As Integer
    End Function

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_Stop", CallingConvention:=CallingConvention.StdCall)>
    Private Function Stop64() As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_Stop", CallingConvention:=CallingConvention.StdCall)>
    Private Function Stop32() As Integer
    End Function

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_IsRunning", CallingConvention:=CallingConvention.StdCall)>
    Private Function IsRunning64() As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_IsRunning", CallingConvention:=CallingConvention.StdCall)>
    Private Function IsRunning32() As Integer
    End Function

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_MouseMove", CallingConvention:=CallingConvention.StdCall)>
    Private Function MouseMove64(x As Integer, y As Integer) As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_MouseMove", CallingConvention:=CallingConvention.StdCall)>
    Private Function MouseMove32(x As Integer, y As Integer) As Integer
    End Function

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_MouseClick", CallingConvention:=CallingConvention.StdCall)>
    Private Function MouseClick64(button As Integer, x As Integer, y As Integer) As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_MouseClick", CallingConvention:=CallingConvention.StdCall)>
    Private Function MouseClick32(button As Integer, x As Integer, y As Integer) As Integer
    End Function

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_KeyTap", CallingConvention:=CallingConvention.StdCall)>
    Private Function KeyTap64(vk As UShort) As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_KeyTap", CallingConvention:=CallingConvention.StdCall)>
    Private Function KeyTap32(vk As UShort) As Integer
    End Function

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_KeyDown", CallingConvention:=CallingConvention.StdCall)>
    Private Function KeyDown64(vk As UShort) As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_KeyDown", CallingConvention:=CallingConvention.StdCall)>
    Private Function KeyDown32(vk As UShort) As Integer
    End Function

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_KeyUp", CallingConvention:=CallingConvention.StdCall)>
    Private Function KeyUp64(vk As UShort) As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_KeyUp", CallingConvention:=CallingConvention.StdCall)>
    Private Function KeyUp32(vk As UShort) As Integer
    End Function

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_KeyCombo", CallingConvention:=CallingConvention.StdCall)>
    Private Function KeyCombo64(keys As UShort(), keyCount As Integer) As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_KeyCombo", CallingConvention:=CallingConvention.StdCall)>
    Private Function KeyCombo32(keys As UShort(), keyCount As Integer) As Integer
    End Function

    <DllImport("OpCore64.dll", EntryPoint:="OpCore_TypeText", CallingConvention:=CallingConvention.StdCall, CharSet:=CharSet.Unicode)>
    Private Function TypeText64(text As String) As Integer
    End Function
    <DllImport("OpCore32.dll", EntryPoint:="OpCore_TypeText", CallingConvention:=CallingConvention.StdCall, CharSet:=CharSet.Unicode)>
    Private Function TypeText32(text As String) As Integer
    End Function

#End Region

    Private _is64 As Boolean = Environment.Is64BitProcess
    Private _probed As Boolean = False
    Private _available As Boolean = False

    <DllImport("kernel32.dll", SetLastError:=True, CharSet:=CharSet.Unicode)>
    Private Function LoadLibrary(lpFileName As String) As IntPtr
    End Function

    <DllImport("kernel32.dll")>
    Private Function FreeLibrary(hModule As IntPtr) As Boolean
    End Function

    '检测当前进程对应位数的 DLL 是否可用（只探测一次）
    Public Function IsAvailable() As Boolean
        If Not _probed Then
            _probed = True
            Dim dllName As String = If(_is64, "OpCore64.dll", "OpCore32.dll")
            Try
                Dim hModule As IntPtr = LoadLibrary(dllName)
                If hModule <> IntPtr.Zero Then
                    FreeLibrary(hModule)
                    _available = True
                End If
            Catch
                _available = False
            End Try
        End If
        Return _available
    End Function

    Public Function StartMouseClick(intervalMs As Integer, randomSpeedOffset As Boolean, randomPosOffset As Boolean, baseX As Integer, baseY As Integer, mouseButton As Integer) As Integer
        Dim speedFlag As Integer = If(randomSpeedOffset, 1, 0)
        Dim posFlag As Integer = If(randomPosOffset, 1, 0)
        If _is64 Then
            Return StartMouseClick64(intervalMs, speedFlag, posFlag, baseX, baseY, mouseButton)
        End If
        Return StartMouseClick32(intervalMs, speedFlag, posFlag, baseX, baseY, mouseButton)
    End Function

    Public Function StartKeyClick(intervalMs As Integer, keys As UShort()) As Integer
        If _is64 Then
            Return StartKeyClick64(intervalMs, keys, keys.Length)
        End If
        Return StartKeyClick32(intervalMs, keys, keys.Length)
    End Function

    Public Function StartMouseHold(mouseButton As Integer) As Integer
        If _is64 Then Return StartMouseHold64(mouseButton) Else Return StartMouseHold32(mouseButton)
    End Function

    Public Function StartKeyHold(keys As UShort()) As Integer
        If _is64 Then Return StartKeyHold64(keys, keys.Length) Else Return StartKeyHold32(keys, keys.Length)
    End Function

    Public Function StopCore() As Integer
        If _is64 Then Return Stop64() Else Return Stop32()
    End Function

    Public Function IsRunning() As Integer
        If _is64 Then Return IsRunning64() Else Return IsRunning32()
    End Function

    Public Function MouseMove(x As Integer, y As Integer) As Integer
        If _is64 Then Return MouseMove64(x, y) Else Return MouseMove32(x, y)
    End Function

    Public Function MouseClick(mouseButton As Integer, x As Integer, y As Integer) As Integer
        If _is64 Then Return MouseClick64(mouseButton, x, y) Else Return MouseClick32(mouseButton, x, y)
    End Function

    Public Function KeyTap(vk As UShort) As Integer
        If _is64 Then Return KeyTap64(vk) Else Return KeyTap32(vk)
    End Function

    Public Function KeyDown(vk As UShort) As Integer
        If _is64 Then Return KeyDown64(vk) Else Return KeyDown32(vk)
    End Function

    Public Function KeyUp(vk As UShort) As Integer
        If _is64 Then Return KeyUp64(vk) Else Return KeyUp32(vk)
    End Function

    Public Function KeyCombo(keys As UShort()) As Integer
        If _is64 Then Return KeyCombo64(keys, keys.Length) Else Return KeyCombo32(keys, keys.Length)
    End Function

    Public Function TypeText(text As String) As Integer
        If _is64 Then Return TypeText64(text) Else Return TypeText32(text)
    End Function

End Module
