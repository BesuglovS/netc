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
    [string]$Dotnet = "C:\Users\NexTouch\.dotnet\dotnet.exe",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not (Test-Path $Dotnet)) { throw "Не найден dotnet: $Dotnet" }

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

Write-Host ""
Write-Host "Готово: $root\dist" -ForegroundColor Green
