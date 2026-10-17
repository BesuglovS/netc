#Requires -Version 5.1
<#
    Питание: не отключать дисплей и не уходить в спящий режим (от сети).
    Сам поднимает права администратора и пишет результат в power-result.txt рядом со скриптом.
#>
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ('"' + $PSCommandPath + '"'))
    exit
}

$log = Join-Path $PSScriptRoot "power-result.txt"
"=== set-power ===" | Set-Content -LiteralPath $log -Encoding UTF8
("время: " + (Get-Date))       | Add-Content -LiteralPath $log -Encoding UTF8
("admin: " + $isAdmin)         | Add-Content -LiteralPath $log -Encoding UTF8
("whoami: " + (whoami))        | Add-Content -LiteralPath $log -Encoding UTF8
("scheme: " + ((powercfg /getactivescheme) -join " ")) | Add-Content -LiteralPath $log -Encoding UTF8

"--- до ---" | Add-Content -LiteralPath $log -Encoding UTF8
(powercfg /query SCHEME_CURRENT SUB_VIDEO VIDEOIDLE) | Add-Content -LiteralPath $log -Encoding UTF8
(powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE) | Add-Content -LiteralPath $log -Encoding UTF8

"--- меняю ---" | Add-Content -LiteralPath $log -Encoding UTF8
(powercfg /change monitor-timeout-ac 0 2>&1) | Add-Content -LiteralPath $log -Encoding UTF8
(powercfg /change standby-timeout-ac 0 2>&1) | Add-Content -LiteralPath $log -Encoding UTF8

"--- после ---" | Add-Content -LiteralPath $log -Encoding UTF8
(powercfg /query SCHEME_CURRENT SUB_VIDEO VIDEOIDLE) | Add-Content -LiteralPath $log -Encoding UTF8
(powercfg /query SCHEME_CURRENT SUB_SLEEP STANDBYIDLE) | Add-Content -LiteralPath $log -Encoding UTF8
"=== конец ===" | Add-Content -LiteralPath $log -Encoding UTF8

Write-Host "Готово. Результат записан в $log" -ForegroundColor Green
Start-Sleep -Seconds 3
