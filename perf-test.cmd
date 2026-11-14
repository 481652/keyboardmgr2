@echo off
rem KeyboardManager performance suite launcher.
rem Works from any working directory: resolves the suite relative to this file.
setlocal
set "PS1=%~dp0scripts\perf-suite.ps1"
if not exist "%PS1%" (
  echo [ERROR] perf-suite.ps1 not found: "%PS1%"
  echo         Make sure this launcher stays next to the "scripts" folder.
  exit /b 2
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%PS1%" %*
exit /b %ERRORLEVEL%
