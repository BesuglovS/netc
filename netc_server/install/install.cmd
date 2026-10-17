@echo off
chcp 65001 >nul
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-agent.ps1" -SharedKey "netc-default-key"
if errorlevel 1 (
  echo.
  echo ERROR: install failed. See messages above.
) else (
  echo.
  echo OK: agent installed.
)
pause >nul
