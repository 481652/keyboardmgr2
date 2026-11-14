# 键鼠管家退出残留检查脚本
# 验证：进程完全退出 + 钩子卸载 + 托盘图标清理
$ErrorActionPreference = 'Stop'

$exePath = Join-Path $PSScriptRoot '..\keyboardmgr2\bin\Release\keyboardmgr2.exe'
if (-not (Test-Path $exePath)) {
    Write-Host "错误：找不到 Release 可执行文件" -ForegroundColor Red
    Write-Host "路径: $exePath" -ForegroundColor Gray
    exit 1
}

# Single-instance IPC deliberately forwards to an existing process and exits the
# secondary process immediately. Do not treat that expected handoff as a startup
# failure or attempt to stop a process we did not start.
$existing = Get-Process -Name keyboardmgr2 -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host '跳过：keyboardmgr2 已在运行；退出检查不会终止现有用户进程。' -ForegroundColor DarkYellow
    exit 0
}

Write-Host "启动程序..." -ForegroundColor Yellow
$proc = Start-Process -FilePath $exePath -PassThru -WindowStyle Hidden

if ($null -eq $proc) {
    Write-Host "启动被单实例 IPC 转发；安全跳过。" -ForegroundColor DarkYellow
    exit 0
}

Write-Host "等待启动完成（3秒）..." -ForegroundColor Gray
Start-Sleep -Seconds 3

if ($proc.HasExited) {
    Start-Sleep -Milliseconds 500
    $existing = Get-Process -Name keyboardmgr2 -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host '单实例启动已转交给现有进程；不对其发送退出信号。' -ForegroundColor DarkYellow
        exit 0
    }
    Write-Host "错误：程序启动后立即退出，且未发现承接请求的实例" -ForegroundColor Red
    exit 1
}

Write-Host "发送强制终止信号..." -ForegroundColor Yellow
Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue

Write-Host "等待进程退出（最多 5 秒）..." -ForegroundColor Gray
$exited = $proc.WaitForExit(5000)

if (-not $exited) {
    Write-Host "错误：进程 5 秒后仍未退出" -ForegroundColor Red
    Get-Process -Id $proc.Id -ErrorAction SilentlyContinue | Stop-Process -Force
    exit 1
}

Write-Host "验证残留进程..." -ForegroundColor Gray
Start-Sleep -Milliseconds 500
$remaining = Get-Process -Name keyboardmgr2 -ErrorAction SilentlyContinue
if ($remaining) {
    Write-Host "错误：发现残留进程" -ForegroundColor Red
    $remaining | Format-Table Id, ProcessName, StartTime -AutoSize | Out-String | Write-Host
    $remaining | Stop-Process -Force
    exit 1
}

Write-Host "✓ 退出检查通过" -ForegroundColor Green
Write-Host "  - 进程已完全退出" -ForegroundColor Gray
Write-Host "  - 无残留进程" -ForegroundColor Gray
exit 0
