#Requires -Version 5.1
<#
    Установка netc_server на ученический ПК (запускать локально от администратора).
    Копирует файлы, настраивает netc.json, регистрирует службу, правила брандмауэра
    и автозапуск трей-агента в сессии пользователя.

    Пример:
      powershell -ExecutionPolicy Bypass -File .\install.ps1 -Source .\ -SharedKey "my-secret"
#>
[CmdletBinding()]
param(
    [string]$Source = $PSScriptRoot,
    [string]$InstallDir = "C:\Program Files\netc",
    [string]$SharedKey = "netc-default-key",
    [int]$ControlPort = 7801,
    [int]$DiscoveryPort = 7802,
    [string]$ScreenPort = "7803"
)

$ErrorActionPreference = "Stop"

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Запустите от имени администратора." }

function Find-File([string]$name) {
    $p = Join-Path $Source $name
    if (Test-Path $p) { return $p }
    return $null
}

$svcExe = Find-File "NetcServer.Service.exe"
$agentExe = Find-File "NetcServer.Agent.exe"
if (-not $svcExe) { throw "Не найден NetcServer.Service.exe в $Source (сначала соберите build.ps1)." }
if (-not $agentExe) { throw "Не найден NetcServer.Agent.exe в $Source." }

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
Copy-Item $svcExe (Join-Path $InstallDir "NetcServer.Service.exe") -Force
Copy-Item $agentExe (Join-Path $InstallDir "NetcServer.Agent.exe") -Force

# Конфигурация (общий ключ и порты)
$cfg = @{ sharedKey = $SharedKey; controlPort = $ControlPort; discoveryPort = $DiscoveryPort }
($cfg | ConvertTo-Json) | Set-Content -Path (Join-Path $InstallDir "netc.json") -Encoding UTF8
Write-Host "Конфигурация записана: netc.json"

# Служба
$svcPath = Join-Path $InstallDir "NetcServer.Service.exe"
if (Get-Service -Name "netc_server" -ErrorAction SilentlyContinue) {
    Stop-Service -Name "netc_server" -Force -ErrorAction SilentlyContinue
    sc.exe delete netc_server | Out-Null
    Start-Sleep -Seconds 1
}
New-Service -Name "netc_server" -BinaryPathName ('"' + $svcPath + '"') `
    -DisplayName "netc_server (управление классом)" -StartupType Automatic | Out-Null
Start-Service -Name "netc_server"
Write-Host "Служба netc_server установлена и запущена."

# Брандмауэр
$rules = @(
    @{ n = "netc_server TCP"; proto = "TCP"; ports = "$ControlPort,$ScreenPort" },
    @{ n = "netc_server UDP"; proto = "UDP"; ports = "$DiscoveryPort" }
)
foreach ($r in $rules) {
    if (-not (Get-NetFirewallRule -DisplayName $r.n -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $r.n -Direction Inbound -Action Allow `
            -Protocol $r.proto -LocalPort $r.ports -Profile Any | Out-Null
        Write-Host ("Правило брандмауэра: " + $r.n)
    }
}

# Автозапуск трей-агента в сессии пользователя
$agentPath = Join-Path $InstallDir "NetcServer.Agent.exe"
New-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "netc_agent" `
    -Value ('"' + $agentPath + '"') -PropertyType String -Force | Out-Null
Write-Host "Трей-агент добавлен в автозапуск (HKLM Run)."

Write-Host ""
Write-Host "Готово. netc_server установлен." -ForegroundColor Green
Write-Host "Проверка с ПК учителя: Test-NetConnection $env:COMPUTERNAME -Port $ControlPort"
