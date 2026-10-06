'脚本宿主：注入 PowerShell 会话的自动化 API（键鼠输入、阻止输入、取色截屏、窗口枚举、剪贴板等）。
Imports System.Drawing
Imports System.Drawing.Imaging
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text
Imports System.Threading

Public Class WindowInfo
    Public Property Hwnd As Long
    Public Property Title As String
    Public Property ClassName As String
    Public Property ProcessId As Integer
    Public Property X As Integer
    Public Property Y As Integer
    Public Property Width As Integer
    Public Property Height As Integer
    Public Property Visible As Boolean
End Class

Public Class CursorInfo
    Public Property X As Integer
    Public Property Y As Integer
End Class

Public Class ScriptHost

#Region "输入"

    Public Sub Delay(ms As Integer)
        If ms < 0 Then ms = 0
        If ms > 3600000 Then ms = 3600000
        Dim remaining As Integer = ms
        While remaining > 0
            If ScriptRunner.StopRequested Then Return
            Dim chunk As Integer = Math.Min(remaining, 50)
            Thread.Sleep(chunk)
            remaining -= chunk
        End While
    End Sub

    Public Sub Sleep(ms As Integer)
        Delay(ms)
    End Sub

    Public Function Move(x As Integer, y As Integer) As Boolean
        Return OpEngine.MouseMove(x, y)
    End Function

    Public Function Click(x As Integer, y As Integer, Optional button As String = "Left") As Boolean
        Dim parsed As Integer = ParseButton(button)
        If parsed = 1 OrElse parsed = 2 Then Return OpEngine.MouseClick(parsed, x, y)
        OpEngine.MouseMove(x, y)
        Thread.Sleep(10)
        SendMouseButton(parsed, True)
        SendMouseButton(parsed, False)
        Return True
    End Function

    Public Function DoubleClick(x As Integer, y As Integer, Optional button As String = "Left") As Boolean
        Click(x, y, button)
        Thread.Sleep(50)
        Return Click(x, y, button)
    End Function

    Public Function MouseDown(Optional button As String = "Left") As Boolean
        SendMouseButton(ParseButton(button), True)
        Return True
    End Function

    Public Function MouseUp(Optional button As String = "Left") As Boolean
        SendMouseButton(ParseButton(button), False)
        Return True
    End Function

    Public Function Wheel(delta As Integer) As Boolean
        If delta < -12000 Then delta = -12000
        If delta > 12000 Then delta = 12000
        UserInputHandler.mouse_event(UserInputHandler.MOUSEEVENTF_WHEEL, 0, 0, delta, 0)
        Return True
    End Function

    Public Function Drag(x1 As Integer, y1 As Integer, x2 As Integer, y2 As Integer, Optional button As String = "Left", Optional durationMs As Integer = 300) As Boolean
        Dim parsed As Integer = ParseButton(button)
        If durationMs < 1 Then durationMs = 1
        If durationMs > 60000 Then durationMs = 60000
        OpEngine.MouseMove(x1, y1)
        Thread.Sleep(30)
        SendMouseButton(parsed, True)
        Try
            Dim steps As Integer = Math.Max(1, Math.Min(60, durationMs \ 10))
            For moveStep As Integer = 1 To steps
                If ScriptRunner.StopRequested Then Exit For
                Dim ratio As Double = moveStep / CDbl(steps)
                OpEngine.MouseMove(CInt(x1 + (x2 - x1) * ratio), CInt(y1 + (y2 - y1) * ratio))
                Thread.Sleep(Math.Max(1, durationMs \ steps))
            Next
        Finally
            SendMouseButton(parsed, False)
        End Try
        Return True
    End Function

    Public Function KeyDown(key As String) As Boolean
        Dim vk As UShort = ParseKey(key)
        Return OpEngine.KeyDown(vk)
    End Function

    Public Function KeyUp(key As String) As Boolean
        Dim vk As UShort = ParseKey(key)
        Return OpEngine.KeyUp(vk)
    End Function

    Public Function KeyTap(key As String) As Boolean
        Dim vk As UShort = ParseKey(key)
        Return OpEngine.KeyTap(vk)
    End Function

    Public Function Combo(comboText As String) As Boolean
        If String.IsNullOrWhiteSpace(comboText) Then Throw New ArgumentException("组合键不能为空。")
        Dim keys As New List(Of UShort)
        For Each part As String In comboText.Split("+"c)
            keys.Add(ParseKey(part))
        Next
        If keys.Count < 1 OrElse keys.Count > 5 Then Throw New ArgumentException("组合键最多支持5个按键。")
        Return OpEngine.KeyCombo(keys.ToArray())
    End Function

    Public Function TypeText(text As String) As Boolean
        Return OpEngine.TypeText(text)
    End Function

    Public Function BlockInput(block As Boolean) As Boolean
        Return SetBlockInput(block)
    End Function

    Private Function ParseKey(key As String) As UShort
        Dim vk As UShort = KeyNameMapper.ParseKeyName(key)
        If vk = 0 Then Throw New ArgumentException("未知按键名称：" & key)
        Return vk
    End Function

    Private Function ParseButton(button As String) As Integer
        Dim value As String = If(String.IsNullOrWhiteSpace(button), "Left", button.Trim())
        Select Case value.ToUpperInvariant()
            Case "LEFT", "L", "1"
                Return 1
            Case "RIGHT", "R", "2"
                Return 2
            Case "MIDDLE", "M", "3"
                Return 3
            Case "X1", "4"
                Return 4
            Case "X2", "5"
                Return 5
            Case Else
                Throw New ArgumentException("未知鼠标按钮：" & button)
        End Select
    End Function

    Private Sub SendMouseButton(button As Integer, down As Boolean)
        Select Case button
            Case 1
                UserInputHandler.mouse_event(If(down, UserInputHandler.MOUSEEVENTF_LEFTDOWN, UserInputHandler.MOUSEEVENTF_LEFTUP), 0, 0, 0, 0)
            Case 2
                UserInputHandler.mouse_event(If(down, UserInputHandler.MOUSEEVENTF_RIGHTDOWN, UserInputHandler.MOUSEEVENTF_RIGHTUP), 0, 0, 0, 0)
            Case 3
                UserInputHandler.mouse_event(If(down, UserInputHandler.MOUSEEVENTF_MIDDLEDOWN, UserInputHandler.MOUSEEVENTF_MIDDLEUP), 0, 0, 0, 0)
            Case 4
                UserInputHandler.mouse_event(If(down, UserInputHandler.MOUSEEVENTF_XDOWN, UserInputHandler.MOUSEEVENTF_XUP), 0, 0, 1, 0)
            Case 5
                UserInputHandler.mouse_event(If(down, UserInputHandler.MOUSEEVENTF_XDOWN, UserInputHandler.MOUSEEVENTF_XUP), 0, 0, 2, 0)
        End Select
    End Sub

