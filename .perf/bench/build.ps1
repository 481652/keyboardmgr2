# ============================================================================
# keyboardmgr2 performance benchmark - standalone build script
# Writes only under .perf\bench and .perf\data. Does NOT touch the main
# project sources/vbproj/sln and does NOT start keyboardmgr2.exe.
#
# Outputs (all in .perf\bench\bin):
#   KbmrBench.Managed.dll - built from REAL sources OpCoreManaged.vb,
#                           UserInputHandler.vb, GlobalMouseHook.vb
#                           (only OpEngine is a 2-member shim)
#   KbmrBench.Data.dll    - built from REAL sources KbmrCodec.vb, MacroModel.vb,
#                           RecordingStore.vb, KeyNameMapper.vb, SettingsModule.vb
#                           (only DlgModule.ShowExpdlg is a shim)
#   bench-click.exe       - C# WinForms click/key injection benchmark
#   bench-data.exe        - C# data-path micro benchmark
#   OpCore64.dll          - copy of the native core (read-only copy for P/Invoke)
# ============================================================================
param(
    [switch]$SkipClick,
    [switch]$SkipData
)

$ErrorActionPreference = 'Stop'

$Bench  = $PSScriptRoot
$Root   = Split-Path (Split-Path $Bench -Parent) -Parent
$Module = Join-Path $Root 'keyboardmgr2\Modules'
$Bin    = Join-Path $Bench 'bin'
$LogDir = Join-Path $Bench 'logs'
$RefDir = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.7.2'
$Roslyn = 'E:\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\Roslyn'
$Csc = Join-Path $Roslyn 'csc.exe'
$Vbc = Join-Path $Roslyn 'vbc.exe'

New-Item -ItemType Directory -Force -Path $Bin, $LogDir | Out-Null

foreach ($tool in @($Csc, $Vbc)) {
    if (-not (Test-Path $tool)) { throw "compiler not found: $tool" }
}
if (-not (Test-Path $RefDir)) { throw "reference assemblies not found: $RefDir" }

# .NET Framework 4.7.2 reference assemblies (explicit refs; bypass Roslyn rsp)
$AllRefs = @(
    'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Drawing.dll',
    'System.Windows.Forms.dll', 'Microsoft.VisualBasic.dll', 'System.Xml.dll',
    'System.Xaml.dll', 'WindowsBase.dll', 'PresentationCore.dll',
    'PresentationFramework.dll', 'UIAutomationClient.dll', 'UIAutomationTypes.dll',
    'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll'
)
$RefArgs = @()
foreach ($r in $AllRefs) {
    $p = Join-Path $RefDir $r
    if (-not (Test-Path $p)) { throw "reference assembly missing: $p" }
    $RefArgs += "/r:$p"
}

function Invoke-Compile {
    param(
        [string]$Compiler,
        [string[]]$Arguments,
        [string]$LogName
    )
    $log = Join-Path $LogDir $LogName
    $all = @($Arguments)
    Write-Host ">> compile -> $LogName"
    $out = & $Compiler @all 2>&1
    $code = $LASTEXITCODE
    $out | Out-File -FilePath $log -Encoding utf8
    if ($out) { $out | ForEach-Object { Write-Host $_ } }
    if ($code -ne 0) { throw "compile failed ($code), log: $log" }
    Write-Host "   OK"
}

