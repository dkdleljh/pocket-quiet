# 0.3.0 — signing preparation and low-heat power

- Add reversible low-heat power plan, persistent original-plan backup, readback and rollback. Add explicit desktop apply/restore controls with isolated demo behavior.
- Add executable `--install`, `--recover`, `--uninstall` entry points for signed deployments.
- Fail installation before stopping the existing service when enforced SAC encounters unsigned components.
- Add certificate-store / Artifact Signing support, complete runtime signature inventory, signed-package finalization and exact manifest inventory checks.
- Public signing and end-to-end execution under SAC remain pending. This is not a signed release.

# Changelog

## 0.2.0

- Add Silent (무소음 · 최소 소음), targeting 30% through 55°C without disabling thermal/stall safeguards or promising fan stop/0dB.
- Avoid the initial 60% burst when a fresh sample confirms a running fan at or below 55°C; keep conservative startup for other conditions.
- Skip hidden-window chart rendering and reduce hidden UI polling to five seconds while the controller stays at one second.
- Preserve output, smoothing, deceleration and stall state when settings are reapplied. Complete deceleration to the curve instead of remaining 3% above it.
- Enforce immediate 75°C/65% and 80°C/90% output floors; retain the 85°C/100% override.
- Extract testable controller state, settings persistence, IPC, watchdog and asynchronous logging.
- Separate measurement time from controller time; clear failed readings and retain protection history.
- Cancel manual requests before restoring firmware; disk persistence failures cannot reactivate manual mode. Never confirm restoration without hardware verification.
- Apply the exact device/BIOS gate to all EC access, including recovery and watchdog paths.
- Monitor watchdog startup/liveness, release manual control on loss and retry the observer.
- Isolate every demo command, prevent duplicate real UI instances, add applied/draft state, time axes, curve previews and 2–12-point editing.
- Add power-efficiency settings shortcut and a sourced fan-noise/ANC guide; no automatic power-plan changes or anti-noise playback.
- Add bounded IPC parsing, atomic reset, locked dependency restores, deployment hashes and staged installation with rollback.

## 0.1.1

- Add the original mint fan badge to the executable, title bar, taskbar, desktop shortcut, sidebar, and system tray.
- Include ten alpha-preserving ICO sizes from 16 to 256 pixels.
- Preserve the existing controller service and fan settings during the desktop UI update.

## 0.1.0

- Initial GPD Pocket 4 Windows implementation.
- Adaptive, quiet, balanced, cooling, custom, and firmware presets.
- WPF/tray interface, privileged service, heartbeat watchdog, guarded EC writes.
- Real hardware smoke tests, deterministic policy tests, Korean manuals, reproducible build scripts.
- Starts in firmware mode; long-duration and real suspend/resume qualification remain open.
