# 启动性能探针：外部计时（进程启动 -> 输入空闲 / 首个可见窗口）+ 内置 PerfProbe 时间线
param(
    [int]$Runs = 3,
    [string]$Exe = 'D:\文档\Visual Studio\Projects\keyboardmgr2_\keyboardmgr2\bin\Release\keyboardmgr2.exe',
    [string]$OutJson = 'D:\文档\Visual Studio\Projects\keyboardmgr2_\.perf\data\startup.json'
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WinProbe
{
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr hProcess, uint flags);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);

    public static string[] GetWindows(uint targetPid)
    {
        var list = new List<string>();
        EnumWindows((h, l) =>
        {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == targetPid)
            {
                var t = new StringBuilder(512); GetWindowTextW(h, t, 512);
                var c = new StringBuilder(256); GetClassNameW(h, c, 256);
                list.Add(h.ToInt64().ToString() + "|" + (IsWindowVisible(h) ? "1" : "0") + "|" + c.ToString() + "|" + t.ToString());
            }
            return true;
        }, IntPtr.Zero);
        return list.ToArray();
    }

    public static uint[] GetGui(uint pid)
    {
        IntPtr h = OpenProcess(0x0400, false, pid); // PROCESS_QUERY_INFORMATION
        if (h == IntPtr.Zero) return new uint[] { 0, 0 };
        uint gdi = GetGuiResources(h, 0);
        uint user = GetGuiResources(h, 1);
        CloseHandle(h);
        return new uint[] { gdi, user };
    }
}
'@

$exeDir = Split-Path -Parent $Exe
$probeLog = Join-Path $env:TEMP 'kbmperf\startup.log'
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutJson) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $probeLog) | Out-Null
Remove-Item $probeLog -ErrorAction SilentlyContinue

function Get-ProbeTimeline([int]$procId) {
    if (-not (Test-Path $probeLog)) { return @() }
    $rows = @()
    foreach ($line in (Get-Content $probeLog)) {
        $parts = $line -split "`t"
        if ($parts.Count -ge 3 -and $parts[0] -eq "$procId") {
            $rows += [pscustomobject]@{ ms = [double]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture); phase = $parts[2] }
        }
    }
    return $rows
}

$results = @()

foreach ($mode in @('normal', 'autostart')) {
    for ($i = 1; $i -le $Runs; $i++) {
        # 清理旧实例
        Get-Process keyboardmgr2 -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 400

        $argList = if ($mode -eq 'autostart') { @('--autostart') } else { @() }
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $startArgs = @{ FilePath = $Exe; WorkingDirectory = $exeDir; PassThru = $true }
        if ($argList.Count -gt 0) { $startArgs['ArgumentList'] = $argList }
        $p = Start-Process @startArgs

        $tInputIdle = $null
        $tVisible = $null
        $visibleTitles = @()

        # 输入空闲（消息循环就绪）
        try {
            $null = $p.WaitForInputIdle(15000)
            $tInputIdle = [math]::Round($sw.Elapsed.TotalMilliseconds, 1)
        } catch { }

        # 轮询首个可见窗口
        $deadline = 15000
        while ($sw.Elapsed.TotalMilliseconds -lt $deadline) {
            $wins = [WinProbe]::GetWindows([uint32]$p.Id)
            $vis = @($wins | Where-Object { ($_ -split '\|')[1] -eq '1' })
            if ($vis.Count -gt 0) {
                $tVisible = [math]::Round($sw.Elapsed.TotalMilliseconds, 1)
                $visibleTitles = @($vis | ForEach-Object { ($_ -split '\|')[3] }) | Where-Object { $_ -ne '' }
                break
            }
            Start-Sleep -Milliseconds 5
        }

        # 稳定窗口：连续采样工作集与 CPU，直到 CPU 静止或 6 秒
        $wsSamples = @()
        $cpuSamples = @()
        $tCpuQuiet = $null
        $prevCpu = $p.TotalProcessorTime.TotalMilliseconds
        for ($k = 0; $k -lt 20; $k++) {
            Start-Sleep -Milliseconds 300
            $p.Refresh()
            if ($p.HasExited) { break }
            $wsSamples += [math]::Round($p.WorkingSet64 / 1MB, 1)
            $cpuNow = $p.TotalProcessorTime.TotalMilliseconds
            $cpuSamples += $cpuNow
            if ($null -eq $tCpuQuiet -and $k -ge 1 -and ($cpuNow - $prevCpu) -lt 8) {
                $tCpuQuiet = [math]::Round($sw.Elapsed.TotalMilliseconds, 1)
            }
            $prevCpu = $cpuNow
        }
        $p.Refresh()
        $cpuMs = [math]::Round($p.TotalProcessorTime.TotalMilliseconds, 1)
        $ws = if ($wsSamples.Count -gt 0) { $wsSamples[-1] } else { 0 }
        $peakWs = if ($wsSamples.Count -gt 0) { ($wsSamples | Measure-Object -Maximum).Maximum } else { 0 }
        $priv = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
        $handles = $p.HandleCount
        $threads = $p.Threads.Count
        $gui = [WinProbe]::GetGui([uint32]$p.Id)

        $timeline = Get-ProbeTimeline $p.Id
        $allWins = [WinProbe]::GetWindows([uint32]$p.Id)

        try { $p.Kill(); $p.WaitForExit(5000) | Out-Null } catch { }

        $entry = [pscustomobject]@{
            mode          = $mode
            run           = $i
            pid           = $p.Id
            tInputIdleMs  = $tInputIdle
            tVisibleMs    = $tVisible
            tCpuQuietMs   = $tCpuQuiet
            cpuMsAtSettle = $cpuMs
            workingSetMB  = $ws
            privateMB     = $priv
            peakWsMB      = $peakWs
            handles       = $handles
            threads       = $threads
            gdi           = $gui[0]
            user          = $gui[1]
            wsSamples     = $wsSamples
            visibleTitles = ($visibleTitles -join ' / ')
            windows       = ($allWins -join ' ;; ')
            timeline      = $timeline
        }
        $results += $entry
        Write-Host ("[{0} #{1}] inputIdle={2}ms visible={3}ms cpuQuiet={4}ms CPU={5}ms WS={6}MB peak={7}MB priv={8}MB h={9} t={10} gdi={11} user={12} titles=[{13}]" -f $mode, $i, $tInputIdle, $tVisible, $tCpuQuiet, $cpuMs, $ws, $peakWs, $priv, $handles, $threads, $gui[0], $gui[1], ($visibleTitles -join '/'))
        Write-Host ("        timeline: " + (($timeline | ForEach-Object { "$($_.phase)=$($_.ms)" }) -join ' -> '))
        Start-Sleep -Milliseconds 300
    }
}

$results | ConvertTo-Json -Depth 6 | Out-File -FilePath $OutJson -Encoding utf8
Write-Host "saved: $OutJson"
