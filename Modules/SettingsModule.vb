'存储、修改、读取及删除应用程序设置。
Imports Microsoft.Win32

Module SettingsModule
    Private Const RegistryPath As String = "Software\LCS\keyboardmgr"

    '写入设置
    Public Sub WriteSetting(key As String, value As Object)
        Try
            Using regKey As RegistryKey = Registry.CurrentUser.CreateSubKey(RegistryPath)
                regKey.SetValue(key, value)
            End Using
        Catch ex As Exception
            ShowExpdlg("错误3：程序在写入注册表设置时出现异常，可能是无权限或杀软误拦！", ex.Message & vbLf & ex.StackTrace)
        End Try
    End Sub

    '读取设置
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
            ShowExpdlg("错误4：程序在读取注册表设置时出现异常，可能是无权限或杀软误拦！", ex.Message & vbLf & ex.StackTrace)
            Return defaultValue
        End Try
    End Function

    '删除设置
    Public Sub DeleteSetting(key As String)
        Try
            Using regKey As RegistryKey = Registry.CurrentUser.OpenSubKey(RegistryPath, True)
                regKey?.DeleteValue(key, False)
            End Using
        Catch ex As Exception
            ShowExpdlg("错误5：程序在删除注册表设置时出现异常，可能是无权限或杀软误拦！", ex.Message & vbLf & ex.StackTrace)
        End Try
    End Sub

    '删除所有设置
    Public Sub DeleteAllSettings()
        Try
            Using regKey As RegistryKey = Registry.CurrentUser.OpenSubKey("Software", True)
                regKey?.DeleteSubKeyTree("LCS\keyboardmgr", False)
            End Using
        Catch ex As Exception
            ShowExpdlg("错误5：程序在删除注册表设置时出现异常，可能是无权限或杀软误拦！", ex.Message & vbLf & ex.StackTrace)
        End Try
    End Sub

    '判断解析结果是否等价于无效/None
    '注意: 旧代码用 IsNot New List(Of Key) From {Key.None} 做引用比较，
    '       New List 每次都产生新引用，IsNot 恒为 True、Is 恒为 False，
    '       导致加载失败检测形同虚设。以下函数做值判断。
    Public Function IsNoneKeyList(keys As List(Of Key)) As Boolean
        If keys Is Nothing Then Return True
        If keys.Count = 0 Then Return True
        If keys.Count = 1 AndAlso keys(0) = Key.None Then Return True
        Return False
    End Function
        '加载设置中的按键
    Public Function LoadKeyData(KeysStr As String) As List(Of Key)
        Dim savedKeys As New List(Of Key)
        If KeysStr.Length > 0 Then
            For Each KeyStr In KeysStr.Split("+")
                If KeyStr = "Ctrl" Then
                    savedKeys.Add(Key.LeftCtrl)
                    Continue For
                ElseIf KeyStr = "Alt" Then
                    savedKeys.Add(Key.LeftAlt)
                    Continue For
                ElseIf KeyStr = "Shift" Then
                    savedKeys.Add(Key.LeftShift)
                    Continue For
                ElseIf KeyStr = "Win" Then
                    savedKeys.Add(Key.LWin)
                    Continue For
                ElseIf KeyStr = "Esc" Then
                    savedKeys.Add(Key.Escape)
                    Continue For
                End If
                savedKeys.Add([Enum].Parse(GetType(Key), KeyStr))
            Next
            Return savedKeys
        End If
        savedKeys.Add(Key.None)
        Return savedKeys
    End Function

End Module