#End Region

#Region "阻止用户输入"

    Private Shared blockInputActive As Boolean

    Public Shared Function SetBlockInput(block As Boolean) As Boolean
        Try
            Dim result As Boolean = BlockInputApi(block)
            If result Then blockInputActive = block
            Return result
        Catch
            Return False
        End Try
    End Function

    Public Shared Sub ForceUnblockInput()
        Try
            If blockInputActive Then BlockInputApi(False)
        Catch
        End Try
        blockInputActive = False
    End Sub

#End Region

#Region "屏幕读取"

    Private Shared pixelBitmap As Bitmap
    Private Shared pixelGraphics As Graphics
    Private Shared ReadOnly pixelLock As New Object()

    Public Function GetCursorPos() As CursorInfo
        Dim point As New POINTAPI
        If Not GetCursorPosApi(point) Then Throw New InvalidOperationException("无法获取鼠标位置。")
        Return New CursorInfo With {.X = point.x, .Y = point.y}
    End Function

    Public Function GetPixelColor(x As Integer, y As Integer) As String
        SyncLock pixelLock
            If pixelBitmap Is Nothing Then
                pixelBitmap = New Bitmap(1, 1, PixelFormat.Format32bppArgb)
                pixelGraphics = Graphics.FromImage(pixelBitmap)
            End If
            pixelGraphics.CopyFromScreen(x, y, 0, 0, New Size(1, 1), CopyPixelOperation.SourceCopy)
            Dim color As Color = pixelBitmap.GetPixel(0, 0)
            Return String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B)
        End SyncLock
    End Function

    Public Function GetCursorColor() As String
        Dim cursor As CursorInfo = GetCursorPos()
        Return GetPixelColor(cursor.X, cursor.Y)
    End Function

    Public Function SaveScreenRegion(x As Integer, y As Integer, w As Integer, h As Integer, filePath As String) As String
        If String.IsNullOrWhiteSpace(filePath) Then Throw New ArgumentException("保存路径不能为空。")
        If w < 1 OrElse w > 10000 OrElse h < 1 OrElse h > 10000 Then Throw New ArgumentException("截屏区域尺寸无效（1-10000）。")
        Dim fullPath As String = System.IO.Path.GetFullPath(filePath)
        Dim directory As String = System.IO.Path.GetDirectoryName(fullPath)
        If Not String.IsNullOrEmpty(directory) AndAlso Not System.IO.Directory.Exists(directory) Then System.IO.Directory.CreateDirectory(directory)
        Using bitmap As New Bitmap(w, h, PixelFormat.Format32bppArgb)
            Using graphics As Graphics = Graphics.FromImage(bitmap)
                graphics.CopyFromScreen(x, y, 0, 0, New Size(w, h), CopyPixelOperation.SourceCopy)
            End Using
            bitmap.Save(fullPath, ImageFormat.Png)
        End Using
        Return fullPath
    End Function

