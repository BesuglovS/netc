# Сборка (публикация) комплекса netc.
#   dist\agent   — автономный агент (папкой, self-contained, без службы)
#   dist\service — служба (опционально, для сред, где разрешён запуск служб)
#   dist\client  — клиент учителя (single-file)

[CmdletBinding()]
param(
    [string]$Dotnet = "C:\Users\NexTouch\.dotnet\dotnet.exe",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not (Test-Path $Dotnet)) { throw "Не найден dotnet: $Dotnet" }

$base = @("-c", "Release", "-r", $Runtime, "--self-contained", "true")

Write-Host "Публикация автономного агента (папкой)…"
& $Dotnet publish (Join-Path $root "netc_server\NetcServer.Agent\NetcServer.Agent.csproj") @base -p:PublishSingleFile=false -o (Join-Path $root "dist\agent")

Write-Host "Публикация службы (single-file, опционально)…"
& $Dotnet publish (Join-Path $root "netc_server\NetcServer.Service\NetcServer.Service.csproj") @base -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root "dist\service")

Write-Host "Публикация сторожевой службы (single-file, рядом с агентом)…"
& $Dotnet publish (Join-Path $root "netc_server\NetcServer.Watchdog\NetcServer.Watchdog.csproj") @base -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root "dist\agent")

Write-Host "Публикация клиента учителя (single-file)…"
& $Dotnet publish (Join-Path $root "netc_client\NetcClient.csproj") @base -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root "dist\client")

Write-Host ""
Write-Host "Готово: $root\dist" -ForegroundColor Green
