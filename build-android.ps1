# Сборка Android-приложения учителя (netc_android).
#   dist\android\NetcAndroid.apk  — подписанный APK для установки на планшет
#
# Пример использования:
#   powershell -ExecutionPolicy Bypass -File .\build-android.ps1
#   powershell -ExecutionPolicy Bypass -File .\build-android.ps1 -Install -Device b4ecb873

[CmdletBinding()]
param(
    [string]$Dotnet,
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",

    # Серийный номер планшета (adb devices). Пусто — единственное подключённое устройство.
    [string]$Device = "",

    # Установить APK на планшет после сборки.
    [switch]$Install
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

Write-Host "Сборка netc_android ($Configuration)…"
& $Dotnet build (Join-Path $root "netc_android\NetcAndroid.csproj") -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Сборка не удалась" }

$bin = Join-Path $root "netc_android\bin\$Configuration\net10.0-android"
$apk = Get-ChildItem $bin -Filter "*-Signed.apk" -ErrorAction SilentlyContinue | Select-Object -First 1
if ($null -eq $apk) { throw "Не найден подписанный APK в $bin" }

$dest = Join-Path $root "dist\android"
New-Item -ItemType Directory -Force -Path $dest | Out-Null
$outApk = Join-Path $dest "NetcAndroid.apk"
Copy-Item $apk.FullName $outApk -Force
Write-Host "Готово: $outApk" -ForegroundColor Green

if ($Install) {
    $adb = Join-Path $env:LOCALAPPDATA "Android\Sdk\platform-tools\adb.exe"
    if (-not (Test-Path $adb)) { throw "Не найден adb: $adb" }

    $target = @()
    if ($Device) { $target = @("-s", $Device) }

    Write-Host "Установка на планшет…"
    & $adb @target install -r $outApk
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Установка не удалась. На планшете включите «Для разработчиков» → «Установка через USB»."
    }
}
