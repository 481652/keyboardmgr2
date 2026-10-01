
Imports System.Runtime.InteropServices
Imports System.Text
Public Class Selector
    Public Class Class1
        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function EnumWindows(ByVal lpEnumFunc As EnumWindowsProc, ByVal lParam As IntPtr) As Boolean
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function GetWindowText(ByVal hWnd As IntPtr, ByVal lpString As StringBuilder, ByVal nMaxCount As Integer) As Integer
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function GetWindowTextLength(ByVal hWnd As IntPtr) As Integer
        End Function

        <DllImport("user32.dll", SetLastError:=True)>
        Private Shared Function IsWindowVisible(ByVal hWnd As IntPtr) As Boolean
        End Function

        Private Delegate Function EnumWindowsProc(ByVal hWnd As IntPtr, ByVal lParam As IntPtr) As Boolean

        Public Shared Function GetOpenWindows() As Dictionary(Of IntPtr, String)
            Dim windows As New Dictionary(Of IntPtr, String)()
            EnumWindows(Function(hWnd, lParam)
                            If IsWindowVisible(hWnd) Then
                                Dim length As Integer = GetWindowTextLength(hWnd)
                                If length > 0 Then
                                    Dim builder As New StringBuilder(length + 1)
                                    GetWindowText(hWnd, builder, builder.Capacity)
                                    windows(hWnd) = builder.ToString()
                                End If
                            End If
                            Return True
                        End Function, IntPtr.Zero)
            Return windows
        End Function
    End Class

End Class
