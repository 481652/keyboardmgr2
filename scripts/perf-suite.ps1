#requires -Version 5.1
<#
.SYNOPSIS
    键鼠管家自动化性能测试套件（一键快速回归）。

.DESCRIPTION
    自动完成：环境预检 → 构建计时 → 启动/空闲/连点内核/连发/数据通路/退出残留测量 →
    阈值判定 → 基线对比 → 生成 Markdown 报告 + JSON 结果，并返回可用于 CI 的退出码。

    默认“快速模式”约 2–4 分钟；“完整模式”约 8–12 分钟。
    会注入真实键盘/鼠标事件（连点/连发阶段），请勿在无人值守之外同时使用键盘鼠标。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\perf-suite.ps1 -Quick

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\perf-suite.ps1 -Full -UpdateBaseline

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File scripts\perf-suite.ps1 -Quick -NoClick -Ci
#>
[CmdletBinding()]
param(
    [switch]$Quick,
    [switch]$Full,
    [switch]$SkipBuild,
    [switch]$Rebuild,
    [switch]$NoClick,
    [switch]$KillExisting,
    [switch]$UpdateBaseline,
    [switch]$Ci,
    [string]$Configuration = 'Release',
    [string]$OutDir,
    [string]$ThresholdsPath,
    [string]$BaselinePath,
    [string]$MSBuildPath
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$perfDir = Join-Path $rootDir '.perf'
$probeDir = Join-Path $perfDir 'scripts'
$benchDir = Join-Path $perfDir 'bench'
$benchBin = Join-Path $benchDir 'bin'
$exePath = Join-Path $rootDir "keyboardmgr2\bin\$Configuration\keyboardmgr2.exe"
$slnPath = Join-Path $rootDir 'keyboardmgr2\keyboardmgr2.sln'
$thresholdsPath = if ($ThresholdsPath) { $ThresholdsPath } else { Join-Path $scriptDir 'perf-thresholds.json' }
$baselinePath = if ($BaselinePath) { $BaselinePath } else { Join-Path $scriptDir 'perf-baseline.json' }

if ($Quick -and $Full) { throw '不能同时指定 -Quick 和 -Full。' }
$mode = if ($Full) { 'full' } else { 'quick' }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outRoot = if ($OutDir) { $OutDir } else { Join-Path $perfDir "results\$stamp" }
$rawDir = Join-Path $outRoot 'raw'
$logDir = Join-Path $outRoot 'logs'
$reportPath = Join-Path $outRoot 'perf-report.md'
$resultsJsonPath = Join-Path $outRoot 'perf-results.json'

$script:checks = New-Object System.Collections.ArrayList
$script:flatMetrics = [ordered]@{}
$script:stageNotes = [ordered]@{}
$script:failCount = 0
$script:warnCount = 0
$script:skipCount = 0
$script:startTime = Get-Date
$script:buildInfo = $null
$script:envInfo = $null
$script:config = $null
$script:originalAutoStartTask = $null
$script:registryTouched = $false
$script:registryPath = 'HKCU:\Software\LCS\keyboardmgr'

function Write-Stage([string]$text) {
    if (-not $Ci) { Write-Host "`n==> $text" -ForegroundColor Cyan } else { Write-Host "==> $text" }
}

function Write-Info([string]$text) {
    if (-not $Ci) { Write-Host "    $text" -ForegroundColor Gray } else { Write-Host "    $text" }
}

function Get-PropValue($obj, [string]$name) {
    if ($null -eq $obj) { return $null }
    $p = $obj.PSObject.Properties[$name]
    if ($null -eq $p) { return $null }
    return $p.Value
}

function Read-JsonFile([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "找不到 JSON 文件：$path" }
    $raw = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    $parsed = $raw | ConvertFrom-Json
    if ($null -eq $parsed) { return }
    # Windows PowerShell 5.1 的 ConvertFrom-Json 会把顶层数组当成单个对象输出；
    # 这里显式展开，保证调用方拿到的是数组元素。
    if ($parsed -is [System.Array]) {
        foreach ($item in $parsed) { $item }
    } else {
        $parsed
    }
}

function Get-Median($values) {
    $v = @($values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ } | Sort-Object)
    if ($v.Count -eq 0) { return $null }
    if ($v.Count % 2 -eq 1) { return [math]::Round($v[[int](($v.Count - 1) / 2)], 2) }
    return [math]::Round(($v[$v.Count / 2 - 1] + $v[$v.Count / 2]) / 2.0, 2)
}

function Get-Percentile($values, [double]$p) {
    $v = @($values | Where-Object { $null -ne $_ } | ForEach-Object { [double]$_ } | Sort-Object)
    if ($v.Count -eq 0) { return $null }
    $idx = [int][math]::Floor(($v.Count - 1) * $p)
    return [math]::Round($v[$idx], 2)
}

function Format-Num($value, [int]$digits = 1) {
    if ($null -eq $value) { return '—' }
    return ([double]$value).ToString("F$digits", [Globalization.CultureInfo]::InvariantCulture)
}

function Set-Metric([string]$key, $value) {
    $script:flatMetrics[$key] = $value
}

function Add-Check {
    param(
        [string]$Section,
        [string]$Name,
        $Value,
        [string]$Unit = '',
        $Limit = $null,
        [string]$Level = 'fail',
        [string]$DisplayValue = '',
        [string]$Note = ''
    )
    $status = 'SKIP'
    $numeric = $Value
    if ($null -ne $numeric -and $null -ne $Limit) {
        $status = if ([double]$numeric -le [double]$Limit) { 'PASS' } elseif ($Level -eq 'warn') { 'WARN' } else { 'FAIL' }
    }
    if ($status -eq 'PASS') {
        $displayForConsole = if ($DisplayValue -ne '') { $DisplayValue } else { (Format-Num $numeric 1) + ' ' + $Unit }
    } else {
        $displayForConsole = if ($DisplayValue -ne '') { $DisplayValue } else { '—' }
    }
    $record = [pscustomobject]@{
        section = $Section
        name = $Name
        value = if ($null -ne $numeric) { [math]::Round([double]$numeric, 3) } else { $null }
        unit = $Unit
        limit = if ($null -ne $Limit) { [math]::Round([double]$Limit, 3) } else { $null }
        level = $Level
        status = $status
        display = $DisplayValue
        note = $Note
    }
    [void]$script:checks.Add($record)
    if ($status -eq 'FAIL') { $script:failCount++ }
    if ($status -eq 'WARN') { $script:warnCount++ }
    if ($status -eq 'SKIP') { $script:skipCount++ }
    $color = 'Green'
    if ($status -eq 'FAIL') { $color = 'Red' } elseif ($status -eq 'WARN') { $color = 'Yellow' } elseif ($status -eq 'SKIP') { $color = 'DarkGray' }
    $limitText = if ($null -ne $Limit) { (Format-Num $Limit 1) + ' ' + $Unit } else { '—' }
    if (-not $Ci) {
        Write-Host ('    [{0,-4}] {1} = {2} (限值 {3})' -f $status, $Name, $displayForConsole, $limitText) -ForegroundColor $color
    } else {
        Write-Host ('[{0}] {1} = {2} (限值 {3})' -f $status, $Name, $displayForConsole, $limitText)
    }
}

