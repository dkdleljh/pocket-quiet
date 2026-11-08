# Pocket Quiet 0.3.0 — unsigned development build

This build prepares public signing and adds reversible heat reduction. It must not be advertised as a Smart App Control fix until public signing and live Windows verification are complete.

- Low-heat plan: CPU boost disabled, energy-efficiency preference increased, original plan preserved. Lower peak performance is expected; restore is available in the UI.
- Maintenance: `PocketQuiet.exe --install`, `--recover`, `--uninstall`.
- Installer checks all runtime EXE, DLL and PowerShell signatures before stopping an installed service when SAC is enforced.
- Package finalization rechecks signatures on Windows, updates file hashes and regenerates the ZIP checksum after signing.

See [Code signing policy](docs/CODE_SIGNING.md) and [validation](docs/VALIDATION.md). Existing thermal floors, supported-device gates and firmware fallback are unchanged.
