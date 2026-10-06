'自己借助winforms实现了一套托盘图标的功能

Imports System.Windows.Forms
Imports ContextMenu = System.Windows.Controls.ContextMenu
Imports MenuItem = System.Windows.Controls.MenuItem

Public Class MyTrayicon
    Implements IDisposable
    Private notifyIcon As NotifyIcon

    Public Sub New()
        CreateTrayIcon()
    End Sub

    Public Sub Dispose() Implements IDisposable.Dispose
        If notifyIcon IsNot Nothing Then
            notifyIcon.Visible = False
            notifyIcon.Dispose()
            notifyIcon = Nothing
        End If
    End Sub

    Private Sub CreateTrayIcon()
        Dim resourceUri As New Uri("pack://application:,,,/appiconnew.ico")
        Dim iconStream = Windows.Application.GetResourceStream(resourceUri).Stream

        notifyIcon = New NotifyIcon With {
            .Icon = New System.Drawing.Icon(iconStream),
            .Visible = True,
            .Text = "键鼠管家"
        }
        AddHandler notifyIcon.Click, AddressOf NotifyIcon_Click
        AddHandler notifyIcon.MouseUp, AddressOf NotifyIcon_MouseUp
    End Sub

    Private isRightClick As Boolean = False  '用于标记是否右键点击

    Private Sub NotifyIcon_Click(sender As Object, e As EventArgs)
        If Not isRightClick Then
            Windows.Application.Current.Dispatcher.Invoke(Sub() ShowMainWindow())
        End If
        isRightClick = False '恢复标记
    End Sub

    Private Sub NotifyIcon_MouseUp(sender As Object, e As MouseEventArgs)
        If e.Button = MouseButtons.Right Then
            isRightClick = True  '设置为右键点击标记
            Windows.Application.Current.Dispatcher.Invoke(Sub() ShowContextMenu())
        End If
    End Sub

    Private Sub ShowContextMenu()
        Dim resourceDictionary As New ResourceDictionary With {
           .Source = New Uri("pack://application:,,,/keyboardmgr2;Component/resource/" & If(isDarkTheme, "DarkTheme.xaml", "LightTheme.xaml"), UriKind.Absolute)
       }
        Dim menuStyle As Style = resourceDictionary("MenuStyle")
        Dim menuItemStyle As Style = resourceDictionary("MenuItemStyle")
        Dim contextMenu As New ContextMenu With {
        .Style = menuStyle}
        Dim menuItem1 As New MenuItem() With {.Header = "显示主窗体"}
        AddHandler menuItem1.Click, AddressOf MenuOption1_Click
        Dim menuItem2 As New MenuItem() With {.Header = "连点"}
        AddHandler menuItem2.Click, AddressOf MenuOption2_Click
        Dim menuItem3 As New MenuItem() With {.Header = "连发"}
        AddHandler menuItem3.Click, AddressOf MenuOption3_Click
        Dim menuItem4 As New MenuItem() With {.Header = "摸鱼"}
        AddHandler menuItem4.Click, AddressOf MenuOption4_Click
        Dim menuItemRecord As New MenuItem() With {.Header = "录制"}
        AddHandler menuItemRecord.Click, AddressOf MenuOptionRecord_Click
        If MainWindow1.Instance.isExperimentalFeatureEnabled Then
            Dim menuItemScript As New MenuItem() With {.Header = "脚本"}
            AddHandler menuItemScript.Click, AddressOf MenuOptionScript_Click
            contextMenu.Items.Add(menuItemScript)
        End If
        Dim menuItem5 As New MenuItem() With {.Header = "选项"}
        AddHandler menuItem5.Click, AddressOf MenuOption5_Click
        Dim menuItem6 As New MenuItem() With {.Header = "退出程序"}
        AddHandler menuItem6.Click, AddressOf MenuOption6_Click
        contextMenu.Items.Add(menuItem1)
        contextMenu.Items.Add(menuItem2)
        contextMenu.Items.Add(menuItem3)
        contextMenu.Items.Add(menuItem4)
        contextMenu.Items.Add(menuItemRecord)
        contextMenu.Items.Add(menuItem5)
        contextMenu.Items.Add(menuItem6)
        '必须在添加菜单项之后再遍历应用样式，否则集合为空、样式不生效
        For Each item As MenuItem In contextMenu.Items
            item.Style = menuItemStyle
        Next
        contextMenu.IsOpen = True
    End Sub


    Private Sub ShowMainWindow()
        MainWindow1.Instance.ShowInTaskbar = True
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Activate()
    End Sub

    Private Sub MenuOption1_Click(sender As Object, e As RoutedEventArgs) '显示主窗体
        ShowMainWindow()
    End Sub

    Private Sub MenuOption2_Click(sender As Object, e As RoutedEventArgs) '连点
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Activate()
        MainWindow1.Instance.TabControl1.SelectedIndex = 1
    End Sub

    Private Sub MenuOption3_Click(sender As Object, e As RoutedEventArgs) '连发
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Activate()
        MainWindow1.Instance.TabControl1.SelectedIndex = 2
    End Sub

    Private Sub MenuOption4_Click(sender As Object, e As RoutedEventArgs) '摸鱼
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Activate()
        MainWindow1.Instance.TabControl1.SelectedIndex = 3
    End Sub

    Private Sub MenuOption5_Click(sender As Object, e As RoutedEventArgs) '选项
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Activate()
        MainWindow1.Instance.TabControl1.SelectedIndex = 6
    End Sub

    Private Sub MenuOptionRecord_Click(sender As Object, e As RoutedEventArgs) '录制
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Activate()
        MainWindow1.Instance.TabControl1.SelectedIndex = 4
    End Sub

    Private Sub MenuOptionScript_Click(sender As Object, e As RoutedEventArgs) '脚本
        If MainWindow1.Instance.Visibility = Visibility.Hidden Then
            MainWindow1.Instance.Visibility = Visibility.Visible
        End If
        MainWindow1.Instance.Activate()
        MainWindow1.Instance.TabControl1.SelectedIndex = 5
    End Sub

    Private Sub MenuOption6_Click(sender As Object, e As RoutedEventArgs) '退出程序
        FloatingWindow.Instance.StopApp()
    End Sub
End Class