#End Region

#Region "窗口"

    Private Delegate Function EnumWindowsProc(hwnd As IntPtr, lParam As IntPtr) As Boolean

    Public Function GetWindows(Optional titleFilter As String = "", Optional classFilter As String = "", Optional visibleOnly As Boolean = True) As List(Of WindowInfo)
        Dim result As New List(Of WindowInfo)
        Dim callback As EnumWindowsProc =
            Function(hwnd As IntPtr, lParam As IntPtr) As Boolean
                Try
                    If visibleOnly AndAlso Not IsWindowVisibleApi(hwnd) Then Return True
                    Dim title As String = GetWindowTitle(hwnd)
                    Dim className As String = GetWindowClassName(hwnd)
                    If Not MatchWindowFilter(title, titleFilter) Then Return True
                    If Not MatchWindowFilter(className, classFilter) Then Return True
                    result.Add(BuildWindowInfo(hwnd, title, className))
                Catch
                End Try
                Return True
            End Function
        EnumWindowsApi(callback, IntPtr.Zero)
        Return result
    End Function

    Public Function GetForegroundWindow() As Long
        Return GetForegroundWindowApi().ToInt64()
    End Function

    Public Function SetForegroundWindow(hwnd As Long) As Boolean
        Return SetForegroundWindowApi(New IntPtr(hwnd))
    End Function

    Public Function GetWindowRect(hwnd As Long) As WindowInfo
        Dim handle As IntPtr = New IntPtr(hwnd)
        If handle = IntPtr.Zero OrElse Not IsWindowApi(handle) Then Throw New ArgumentException("窗口句柄无效。")
        Return BuildWindowInfo(handle, GetWindowTitle(handle), GetWindowClassName(handle))
    End Function

    Public Function CloseWindow(hwnd As Long) As Boolean
        Return PostMessageApi(New IntPtr(hwnd), WM_CLOSE, IntPtr.Zero, IntPtr.Zero)
    End Function

    Public Function MoveWindow(hwnd As Long, x As Integer, y As Integer, w As Integer, h As Integer) As Boolean
        Return MoveWindowApi(New IntPtr(hwnd), x, y, w, h, True)
    End Function

    Private Function MatchWindowFilter(value As String, filter As String) As Boolean
        If String.IsNullOrWhiteSpace(filter) OrElse filter = "*" Then Return True
        Return value Like filter
    End Function

    Private Function BuildWindowInfo(hwnd As IntPtr, title As String, className As String) As WindowInfo
        Dim info As New WindowInfo With {.Hwnd = hwnd.ToInt64(), .Title = title, .ClassName = className}
        Dim processId As UInteger = 0
        GetWindowThreadProcessIdApi(hwnd, processId)
        info.ProcessId = CInt(processId)
        Dim rect As New RECTAPI
        If GetWindowRectApi(hwnd, rect) Then
            info.X = rect.Left
            info.Y = rect.Top
            info.Width = rect.Right - rect.Left
            info.Height = rect.Bottom - rect.Top
        End If
        info.Visible = IsWindowVisibleApi(hwnd)
        Return info
    End Function

    Private Function GetWindowTitle(hwnd As IntPtr) As String
        Dim length As Integer = GetWindowTextLengthApi(hwnd)
        If length <= 0 Then Return String.Empty
        Dim builder As New StringBuilder(length + 1)
        GetWindowTextApi(hwnd, builder, builder.Capacity)
        Return builder.ToString()
    End Function

    Private Function GetWindowClassName(hwnd As IntPtr) As String
        Dim builder As New StringBuilder(256)
        GetClassNameApi(hwnd, builder, builder.Capacity)
        Return builder.ToString()
    End Function

