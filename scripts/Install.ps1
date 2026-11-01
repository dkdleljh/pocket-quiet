#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$package = Split-Path $PSScriptRoot -Parent
$target = Join-Path $env:ProgramFiles 'Pocket Quiet'
$data = Join-Path $env:ProgramData 'PocketQuiet'
$serviceExe = Join-Path $target 'PocketQuiet.Service.exe'
$driver = Join-Path $package 'third-party\PawnIO_setup.exe'
if (-not (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO')) {
 if (-not (Test-Path $driver)) { throw 'PawnIO_setup.exe is missing. Download the signed installer from https://pawnio.eu/.' }
 $signature = Get-AuthenticodeSignature $driver
 if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=namazso.eu') { throw 'PawnIO installer signature validation failed.' }
 $install = Start-Process $driver -ArgumentList '-install','-silent' -Wait -PassThru
 if ($install.ExitCode -ne 0) { throw "PawnIO installation failed: $($install.ExitCode)" }
}
$service = Get-Service PocketQuiet -ErrorAction SilentlyContinue
if ($service) { Stop-Service PocketQuiet; $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(15)); Start-Sleep -Seconds 2 }
for($attempt=0;$attempt -lt 15;$attempt++) {
 $remaining=@(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $serviceExe })
 if($remaining.Count -eq 0){break}
 Start-Sleep -Seconds 1
}
if($remaining.Count -gt 0){throw 'Previous service/watchdog has not exited. Retry after recovery completes.'}
New-Item -ItemType Directory -Force -Path $target,$data | Out-Null
# Service executable and configuration must never be writable by ordinary users.
& icacls.exe $target /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Program directory ACL failed.' }
& icacls.exe $data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Data directory ACL failed.' }
Copy-Item (Join-Path $package 'service\*') $target -Recurse -Force
$binary = '"' + (Join-Path $target 'PocketQuiet.Service.exe') + '"'
if (-not $service) {
 New-Service -Name PocketQuiet -BinaryPathName $binary -DisplayName 'Pocket Quiet Fan Control' -StartupType Automatic -Description 'Local thermal fan controller for supported GPD Pocket 4 devices. Starts in firmware automatic mode.' | Out-Null
} else { & sc.exe config PocketQuiet binPath= $binary start= auto | Out-Null }
& sc.exe failure PocketQuiet reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Service recovery configuration failed.' }
Start-Service PocketQuiet
(Get-Service PocketQuiet).WaitForStatus('Running',[TimeSpan]::FromSeconds(15))
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Pocket Quiet.lnk'))
$shortcut.TargetPath = Join-Path $package 'PocketQuiet.exe'
$shortcut.WorkingDirectory = $package
$shortcut.Description = 'Pocket Quiet - adaptive thermal fan control'
$shortcut.Save()
Write-Host 'Pocket Quiet installed. Open the desktop shortcut and select a preset.'
