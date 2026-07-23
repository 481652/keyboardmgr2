'设置程序的主题相关。
Imports System.Runtime.InteropServices
Imports System.Windows.Interop
Imports Microsoft.Win32

Module ThemeModule
#Region "Theme"
    <DllImport("dwmapi.dll", PreserveSig:=True)>
    Private Function DwmSetWindowAttribute(hWnd As IntPtr, dwAttribute As Integer, ByRef pvAttribute As Integer, cbAttribute As Integer) As Integer
    End Function

    <DllImport("dwmapi.dll", PreserveSig:=True)>
    Private Function DwmExtendFrameIntoClientArea(hWnd As IntPtr, ByRef margins As Margins) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Function SetWindowCompositionAttribute(hWnd As IntPtr, ByRef data As WindowCompositionAttributeData) As Boolean
    End Function

    Private Structure Margins
        Public Left As Integer
        Public Right As Integer
        Public Top As Integer
        Public Bottom As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure AccentPolicy
        Public State As Integer
        Public Flags As Integer
        Public GradientColor As Integer
        Public AnimationId As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure WindowCompositionAttributeData
        Public Attribute As Integer
        Public Data As IntPtr
        Public SizeOfData As Integer
    End Structure

    Private Const DWMWA_USE_IMMERSIVE_DARK_MODE As Integer = 20
    Private Const DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 As Integer = 19
    Private Const DWMWA_WINDOW_CORNER_PREFERENCE As Integer = 33
    Private Const DWMWA_SYSTEMBACKDROP_TYPE As Integer = 38
    Private Const DWMSBT_NONE As Integer = 1
    Private Const DWMSBT_MAINWINDOW As Integer = 2
    Private Const DWMSBT_TABBEDWINDOW As Integer = 4
    Private Const DWMWCP_ROUND As Integer = 2
    Private Const WCA_ACCENT_POLICY As Integer = 19
    Private Const ACCENT_DISABLED As Integer = 0
    Private Const ACCENT_ENABLE_HOSTBACKDROP As Integer = 5

    Public isDarkTheme As Boolean = False
    Public isMicaEnabled As Boolean = False
    Public ThemeColor As Boolean
    Public usercolor As Color
    Public color As Color

    Public Sub SwitchTheme(isDarkMode As Boolean)
        Dim themeUri As New Uri(If(isDarkMode, "resource/DarkTheme.xaml", "resource/LightTheme.xaml"), UriKind.Relative)
        Dim newResourceDict As New ResourceDictionary() With {.Source = themeUri}
        Windows.Application.Current.Resources.MergedDictionaries.Clear()
        Windows.Application.Current.Resources.MergedDictionaries.Add(newResourceDict)
        isDarkTheme = isDarkMode

        If Windows.Application.Current.MainWindow IsNot Nothing Then
            Windows.Application.Current.MainWindow.UpdateLayout()
            MainWindow1.Instance.Pinicon_Set()
        End If
        UpdateWindowBackdrops()
    End Sub

    Public Function IsDarkModeEnabled() As Boolean
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

    Public Function IsWindows11_22H2OrLater() As Boolean
        Dim os As Version = Environment.OSVersion.Version
        Return os.Major >= 10 AndAlso os.Build >= 22621
    End Function
#End Region

