'处理对话框及悬浮窗的相关功能

Module DlgModule
#Region "DialogsAndMessages"
    Public Sub ShowExpdlg(ex As String, text As String) 'ex为提示信息，text为异常内容（可空）
        Dim frm As New expWindow()
        frm.TextBlock1.Inlines.Add("错误信息：" & vbNewLine)
        frm.TextBlock1.Inlines.Add(ex & vbNewLine)
        frm.TextBlock1.Inlines.Add("系统名称：" & My.Computer.Info.OSFullName & vbNewLine)
        frm.TextBlock1.Inlines.Add("系统版本：" & My.Computer.Info.OSVersion & vbNewLine)
        '判断x86还是64
        If Environment.GetEnvironmentVariable("ProgramFiles(x86)") = "" Then
            frm.TextBlock1.Inlines.Add("系统平台：x86" & vbNewLine)
        Else
            frm.TextBlock1.Inlines.Add("系统平台：x64" & vbNewLine)
        End If
        If text <> "" Then
            frm.TextBlock1.Inlines.Add("以下是异常内容：" & vbCrLf)
            frm.TextBlock1.Inlines.Add(text)
        End If
        frm.ShowDialog()
    End Sub
    Public Sub ShowMyMessage(message As String)
        Dim myMsgbox As New MyMsgbox
        myMsgbox.messageText.Text = message
        myMsgbox.ShowMsg()
    End Sub
    Public Sub ShowHelp(helpTexts As List(Of String), helpTheme As String)
        '这里在helptexts中以行为单位存放了所有的帮助信息
        Dim helpWin As New HelpWindow()
        helpWin.HelpTheme.Content = helpTheme
        helpWin.HelpText.Text = ""
        For Each line In helpTexts
            helpWin.HelpText.Inlines.Add(line & vbNewLine)
        Next
        helpWin.Show()
    End Sub
#End Region



End Module
