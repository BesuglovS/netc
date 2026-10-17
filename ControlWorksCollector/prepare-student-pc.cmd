@echo off
rem ---------------------------------------------------------------------------
rem  Preparation of a student PC for collecting works via the C$ admin share.
rem  Run on each student PC. Elevates to Administrator automatically.
rem ---------------------------------------------------------------------------
setlocal
set SCRIPT=%~dp0Prepare-StudentPc.ps1

if not exist "%SCRIPT%" (
    echo Not found: %SCRIPT%
    pause
    exit /b 1
)

echo Preparing student PC: %COMPUTERNAME%
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%" %*

echo.
echo Finished. Press any key to close.
pause >nul
endlocal
