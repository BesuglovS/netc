param(
    [Parameter(Mandatory = $true)][string]$Targets,
    [string]$User,
    [string]$Password,
    [string]$SharedKey
)
# Лёгкое обновление УЖЕ установленных ПК: синхронизирует файлы приложения (агент + сторож).
#
# Оптимизации (время на ПК):
#   * удалённый каталог перечисляется ОДНИМ обходом, а не сотнями вызовов Get-Item;
#   * сторож (`netc_watchdog`) останавливается/запускается ТОЛЬКО если изменился его код
#     (NetcServer.Watchdog.dll или NetcShared.dll) — иначе экономится ~43 с на ПК;
#   * жёсткие паузы заменены опросом: restart-agent сам ретраит, порт 7801 ждём до открытия;
#   * если менять нечего — ПК не перезапускается вовсе (агент не дёргаем).
#
# Учётка берётся из deploy\secrets.psd1 / переменных окружения / параметров (см. deploy\_common.ps1).
# Для установки на НОВЫЙ ПК (полная раскладка) используйте DeployPc.ps1.

$ErrorActionPreference = "Continue"
. (Join-Path $PSScriptRoot '_common.ps1')
$cfg = Get-NetcDeployConfig -User $User -Password $Password -SharedKey $SharedKey
$user = $cfg.User; $pass = $cfg.Password
if (-not $user -or -not $pass) { throw "Не заданы учётные данные: заполните deploy\secrets.psd1 (см. secrets.example.psd1) или передайте -User/-Password." }
$targetList = @($Targets.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
$src = 'C:\admin\files\dist\agent'
$srcLen = $src.Length
$noBom = New-Object System.Text.UTF8Encoding($false)
$srcFiles = Get-ChildItem $src -File -Recurse

# Файлы, при изменении которых требуется перезапуск СЛУЖБЫ сторожа.
# (NetcServer.Watchdog.exe — только apphost/метаданные версии, его менять можно без остановки службы.)
$watchdogCritical = @('NetcServer.Watchdog.dll', 'NetcShared.dll')

function Test-AppFile([string]$name) { return $name -match '^(NetcServer\.|NetcShared\.)' }

function ScCmd([string]$ip, [string]$verb, [int]$timeoutSec = 40) {
    $out = [IO.Path]::GetTempFileName(); $err = [IO.Path]::GetTempFileName()
    try {
        $p = Start-Process -FilePath 'sc.exe' -ArgumentList @("\\$ip", $verb, 'netc_watchdog') `
            -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
        if (-not $p.WaitForExit($timeoutSec * 1000)) { try { $p.Kill() } catch { }; return 'TIMEOUT' }
        return ((Get-Content $out -Raw -ErrorAction SilentlyContinue) + (Get-Content $err -Raw -ErrorAction SilentlyContinue))
    }
    catch { return 'ERROR' }
    finally { Remove-Item $out, $err -Force -ErrorAction SilentlyContinue }
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
        $tc = New-Object System.Net.Sockets.TcpClient
        try { $ar = $tc.BeginConnect($ip, 7801, $null, $null); if ($ar.AsyncWaitHandle.WaitOne(1000)) { $tc.EndConnect($ar); return $true } } catch { } finally { $tc.Close() }
        Start-Sleep -Milliseconds 400
    }
    return $false
}

# Копирует файл, при занятости целевого переименовывает его в *.old. Возвращает $true при успехе.
function Copy-One([string]$full, [string]$dst) {
    $dir = Split-Path $dst -Parent
    if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    try { Copy-Item $full $dst -Force -ErrorAction Stop; return $true }
    catch {
        try {
            $old = $dst + '.' + (Get-Date -Format 'yyyyMMddHHmmss') + '.old'
            Rename-Item $dst $old -Force -ErrorAction Stop
            Copy-Item $full $dst -Force -ErrorAction Stop
            return $true
        }
        catch { return $false }
    }
}

# Обход удалённого каталога одним вызовом: rel-path -> длина.
function Get-RemoteMap([string]$inst) {
    $map = @{}
    try {
        $instLen = $inst.Length
        Get-ChildItem $inst -Recurse -File -ErrorAction Stop | ForEach-Object {
            $rel = $_.FullName.Substring($instLen).TrimStart('\')
            $map[$rel] = [long]$_.Length
        }
    } catch { }
    return $map
}

$refAgent = ''; $refCore = ''
try { $refAgent = (Get-FileHash (Join-Path $src 'NetcServer.Agent.dll')).Hash.Substring(0, 8) } catch { }
try { $refCore  = (Get-FileHash (Join-Path $src 'NetcServer.Core.dll')).Hash.Substring(0, 8) } catch { }

$total = $targetList.Count
$okCount = 0; $badCount = 0; $offline = 0; $skippedNoop = 0; $wdRestarts = 0
$runStart = Get-Date
Write-NetcLog ("▶ Раскатка netc: ПК=$total, эталон agent.dll=$refAgent core.dll=$refCore")

$idx = 0
foreach ($ip in $targetList) {
    $idx++
    $t0 = Get-Date
    $n = "[$idx/$total] $ip"

    $c = New-Object System.Net.Sockets.TcpClient; $up = $false
    try { $ar = $c.BeginConnect($ip, 445, $null, $null); if ($ar.AsyncWaitHandle.WaitOne(3000)) { $c.EndConnect($ar); $up = $true } } catch { } finally { $c.Close() }
    if (-not $up) { $offline++; Write-NetcLog "$n OFFLINE (нет 445)"; continue }

    try { $np = Start-Process 'net.exe' -ArgumentList @('use', "\\$ip\IPC$", "/user:$ip\$user", $pass) -NoNewWindow -PassThru; if (-not $np.WaitForExit(12000)) { try { $np.Kill() } catch { } } } catch { }

    $inst = "\\$ip\C`$\Program Files\netc"
    if (-not (Test-Path $inst)) { $offline++; Write-NetcLog "$n NO INSTALL DIR"; continue }

    # 1) Один обход удалённого каталога.
    $remote = Get-RemoteMap $inst

    # 2) Нужен ли перезапуск службы сторожа? (только если изменился её код)
    $wdChanged = $false
    foreach ($wn in $watchdogCritical) {
        $lp = Join-Path $src $wn
        if (-not (Test-Path $lp)) { continue }
        if (-not $remote.ContainsKey($wn) -or [long]$remote[$wn] -ne (Get-Item $lp).Length) { $wdChanged = $true; break }
        if ((Get-FileHash (Join-Path $inst $wn)).Hash -ne (Get-FileHash $lp).Hash) { $wdChanged = $true; break }
    }

    # 3) Остановить сторожа — только при необходимости.
    $stopped = $false; $tStop = 0.0
    if ($wdChanged) {
        $s = [Diagnostics.Stopwatch]::StartNew(); ScCmd $ip 'stop' 40 | Out-Null; $tStop = $s.Elapsed.TotalSeconds
        Start-Sleep -Milliseconds 800
        $stopped = $true
        $wdRestarts++
    }

    # 4) Синхронизация файлов (копируем изменившееся; занятые — через *.old).
    $tCopy = [Diagnostics.Stopwatch]::StartNew()
    $copied = @(); $bad = @()
    foreach ($f in $srcFiles) {
        $rel = $f.FullName.Substring($srcLen).TrimStart('\')
        $dst = Join-Path $inst $rel
        $dl = if ($remote.ContainsKey($rel)) { [long]$remote[$rel] } else { -1 }
        $need = $false
        if ($dl -lt 0) { $need = $true }
        elseif ($dl -ne $f.Length) { $need = $true }
        elseif (Test-AppFile $f.Name) {
            # ВАЖНО: явный вложенный if. В комбинированном «Test-AppFile $f.Name -and (Get-FileHash ...)»
            # PowerShell вычисляет Get-FileHash для КАЖДОГО файла и даёт ложные срабатывания
            # (проверено: 80 с и 14 «изменений» вместо 2 с и 0).
            if ((Get-FileHash $dst).Hash -ne (Get-FileHash $f.FullName).Hash) { $need = $true }
        }
        if ($need) { if (Copy-One $f.FullName $dst) { $copied += $rel } else { $bad += $rel } }
    }
    $tCopy.Stop()

    # 5) Запустить сторожа обратно, если останавливали.
    $tStart = 0.0
    if ($stopped) { $s = [Diagnostics.Stopwatch]::StartNew(); ScCmd $ip 'start' 40 | Out-Null; $tStart = $s.Elapsed.TotalSeconds }

    # 6) Активировать новую сборку агента — только если что-то меняли.
    $restart = 'skip'; $tRestart = 0.0
    if ($copied.Count -gt 0) {
        $s = [Diagnostics.Stopwatch]::StartNew(); $restart = Restart-Agent $ip; $tRestart = $s.Elapsed.TotalSeconds
    } else {
        $skippedNoop++
    }

    # 7) Проверка: порт агента (ждём открытия) + хэши agent/core.
    $port = Wait-Port $ip 20
    $h1 = "n/a"; $hc1 = "n/a"
    try { $h1 = (Get-FileHash (Join-Path $inst 'NetcServer.Agent.dll')).Hash.Substring(0, 8) } catch { }
    try { $hc1 = (Get-FileHash (Join-Path $inst 'NetcServer.Core.dll')).Hash.Substring(0, 8) } catch { }

    $coreOk = ($hc1 -eq $refCore); $agentOk = ($h1 -eq $refAgent)
    $ok = ($bad.Count -eq 0) -and $agentOk -and $coreOk -and $port
    if ($ok) { $okCount++ } else { $badCount++ }

    $sec = [int]((Get-Date) - $t0).TotalSeconds
    Write-NetcLog ("{0} files={1} copied={2} wd={3} restart={4} port={5} agent={6}{7} core={8}{9} ({10}с: stop={11:N1} copy={12:N1} start={13:N1} restart={14:N1})" -f `
        $n, $(if ($bad.Count -eq 0) { 'OK' } else { 'BAD:' + ($bad -join ',') }), $copied.Count, $(if ($wdChanged) { 'restart' } else { 'keep' }),
        $restart, $(if ($port) { 'open' } else { 'CLOSED' }),
        $h1, $(if ($agentOk) { '' } else { '(!)' }), $hc1, $(if ($coreOk) { '' } else { '(!)' }),
        $sec, $tStop, $tCopy.Elapsed.TotalSeconds, $tStart, $tRestart)
}

$secTotal = [int]((Get-Date) - $runStart).TotalSeconds
Write-NetcLog ("■ Готово: ok=$okCount, проблемы=$badCount, офлайн=$offline, без изменений=$skippedNoop, рестарт сторожа=$wdRestarts, всего=$total (за $secTotal с)")
Write-NetcLog "UPDATE-DONE"
