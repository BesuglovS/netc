#Requires -Version 5.1
<#
    Подготовка ученического ПК (Windows 10/11) к сбору работ через SMB-шару C$.

    Модель работы: учитель со своего ПК подключается к админ-шаре C$ каждого
    ученического ПК и копирует папки с рабочего стола. Агент на ученике не нужен.

    Запускать НА КАЖДОМ ученическом ПК от имени администратора
    (вручную, через общий ресурс, RDP или скриптом развёртывания).

    Что делает:
      1. Включает правила брандмауэра для общего доступа к файлам (SMB 445).
      2. Запускает службу «Сервер» (LanmanServer) и ставит её в автозапуск.
      3. Проверяет наличие админ-шары C$.
      4. Показывает локальных администраторов.
      5. Разрешает локальным администраторам доступ к C$ по сети
         (LocalAccountTokenFilterPolicy=1 — обход UAC-фильтрации токена).
      6. По желанию задаёт пароль локальному администратору.
      7. Проверяет, что порт 445 слушается локально.

    Примеры:
      .\Prepare-StudentPc.ps1
      .\Prepare-StudentPc.ps1 -AdminUser Administrator -Password "P@ssw0rd!"
      .\Prepare-StudentPc.ps1 -AdminUser student-admin -Password "P@ssw0rd!" -EnableAdmin
#>
[CmdletBinding()]
param(
    [string]$AdminUser,
    [string]$Password,
    [switch]$EnableAdmin,
    [switch]$PasswordNeverExpires
)

$ErrorActionPreference = "Continue"

function Write-Section([string]$text) {
    Write-Host ""
    Write-Host ("=== " + $text + " ===") -ForegroundColor Cyan
}

function Test-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($id)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-IsAdmin)) {
    Write-Host "Требуются права администратора — перезапуск..." -ForegroundColor Yellow
    $argList = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", ('"' + $PSCommandPath + '"'))
    if ($AdminUser)           { $argList += @("-AdminUser", ('"' + $AdminUser + '"')) }
    if ($Password)            { $argList += @("-Password", ('"' + $Password + '"')) }
    if ($EnableAdmin)         { $argList += "-EnableAdmin" }
    if ($PasswordNeverExpires){ $argList += "-PasswordNeverExpires" }
    try {
        Start-Process -FilePath "powershell.exe" -Verb RunAs -ArgumentList $argList
    } catch {
        Write-Warning ("Не удалось перезапустить с правами администратора: " + $_.Exception.Message)
    }
    exit
}

Write-Host "Подготовка ученического ПК: $env:COMPUTERNAME" -ForegroundColor White
Write-Host ("Пользователь: " + [Security.Principal.WindowsIdentity]::GetCurrent().Name)

# --- 1. Брандмауэр: общий доступ к файлам (SMB) ---
Write-Section "Брандмауэр"
try {
    # Языконезависимый идентификатор группы «Общий доступ к файлам и принтерам».
    $group = "@FirewallAPI.dll,-28502"
    $rules = Get-NetFirewallRule -Group $group -ErrorAction SilentlyContinue
    if ($rules) {
        $rules | Enable-NetFirewallRule -ErrorAction SilentlyContinue
        Write-Host "Включены правила группы «Общий доступ к файлам и принтерам» ($($rules.Count) шт.)." -ForegroundColor Green
    } else {
        Write-Warning "Группа правил брандмауэра не найдена, создаю явное правило для TCP 445."
    }

    # Явное входящее правило для SMB (идемпотентно).
    $explicitName = "ControlWorks SMB-In (TCP 445)"
    $existing = Get-NetFirewallRule -DisplayName $explicitName -ErrorAction SilentlyContinue
    if (-not $existing) {
        New-NetFirewallRule -DisplayName $explicitName `
            -Direction Inbound -Protocol TCP -LocalPort 445 `
            -Action Allow -Profile Any -ErrorAction Stop | Out-Null
        Write-Host "Создано правило: $explicitName" -ForegroundColor Green
    } else {
        Enable-NetFirewallRule -DisplayName $explicitName -ErrorAction SilentlyContinue
        Write-Host "Правило уже существует и включено: $explicitName" -ForegroundColor Green
    }
} catch {
    Write-Warning ("Не удалось настроить брандмауэр: " + $_.Exception.Message)
}

# --- 2. Служба «Сервер» (LanmanServer) ---
Write-Section "Служба Сервер (LanmanServer)"
try {
    Set-Service -Name LanmanServer -StartupType Automatic -ErrorAction Stop
    if ((Get-Service -Name LanmanServer).Status -ne "Running") {
        Start-Service -Name LanmanServer -ErrorAction Stop
    }
    Write-Host "Служба LanmanServer: $((Get-Service -Name LanmanServer).Status), тип запуска: Automatic." -ForegroundColor Green
} catch {
    Write-Warning ("Не удалось настроить службу LanmanServer: " + $_.Exception.Message)
}

