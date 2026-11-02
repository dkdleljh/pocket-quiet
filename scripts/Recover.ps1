#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ServiceTools.ps1')
$exe = Join-Path $env:ProgramFiles 'Pocket Quiet\PocketQuiet.Service.exe'
$service = Get-Service PocketQuiet -ErrorAction SilentlyContinue
if ($service) { Stop-Service PocketQuiet; $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(15)) }
Wait-PocketProcesses $exe
# Prefer the packaged recovery tool: it enforces the current device/BIOS policy.
$packaged = Join-Path (Split-Path $PSScriptRoot -Parent) 'service\PocketQuiet.Service.exe'
if (Test-Path $packaged) { $exe = $packaged }
& $exe --restore
if ($LASTEXITCODE -ne 0) { throw 'Firmware restoration was not verified. Inspect the device state.' }
Write-Host 'Firmware automatic mode verified. The service is left stopped.'
