#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ServiceTools.ps1')
$package = Split-Path $PSScriptRoot -Parent
$manifest = Test-PocketPackage $package
$target = Join-Path $env:ProgramFiles 'Pocket Quiet'
$data = Join-Path $env:ProgramData 'PocketQuiet'
$serviceExe = Join-Path $target 'PocketQuiet.Service.exe'
$driver = Join-Path $package 'third-party\PawnIO_setup.exe'
if (-not (Test-Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO')) {
 $signature = Get-AuthenticodeSignature $driver
 if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=namazso.eu') { throw 'PawnIO installer signature validation failed.' }
 $install = Start-Process $driver -ArgumentList '-install','-silent' -Wait -PassThru
 if ($install.ExitCode -ne 0) { throw "PawnIO installation failed: $($install.ExitCode)" }
}
$staging = $target + '.staging-' + [Guid]::NewGuid().ToString('N')
$backup = $target + '.previous-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Set-PocketDirectoryAcl $staging $true
Copy-Item (Join-Path $package 'service\*') $staging -Recurse -Force
$service = Get-Service PocketQuiet -ErrorAction SilentlyContinue
$hadService = $null -ne $service
$replaced = $false
$created = $false
try {
 if ($service) { Stop-Service PocketQuiet; $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(15)) }
 Wait-PocketProcesses $serviceExe
 # Use the new, BIOS-gated recovery code before replacing the installed service.
 & (Join-Path $staging 'PocketQuiet.Service.exe') --restore-if-supported
 if ($LASTEXITCODE -ne 0) { throw 'Firmware recovery failed; old service files retained.' }
 if (Test-Path $target) { Move-Item -LiteralPath $target -Destination $backup }
 $replaced = $true
 Move-Item -LiteralPath $staging -Destination $target
 New-Item -ItemType Directory -Path $data -Force | Out-Null
 Set-PocketDirectoryAcl $target $true
 Set-PocketDirectoryAcl $data $false
 $binary = '"' + $serviceExe + '"'
 if (-not $hadService) {
  New-Service -Name PocketQuiet -BinaryPathName $binary -DisplayName 'Pocket Quiet Fan Control' -StartupType Automatic -Description 'Local fan controller with thermal safeguards. Starts in firmware mode.' | Out-Null
  $created = $true
 } else {
  & sc.exe config PocketQuiet binPath= $binary start= auto obj= LocalSystem | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'Service configuration failed.' }
 }
 & sc.exe failure PocketQuiet reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
 if ($LASTEXITCODE -ne 0) { throw 'Service recovery configuration failed.' }
 Start-Service PocketQuiet
 $healthy = $false
 for ($attempt=0; $attempt -lt 20; $attempt++) {
  try {
   $status = Invoke-PocketRequest
   if ($status.Version -eq $manifest.Version -and $status.WatchdogHealthy -and $status.SampleTime -and
       ((-not $status.Supported) -or ($status.Ready -and $status.ControlState -eq 'Firmware'))) { $healthy=$true; break }
  } catch { }
  Start-Sleep -Seconds 1
 }
 if (-not $healthy) { throw 'New service failed readiness validation.' }
 $shell = New-Object -ComObject WScript.Shell
 $shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'Pocket Quiet.lnk'))
 $shortcut.TargetPath = Join-Path $package 'PocketQuiet.exe'
 $shortcut.WorkingDirectory = $package
 $shortcut.IconLocation = (Join-Path $package 'Assets\PocketQuiet.ico') + ',0'
 $shortcut.Description = 'Pocket Quiet - quiet thermal fan control'
 $shortcut.Save()
 Write-Host 'Pocket Quiet 0.2.0 installed and responding. Starts in firmware automatic mode.'
 if (Test-Path $backup) { Write-Host "Previous service retained at: $backup" }
} catch {
 $failure = $_
 if ($replaced) {
  $current = Get-Service PocketQuiet -ErrorAction SilentlyContinue
  if ($current -and $current.Status -ne 'Stopped') { Stop-Service PocketQuiet; $current.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(15)) }
  Wait-PocketProcesses $serviceExe
  # Keep failed files for diagnosis; do not overwrite the rollback copy.
  if (Test-Path $target) { Move-Item $target ($target+'.failed-'+[Guid]::NewGuid().ToString('N')) }
  if (Test-Path $backup) { Move-Item $backup $target }
  if ($created) { & sc.exe delete PocketQuiet | Out-Null }
  elseif ($hadService -and (Test-Path $serviceExe)) { Start-Service PocketQuiet }
 }
 elseif ($hadService) {
  $old = Get-Service PocketQuiet -ErrorAction SilentlyContinue
  if ($old.Status -eq 'Stopped') { Start-Service PocketQuiet }
 }
 throw $failure
} finally {
 if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
}