# ---------------------------------------------------------------------------
# 1) managed kernel assembly: real sources + OpEngine shim only
# ---------------------------------------------------------------------------
if (-not $SkipClick) {
    $managedSrc = @(
        (Join-Path $Module 'OpCoreManaged.vb'),
    (Join-Path $Module 'HighResolutionWaiter.vb'),
        (Join-Path $Module 'UserInputHandler.vb'),
        (Join-Path $Root 'keyboardmgr2\GlobalMouseHook.vb'),
        (Join-Path $Bench 'src\managed\OpEngineShim.vb'),
        (Join-Path $Bench 'src\managed\ManagedBridge.vb')
    )
    foreach ($s in $managedSrc) { if (-not (Test-Path $s)) { throw "source missing: $s" } }
    $managedImports = 'Microsoft.VisualBasic,System,System.Collections,System.Collections.Generic,System.Diagnostics,System.Linq,System.Threading.Tasks,System.Windows,System.Windows.Controls,System.Windows.Input,System.Windows.Automation'
    $managedArgs = @(
        '/noconfig', '/nologo', '/codepage:65001', '/target:library', '/platform:AnyCPU', '/optimize+', '/debug-',
        '/optionstrict-', '/optionexplicit+', '/optioninfer+',
        "/out:$(Join-Path $Bin 'KbmrBench.Managed.dll')",
        "/imports:$managedImports"
    ) + $RefArgs + $managedSrc
    Invoke-Compile -Compiler $Vbc -Arguments $managedArgs -LogName 'build-managed.log'

    # -----------------------------------------------------------------------
    # 2) C# click/key injection benchmark (x64, WinForms)
    # -----------------------------------------------------------------------
    $clickArgs = @(
        '/noconfig', '/nologo', '/codepage:65001', '/target:exe', '/platform:x64', '/optimize+', '/debug-',
        '/langversion:latest',
        "/out:$(Join-Path $Bin 'bench-click.exe')",
        "/r:$(Join-Path $Bin 'KbmrBench.Managed.dll')"
    ) + $RefArgs + @((Join-Path $Bench 'src\click\Program.cs'))
    Invoke-Compile -Compiler $Csc -Arguments $clickArgs -LogName 'build-click.log'
}

# ---------------------------------------------------------------------------
# 3) data-path assembly: real sources + DlgModule shim only
# ---------------------------------------------------------------------------
if (-not $SkipData) {
    $dataSrc = @(
        (Join-Path $Module 'MacroModel.vb'),
        (Join-Path $Module 'KbmrCodec.vb'),
        (Join-Path $Module 'ScreenMetrics.vb'),
        (Join-Path $Module 'RecordingStore.vb'),
        (Join-Path $Module 'KeyNameMapper.vb'),
        (Join-Path $Module 'SettingsModule.vb'),
        (Join-Path $Bench 'src\data\DlgShim.vb'),
        (Join-Path $Bench 'src\data\DataBridge.vb')
    )
    foreach ($s in $dataSrc) { if (-not (Test-Path $s)) { throw "source missing: $s" } }
    $dataImports = 'Microsoft.VisualBasic,System,System.Collections,System.Collections.Generic,System.Diagnostics,System.Linq,System.Windows.Input'
    $dataArgs = @(
        '/noconfig', '/nologo', '/codepage:65001', '/target:library', '/platform:AnyCPU', '/optimize+', '/debug-',
        '/optionstrict-', '/optionexplicit+', '/optioninfer+',
        "/out:$(Join-Path $Bin 'KbmrBench.Data.dll')",
        "/imports:$dataImports"
    ) + $RefArgs + $dataSrc
    Invoke-Compile -Compiler $Vbc -Arguments $dataArgs -LogName 'build-data.log'

    # -----------------------------------------------------------------------
    # 4) C# data-path benchmark
    # -----------------------------------------------------------------------
    $dataBenchArgs = @(
        '/noconfig', '/nologo', '/codepage:65001', '/target:exe', '/platform:AnyCPU', '/optimize+', '/debug-',
        '/langversion:latest',
        "/out:$(Join-Path $Bin 'bench-data.exe')",
        "/r:$(Join-Path $Bin 'KbmrBench.Data.dll')"
    ) + $RefArgs + @((Join-Path $Bench 'src\data\Program.cs'))
    Invoke-Compile -Compiler $Csc -Arguments $dataBenchArgs -LogName 'build-databench.log'
}

# ---------------------------------------------------------------------------
# 5) copy native core DLL (read-only from main bin)
# ---------------------------------------------------------------------------
$native = Join-Path $Root 'keyboardmgr2\bin\Release\OpCore64.dll'
if (Test-Path $native) {
    Copy-Item -Force $native (Join-Path $Bin 'OpCore64.dll')
    Write-Host ">> copied OpCore64.dll ($((Get-Item $native).Length) bytes)"
} else {
    throw "native core missing: $native"
}

Write-Host ''
Write-Host '=== build ok ==='
Get-ChildItem $Bin | Select-Object Name, Length | Format-Table -AutoSize | Out-String -Width 120 | Write-Host
