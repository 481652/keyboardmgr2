'脚本安全：基于 PowerShell AST 的静态能力分析 + 文件哈希信任列表。
'导入或运行未信任脚本时先列出其将执行的操作，用户确认后可记住文件哈希，文件修改后需重新确认。
Imports System.IO
Imports System.Linq
Imports System.Management.Automation.Language
Imports System.Security.Cryptography
Imports System.Text
Imports Microsoft.Win32

Public Class ScriptCapability
    Public Property Name As String
    Public Property Description As String
    Public Property Commands As New List(Of String)
End Class

Public Class ScriptAnalysisResult
    Public Property Capabilities As New List(Of ScriptCapability)
    Public Property SyntaxErrors As New List(Of String)
End Class

Public Module ScriptSecurity
    Private Const TrustRegistryPath As String = "Software\LCS\keyboardmgr\TrustedScripts"

    Private ReadOnly capabilityOrder As String() = {
        "阻止用户输入", "模拟键鼠输入", "屏幕读取", "窗口枚举与控制", "剪贴板",
        "文件系统", "网络访问", "进程与系统控制", "注册表操作", "动态执行/代码注入", ".NET 互操作"
    }

    Private ReadOnly commandCategories As New Dictionary(Of String, String)(StringComparer.OrdinalIgnoreCase)
    Private ReadOnly benignCommands As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)

    Sub New()
        For Each name As String In {"Move", "Click", "DoubleClick", "MouseDown", "MouseUp", "Wheel", "Drag", "KeyDown", "KeyUp", "KeyTap", "Combo", "TypeText"}
            commandCategories(name) = "模拟键鼠输入"
        Next
        commandCategories("BlockInput") = "阻止用户输入"
        For Each name As String In {"Get-PixelColor", "Get-CursorColor", "Save-ScreenRegion", "Get-CursorPos"}
            commandCategories(name) = "屏幕读取"
        Next
        For Each name As String In {"Get-Windows", "Set-ForegroundWindow", "Get-ForegroundWindow", "Get-WindowRect", "Close-Window", "Move-Window"}
            commandCategories(name) = "窗口枚举与控制"
        Next
        For Each name As String In {"Set-ClipboardText", "Get-ClipboardText", "Set-Clipboard", "Get-Clipboard"}
            commandCategories(name) = "剪贴板"
        Next
        For Each name As String In {"Out-File", "Set-Content", "Add-Content", "Clear-Content", "Remove-Item", "New-Item", "Copy-Item", "Move-Item", "Rename-Item", "Get-Content", "Get-ChildItem", "Import-Csv", "Export-Csv", "Test-Path", "Resolve-Path", "Split-Path", "Join-Path", "Start-Transcript", "Stop-Transcript", "Get-FileHash", "Compress-Archive", "Expand-Archive", "Select-String", "Set-Location", "Push-Location", "Pop-Location"}
            commandCategories(name) = "文件系统"
        Next
        For Each name As String In {"Invoke-WebRequest", "Invoke-RestMethod", "iwr", "irm", "Start-BitsTransfer", "Test-NetConnection", "Test-Connection", "Resolve-DnsName", "curl", "wget", "ftp"}
            commandCategories(name) = "网络访问"
        Next
        For Each name As String In {"Start-Process", "Stop-Process", "Get-Process", "Wait-Process", "Debug-Process", "New-Service", "Start-Service", "Stop-Service", "Restart-Service", "Set-Service", "schtasks", "shutdown", "Restart-Computer", "Stop-Computer", "cmd", "powershell", "pwsh", "wscript", "cscript", "mshta", "rundll32", "regsvr32", "sc", "net", "taskkill", "tasklist", "Set-ExecutionPolicy", "Start-Job", "Register-ScheduledTask", "New-ScheduledTask", "Unregister-ScheduledTask", "Get-ScheduledTask", "New-LocalUser", "Add-LocalGroupMember", "New-NetFirewallRule"}
            commandCategories(name) = "进程与系统控制"
        Next
        For Each name As String In {"reg", "regedit", "Get-ItemProperty", "Set-ItemProperty", "Remove-ItemProperty", "New-ItemProperty", "New-PSDrive", "Remove-PSDrive"}
            commandCategories(name) = "注册表操作"
        Next
        For Each name As String In {"Invoke-Expression", "iex", "Add-Type", "Invoke-Command", "Invoke-ScriptBlock", "New-Object"}
            commandCategories(name) = "动态执行/代码注入"
        Next
        For Each name As String In {"Write-Host", "Write-Output", "Write-Verbose", "Write-Warning", "Write-Error", "Write-Debug", "Write-Information", "Write-Progress", "Start-Sleep", "Get-Date", "ForEach-Object", "Where-Object", "Select-Object", "Sort-Object", "Measure-Object", "Group-Object", "Compare-Object", "Out-Null", "Out-String", "Out-Host", "Format-Table", "Format-List", "Format-Wide", "Get-Random", "ConvertTo-Json", "ConvertFrom-Json", "Get-Member", "Clear-Host", "Tee-Object", "Get-Variable", "Set-Variable", "Remove-Variable", "Get-Command", "Get-Help", "Set-StrictMode", "Delay", "Sleep"}
            benignCommands.Add(name)
        Next
    End Sub

    Public Function Analyze(code As String) As ScriptAnalysisResult
        Dim result As New ScriptAnalysisResult()
        If String.IsNullOrWhiteSpace(code) Then Return result

        Dim tokens() As Token = Nothing
        Dim errors() As ParseError = Nothing
        Dim ast As ScriptBlockAst = Nothing
        Try
            ast = Parser.ParseInput(code, tokens, errors)
        Catch ex As Exception
            result.SyntaxErrors.Add("解析脚本时出错：" & ex.Message)
            Return result
        End Try
        If errors IsNot Nothing Then
            For Each item As ParseError In errors
                result.SyntaxErrors.Add("第 " & item.Extent.StartLineNumber & " 行：" & item.Message)
            Next
        End If

        Dim capabilityMap As New Dictionary(Of String, List(Of String))(StringComparer.OrdinalIgnoreCase)
        Dim otherCommands As New List(Of String)
        If ast IsNot Nothing Then
            For Each node As Ast In ast.FindAll(Function(item As Ast) TypeOf item Is CommandAst, True)
                Dim command As CommandAst = DirectCast(node, CommandAst)
                Dim name As String = GetCommandName(command)
                If Not String.IsNullOrEmpty(name) Then
                    Dim category As String = Nothing
                    If commandCategories.TryGetValue(name, category) Then
                        AddCommand(capabilityMap, category, name)
                    ElseIf Not benignCommands.Contains(name) Then
                        If Not otherCommands.Any(Function(item As String) String.Equals(item, name, StringComparison.OrdinalIgnoreCase)) Then otherCommands.Add(name)
                    End If
                End If
                If command.InvocationOperator = TokenKind.Dot OrElse command.InvocationOperator = TokenKind.Ampersand Then
                    AddCommand(capabilityMap, "动态执行/代码注入", If(command.InvocationOperator = TokenKind.Ampersand, "& 调用运算符", ". 点调用/点源"))
                End If
            Next
            Dim hasNetInterop As Boolean = False
            For Each node As Ast In ast.FindAll(Function(item As Ast) TypeOf item Is InvokeMemberExpressionAst, True)
                Dim invoke As InvokeMemberExpressionAst = DirectCast(node, InvokeMemberExpressionAst)
                Dim targetText As String = invoke.Expression.Extent.Text
                Dim memberName As String = GetInvokeMemberName(invoke)
                If String.Equals(targetText, "$KM", StringComparison.OrdinalIgnoreCase) Then
                    If Not String.IsNullOrEmpty(memberName) AndAlso Not benignCommands.Contains(memberName) Then
                        Dim category As String = Nothing
                        If commandCategories.TryGetValue(memberName, category) Then
                            AddCommand(capabilityMap, category, memberName)
                        ElseIf Not otherCommands.Any(Function(item As String) String.Equals(item, "$KM." & memberName, StringComparison.OrdinalIgnoreCase)) Then
                            otherCommands.Add("$KM." & memberName)
                        End If
                    End If
                Else
                    hasNetInterop = True
                End If
            Next
            For Each node As Ast In ast.FindAll(Function(item As Ast) TypeOf item Is TypeExpressionAst, True)
                Dim typeName As TypeName = DirectCast(node, TypeExpressionAst).TypeName
                If typeName IsNot Nothing AndAlso typeName.FullName.Contains(".") Then
                    hasNetInterop = True
                End If
            Next
            If hasNetInterop Then
                AddCommand(capabilityMap, ".NET 互操作", "类型或方法调用")
            End If
        End If

        For Each categoryName As String In capabilityOrder
            Dim commands As List(Of String) = Nothing
            If capabilityMap.TryGetValue(categoryName, commands) AndAlso commands.Count > 0 Then
                Dim capability As New ScriptCapability With {.Name = categoryName, .Description = GetDescription(categoryName)}
                capability.Commands.AddRange(commands)
                result.Capabilities.Add(capability)
            End If
        Next
        If otherCommands.Count > 0 Then
            Dim capability As New ScriptCapability With {.Name = "其他命令", .Description = "脚本包含无法自动识别的命令，请自行确认其行为"}
            capability.Commands.AddRange(otherCommands)
            result.Capabilities.Add(capability)
        End If
        Return result
    End Function

    Private Function GetCommandName(command As CommandAst) As String
        Dim name As String = command.GetCommandName()
        If Not String.IsNullOrEmpty(name) Then Return name
        If command.CommandElements.Count >= 2 Then
            Dim first As CommandElementAst = command.CommandElements(0)
            Dim second As CommandElementAst = command.CommandElements(1)
            If TypeOf first Is VariableExpressionAst AndAlso TypeOf second Is StringConstantExpressionAst Then
                Return DirectCast(second, StringConstantExpressionAst).Value
            End If
        End If
        Return Nothing
    End Function

    Private Function GetInvokeMemberName(invoke As InvokeMemberExpressionAst) As String
        Dim member As StringConstantExpressionAst = TryCast(invoke.Member, StringConstantExpressionAst)
        If member IsNot Nothing Then Return member.Value
        Return invoke.Member.Extent.Text
    End Function

    Private Sub AddCommand(capabilityMap As Dictionary(Of String, List(Of String)), category As String, command As String)
        Dim commands As List(Of String) = Nothing
        If Not capabilityMap.TryGetValue(category, commands) Then
            commands = New List(Of String)()
            capabilityMap(category) = commands
        End If
        If Not commands.Any(Function(item As String) String.Equals(item, command, StringComparison.OrdinalIgnoreCase)) Then commands.Add(command)
    End Sub

    Private Function GetDescription(name As String) As String
        Select Case name
            Case "阻止用户输入"
                Return "脚本可以冻结键盘和鼠标输入"
            Case "模拟键鼠输入"
                Return "脚本可以模拟键盘按键和鼠标点击"
            Case "屏幕读取"
                Return "脚本可以读取屏幕像素或保存屏幕截图"
            Case "窗口枚举与控制"
                Return "脚本可以枚举、激活、移动或关闭窗口"
            Case "剪贴板"
                Return "脚本可以读写系统剪贴板"
            Case "文件系统"
                Return "脚本可以读取、写入或删除文件"
            Case "网络访问"
                Return "脚本可以访问网络，可能下载或上传数据"
            Case "进程与系统控制"
                Return "脚本可以启动或结束进程、修改系统设置"
            Case "注册表操作"
                Return "脚本可以读写系统注册表"
            Case "动态执行/代码注入"
                Return "脚本可以动态执行代码或加载程序集，可能隐藏真实行为"
            Case ".NET 互操作"
                Return "脚本可以直接调用 .NET 类型与方法（可访问文件、网络等）"
            Case Else
                Return ""
        End Select
    End Function

    Public Function ComputeTextHash(code As String) As String
        Return ComputeHash(Encoding.UTF8.GetBytes(If(code, String.Empty)))
    End Function

    Public Function ComputeFileHash(path As String) As String
        Return ComputeHash(File.ReadAllBytes(path))
    End Function

    Private Function ComputeHash(bytes As Byte()) As String
        Using sha256 As SHA256 = SHA256.Create()
            Dim hash As Byte() = sha256.ComputeHash(bytes)
            Dim builder As New StringBuilder(hash.Length * 2)
            For Each item As Byte In hash
                builder.Append(item.ToString("x2"))
            Next
            Return builder.ToString()
        End Using
    End Function

    Public Function IsTrusted(hash As String) As Boolean
        If String.IsNullOrEmpty(hash) Then Return False
        Try
            Using key As RegistryKey = Registry.CurrentUser.OpenSubKey(TrustRegistryPath)
                Return key IsNot Nothing AndAlso key.GetValue(hash) IsNot Nothing
            End Using
        Catch
            Return False
        End Try
    End Function

    Public Sub TrustScript(hash As String, path As String)
        If String.IsNullOrEmpty(hash) Then Return
        Try
            Using key As RegistryKey = Registry.CurrentUser.CreateSubKey(TrustRegistryPath)
                key.SetValue(hash, If(path, String.Empty) & "|" & DateTime.UtcNow.ToString("o"))
            End Using
        Catch ex As Exception
            ShowMyMessage("无法保存脚本信任记录：" & ex.Message)
        End Try
    End Sub

    Public Sub ClearTrustedScripts()
        Try
            Registry.CurrentUser.DeleteSubKeyTree(TrustRegistryPath, False)
        Catch
        End Try
    End Sub
End Module
