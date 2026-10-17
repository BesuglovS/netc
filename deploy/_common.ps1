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
