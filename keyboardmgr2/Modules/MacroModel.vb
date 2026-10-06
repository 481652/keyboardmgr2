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
End Class

Public Class KbmrRecording
    Public Property Name As String
    Public Property SampleIntervalMs As Integer
    Public Property ScreenWidth As Integer
    Public Property ScreenHeight As Integer
    Public Property DurationMs As Long
    Public Property CreatedUtc As DateTime
    Public Property Events As New List(Of KbmrEvent)
End Class
