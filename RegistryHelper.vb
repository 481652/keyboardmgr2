'存储、修改、读取及删除应用程序设置。
Imports Microsoft.Win32

Module RegistryHelper
    Private Const RegistryPath As String = "Software\LCS\keyboardmgr"

    ' 写入设置
    Public Sub WriteSetting(key As String, value As Object)
        Try
            Using regKey As RegistryKey = Registry.CurrentUser.CreateSubKey(RegistryPath)
                regKey.SetValue(key, value)
            End Using
        Catch ex As Exception
            ShowExpdlg("错误3：程序在写入注册表设置时出现异常，可能是无权限或杀软误拦！", ex.Message)
        End Try
    End Sub
#Disable Warning BC42105
    ' 读取设置
    Public Function ReadSetting(key As String, defaultValue As Object) As Object
        Try
            Using regKey As RegistryKey = Registry.CurrentUser.OpenSubKey(RegistryPath)
                If regKey IsNot Nothing Then
                    Return regKey.GetValue(key, defaultValue)
                Else
                    Return defaultValue
                End If
            End Using
        Catch ex As Exception
            ShowExpdlg("错误4：程序在读取注册表设置时出现异常，可能是无权限或杀软误拦！", ex.Message)
        End Try
    End Function
#Enable Warning

    ' 删除设置
    Public Sub DeleteSetting(key As String)
        Try
            Using regKey As RegistryKey = Registry.CurrentUser.OpenSubKey(RegistryPath, True)
                regKey?.DeleteValue(key, False)
            End Using
        Catch ex As Exception
            ShowExpdlg("错误5：程序在删除注册表设置时出现异常，可能是无权限或杀软误拦！", ex.Message)
        End Try
    End Sub

    ' 删除所有设置
    Public Sub DeleteAllSettings()
        Try
            Using regKey As RegistryKey = Registry.CurrentUser.OpenSubKey("Software", True)
                regKey?.DeleteSubKeyTree("LCS\keyboardmgr", False)
            End Using
        Catch ex As Exception
            ShowExpdlg("错误5：程序在删除注册表设置时出现异常，可能是无权限或杀软误拦！", ex.Message)
        End Try
    End Sub

End Module
