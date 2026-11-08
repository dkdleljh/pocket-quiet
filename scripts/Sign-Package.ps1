[CmdletBinding(DefaultParameterSetName='Certificate')]
param(
 [Parameter(Mandatory=$true)][string]$Package,
 [Parameter(Mandatory=$true)][string]$SignTool,
 [Parameter(Mandatory=$true,ParameterSetName='Certificate')][ValidatePattern('^[0-9a-fA-F]{40}$')][string]$Thumbprint,
 [Parameter(ParameterSetName='Certificate')][switch]$LocalMachine,
 [Parameter(Mandatory=$true,ParameterSetName='ArtifactSigning')][string]$Dlib,
 [Parameter(Mandatory=$true,ParameterSetName='ArtifactSigning')][string]$Metadata,
 [string]$TimestampUrl='http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Package).Path.TrimEnd('\')
$SignTool = (Resolve-Path -LiteralPath $SignTool).Path
if ($PSCmdlet.ParameterSetName -eq 'ArtifactSigning') {
 $Dlib = (Resolve-Path -LiteralPath $Dlib).Path
 $Metadata = (Resolve-Path -LiteralPath $Metadata).Path
} else {
 $store = if ($LocalMachine) { 'LocalMachine' } else { 'CurrentUser' }
 $certificate = Get-Item "Cert:\$store\My\$Thumbprint"
 if (-not $certificate.HasPrivateKey -or $certificate.NotAfter -lt (Get-Date) -or $certificate.Subject -eq $certificate.Issuer) {
  throw 'Use a current CA-issued code signing certificate with an accessible private key, not a self-signed certificate.'
 }
 if ('1.3.6.1.5.5.7.3.3' -notin @($certificate.EnhancedKeyUsageList | ForEach-Object {$_.ObjectId})) {
  throw 'The selected certificate does not have the code signing EKU.'
 }
}
# Sign each distinct unsigned payload once; preserve existing valid vendor signatures.
# A commercial signing identity may sign licensed dependencies. SignPath Foundation has
# stricter upstream rules: use its approved artifact configuration instead of this script.
$signed = @{}
$files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
 $_.Extension -in '.exe','.dll','.ps1' -and -not $_.FullName.StartsWith($root+'\source\',[StringComparison]::OrdinalIgnoreCase)
})
foreach ($file in $files) {
 $signature = Get-AuthenticodeSignature -LiteralPath $file.FullName
 if ($signature.Status -eq 'Valid') { continue }
 if ($signature.Status -ne 'NotSigned') { throw "Invalid existing signature; investigate instead of replacing: $($file.FullName)" }
 $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
 if ($signed.ContainsKey($hash)) { Copy-Item -LiteralPath $signed[$hash] -Destination $file.FullName -Force; continue }
 $arguments = @('sign','/fd','SHA256','/tr',$TimestampUrl,'/td','SHA256')
 if ($PSCmdlet.ParameterSetName -eq 'ArtifactSigning') { $arguments += @('/dlib',$Dlib,'/dmdf',$Metadata) }
 else { $arguments += @('/sha1',$Thumbprint); if ($LocalMachine) { $arguments += '/sm' } }
 & $SignTool @arguments $file.FullName
 if ($LASTEXITCODE -ne 0) { throw "Signing failed: $($file.FullName)" }
 $check = Get-AuthenticodeSignature -LiteralPath $file.FullName
 if ($check.Status -ne 'Valid' -or -not $check.TimeStamperCertificate) { throw "Signature/timestamp verification failed: $($file.FullName)" }
 $signed[$hash] = $file.FullName
}
& (Join-Path $PSScriptRoot 'Test-Signatures.ps1') -Package $root -RequireValid -ReportPath (Join-Path $root 'signatures.json') | Out-Null
Write-Host 'Signatures verified. Next run package.py --finalize-signed PACKAGE to rebuild hashes and ZIP.'
