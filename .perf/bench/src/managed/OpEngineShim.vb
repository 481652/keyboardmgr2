' ============================================================================
' 仅供独立基准工程使用的 OpEngine 垫片（shim）。
' 真实 keyboardmgr2\Modules\OpEngine.vb 依赖 SettingsModule / OpCoreNative /
' WPF 消息框，无法脱离主工程独立编译；而 UserInputHandler.vb 只调用
' OpEngine.MouseClick 与 OpEngine.TypeText 两个成员。此处按真实 OpEngine 中
' 托管分支的实现 1:1 复刻这两个成员，其余 UserInputHandler / OpCoreManaged /
' GlobalMouseHook 均为原始源码直接编译（见 build.ps1 的源文件清单）。
' ============================================================================

Public Module OpEngine

    Public Function MouseClick(mouseButton As Integer, x As Integer, y As Integer) As Boolean
        If x >= 0 AndAlso y >= 0 Then
            SetCursorPosition(x, y)
        End If
        SendMouseButtonInput(mouseButton, True)
        SendMouseButtonInput(mouseButton, False)
        Return True
    End Function

    Public Function TypeText(text As String) As Boolean
        If String.IsNullOrEmpty(text) Then Return True
        SendUnicodeText(text)
        Return True
    End Function

End Module
