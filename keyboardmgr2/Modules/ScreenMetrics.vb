'屏幕度量：统一用"与鼠标坐标相同的像素坐标"描述屏幕分辨率。
'注意：WPF 的 SystemParameters.PrimaryScreenWidth/Height 返回设备无关单位（DIP），
'在非 100% 系统缩放下与 GetCursorPos/SetCursorPos 使用的像素坐标不是同一坐标系；
'而 GetSystemMetrics 与本进程的坐标虚拟化方式一致，因此录制文件头、回放比对一律改用它。
Imports System.Runtime.InteropServices

Public Module ScreenMetrics

    Private Const SM_CXSCREEN As Integer = 0
    Private Const SM_CYSCREEN As Integer = 1
    Private Const LOGPIXELSX As Integer = 88

    ''' <summary>当前主屏宽度（与鼠标事件坐标同一坐标空间）。</summary>
    Public Function GetPrimaryScreenWidth() As Integer
        Return Math.Max(1, GetSystemMetrics(SM_CXSCREEN))
    End Function

    ''' <summary>当前主屏高度（与鼠标事件坐标同一坐标空间）。</summary>
    Public Function GetPrimaryScreenHeight() As Integer
        Return Math.Max(1, GetSystemMetrics(SM_CYSCREEN))
    End Function

    ''' <summary>当前系统 DPI 缩放（1.0 = 100%）。用于显示和旧格式换算，不参与回放定位。</summary>
    Public Function GetDpiScale() As Double
        Return Math.Round(GetDpi() / 96.0, 3)
    End Function

    ''' <summary>当前系统 DPI 缩放的千分比（1000 = 100%）。</summary>
    Public Function GetDpiScalePermille() As Integer
        Return CInt(Math.Round(GetDpiScale() * 1000))
    End Function

    Private Function GetDpi() As Integer
        Try
            Dim systemDpi As UInteger = GetDpiForSystem()
            If systemDpi > 0UI Then Return CInt(systemDpi)
        Catch ex As Exception When TypeOf ex Is DllNotFoundException OrElse TypeOf ex Is EntryPointNotFoundException
            '旧系统没有 GetDpiForSystem，走下面的屏幕 DC 回退路径
        End Try
        Dim screenDc As IntPtr = GetDC(IntPtr.Zero)
        If screenDc = IntPtr.Zero Then Return 96
        Try
            Dim dpi As Integer = GetDeviceCaps(screenDc, LOGPIXELSX)
            If dpi > 0 Then Return dpi
        Finally
            ReleaseDC(IntPtr.Zero, screenDc)
        End Try
        Return 96
    End Function

    <DllImport("user32.dll")>
    Private Function GetSystemMetrics(nIndex As Integer) As Integer
    End Function

    <DllImport("user32.dll")>
    Private Function GetDpiForSystem() As UInteger
    End Function

    <DllImport("user32.dll")>
    Private Function GetDC(hWnd As IntPtr) As IntPtr
    End Function

    <DllImport("user32.dll")>
    Private Function ReleaseDC(hWnd As IntPtr, hDC As IntPtr) As Integer
    End Function

    <DllImport("gdi32.dll")>
    Private Function GetDeviceCaps(hdc As IntPtr, index As Integer) As Integer
    End Function

End Module

''' <summary>
''' 回放坐标变换：把录制坐标换算到当前屏幕。
''' 只缩放落在录制主屏范围（0..SourceWidth-1 / 0..SourceHeight-1）内的坐标；
''' 负坐标（副屏）或超出录制主屏的坐标保持原值，避免被比例放大到更远的位置。
''' 缩放比为 1 时完全是恒等变换。
''' </summary>
Public Structure PlaybackTransform

    Public ReadOnly ScaleX As Double
    Public ReadOnly ScaleY As Double
    Public ReadOnly SourceWidth As Integer
    Public ReadOnly SourceHeight As Integer

    Public Sub New(scaleX As Double, scaleY As Double, sourceWidth As Integer, sourceHeight As Integer)
        Me.ScaleX = scaleX
        Me.ScaleY = scaleY
        Me.SourceWidth = sourceWidth
        Me.SourceHeight = sourceHeight
    End Sub

    Public Shared ReadOnly Identity As PlaybackTransform = New PlaybackTransform(1.0, 1.0, 0, 0)

    Public ReadOnly Property IsIdentity As Boolean
        Get
            Return ScaleX = 1.0 AndAlso ScaleY = 1.0
        End Get
    End Property

    Public Function MapX(value As Integer) As Integer
        If ScaleX = 1.0 Then Return value
        If Double.IsNaN(ScaleX) OrElse Double.IsInfinity(ScaleX) OrElse ScaleX <= 0 Then Return value
        If value < 0 Then Return value
        If SourceWidth > 0 AndAlso value >= SourceWidth Then Return value
        Return ScaleValue(value, ScaleX)
    End Function

    Public Function MapY(value As Integer) As Integer
        If ScaleY = 1.0 Then Return value
        If Double.IsNaN(ScaleY) OrElse Double.IsInfinity(ScaleY) OrElse ScaleY <= 0 Then Return value
        If value < 0 Then Return value
        If SourceHeight > 0 AndAlso value >= SourceHeight Then Return value
        Return ScaleValue(value, ScaleY)
    End Function

    '用 Double/Int64 计算并夹到 Integer 范围内，避免异常数据导致溢出
    Private Shared Function ScaleValue(value As Integer, scale As Double) As Integer
        Dim scaled As Double = value * scale
        If scaled >= Integer.MaxValue Then Return Integer.MaxValue
        If scaled <= Integer.MinValue Then Return Integer.MinValue
        Return CInt(Math.Round(scaled))
    End Function

End Structure
