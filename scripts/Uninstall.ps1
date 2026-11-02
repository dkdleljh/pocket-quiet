#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ServiceTools.ps1')
$target = Join-Path $env:ProgramFiles 'Pocket Quiet'
$exe = Join-Path $target 'PocketQuiet.Service.exe'
$service = Get-Service PocketQuiet -ErrorAction SilentlyContinue
if ($service) { Stop-Service PocketQuiet; $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(15)) }
Wait-PocketProcesses $exe
$recovery = Join-Path (Split-Path $PSScriptRoot -Parent) 'service\PocketQuiet.Service.exe'
if (Test-Path $recovery) {
 & $recovery --restore-if-supported
 if ($LASTEXITCODE -ne 0) { throw 'Recovery failed. Service files were retained.' }
} elseif (Test-Path $exe) { throw 'Packaged recovery tool is missing. Service files were retained.' }
if ($service) {
 & sc.exe delete PocketQuiet | Out-Null
 if ($LASTEXITCODE -ne 0) { throw 'Service deletion failed.' }
}
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
$link = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Pocket Quiet.lnk'
if (Test-Path $link) { Remove-Item $link }
Write-Host 'Service removed. Logs, rollback copies, desktop package and shared PawnIO remain installed.'
