# Общие настройки развёртывания netc (не хранит секреты в репозитории).
#
# Приоритет источников для учётки и общего ключа:
#   1) параметры скрипта (-User/-Password/-SharedKey);
#   2) файл deploy\secrets.psd1 (локальный, в .gitignore);
#   3) переменные окружения NETC_DEPLOY_USER / NETC_DEPLOY_PASSWORD / NETC_SHARED_KEY.
#
# Файл secrets.psd1 создаётся из secrets.example.psd1 и НЕ коммитится.

function Get-NetcDeployConfig {
    param(
        [string]$User,
        [string]$Password,
        [string]$SharedKey
    )

    $cfg = @{ User = $User; Password = $Password; SharedKey = $SharedKey }

    $file = Join-Path $PSScriptRoot 'secrets.psd1'
    if (Test-Path $file) {
        try {
            $d = Import-PowerShellDataFile -Path $file -ErrorAction Stop
            if (-not $cfg.User -and $d.User) { $cfg.User = [string]$d.User }
            if (-not $cfg.Password -and $d.Password) { $cfg.Password = [string]$d.Password }
            if (-not $cfg.SharedKey -and $d.SharedKey) { $cfg.SharedKey = [string]$d.SharedKey }
        }
        catch { Write-Warning ("Не удалось прочитать " + $file + ": " + $_.Exception.Message) }
    }

    if (-not $cfg.User) { $cfg.User = $env:NETC_DEPLOY_USER }
    if (-not $cfg.Password) { $cfg.Password = $env:NETC_DEPLOY_PASSWORD }
    if (-not $cfg.SharedKey) { $cfg.SharedKey = $env:NETC_SHARED_KEY }

    if (-not $cfg.SharedKey) { $cfg.SharedKey = 'netc-default-key' }

    return $cfg
}

# Единый журнал развёртывания: каждая строка с меткой времени [HH:mm:ss].
function Write-NetcLog {
    param([string]$Message)
    Write-Host ("[" + (Get-Date -Format 'HH:mm:ss') + "] " + $Message)
}

# Параллельный прогон одного и того же обработчика ($Worker) по списку $Items в пуле runspace-ов.
# Нужно для удалённых вызовов sc.exe: его медленный опрос (~21 с) на одном ПК перекрывается
# работой по другим ПК. $Worker должен быть самодостаточным (свои функции внутри) и принимать
# параметр $Item + всё из $Params, а возвращать объект результата.
function Invoke-NetcParallel {
    param(
        [Parameter(Mandatory = $true)][string[]]$Items,
        [Parameter(Mandatory = $true)][scriptblock]$Worker,
        [hashtable]$Params = @{},
        [int]$Throttle = 4
    )
    $pool = [runspacefactory]::CreateRunspacePool(1, [Math]::Max(1, $Throttle))
    $pool.Open()

    $jobs = New-Object System.Collections.ArrayList
    foreach ($it in $Items) {
        $ps = [powershell]::Create()
        $ps.RunspacePool = $pool
        $null = $ps.AddScript($Worker.ToString())
        $null = $ps.AddParameter('Item', $it)
        foreach ($k in $Params.Keys) { $null = $ps.AddParameter($k, $Params[$k]) }
        $null = $jobs.Add([pscustomobject]@{ Item = $it; Ps = $ps; H = $ps.BeginInvoke() })
    }

    foreach ($j in $jobs) {
        $out = $null
        try { $out = $j.Ps.EndInvoke($j.H) }
        catch {
            $out = [pscustomobject]@{ Item = $j.Item; error = $_.Exception.Message }
        }
        if ($j.Ps.HadErrors) {
            try {
                foreach ($er in $j.Ps.Streams.Error) {
                    Write-Host ("[" + (Get-Date -Format 'HH:mm:ss') + "] !! " + $j.Item + ": " + $er.ToString())
                }
            } catch { }
        }
        foreach ($o in @($out)) { if ($null -ne $o) { $o } }
        $j.Ps.Dispose()
    }

    $pool.Close()
    $pool.Dispose()
}
