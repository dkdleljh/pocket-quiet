# Pocket Quiet

**Local, temperature-aware fan control for GPD Pocket 4 on Windows.**

Pocket Quiet combines a Korean desktop interface, a dedicated control service and an independent recovery watchdog. Version **0.2.0** adds a Silent preset, smoother preset changes, verified recovery state, sensor freshness, retained protection history and a testable controller.

![Pocket Quiet interface — demo values](docs/screenshot.png)

Manual control and EC access are restricted to **GPD G1628-04 / G1628-04-L, BIOS 2.11**. The original physical validation used G1628-04 / Ryzen AI 9 HX 370; the -L variant is protocol-mapped but not physically tested. Other devices/BIOS versions receive CPU/GPU monitoring only, without EC reads or writes. This remains a hardware-specific preview.

## 설치 / 업데이트

1. [Releases](../../releases)에서 전체 ZIP을 받아 압축을 풉니다. 개발 빌드는 [Actions](../../actions)의 검증된 아티팩트를 사용할 수 있습니다.
2. 기존 팬 제어 도구를 종료하고 `Install.cmd`를 실행합니다.
3. 설치기는 파일 SHA-256, 필요한 PawnIO 설치 파일 서명, 새 서비스의 준비 상태를 확인합니다. 기존 서비스 폴더를 보존하고 새 설치가 실패하면 복원을 시도합니다.
4. 바탕화면 바로가기로 앱을 열고 **적용 상태·정상 측정 시각·복구 감시**를 확인합니다.
5. 사용할 프리셋을 선택하고 **프리셋 적용**을 누릅니다.

서비스는 Program Files에 별도로 설치됩니다. 배포 폴더의 DLL 교체만으로 설치된 서비스가 갱신되지는 않습니다. 이전 UI를 완전히 종료한 뒤 새 배포 파일을 교체하세요. 부팅·서비스 재시작·절전 복귀 후에는 기기 기본 자동으로 시작하며 수동 프리셋을 자동 복원하지 않습니다.

설명서: [사용 안내](docs/USER_GUIDE.ko.md) · [소음 줄이기 / ANC 검토](docs/NOISE.ko.md) · [개발 명세](docs/DEVELOPMENT.ko.md) · [검증 기록](docs/VALIDATION.md) · [변경 이력](CHANGELOG.md)

## Quiet operation and safeguards

- Firmware, Silent, Quiet, Adaptive, Balanced, Cooling and 2–12-point Custom curves.
- Silent targets 30% through 55°C, 38% at 65°C, 65% at 75°C and 100% at 85°C. **It does not promise fan stop or 0dB.**
- Reapplying settings preserves output, smoothing, deceleration and stall detection. Sustained deceleration reaches the target rather than remaining 3% above it.
- Immediate output floors of 65% at 75°C and 90% at 80°C; immediate 100% at 85°C after sensor validation. These are application policy, not manufacturer ratings.
- Temperature/RPM/timestamp validation, persistent-stall detection, write/read-back verification and firmware fallback.
- Hardware restoration and disk persistence have separate outcomes. A failed save cannot resume manual control after firmware restoration.
- Failed readings are cleared; last sample time and last protection event remain visible. The chart shows real time and gaps.
- Watchdog startup/liveness monitoring, firmware fallback on observer loss and observer restart attempts.
- Local bounded asynchronous logs. No accounts, analytics, API keys, microphone recording or anti-noise playback.

“AI automatic” is deterministic local temperature-trend compensation, not a neural network or learning system. CPU load and package power are diagnostic readings, not control inputs.

Sustained high temperature requires cooling even in Silent mode. A Windows power-efficiency shortcut and the noise guide help address heat generation. The app does not automatically change power plans, TDP, CPU boost or voltage. Acoustic improvement and long-duration thermal stability require physical measurement.

The watchdog cannot guarantee recovery from OS/kernel hangs, failed drivers, hardware faults or arbitrary external EC writers. App/service executables are not Authenticode-signed; the independently signed PawnIO installer is verified separately. Deployment hashes detect corruption/mixed files, not publisher identity.

## Development

The distribution includes a `source` directory. Run build commands **inside that directory**, or at the root of a Git checkout. The SDK baseline is in `global.json` (.NET 10.0.403, latest patch roll-forward).

```powershell
./scripts/build.ps1
```

```bash
./scripts/build.sh
```

These run the portable policy/state tests and publish self-contained Windows app/service outputs. Locked NuGet restores prevent unnoticed dependency changes. Windows x64 is required for live service and WPF execution; cross-compilation is supported.

```bash
dotnet run --project tests/PocketQuiet.Tests -c Release
python -m pip install -r scripts/requirements.txt
python scripts/package.py
python scripts/package.py --verify 'artifacts/package/Pocket Quiet'
```

Packaging includes source, docs, dependency notices, the pinned official PawnIO installer, a deployment manifest and ZIP SHA-256. It rejects stale versions or different app/service Core binaries. Manifest source commit and dirty status distinguish a release build from uncommitted work.

For UI previews, run `PocketQuiet.exe --demo`. Every preview action, including reset and retry, is isolated from the real service. `--preview-mode Silent` selects a preview preset; `--screenshot PATH --exit-after-screenshot` captures a demo view.

## Contributing and license

Do not extend the device/BIOS gate merely because another product looks similar. Include authoritative EC references, reversible control/restore tests, temperature/RPM evidence and recovery results. Remove personal details from logs and screenshots.

Source is **GPL-2.0-or-later**. Hardware access credits: [Cryolitia/gpd-fan-driver](https://github.com/Cryolitia/gpd-fan-driver), [Linux GPD driver](https://github.com/torvalds/linux/blob/master/drivers/hwmon/gpd-fan.c), [chenx-dust/FanControl.GPDPlugin](https://github.com/chenx-dust/FanControl.GPDPlugin). Monitoring uses [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), driver access uses [PawnIO](https://pawnio.eu/). See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and [LICENSE](LICENSE). No GPD affiliation or endorsement is implied.
