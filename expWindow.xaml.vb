Imports System.Text
Imports System.Windows
Imports System.Windows.Controls.Primitives
Imports System.Windows.Media.Animation

Public Class expWindow
    Public Sub New()
        InitializeComponent()
    End Sub

    Private Sub Button_Click(sender As Object, e As RoutedEventArgs)
        Environment.Exit(1)
    End Sub

    Private Sub Window_Closing(sender As Object, e As ComponentModel.CancelEventArgs)
        Environment.Exit(1)
    End Sub
    Private Sub CopyButton_Click(sender As Object, e As RoutedEventArgs) '获取textblock全部内容和弹出Popup
        Clipboard.Clear()
        Dim allText As New StringBuilder()
        For Each inline As Inline In TextBlock1.Inlines
            If TypeOf inline Is Run Then
                allText.Append(CType(inline, Run).Text)
            End If
        Next
        Dim textBlockContent As String = allText.ToString()
        Clipboard.SetText(textBlockContent)
        MyPopup.RenderTransform = New ScaleTransform()
        MyPopup.IsOpen = True
        Dim popupOpenAnimation As Storyboard = CType(FindResource("PopupOpenAnimation"), Storyboard)
        popupOpenAnimation.Begin(MyPopup)
        CopyButton.Visibility = Visibility.Hidden '防止点按过多次引发剪贴板异常

    End Sub
End Class
