# Сборка (публикация) комплекса netc.
#   dist\agent   — автономный агент + сторож  (self-contained, папкой)
#   dist\service — служба                     (self-contained, single-file, опционально)
#   dist\client  — клиент учителя             (self-contained, single-file)
#
# Self-contained: .NET на ученических ПК ставить НЕ нужно — рантайм лежит рядом с приложением.
# Сторож публикуется ПАПКОЙ, а не single-file: тогда при обновлении копируются только
# изменившиеся файлы приложения (десятки-сотни КБ), а не весь рантайм.

[CmdletBinding()]
param(
    [string]$Dotnet,
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# Поиск dotnet: параметр -> %USERPROFILE%\.dotnet -> %DOTNET_ROOT% -> Program Files -> PATH.
if (-not $Dotnet) {
    $try = @()
    if ($env:USERPROFILE) { $try += (Join-Path $env:USERPROFILE '.dotnet\dotnet.exe') }
    if ($env:DOTNET_ROOT) { $try += (Join-Path $env:DOTNET_ROOT 'dotnet.exe') }
    if ($env:ProgramFiles) { $try += (Join-Path $env:ProgramFiles 'dotnet\dotnet.exe') }
    $Dotnet = $try | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $Dotnet) {
        $cmd = Get-Command dotnet -ErrorAction SilentlyContinue
        if ($cmd) { $Dotnet = $cmd.Source }
    }
}
if (-not $Dotnet -or -not (Test-Path $Dotnet)) { throw "Не найден dotnet. Укажите -Dotnet <путь к dotnet.exe>." }

$base = @("-c", "Release", "-r", $Runtime, "--self-contained", "true")

# Чистим выходные папки, чтобы не остались файлы прошлой (framework-dependent) публикации.
# dist\client не чистим — рядом с exe лежит netc_pcs.txt (список ПК клиента).
foreach ($d in @("agent", "service")) {
    $p = Join-Path $root "dist\$d"
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

Write-Host "Публикация автономного агента (self-contained, папкой)…"
& $Dotnet publish (Join-Path $root "netc_server\NetcServer.Agent\NetcServer.Agent.csproj") @base -p:PublishSingleFile=false -o (Join-Path $root "dist\agent")

Write-Host "Публикация службы (self-contained, single-file, опционально)…"
& $Dotnet publish (Join-Path $root "netc_server\NetcServer.Service\NetcServer.Service.csproj") @base -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root "dist\service")

Write-Host "Публикация сторожевой службы (self-contained, ПАПКОЙ, рядом с агентом)…"
& $Dotnet publish (Join-Path $root "netc_server\NetcServer.Watchdog\NetcServer.Watchdog.csproj") @base -p:PublishSingleFile=false -o (Join-Path $root "dist\agent")

Write-Host "Публикация клиента учителя (self-contained, single-file)…"
& $Dotnet publish (Join-Path $root "netc_client\NetcClient.csproj") @base -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root "dist\client")

# Пакет локальной установки (для ПК без сети/удалённого доступа): свежая сборка + install-скрипты.
Write-Host "Сборка пакета локальной установки (dist\agent_setup)…"
$setup = Join-Path $root "dist\agent_setup"
Remove-Item $setup -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $setup | Out-Null
& robocopy (Join-Path $root "dist\agent") $setup /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
$install = Join-Path $root "netc_server\install"
foreach ($f in @("install-agent.ps1", "install.cmd", "set-power.ps1", "set-power.cmd", "uninstall.ps1")) {
    $p = Join-Path $install $f
    if (Test-Path $p) { Copy-Item $p $setup -Force }
}
# К списку ПК (если он есть рядом с клиентом) — кладём и в пакет установки, чтобы он был единым источником.
$ipsSrc = Join-Path $root "ips.txt"
if (Test-Path $ipsSrc) { Copy-Item $ipsSrc $setup -Force }

Write-Host ""
Write-Host "Готово: $root\dist" -ForegroundColor Green
