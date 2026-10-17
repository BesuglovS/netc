@echo off
rem netc: open network access on a student PC (SMB 445/139, ICMP, Server service).
rem Run this file on the target PC; it self-elevates to administrator.
setlocal
chcp 65001 >nul
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator rights...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0enable-remote-access.ps1"
echo.
pause
