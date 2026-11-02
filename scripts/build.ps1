$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
& dotnet restore tests/PocketQuiet.Tests --locked-mode
if ($LASTEXITCODE -ne 0) { throw 'Locked dependency restore failed.' }
& dotnet run --project tests/PocketQuiet.Tests -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
& dotnet publish src/PocketQuiet.Service -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o artifacts/service
if ($LASTEXITCODE -ne 0) { throw 'Service build failed.' }
& dotnet publish src/PocketQuiet.App -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o artifacts/app
if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
Write-Host 'Published to artifacts/app and artifacts/service. Run python scripts/package.py to package.'
