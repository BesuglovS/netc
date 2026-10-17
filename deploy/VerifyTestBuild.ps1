param(
    [Parameter(Mandatory = $true)][string]$Targets,
    [string]$User,
    [string]$Password,
    [int]$Throttle = 24
)
$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot '_common.ps1')
$cfg = Get-NetcDeployConfig -User $User -Password $Password
$user = $cfg.User; $pass = $cfg.Password
if (-not $user -or -not $pass) { throw "Не заданы учётные данные: заполните deploy\secrets.psd1 (см. secrets.example.psd1) или передайте -User/-Password." }
$targetList = @($Targets.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })

$refAgent = (Get-FileHash 'C:\admin\files\dist\agent\NetcServer.Agent.dll').Hash.Substring(0, 8)
$refCore  = (Get-FileHash 'C:\admin\files\dist\agent\NetcServer.Core.dll').Hash.Substring(0, 8)

$runStart = Get-Date
Write-NetcLog ("▶ Проверка сборки (параллельно, потоков $Throttle): эталон agent.dll=$refAgent core.dll=$refCore")

# Обработчик одного ПК (в своём runspace). Возвращает объект результата.
$worker = {
    param($Item, $user, $pass, $refAgent, $refCore)
    $ip = $Item
    $r = [pscustomobject]@{ ip = $ip; online = $false; agent = 'n/a'; core = 'n/a'; port = $false; svc = '?'; error = '' }

    function TcpOpen([string]$ip, [int]$port, [int]$ms) {
        $c = New-Object System.Net.Sockets.TcpClient
        try { $ar = $c.BeginConnect($ip, $port, $null, $null); if ($ar.AsyncWaitHandle.WaitOne($ms)) { try { $c.EndConnect($ar); return $true } catch { return $false } }; return $false }
        catch { return $false } finally { $c.Close() }
    }

    try {
        if (-not (TcpOpen $ip 445 3000)) { return $r }
        $r.online = $true

        try { $np = Start-Process 'net.exe' -ArgumentList @('use', "\\$ip\IPC$", "/user:$ip\$user", $pass) -NoNewWindow -PassThru; if (-not $np.WaitForExit(12000)) { try { $np.Kill() } catch { } } } catch { }

        $base = "\\$ip\C`$\Program Files\netc"
        try { $r.agent = (Get-FileHash (Join-Path $base 'NetcServer.Agent.dll')).Hash.Substring(0, 8) } catch { }
        try { $r.core  = (Get-FileHash (Join-Path $base 'NetcServer.Core.dll')).Hash.Substring(0, 8) } catch { }

        $r.port = TcpOpen $ip 7801 3000

        try {
            $o = [IO.Path]::GetTempFileName(); $e = [IO.Path]::GetTempFileName()
            $p = Start-Process 'sc.exe' -ArgumentList @("\\$ip", 'query', 'netc_watchdog') -NoNewWindow -PassThru -RedirectStandardOutput $o -RedirectStandardError $e
            if ($p.WaitForExit(60000)) {
                $t = (Get-Content $o -Raw -ErrorAction SilentlyContinue)
                if ($t -match 'RUNNING') { $r.svc = 'RUNNING' } elseif ($t -match 'STOPPED') { $r.svc = 'STOPPED' }
            } else { try { $p.Kill() } catch { }; $r.svc = 'timeout' }
            Remove-Item $o, $e -Force -ErrorAction SilentlyContinue
        } catch { }
    }
    catch { $r.error = $_.Exception.Message }
    return $r
}

$ok = 0; $i = 0
Invoke-NetcParallel -Items $targetList -Worker $worker -Throttle $Throttle `
    -Params @{ user = $user; pass = $pass; refAgent = $refAgent; refCore = $refCore } | ForEach-Object {
    $r = $_
    $i++
    if (-not $r.online) { Write-NetcLog ("[{0}/{1}] {2} OFFLINE" -f $i, $targetList.Count, $r.ip); return }
    $agentOk = ($r.agent -eq $refAgent)
    $coreOk  = ($r.core  -eq $refCore)
    if ($agentOk -and $coreOk -and $r.port -and $r.svc -eq 'RUNNING') { $ok++ }
    $tail = if ($r.error) { "  ошибка: " + $r.error } else { "" }
    Write-NetcLog ("[{0}/{1}] {2} agent={3} {4} core={5} {6} port7801={7,-7} watchdog={8}{9}" -f `
        $i, $targetList.Count, $r.ip, $r.agent, $(if ($agentOk) { 'OK ' } else { 'BAD' }), `
        $r.core, $(if ($coreOk) { 'OK ' } else { 'BAD' }), $(if ($r.port) { 'open' } else { 'CLOSED' }), $r.svc, $tail)
}

$secTotal = [int]((Get-Date) - $runStart).TotalSeconds
Write-NetcLog ("■ Итог: ok=$ok/$($targetList.Count) (за $secTotal с)")
Write-NetcLog "VERIFY-DONE"
