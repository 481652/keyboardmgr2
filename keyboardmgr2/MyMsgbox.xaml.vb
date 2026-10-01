Imports System.Timers
Imports System.Web.UI
Imports System.Windows.Media.Animation
Imports System.Windows.Threading

Public Class MyMsgbox
    Inherits Window

    Public timer As New DispatcherTimer()
    Dim duration As Integer = 3000
    Dim timer1 As New System.Timers.Timer(duration)
    Public Sub ShowMsg()

        Show()
        AddHandler timer1.Elapsed, AddressOf Timer1_Elapsed
        timer1.Interval = duration
        timer1.AutoReset = False
        timer1.Enabled = True
        timer1.Start()

    End Sub

    Private Sub Window_Loaded(sender As Object, e As RoutedEventArgs)
        InitializeComponent()
        Dim storyboard As Storyboard = CType(FindResource("PopupOpenAnimation"), Storyboard)
        storyboard.Begin(Me)
    End Sub
    Private Sub Timer1_Elapsed(sender As Object, e As ElapsedEventArgs)
        Windows.Application.Current.Dispatcher.Invoke(Sub()
                                                  Close()
                                              End Sub)
    End Sub
End Class

