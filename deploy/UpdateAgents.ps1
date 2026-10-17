param(
    [Parameter(Mandatory = $true)][string]$Targets
)
# Лёгкое обновление УЖЕ установленных ПК: копируем только прикладные файлы (агент + сторож),
# рантайм .NET не трогаем. Сборка self-contained, папкой — поэтому обновление весит сотни КБ.
# Последовательность: стоп сторожа -> копирование (занятые файлы переименовываются в *.old) ->
# старт сторожа -> restart-agent (активация без перезагрузки ПК).
#
# Для установки на НОВЫЙ ПК (полная раскладка) используйте DeployPc.ps1.

$ErrorActionPreference = "Continue"
$targetList = @($Targets.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne '' })
$user = "MNU"; $pass = "***REMOVED-SECRET***"
$src = 'C:\admin\files\dist\agent'
$noBom = New-Object System.Text.UTF8Encoding($false)

# Прикладные файлы: агент + сторож + зависимости сторожа (Microsoft.Extensions.*).
$files = @(
    'NetcServer.Agent.dll', 'NetcServer.Core.dll', 'NetcShared.dll',
    'NetcServer.Watchdog.exe', 'NetcServer.Watchdog.dll',
    'NetcServer.Watchdog.deps.json', 'NetcServer.Watchdog.runtimeconfig.json'
)
$files += (Get-ChildItem $src -File | Where-Object { $_.Name -like 'Microsoft.Extensions*' } | Select-Object -ExpandProperty Name)

function Update-File([string]$srcDir, [string]$dst) {
    $s = Join-Path $srcDir (Split-Path $dst -Leaf)
    try {
        if (Test-Path $dst) {
            $hs = (Get-FileHash $s -Algorithm SHA256).Hash
            $hd = (Get-FileHash $dst -Algorithm SHA256).Hash
            if ($hs -eq $hd) { return $true }   # файл уже актуален — не трогаем
        }
    }
    catch { }
    try { Copy-Item $s $dst -Force -ErrorAction Stop; return $true }
    catch {
        try {
            $old = $dst + '.' + (Get-Date -Format 'yyyyMMddHHmmss') + '.old'
            Rename-Item $dst $old -Force -ErrorAction Stop
            Copy-Item $s $dst -Force -ErrorAction Stop
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

foreach ($ip in $targetList) {
    $c = New-Object System.Net.Sockets.TcpClient; $up = $false
    try { $ar = $c.BeginConnect($ip, 445, $null, $null); if ($ar.AsyncWaitHandle.WaitOne(3000)) { $c.EndConnect($ar); $up = $true } } catch { } finally { $c.Close() }
    if (-not $up) { Write-Host "[$ip] OFFLINE"; continue }

    try { $np = Start-Process 'net.exe' -ArgumentList @('use', "\\$ip\IPC$", "/user:$ip\$user", $pass) -NoNewWindow -PassThru; if (-not $np.WaitForExit(12000)) { try { $np.Kill() } catch { } } } catch { }

    $inst = "\\$ip\C`$\Program Files\netc"
    if (-not (Test-Path $inst)) { Write-Host "[$ip] NO INSTALL DIR"; continue }

    # 1) Остановить сторожа (чтобы обновить занятые файлы).
    ScCmd $ip 'stop' 40 | Out-Null

    # 2) Обновить прикладные файлы (занятые переименовываем в *.old).
    $bad = @()
    foreach ($f in $files) { if (-not (Update-File $src (Join-Path $inst $f))) { $bad += $f } }

    # 3) Запустить сторожа.
    ScCmd $ip 'start' 40 | Out-Null

    # 4) Попросить сторожа перезапустить агента (активация новой сборки без перезагрузки).
    $pdir = "\\$ip\C`$\ProgramData\netc\personalize"
    New-Item -ItemType Directory -Force -Path $pdir -ErrorAction SilentlyContinue | Out-Null
    $id = [guid]::NewGuid().ToString('N')
    $req = Join-Path $pdir ("req-" + $id + ".json")
    $res = Join-Path $pdir ("res-" + $id + ".json")
    $tmp = $req + ".tmp"
    [IO.File]::WriteAllText($tmp, '{"action":"restart-agent"}', $noBom)
    [IO.File]::Move($tmp, $req)

    $msg = ''
    for ($i = 0; $i -lt 40; $i++) {
        if (Test-Path $res) { $msg = (Get-Content $res -Raw); Remove-Item $res -Force -ErrorAction SilentlyContinue; break }
        Start-Sleep -Milliseconds 500
    }
    Start-Sleep -Seconds 8

    # 5) Проверка: порт агента и совпадение хэша.
    $port = $false
    $tc = New-Object System.Net.Sockets.TcpClient
    try { $ar = $tc.BeginConnect($ip, 7801, $null, $null); if ($ar.AsyncWaitHandle.WaitOne(4000)) { $tc.EndConnect($ar); $port = $true } } catch { } finally { $tc.Close() }

    $h1 = ""; $h2 = "x"
    try { $h1 = (Get-FileHash (Join-Path $inst 'NetcServer.Agent.dll')).Hash.Substring(0, 8) } catch { }
    try { $h2 = (Get-FileHash (Join-Path $src 'NetcServer.Agent.dll')).Hash.Substring(0, 8) } catch { }

    Write-Host ("[{0,-15}] files={1} restart={2} port7801={3} agent.dll {4}/{5}" -f $ip,
        $(if ($bad.Count -eq 0) { 'OK' } else { 'BAD:' + ($bad -join ',') }),
        $(if ($msg) { $msg.Trim() } else { 'NO RESPONSE' }),
        $(if ($port) { 'open' } else { 'CLOSED' }), $h1, $h2)
}
Write-Host "UPDATE-DONE"
