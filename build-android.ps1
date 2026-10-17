# Сборка Android-приложения учителя (netc_android).
#   dist\android\NetcAndroid.apk  — подписанный APK для установки на планшет
#
# Пример использования:
#   powershell -ExecutionPolicy Bypass -File .\build-android.ps1
#   powershell -ExecutionPolicy Bypass -File .\build-android.ps1 -Install -Device b4ecb873

[CmdletBinding()]
param(
    [string]$Dotnet = "C:\Users\NexTouch\.dotnet\dotnet.exe",
    [ValidateSet("Debug", "Release")][string]$Configuration = "Release",

    # Серийный номер планшета (adb devices). Пусто — единственное подключённое устройство.
    [string]$Device = "",

    # Установить APK на планшет после сборки.
    [switch]$Install
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
if (-not (Test-Path $Dotnet)) { throw "Не найден dotnet: $Dotnet" }

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
