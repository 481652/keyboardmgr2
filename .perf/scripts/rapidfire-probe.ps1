# 连发(列表快速发送)运行时性能探针：
# 通过真实热键 Ctrl+F2 触发程序自身的连发流程，向测试目标窗口发送 N 条文本，
# 同时测量：UI 线程响应延迟（SendMessageTimeout）、每条例目实际间隔、总耗时、CPU、停止热键响应延迟。
param(
    [string]$Exe = 'D:\文档\Visual Studio\Projects\keyboardmgr2_\keyboardmgr2\bin\Release\keyboardmgr2.exe',
    [string]$TargetExe = 'D:\文档\Visual Studio\Projects\keyboardmgr2_\.perf\scripts\PerfTarget.exe',
    [string]$WorkDir = 'D:\文档\Visual Studio\Projects\keyboardmgr2_\.perf',
    [string]$OutJson = 'D:\文档\Visual Studio\Projects\keyboardmgr2_\.perf\data\rapidfire.json'
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class Rfp
{
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr wp, IntPtr lp, uint flags, uint timeout, out IntPtr result);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int L, T, R, B; }

    public static string[] ListWindows(uint pid)
    {
        var list = new List<string>();
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p == pid)
            {
                var t = new StringBuilder(512); GetWindowTextW(h, t, 512);
                var c = new StringBuilder(256); GetClassNameW(h, c, 256);
                list.Add(h.ToInt64() + "|" + (IsWindowVisible(h) ? "1" : "0") + "|" + c + "|" + t);
            }
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    public static IntPtr FindWindow(uint pid, string titlePart, bool visibleOnly)
    {
        IntPtr best = IntPtr.Zero;
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            if (visibleOnly && !IsWindowVisible(h)) return true;
            var t = new StringBuilder(512); GetWindowTextW(h, t, 512);
            if (t.ToString().Contains(titlePart)) { best = h; return false; }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    public static int[] GetRect(IntPtr h)
    {
        RECT r;
        if (!GetWindowRect(h, out r)) return new int[] { 0, 0, 0, 0 };
        return new int[] { r.L, r.T, r.R, r.B };
    }

    public static double PingUi(IntPtr h, uint timeoutMs)
    {
        if (h == IntPtr.Zero) return -1;
        IntPtr result;
        var sw = Stopwatch.StartNew();
        IntPtr ok = SendMessageTimeout(h, 0, IntPtr.Zero, IntPtr.Zero, 0x0002, timeoutMs, out result);
        sw.Stop();
        if (ok == IntPtr.Zero) return -1;
        return sw.Elapsed.TotalMilliseconds;
    }

    public static void ClickAt(int x, int y)
    {
        SetCursorPos(x, y);
        System.Threading.Thread.Sleep(30);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // LEFTDOWN
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // LEFTUP
    }

    public static void HotkeyCtrlF2()
    {
        const byte VK_CONTROL = 0x11, VK_F2 = 0x71;
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(15);
        keybd_event(VK_F2, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(15);
        keybd_event(VK_F2, 0, 0x0002, UIntPtr.Zero);
        System.Threading.Thread.Sleep(15);
        keybd_event(VK_CONTROL, 0, 0x0002, UIntPtr.Zero);
    }

    public static void SendCtrlG()
    {
        const byte VK_CONTROL = 0x11, VK_G = 0x47;
        keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(15);
        keybd_event(VK_G, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(15);
        keybd_event(VK_G, 0, 0x0002, UIntPtr.Zero);
        System.Threading.Thread.Sleep(15);
        keybd_event(VK_CONTROL, 0, 0x0002, UIntPtr.Zero);
    }
}
'@

function Get-QpcMs { [math]::Round(([Diagnostics.Stopwatch]::GetTimestamp() / [Diagnostics.Stopwatch]::Frequency) * 1000.0, 3) }

function New-Preset([string]$path, [int]$count, [int]$intervalMs) {
    $utf8 = New-Object Text.UTF8Encoding($false, $true)
    $payload = New-Object IO.MemoryStream
    $w = New-Object IO.BinaryWriter($payload, $utf8, $true)
    $w.Write([int]$intervalMs)
    $w.Write([bool]$false)
    $w.Write([int]$count)
    for ($i = 1; $i -le $count; $i++) {
        $bytes = $utf8.GetBytes(("perf-item-{0:D4}" -f $i))
        $w.Write([int]$bytes.Length)
        $w.Write($bytes)
    }
    $w.Flush()
    $raw = $payload.ToArray()
    $w.Dispose(); $payload.Dispose()

    $cs = New-Object IO.MemoryStream
    $ds = New-Object IO.Compression.DeflateStream($cs, [IO.Compression.CompressionLevel]::Optimal, $true)
    $ds.Write($raw, 0, $raw.Length); $ds.Dispose()
    $compressed = $cs.ToArray(); $cs.Dispose()

    $sha = [Security.Cryptography.SHA256]::Create()
    $hash = $sha.ComputeHash($compressed)
    $sha.Dispose()

    $out = New-Object IO.MemoryStream
    $bw = New-Object IO.BinaryWriter($out)
    $bw.Write($utf8.GetBytes('LCSLST2'))
    $bw.Write([UInt16]1)
    $bw.Write([int]$compressed.Length)
    $bw.Write($hash)
    $bw.Write($compressed)
    $bw.Flush()
    [IO.File]::WriteAllBytes($path, $out.ToArray())
    $bw.Dispose(); $out.Dispose()
    Write-Host "preset: $path ($count items, ${intervalMs}ms)"
}

function Stop-AllTargets {
    Get-Process keyboardmgr2, PerfTarget -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Read-EnterTimes([string]$logPath) {
    $times = @()
    if (Test-Path $logPath) {
        foreach ($line in (Get-Content $logPath -ErrorAction SilentlyContinue)) {
            if ($line -like 'ENTER*') {
                $parts = $line -split "`t"
                $times += [double]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture)
            }
        }
    }
    return $times
}

function Test-MessageBoxes([int]$procId) {
    return @([Rfp]::ListWindows([uint32]$procId) | Where-Object { ($_ -split '\|')[3] -match '错误|失败|无法|提示' })
}

$results = @()

function Measure-Scenario([string]$name, [int]$items, [int]$intervalMs, [int]$stopAfterEnters) {
    Stop-AllTargets
    Start-Sleep -Milliseconds 500

    $dir = Join-Path $WorkDir 'data'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $presetPath = Join-Path $dir ("rf-preset-{0}.lcslst2" -f $items)
    New-Preset $presetPath $items $intervalMs
    $targetLog = Join-Path $dir ("rf-target-{0}.log" -f $name)
    Remove-Item $targetLog -ErrorAction SilentlyContinue

    $target = Start-Process -FilePath $TargetExe -ArgumentList "`"$targetLog`"" -PassThru
    $ready = $null
    $deadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $targetLog) {
            $readyLines = @(Get-Content $targetLog -ErrorAction SilentlyContinue | Where-Object { $_ -like 'READY*' })
            if ($readyLines.Count -gt 0) { $ready = $readyLines[0]; break }
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $ready) { throw "target window not ready" }
    $targetHwnd = [IntPtr]([int64](($ready -split "`t")[1]))
    Write-Host "[$name] target ready hwnd=$targetHwnd pid=$($target.Id)"

    $app = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path $Exe) -ArgumentList "`"$presetPath`"" -PassThru
    $appHwnd = [IntPtr]::Zero
    $deadline = (Get-Date).AddSeconds(20)
    while ((Get-Date) -lt $deadline) {
        $appHwnd = [Rfp]::FindWindow([uint32]$app.Id, '', $false)
        if ($appHwnd -eq [IntPtr]::Zero) { $appHwnd = [Rfp]::FindWindow([uint32]$app.Id, '键鼠管家', $false) }
        if ($appHwnd -ne [IntPtr]::Zero -and [Rfp]::PingUi($appHwnd, 300) -ge 0) { break }
        Start-Sleep -Milliseconds 100
    }
    if ($appHwnd -eq [IntPtr]::Zero) { throw "app main window not found" }
    Start-Sleep -Milliseconds 1500
    Write-Host "[$name] app ready hwnd=$appHwnd pid=$($app.Id)"

    # 激活目标窗口并点击其输入框
    $rect = [Rfp]::GetRect($targetHwnd)
    $cx = [int](($rect[0] + $rect[2]) / 2); $cy = [int](($rect[1] + $rect[3]) / 2)
    $focused = $false
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        [void][Rfp]::SetForegroundWindow($targetHwnd)
        [Rfp]::ClickAt($cx, $cy)
        Start-Sleep -Milliseconds 250
        if ([Rfp]::GetForegroundWindow() -eq $targetHwnd) { $focused = $true; break }
    }
    if (-not $focused) { throw "cannot focus target window" }
    Start-Sleep -Milliseconds 300

    $baselinePing = [Rfp]::PingUi($appHwnd, 200)
    $cpuStart = $app.TotalProcessorTime.TotalMilliseconds
    $enterCount0 = (Read-EnterTimes $targetLog).Count
    $tHotkey = Get-QpcMs
    [Rfp]::HotkeyCtrlF2()

    $pings = @()
    $timeouts = 0
    $wsSamples = @()
    $privSamples = @()
    $lastEnterCount = $enterCount0
    $lastEnterQpc = $null
    $tStopHotkey = $null
    $tFirstEnter = $null
    $deadline = (Get-Date).AddSeconds(120)
    $finished = $false
    while ((Get-Date) -lt $deadline) {
        $ping = [Rfp]::PingUi($appHwnd, 300)
        $pings += $ping
        if ($ping -lt 0) { $timeouts++ }
        try { $app.Refresh(); $wsSamples += [math]::Round($app.WorkingSet64 / 1MB, 1); $privSamples += [math]::Round($app.PrivateMemorySize64 / 1MB, 1) } catch { }

        $times = Read-EnterTimes $targetLog
        if ($times.Count -gt $lastEnterCount) {
            if ($null -eq $tFirstEnter) { $tFirstEnter = $times[0] }
            $lastEnterCount = $times.Count
            $lastEnterQpc = $times[-1]
        }

        $msg = Test-MessageBoxes $app.Id
        if ($msg.Count -gt 0) { Write-Host "[$name] message box detected: $($msg -join ' ; ')"; break }

        if ($stopAfterEnters -gt 0) {
            if ($null -eq $tStopHotkey -and $lastEnterCount -ge ($enterCount0 + $stopAfterEnters)) {
                $tStopHotkey = Get-QpcMs
                [Rfp]::HotkeyCtrlF2()
                Write-Host "[$name] stop hotkey sent at enter count=$lastEnterCount"
            }
            if ($null -ne $tStopHotkey) {
                # 停止后 1.2s 内没有新 Enter 则认为已停
                if (((Get-QpcMs) - $lastEnterQpc) -gt 1200) { $finished = $true; break }
            }
        } else {
            if ($lastEnterCount -ge ($enterCount0 + $items)) { $finished = $true; break }
        }
        Start-Sleep -Milliseconds 25
    }

    $cpuEnd = $app.TotalProcessorTime.TotalMilliseconds
    Start-Sleep -Milliseconds 3000
    $app.Refresh()
    $cpuEnd = $app.TotalProcessorTime.TotalMilliseconds
    $ws = [math]::Round($app.WorkingSet64 / 1MB, 1)
    $privAfter = [math]::Round($app.PrivateMemorySize64 / 1MB, 1)
    $wsDuringMax = if ($wsSamples.Count) { ($wsSamples | Measure-Object -Maximum).Maximum } else { $null }
    $privDuringMax = if ($privSamples.Count) { ($privSamples | Measure-Object -Maximum).Maximum } else { $null }
    $handles = $app.HandleCount
    $threads = $app.Threads.Count
    $appWindows = [Rfp]::ListWindows([uint32]$app.Id)

    Start-Sleep -Milliseconds 200
    Stop-AllTargets
    Start-Sleep -Milliseconds 400

    $times = Read-EnterTimes $targetLog
    $relTimes = @()
    foreach ($t in $times) { $relTimes += ($t - $tHotkey) }
    $intervals = @()
    for ($i = 1; $i -lt $relTimes.Count; $i++) { $intervals += ($relTimes[$i] - $relTimes[$i - 1]) }

    $sorted = @($intervals | Sort-Object)
    $avg = $null; $sd = $null
    if ($intervals.Count -gt 0) {
        $sum = 0.0
        foreach ($v in $intervals) { $sum += $v }
        $avg = $sum / $intervals.Count
        $sumSq = 0.0
        foreach ($v in $intervals) { $sumSq += ($v - $avg) * ($v - $avg) }
        $sd = [math]::Sqrt($sumSq / $intervals.Count)
    }
    $stat = [pscustomobject]@{
        count       = $intervals.Count
        mean        = if ($null -ne $avg) { [math]::Round($avg, 2) } else { $null }
        sd          = if ($null -ne $sd) { [math]::Round($sd, 2) } else { $null }
        p50         = if ($sorted.Count) { [math]::Round($sorted[[int][math]::Floor($sorted.Count * 0.50)], 1) } else { $null }
        p95         = if ($sorted.Count) { [math]::Round($sorted[[math]::Min($sorted.Count - 1, [int][math]::Floor($sorted.Count * 0.95))], 1) } else { $null }
        max         = if ($sorted.Count) { [math]::Round($sorted[-1], 1) } else { $null }
        min         = if ($sorted.Count) { [math]::Round($sorted[0], 1) } else { $null }
    }
    $totalMs = if ($relTimes.Count) { [math]::Round($relTimes[-1], 1) } else { $null }
    $stopLatency = if ($null -ne $tStopHotkey -and $null -ne $lastEnterQpc) { [math]::Round(($lastEnterQpc - $tStopHotkey), 1) } else { $null }
    $pingValid = @($pings | Where-Object { $_ -ge 0 })
    $pingStats = [pscustomobject]@{
        samples = $pings.Count
        timeouts = $timeouts
        maxMs = if ($pingValid.Count) { [math]::Round(($pingValid | Measure-Object -Maximum).Maximum, 1) } else { $null }
        avgMs = if ($pingValid.Count) { [math]::Round((($pingValid | Measure-Object -Average).Average), 2) } else { $null }
        over20ms = @($pingValid | Where-Object { $_ -gt 20 }).Count
        over100ms = @($pingValid | Where-Object { $_ -gt 100 }).Count
    }

    $result = [pscustomobject]@{
        scenario      = $name
        itemsPlanned  = $items
        intervalMs    = $intervalMs
        stopAfter     = $stopAfterEnters
        firstEnterMs  = if ($relTimes.Count) { [math]::Round($relTimes[0], 1) } else { $null }
        totalMs       = $totalMs
        achievedAvgMs = $stat.mean
        intervalStats = $stat
        stopLatencyMs = $stopLatency
        cpuMsTotal    = [math]::Round($cpuEnd - $cpuStart, 1)
        cpuPctDuring  = if ($totalMs -and $totalMs -gt 0) { [math]::Round(($cpuEnd - $cpuStart) / $totalMs * 100, 1) } else { $null }
        workingSetMB  = $ws
        wsDuringMaxMB = $wsDuringMax
        privDuringMaxMB = $privDuringMax
        privAfter3sMB = $privAfter
        handles       = $handles
        threads       = $threads
        baselinePingMs = [math]::Round($baselinePing, 2)
        uiPing        = $pingStats
        appWindows    = ($appWindows -join ' ;; ')
        finished      = $finished
        sampleIntervals = @($intervals | Select-Object -First 200 | ForEach-Object { [math]::Round($_, 1) })
    }
    $script:results += $result
    Write-Host ("[{0}] enters={1} first={2}ms total={3}ms avgInterval={4}ms sd={5} p95={6} max={7} stopLatency={8}ms cpuPct={9}% pingMax={10}ms timeouts={11} finished={12}" -f $name, $relTimes.Count, $result.firstEnterMs, $result.totalMs, $stat.mean, $stat.sd, $stat.p95, $stat.max, $stopLatency, $result.cpuPctDuring, $pingStats.maxMs, $pingStats.timeouts, $finished)
}

Measure-Scenario 'throughput-100ms' 60 100 0
Measure-Scenario 'stop-latency' 400 100 6
Measure-Scenario 'throughput-50ms' 60 50 0

$results | ConvertTo-Json -Depth 6 | Out-File -FilePath $OutJson -Encoding utf8
Write-Host "saved: $OutJson"
