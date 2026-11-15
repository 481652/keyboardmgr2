#requires -Version 5.1
<#
    双击 perf-test.cmd 后显示的性能测试模式菜单。
    纯键盘交互；所有路径按本脚本位置解析，不依赖当前目录。
#>

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$perfDir = Join-Path $rootDir '.perf'
$suitePath = Join-Path $scriptDir 'perf-suite.ps1'
$latestResults = Join-Path $perfDir 'results\latest-results.json'
$interactive = -not [Console]::IsInputRedirected

function Show-Banner {
    try { Clear-Host } catch { }
    Write-Host '================================================' -ForegroundColor Cyan
    Write-Host '            键鼠管家  性能测试套件' -ForegroundColor White
    Write-Host '        输入数字后回车，选择测试模式' -ForegroundColor Gray
    Write-Host '================================================' -ForegroundColor Cyan
    Write-Host ''
    Write-Host '  [1]  快速模式                约 3-4 分钟，日常回归（推荐）'
    Write-Host '  [2]  完整模式                约 8-12 分钟，发版前全面测试'
    Write-Host '  [3]  快速模式 · 不注入键鼠     约 2 分钟，可同时用电脑'
    Write-Host '  [4]  完整模式 · 不注入键鼠     约 5-6 分钟'
    Write-Host '  [5]  更新性能基线            快速模式 + 写入基线，供以后回归对比'
    Write-Host '  [6]  查看最近一次报告'
    Write-Host '  [0]  退出'
    Write-Host ''
}

function Get-RecentReportPath {
    if (Test-Path -LiteralPath $latestResults) {
        try {
            $r = Get-Content -LiteralPath $latestResults -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($r.report -and (Test-Path -LiteralPath $r.report)) { return $r.report }
        } catch { }
    }
    # latest-results.json 可能指向已被清理的目录：退回扫描最近的结果目录，找第一个真正包含报告的
    $resultsRoot = Join-Path $perfDir 'results'
    if (-not (Test-Path -LiteralPath $resultsRoot)) { return $null }
    $dirs = Get-ChildItem -LiteralPath $resultsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending
    foreach ($d in $dirs) {
        $p = Join-Path $d.FullName 'perf-report.md'
        if (Test-Path -LiteralPath $p) { return $p }
    }
    return $null
}

function Confirm-Run([string]$Message) {
    if (-not $interactive) { return $true }
    $answer = Read-Host "$Message (Y/n)"
    if ([string]::IsNullOrWhiteSpace($answer)) { return $true }
    return $answer.Trim().ToLowerInvariant() -ne 'n'
}

function Open-Report([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    try {
        Start-Process -FilePath $Path
    } catch {
        try { Start-Process notepad.exe -ArgumentList $Path } catch { }
    }
}

function Invoke-Suite([string[]]$argsList, [string]$label, [bool]$injectsInput) {
    $running = Get-Process keyboardmgr2 -ErrorAction SilentlyContinue
    if ($running) {
        if (-not (Confirm-Run '检测到键鼠管家正在运行，是否结束它并继续？')) { return }
        $argsList += '-KillExisting'
    }

    if ($injectsInput) {
        Write-Host ''
        Write-Host '提示：该模式会注入真实键鼠事件，测试窗口会短暂抢占前台。' -ForegroundColor Yellow
        Write-Host '      开始后请不要操作键盘鼠标，直到测试结束。' -ForegroundColor Yellow
        if (-not (Confirm-Run '确认开始？')) { return }
    }

    Write-Host ''
    Write-Host "开始：$label（按 Ctrl+C 可中断）" -ForegroundColor Cyan
    Write-Host ''
    $oldEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File $suitePath @argsList
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldEap
    }

    Write-Host ''
    switch ($code) {
        0 { Write-Host '测试完成：PASS（可能含警告）' -ForegroundColor Green }
        1 { Write-Host '测试完成：存在 FAIL 项，请查看报告' -ForegroundColor Red }
        default { Write-Host "测试进程退出码：$code（可能构建失败或环境不满足）" -ForegroundColor Yellow }
    }

    $report = Get-RecentReportPath
    if ($report) {
        Write-Host "报告：$report" -ForegroundColor White
        if ($interactive) {
            $open = Read-Host '输入 O 打开报告，直接回车返回菜单'
            if ($open -and $open.Trim().ToLowerInvariant() -eq 'o') { Open-Report $report }
        }
    } else {
        Write-Host '未找到报告文件。' -ForegroundColor Yellow
        if ($interactive) { [void](Read-Host '按回车键返回菜单') }
    }
}

while ($true) {
    Show-Banner
    $choice = Read-Host '请输入序号'
    if ($null -eq $choice) { break }
    $choice = $choice.Trim()

    switch ($choice) {
        '1' { Invoke-Suite -argsList @('-Quick') -label '快速模式' -injectsInput $true }
        '2' { Invoke-Suite -argsList @('-Full') -label '完整模式' -injectsInput $true }
        '3' { Invoke-Suite -argsList @('-Quick', '-NoClick') -label '快速模式（不注入键鼠）' -injectsInput $false }
        '4' { Invoke-Suite -argsList @('-Full', '-NoClick') -label '完整模式（不注入键鼠）' -injectsInput $false }
        '5' { Invoke-Suite -argsList @('-Quick', '-UpdateBaseline') -label '更新性能基线' -injectsInput $true }
        '6' {
            $report = Get-RecentReportPath
            if ($report) {
                Write-Host ''
                Write-Host "最近报告：$report" -ForegroundColor White
                if ($interactive) {
                    $open = Read-Host '输入 O 打开报告，直接回车返回菜单'
                    if ($open -and $open.Trim().ToLowerInvariant() -eq 'o') { Open-Report $report }
                }
            } else {
                Write-Host ''
                Write-Host '还没有测试报告，请先运行一次测试。' -ForegroundColor Yellow
                if ($interactive) { [void](Read-Host '按回车键返回菜单') }
            }
        }
        '0' { break }
        '' { }
        default {
            Write-Host '无效输入，请输入 0 到 6 的数字。' -ForegroundColor Yellow
            Start-Sleep -Milliseconds 800
        }
    }
    if ($choice -eq '0') { break }
}

if ($interactive) { Write-Host ''; Write-Host '已退出。' -ForegroundColor Gray }
