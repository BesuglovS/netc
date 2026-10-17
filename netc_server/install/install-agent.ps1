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
    [int]$ScreenPort = 7803,
    [string]$DotnetInstaller = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Source)) { $Source = $PSScriptRoot }
$Source = $Source.TrimEnd('\', '/')

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) { throw "Запустите от имени администратора." }

# --- .NET 10 Desktop Runtime: требуется framework-dependent сборке агента/сторожа ---
function Test-DotnetDesktopInstalled {
    $shared = Join-Path ${env:ProgramFiles} 'dotnet\shared\Microsoft.WindowsDesktop.App'
    if (-not (Test-Path $shared)) { return $false }
    return [bool](Get-ChildItem $shared -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '10.*' } | Select-Object -First 1)
}

function Resolve-DotnetInstaller {
    if ($DotnetInstaller -and (Test-Path $DotnetInstaller)) { return $DotnetInstaller }
    foreach ($d in @((Join-Path $Source 'dotnet'), $PSScriptRoot)) {
        if (Test-Path $d) {
            $f = Get-ChildItem $d -Filter 'windowsdesktop-runtime-*.exe' -ErrorAction SilentlyContinue |
                Sort-Object Name -Descending | Select-Object -First 1
            if ($f) { return $f.FullName }
        }
    }
    return ''
}

function Ensure-DotnetDesktop {
    if (Test-DotnetDesktopInstalled) { Write-Host ".NET 10 Desktop Runtime: уже установлен."; return }

    $inst = Resolve-DotnetInstaller
    if (-not $inst) {
        $inst = Join-Path $env:TEMP 'windowsdesktop-runtime-10.0.13-win-x64.exe'
        $url = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.13/windowsdesktop-runtime-10.0.13-win-x64.exe'
        Write-Host "Установщик .NET рядом не найден — скачиваю: $url"
        Invoke-WebRequest -Uri $url -OutFile $inst -UseBasicParsing
    }

    Write-Host ("Установка .NET 10 Desktop Runtime: " + $inst)
    $proc = Start-Process -FilePath $inst -ArgumentList '/install', '/quiet', '/norestart' -Wait -PassThru
    Write-Host ("Код установщика .NET: " + $proc.ExitCode)
    if (-not (Test-DotnetDesktopInstalled)) {
        throw "Не удалось установить .NET 10 Desktop Runtime (код $($proc.ExitCode)). Установите рантайм вручную и повторите."
    }
}

Ensure-DotnetDesktop

$agentExe = Join-Path $Source "NetcServer.Agent.exe"
if (-not (Test-Path $agentExe)) { throw "Не найден NetcServer.Agent.exe в $Source (сначала соберите build.ps1)." }

New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
# Копируем содержимое Source в InstallDir, кроме служебной папки `dotnet` (там может лежать
# установщик рантайма — его в Program Files класть не нужно).
robocopy $Source $InstallDir /E /XD dotnet /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
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
        Start-Sleep -Seconds 1
    }
    New-Service -Name "netc_watchdog" -BinaryPathName ('"' + $wdExe + '"') `
        -DisplayName "netc_watchdog (сторож агента)" -StartupType Automatic | Out-Null
    try {
        Start-Service -Name "netc_watchdog"
        Write-Host "Сторожевая служба netc_watchdog запущена."
    }
    catch {
        Write-Host ("Не удалось запустить netc_watchdog: " + $_.Exception.Message)
    }
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
