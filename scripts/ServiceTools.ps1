$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8
function Wait-PocketProcesses {
 param([string]$Executable)
 for ($attempt = 0; $attempt -lt 20; $attempt++) {
  $remaining = @(Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -eq $Executable })
  if ($remaining.Count -eq 0) { return }
  Start-Sleep -Seconds 1
 }
 throw 'Previous service/watchdog has not exited. Files have not been replaced.'
}
function Invoke-PocketRequest {
 param([string]$Command='status', [object]$Settings=$null)
 $pipe = New-Object System.IO.Pipes.NamedPipeClientStream('.', 'PocketQuiet.v1', [System.IO.Pipes.PipeDirection]::InOut)
 try {
  $pipe.Connect(3000)
  $writer = New-Object System.IO.StreamWriter($pipe)
  $writer.AutoFlush = $true
  $reader = New-Object System.IO.StreamReader($pipe)
  $request = @{Command=$Command}
  if ($null -ne $Settings) { $request.Settings = $Settings }
  $writer.WriteLine(($request | ConvertTo-Json -Compress -Depth 8))
  $read = $reader.ReadLineAsync()
  if (-not $read.Wait(4000)) { throw 'Service response timed out.' }
  $result = $read.Result | ConvertFrom-Json
  if (-not $result.Ok) { throw $result.Error }
  return $result.Status
 } finally { $pipe.Dispose() }
}
function Set-PocketDirectoryAcl {
 param([string]$Path, [bool]$AllowRead=$false)
 $acl = New-Object System.Security.AccessControl.DirectorySecurity
 $acl.SetAccessRuleProtection($true, $false)
 $admins = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-544')
 $system = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-18')
 $acl.SetOwner($admins)
 foreach ($identity in @($admins,$system)) {
  $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')))
 }
 if ($AllowRead) {
  $users = New-Object System.Security.Principal.SecurityIdentifier('S-1-5-32-545')
  $acl.AddAccessRule((New-Object System.Security.AccessControl.FileSystemAccessRule($users,'ReadAndExecute','ContainerInherit,ObjectInherit','None','Allow')))
 }
 Set-Acl -LiteralPath $Path -AclObject $acl
}
function Test-PocketPackage {
 param([string]$Package)
 $manifestPath = Join-Path $Package 'deployment-manifest.json'
 if (-not (Test-Path $manifestPath)) { throw 'deployment-manifest.json is missing. Use a complete packaged build.' }
 $manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
 $prefix = [IO.Path]::GetFullPath($Package).TrimEnd('\') + '\'
 foreach ($entry in $manifest.Files.PSObject.Properties) {
  $path = [IO.Path]::GetFullPath((Join-Path $Package $entry.Name))
  if (-not $path.StartsWith($prefix,[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid manifest path.' }
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package file: $($entry.Name)" }
  if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $entry.Value) { throw "Package hash mismatch: $($entry.Name)" }
 }
 if ($manifest.Version -ne '0.3.0') { throw 'Unexpected package version.' }
 return $manifest
}
