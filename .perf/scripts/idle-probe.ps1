# 空闲/稳定性探针：托盘静默运行 + 主窗口打开 两种场景
param(
    [string]$Exe = 'D:\文档\Visual Studio\Projects\keyboardmgr2_\keyboardmgr2\bin\Release\keyboardmgr2.exe',
    [int]$TraySeconds = 240,
    [int]$WindowSeconds = 60,
    [string]$OutJson = 'D:\文档\Visual Studio\Projects\keyboardmgr2_\.perf\data\idle.json'
)
$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class IdleProbe
{
    delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowTextW(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern uint GetGuiResources(IntPtr hProcess, uint flags);
    [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }

    public static IntPtr FindMainWindow(uint pid)
    {
        IntPtr best = IntPtr.Zero; long bestArea = -1;
        EnumWindows((h, l) =>
        {
            uint p; GetWindowThreadProcessId(h, out p);
            if (p != pid) return true;
            RECT r; if (!GetWindowRect(h, out r)) return true;
            long area = (long)(r.R - r.L) * (r.B - r.T);
            var t = new StringBuilder(512); GetWindowTextW(h, t, 512);
            if (area > bestArea && t.ToString().Contains("键鼠管家")) { bestArea = area; best = h; }
            return true;
        }, IntPtr.Zero);
        return best;
    }

    public static uint[] GetGui(uint pid)
    {
        IntPtr h = OpenProcess(0x0400, false, pid);
        if (h == IntPtr.Zero) return new uint[] { 0, 0 };
        uint g = GetGuiResources(h, 0); uint u = GetGuiResources(h, 1);
        CloseHandle(h);
        return new uint[] { g, u };
    }

    // 返回 0 = 成功并给出耗时；-1 = 超时/无响应
    public static double PingUi(IntPtr hwnd, uint timeoutMs)
    {
        if (hwnd == IntPtr.Zero) return -1;
        IntPtr result;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        IntPtr ok = SendMessageTimeout(hwnd, 0, IntPtr.Zero, IntPtr.Zero, 0x0002 /*SMTO_ABORTIFHUNG*/, timeoutMs, out result);
        sw.Stop();
        if (ok == IntPtr.Zero) return -1;
        return sw.Elapsed.TotalMilliseconds;
    }
}
'@

$exeDir = Split-Path -Parent $Exe
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutJson) | Out-Null

function Measure-Scenario([string]$mode, [int]$seconds, [int[]]$pingAt) {
    Get-Process keyboardmgr2 -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
    $startArgs = @{ FilePath = $Exe; WorkingDirectory = $exeDir; PassThru = $true }
    if ($mode -eq 'tray') { $startArgs['ArgumentList'] = '--autostart' }
    $p = Start-Process @startArgs

    # 等待 CPU 静止（启动完成）
    $prev = 0; $quiet = 0
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 300
        $p.Refresh()
        if ($p.HasExited) { throw "process exited during startup" }
        $cpu = $p.TotalProcessorTime.TotalMilliseconds
        if ($i -gt 0 -and ($cpu - $prev) -lt 10) { $quiet++ } else { $quiet = 0 }
        $prev = $cpu
        if ($quiet -ge 2) { break }
    }
    $hwnd = [IdleProbe]::FindMainWindow([uint32]$p.Id)
    $t0 = Get-Date
    $baseCpu = $p.TotalProcessorTime.TotalMilliseconds
    $samples = @()
    for ($t = 0; $t -le $seconds; $t += 5) {
        Start-Sleep -Milliseconds 5000
        $p.Refresh()
        if ($p.HasExited) { break }
        $gui = [IdleProbe]::GetGui([uint32]$p.Id)
        $ping = -1
        if ($t -in $pingAt) { $ping = [math]::Round([IdleProbe]::PingUi($hwnd, 500), 1) }
        $samples += [pscustomobject]@{
            t         = $t + 5
            cpuMs     = [math]::Round($p.TotalProcessorTime.TotalMilliseconds, 1)
            wsMB      = [math]::Round($p.WorkingSet64 / 1MB, 2)
            privMB    = [math]::Round($p.PrivateMemorySize64 / 1MB, 2)
            handles   = $p.HandleCount
            threads   = $p.Threads.Count
            gdi       = $gui[0]
            user      = $gui[1]
            uiPingMs  = $ping
        }
        Write-Host ("  {0} t={1}s cpu={2}ms ws={3}MB priv={4}MB h={5} th={6} gdi={7} user={8} ping={9}" -f $mode, ($t + 5), $samples[-1].cpuMs, $samples[-1].wsMB, $samples[-1].privMB, $samples[-1].handles, $samples[-1].threads, $samples[-1].gdi, $samples[-1].user, $samples[-1].uiPingMs)
    }
    $endCpu = $samples[-1].cpuMs
    $cpu60 = $null
    $s60 = $samples | Where-Object { $_.t -ge 55 -and $_.t -le 65 } | Select-Object -First 1
    if ($s60) { $cpu60 = [math]::Round(($s60.cpuMs - $baseCpu) / 60000 * 100, 3) }
    $try = $p.Kill(); $p.WaitForExit(5000) | Out-Null
    Start-Sleep -Milliseconds 300

    $first = $samples[0]; $last = $samples[-1]
    return [pscustomobject]@{
        mode            = $mode
        pid             = $p.Id
        startedAt       = $t0.ToString('s')
        samples         = $samples
        idleCpuPct60s   = $cpu60
        totalCpuMs      = [math]::Round($endCpu - $baseCpu, 1)
        wallSeconds     = $seconds
        avgCpuPct       = [math]::Round(($endCpu - $baseCpu) / ($seconds * 1000) * 100, 3)
        wsStartMB       = $first.wsMB
        wsEndMB         = $last.wsMB
        wsDriftMB       = [math]::Round($last.wsMB - $first.wsMB, 2)
        privStartMB     = $first.privMB
        privEndMB       = $last.privMB
        privDriftMB     = [math]::Round($last.privMB - $first.privMB, 2)
        handleStart     = $first.handles
        handleEnd       = $last.handles
        handleDrift     = $last.handles - $first.handles
        threadStart     = $first.threads
        threadEnd       = $last.threads
        gdiEnd          = $last.gdi
        userEnd         = $last.user
        uiPingMaxMs     = ($samples | Where-Object { $_.uiPingMs -ge 0 } | Measure-Object uiPingMs -Maximum).Maximum
        uiPingTimeouts  = ($samples | Where-Object { $_.uiPingMs -lt 0 }).Count
    }
}

# 记录采样，供后台收集
$all = @()
$all += Measure-Scenario 'tray' $TraySeconds @(5, 60, 120, 180, 240)
$all += Measure-Scenario 'window' $WindowSeconds @(5, 30, 60)
$all | ConvertTo-Json -Depth 6 | Out-File -FilePath $OutJson -Encoding utf8
Write-Host "saved: $OutJson"
