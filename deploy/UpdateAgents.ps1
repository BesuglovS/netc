param(
    [Parameter(Mandatory = $true)][string]$Targets,
    [string]$User,
    [string]$Password,
    [string]$SharedKey
)
# Лёгкое обновление УЖЕ установленных ПК: синхронизирует файлы приложения (агент + сторож).
# Сборка self-contained, папкой — обновление весит сотни КБ (рантайм на ПК уже есть).
#
# Копируются файлы, которых нет в целевой папке или размер которых отличается (конфиг netc.json
# и установочные скрипты не затрагиваются). Последовательность: стоп сторожа -> копирование
# (занятые файлы переименовываются в *.old) -> старт сторожа -> restart-agent (без перезагрузки).
#
# Учётка берётся из deploy\secrets.psd1 / переменных окружения / параметров (см. deploy\_common.ps1).
#
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

function Copy-Needed([string]$full, [string]$dst) {
    # Одинаковый размер не значит одинаковое содержимое (пример: NetcServer.Core.dll меняется,
    # но размер тот же) — для файлов приложения сверяем ещё и хэш. Остальное (рантайм .NET)
    # пропускаем по размеру, иначе каждый прогон хэширует сотни неизменных файлов.
    $name = Split-Path $full -Leaf
    try {
        if (Test-Path $dst) {
            if ((Get-Item $dst).Length -eq (Get-Item $full).Length) {
                if ($name -notmatch '^(NetcServer\.|NetcShared\.)') { return $true }
                if ((Get-FileHash $dst).Hash -eq (Get-FileHash $full).Hash) { return $true }
            }
        }
    } catch { }
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

function ScCmd([string]$ip, [string]$verb, [int]$timeoutSec = 40) {
    $out = [IO.Path]::GetTempFileName(); $err = [IO.Path]::GetTempFileName()
    try {
        $p = Start-Process -FilePath 'sc.exe' -ArgumentList @("\\$ip", $verb, 'netc_watchdog') `
            -NoNewWindow -PassThru -RedirectStandardOutput $out -RedirectStandardError $err
        if (-not $p.WaitForExit($timeoutSec * 1000)) { try { $p.Kill() } catch { } ; return 'TIMEOUT' }
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
        Start-Sleep -Seconds 3
    }
    return 'NO RESPONSE'
}

foreach ($ip in $targetList) {
    $c = New-Object System.Net.Sockets.TcpClient; $up = $false
    try { $ar = $c.BeginConnect($ip, 445, $null, $null); if ($ar.AsyncWaitHandle.WaitOne(3000)) { $c.EndConnect($ar); $up = $true } } catch { } finally { $c.Close() }
    if (-not $up) { Write-Host "[$ip] OFFLINE"; continue }

    try { $np = Start-Process 'net.exe' -ArgumentList @('use', "\\$ip\IPC$", "/user:$ip\$user", $pass) -NoNewWindow -PassThru; if (-not $np.WaitForExit(12000)) { try { $np.Kill() } catch { } } } catch { }

    $inst = "\\$ip\C`$\Program Files\netc"
    if (-not (Test-Path $inst)) { Write-Host "[$ip] NO INSTALL DIR"; continue }

    # 1) Остановить сторожа (освобождает его файлы).
    ScCmd $ip 'stop' 40 | Out-Null
    Start-Sleep -Seconds 2

    # 2) Синхронизировать файлы приложения (отсутствующие/изменившиеся).
    $bad = @()
    foreach ($f in $srcFiles) {
        $rel = $f.FullName.Substring($srcLen).TrimStart('\')
        if (-not (Copy-Needed $f.FullName (Join-Path $inst $rel))) { $bad += $rel }
    }

    # 3) Запустить сторожа и дождаться запуска (перед restart-agent).
    ScCmd $ip 'start' 40 | Out-Null
    Start-Sleep -Seconds 10

    # 4) Активировать новую сборку агента (без перезагрузки), с повторами.
    $restart = Restart-Agent $ip
    Start-Sleep -Seconds 6

    # 5) Проверка: порт агента и совпадение хэша.
    $port = $false
    $tc = New-Object System.Net.Sockets.TcpClient
    try { $ar = $tc.BeginConnect($ip, 7801, $null, $null); if ($ar.AsyncWaitHandle.WaitOne(4000)) { $tc.EndConnect($ar); $port = $true } } catch { } finally { $tc.Close() }

    $h1 = ""; $h2 = "x"
    try { $h1 = (Get-FileHash (Join-Path $inst 'NetcServer.Agent.dll')).Hash.Substring(0, 8) } catch { }
    try { $h2 = (Get-FileHash (Join-Path $src 'NetcServer.Agent.dll')).Hash.Substring(0, 8) } catch { }

    Write-Host ("[{0,-15}] files={1} restart={2} port7801={3} agent.dll {4}/{5}" -f $ip,
        $(if ($bad.Count -eq 0) { 'OK' } else { 'BAD:' + ($bad -join ',') }),
        $restart,
        $(if ($port) { 'open' } else { 'CLOSED' }), $h1, $h2)
}
Write-Host "UPDATE-DONE"
