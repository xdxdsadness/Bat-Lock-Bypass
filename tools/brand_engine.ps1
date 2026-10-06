# Rebuilds the engine executable: engine.exe -> "Bat lock bypass.exe".
# A copy of the binary with the icon from src\app.ico and the "Bat lock bypass"
# description — exactly what Task Manager shows in the process list.
# Run after every engine.exe or app.ico update.
# Tool: rcedit (https://github.com/electron/rcedit, MIT).
$ErrorActionPreference = 'Stop'
$tools = Split-Path -Parent $MyInvocation.MyCommand.Path
$bin = [System.IO.Path]::GetFullPath((Join-Path $tools '..\bin'))
$src = Join-Path $bin 'engine.exe'
$dst = Join-Path $bin 'Bat lock bypass.exe'
$ico = [System.IO.Path]::GetFullPath((Join-Path $tools '..\src\app.ico'))

if (-not (Test-Path $src)) { Write-Error "engine.exe not found: $src"; exit 1 }
if (-not (Test-Path $ico)) { Write-Error "app.ico not found: $ico"; exit 1 }

Copy-Item $src $dst -Force

$rcedit = Join-Path $tools 'rcedit-x64.exe'
& $rcedit $dst `
    --set-icon $ico `
    --set-version-string "FileDescription" "Bat lock bypass" `
    --set-version-string "ProductName" "Bat Player" `
    --set-version-string "CompanyName" "BatPlayer" `
    --set-version-string "OriginalFilename" "Bat lock bypass.exe" `
    --set-version-string "LegalCopyright" "(c) Bat Player" `
    --set-file-version "1.0.0.0" `
    --set-product-version "1.0.0.0" | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Error "rcedit failed"; exit 1 }

Write-Host "Branded: $dst"
(Get-Item $dst).VersionInfo | Format-List FileDescription, ProductName, OriginalFilename