# --- 3. Админ-шара C$ ---
Write-Section "Админ-шара C$"
$cShare = Get-SmbShare -Name "C$" -ErrorAction SilentlyContinue
if ($cShare) {
    Write-Host "Шара C$ присутствует." -ForegroundColor Green
} else {
    Write-Warning "Шара C$ не найдена! Убедитесь, что она не отключена политикой."
}

# --- 4. Локальные администраторы ---
Write-Section "Локальные администраторы"
try {
    $adminsGroup = Get-LocalGroup -SID "S-1-5-32-544" -ErrorAction SilentlyContinue
    if ($adminsGroup) {
        Get-LocalGroupMember -Group $adminsGroup.Name |
            Select-Object Name, PrincipalSource, ObjectClass |
            Format-Table -AutoSize | Out-String | Write-Host
    }
} catch {
    Write-Warning ("Не удалось получить список администраторов: " + $_.Exception.Message)
}

# --- 5. Сетевой доступ администраторов (UAC-фильтрация токена) ---
Write-Section "Доступ к админ-шарам по сети (UAC)"
try {
    # По умолчанию локальный администратор, подключаясь по сети, получает
    # урезанный токен и не может открыть C$. Разрешаем полный доступ.
    $polPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System"
    $cur = (Get-ItemProperty -Path $polPath -Name LocalAccountTokenFilterPolicy -ErrorAction SilentlyContinue).LocalAccountTokenFilterPolicy
    if ($cur -ne 1) {
        New-ItemProperty -Path $polPath -Name LocalAccountTokenFilterPolicy -Value 1 `
            -PropertyType DWord -Force | Out-Null
        Write-Host "Установлено LocalAccountTokenFilterPolicy=1 (доступ к C$ для локальных админов)." -ForegroundColor Green
    } else {
        Write-Host "LocalAccountTokenFilterPolicy уже = 1." -ForegroundColor Green
    }
} catch {
    Write-Warning ("Не удалось задать LocalAccountTokenFilterPolicy: " + $_.Exception.Message)
}

# --- 6. Учётная запись для сбора (по желанию) ---
if ($AdminUser -and $Password) {
    Write-Section "Настройка учётной записи $AdminUser"
    try {
        $sec = ConvertTo-SecureString $Password -AsPlainText -Force

        $user = Get-LocalUser -Name $AdminUser -ErrorAction SilentlyContinue
        if (-not $user) {
            New-LocalUser -Name $AdminUser -Password $sec `
                -FullName "Учётная запись для сбора работ" `
                -Description "Создана скриптом Prepare-StudentPc" -ErrorAction Stop | Out-Null
            Write-Host "Создан пользователь $AdminUser." -ForegroundColor Green
        } else {
            Set-LocalUser -Name $AdminUser -Password $sec -ErrorAction Stop
            Write-Host "Пароль пользователя $AdminUser обновлён." -ForegroundColor Green
        }

        if ($PasswordNeverExpires) {
            Set-LocalUser -Name $AdminUser -PasswordNeverExpires $true -ErrorAction SilentlyContinue
        }
        if ($EnableAdmin) {
            if ((Get-LocalUser -Name $AdminUser).Enabled -eq $false) {
                Enable-LocalUser -Name $AdminUser -ErrorAction SilentlyContinue
            }
            Add-LocalGroupMember -SID "S-1-5-32-544" -Member $AdminUser -ErrorAction SilentlyContinue
            Write-Host "$AdminUser добавлен в группу администраторов." -ForegroundColor Green
        }
    } catch {
        Write-Warning ("Не удалось настроить пользователя: " + $_.Exception.Message)
    }
}

# --- 7. Проверка прослушивания порта 445 ---
Write-Section "Проверка SMB"
try {
    $listen = Get-NetTCPConnection -LocalPort 445 -State Listen -ErrorAction SilentlyContinue
    if ($listen) {
        Write-Host "Порт 445 слушается." -ForegroundColor Green
    } else {
        Write-Warning "Порт 445 не слушается. Проверьте службу LanmanServer и брандмауэр."
    }

    if (Test-Path "\\localhost\C$\Users") {
        Write-Host "Доступ к \\localhost\C$\Users есть." -ForegroundColor Green
    } else {
        Write-Warning "Не удалось открыть \\localhost\C$\Users."
    }
} catch {
    Write-Warning ("Ошибка проверки SMB: " + $_.Exception.Message)
}

Write-Host ""
Write-Host "Готово. Этот ПК, скорее всего, доступен для сбора." -ForegroundColor Green
Write-Host "Проверьте с ПК учителя: Test-NetConnection $env:COMPUTERNAME -Port 445"
