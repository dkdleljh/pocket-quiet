#Requires -RunAsAdministrator
[CmdletBinding()]
param([ValidateSet('Apply','Restore','Status')][string]$Action='Status')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
. (Join-Path $PSScriptRoot 'ServiceTools.ps1')

# Only a dedicated copy is edited. The user's original plan is never modified.
$managed = '714b0ad2-3382-45c1-8fe0-70551b461890'
$processor = '54533251-82be-4824-96c1-47b60b740d00'
$data = Join-Path $env:ProgramData 'PocketQuiet'
$statePath = Join-Path $data 'quiet-power.json'
$schemeRoot = 'HKLM:\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes'
$powercfg = Join-Path $env:SystemRoot 'System32\powercfg.exe'
$settings = @(
 @('be337238-0d82-4146-a960-4f3749d470c7',0,0),
 @('893dee8e-2bef-41e0-89c6-b55d0929964c',5,5),
 @('bc5038f7-23e0-4960-96da-33abaf5935ec',100,100),
 @('36687f9e-e3a5-4dbf-b1dc-15eb381c6863',70,80),
 @('0cc5b647-c1df-4637-891a-dec35c318583',10,10))
# Windows may expose separate policy indices for efficiency class 1.
foreach ($entry in @(
 @('893dee8e-2bef-41e0-89c6-b55d0929964d',5,5),
 @('bc5038f7-23e0-4960-96da-33abaf5935ed',100,100),
 @('36687f9e-e3a5-4dbf-b1dc-15eb381c6864',70,80),
 @('0cc5b647-c1df-4637-891a-dec35c318584',10,10))) {
 if (Test-Path -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerSettings\$processor\$($entry[0])") { $settings += ,$entry }
}
function Invoke-Power([string[]]$Arguments) {
 $output = & $powercfg @Arguments 2>&1
 if ($LASTEXITCODE -ne 0) { throw "powercfg failed ($LASTEXITCODE): $output" }
 return ($output -join "`n")
}
function Get-ActivePlan {
 $output = Invoke-Power @('/getactivescheme')
 $match = [regex]::Match($output, '[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}')
 if (-not $match.Success) { throw 'Cannot read active power plan.' }
 return $match.Value.ToLowerInvariant()
}
function Set-ActivePlan([string]$Plan) {
 Invoke-Power @('/setactive', $Plan) | Out-Null
 if ((Get-ActivePlan) -ne $Plan) { throw 'Active power plan verification failed.' }
}
function Set-QuietValue([string]$Setting, [int]$AC, [int]$DC) {
 Invoke-Power @('/setacvalueindex', $managed, $processor, $Setting, "$AC") | Out-Null
 Invoke-Power @('/setdcvalueindex', $managed, $processor, $Setting, "$DC") | Out-Null
 $values = Get-ItemProperty -LiteralPath "$schemeRoot\$managed\$processor\$Setting"
 if ($values.ACSettingIndex -ne $AC -or $values.DCSettingIndex -ne $DC) {
  throw "Power setting readback failed: $Setting"
 }
}
New-Item -ItemType Directory -Path $data -Force | Out-Null
Set-PocketDirectoryAcl $data $false
# Serializes UI, command-line and simultaneous instances; disposal also releases a crashed writer.
$lock = [IO.File]::Open((Join-Path $data 'quiet-power.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
try {
 $state = if (Test-Path -LiteralPath $statePath) { Get-Content -LiteralPath $statePath -Raw -Encoding UTF8 | ConvertFrom-Json } else { $null }
 if ($state -and ($state.Version -ne 1 -or $state.Managed -ne $managed -or
     $state.Original -notmatch '^[0-9a-fA-F]{8}(?:-[0-9a-fA-F]{4}){3}-[0-9a-fA-F]{12}$' -or $state.Original -eq $managed)) {
  throw 'Invalid power recovery state. No power settings were changed.'
 }
 $before = Get-ActivePlan
 if ($Action -eq 'Restore') {
  if ($state -and $before -eq $managed) {
   if (-not (Test-Path -LiteralPath "$schemeRoot\$($state.Original)")) {
    throw 'Original plan is missing. Restore original-power.pow using powercfg /import before retrying.'
   }
   Set-ActivePlan $state.Original
  }
  # An unrelated plan selected by the user is never replaced during restore.
 } elseif ($Action -eq 'Apply') {
  if (-not $state) {
   if (Test-Path -LiteralPath "$schemeRoot\$managed") { throw 'Reserved plan already exists without ownership data. No changes made.' }
   Invoke-Power @('/export', (Join-Path $data 'original-power.pow'), $before) | Out-Null
   $state = [pscustomobject]@{Version=1; Managed=$managed; Original=$before; SavedAt=[DateTimeOffset]::UtcNow.ToString('o')}
   $state | ConvertTo-Json | Set-Content -LiteralPath ($statePath+'.tmp') -Encoding UTF8
   Move-Item -LiteralPath ($statePath+'.tmp') -Destination $statePath -Force
  }
  try {
   if (-not (Test-Path -LiteralPath "$schemeRoot\$managed")) {
    Invoke-Power @('/duplicatescheme', $state.Original, $managed) | Out-Null
   }
   Invoke-Power @('/changename', $managed, 'Pocket Quiet - Low Heat', 'Reduced boost and energy preference; original plan preserved.') | Out-Null
   # No undervolting, firmware modifications or temperature-limit changes.
   foreach ($entry in $settings) { Set-QuietValue $entry[0] $entry[1] $entry[2] }
   Set-ActivePlan $managed
  } catch {
   $failure = $_
   # If reapplying the managed plan fails, use the untouched original.
   $rollback = if ($before -eq $managed) { $state.Original } else { $before }
   try { Set-ActivePlan $rollback } catch { throw "Apply failed: $failure. Rollback also failed: $_" }
   throw $failure
  }
 }
 $active = Get-ActivePlan
 $verified = $active -eq $managed
 if ($verified) {
  foreach ($entry in $settings) {
   $values = Get-ItemProperty -LiteralPath "$schemeRoot\$managed\$processor\$($entry[0])" -ErrorAction SilentlyContinue
   if ($null -eq $values -or $values.ACSettingIndex -ne $entry[1] -or $values.DCSettingIndex -ne $entry[2]) { $verified = $false }
  }
 }
 [pscustomobject]@{Active=($active -eq $managed); Verified=$verified; ActivePlan=$active; OriginalPlan=$(if ($state) {$state.Original} else {$null}); BackupPath=$statePath} | ConvertTo-Json -Compress
} finally { $lock.Dispose() }
