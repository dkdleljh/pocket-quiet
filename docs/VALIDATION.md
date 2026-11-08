# Validation report — 0.3.0 development / signing pending

Date: 2026-11-08 (Asia/Seoul). Physical target: GPD G1628-04, BIOS 2.11, Windows build 26300.9818.

## Verified in this session

- GitHub default-branch revision matched the initial local source (`b52bec4`).
- SAC was enforced (`VerifiedAndReputablePolicyState=1`). Event 3077 identified the original unsigned desktop executable. Security policy was not modified.
- 94 controller/state/protocol checks passed, including the existing 60,000 randomized decisions. Windows app/service self-contained builds completed with locked restores.
- Nine Python package checks passed: integrity, extra executable, missing/tampered file, version/path rejection, non-signable signing-stage change rejection, failed signature check preserving the manifest, and post-verification hash regeneration. Signature calls in the finalization unit tests are mocked; they are not evidence of actual public signing.
- All PowerShell scripts parsed on Windows PowerShell 5.1.
- The newly compiled 0.3.0 WPF executable launched normally while SAC remained enabled. A demo screenshot was inspected. UI Automation exercised both demo power buttons and verified that the actual plan and backup did not change.
- The packaged real UI connected to the installed 0.2.0 service and displayed real sensors. UI Automation exercised original-power restore and low-heat apply, with actual Windows power-plan readback.
- Power integration checked repeated apply preserving the original backup, restoration of the original High Performance plan, repeated restore, and preservation of an independently selected Balanced plan. Low Heat was reapplied afterward.
- The existing service accepted Silent, reported 30% output and healthy watchdog during a 60-second sample, then verified firmware restoration. A later status sample confirmed Ready=true, WatchdogHealthy=true and no error.
- Unsigned install preflight rejected the package before stopping the existing service (PID unchanged). Runtime signature inventory: 665 of 687 files valid, 22 unsigned, including project files, scripts and five upstream sensor libraries.

## Observations, not an acoustic benchmark

Before power changes, 30 samples over about one minute averaged 42.13°C, 2,586.53 RPM, 4.80 W and 3.35% CPU load. The final 30 samples of a two-minute Low Heat observation averaged 38.61°C, 2,499.20 RPM, 3.28 W and 4.92% CPU load. Workload was not held constant; these readings do not establish a causal reduction percentage or a dB result.

During Silent with Low Heat, the final 15 samples averaged 36.16°C and 2,523.07 RPM at 30% fan output. Firmware automatic was slightly quieter by RPM in this light workload, so the machine was left in Firmware with Low Heat power active. No lower fan floor, undervolting, TDP write or thermal-protection reduction was introduced.

## Still required before completion

- Obtain an eligible public signing identity and resolve all upstream DLL signatures. No provider application has been submitted and no public signature was produced.
- Verify the actual signed-file path through final ZIP download, installation, UI, service, watchdog, power buttons, recovery and reboot under enforced SAC. Current local unsigned-build acceptance is not a durable public-trust guarantee.
- The installed service remains 0.2.0; the new service was built but not installed because its signature preflight correctly fails. The 0.3.0 UI is protocol-compatible with this running service.
- Sustained CPU/GPU load, sleep/resume, ambient-temperature variation and acoustic measurements remain unqualified. No “perfectly silent” or universal future compatibility claim is made.

