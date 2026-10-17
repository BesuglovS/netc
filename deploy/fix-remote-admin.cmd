@echo off
rem netc: fix remote administrative access (run on the target PC, self-elevates).
rem Sets: Classic sharing model (ForceGuest=0) + LocalAccountTokenFilterPolicy=1.
setlocal
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo Requesting administrator rights...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)
echo ===== before =====
reg query "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" /v ForceGuest
reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v LocalAccountTokenFilterPolicy
echo.
echo ===== applying =====
reg add "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" /v ForceGuest /t REG_DWORD /d 0 /f
reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v LocalAccountTokenFilterPolicy /t REG_DWORD /d 1 /f
echo.
echo ===== after =====
reg query "HKLM\SYSTEM\CurrentControlSet\Control\Lsa" /v ForceGuest
reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System" /v LocalAccountTokenFilterPolicy
echo.
echo Done. Reboot is recommended.
pause