#Region "Mica"
    Public Sub UpdateWindowBackdrops()
        Dim useMica As Boolean = isMicaEnabled AndAlso IsWindows11_22H2OrLater()
        If MainWindow1.Instance IsNot Nothing AndAlso MainWindow1.Instance.IsLoaded Then
            ApplyWindowBackdrop(MainWindow1.Instance, MainWindow1.Instance.RootGrid, useMica, False)
        End If
        If FloatingWindow.Instance IsNot Nothing AndAlso FloatingWindow.Instance.IsLoaded Then
            ApplyWindowBackdrop(FloatingWindow.Instance, FloatingWindow.Instance.AnimatedBorder, useMica, True)
        End If
    End Sub

    Public Sub ApplyWindowBackdrop(targetWindow As Window, contentSurface As FrameworkElement, enabled As Boolean, Optional useMicaAlt As Boolean = False)
        If targetWindow Is Nothing Then Return

        Try
            Dim hwnd As IntPtr = New WindowInteropHelper(targetWindow).Handle
            If hwnd = IntPtr.Zero Then Return

            Dim darkModeValue As Integer = If(isDarkTheme, 1, 0)
            If DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, darkModeValue, Marshal.SizeOf(darkModeValue)) <> 0 Then
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, darkModeValue, Marshal.SizeOf(darkModeValue))
            End If

            Dim backdropType As Integer = If(enabled, If(useMicaAlt, DWMSBT_TABBEDWINDOW, DWMSBT_MAINWINDOW), DWMSBT_NONE)
            Dim backdropResult As Integer = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, backdropType, Marshal.SizeOf(backdropType))
            If enabled AndAlso backdropResult <> 0 Then Throw New COMException("系统未能启用云母背景", backdropResult)
            If useMicaAlt Then SetHostBackdrop(hwnd, enabled)

            Dim cornerPreference As Integer = DWMWCP_ROUND
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, cornerPreference, Marshal.SizeOf(cornerPreference))

            If enabled Then
                Dim frameMargins As New Margins With {.Left = -1, .Right = -1, .Top = -1, .Bottom = -1}
                Dim frameResult As Integer = DwmExtendFrameIntoClientArea(hwnd, frameMargins)
                If frameResult <> 0 Then Throw New COMException("系统未能将云母扩展到客户区", frameResult)
                targetWindow.Background = Brushes.Transparent
                If TypeOf contentSurface Is Panel Then
                    DirectCast(contentSurface, Panel).Background = Brushes.Transparent
                ElseIf TypeOf contentSurface Is Border Then
                    DirectCast(contentSurface, Border).Background = Brushes.Transparent
                ElseIf TypeOf contentSurface Is Control Then
                    DirectCast(contentSurface, Control).Background = Brushes.Transparent
                End If
                Dim source As HwndSource = HwndSource.FromHwnd(hwnd)
                If source IsNot Nothing Then source.CompositionTarget.BackgroundColor = Colors.Transparent
            Else
                Dim backgroundBrush As Brush = TryCast(Application.Current.TryFindResource("backgroundColor1"), Brush)
                Dim frameMargins As New Margins With {.Left = 0, .Right = 0, .Top = 0, .Bottom = 0}
                DwmExtendFrameIntoClientArea(hwnd, frameMargins)
                targetWindow.Background = backgroundBrush
                If TypeOf contentSurface Is Panel Then
                    DirectCast(contentSurface, Panel).Background = Brushes.Transparent
                ElseIf TypeOf contentSurface Is Border Then
                    DirectCast(contentSurface, Border).Background = backgroundBrush
                End If
                Dim source As HwndSource = HwndSource.FromHwnd(hwnd)
                If source IsNot Nothing AndAlso TypeOf backgroundBrush Is SolidColorBrush Then
                    source.CompositionTarget.BackgroundColor = DirectCast(backgroundBrush, SolidColorBrush).Color
                End If
            End If
        Catch ex As Exception
            isMicaEnabled = False
            WriteSetting("IsMicaEnabled", 0)
            Dim backgroundBrush As Brush = TryCast(Application.Current.TryFindResource("backgroundColor1"), Brush)
            targetWindow.Background = backgroundBrush
            If TypeOf contentSurface Is Border Then DirectCast(contentSurface, Border).Background = backgroundBrush
        End Try
    End Sub

    Private Sub SetHostBackdrop(hwnd As IntPtr, enabled As Boolean)
        Dim policy As New AccentPolicy With {
            .State = If(enabled, ACCENT_ENABLE_HOSTBACKDROP, ACCENT_DISABLED)
        }
        Dim policyPointer As IntPtr = Marshal.AllocHGlobal(Marshal.SizeOf(policy))
        Try
            Marshal.StructureToPtr(policy, policyPointer, False)
            Dim data As New WindowCompositionAttributeData With {
                .Attribute = WCA_ACCENT_POLICY,
                .Data = policyPointer,
                .SizeOfData = Marshal.SizeOf(policy)
            }
            SetWindowCompositionAttribute(hwnd, data)
        Finally
            Marshal.FreeHGlobal(policyPointer)
        End Try
    End Sub
#End Region
End Module
