' ============================================================================
' 数据通路微基准桥：C# 基准程序通过它调用真实模块
'   KbmrCodec.vb / MacroModel.vb / RecordingStore.vb / KeyNameMapper.vb /
'   SettingsModule.vb 均为原始源码直接编译，未经改写。
' ============================================================================

Imports System.IO

Public Class DataBridge

    Public Shared ReadOnly RegistryPath As String = "Software\LCS\keyboardmgr"

    ' 构造一份“贴近真实录制分布”的合成录制：
    '   70% MouseMove（125Hz 采样、±12px 随机游走，1920x1080）
    '   12% KeyDown/KeyUp 成对（A-Z）
    '   12% MouseDown/MouseUp 成对（左/右键）
    '    6% Wheel（±120）
    ' 时间戳单调不减，事件类型与 KbmrCodec 的写入分支一一对应。
    Public Shared Function MakeRecording(eventCount As Integer, seed As Integer) As KbmrRecording
        Dim rng As New Random(seed)
        Dim rec As New KbmrRecording()
        rec.Name = "bench-" & eventCount.ToString(Globalization.CultureInfo.InvariantCulture)
        rec.SampleIntervalMs = 8
        rec.ScreenWidth = 1920
        rec.ScreenHeight = 1080
        rec.CreatedUtc = New DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        Dim events As New List(Of KbmrEvent)(eventCount)
        Dim offset As Long = 0
        Dim x As Integer = 960
        Dim y As Integer = 540
        Do While events.Count < eventCount
            offset += rng.Next(1, 12)
            Dim roll As Integer = rng.Next(100)
            If roll < 70 Then
                x = Math.Max(0, Math.Min(1919, x + rng.Next(-12, 13)))
                y = Math.Max(0, Math.Min(1079, y + rng.Next(-12, 13)))
                events.Add(New KbmrEvent With {.OffsetMs = offset, .Kind = KbmrEventKind.MouseMove, .X = x, .Y = y})
            ElseIf roll < 82 Then
                Dim vk As UShort = CUShort(65 + rng.Next(26))
                events.Add(New KbmrEvent With {.OffsetMs = offset, .Kind = KbmrEventKind.KeyDown, .Vk = vk})
                If events.Count < eventCount Then
                    offset += rng.Next(1, 6)
                    events.Add(New KbmrEvent With {.OffsetMs = offset, .Kind = KbmrEventKind.KeyUp, .Vk = vk})
                End If
            ElseIf roll < 94 Then
                Dim button As Byte = CByte(If(rng.Next(2) = 0, 1, 2))
                events.Add(New KbmrEvent With {.OffsetMs = offset, .Kind = KbmrEventKind.MouseDown, .Button = button, .X = x, .Y = y})
                If events.Count < eventCount Then
                    offset += rng.Next(20, 120)
                    events.Add(New KbmrEvent With {.OffsetMs = offset, .Kind = KbmrEventKind.MouseUp, .Button = button, .X = x, .Y = y})
                End If
            Else
                events.Add(New KbmrEvent With {.OffsetMs = offset, .Kind = KbmrEventKind.Wheel, .Delta = If(rng.Next(2) = 0, 120, -120)})
            End If
        Loop
        rec.Events = events
        rec.DurationMs = events(events.Count - 1).OffsetMs
        Return rec
    End Function

    Public Shared Function Serialize(rec As KbmrRecording) As Byte()
        Return KbmrCodec.Serialize(rec)
    End Function

    Public Shared Function Deserialize(data As Byte()) As KbmrRecording
        Return KbmrCodec.Deserialize(data)
    End Function

    ' 调用真实 RecordingStore.Save；rec.FilePath 预置后直接写到指定临时路径（不再走
    ' %LOCALAPPDATA%\keyboardmgr2\recordings 的唯一名生成逻辑，但写盘与序列化路径相同）。
    Public Shared Function SaveRecording(rec As KbmrRecording, path As String) As Boolean
        rec.FilePath = path
        Return RecordingStore.Save(rec)
    End Function

    ' 等价于 RecordingStore.LoadAll 内部对单个文件做的事：File.ReadAllBytes + KbmrCodec.Deserialize。
    ' （LoadAll 的目录固定为 %LOCALAPPDATA%\keyboardmgr2\recordings，基准不写入用户真实录制目录，
    '   故此函数只测单文件加载路径，见报告说明。）
    Public Shared Function LoadFile(path As String) As KbmrRecording
        Return KbmrCodec.Deserialize(File.ReadAllBytes(path))
    End Function

    Public Shared Function LoadMetadata(path As String) As KbmrRecording
        Return KbmrCodec.ReadMetadata(path)
    End Function

    Public Shared Function LoadEvents(rec As KbmrRecording) As Boolean
        Return RecordingStore.LoadEvents(rec)
    End Function


    Public Shared Function ParseKeyName(name As String) As UShort
        Return KeyNameMapper.ParseKeyName(name)
    End Function

    Public Shared Function FormatKeyName(vk As UShort) As String
        Return KeyNameMapper.FormatKeyName(vk)
    End Function

    Public Shared Function SanitizeName(name As String) As String
        Return RecordingStore.SanitizeName(name)
    End Function

    Public Shared Sub WriteSetting(key As String, value As Object)
        SettingsModule.WriteSetting(key, value)
    End Sub

    Public Shared Function ReadSetting(key As String, defaultValue As Object) As Object
        Return SettingsModule.ReadSetting(key, defaultValue)
    End Function

    Public Shared Sub DeleteSetting(key As String)
        SettingsModule.DeleteSetting(key)
    End Sub

    Public Shared Function ShimErrorCount() As Integer
        Return DlgModule.errorCount
    End Function

End Class