function Invoke-PwshScript {
    param([string]$Path, [string[]]$Arguments, [string]$LogName)
    $logPath = Join-Path $logDir $LogName
    $sw = [Diagnostics.Stopwatch]::StartNew()
    # 子进程可能向 stderr 写警告；在 $ErrorActionPreference=Stop 下会被当成终止错误，这里降级为 Continue
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & powershell -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments 2>&1 | Out-String
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldEap
    }
    $sw.Stop()
    $out | Out-File -LiteralPath $logPath -Encoding utf8
    return [pscustomobject]@{ code = $code; seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1); log = $logPath; output = $out }
}

function Invoke-Exe {
    param([string]$FilePath, [string[]]$Arguments, [string]$LogName)
    $logPath = Join-Path $logDir $LogName
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & $FilePath @Arguments 2>&1 | Out-String
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldEap
    }
    $sw.Stop()
    $out | Out-File -LiteralPath $logPath -Encoding utf8
    return [pscustomobject]@{ code = $code; seconds = [math]::Round($sw.Elapsed.TotalSeconds, 1); log = $logPath; output = $out }
}

function Resolve-MSBuild {
    if ($MSBuildPath) {
        if (Test-Path -LiteralPath $MSBuildPath) { return (Resolve-Path -LiteralPath $MSBuildPath).Path }
        throw "指定的 MSBuild 不存在：$MSBuildPath"
    }
    $candidates = @(
        'E:\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
    )
    foreach ($c in $candidates) { if (Test-Path -LiteralPath $c) { return $c } }
    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path -LiteralPath $vswhere) {
        try {
            $found = & $vswhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null
            if ($found) { return @($found)[0] }
        } catch { }
    }
    throw '找不到 MSBuild.exe，请用 -MSBuildPath 指定。'
}

