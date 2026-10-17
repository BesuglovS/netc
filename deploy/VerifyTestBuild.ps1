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
$srcHash = (Get-FileHash 'C:\admin\files\dist\agent\NetcServer.Agent.dll').Hash.Substring(0, 8)

Write-Host ("dist reference hash: " + $srcHash)
$ok = 0

foreach ($ip in $targetList) {
    $c = New-Object System.Net.Sockets.TcpClient; $up = $false
    try { $ar = $c.BeginConnect($ip, 445, $null, $null); if ($ar.AsyncWaitHandle.WaitOne(3000)) { $c.EndConnect($ar); $up = $true } } catch { } finally { $c.Close() }
    if (-not $up) { Write-Host ("[{0,-15}] OFFLINE" -f $ip); continue }

    try { $np = Start-Process 'net.exe' -ArgumentList @('use', "\\$ip\IPC$", "/user:$ip\$user", $pass) -NoNewWindow -PassThru; if (-not $np.WaitForExit(12000)) { try { $np.Kill() } catch { } } } catch { }

    $hash = "n/a"
    try { $hash = (Get-FileHash ("\\$ip\C`$\Program Files\netc\NetcServer.Agent.dll")).Hash.Substring(0, 8) } catch { }

    $port = $false
    $tc = New-Object System.Net.Sockets.TcpClient
    try { $ar2 = $tc.BeginConnect($ip, 7801, $null, $null); if ($ar2.AsyncWaitHandle.WaitOne(3000)) { $tc.EndConnect($ar2); $port = $true } } catch { } finally { $tc.Close() }

    $svc = "?"
    try {
        $sout = [IO.Path]::GetTempFileName(); $serr = [IO.Path]::GetTempFileName()
        $p = Start-Process sc.exe -ArgumentList @("\\$ip", 'query', 'netc_watchdog') -NoNewWindow -PassThru -RedirectStandardOutput $sout -RedirectStandardError $serr
        if ($p.WaitForExit(40000)) { $t = Get-Content $sout -Raw; if ($t -match 'RUNNING') { $svc = 'RUNNING' } elseif ($t -match 'STOPPED') { $svc = 'STOPPED' } }
        Remove-Item $sout, $serr -Force -ErrorAction SilentlyContinue
    } catch { }

    $match = ($hash -eq $srcHash)
    if ($match -and $port -and $svc -eq 'RUNNING') { $ok++ }
    Write-Host ("[{0,-15}] dll={1} {2}  port7801={3,-7} watchdog={4}" -f $ip, $hash,
        $(if ($match) { 'OK ' } else { 'BAD' }), $(if ($port) { 'open' } else { 'CLOSED' }), $svc)
}
Write-Host ("TOTAL ok: {0}/{1}" -f $ok, $targetList.Count)
