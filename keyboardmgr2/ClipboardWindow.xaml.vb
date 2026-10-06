Public Class ClipboardWindow
    Const FoldedHeight As Double = 50
    Const ExpandHeight As Double = 450
    Private Sub Button_Click(sender As Object, e As RoutedEventArgs)

    End Sub

    Public Sub New()
        InitializeComponent()
        WindowStyle = WindowStyle.None
        AllowsTransparency = False
        Left = (SystemParameters.WorkArea.Width - Width) / 2
        WindowStartupLocation = WindowStartupLocation.Manual
    End Sub

    Private Sub Window_MouseEnter(sender As Object, e As MouseEventArgs)
        Height = ExpandHeight
    End Sub

    Private Sub Window_MouseLeave(sender As Object, e As MouseEventArgs)
        Height = FoldedHeight
    End Sub

    Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs)
        Height = FoldedHeight
    End Sub
End Class
