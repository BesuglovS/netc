<#
    netc: открытие сетевого доступа к ученическому ПК.
    Запускать ОТ АДМИНИСТРАТОРА НА САМОМ ПК (новые ПК, свежая Windows).

    Что делает:
      - переводит активный сетевой профиль в «Частный» (Private);
      - включает общий доступ к файлам и принтерам и сетевое обнаружение (по возможности);
      - создаёт явные правила брандмауэра: TCP 445, TCP 139, UDP 137/138, ICMPv4 (ping);
      - включает службу «Сервер» (LanmanServer);
      - печатает имя ПК, IPv4-адреса, MAC и профиль сети (для протокола развёртывания).
#>

$ErrorActionPreference = 'Continue'
function Step([string]$t) { Write-Host ("`n== " + $t) }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "ОШИБКА: запустите от имени администратора." -ForegroundColor Red
    exit 1
}

Step 'Идентификация ПК'
try {
    $os = Get-CimInstance Win32_OperatingSystem -ErrorAction Stop
    Write-Host ("Имя ПК     : " + $env:COMPUTERNAME)
    Write-Host ("ОС         : " + $os.Caption + " (" + $os.Version + ")")
    Write-Host ("Пользователь: " + $env:USERNAME)
} catch { Write-Host ("Не удалось прочитать сведения об ОС: " + $_.Exception.Message) }

try {
    Write-Host "IPv4:"
    Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
        ForEach-Object { Write-Host ("   {0,-16} {1}" -f $_.IPAddress, $_.InterfaceAlias) }
    Write-Host "Адаптеры (включённые):"
    Get-NetAdapter -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'Up' } |
        ForEach-Object { Write-Host ("   {0,-22} {1}  {2}" -f $_.Name, $_.MacAddress, $_.LinkSpeed) }
} catch { Write-Host ("Сеть: " + $_.Exception.Message) }

Step 'Сетевой профиль -> Частный'
try {
    Get-NetConnectionProfile -ErrorAction SilentlyContinue | ForEach-Object {
        Write-Host ("   {0}: {1}" -f $_.InterfaceAlias, $_.NetworkCategory)
        if ($_.NetworkCategory -eq 'Public') {
            try { Set-NetConnectionProfile -InterfaceIndex $_.InterfaceIndex -NetworkCategory Private -ErrorAction Stop; Write-Host '      -> переведён в Private' }
            catch { Write-Host ("      -> не удалось: " + $_.Exception.Message) }
        }
    }
} catch { Write-Host ("   профили: " + $_.Exception.Message) }

Step 'Встроенные группы брандмауэра (best-effort)'
foreach ($g in 'File and Printer Sharing', 'Network Discovery') {
    try { Set-NetFirewallRule -DisplayGroup $g -Enabled True -Profile Any -ErrorAction Stop; Write-Host ("   включена группа: " + $g) }
    catch { Write-Host ("   группа «" + $g + "» недоступна (локализация?) — пропускаю") }
}

Step 'Явные правила брандмауэра'
function Add-Rule([string]$name, [string]$proto, [string]$ports, [string]$icmp) {
    try {
        Get-NetFirewallRule -DisplayName $name -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue
        if ($icmp) {
            New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Protocol ICMPv4 -IcmpType 8 -Profile Any -ErrorAction Stop | Out-Null
        } else {
            New-NetFirewallRule -DisplayName $name -Direction Inbound -Action Allow -Protocol $proto -LocalPort $ports -Profile Any -ErrorAction Stop | Out-Null
        }
        Write-Host ("   ок: " + $name)
    } catch { Write-Host ("   НЕ удалось " + $name + ": " + $_.Exception.Message) }
}
Add-Rule 'netc SMB TCP 445' 'TCP' '445' ''
Add-Rule 'netc NetBIOS TCP 139' 'TCP' '139' ''
Add-Rule 'netc NetBIOS UDP 137' 'UDP' '137' ''
Add-Rule 'netc NetBIOS UDP 138' 'UDP' '138' ''
Add-Rule 'netc ICMPv4 echo (ping)' '' '' '1'

Step 'Службы'
foreach ($s in 'LanmanServer', 'LanmanWorkstation') {
    try {
        Set-Service -Name $s -StartupType Automatic -ErrorAction Stop
        if ((Get-Service -Name $s).Status -ne 'Running') { Start-Service -Name $s -ErrorAction Stop }
        Write-Host ("   " + $s + ": RUNNING")
    } catch { Write-Host ("   " + $s + ": " + $_.Exception.Message) }
}

Step 'Проверка (локально)'
$smb = (Get-Service LanmanServer -ErrorAction SilentlyContinue).Status
Write-Host ("   SMB-сервер (LanmanServer): " + $(if ($smb) { $smb } else { '?' }))
$listen = (Get-NetTCPConnection -State Listen -LocalPort 445 -ErrorAction SilentlyContinue | Measure-Object).Count
Write-Host ("   порт 445 слушается: " + $(if ($listen -gt 0) { 'да' } else { 'НЕТ' }))

Step 'Удалённое администрирование локальным админом'
$k = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System'
$old = (Get-ItemProperty -Path $k -Name LocalAccountTokenFilterPolicy -ErrorAction SilentlyContinue).LocalAccountTokenFilterPolicy
try {
    New-ItemProperty -Path $k -Name LocalAccountTokenFilterPolicy -Value 1 -PropertyType DWord -Force -ErrorAction Stop | Out-Null
    Write-Host ("   LocalAccountTokenFilterPolicy: " + $(if ($null -ne $old) { $old } else { 'не задан' }) + " -> 1 (разрешён админ-доступ по сети)")
} catch { Write-Host ("   не удалось задать политику: " + $_.Exception.Message) }

$lsa = 'HKLM:\SYSTEM\CurrentControlSet\Control\Lsa'
$fg = (Get-ItemProperty -Path $lsa -Name ForceGuest -ErrorAction SilentlyContinue).ForceGuest
try {
    New-ItemProperty -Path $lsa -Name ForceGuest -Value 0 -PropertyType DWord -Force -ErrorAction Stop | Out-Null
    Write-Host ("   ForceGuest: " + $(if ($null -ne $fg) { $fg } else { 'не задан' }) + " -> 0 (классическая модель общего доступа)")
} catch { Write-Host ("   не удалось задать ForceGuest: " + $_.Exception.Message) }

Step 'Кто администратор на этом ПК'
Write-Host ("   текущий пользователь админ: " + $isAdmin)
foreach ($grp in 'Администраторы', 'Administrators') {
    try {
        Get-LocalGroupMember -Group $grp -ErrorAction Stop | ForEach-Object { Write-Host ("   " + $_.Name) }
        break
    } catch { }
}

Write-Host "`nГотово. Запишите имя ПК и IPv4 выше — по ним выполняется удалённое развёртывание." -ForegroundColor Green
