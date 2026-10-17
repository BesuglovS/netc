# Сборка (публикация) комплекса netc.
#   dist\agent   — автономный агент + сторож  (framework-dependent)
#   dist\service — служба                     (framework-dependent)
#   dist\client  — клиент учителя             (self-contained, single-file)
#
# ВАЖНО: агент, сторож и служба публикуются framework-dependent — на ученических ПК
# требуется .NET 10 Desktop Runtime (устанавливает install-agent.ps1; установщик для
# офлайн-раскладки готовит deploy\fetch-dotnet-runtime.ps1).

[CmdletBinding()]
param(
    [string]$Dotnet = "C:\Users\NexTouch\.dotnet\dotnet.exe",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not (Test-Path $Dotnet)) { throw "Не найден dotnet: $Dotnet" }

$fx = @("-c", "Release", "-r", $Runtime, "--self-contained", "false")
$sc = @("-c", "Release", "-r", $Runtime, "--self-contained", "true")

# Чистим выходные папки, чтобы не остались файлы прошлой (self-contained) публикации.
# dist\client не чистим — рядом с exe лежит netc_pcs.txt (список ПК клиента).
foreach ($d in @("agent", "service")) {
    $p = Join-Path $root "dist\$d"
    if (Test-Path $p) { Remove-Item $p -Recurse -Force }
}

Write-Host "Публикация автономного агента (framework-dependent, папкой)…"
& $Dotnet publish (Join-Path $root "netc_server\NetcServer.Agent\NetcServer.Agent.csproj") @fx -p:PublishSingleFile=false -o (Join-Path $root "dist\agent")

Write-Host "Публикация службы (framework-dependent, single-file)…"
& $Dotnet publish (Join-Path $root "netc_server\NetcServer.Service\NetcServer.Service.csproj") @fx -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root "dist\service")

Write-Host "Публикация сторожевой службы (framework-dependent, single-file, рядом с агентом)…"
& $Dotnet publish (Join-Path $root "netc_server\NetcServer.Watchdog\NetcServer.Watchdog.csproj") @fx -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root "dist\agent")

Write-Host "Публикация клиента учителя (self-contained, single-file)…"
& $Dotnet publish (Join-Path $root "netc_client\NetcClient.csproj") @sc -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o (Join-Path $root "dist\client")

Write-Host ""
Write-Host "Готово: $root\dist" -ForegroundColor Green
