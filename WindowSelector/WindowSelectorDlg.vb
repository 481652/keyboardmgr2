
Imports WindowSelector.Selector

'用于 ListBox 展示的条目：标题显示给用户，句柄用于快速取值
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

Public Class WindowSelectorDlg

    Public Property SelectedWindowHwnd As IntPtr '这个属性用于返回用户选择的窗体句柄

    Private Sub Form1_Load(sender As Object, e As EventArgs) Handles MyBase.Load
        '仅枚举一次，将 (句柄, 标题) 保存到 ListBox 条目中
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
End Class