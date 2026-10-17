param(
    [Parameter(Mandatory = $true)][string]$Targets
)
# Развёртывание netc на ПК: копирование сборки + установка официальным install-agent.ps1
# через временную службу, запускающую установщик отсоединённым процессом (RPC не нужен).
$ErrorActionPreference = "Continue"
$targetList = @($Targets.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
$user = "MNU"; $pass = "***REMOVED-SECRET***"
$src = 'C:\admin\files\dist\agent'
$noBom = New-Object System.Text.UTF8Encoding($false)
$svcName = 'netc_setup_run'

$stage = Join-Path $env:TEMP 'netc_setup_stage'
if (-not (Test-Path (Join-Path $stage 'NetcServer.Agent.exe'))) {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    robocopy $src $stage /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    Copy-Item 'C:\admin\files\netc_server\install\install-agent.ps1' (Join-Path $stage 'install-agent.ps1') -Force
}
$wrapper = @"
@echo off
echo wrapper-start > C:\netc_setup\wrapper.txt
start "netc_setup" /min cmd /c "powershell -NoProfile -ExecutionPolicy Bypass -File C:\netc_setup\install-agent.ps1 -Source C:\netc_setup -SharedKey netc-default-key > C:\netc_setup\install.log 2>&1 & echo %ERRORLEVEL% > C:\netc_setup\install.done"
"@
Set-Content -Path (Join-Path $stage 'run-install.cmd') -Value $wrapper -Encoding ASCII
$srcHash = (Get-FileHash (Join-Path $src 'NetcServer.Agent.dll')).Hash.Substring(0, 8)
Write-Host ("staging: {0} (agent.dll {1})" -f $stage, $srcHash)

function Send-RestartAgent([string]$ip) {
    $pdir = "\\$ip\C`$\ProgramData\netc\personalize"
    New-Item -ItemType Directory -Force -Path $pdir -ErrorAction SilentlyContinue | Out-Null
    for ($a = 1; $a -le 4; $a++) {
        $id = [guid]::NewGuid().ToString('N')
        $req = Join-Path $pdir ("req-" + $id + ".json"); $res = Join-Path $pdir ("res-" + $id + ".json"); $tmp = $req + ".tmp"
        try { [IO.File]::WriteAllText($tmp, '{"action":"restart-agent"}', $noBom); Start-Sleep -Milliseconds 300; [IO.File]::Move($tmp, $req) } catch { continue }
        $m = ''
        for ($i = 0; $i -lt 40; $i++) { if (Test-Path $res) { $m = (Get-Content $res -Raw); Remove-Item $res -Force -ErrorAction SilentlyContinue; break }; Start-Sleep -Milliseconds 500 }
        if ($m -match 'ok.{0,4}true') { return 'ok' }
        Start-Sleep -Seconds 2
    }
    return 'NO RESPONSE'
}

foreach ($ip in $targetList) {
    $c = New-Object System.Net.Sockets.TcpClient; $up = $false
    try { $ar = $c.BeginConnect($ip, 445, $null, $null); if ($ar.AsyncWaitHandle.WaitOne(3000)) { $c.EndConnect($ar); $up = $true } } catch { } finally { $c.Close() }
    if (-not $up) { Write-Host ("[{0,-15}] OFFLINE (нет 445)" -f $ip); continue }

    try { $np = Start-Process 'net.exe' -ArgumentList @('use', "\\$ip\IPC$", "/user:$ip\$user", $pass) -NoNewWindow -PassThru; if (-not $np.WaitForExit(12000)) { try { $np.Kill() } catch { } } } catch { }

    $setup = "\\$ip\C`$\netc_setup"
    Remove-Item $setup -Recurse -Force -ErrorAction SilentlyContinue
    robocopy $stage $setup /E /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { Write-Host ("[{0,-15}] НЕТ ДОСТУПА к C$ (rc={1})" -f $ip, $LASTEXITCODE); continue }

    $done = $false
    for ($attempt = 1; $attempt -le 4; $attempt++) {
        & sc.exe "\\$ip" delete $svcName 2>&1 | Out-Null
        Start-Sleep -Seconds 1
        & sc.exe "\\$ip" create $svcName binPath= "cmd.exe /c C:\netc_setup\run-install.cmd" start= demand type= own 2>&1 | Out-Null
        & sc.exe "\\$ip" start $svcName 2>&1 | Out-Null
        for ($i = 0; $i -lt 60; $i++) { if (Test-Path (Join-Path $setup 'install.done')) { $done = $true; break }; Start-Sleep -Seconds 2 }
        if ($done) { break }
        Write-Host ("[{0}] попытка {1} установки не удалась, повтор..." -f $ip, $attempt)
    }
    & sc.exe "\\$ip" delete $svcName 2>&1 | Out-Null

    $tail = ''
    try { $log = Get-Content (Join-Path $setup 'install.log') -Raw -ErrorAction SilentlyContinue; $tail = ($log -split "`n" | Where-Object { $_.Trim() -ne '' } | Select-Object -Last 2) -join ' | ' } catch { }

    $restart = Send-RestartAgent $ip
    Start-Sleep -Seconds 6

    $port = $false
    $tc = New-Object System.Net.Sockets.TcpClient
    try { $ar2 = $tc.BeginConnect($ip, 7801, $null, $null); if ($ar2.AsyncWaitHandle.WaitOne(4000)) { $tc.EndConnect($ar2); $port = $true } } catch { } finally { $tc.Close() }
    $hash = 'n/a'; try { $hash = (Get-FileHash ("\\$ip\C`$\Program Files\netc\NetcServer.Agent.dll")).Hash.Substring(0, 8) } catch { }
    $svc = '?'; if ((& sc.exe "\\$ip" query netc_watchdog 2>&1) -match 'RUNNING') { $svc = 'RUNNING' } else { $svc = 'STOPPED/?' }

    if ($done) { Remove-Item $setup -Recurse -Force -ErrorAction SilentlyContinue }

    Write-Host ("[{0,-15}] done={1} dll={2}/{3} port7801={4} watchdog={5} restart={6}" -f $ip, $done, $hash, $srcHash, $(if ($port) { 'open' } else { 'CLOSED' }), $svc, $restart)
    if ($tail) { Write-Host ("                 log: " + $tail) }
}
Write-Host "DEPLOY-DONE"
