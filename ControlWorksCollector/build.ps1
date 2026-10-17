#Requires -Version 5.1
<#
    Сборка ControlWorksCollector.exe.

    Компилятор — Roslyn (csc.exe) из Build Tools или Visual Studio, с запасным
    вариантом: встроенный в Windows csc.exe (.NET Framework 4.x).

    Все исходники лежат в .\src, результат — в .\bin\ControlWorksCollector.exe.
    Цель — .NET Framework 4.8, который предустановлен в Windows 10/11,
    поэтому готовый exe запускается без установки дополнительных компонентов.
#>
[CmdletBinding()]
param(
    [switch]$Run
)

$ErrorActionPreference = "Stop"

$root    = $PSScriptRoot
$srcDir  = Join-Path $root "src"
$binDir  = Join-Path $root "bin"
$outExe  = Join-Path $binDir "ControlWorksCollector.exe"
$manifest = Join-Path $root "app.manifest"

# 1. Ищем компилятор (сначала современный Roslyn, затем встроенный).
$candidates = @(
    "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe",
    "C:\Program Files (x86)\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
    "C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe",
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
)
$csc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $csc) {
    throw "Не найден компилятор csc.exe. Установите .NET Framework (есть в Windows) или Build Tools."
}
Write-Host "Компилятор: $csc"

# 2. Ссылки на сборки .NET Framework.
$fw = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319"
$refNames = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    "System.Web.Extensions.dll"
)
$refs = @()
foreach ($name in $refNames) {
    $path = Join-Path $fw $name
    if (-not (Test-Path $path)) { throw "Не найдена сборка $path" }
    $refs += "-r:$path"
}

# 3. Список исходников.
if (-not (Test-Path $srcDir)) { throw "Не найден каталог $srcDir" }
$sources = @(Get-ChildItem -Path $srcDir -Recurse -Filter *.cs | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "В $srcDir нет .cs файлов." }

if (-not (Test-Path $binDir)) { New-Item -ItemType Directory -Path $binDir | Out-Null }

# 4. Компиляция.
$cscArgs = @(
    "-nologo",
    "-target:winexe",
    "-platform:anycpu",
    "-langversion:latest",
    "-codepage:65001",
    "-optimize+",
    "-win32manifest:$manifest",
    "-out:$outExe"
) + $refs + $sources

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) {
    throw "Сборка не удалась (код $LASTEXITCODE)."
}

Write-Host ""
Write-Host "Готово: $outExe" -ForegroundColor Green

if ($Run) {
    Start-Process $outExe
}