#End Region

#Region "剪贴板与其他"

    Public Function SetClipboardText(text As String) As Boolean
        Dim value As String = If(text, String.Empty)
        For attempt As Integer = 1 To 10
            If OpenClipboardApi(IntPtr.Zero) Then
                Try
                    EmptyClipboardApi()
                    Dim bytes As Byte() = Encoding.Unicode.GetBytes(value & ChrW(0))
                    Dim handle As IntPtr = GlobalAllocApi(GMEM_MOVEABLE, CUInt(bytes.Length))
                    If handle = IntPtr.Zero Then Return False
                    Dim pointer As IntPtr = GlobalLockApi(handle)
                    If pointer = IntPtr.Zero Then
                        GlobalFreeApi(handle)
                        Return False
                    End If
                    Marshal.Copy(bytes, 0, pointer, bytes.Length)
                    GlobalUnlockApi(handle)
                    If SetClipboardDataApi(CF_UNICODETEXT, handle) = IntPtr.Zero Then
                        GlobalFreeApi(handle)
                        Return False
                    End If
                    Return True
                Finally
                    CloseClipboardApi()
                End Try
            End If
            Thread.Sleep(50)
        Next
        Return False
    End Function

    Public Function GetClipboardText() As String
        For attempt As Integer = 1 To 10
            If OpenClipboardApi(IntPtr.Zero) Then
                Try
                    Dim handle As IntPtr = GetClipboardDataApi(CF_UNICODETEXT)
                    If handle = IntPtr.Zero Then Return String.Empty
                    Dim pointer As IntPtr = GlobalLockApi(handle)
                    If pointer = IntPtr.Zero Then Return String.Empty
                    Try
                        Return Marshal.PtrToStringUni(pointer)
                    Finally
                        GlobalUnlockApi(handle)
                    End Try
                Finally
                    CloseClipboardApi()
                End Try
            End If
            Thread.Sleep(50)
        Next
        Return String.Empty
    End Function

    Public Function StartApp(path As String, Optional arguments As String = "") As Integer
        If String.IsNullOrWhiteSpace(path) Then Throw New ArgumentException("程序路径不能为空。")
        Dim startInfo As New Diagnostics.ProcessStartInfo With {
            .FileName = path,
            .Arguments = If(arguments, String.Empty),
            .UseShellExecute = True
        }
        Return Diagnostics.Process.Start(startInfo).Id
    End Function

#End Region

