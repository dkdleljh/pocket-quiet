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