Local raw evidence is in `artifacts/validation` (not committed; contains this machine's runtime data).

---

# Validation report — 0.2.0

Date: 2026-11-03 (Asia/Seoul). This section records this update; historical 0.1.x results follow separately.

## Automated and build verification

- 94 checks passed on Linux and again in a self-contained Windows test executable, including 60,000 randomized decisions across all six manual modes.
- Regression coverage: hot/cold preset reapply, cold manual entry without a 60% burst, stopped-fan startup, true minimum-output convergence, immediate thermal floors, retained stall counters, missing/stale/repeated sensor data, failed manual saves, firmware save failures, failed EC restoration, watchdog loss/recovery, suspend/resume with replacement hardware, unknown BIOS denial, immutable snapshots, atomic settings persistence, strict JSON/line limits, real duplex-pipe framing and cancellation, and isolated demo actions.
- App and service win-x64 self-contained publishes succeeded with locked dependency restores. App/service Core binaries have identical SHA-256 hashes.
- PowerShell scripts parsed successfully. The full deployment manifest was checked both by Python and by Windows PowerShell, including Korean document filenames.
- WPF demo executed on Windows. UI Automation verified adding a sixth curve point, invalid input disabling Apply, valid input enabling Apply, apply and demo reset. Screenshots were inspected. Firmware restore controls stay visible below the scrollable content.

## Physical Windows integration

- Upgraded the installed service through the staged installer; previous files were retained. The new service reported version 0.2.0, Ready=true, WatchdogHealthy=true and verified Firmware state.
- An invalid 0% custom curve was rejected through real IPC.
- Silent was briefly applied on the allowlisted G1628-04 / BIOS 2.11, using verified EC writes, then firmware restoration was explicitly confirmed.
- Terminated only the watchdog child while the service was in firmware mode. The service detected observer loss and launched a replacement; readiness and watchdog health recovered while retaining firmware mode.
- Real desktop duplicate-launch regression passed: the second process exits successfully and the existing window is activated, leaving exactly one desktop process. Activation uses a pipe connection without an unread payload that could block shutdown.
- These were short functional checks. No acoustic dB reduction or thermal stability certification is claimed.

## Remaining physical validation

Long-duration CPU/GPU load, actual sleep/hibernate cycles, thermal behavior across room temperatures and charging states, acoustic noise measurements, failed physical fan hardware, other BIOS versions, and the G1628-04-L variant remain unqualified. Simulated suspend/resume and storage failures do not substitute for those physical tests. Installer rollback is implemented and reviewed, but an intentionally failed physical installation was not injected in this session.

---

# Validation report — 0.1.0

Test date: 2026-11-01. Test device: GPD G1628-04, AMD Ryzen AI 9 HX 370, BIOS 2.11, Windows 11 Home x64 build 26300. These results apply to this device only.

## Automated policy tests

31 assertions passed, including 50,000 randomized decisions across five manual presets. Verified invalid/missing temperatures, invalid RPM, stale/future/repeated timestamps, persistent fan stall, emergency override, output bounds, delayed deceleration, interpolation, configuration rejection, and relative preset behavior.

## Physical fan test

Test sequence used three output levels, six samples at each level, then an unconditional firmware restore. Outputs were read back from the EC after each write. RPM was read using high/low EC registers, consistent with the upstream Linux driver.

| Requested output | Representative stabilized RPM | EC raw value |
|---|---:|---:|
| 60% | 4,728–4,739 | 147 |
| 45% | 3,756–3,776 | 110 |
| 35% | 3,075–3,080 | 86 |
| Firmware automatic | Dynamic firmware policy | 0 (verified) |

Temperatures during this short exercise ranged approximately 48–61°C. These are functional observations, not an acoustic measurement or thermal stability certification. No dB measurement was made.

## Windows integration

- Official PawnIO installer 2.2.0 Authenticode signature: valid; signer namazso.eu. Driver running.
- Self-contained Windows service and WPF builds: succeeded.
- Real sensor values in the service and UI: verified.
- Adaptive preset applied through the pipe and produced a hardware output.
- Unsafe custom output (0%) rejected.
- Reset returned the active mode to Firmware.
- WPF screenshot captured and inspected; temperature history rendering checked.
- Service forcibly killed during control: SCM restarted it; new process ready in Firmware mode.
- Service process deliberately suspended: heartbeat watchdog terminated it and SCM restarted it.
- Normal service stop followed by independent probe: EC control register 0.
- Install/update and recovery paths exercised on the test computer.

## Explicitly not certified / not exhaustively tested

- Long-duration CPU/GPU stress, acoustic noise reduction, battery runtime, and performance impact.
- Actual sleep, hibernate, and resume cycles (handlers implemented, not physically exercised).
- Other BIOS versions, other Windows builds, and the G1628-04-L hardware variant.
- Windows/kernel crash, broken driver calls, failed fan hardware, or simultaneous arbitrary third-party EC writers.
- All Windows Defender / enterprise application-control configurations.

Firmware safety mechanisms remain enabled. Manual mode starts only on user request after restart. See the user guide for recovery steps and the specification for remaining limitations.

## Icon update — 0.1.1

- Generated an original transparent mint fan badge with the built-in image generator.
- Validated all ten ICO directory entries against their embedded 32-bit RGBA PNG dimensions (16 through 256 pixels).
- Published the Windows application successfully and extracted its new executable icon.
- Visually verified the live WPF sidebar, taskbar icon, and actual Windows notification-area icon.
- Updated the desktop shortcut to the explicit ICO path and refreshed the Windows shell icon cache.
- Confirmed the fan-control service PID stayed unchanged during UI replacement. No fan-control algorithm changed.
