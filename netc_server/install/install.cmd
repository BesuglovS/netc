@echo off
chcp 65001 >nul
rem ============================================================
rem  Установка агента netc. Ключ протокола:
rem    * если задан NETC_SHARED_KEY — используется он;
rem    * иначе предлагается ввести ключ (Enter — ключ по умолчанию).
rem  Значение должно совпадать с ключом на остальных ПК класса.
rem ============================================================
set "KEY=%NETC_SHARED_KEY%"
if "%KEY%"=="" set /p KEY=Общий ключ netc (Enter = netc-default-key):
if "%KEY%"=="" set "KEY=netc-default-key"

powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-agent.ps1" -SharedKey "%KEY%"
if errorlevel 1 (
  echo.
  echo ERROR: install failed. See messages above.
) else (
  echo.
  echo OK: agent installed.
)
pause >nul
