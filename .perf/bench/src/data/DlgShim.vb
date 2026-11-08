' ============================================================================
' 仅供独立基准工程使用的对话框垫片。
' 真实 keyboardmgr2\Modules\DlgModule.vb 中 ShowExpdlg 依赖 WPF 异常窗口，
' 无法脱离主工程编译。SettingsModule.vb 仅在异常路径调用 ShowExpdlg，
' 此处改为累计错误计数 + 最多打印 3 条到 stderr（避免 1000 次异常时刷屏）。
' SettingsModule.vb 本身为原始源码直接编译。
' ============================================================================

Module DlgModule

    Public errorCount As Integer = 0

    Public Sub ShowExpdlg(ex As String, text As String)
        errorCount += 1
        If errorCount <= 3 Then
            Console.Error.WriteLine("[DlgShim] " & ex & If(String.IsNullOrEmpty(text), "", " | " & text))
        End If
    End Sub

End Module
