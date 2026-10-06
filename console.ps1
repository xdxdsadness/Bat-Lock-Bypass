# Console version (for debugging and terminal fans).
# The main way to run the app is the "Bat Lock Bypass.exe" window.
# Keys: -Preset <number|part of the name>, -Stop, -Status
param(
    [string]$Preset,
    [switch]$Stop,
    [switch]$Status
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch {}
. (Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) 'bypass-core.ps1')

$presets = Get-Presets

$host.UI.RawUI.WindowTitle = 'Bat Lock Bypass'

function Show-Status {
    if (Test-BypassRunning) {
        $extra = if ($script:RunningPresetIndex -ge 0) { " (пресет $($script:RunningPresetIndex + 1))" } else { '' }
        Write-Host "[•] Обход сейчас: АКТИВЕН$extra" -ForegroundColor Green
    } else {
        Write-Host '[•] Обход сейчас: не запущен' -ForegroundColor Yellow
    }
}

function Resolve-PresetIndex([string]$query) {
    if ($query -match '^\d+$') {
        $n = [int]$query
        if ($n -ge 1 -and $n -le $presets.Count) { return $n - 1 }
        return -1
    }
    for ($i = 0; $i -lt $presets.Count; $i++) {
        if ($presets[$i].name -like "*$query*") { return $i }
    }
    return -1
}

# --- non-menu modes ---
if ($Status) { Show-Status; exit 0 }
if ($Stop)   { Stop-Bypass; Write-Host '[OK] Обход остановлен.' -ForegroundColor Green; exit 0 }
if ($Preset) {
    $idx = Resolve-PresetIndex $Preset
    if ($idx -lt 0) { Write-Host "[X] Пресет '$Preset' не найден (1..$($presets.Count) или часть имени)." -ForegroundColor Red; exit 1 }
    if (Start-Preset $idx) { Write-Host "[OK] Запущен: $(Get-PresetDisplayName $idx)" -ForegroundColor Green }
    else { Write-Host "[X] Не получилось: $($script:LastError)" -ForegroundColor Red; exit 1 }
    exit 0
}

# --- interactive menu ---
Write-Host '===== Bat Lock Bypass =====' -ForegroundColor Cyan

$isAdmin = Test-IsElevated
if (-not $isAdmin) {
    Write-Host '[!] Нет прав администратора — правильный способ: запустить "Bat Lock Bypass.exe".' -ForegroundColor Yellow
}

if (-not (Test-BypassRunning)) {
    Write-Host 'Обход не запущен — применяю пресет по умолчанию (№1)...' -ForegroundColor Gray
    if (Start-Preset 0) { Write-Host "[OK] Запущен: $(Get-PresetDisplayName 0)" -ForegroundColor Green }
    else { Write-Host "[X] Не получилось: $($script:LastError)" -ForegroundColor Red }
} else {
    Show-Status
}

while ($true) {
    Write-Host ''
    Write-Host '--- Пресеты (выбор перезапускает обход) ---' -ForegroundColor Cyan
    for ($i = 0; $i -lt $presets.Count; $i++) {
        Write-Host ('  {0,2}. {1}' -f ($i + 1), $presets[$i].name)
    }
    Write-Host '   S. Остановить обход'
    Write-Host '   Q. Выход (обход продолжит работать в фоне)'

    $choice = (Read-Host 'Выбор (номер / S / Q)').Trim()
    if ($choice -eq '') { continue }

    $lat = $choice.ToLower()
    if ($lat -eq 's' -or $lat -eq 'ы') { Stop-Bypass; Write-Host '[OK] Обход остановлен.' -ForegroundColor Green; continue }
    if ($lat -eq 'q' -or $lat -eq 'й') { break }

    $idx = Resolve-PresetIndex $choice
    if ($idx -ge 0) {
        if (Start-Preset $idx) { Write-Host "[OK] Запущен: $(Get-PresetDisplayName $idx)" -ForegroundColor Green }
        else { Write-Host "[X] Не получилось: $($script:LastError)" -ForegroundColor Red }
    } else {
        Write-Host "Не понял '$choice' — введи номер пресета, S или Q." -ForegroundColor Yellow
    }
}
