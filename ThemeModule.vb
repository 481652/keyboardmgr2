'设置程序的主题相关。
Imports System.Runtime.InteropServices
Imports Microsoft.Win32

Module ThemeModule
    <DllImport("dwmapi.dll", PreserveSig:=False)>
    Public Sub DwmSetWindowAttribute(hWnd As IntPtr, dwAttribute As Integer, pvAttribute As IntPtr, cbAttribute As Integer)
    End Sub
    Public Const DWMWA_USE_IMMERSIVE_DARK_MODE As Integer = 20
    Public isDarkTheme As Boolean = False '这个全局变量可以获取应用程序主题
    Public ThemeColor As Boolean
    Public usercolor As Color
    Public color As Color
    '切换深浅色主题，需要时调用即可
    Public Sub SwitchTheme(isDarkMode As Boolean)
        '切换深浅色资源
        Dim themeUri As New Uri(If(isDarkMode, "resource/DarkTheme.xaml", "resource/LightTheme.xaml"), UriKind.Relative)
        Dim newResourceDict As New ResourceDictionary() With {.Source = themeUri}
        Windows.Application.Current.Resources.MergedDictionaries.Clear()
        Windows.Application.Current.Resources.MergedDictionaries.Add(newResourceDict)
        isDarkTheme = isDarkMode
        Windows.Application.Current.MainWindow.UpdateLayout()
        'fix:修复pinicon的颜色问题
        MainWindow1.Instance.Pinicon_Set()
        '设置窗体属性
        Dim hWnd As IntPtr = New System.Windows.Interop.WindowInteropHelper(Windows.Application.Current.MainWindow).Handle
        Dim attributeValue As Integer = If(isDarkMode, 1, 0)
        Dim pvAttribute As IntPtr = Marshal.AllocHGlobal(Marshal.SizeOf(attributeValue))
        Marshal.StructureToPtr(attributeValue, pvAttribute, False)
        DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, pvAttribute, Marshal.SizeOf(attributeValue))
        Marshal.FreeHGlobal(pvAttribute)
    End Sub
    '这里重新写了一套实现，不同于老版本
    Public Function IsDarkModeEnabled() As Boolean '检测深色模式是否开启
        Const keyPath As String = "Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"
        Const valueName As String = "AppsUseLightTheme"

        Using key As RegistryKey = Registry.CurrentUser.OpenSubKey(keyPath)
            If key IsNot Nothing Then
                Dim value As Object = key.GetValue(valueName)
                If value IsNot Nothing AndAlso TypeOf value Is Integer Then
                    Return CType(value, Integer) = 0
                End If
            End If
        End Using

        Return False
    End Function


End Module