function Kill-TestProcesses {
    Get-Process keyboardmgr2, PerfTarget, bench-click, bench-data -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Save-RegistryState {
    if (Test-Path -LiteralPath $script:registryPath) {
        $value = (Get-ItemProperty -LiteralPath $script:registryPath).DoRegisterAutoStartTask
        if ($null -ne $value) { $script:originalAutoStartTask = $value }
        Set-ItemProperty -LiteralPath $script:registryPath -Name DoRegisterAutoStartTask -Value 0 -Type DWord
        $script:registryTouched = $true
    }
}

function Restore-RegistryState {
    if ($script:registryTouched) {
        try {
            Set-ItemProperty -LiteralPath $script:registryPath -Name DoRegisterAutoStartTask -Value ([int]$script:originalAutoStartTask) -Type DWord
        } catch { }
    }
}

function Warmup-Application([int]$count) {
    for ($i = 1; $i -le $count; $i++) {
        try {
            $p = Start-Process -FilePath $exePath -WorkingDirectory (Split-Path $exePath) -ArgumentList '--autostart' -PassThru
            Start-Sleep -Milliseconds 2500
            if ($null -ne $p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
        } catch { }
        Start-Sleep -Milliseconds 300
    }
}

# ---------------------------------------------------------------------------
# 0. 预检
# ---------------------------------------------------------------------------
try {
    if (-not [Environment]::UserInteractive) { throw '当前不是交互式桌面会话，UI/注入类测试无法运行。' }
    if (-not (Test-Path -LiteralPath $slnPath)) { throw "找不到解决方案：$slnPath" }
    if (-not (Test-Path -LiteralPath $thresholdsPath)) { throw "找不到阈值配置：$thresholdsPath" }
    foreach ($required in @('startup-probe.ps1', 'idle-probe.ps1', 'rapidfire-probe.ps1', 'PerfTarget.exe')) {
        $p = Join-Path $probeDir $required
        if (-not (Test-Path -LiteralPath $p)) { throw "缺少测试探针：$p（.perf 目录需完整）" }
    }

    $script:config = Read-JsonFile $thresholdsPath
    $modeCfg = Get-PropValue $script:config $mode
    $limits = Get-PropValue $script:config 'limits'

    New-Item -ItemType Directory -Force -Path $outRoot, $rawDir, $logDir | Out-Null

    $running = Get-Process keyboardmgr2 -ErrorAction SilentlyContinue
    if ($running) {
        if ($KillExisting) {
            Write-Info '检测到正在运行的 keyboardmgr2，按 -KillExisting 结束它。'
            $running | Stop-Process -Force -ErrorAction SilentlyContinue
            Start-Sleep -Milliseconds 500
        } else {
            throw '检测到 keyboardmgr2 正在运行。请先退出程序，或加 -KillExisting 让套件自动结束它。'
        }
    }
    Kill-TestProcesses

    $msbuild = Resolve-MSBuild
    Save-RegistryState

    $gitHead = ''
    $gitDirty = ''
    try { $gitHead = (& git -C $rootDir log -1 --format='%h %s' 2>$null | Out-String).Trim() } catch { }
    try { $gitDirty = (& git -C $rootDir status --porcelain 2>$null | Measure-Object).Count } catch { }

    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $os = Get-CimInstance Win32_OperatingSystem
    $ramGB = [math]::Round((Get-CimInstance Win32_PhysicalMemory | Measure-Object Capacity -Sum).Sum / 1GB, 1)
    $script:envInfo = [ordered]@{
        timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
        mode = $mode
        os = ("{0} build {1}" -f $os.Caption, $os.BuildNumber)
        cpu = $cpu.Name
        cpuCores = ("{0}C/{1}T" -f $cpu.NumberOfCores, $cpu.NumberOfLogicalProcessors)
        ramGB = $ramGB
        gitHead = $gitHead
        gitDirtyFiles = $gitDirty
        msbuild = $msbuild
        configuration = $Configuration
        quickMode = ($mode -eq 'quick')
        noClick = [bool]$NoClick
    }

    if (-not $Ci) {
        Write-Host ''
        Write-Host '键鼠管家自动化性能测试套件' -ForegroundColor White
        Write-Host ("模式: {0}    输出: {1}" -f $mode, $outRoot) -ForegroundColor White
        Write-Host ("环境: {0} / {1} / {2}GB RAM" -f $script:envInfo.cpu, $script:envInfo.os, $ramGB) -ForegroundColor DarkGray
    }

    # -----------------------------------------------------------------------
    # 1. 构建
    # -----------------------------------------------------------------------
    Write-Stage "1/7 构建 ($Configuration)"
    $script:buildInfo = [ordered]@{ skipped = [bool]$SkipBuild; target = ''; seconds = $null; code = $null; exeBytes = $null }
    if (-not $SkipBuild) {
        $buildTarget = if ($Rebuild -or (Get-PropValue $modeCfg 'buildMode') -eq 'rebuild') { 'Rebuild' } else { 'Build' }
        $script:buildInfo.target = $buildTarget
        $buildArgs = @($slnPath, "/t:$buildTarget", "/p:Configuration=$Configuration", '/v:minimal', '/nologo', '/m:4')
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $oldEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $buildOut = & $msbuild @buildArgs 2>&1 | Out-String
            $buildCode = $LASTEXITCODE
        } finally {
            $ErrorActionPreference = $oldEap
        }
        $sw.Stop()
        $buildLog = Join-Path $logDir 'build.log'
        $buildOut | Out-File -LiteralPath $buildLog -Encoding utf8
        $script:buildInfo.seconds = [math]::Round($sw.Elapsed.TotalSeconds, 2)
        $script:buildInfo.code = $buildCode
        $script:buildInfo.log = $buildLog
        Write-Info ("构建耗时 {0}s，退出码 {1}" -f $script:buildInfo.seconds, $script:buildInfo.code)
        if ($buildCode -ne 0) {
            $tail = ($buildOut -split "`r?`n" | Select-Object -Last 8) -join "`n"
            throw "构建失败（exit $buildCode）。`n$tail"
        }
    } else {
        Write-Info '已跳过构建（-SkipBuild）'
    }
    if (-not (Test-Path -LiteralPath $exePath)) { throw "找不到可执行文件：$exePath（构建未产出？）" }
    $script:buildInfo.exeBytes = (Get-Item -LiteralPath $exePath).Length
    $exeVersion = (Get-Item -LiteralPath $exePath).VersionInfo.FileVersion
    $script:envInfo.version = $exeVersion
    Set-Metric 'build.seconds' $script:buildInfo.seconds
    Add-Check -Section '构建' -Name '构建耗时' -Value $script:buildInfo.seconds -Limit (Get-PropValue $limits 'buildSeconds') -DisplayValue ("{0}s ({1})" -f (Format-Num $script:buildInfo.seconds 2), $script:buildInfo.target)
    Add-Check -Section '构建' -Name '构建成功' -Value 1 -Limit 1 -DisplayValue '是'

    Warmup-Application 1
    Write-Info '已预热启动一次'

    # -----------------------------------------------------------------------
    # 2. 启动
    # -----------------------------------------------------------------------
    Write-Stage '2/7 启动性能'
    $startupRuns = [int](Get-PropValue $modeCfg 'startupRuns')
    $r = Invoke-PwshScript -Path (Join-Path $probeDir 'startup-probe.ps1') -Arguments @('-Runs', "$startupRuns", '-Exe', $exePath, '-OutJson', (Join-Path $rawDir 'startup.json')) -LogName 'startup-probe.log'
    $startup = $null
    if ($r.code -eq 0) {
        try { $startup = @(Read-JsonFile (Join-Path $rawDir 'startup.json')) } catch { Write-Info "启动数据解析失败：$($_.Exception.Message)" }
    } else { Write-Info "启动探针退出码 $($r.code)" }
    $startupStats = @{ normal = @{}; autostart = @{} }
    if ($null -ne $startup -and $startup.Count -gt 0) {
        foreach ($m in @('normal', 'autostart')) {
            $group = @($startup | Where-Object { $_.mode -eq $m })
            foreach ($key in @('tInputIdleMs', 'tVisibleMs', 'tCpuQuietMs', 'cpuMsAtSettle', 'workingSetMB', 'privateMB', 'handles', 'threads')) {
                $startupStats[$m][$key] = Get-Median @($group | ForEach-Object { Get-PropValue $_ $key })
            }
            Set-Metric "startup.$m.visibleMs" $startupStats[$m]['tVisibleMs']
            Set-Metric "startup.$m.inputIdleMs" $startupStats[$m]['tInputIdleMs']
            Set-Metric "startup.$m.workingSetMB" $startupStats[$m]['workingSetMB']
        }
        $limStartup = Get-PropValue $limits 'startup'
        Add-Check -Section '启动' -Name '窗口可见(正常启动)' -Value $startupStats['normal']['tVisibleMs'] -Unit 'ms' -Limit (Get-PropValue $limStartup 'visibleMs') -Note '中位数'
        Add-Check -Section '启动' -Name '输入空闲(正常启动)' -Value $startupStats['normal']['tInputIdleMs'] -Unit 'ms' -Limit (Get-PropValue $limStartup 'inputIdleMs') -Note '中位数'
        Add-Check -Section '启动' -Name '初始化完成(正常启动)' -Value $startupStats['normal']['tCpuQuietMs'] -Unit 'ms' -Limit (Get-PropValue $limStartup 'cpuQuietMs') -Note '中位数'
        Add-Check -Section '启动' -Name '窗口可见(托盘启动)' -Value $startupStats['autostart']['tVisibleMs'] -Unit 'ms' -Limit (Get-PropValue $limStartup 'visibleMs') -Note '中位数'
        Add-Check -Section '启动' -Name '工作集(正常启动)' -Value $startupStats['normal']['workingSetMB'] -Unit 'MB' -Limit (Get-PropValue $limStartup 'workingSetMB') -Note '中位数'
    } else {
        Add-Check -Section '启动' -Name '启动探针' -Value $null -DisplayValue '未获取到数据' -Note "退出码 $($r.code)，见 $($r.log)"
    }

    # -----------------------------------------------------------------------
    # 3. 空闲
    # -----------------------------------------------------------------------
    Write-Stage '3/7 空闲资源'
    $traySeconds = [int](Get-PropValue $modeCfg 'idleTraySeconds')
    $windowSeconds = [int](Get-PropValue $modeCfg 'idleWindowSeconds')
    $r = Invoke-PwshScript -Path (Join-Path $probeDir 'idle-probe.ps1') -Arguments @('-Exe', $exePath, '-TraySeconds', "$traySeconds", '-WindowSeconds', "$windowSeconds", '-OutJson', (Join-Path $rawDir 'idle.json')) -LogName 'idle-probe.log'
    $idleStats = @{ tray = @{}; window = @{} }
    if ($r.code -eq 0) {
        try {
            $idle = @(Read-JsonFile (Join-Path $rawDir 'idle.json'))
            foreach ($m in @('tray', 'window')) {
                $sc = @($idle | Where-Object { $_.mode -eq $m }) | Select-Object -First 1
                if ($null -ne $sc) {
                    foreach ($key in @('avgCpuPct', 'idleCpuPct60s', 'wsStartMB', 'wsEndMB', 'privEndMB', 'handleStart', 'handleEnd', 'threadEnd', 'gdiEnd', 'userEnd', 'uiPingMaxMs')) {
                        $idleStats[$m][$key] = Get-PropValue $sc $key
                    }
                    $idleStats[$m]['handleDrift'] = if ($null -ne $idleStats[$m]['handleEnd'] -and $null -ne $idleStats[$m]['handleStart']) { $idleStats[$m]['handleEnd'] - $idleStats[$m]['handleStart'] } else { $null }
                    Set-Metric "idle.$m.avgCpuPct" $idleStats[$m]['avgCpuPct']
                    Set-Metric "idle.$m.workingSetMB" $idleStats[$m]['wsEndMB']
                }
            }
            $limIdle = Get-PropValue $limits 'idle'
            Add-Check -Section '空闲' -Name '托盘平均CPU' -Value $idleStats['tray']['avgCpuPct'] -Unit '%' -Limit (Get-PropValue $limIdle 'avgCpuPct') -Note "采样 ${traySeconds}s"
            Add-Check -Section '空闲' -Name '主窗口平均CPU' -Value $idleStats['window']['avgCpuPct'] -Unit '%' -Limit (Get-PropValue $limIdle 'windowCpuPct') -Note "采样 ${windowSeconds}s"
            Add-Check -Section '空闲' -Name '托盘工作集' -Value $idleStats['tray']['wsEndMB'] -Unit 'MB' -Limit (Get-PropValue $limIdle 'workingSetMB')
            $drift = $idleStats['tray']['handleDrift']
            if ($null -ne $drift) { $drift = [math]::Abs([double]$drift) }
            Add-Check -Section '空闲' -Name '托盘句柄漂移' -Value $drift -Unit '个' -Limit (Get-PropValue $limIdle 'handleDrift') -Level 'warn'
            Add-Check -Section '空闲' -Name 'UI线程探针(最大)' -Value $idleStats['tray']['uiPingMaxMs'] -Unit 'ms' -Limit (Get-PropValue $limIdle 'uiPingMaxMs') -Level 'warn'
        } catch { Write-Info "空闲数据解析失败：$($_.Exception.Message)" }
    } else { Write-Info "空闲探针退出码 $($r.code)" }
    if ($null -eq $idleStats['tray']['avgCpuPct']) {
        Add-Check -Section '空闲' -Name '空闲探针' -Value $null -DisplayValue '未获取到数据' -Note "退出码 $($r.code)，见 $($r.log)"
    }

    # -----------------------------------------------------------------------
    # 4. 连点内核
    # -----------------------------------------------------------------------
    $clickSummary = $null
    if ($NoClick) {
        Write-Stage '4/7 连点内核（-NoClick 已跳过）'
    } else {
        Write-Stage '4/7 连点内核'
        $benchClick = Join-Path $benchBin 'bench-click.exe'
        $needBuild = -not (Test-Path -LiteralPath $benchClick)
        if (-not $needBuild) {
            $binTime = (Get-Item -LiteralPath $benchClick).LastWriteTime
            $srcFiles = @(Get-ChildItem -LiteralPath (Join-Path $benchDir 'src') -Recurse -File -ErrorAction SilentlyContinue) + @(Get-ChildItem -LiteralPath (Join-Path $rootDir 'keyboardmgr2\Modules') -Filter '*.vb' -File -ErrorAction SilentlyContinue)
            $newer = @($srcFiles | Where-Object { $_.LastWriteTime -gt $binTime } | Select-Object -First 1)
            if ($newer.Count -gt 0) { $needBuild = $true; Write-Info "检测到源码更新（$($newer[0].Name)），重建基准工具" }
        }
        if ($needBuild) {
            $r = Invoke-PwshScript -Path (Join-Path $benchDir 'build.ps1') -Arguments @() -LogName 'bench-build.log'
            if ($r.code -ne 0 -or -not (Test-Path -LiteralPath $benchClick)) { Write-Info "基准工具构建失败（exit $($r.code)），跳过连点内核测试" }
        }
        if (Test-Path -LiteralPath $benchClick) {
            $intervals = (Get-PropValue $modeCfg 'clickIntervals') -join ','
            $clickArgs = @('--run', '--out', (Join-Path $rawDir 'bench-click.json'),
                '--repeats', "$([int](Get-PropValue $modeCfg 'clickRepeats'))",
                '--duration', "$([int](Get-PropValue $modeCfg 'clickDurationMs'))",
                '--intervals', $intervals)
            if (-not [bool](Get-PropValue $modeCfg 'runKeyboardTest')) { $clickArgs += '--no-keyboard' }
            $r = Invoke-Exe -FilePath $benchClick -Arguments $clickArgs -LogName 'bench-click.log'
            $script:stageNotes['benchClickSeconds'] = $r.seconds
            if ($r.code -eq 0) {
                try { $clickSummary = Read-JsonFile (Join-Path $rawDir 'bench-click.json') } catch { Write-Info "连点数据解析失败：$($_.Exception.Message)" }
            } else { Write-Info "连点基准退出码 $($r.code)（见 $($r.log)）" }
        }
    }
    if ($null -ne $clickSummary) {
        $limClick = Get-PropValue $limits 'click'
        $lossTable = Get-PropValue $limClick 'lossRateMaxPct'
        foreach ($kernel in @('native', 'managed')) {
            $rows = @(Get-PropValue (Get-PropValue $clickSummary 'summary') $kernel)
            foreach ($row in $rows) {
                $iv = [int](Get-PropValue $row 'intervalMs')
                $mean = Get-PropValue $row 'meanMedian'
                $loss = Get-PropValue $row 'lossRateMedian'
                $cpu = Get-PropValue $row 'cpuPercentMedian'
                if ($null -eq $mean) { continue }
                $dev = [math]::Abs([double]$mean - $iv)
                $devPct = if ($iv -gt 0) { $dev / $iv * 100.0 } else { 0 }
                $tolPct = if ($kernel -eq 'native') { [double](Get-PropValue $limClick 'nativeMeanTolerancePct') } else { [double](Get-PropValue $limClick 'managedMeanTolerancePct') }
                $tolAbs = if ($kernel -eq 'native') { [double](Get-PropValue $limClick 'nativeMeanToleranceAbsMs') } else { [double](Get-PropValue $limClick 'managedMeanToleranceAbsMs') }
                $level = if ($kernel -eq 'native') { 'fail' } else { 'warn' }
                $key = if ($kernel -eq 'native') { '原生' } else { '托管' }
                Set-Metric "click.$kernel.$iv.meanMs" $mean
                Set-Metric "click.$kernel.$iv.lossPct" ($loss * 100)
                $ok = ($devPct -le $tolPct) -or ($dev -le $tolAbs)
                $statusLevel = if ($ok) { 'fail' } else { $level }
                Add-Check -Section '连点' -Name "$key $iv ms 实际间隔" -Value $dev -Unit 'ms(偏差)' -Limit ([math]::Max($tolAbs, $iv * $tolPct / 100.0)) -Level $statusLevel -DisplayValue ("{0}ms (偏差 {1}ms / {2}%)" -f (Format-Num $mean 2), (Format-Num $dev 2), (Format-Num $devPct 1)) -Note ("σ={0} P95={1} 送达={2}" -f (Format-Num (Get-PropValue $row 'stdMedian') 2), (Format-Num (Get-PropValue $row 'p95Median') 1), (Format-Num (Get-PropValue $row 'deliveredMedian') 0))
                $lossLimit = [double](Get-PropValue $lossTable "$iv")
                Add-Check -Section '连点' -Name "$key $iv ms 丢点率" -Value ([double]$loss * 100) -Unit '%' -Limit $lossLimit -Level 'warn' -DisplayValue ("{0}%" -f (Format-Num ([double]$loss * 100) 1))
                Add-Check -Section '连点' -Name "$key $iv ms CPU(单核)" -Value $cpu -Unit '%' -Limit (Get-PropValue $limClick 'cpuOneCoreMaxPct') -Level 'warn'
            }
        }
        $keyRows = @(Get-PropValue (Get-PropValue $clickSummary 'summary') 'key')
        foreach ($row in $keyRows) {
            $case = Get-PropValue $row 'case'
            $period = Get-PropValue $row 'periodMedianMs'
            if ($null -eq $period) { continue }
            Set-Metric "click.key.$case.periodMs" $period
            $limit = if ($case -like '*combo*') { 60.0 } else { 56.0 }
            if ($case -like 'managed*') {
                Add-Check -Section '连点' -Name "键盘 $case 周期" -Value $period -Unit 'ms' -Limit $limit -Level 'warn' -DisplayValue ("{0}ms" -f (Format-Num $period 2))
            } else {
                Add-Check -Section '连点' -Name "键盘 $case 周期" -Value $period -Unit 'ms' -Limit $limit -DisplayValue ("{0}ms" -f (Format-Num $period 2))
            }
        }
    } elseif (-not $NoClick) {
        Add-Check -Section '连点' -Name '连点内核探针' -Value $null -DisplayValue '未获取到数据'
    }

    # -----------------------------------------------------------------------
    # 5. 连发
    # -----------------------------------------------------------------------
    if ($NoClick) {
        Write-Stage '5/7 连发（-NoClick 已跳过）'
    } else {
        Write-Stage '5/7 连发'
        $rfArgs = @('-Exe', $exePath, '-TargetExe', (Join-Path $probeDir 'PerfTarget.exe'), '-WorkDir', $perfDir, '-OutJson', (Join-Path $rawDir 'rapidfire.json'))
        if ([bool](Get-PropValue $modeCfg 'rapidfireQuick')) { $rfArgs += '-Quick' }
        $r = Invoke-PwshScript -Path (Join-Path $probeDir 'rapidfire-probe.ps1') -Arguments $rfArgs -LogName 'rapidfire-probe.log'
        if ($r.code -eq 0) {
            try {
                $rf = @(Read-JsonFile (Join-Path $rawDir 'rapidfire.json'))
                $limRf = Get-PropValue $limits 'rapidfire'
                foreach ($sc in $rf) {
                    $name = Get-PropValue $sc 'scenario'
                    $avg = Get-PropValue $sc 'achievedAvgMs'
                    $p95 = Get-PropValue (Get-PropValue $sc 'intervalStats') 'p95'
                    $first = Get-PropValue $sc 'firstEnterMs'
                    $cpu = Get-PropValue $sc 'cpuPctDuring'
                    $ping = Get-PropValue (Get-PropValue $sc 'uiPing') 'maxMs'
                    if ($name -like 'throughput*') {
                        $interval = [double](Get-PropValue $sc 'intervalMs')
                        $limit = $interval * (1 + [double](Get-PropValue $limRf 'achievedAvgOverPct') / 100.0)
                        Set-Metric "rapidfire.$name.achievedAvgMs" $avg
                        Add-Check -Section '连发' -Name "$name 实际间隔均值" -Value $avg -Unit 'ms' -Limit $limit -DisplayValue ("{0}ms（目标 {1}ms）" -f (Format-Num $avg 2), $interval)
                        Add-Check -Section '连发' -Name "$name P95" -Value $p95 -Unit 'ms' -Limit (Get-PropValue $limRf 'p95MaxMs') -Level 'warn'
                        Add-Check -Section '连发' -Name "$name UI线程探针(最大)" -Value $ping -Unit 'ms' -Limit (Get-PropValue $limRf 'uiPingMaxMs') -Level 'warn'
                        Add-Check -Section '连发' -Name "$name 首条延迟" -Value $first -Unit 'ms' -Limit (Get-PropValue $limRf 'firstItemMaxMs')
                        Add-Check -Section '连发' -Name "$name CPU" -Value $cpu -Unit '%' -Limit (Get-PropValue $limRf 'cpuPctMax') -Level 'warn'
                    }
                }
            } catch { Write-Info "连发数据解析失败：$($_.Exception.Message)" }
        } else { Write-Info "连发探针退出码 $($r.code)（见 $($r.log)）" }
    }

    # -----------------------------------------------------------------------
    # 6. 数据通路
    # -----------------------------------------------------------------------
    Write-Stage '6/7 数据通路（编解码 / 设置读写）'
    $benchData = Join-Path $benchBin 'bench-data.exe'
    if (Test-Path -LiteralPath $benchData) {
        $r = Invoke-Exe -FilePath $benchData -Arguments @('--out', (Join-Path $rawDir 'bench-data.json')) -LogName 'bench-data.log'
        if ($r.code -eq 0) {
            try {
                $bd = Read-JsonFile (Join-Path $rawDir 'bench-data.json')
                $limData = Get-PropValue $limits 'data'
                $codec = @(Get-PropValue $bd 'codec') | Where-Object { [int](Get-PropValue $_ 'events') -eq 100000 } | Select-Object -First 1
                if ($null -ne $codec) {
                    $ser = Get-PropValue $codec 'serializeMedianMs'
                    $des = Get-PropValue $codec 'deserializeMedianMs'
                    Set-Metric 'data.codec.serialize100kMs' $ser
                    Set-Metric 'data.codec.deserialize100kMs' $des
                    Add-Check -Section '数据' -Name '编解码序列化(10万事件)' -Value $ser -Unit 'ms' -Limit (Get-PropValue $limData 'serialize100kMaxMs') -Level 'warn'
                    Add-Check -Section '数据' -Name '编解码反序列化(10万事件)' -Value $des -Unit 'ms' -Limit (Get-PropValue $limData 'deserialize100kMaxMs') -Level 'warn'
                }
                $store = Get-PropValue $bd 'recordingStore'
                if ($null -ne $store) {
                    Add-Check -Section '数据' -Name '录制保存(10万事件)' -Value (Get-PropValue $store 'saveMedianMs') -Unit 'ms' -Limit (Get-PropValue $limData 'save100kMaxMs') -Level 'warn'
                    Add-Check -Section '数据' -Name '录制加载(10万事件)' -Value (Get-PropValue $store 'loadMedianMs') -Unit 'ms' -Limit (Get-PropValue $limData 'load100kMaxMs') -Level 'warn'
                }
                $settings = Get-PropValue $bd 'settings'
                if ($null -ne $settings) {
                    $readMs = Get-PropValue $settings 'readMedianMs'
                    Set-Metric 'data.settings.read1000Ms' $readMs
                    Add-Check -Section '数据' -Name '设置读取(x1000)' -Value $readMs -Unit 'ms' -Limit (Get-PropValue $limData 'settingsRead1000MaxMs') -Level 'warn'
                }
            } catch { Write-Info "数据通路解析失败：$($_.Exception.Message)" }
        } else { Write-Info "数据通路基准退出码 $($r.code)" }
    } else {
        Write-Info 'bench-data.exe 不存在（需 .perf/bench 构建产物）'
    }

    # -----------------------------------------------------------------------
    # 7. 退出残留
    # -----------------------------------------------------------------------
    Write-Stage '7/7 退出残留检查'
    $r = Invoke-PwshScript -Path (Join-Path $scriptDir 'exit-check.ps1') -Arguments @() -LogName 'exit-check.log'
    $exitDisplay = '通过'
    if ($r.code -ne 0) { $exitDisplay = "失败(exit $($r.code))" }
    Add-Check -Section '退出' -Name '退出残留检查' -Value $r.code -Limit 0 -DisplayValue $exitDisplay -Note '详见 exit-check.log'
    Set-Metric 'exit.code' $r.code

    # -----------------------------------------------------------------------
    # 基线对比
    # -----------------------------------------------------------------------
    $baseline = $null
    $baselineRows = New-Object System.Collections.ArrayList
    if ($UpdateBaseline) {
        $baseline = [ordered]@{ created = (Get-Date).ToString('s'); mode = $mode; metrics = $script:flatMetrics }
        $baseline | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $baselinePath -Encoding utf8
        Write-Info "已写入基线：$baselinePath"
    } elseif (Test-Path -LiteralPath $baselinePath) {
        try { $baseline = Read-JsonFile $baselinePath } catch { $baseline = $null }
    }
    if ($null -ne $baseline -and -not $UpdateBaseline) {
        $tol = [double](Get-PropValue $script:config 'baselineTolerancePct')
        $baseMetrics = Get-PropValue $baseline 'metrics'
        if ($null -ne $baseMetrics) {
            foreach ($prop in $baseMetrics.PSObject.Properties) {
                $k = $prop.Name
                if (-not $script:flatMetrics.Contains($k)) { continue }
                $b = [double]$prop.Value
                $c = [double]$script:flatMetrics[$k]
                if ($b -le 0) { continue }
                $deltaPct = ($c - $b) / $b * 100.0
                [void]$baselineRows.Add([pscustomobject]@{ key = $k; baseline = $b; current = $c; deltaPct = [math]::Round($deltaPct, 1) })
                if ($deltaPct -gt $tol) {
                    $level = if ($k -match '^(startup|click\.native|rapidfire)\.') { 'fail' } else { 'warn' }
                    Add-Check -Section '基线对比' -Name "回归: $k" -Value $deltaPct -Unit '%' -Limit $tol -Level $level -DisplayValue ("基线 {0} → 本次 {1}（+{2}%）" -f (Format-Num $b 2), (Format-Num $c 2), (Format-Num $deltaPct 1)) -Note ("基线时间 {0}" -f (Get-PropValue $baseline 'created'))
                }
            }
        }
    }

    # -----------------------------------------------------------------------
    # 报告
    # -----------------------------------------------------------------------
    $total = $script:checks.Count
    $passCount = @($script:checks | Where-Object { $_.status -eq 'PASS' }).Count
    $verdict = if ($script:failCount -gt 0) { 'FAIL' } elseif ($script:warnCount -gt 0) { 'PASS (有警告)' } else { 'PASS' }
    $elapsed = [math]::Round(((Get-Date) - $script:startTime).TotalSeconds, 1)

    $lines = New-Object System.Collections.Generic.List[string]
    $null = $lines.Add('# 键鼠管家性能测试报告')
    $null = $lines.Add('')
    $null = $lines.Add("**结论：$verdict** ｜ 检查项 $total（通过 $passCount / 警告 $($script:warnCount) / 失败 $($script:failCount) / 跳过 $($script:skipCount)） ｜ 总耗时 $elapsed s")
    $null = $lines.Add('')
    $null = $lines.Add('| 项 | 值 |')
    $null = $lines.Add('|---|---|')
    foreach ($key in $script:envInfo.Keys) { $null = $lines.Add("| $key | $($script:envInfo[$key]) |") }
    $null = $lines.Add("| reportPath | $reportPath |")
    $null = $lines.Add('')
    $null = $lines.Add('## 检查明细')
    $null = $lines.Add('')
    $null = $lines.Add('| 阶段 | 检查项 | 实测 | 阈值 | 结果 | 备注 |')
    $null = $lines.Add('|---|---|---|---|---|---|')
    foreach ($c in $script:checks) {
        $limitText = if ($null -ne $c.limit) { (Format-Num $c.limit 1) + ' ' + $c.unit } else { '—' }
        if ($c.display -ne '') { $valueText = $c.display }
        elseif ($null -ne $c.value) { $valueText = (Format-Num $c.value 2) + ' ' + $c.unit }
        else { $valueText = '—' }
        $null = $lines.Add("| $($c.section) | $($c.name) | $valueText | $limitText | $($c.status) | $($c.note) |")
    }
    $null = $lines.Add('')

    if ($null -ne $startup -and $startup.Count -gt 0) {
        $null = $lines.Add('## 启动指标（中位数）')
        $null = $lines.Add('')
        $null = $lines.Add('| 模式 | WaitForInputIdle | 窗口可见 | 初始化完成(CPU静止) | 结算CPU | 工作集 | 私有内存 | 句柄 | 线程 |')
        $null = $lines.Add('|---|---|---|---|---|---|---|---|---|')
        foreach ($m in @('normal', 'autostart')) {
            $s = $startupStats[$m]
            $null = $lines.Add("| $m | $(Format-Num $s['tInputIdleMs'] 0) ms | $(Format-Num $s['tVisibleMs'] 0) ms | $(Format-Num $s['tCpuQuietMs'] 0) ms | $(Format-Num $s['cpuMsAtSettle'] 0) ms | $(Format-Num $s['workingSetMB'] 1) MB | $(Format-Num $s['privateMB'] 1) MB | $(Format-Num $s['handles'] 0) | $(Format-Num $s['threads'] 0) |")
        }
        $null = $lines.Add('')
    }

    if ($null -ne $idleStats['tray']['avgCpuPct']) {
        $null = $lines.Add('## 空闲资源')
        $null = $lines.Add('')
        $null = $lines.Add('| 场景 | 平均CPU | 工作集 起始→结束 | 句柄 起始→结束 | 线程 结束 | UI探针最大 |')
        $null = $lines.Add('|---|---|---|---|---|---|')
        foreach ($m in @('tray', 'window')) {
            $s = $idleStats[$m]
            $null = $lines.Add("| $m | $(Format-Num $s['avgCpuPct'] 3)% | $(Format-Num $s['wsStartMB'] 1) → $(Format-Num $s['wsEndMB'] 1) MB | $(Format-Num $s['handleStart'] 0) → $(Format-Num $s['handleEnd'] 0) | $(Format-Num $s['threadEnd'] 0) | $(Format-Num $s['uiPingMaxMs'] 1) ms |")
        }
        $null = $lines.Add('')
    }

    if ($null -ne $clickSummary) {
        $null = $lines.Add('## 连点内核（3 次中位数）')
        $null = $lines.Add('')
        $null = $lines.Add('| 内核 | 间隔 | 实际均值 | σ | P95 | 送达 | 丢点率 | CPU(单核) |')
        $null = $lines.Add('|---|---|---|---|---|---|---|---|')
        foreach ($kernel in @('native', 'managed')) {
            $rows = @(Get-PropValue (Get-PropValue $clickSummary 'summary') $kernel)
            foreach ($row in $rows) {
                $null = $lines.Add("| $kernel | $([int](Get-PropValue $row 'intervalMs')) ms | $(Format-Num (Get-PropValue $row 'meanMedian') 2) ms | $(Format-Num (Get-PropValue $row 'stdMedian') 2) | $(Format-Num (Get-PropValue $row 'p95Median') 2) | $(Format-Num (Get-PropValue $row 'deliveredMedian') 0) | $(Format-Num ([double](Get-PropValue $row 'lossRateMedian') * 100) 1)% | $(Format-Num (Get-PropValue $row 'cpuPercentMedian') 1)% |")
            }
        }
        $null = $lines.Add('')
        $keyRows = @(Get-PropValue (Get-PropValue $clickSummary 'summary') 'key')
        if ($keyRows.Count -gt 0) {
            $null = $lines.Add('| 键盘用例 | 周期 | 按住 | 释放→下次 |')
            $null = $lines.Add('|---|---|---|---|')
            foreach ($row in $keyRows) {
                $null = $lines.Add("| $(Get-PropValue $row 'case') | $(Format-Num (Get-PropValue $row 'periodMedianMs') 2) ms | $(Format-Num (Get-PropValue $row 'holdMedianMs') 2) ms | $(Format-Num (Get-PropValue $row 'gapMedianMs') 2) ms |")
            }
            $null = $lines.Add('')
        }
    }

    if ($null -ne $rf) {
        $null = $lines.Add('## 连发')
        $null = $lines.Add('')
        $null = $lines.Add('| 场景 | 实际均值 | P95 | 首条延迟 | CPU | UI探针最大 |')
        $null = $lines.Add('|---|---|---|---|---|---|')
        foreach ($sc in $rf) {
            $null = $lines.Add("| $(Get-PropValue $sc 'scenario') | $(Format-Num (Get-PropValue $sc 'achievedAvgMs') 2) ms | $(Format-Num (Get-PropValue (Get-PropValue $sc 'intervalStats') 'p95') 1) ms | $(Format-Num (Get-PropValue $sc 'firstEnterMs') 0) ms | $(Format-Num (Get-PropValue $sc 'cpuPctDuring') 1)% | $(Format-Num (Get-PropValue (Get-PropValue $sc 'uiPing') 'maxMs') 1) ms |")
        }
        $null = $lines.Add('')
    }

    if ($null -ne $bd) {
        $null = $lines.Add('## 数据通路')
        $null = $lines.Add('')
        $null = $lines.Add('| 项目 | 值 |')
        $null = $lines.Add('|---|---|')
        $codecRows = @(Get-PropValue $bd 'codec')
        foreach ($c in $codecRows) {
            $null = $lines.Add("| 编解码 $([int](Get-PropValue $c 'events')) 事件 | 序列化 $(Format-Num (Get-PropValue $c 'serializeMedianMs') 2) ms / 反序列化 $(Format-Num (Get-PropValue $c 'deserializeMedianMs') 2) ms / 文件 $([int](Get-PropValue $c 'fileBytes')) B |")
        }
        $store = Get-PropValue $bd 'recordingStore'
        if ($null -ne $store) {
            $null = $lines.Add("| 录制保存/加载(10万) | $(Format-Num (Get-PropValue $store 'saveMedianMs') 2) ms / $(Format-Num (Get-PropValue $store 'loadMedianMs') 2) ms |")
        }
        $settings = Get-PropValue $bd 'settings'
        if ($null -ne $settings) {
            $null = $lines.Add("| 设置读取(x1000) | $(Format-Num (Get-PropValue $settings 'readMedianMs') 3) ms |")
        }
        $null = $lines.Add('')
    }

    if ($baselineRows.Count -gt 0) {
        $null = $lines.Add('## 与基线对比')
        $null = $lines.Add('')
        $null = $lines.Add('| 指标 | 基线 | 本次 | 变化 |')
        $null = $lines.Add('|---|---|---|---|')
        foreach ($row in $baselineRows) {
            $sign = if ($row.deltaPct -ge 0) { '+' } else { '' }
            $null = $lines.Add("| $($row.key) | $(Format-Num $row.baseline 2) | $(Format-Num $row.current 2) | $sign$($row.deltaPct)% |")
        }
        $null = $lines.Add('')
    }

    $null = $lines.Add('## 原始数据与说明')
    $null = $lines.Add('')
    $null = $lines.Add("- 原始数据目录：``$rawDir``")
    $null = $lines.Add("- 日志目录：``$logDir``")
    $null = $lines.Add('- 启动/连发阶段会注入真实键鼠事件并临时将 `DoRegisterAutoStartTask` 置 0（结束后恢复）。')
    $null = $lines.Add('- 阈值可在 `scripts/perf-thresholds.json` 调整；首次运行后可用 `-UpdateBaseline` 生成 `scripts/perf-baseline.json` 作为后续回归基线。')
    $null = $lines.Add('- 连点/连发测试目标均为自建窗口；不会点击用户窗口，但会短暂抢前台。')
    $null = $lines.Add('')
    $lines | Out-File -LiteralPath $reportPath -Encoding utf8
    Copy-Item -LiteralPath $reportPath -Destination (Join-Path $perfDir 'results\latest-report.md') -Force
    $null = $lines.Add("<!-- raw: $($script:flatMetrics | ConvertTo-Json -Depth 4 -Compress) -->")

    $results = [ordered]@{
        meta = $script:envInfo
        build = $script:buildInfo
        summary = [ordered]@{ verdict = $verdict; total = $total; pass = $passCount; warn = $script:warnCount; fail = $script:failCount; skip = $script:skipCount; elapsedSeconds = $elapsed }
        metrics = $script:flatMetrics
        checks = $script:checks
        stageNotes = $script:stageNotes
        rawDir = $rawDir
        logDir = $logDir
        report = $reportPath
    }
    $results | ConvertTo-Json -Depth 10 | Out-File -LiteralPath $resultsJsonPath -Encoding utf8
    Copy-Item -LiteralPath $resultsJsonPath -Destination (Join-Path $perfDir 'results\latest-results.json') -Force

    if ($Ci) {
        Write-Host ("RESULT={0} PASS={1} WARN={2} FAIL={3} SKIP={4}" -f $verdict, $passCount, $script:warnCount, $script:failCount, $script:skipCount)
        Write-Host ("REPORT={0}" -f $reportPath)
    } else {
        Write-Host ''
        $color = if ($script:failCount -gt 0) { 'Red' } elseif ($script:warnCount -gt 0) { 'Yellow' } else { 'Green' }
        Write-Host "结论: $verdict  (通过 $passCount / 警告 $($script:warnCount) / 失败 $($script:failCount) / 跳过 $($script:skipCount))" -ForegroundColor $color
        Write-Host "报告: $reportPath" -ForegroundColor White
        Write-Host "结果: $resultsJsonPath" -ForegroundColor White
        Write-Host "耗时: $elapsed s" -ForegroundColor DarkGray
    }

    if ($script:failCount -gt 0) { exit 1 } else { exit 0 }
} catch {
    Write-Host ''
    Write-Host "性能测试套件执行失败：$($_.Exception.Message)" -ForegroundColor Red
    if (-not $Ci -and $_.ScriptStackTrace) { Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray }
    try {
        New-Item -ItemType Directory -Force -Path $outRoot, $rawDir, $logDir | Out-Null
        $errLines = @(
            '# 键鼠管家性能测试报告（执行失败）',
            '',
            ("时间：{0}" -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')),
            ("模式：{0}" -f $mode),
            '',
            '```',
            $_.Exception.ToString(),
            '```'
        )
        $errLines | Out-File -LiteralPath $reportPath -Encoding utf8
        Write-Host "失败报告: $reportPath" -ForegroundColor Yellow
    } catch { }
    exit 2
} finally {
    try { Kill-TestProcesses } catch { }
    try { Restore-RegistryState } catch { }
}
