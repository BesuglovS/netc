#Requires -Version 5.1
<#
    Удаление netc_server с ученического ПК (локально от администратора).
#>
[CmdletBinding()]
param(
    [string]$InstallDir = "C:\Program Files\netc"
)

$ErrorActionPreference = "Continue"
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Запустите от имени администратора." }

Get-Process -Name "NetcServer.Agent" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

# Служба-сторож (ставится install-agent.ps1) и, на всякий случай, старая служба netc_server.
foreach ($svc in @("netc_watchdog", "netc_server")) {
    if (Get-Service -Name $svc -ErrorAction SilentlyContinue) {
        Stop-Service -Name $svc -Force -ErrorAction SilentlyContinue
        sc.exe delete $svc | Out-Null
    }
}

Remove-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "netc_agent" -ErrorAction SilentlyContinue

# install-agent.ps1 создаёт правила «netc_server TCP 7801», «netc_server TCP 7803», «netc_server UDP 7802» —
# удаляем по маске (и старые «netc_server TCP»/«netc_server UDP» тоже попадают под неё).
Get-NetFirewallRule -DisplayName "netc_server*" -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue

if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir -ErrorAction SilentlyContinue }

Write-Host "netc_server удалён." -ForegroundColor Green
