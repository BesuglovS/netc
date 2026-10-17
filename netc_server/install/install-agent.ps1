#Requires -Version 5.1
<#
    Установка АВТОНОМНОГО агента netc_server (без службы) на ученический ПК.
    Запускать локально от администратора. Агент запускается в сессии пользователя
    (трей) и сам держит сетевой сервер (TCP/UDP).

    Пример:
      powershell -ExecutionPolicy Bypass -File .\install-agent.ps1 -Source .\ -SharedKey "my-secret"
#>
[CmdletBinding()]
param(
    [string]$Source = $PSScriptRoot,
    [string]$InstallDir = "C:\Program Files\netc",
    [string]$SharedKey = "netc-default-key",
    [int]$ControlPort = 7801,
    [int]$DiscoveryPort = 7802,
    [int]$ScreenPort = 7803
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Source)) { $Source = $PSScriptRoot }
$Source = $Source.TrimEnd('\', '/')

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Запустите от имени администратора." }

$agentExe = Join-Path $Source "NetcServer.Agent.exe"
if (-not (Test-Path $agentExe)) { throw "Не найден NetcServer.Agent.exe в $Source (сначала соберите build.ps1)." }

# Останавливаем уже работающие сторож и агент: при переустановке поверх их файлы в InstallDir
# заняты (NetcServer.Watchdog.exe/Agent.dll), и копирование иначе падает.
if (Get-Service -Name "netc_watchdog" -ErrorAction SilentlyContinue) {
    Stop-Service -Name "netc_watchdog" -Force -ErrorAction SilentlyContinue
    # Ждём остановки и УДАЛЯЕМ службу: пока служба существует, её exe занят и копирование падает;
    # удаление также не даёт ей перезапуститься во время копирования.
    for ($i = 0; $i -lt 30; $i++) {
        $s = Get-Service -Name "netc_watchdog" -ErrorAction SilentlyContinue
        if (-not $s -or $s.Status -eq 'Stopped') { break }
        Start-Sleep -Milliseconds 500
    }
    sc.exe delete netc_watchdog | Out-Null
    Start-Sleep -Seconds 2
}
Get-Process -Name "NetcServer.Agent" -ErrorAction SilentlyContinue | ForEach-Object {
    try { $_.Kill(); $_.WaitForExit(5000) } catch { }
}
Start-Sleep -Seconds 1

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
# Зеркалируем Source в InstallDir (/MIR): убирает файлы прошлых сборок (в т.ч. остатки другого
# типа публикации) — иначе в папке может остаться лишнее.
robocopy $Source $InstallDir /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Копирование файлов в $InstallDir не удалось (robocopy rc=$LASTEXITCODE)." }

$cfg = @{ sharedKey = $SharedKey; controlPort = $ControlPort; discoveryPort = $DiscoveryPort }
($cfg | ConvertTo-Json) | Set-Content -Path (Join-Path $InstallDir "netc.json") -Encoding UTF8
Write-Host "Конфигурация записана: netc.json"

# Брандмауэр (отдельное правило на каждый порт — -LocalPort принимает массив)
$rules = @(
    @{ n = "netc_server TCP 7801"; proto = "TCP"; ports = @($ControlPort) },
    @{ n = "netc_server TCP 7803"; proto = "TCP"; ports = @($ScreenPort) },
    @{ n = "netc_server UDP 7802"; proto = "UDP"; ports = @($DiscoveryPort) }
)
foreach ($r in $rules) {
    if (-not (Get-NetFirewallRule -DisplayName $r.n -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $r.n -Direction Inbound -Action Allow -Protocol $r.proto -LocalPort $r.ports -Profile Any | Out-Null
        Write-Host ("Правило брандмауэра: " + $r.n)
    }
}

# Питание: не отключать дисплей и не уходить в сон (от сети)
powercfg /change monitor-timeout-ac 0 2>$null
powercfg /change standby-timeout-ac 0 2>$null
Write-Host "Настройки питания: дисплей и сон = Никогда (от сети)."

# Автозапуск в сессии пользователя
$agentPath = Join-Path $InstallDir "NetcServer.Agent.exe"
New-ItemProperty -Path "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name "netc_agent" -Value ('"' + $agentPath + '"') -PropertyType String -Force | Out-Null
Write-Host "Агент добавлен в автозапуск (HKLM Run)."

# Сторожевая служба: перезапускает агента, если ученик его закрыл (работает от SYSTEM)
$wdExe = Join-Path $InstallDir "NetcServer.Watchdog.exe"
if (Test-Path $wdExe) {
    if (Get-Service -Name "netc_watchdog" -ErrorAction SilentlyContinue) {
        Stop-Service -Name "netc_watchdog" -Force -ErrorAction SilentlyContinue
        sc.exe delete netc_watchdog | Out-Null
    }
    # Ждём исчезновения прежней службы: сразу после удаления она бывает «помечена на удаление»,
    # и New-Service на то же имя падает.
    for ($i = 0; $i -lt 30; $i++) {
        if (-not (Get-Service -Name "netc_watchdog" -ErrorAction SilentlyContinue)) { break }
        Start-Sleep -Milliseconds 500
    }
    $created = $false
    for ($i = 0; $i -lt 15 -and -not $created; $i++) {
        try {
            New-Service -Name "netc_watchdog" -BinaryPathName ('"' + $wdExe + '"') `
                -DisplayName "netc_watchdog (сторож агента)" -StartupType Automatic -ErrorAction Stop | Out-Null
            $created = $true
        }
        catch { Start-Sleep -Seconds 1 }
    }
    if (-not $created) { throw "Не удалось создать службу netc_watchdog." }
    # Дожидаемся RUNNING (Start-Service может вернуть раньше, чем служба реально поднимется).
    try { Start-Service -Name "netc_watchdog" -ErrorAction SilentlyContinue } catch { }
    $svcStatus = 'неизвестно'
    for ($i = 0; $i -lt 60; $i++) {
        $svc = Get-Service -Name "netc_watchdog" -ErrorAction SilentlyContinue
        if ($svc) { $svcStatus = $svc.Status; if ($svc.Status -eq 'Running') { break } }
        Start-Sleep -Milliseconds 500
    }
    if ($svcStatus -eq 'Running') { Write-Host "Сторожевая служба netc_watchdog запущена." }
    else { Write-Host ("netc_watchdog: состояние " + $svcStatus) }
}
else {
    Write-Host "NetcServer.Watchdog.exe не найден — сторож не установлен."
}

# Запустить сейчас (в текущей сессии), если ещё не запущен
if (-not (Get-Process -Name "NetcServer.Agent" -ErrorAction SilentlyContinue)) {
    Start-Process -FilePath $agentPath
    Write-Host "Агент запущен."
}

Write-Host ""
Write-Host "Готово. Автономный агент netc_server установлен." -ForegroundColor Green
Write-Host "Проверка с ПК учителя: Test-NetConnection $env:COMPUTERNAME -Port $ControlPort"
