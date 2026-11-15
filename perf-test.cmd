@echo off
rem KeyboardManager performance test launcher.
rem  - Double-click (no arguments): shows an interactive mode menu.
rem  - With arguments: forwards them directly to scripts\perf-suite.ps1 (CI/CLI).
rem Resolves all paths relative to this file, so it works from any directory.
setlocal
set "ROOT=%~dp0"
set "SUITE=%ROOT%scripts\perf-suite.ps1"
set "MENU=%ROOT%scripts\perf-test-menu.ps1"

if not exist "%SUITE%" (
  echo [ERROR] perf-suite.ps1 not found: "%SUITE%"
  echo         Make sure this launcher stays next to the "scripts" folder.
  exit /b 2
)

if not "%~1"=="" goto cli

if not exist "%MENU%" (
  echo [ERROR] perf-test-menu.ps1 not found: "%MENU%"
  exit /b 2
)
title KeyboardManager Performance Test
powershell -NoProfile -ExecutionPolicy Bypass -File "%MENU%"
set "RC=%ERRORLEVEL%"
echo.
pause
exit /b %RC%

:cli
powershell -NoProfile -ExecutionPolicy Bypass -File "%SUITE%" %*
exit /b %ERRORLEVEL%
