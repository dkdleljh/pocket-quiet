#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project tests/PocketQuiet.Tests -c Release
dotnet publish src/PocketQuiet.Service -c Release -r win-x64 --self-contained true -o artifacts/service
dotnet publish src/PocketQuiet.App -c Release -r win-x64 --self-contained true -o artifacts/app
