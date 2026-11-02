#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore tests/PocketQuiet.Tests --locked-mode
dotnet run --project tests/PocketQuiet.Tests -c Release --no-restore
dotnet publish src/PocketQuiet.Service -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o artifacts/service
dotnet publish src/PocketQuiet.App -c Release -r win-x64 --self-contained true -p:RestoreLockedMode=true -o artifacts/app
