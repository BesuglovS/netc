param(
    [Parameter(Mandatory = $true)][string]$Targets,
    [string]$User,
    [string]$Password
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
Write-NetcLog ("▶ Проверка сборки: эталон agent.dll=$refAgent core.dll=$refCore")
$ok = 0
$idx = 0

foreach ($ip in $targetList) {
    $idx++
    $n = "[$idx/$($targetList.Count)] $ip"
    $c = New-Object System.Net.Sockets.TcpClient; $up = $false
    try { $ar = $c.BeginConnect($ip, 445, $null, $null); if ($ar.AsyncWaitHandle.WaitOne(3000)) { $c.EndConnect($ar); $up = $true } } catch { } finally { $c.Close() }
    if (-not $up) { Write-NetcLog ("$n OFFLINE"); continue }

    try { $np = Start-Process 'net.exe' -ArgumentList @('use', "\\$ip\IPC$", "/user:$ip\$user", $pass) -NoNewWindow -PassThru; if (-not $np.WaitForExit(12000)) { try { $np.Kill() } catch { } } } catch { }

    $hash = "n/a"; $core = "n/a"
    try { $hash = (Get-FileHash ("\\$ip\C`$\Program Files\netc\NetcServer.Agent.dll")).Hash.Substring(0, 8) } catch { }
    try { $core = (Get-FileHash ("\\$ip\C`$\Program Files\netc\NetcServer.Core.dll")).Hash.Substring(0, 8) } catch { }

    $port = $false
    $tc = New-Object System.Net.Sockets.TcpClient
    try { $ar2 = $tc.BeginConnect($ip, 7801, $null, $null); if ($ar2.AsyncWaitHandle.WaitOne(3000)) { $tc.EndConnect($ar2); $port = $true } } catch { } finally { $tc.Close() }

    $svc = "?"
    try {
        $sout = [IO.Path]::GetTempFileName(); $serr = [IO.Path]::GetTempFileName()
        $p = Start-Process sc.exe -ArgumentList @("\\$ip", 'query', 'netc_watchdog') -NoNewWindow -PassThru -RedirectStandardOutput $sout -RedirectStandardError $serr
        if ($p.WaitForExit(60000)) { $t = Get-Content $sout -Raw; if ($t -match 'RUNNING') { $svc = 'RUNNING' } elseif ($t -match 'STOPPED') { $svc = 'STOPPED' } } else { try { $p.Kill() } catch { }; $svc = 'timeout' }
        Remove-Item $sout, $serr -Force -ErrorAction SilentlyContinue
    } catch { }

    $agentOk = ($hash -eq $refAgent)
    $coreOk  = ($core -eq $refCore)
    if ($agentOk -and $coreOk -and $port -and $svc -eq 'RUNNING') { $ok++ }
    Write-NetcLog ("{0} agent={1} {2} core={3} {4} port7801={5,-7} watchdog={6}" -f $n, $hash,
        $(if ($agentOk) { 'OK ' } else { 'BAD' }), $core, $(if ($coreOk) { 'OK ' } else { 'BAD' }),
        $(if ($port) { 'open' } else { 'CLOSED' }), $svc)
}
$secTotal = [int]((Get-Date) - $runStart).TotalSeconds
Write-NetcLog ("■ Итог: ok=$ok/$($targetList.Count) (за $secTotal с)")
Write-NetcLog "VERIFY-DONE"
