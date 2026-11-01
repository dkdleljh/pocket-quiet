#Requires -RunAsAdministrator
$ErrorActionPreference='Stop'
$service=Get-Service PocketQuiet -ErrorAction SilentlyContinue
if($service){Stop-Service PocketQuiet; $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(15));Start-Sleep -Seconds 2}
$exe=Join-Path $env:ProgramFiles 'Pocket Quiet\PocketQuiet.Service.exe'
if(-not(Test-Path $exe)){$exe=Join-Path (Split-Path $PSScriptRoot -Parent) 'service\PocketQuiet.Service.exe'}
& $exe --restore
if($LASTEXITCODE -ne 0){throw 'Firmware restoration failed. Inspect the device immediately.'}
Write-Host 'Firmware automatic mode verified. The service is left stopped.'
