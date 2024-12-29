'帮助与信息显示器
Public Class HelpWindow
    Private Sub ToggleButton_Click(sender As Object, e As RoutedEventArgs)
        If FontButton.IsChecked = True Then
            HelpText.FontSize = 15
        Else
            HelpText.FontSize = 12
        End If
    End Sub
End Class
