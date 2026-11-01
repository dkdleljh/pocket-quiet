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
