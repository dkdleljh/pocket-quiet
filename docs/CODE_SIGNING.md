# Code signing policy

## Current status

0.3.0 is an **unsigned development build**. No public certificate or signing service has been connected. Do not call it SAC-compatible yet. A local hash manifest is an integrity check, not proof of publisher identity. Self-signed certificates, adding local trusted roots, changing file names, removing download marks or launching via another runtime are not used as fixes.

On the tested GPD G1628-04 / BIOS 2.11, SAC was enforced (`VerifiedAndReputablePolicyState=1`); Code Integrity event 3077 identified the unsigned `PocketQuiet.exe`. The installed 0.2.0 service was already running. Its continued operation does not prove a replacement or a fresh boot will be allowed.

Microsoft explains [trusted signing for SAC](https://learn.microsoft.com/en-us/windows/apps/develop/smart-app-control/code-signing-for-smart-app-control) and [why an individual app cannot be exempted](https://support.microsoft.com/en-us/windows/security/threat-malware-protection/smart-app-control-frequently-asked-questions).

## Coverage and provider prerequisites

All runtime EXE, DLL and PS1 files, including service dependencies, are inventoried. Existing valid vendor signatures are preserved. On this machine, the unsigned third-party DLLs include BlackSharp.Core, DiskInfoToolkit, HidSharp, LibreHardwareMonitorLib and RAMSPDToolkit-NDD. A signed main EXE alone is insufficient evidence.

Available public-trust routes:

- A CA-issued code signing certificate with its private key in a hardware/cloud provider accessible through the Windows certificate store.
- Azure Artifact Signing with completed Public Trust identity validation and a certificate profile. Consult [current eligibility](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart): Korean organizations are listed; individual developer availability is currently US/Canada. A private-trust profile is not a substitute.
- [SignPath Foundation](https://signpath.org/) for an approved OSS project. Its [terms](https://signpath.org/terms.html) restrict signing upstream binaries with the project's certificate. The unsigned sensor dependencies need approved upstream signatures or explicit resolution with SignPath before this route can provide complete coverage. An accepted project alone does not resolve that gap.

No provider application has been submitted and no terms have been accepted on behalf of the maintainer. No private key or password belongs in this repository or chat.

## Build, sign, verify

1. Build with `scripts/build.ps1` and package with `python scripts/package.py` from the source root. Run the policy/state and package tests.
2. Preserve that unsigned package as the source-of-record artifact. Sign a copy without changing its source or other non-signable files.
3. With an approved commercial certificate/provider, run on Windows:

```powershell
./scripts/Sign-Package.ps1 -Package 'artifacts/package/Pocket Quiet' `
  -SignTool 'C:\path\to\signtool.exe' -Thumbprint 'CERTIFICATE_SHA1_THUMBPRINT'
```

Use `-LocalMachine` only when the certificate is in that store. For Artifact Signing use `-Dlib PATH -Metadata PATH` instead of `-Thumbprint`. Metadata contains Endpoint, CodeSigningAccountName and CertificateProfileName, not a secret key. Follow the [official client setup](https://learn.microsoft.com/en-us/azure/artifact-signing/how-to-signing-integrations). Timestamp with the provider's supported RFC 3161 URL via `-TimestampUrl` if required.

For SignPath Foundation, use its reviewed artifact configuration and trusted GitHub build integration instead of the commercial-certificate script. Do not sign upstream dependencies in violation of the provider's terms.

4. `Test-Signatures.ps1 -Package PATH -RequireValid -ReportPath signatures.json` must pass. This checks current files, not a cached claim. The report includes each file hash, status, signer and timestamp presence. A valid chain on a developer workstation is necessary evidence, not proof of public reputation or SAC acceptance.
5. Run `python scripts/package.py --finalize-signed PATH` **on Windows**. It verifies the current signatures again, rejects inventory or non-signable content changes, refreshes the manifest and rebuilds ZIP/SHA256SUMS. Do not run normal packaging again afterward, as it would replace signed files with unsigned build output. Do not edit any signed script afterward.
6. Download/extract the final ZIP through the normal delivery path on the target PC. Keep SAC enabled. Launch `PocketQuiet.exe --install`; then launch the UI, verify service readiness, sensors, watchdog, a supported preset, recovery, stop/start and reboot. Inspect fresh Code Integrity events. Confirm every shipped executable path, including maintenance and power controls, works.

Only after these checks may a release be described as tested with SAC on that Windows build. Do not promise universal future allowlisting.

## Maintainer review and privacy

The GitHub repository is `dkdleljh/pocket-quiet`. Signing must use a reviewed revision and a protected signing identity. Do not expose signing credentials to pull-request code. The maintainer must assign author/reviewer/approver roles and confirm MFA before enabling a hosted signing service. These administrative facts have not been verified in this session.

The application itself does not transmit telemetry, record a microphone, or contact a signing service at runtime. Logs stay local. Build tools download dependencies; Windows may perform its own security/reputation checks.

## SignPath application draft (not submitted)

- Project: Pocket Quiet — local Windows fan control for GPD Pocket 4.
- Repository: https://github.com/dkdleljh/pocket-quiet
- License: GPL-2.0-or-later; dependency notices included.
- Build: Windows GitHub Actions, pinned .NET SDK, locked NuGet restore, portable controller tests, self-contained win-x64 app/service, complete source and hashes in package.
- Signing scope requested: project EXE/DLL files and PowerShell installation/recovery/power scripts. Preserve Microsoft and PawnIO signatures.
- Question requiring provider resolution: how to obtain approved signatures for the unsigned third-party sensor libraries before claiming full SAC compatibility.
- Maintainer-supplied fields still needed: contact, assigned signing roles, MFA confirmation and acceptance of provider terms.
