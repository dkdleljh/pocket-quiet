#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$service=Get-Service PocketQuiet -ErrorAction SilentlyContinue
if ($service) { Stop-Service PocketQuiet; $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(15)); & sc.exe delete PocketQuiet; Start-Sleep -Seconds 3 }
$target=Join-Path $env:ProgramFiles 'Pocket Quiet'
for($attempt=0;$attempt -lt 15;$attempt++) {
 $remaining=@(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq (Join-Path $target 'PocketQuiet.Service.exe') })
 if($remaining.Count -eq 0){break};Start-Sleep -Seconds 1
}
if($remaining.Count -gt 0){throw 'Service/watchdog has not exited; files were retained.'}
if (Test-Path $target) { Remove-Item $target -Recurse -Force }
$link=Join-Path ([Environment]::GetFolderPath('Desktop')) 'Pocket Quiet.lnk'
if(Test-Path $link){Remove-Item $link}
Write-Host 'Service removed. Local logs and the desktop package are retained. PawnIO is shared and remains installed.'