#Region "Win32"

    Private Const WM_CLOSE As Integer = &H10
    Private Const CF_UNICODETEXT As UInteger = 13UI
    Private Const GMEM_MOVEABLE As UInteger = 2UI

    <StructLayout(LayoutKind.Sequential)>
    Private Structure POINTAPI
        Public x As Integer
        Public y As Integer
    End Structure

    <StructLayout(LayoutKind.Sequential)>
    Private Structure RECTAPI
        Public Left As Integer
        Public Top As Integer
        Public Right As Integer
        Public Bottom As Integer
    End Structure

    <DllImport("user32.dll", EntryPoint:="BlockInput", SetLastError:=True)>
    Private Shared Function BlockInputApi(fBlockIt As Boolean) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="GetCursorPos", SetLastError:=True)>
    Private Shared Function GetCursorPosApi(ByRef lpPoint As POINTAPI) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="EnumWindows")>
    Private Shared Function EnumWindowsApi(lpEnumFunc As EnumWindowsProc, lParam As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="IsWindowVisible")>
    Private Shared Function IsWindowVisibleApi(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="IsWindow")>
    Private Shared Function IsWindowApi(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowTextW", CharSet:=CharSet.Unicode)>
    Private Shared Function GetWindowTextApi(hWnd As IntPtr, lpString As StringBuilder, nMaxCount As Integer) As Integer
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowTextLengthW", CharSet:=CharSet.Unicode)>
    Private Shared Function GetWindowTextLengthApi(hWnd As IntPtr) As Integer
    End Function

    <DllImport("user32.dll", EntryPoint:="GetClassNameW", CharSet:=CharSet.Unicode)>
    Private Shared Function GetClassNameApi(hWnd As IntPtr, lpClassName As StringBuilder, nMaxCount As Integer) As Integer
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowThreadProcessId")>
    Private Shared Function GetWindowThreadProcessIdApi(hWnd As IntPtr, ByRef lpdwProcessId As UInteger) As UInteger
    End Function

    <DllImport("user32.dll", EntryPoint:="GetWindowRect")>
    Private Shared Function GetWindowRectApi(hWnd As IntPtr, ByRef lpRect As RECTAPI) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="SetForegroundWindow")>
    Private Shared Function SetForegroundWindowApi(hWnd As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="GetForegroundWindow")>
    Private Shared Function GetForegroundWindowApi() As IntPtr
    End Function

    <DllImport("user32.dll", EntryPoint:="PostMessageW", CharSet:=CharSet.Unicode)>
    Private Shared Function PostMessageApi(hWnd As IntPtr, msg As Integer, wParam As IntPtr, lParam As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="MoveWindow", SetLastError:=True)>
    Private Shared Function MoveWindowApi(hWnd As IntPtr, x As Integer, y As Integer, nWidth As Integer, nHeight As Integer, bRepaint As Boolean) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="OpenClipboard", SetLastError:=True)>
    Private Shared Function OpenClipboardApi(hWndNewOwner As IntPtr) As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="CloseClipboard")>
    Private Shared Function CloseClipboardApi() As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="EmptyClipboard")>
    Private Shared Function EmptyClipboardApi() As Boolean
    End Function

    <DllImport("user32.dll", EntryPoint:="GetClipboardData")>
    Private Shared Function GetClipboardDataApi(uFormat As UInteger) As IntPtr
    End Function

    <DllImport("user32.dll", EntryPoint:="SetClipboardData")>
    Private Shared Function SetClipboardDataApi(uFormat As UInteger, hMem As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", EntryPoint:="GlobalAlloc")>
    Private Shared Function GlobalAllocApi(uFlags As UInteger, dwBytes As UInteger) As IntPtr
    End Function

    <DllImport("kernel32.dll", EntryPoint:="GlobalLock")>
    Private Shared Function GlobalLockApi(hMem As IntPtr) As IntPtr
    End Function

    <DllImport("kernel32.dll", EntryPoint:="GlobalUnlock")>
    Private Shared Function GlobalUnlockApi(hMem As IntPtr) As Boolean
    End Function

    <DllImport("kernel32.dll", EntryPoint:="GlobalFree")>
    Private Shared Function GlobalFreeApi(hMem As IntPtr) As IntPtr
    End Function

#End Region

End Class
