param(
    [Parameter(Mandatory = $true)][string]$Targets,
    [string]$User,
    [string]$Password,
    [string]$SharedKey,
    [int]$Throttle = 6
)
# Лёгкое обновление УЖЕ установленных ПК (агент + сторож). Параллельно по ПК (RunspacePool).
#
# На каждом ПК:
#   * удалённый каталог перечисляется одним обходом;
#   * сторож останавливается/запускается ТОЛЬКО если изменился его код
#     (NetcServer.Watchdog.dll или NetcShared.dll) — иначе экономится ~43 с;
#   * приоритет — хэш (для файлов приложения), опрос готовности вместо жёстких пауз;
#   * если менять нечего — ПК не перезапускается.
#
# Параллельность перекрывает медленный удалённый sc.exe (~21 с на ПК).
#
# Учётка берётся из deploy\secrets.psd1 / переменных окружения / параметров.

$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot '_common.ps1')
$cfg = Get-NetcDeployConfig -User $User -Password $Password -SharedKey $SharedKey
$user = $cfg.User; $pass = $cfg.Password
if (-not $user -or -not $pass) { throw "Не заданы учётные данные: заполните deploy\secrets.psd1 (см. secrets.example.psd1) или передайте -User/-Password." }
$targetList = @($Targets.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
$src = 'C:\admin\files\dist\agent'
$noBom = New-Object System.Text.UTF8Encoding($false)
$watchdogCritical = @('NetcServer.Watchdog.dll', 'NetcShared.dll')

$refAgent = ''; $refCore = ''
try { $refAgent = (Get-FileHash (Join-Path $src 'NetcServer.Agent.dll')).Hash.Substring(0, 8) } catch { }
try { $refCore  = (Get-FileHash (Join-Path $src 'NetcServer.Core.dll')).Hash.Substring(0, 8) } catch { }

$runStart = Get-Date
Write-NetcLog ("▶ Раскатка netc (параллельно, потоков $Throttle): ПК=$($targetList.Count), эталон agent.dll=$refAgent core.dll=$refCore")

$worker = {
    param($Item, $user, $pass, $src, $noBom, $refAgent, $refCore, $watchdogCritical)
    $ip = $Item
    $srcLen = $src.Length
    $srcFiles = Get-ChildItem $src -File -Recurse
    $r = [pscustomobject]@{ ip = $ip; online = $false; ok = $false; copied = 0; bad = 0; wd = '-'; restart = '-'; port = $false;
                            agent = 'n/a'; core = 'n/a'; agentOk = $false; coreOk = $false;
                            sec = 0; tStop = 0.0; tCopy = 0.0; tStart = 0.0; tRestart = 0.0; error = '' }
    $t0 = Get-Date

    function TcpOpen([string]$ip, [int]$port, [int]$ms) {
        $c = New-Object System.Net.Sockets.TcpClient
        try { $ar = $c.BeginConnect($ip, $port, $null, $null); if ($ar.AsyncWaitHandle.WaitOne($ms)) { try { $c.EndConnect($ar); return $true } catch { return $false } }; return $false }
        catch { return $false } finally { $c.Close() }
    }
    function ScCmd([string]$ip, [string]$verb, [int]$timeoutSec = 40) {
        $o = [IO.Path]::GetTempFileName(); $e = [IO.Path]::GetTempFileName()
        try {
            $p = Start-Process -FilePath 'sc.exe' -ArgumentList @("\\$ip", $verb, 'netc_watchdog') -NoNewWindow -PassThru -RedirectStandardOutput $o -RedirectStandardError $e
            if (-not $p.WaitForExit($timeoutSec * 1000)) { try { $p.Kill() } catch { }; return 'TIMEOUT' }
            return ((Get-Content $o -Raw -ErrorAction SilentlyContinue) + (Get-Content $e -Raw -ErrorAction SilentlyContinue))
        } catch { return 'ERROR' } finally { Remove-Item $o, $e -Force -ErrorAction SilentlyContinue }
    }
    function Restart-Agent([string]$ip) {
        $pdir = "\\$ip\C`$\ProgramData\netc\personalize"
        New-Item -ItemType Directory -Force -Path $pdir -ErrorAction SilentlyContinue | Out-Null
        for ($a = 1; $a -le 5; $a++) {
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
    function Wait-Port([string]$ip, [int]$timeoutSec = 20) {
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($sw.Elapsed.TotalSeconds -lt $timeoutSec) {
            if (TcpOpen $ip 7801 1000) { return $true }
            Start-Sleep -Milliseconds 400
        }
        return $false
    }
    function Copy-One([string]$full, [string]$dst) {
        $dir = Split-Path $dst -Parent
        if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        try { Copy-Item $full $dst -Force -ErrorAction Stop; return $true }
        catch {
            try { $old = $dst + '.' + (Get-Date -Format 'yyyyMMddHHmmss') + '.old'; Rename-Item $dst $old -Force -ErrorAction Stop; Copy-Item $full $dst -Force -ErrorAction Stop; return $true } catch { return $false }
        }
    }
    function Get-RemoteMap([string]$inst) {
        $map = @{}
        try { $il = $inst.Length; Get-ChildItem $inst -Recurse -File -ErrorAction Stop | ForEach-Object { $map[$_.FullName.Substring($il).TrimStart('\')] = [long]$_.Length } } catch { }
        return $map
    }
    function Test-AppFile([string]$name) { return $name -match '^(NetcServer\.|NetcShared\.)' }

    try {
        if (-not (TcpOpen $ip 445 3000)) { return $r }
        $r.online = $true
        try { $np = Start-Process 'net.exe' -ArgumentList @('use', "\\$ip\IPC$", "/user:$ip\$user", $pass) -NoNewWindow -PassThru; if (-not $np.WaitForExit(12000)) { try { $np.Kill() } catch { } } } catch { }

        $inst = "\\$ip\C`$\Program Files\netc"
        if (-not (Test-Path $inst)) { $r.error = 'нет каталога установки'; return $r }

        $remote = Get-RemoteMap $inst

        # Нужен ли перезапуск службы сторожа (только если изменился её код).
        $wdChanged = $false
        foreach ($wn in $watchdogCritical) {
            $lp = Join-Path $src $wn
            if (-not (Test-Path $lp)) { continue }
            if (-not $remote.ContainsKey($wn) -or [long]$remote[$wn] -ne (Get-Item $lp).Length) { $wdChanged = $true; break }
            if ((Get-FileHash (Join-Path $inst $wn)).Hash -ne (Get-FileHash $lp).Hash) { $wdChanged = $true; break }
        }
        $r.wd = if ($wdChanged) { 'restart' } else { 'keep' }

        # Стоп сторожа — только при необходимости.
        $stopped = $false
        if ($wdChanged) {
            $s = [Diagnostics.Stopwatch]::StartNew(); ScCmd $ip 'stop' 40 | Out-Null; $r.tStop = $s.Elapsed.TotalSeconds
            Start-Sleep -Milliseconds 800; $stopped = $true
        }

        # Синхронизация файлов.
        $s = [Diagnostics.Stopwatch]::StartNew()
        $copied = @(); $bad = @()
        foreach ($f in $srcFiles) {
            $rel = $f.FullName.Substring($srcLen).TrimStart('\')
            $dst = Join-Path $inst $rel
            $dl = if ($remote.ContainsKey($rel)) { [long]$remote[$rel] } else { -1 }
            $need = $false
            if ($dl -lt 0) { $need = $true }
            elseif ($dl -ne $f.Length) { $need = $true }
            elseif (Test-AppFile $f.Name) {
                if ((Get-FileHash $dst).Hash -ne (Get-FileHash $f.FullName).Hash) { $need = $true }
            }
            if ($need) { if (Copy-One $f.FullName $dst) { $copied += $rel } else { $bad += $rel } }
        }
        $s.Stop(); $r.tCopy = $s.Elapsed.TotalSeconds
        $r.copied = $copied.Count; $r.bad = $bad.Count

        # Запуск сторожа — если останавливали.
        if ($stopped) { $s = [Diagnostics.Stopwatch]::StartNew(); ScCmd $ip 'start' 40 | Out-Null; $r.tStart = $s.Elapsed.TotalSeconds }

        # Перезапуск агента — если что-то меняли.
        if ($copied.Count -gt 0) { $s = [Diagnostics.Stopwatch]::StartNew(); $r.restart = Restart-Agent $ip; $r.tRestart = $s.Elapsed.TotalSeconds }
        else { $r.restart = 'skip' }

        $r.port = Wait-Port $ip 20
        try { $r.agent = (Get-FileHash (Join-Path $inst 'NetcServer.Agent.dll')).Hash.Substring(0, 8) } catch { }
        try { $r.core  = (Get-FileHash (Join-Path $inst 'NetcServer.Core.dll')).Hash.Substring(0, 8) } catch { }
        $r.agentOk = ($r.agent -eq $refAgent); $r.coreOk = ($r.core -eq $refCore)
        $r.ok = ($r.bad -eq 0) -and $r.agentOk -and $r.coreOk -and $r.port
    }
    catch { $r.error = $_.Exception.Message }
    $r.sec = [int]((Get-Date) - $t0).TotalSeconds
    return $r
}

$okCount = 0; $badCount = 0; $offline = 0; $skippedNoop = 0; $wdRestarts = 0; $i = 0
Invoke-NetcParallel -Items $targetList -Worker $worker -Throttle $Throttle `
    -Params @{ user = $user; pass = $pass; src = $src; noBom = $noBom; refAgent = $refAgent; refCore = $refCore; watchdogCritical = $watchdogCritical } | ForEach-Object {
    $r = $_
    $i++
    $n = "[$i/$($targetList.Count)] $($r.ip)"
    if (-not $r.online) { $offline++; Write-NetcLog "$n OFFLINE (нет 445)"; return }
    if ($r.error -and $r.agent -eq 'n/a') { $badCount++; Write-NetcLog "$n ОШИБКА: $($r.error)"; return }
    if ($r.wd -eq 'restart') { $wdRestarts++ }
    if ($r.restart -eq 'skip') { $skippedNoop++ }
    if ($r.ok) { $okCount++ } else { $badCount++ }
    Write-NetcLog ("{0} files={1} copied={2} wd={3} restart={4} port={5} agent={6}{7} core={8}{9} ({10}с: stop={11:N1} copy={12:N1} start={13:N1} restart={14:N1})" -f `
        $n, $(if ($r.bad -eq 0) { 'OK' } else { "BAD:$($r.bad)" }), $r.copied, $r.wd, $r.restart,
        $(if ($r.port) { 'open' } else { 'CLOSED' }), $r.agent, $(if ($r.agentOk) { '' } else { '(!)' }),
        $r.core, $(if ($r.coreOk) { '' } else { '(!)' }), $r.sec, $r.tStop, $r.tCopy, $r.tStart, $r.tRestart)
}

$secTotal = [int]((Get-Date) - $runStart).TotalSeconds
Write-NetcLog ("■ Готово: ok=$okCount, проблемы=$badCount, офлайн=$offline, без изменений=$skippedNoop, рестарт сторожа=$wdRestarts, всего=$($targetList.Count) (за $secTotal с)")
Write-NetcLog "UPDATE-DONE"
