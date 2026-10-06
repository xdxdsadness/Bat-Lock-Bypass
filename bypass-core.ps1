# Shared bypass logic. Loaded via dot-source:
#   . (Join-Path $dir 'bypass-core.ps1')
# Used by the console (console.ps1); the GUI keeps its own C# copy of the same logic.

$script:Base = Split-Path -Parent $MyInvocation.MyCommand.Path
$script:Bin = Join-Path $script:Base 'bin'
$script:PresetsFile = Join-Path $script:Base 'presets.json'

# Main process — the branded copy of the engine ("Bat lock bypass").
# Falls back to the plain engine.exe when the branded copy is missing.
$script:ExeName = 'Bat lock bypass'
$script:ExePath = Join-Path $script:Bin ($script:ExeName + '.exe')
if (-not (Test-Path $script:ExePath)) {
    $script:ExeName = 'engine'
    $script:ExePath = Join-Path $script:Bin 'engine.exe'
}

# When stopping, kill both the branded copy and the plain engine.
$script:AllProcessNames = @('Bat lock bypass', 'engine')

function Test-IsElevated {
    return ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$script:Presets = $null
$script:RunningPresetIndex = -1
$script:LastError = $null

function Get-Presets {
    if ($null -eq $script:Presets) {
        if (-not (Test-Path $script:PresetsFile)) { throw "Не найден файл пресетов: $script:PresetsFile" }
        $script:Presets = @(Get-Content $script:PresetsFile -Raw -Encoding UTF8 | ConvertFrom-Json)
    }
    return $script:Presets
}

function Get-PresetDisplayName([int]$Index) {
    $presets = Get-Presets
    if ($Index -lt 0 -or $Index -ge $presets.Count) { return "<нет пресета $Index>" }
    return '{0}. {1}' -f ($Index + 1), $presets[$Index].name
}

function Test-BypassRunning {
    return [bool](Get-Process -Name $script:AllProcessNames -ErrorAction SilentlyContinue)
}

function Stop-Bypass {
    Stop-Process -Name $script:AllProcessNames -Force -ErrorAction SilentlyContinue
    $script:RunningPresetIndex = -1
}

# Starts the bypass process. Elevated: Start-Process (ShellExecute) — the engine gets
# its own hidden console and detaches from the parent; otherwise closing the console
# window sends CTRL_CLOSE and the engine dies together with it.
# Non-elevated: a temporary .ps1 launcher + Verb=RunAs (a single UAC prompt):
# the launcher uses CreateNoWindow, so the process lives on without a window.
function Start-BypassProcess([string]$Exe, [string]$ExeArgs) {
    if (Test-IsElevated) {
        Start-Process -FilePath $Exe -ArgumentList $ExeArgs -WorkingDirectory $script:Bin -WindowStyle Hidden
        return $false
    }
    $launcher = Join-Path $env:TEMP ("blb-launch-{0}.ps1" -f $PID)
    @(
        "`$psi = New-Object System.Diagnostics.ProcessStartInfo",
        "`$psi.FileName = '$Exe'",
        "`$psi.Arguments = '$ExeArgs'",
        "`$psi.WorkingDirectory = '$script:Bin'",
        "`$psi.UseShellExecute = `$false",
        "`$psi.CreateNoWindow = `$true",
        '[System.Diagnostics.Process]::Start($psi) | Out-Null',
        # the launcher deletes itself: the parent never knows when the elevated PS finishes reading the file
        "Remove-Item -LiteralPath '$launcher' -ErrorAction SilentlyContinue"
    ) | Set-Content -Path $launcher -Encoding ASCII
    Start-Process 'powershell.exe' -Verb RunAs -WindowStyle Hidden -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-WindowStyle', 'Hidden', '-File', "`"$launcher`""
    )
    # the elevated PowerShell needs time to start — the parent waits a bit longer
    return $true
}

# Starts the bypass with preset $Index. Returns $true when the process is alive.
function Start-Preset([int]$Index) {
    $presets = Get-Presets
    if ($Index -lt 0 -or $Index -ge $presets.Count) {
        $script:LastError = "Нет пресета №$($Index + 1)."
        return $false
    }
    $p = $presets[$Index]
    $script:LastError = $null
    Stop-Bypass
    Start-Sleep -Milliseconds 400
    $engineArgs = $p.args.Replace('{BASE}', $script:Base)

    try {
        $viaLauncher = Start-BypassProcess $script:ExePath $engineArgs
    } catch {
        $script:LastError = $_.Exception.Message
        return $false
    }
    # Waiting for the process to appear. Via the launcher it takes longer (starting
    # the elevated PS and waiting for the UAC answer both take time).
    $deadline = (Get-Date).AddSeconds($(if ($viaLauncher) { 20 } else { 10 }))
    do {
        if (Test-BypassRunning) {
            $script:RunningPresetIndex = $Index
            return $true
        }
        Start-Sleep -Milliseconds 300
    } while ((Get-Date) -lt $deadline)
    $script:LastError = 'Процесс движка не запустился. Попробуй другой способ обхода.'
    return $false
}
