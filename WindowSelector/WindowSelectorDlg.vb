
Imports WindowSelector.Selector

Public Class WindowSelectorDlg

    Public Property SelectedWindowHwnd As IntPtr '这个属性用于返回用户选择的窗体句柄

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        Dim windows = Class1.GetOpenWindows()
        For Each window In windows
            ListBox1.Items.Add(New WindowItem(window.Key, window.Value))
        Next
    End Sub

    Private Sub ListBox1_SelectedIndexChanged(sender As Object, e As EventArgs) Handles ListBox1.SelectedIndexChanged
        Dim selectedItem As WindowItem = TryCast(ListBox1.SelectedItem, WindowItem)
        If selectedItem IsNot Nothing Then
            Label2.Text = "选取的窗体句柄：" & selectedItem.Hwnd.ToString()
            SelectedWindowHwnd = selectedItem.Hwnd
            Button1.Visible = True
        End If
    End Sub

    Private Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
#Disable Warning BC42025
        DialogResult = System.Windows.Forms.DialogResult.OK
#Enable Warning BC42025
        Close()
    End Sub

    Private Sub Button2_Click(sender As Object, e As EventArgs) Handles Button2.Click
        ListBox1.Items.Clear()
        Dim windows = Class1.GetOpenWindows()
        For Each window In windows
            ListBox1.Items.Add(New WindowItem(window.Key, window.Value))
        Next
    End Sub
End Class


Public Class WindowItem
    Public ReadOnly Property Hwnd As IntPtr
    Public ReadOnly Property Title As String
    Public Sub New(hwnd As IntPtr, title As String)
        Me.Hwnd = hwnd
        Me.Title = title
    End Sub
    Public Overrides Function ToString() As String
        Return Title
    End Function
End Class