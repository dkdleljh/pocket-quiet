param([ValidateSet('Install','Recover','Uninstall')][string]$Task='Install')
$ErrorActionPreference='Stop'
$path=Join-Path $PSScriptRoot ($Task+'.ps1')
$p=Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$path`"" -Wait -PassThru
exit $p.ExitCode
