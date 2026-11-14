# App startup, idle, and clean-exit smoke checks.
param(
    [switch]$Quick,
    [int]$Runs = 3
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$perfDir = Join-Path $rootDir '.perf'
$scriptsDir = Join-Path $perfDir 'scripts'
$startupProbe = Join-Path $scriptsDir 'startup-probe.ps1'
$idleProbe = Join-Path $scriptsDir 'idle-probe.ps1'
$exitCheck = Join-Path $scriptDir 'exit-check.ps1'

Write-Host '=== keyboardmgr2 smoke test ===' -ForegroundColor Cyan
Write-Host "Quick mode: $Quick"

if (Test-Path -LiteralPath $startupProbe) {
    Write-Host "[1/3] Startup probe (Runs=$Runs)..." -ForegroundColor Yellow
    & powershell -ExecutionPolicy Bypass -File $startupProbe -Runs $Runs
    if ($LASTEXITCODE -ne 0) { throw 'Startup probe failed.' }
} else {
    Write-Host '[1/3] Startup probe skipped (not found).' -ForegroundColor DarkYellow
}

if (-not $Quick -and (Test-Path -LiteralPath $idleProbe)) {
    Write-Host '[2/3] Idle probe...' -ForegroundColor Yellow
    & powershell -ExecutionPolicy Bypass -File $idleProbe -WindowSeconds 60 -TraySeconds 60
    if ($LASTEXITCODE -ne 0) { throw 'Idle probe failed.' }
} else {
    Write-Host '[2/3] Idle probe skipped.' -ForegroundColor Gray
}

if (Test-Path -LiteralPath $exitCheck) {
    Write-Host '[3/3] Exit residue check...' -ForegroundColor Yellow
    & powershell -ExecutionPolicy Bypass -File $exitCheck
    if ($LASTEXITCODE -ne 0) { throw 'Exit residue check failed.' }
} else {
    Write-Host '[3/3] Exit check skipped (not found).' -ForegroundColor DarkYellow
}

Write-Host '=== Smoke test passed ===' -ForegroundColor Green
