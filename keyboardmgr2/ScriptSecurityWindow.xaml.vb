'脚本安全提醒窗口：列出脚本将执行的操作，用户确认后才允许导入/运行。
Imports System.Linq

Public Class ScriptSecurityWindow
    Inherits Window

    Public Property Allowed As Boolean = False
    Public Property TrustRequested As Boolean = False

    Public Sub New(result As ScriptAnalysisResult, sourcePath As String, hash As String)
        InitializeComponent()
        Dim lines As New List(Of String)
        If result.SyntaxErrors.Count > 0 Then
            HeaderText.Text = "脚本存在语法错误，无法运行："
            For Each item As String In result.SyntaxErrors.Take(8)
                lines.Add("× " & item)
            Next
            BtnAllow.IsEnabled = False
            BtnAllow.Content = "无法运行"
        Else
            For Each capability As ScriptCapability In result.Capabilities
                Dim commands As String = ""
                If capability.Commands.Count > 0 Then
                    commands = "（" & String.Join("、", capability.Commands.Take(12)) & If(capability.Commands.Count > 12, " 等", "") & "）"
                End If
                lines.Add("• " & capability.Name & "：" & capability.Description & commands)
            Next
            If lines.Count = 0 Then lines.Add("• 未检测到明显的敏感操作（脚本可能仅包含基本逻辑）")
        End If
        CapabilityList.ItemsSource = lines
        SourceText.Text = "来源：" & If(String.IsNullOrWhiteSpace(sourcePath), "(未保存的脚本)", sourcePath)
        HashText.Text = "SHA256：" & If(hash, "")
    End Sub

    Private Sub BtnAllow_Click(sender As Object, e As RoutedEventArgs)
        Allowed = True
        TrustRequested = ChkTrust.IsChecked.GetValueOrDefault(False)
        Close()
    End Sub

    Private Sub BtnCancel_Click(sender As Object, e As RoutedEventArgs)
        Close()
    End Sub
End Class
