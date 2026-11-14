'录制数据模型：.kbmr 录制事件与录制对象定义。
Public Enum KbmrEventKind As Byte
    KeyDown = 1
    KeyUp = 2
    MouseDown = 3
    MouseUp = 4
    MouseMove = 5
    Wheel = 6
End Enum

Public Class KbmrEvent
    Public Property OffsetMs As Long
    Public Property Kind As KbmrEventKind
    Public Property Vk As UShort
    Public Property Button As Byte
    Public Property X As Integer
    Public Property Y As Integer
    Public Property Delta As Integer
    '鼠标按键事件是否带有有效点击坐标（旧格式没有记录点击坐标时为 False）
    Public Property HasPosition As Boolean = True
End Class

Public Class KbmrRecording
    Private _events As List(Of KbmrEvent) = New List(Of KbmrEvent)()
    Private _eventCount As Integer
    Public Property EventCount As Integer
        Get
            Return If(EventsLoaded, _events.Count, _eventCount)
        End Get
        Set(value As Integer)
            _eventCount = Math.Max(0, value)
        End Set
    End Property
    Public Property EventsLoaded As Boolean = True
    Public Property Name As String
    '历史录制对应的磁盘文件路径（不参与序列化，仅用于自动保存/重命名/删除）。
    Public Property FilePath As String
    Public Property SampleIntervalMs As Integer
    '录制时主屏的像素尺寸（与鼠标事件坐标同一坐标空间）；0 表示旧格式未记录，不参与缩放
    Public Property ScreenWidth As Integer
    Public Property ScreenHeight As Integer
    '录制时的系统 DPI 缩放千分比（1000 = 100%），仅用于显示和旧格式换算
    Public Property DpiScalePermille As Integer = 1000
    Public Property DurationMs As Long
    Public Property CreatedUtc As DateTime
    Public Property Events As List(Of KbmrEvent)
        Get
            Return _events
        End Get
        Set(value As List(Of KbmrEvent))
            _events = If(value, New List(Of KbmrEvent)())
            _eventCount = _events.Count
            EventsLoaded = True
        End Set
    End Property

    Friend Sub SetMetadataOnly(eventCount As Integer)
        _eventCount = Math.Max(0, eventCount)
        _events = New List(Of KbmrEvent)()
        EventsLoaded = False
    End Sub

    '是否有可信的录制分辨率（旧格式文件没有记录，回放时不参与缩放）
    Public ReadOnly Property HasScreenSize As Boolean
        Get
            Return ScreenWidth > 0 AndAlso ScreenHeight > 0
        End Get
    End Property
End Class
