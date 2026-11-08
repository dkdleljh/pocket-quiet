[CmdletBinding()]
param(
 [Parameter(Mandatory=$true)][string]$Package,
 [string]$ReportPath,
 [switch]$RequireValid
)
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $Package).Path.TrimEnd('\')
foreach ($required in @('PocketQuiet.exe','PocketQuiet.dll','PocketQuiet.Core.dll','service\PocketQuiet.Service.exe','scripts\Install.ps1')) {
 if (-not (Test-Path -LiteralPath (Join-Path $root $required) -PathType Leaf)) { throw "Incomplete package: $required" }
}
$files = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
 $_.Extension -in '.exe','.dll','.ps1' -and -not $_.FullName.StartsWith($root+'\source\', [StringComparison]::OrdinalIgnoreCase)
})
$results = @($files | ForEach-Object {
 $signature = Get-AuthenticodeSignature -LiteralPath $_.FullName
 [pscustomobject]@{
  Path=$_.FullName.Substring($root.Length+1).Replace('\','/')
  SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  Status=[string]$signature.Status
  Signer=$(if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null })
  Thumbprint=$(if ($signature.SignerCertificate) { $signature.SignerCertificate.Thumbprint } else { $null })
  Timestamped=($null -ne $signature.TimeStamperCertificate)
 }
})
$invalid = @($results | Where-Object Status -ne 'Valid')
$report = [pscustomobject]@{
 CheckedAt=[DateTimeOffset]::UtcNow.ToString('o'); FileCount=$results.Count
 AllValid=($invalid.Count -eq 0); InvalidCount=$invalid.Count; Files=$results
}
if ($ReportPath) { $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $ReportPath -Encoding UTF8 }
$invalid | Format-Table Path,Status -AutoSize | Out-Host
Write-Host "Authenticode: $($results.Count - $invalid.Count)/$($results.Count) valid. Live SAC testing is still required."
if ($RequireValid -and $invalid.Count) { throw 'Unsigned or invalid execution files remain. Do not label this package SAC-ready.' }
return $report
