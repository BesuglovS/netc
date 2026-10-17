<#
    Скачивает установщик .NET <Channel> Desktop Runtime (Windows Desktop) для офлайн-развёртывания.
    Кладёт в dist\dotnet\ и печатает путь. Проверяет SHA-512 из официального releases.json.

    Использование:
      powershell -ExecutionPolicy Bypass -File deploy\fetch-dotnet-runtime.ps1
      powershell -ExecutionPolicy Bypass -File deploy\fetch-dotnet-runtime.ps1 -Channel 10.0
#>
[CmdletBinding()]
param(
    [string]$Channel = "10.0",
    [string]$OutDir = "C:\admin\files\dist\dotnet"
)

$ErrorActionPreference = "Stop"

$metaUri = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/$Channel/releases.json"
Write-Host "Чтение $metaUri"
$meta = Invoke-RestMethod -Uri $metaUri -TimeoutSec 60
$latest = $meta.'latest-release'
$rel = $meta.releases | Where-Object { $_.'release-version' -eq $latest } | Select-Object -First 1
if (-not $rel) { throw "Не найден релиз $latest в $metaUri" }

$file = $rel.'windowsdesktop'.files |
    Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.exe' } |
    Select-Object -First 1
if (-not $file) { throw "Не найден windowsdesktop win-x64 (.exe) для $latest" }

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$out = Join-Path $OutDir $file.name

Write-Host ("Версия: {0}" -f $latest)
Write-Host ("Скачивание: {0}" -f $file.url)
Invoke-WebRequest -Uri $file.url -OutFile $out -UseBasicParsing

$sha = (Get-FileHash $out -Algorithm SHA512).Hash.ToLowerInvariant()
$expected = ($file.hash -split '\s+')[0].ToLowerInvariant()
if ($sha -ne $expected) {
    Remove-Item $out -Force -ErrorAction SilentlyContinue
    throw "SHA-512 не совпал. Ожидался $expected, получен $sha"
}

$mb = [math]::Round((Get-Item $out).Length / 1MB, 1)
Write-Host ("Проверка хэша: OK") -ForegroundColor Green
Write-Host ("Готово: {0} ({1} МБ)" -f $out, $mb) -ForegroundColor Green
