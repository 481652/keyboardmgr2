Imports System.IO
Imports System.Linq
Imports System.Security.Principal
Imports System.Text

Public Class LaunchOnSetupModule
    Public Const ElevatedArgument As String = "--elevated"
    Public Const TaskActionArgumentPrefix As String = "--task="

    Private Const TaskName As String = "keyboardmgr2"
    Private Const AutoStartArgument As String = "--autostart"

    Private Const LogonDelaySeconds As Integer = 15
    '是否注册过
    Private Const SignatureSettingKey As String = "AutoStartTaskSignature"

    '提权重启的新实例需要执行的任务操作："register" / "delete" / Nothing
    Public Shared Property PendingTaskAction As String = Nothing

    Public Shared Function IsRunningAsAdministrator() As Boolean
        Try
            Return New WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)
        Catch
            Return False
        End Try
    End Function

    Public Shared Function CurrentExePath() As String
        Return Process.GetCurrentProcess().MainModule.FileName
    End Function

    '任务是否注册过？
    Public Shared Function TaskExists() As Boolean
        Return Not String.IsNullOrEmpty(TryCast(ReadSetting(SignatureSettingKey, ""), String))
    End Function

    '注册开机自启任务（PowerShell 在线程池里跑，不阻塞 UI 线程）
    Public Shared Async Sub RegisterTask(Optional showResult As Boolean = True)
        Dim exePath As String = CurrentExePath()
        Try
            Dim result As PowerShellResult = Await RunPowerShellAsync(BuildRegisterScript(exePath))

            If result.ExitCode = 0 AndAlso result.StdOut.Trim().StartsWith("OK", StringComparison.OrdinalIgnoreCase) Then
                WriteSetting(SignatureSettingKey, exePath)
                If showResult Then
                    MessageBox.Show("已注册开机自启任务。若要关闭，请在任务计划程序库根目录中删除 keyboardmgr2。或取消勾选开机自启动选项")
                End If
            Else
                MessageBox.Show("注册开机自启任务失败，调试信息：" & vbCrLf &
                                "exitCode: " & result.ExitCode & vbCrLf &
                                "stdout: " & result.StdOut.Trim() & vbCrLf &
                                "stderr: " & result.StdErr.Trim())
            End If
        Catch ex As Exception
            MessageBox.Show("程序在注册开机自启动任务时出现错误：" & ex.Message)
        End Try
    End Sub

    Public Shared Async Sub EnsureTaskRegistered()
        Dim taskMode As Integer = 0
        Integer.TryParse(Convert.ToString(ReadSetting("DoRegisterAutoStartTask", 0)), taskMode)
        If taskMode <> 1 Then Return
        If Not String.IsNullOrEmpty(PendingTaskAction) Then Return

        ' 检查任务是否正常，以及是否注册过，如果注册过就不提权
        If IsRunningAsAdministrator() AndAlso HasAutoStartArgument() Then
            Dim status As String = Await GetTaskStatusAsync()
            '状态未知（例如 PowerShell 被组策略/杀软拦截）时不重写任务，避免每次登录都报错
            If status = TaskStatusOk OrElse status = TaskStatusUnknown Then Return
            RegisterTask(showResult:=False)
            Return
        End If


        If TryCast(ReadSetting(SignatureSettingKey, ""), String) = CurrentExePath() Then Return

        If IsRunningAsAdministrator() Then
            RegisterTask(showResult:=False)
            Return
        End If

        If MessageBox.Show("检测到开机自启任务尚未注册（或程序路径已变化）。" & vbCrLf & vbCrLf &
                           "是否请求管理员权限并注册？", "keyboardmgr2",
                           MessageBoxButton.YesNo, MessageBoxImage.Question) = MessageBoxResult.Yes Then
            RestartToRequestAdmin("register")
        End If
    End Sub

    Private Shared Function HasAutoStartArgument() As Boolean
        Return Environment.GetCommandLineArgs().Skip(1).Any(
            Function(argument) String.Equals(argument, AutoStartArgument, StringComparison.OrdinalIgnoreCase))
    End Function

    Public Shared Sub RestartToRequestAdmin(Optional taskAction As String = Nothing)
        If IsRunningAsAdministrator() Then
            Return
        End If

        Dim info As New ProcessStartInfo()
        info.UseShellExecute = True
        '程序由任务计划拉起时当前目录是 System32，必须显式指定程序所在目录
        info.WorkingDirectory = Path.GetDirectoryName(CurrentExePath())
        info.FileName = CurrentExePath()
        info.Verb = "runas" ' 请求提权
        info.Arguments = ElevatedArgument
        If Not String.IsNullOrEmpty(taskAction) Then
            info.Arguments &= " " & TaskActionArgumentPrefix & taskAction
        End If

        Try
            Process.Start(info)
            Application.ReleaseSingleInstanceForRestart()
            Application.Current.Shutdown()
        Catch ex As Exception
            MessageBox.Show("请求管理员权限失败：" & ex.Message)
        End Try
    End Sub

    Public Shared Async Sub EraseTask(Optional showResult As Boolean = True)
        Try
            Dim result As PowerShellResult = Await RunPowerShellAsync(BuildEraseScript())
            Dim text As String = result.StdOut.Trim()

            If result.ExitCode = 0 AndAlso text.StartsWith("DELETED", StringComparison.OrdinalIgnoreCase) Then
                DeleteSetting(SignatureSettingKey)
                If showResult Then MessageBox.Show("已删除开机启动任务。")
            ElseIf text.StartsWith("MISSING", StringComparison.OrdinalIgnoreCase) Then
                DeleteSetting(SignatureSettingKey)
                If showResult Then MessageBox.Show("任务不存在，无需删除。")
            ElseIf text.StartsWith("UNKNOWN", StringComparison.OrdinalIgnoreCase) Then
                MessageBox.Show("无法读取开机自启任务（权限不足?），未删除。")
            Else
                MessageBox.Show("删除失败，错误信息： " & result.ExitCode & vbCrLf & text & vbCrLf & result.StdErr.Trim())
            End If
        Catch ex As Exception
            MessageBox.Show("程序在删除开机自启动任务时出现错误：" & ex.Message)
        End Try
    End Sub

    Private Const TaskStatusOk As String = "OK"
    Private Const TaskStatusMissing As String = "MISSING"
    Private Const TaskStatusStale As String = "STALE"
    Private Const TaskStatusUnknown As String = "UNKNOWN"

    Private Shared Async Function GetTaskStatusAsync() As Task(Of String)
        Try
            Dim result As PowerShellResult = Await RunPowerShellAsync(BuildStatusScript(CurrentExePath()))
            Dim text As String = result.StdOut.Trim()
            If result.ExitCode <> 0 OrElse text.Length = 0 Then Return TaskStatusUnknown

            If text.StartsWith("OK", StringComparison.OrdinalIgnoreCase) Then Return TaskStatusOk
            If text.StartsWith("MISSING", StringComparison.OrdinalIgnoreCase) Then Return TaskStatusMissing
            If text.StartsWith("STALE", StringComparison.OrdinalIgnoreCase) Then Return TaskStatusStale
            Return TaskStatusUnknown
        Catch
            Return TaskStatusUnknown
        End Try
    End Function

    Private Shared Function BuildRegisterScript(exePath As String) As String
        Dim sb As New StringBuilder()
        sb.AppendLine("[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)")
        sb.AppendLine("$ProgressPreference = 'SilentlyContinue'")
        sb.AppendLine("$ErrorActionPreference = 'Stop'")
        sb.AppendLine("try {")
        sb.AppendLine("  $name = " & PowerShellQuote(TaskName))
        sb.AppendLine("  $exe = " & PowerShellQuote(exePath))
        sb.AppendLine("  $user = " & PowerShellQuote(WindowsIdentity.GetCurrent().Name))
        sb.AppendLine("  $action = New-ScheduledTaskAction -Execute $exe -Argument '--autostart'")
        sb.AppendLine("  $trigger = New-ScheduledTaskTrigger -AtLogOn -User $user")
        sb.AppendLine("  $trigger.Delay = 'PT" & LogonDelaySeconds.ToString() & "S'")
        sb.AppendLine("  $principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest")
        sb.AppendLine("  $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -DontStopOnIdleEnd -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit ([TimeSpan]::Zero)")
        sb.AppendLine("  Register-ScheduledTask -TaskName $name -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null")
        sb.AppendLine("  'OK'")
        sb.AppendLine("} catch {")
        sb.AppendLine("  'FAIL: ' + $_.Exception.Message")
        sb.AppendLine("}")
        Return sb.ToString()
    End Function

    Private Shared Function BuildEraseScript() As String
        Dim sb As New StringBuilder()
        sb.AppendLine("[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)")
        sb.AppendLine("$ProgressPreference = 'SilentlyContinue'")
        sb.AppendLine("$name = " & PowerShellQuote(TaskName))
        sb.AppendLine("try { $t = Get-ScheduledTask -TaskName $name -ErrorAction Stop }")
        sb.AppendLine("catch { if ($_.CategoryInfo.Category -eq 'ObjectNotFound') { 'MISSING' } else { 'UNKNOWN' }; exit 0 }")
        sb.AppendLine("if ($null -eq $t) { 'MISSING'; exit 0 }")
        sb.AppendLine("try { Unregister-ScheduledTask -TaskName $name -Confirm:$false -ErrorAction Stop; 'DELETED' } catch { 'FAIL: ' + $_.Exception.Message }")
        Return sb.ToString()
    End Function

    Private Shared Function BuildStatusScript(exePath As String) As String
        Dim sb As New StringBuilder()
        sb.AppendLine("[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)")
        sb.AppendLine("$ProgressPreference = 'SilentlyContinue'")
        sb.AppendLine("$name = " & PowerShellQuote(TaskName))
        sb.AppendLine("$exe = " & PowerShellQuote(exePath))
        sb.AppendLine("try { $t = Get-ScheduledTask -TaskName $name -ErrorAction Stop }")
        sb.AppendLine("catch { if ($_.CategoryInfo.Category -eq 'ObjectNotFound') { 'MISSING' } else { 'UNKNOWN' }; exit 0 }")
        sb.AppendLine("if ($null -eq $t) { 'MISSING'; exit 0 }")
        sb.AppendLine("if (($t.Actions | ForEach-Object { $_.Execute }) -notcontains $exe) { 'STALE-EXE'; exit 0 }")
        sb.AppendLine("if ($t.Settings.DisallowStartIfOnBatteries -or $t.Settings.StopIfGoingOnBatteries) { 'STALE-BATTERY'; exit 0 }")
        sb.AppendLine("if (-not ($t.Triggers | Where-Object { $_.CimClass.CimClassName -eq 'MSFT_TaskLogonTrigger' })) { 'STALE-TRIGGER'; exit 0 }")
        sb.AppendLine("'OK'")
        Return sb.ToString()
    End Function

    Private Shared Function PowerShellQuote(value As String) As String
        Return "'" & value.Replace("'", "''") & "'"
    End Function
    Private Structure PowerShellResult
        Public ExitCode As Integer
        Public StdOut As String
        Public StdErr As String
    End Structure

    '异步执行 PowerShell 脚本，避免阻塞 UI 线程
    Private Shared Async Function RunPowerShellAsync(script As String) As Task(Of PowerShellResult)
        Return Await Threading.Tasks.Task.Run(Function() RunPowerShell(script))
    End Function

    Private Shared Function RunPowerShell(script As String) As PowerShellResult
        Dim result As New PowerShellResult With {.StdOut = String.Empty, .StdErr = String.Empty}
        Dim psi As New ProcessStartInfo()
        psi.FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell\v1.0\powershell.exe")
        psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " &
                        Convert.ToBase64String(Encoding.Unicode.GetBytes(script))
        psi.UseShellExecute = False
        psi.CreateNoWindow = True
        psi.RedirectStandardOutput = True
        psi.RedirectStandardError = True
        psi.StandardOutputEncoding = Encoding.UTF8
        psi.StandardErrorEncoding = Encoding.UTF8

        Using proc As Process = Process.Start(psi)
            Dim stdoutTask As Threading.Tasks.Task(Of String) = proc.StandardOutput.ReadToEndAsync()
            Dim stderrTask As Threading.Tasks.Task(Of String) = proc.StandardError.ReadToEndAsync()
            If Not proc.WaitForExit(30000) Then
                'kill脚本保主程序
                Try
                    proc.Kill()
                Catch
                End Try
                result.ExitCode = -1
                result.StdErr = "PowerShell 执行超时（30 秒）"
                Return result
            End If
            result.StdOut = stdoutTask.Result
            result.StdErr = stderrTask.Result
            result.ExitCode = proc.ExitCode
        End Using
        Return result
    End Function
End Class
