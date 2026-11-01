$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
& dotnet run --project tests/PocketQuiet.Tests -c Release
if($LASTEXITCODE -ne 0){throw 'Tests failed.'}
& dotnet publish src/PocketQuiet.Service -c Release -r win-x64 --self-contained true -o artifacts/service
if($LASTEXITCODE -ne 0){throw 'Service build failed.'}
& dotnet publish src/PocketQuiet.App -c Release -r win-x64 --self-contained true -o artifacts/app
if($LASTEXITCODE -ne 0){throw 'App build failed.'}
Write-Host 'Published to artifacts/app and artifacts/service.'
