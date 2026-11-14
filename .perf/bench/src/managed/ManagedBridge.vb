' ============================================================================
' 托管内核基准桥：C# 基准程序通过它调用真实 OpCoreManaged 模块
' （OpCoreManaged.vb 为原始源码直接编译，未经改写）。
' ============================================================================

Public Class ManagedKernelBridge

    Public Shared Sub StartMouseClick(intervalMs As Integer, randomSpeedOffset As Boolean, randomPosOffset As Boolean, baseX As Integer, baseY As Integer, mouseButton As Integer)
        OpCoreManaged.StartMouseClick(intervalMs, randomSpeedOffset, randomPosOffset, baseX, baseY, mouseButton)
    End Sub

    Public Shared Sub StartKeyClick(intervalMs As Integer, keys As UShort())
        OpCoreManaged.StartKeyClick(intervalMs, keys)
    End Sub

    Public Shared Sub StopAll()
        OpCoreManaged.StopAll()
    End Sub

    Public Shared Function IsRunning() As Boolean
        Return OpCoreManaged.IsRunning()
    End Function

    Public Shared Function KeyTap(vk As UShort) As Boolean
        Return OpCoreManaged.KeyTap(vk)
    End Function

End Class
