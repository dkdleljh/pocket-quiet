Pocket Quiet 0.1.0 — hardware-tested preview for GPD Pocket 4

Windows x64 self-contained desktop application with AI-labelled local adaptive mode, Quiet/Balanced/Cooling/Custom presets, temperature/RPM monitoring, a separate Windows service, thermal safeguards, and a heartbeat watchdog.

Download the ZIP, extract it completely, and run Install.cmd. The package includes Korean HTML/Markdown manuals, developer specifications, exact source, licenses, and the unmodified official signed PawnIO 2.2.0 installer. Verify the ZIP with SHA256SUMS.txt.

Manual control is restricted to GPD Pocket 4 G1628-04 / G1628-04-L with BIOS 2.11. Physical tests were on G1628-04 / Ryzen AI 9 HX 370 only. Other systems are monitoring-only. AI mode is deterministic local adaptive control, not a machine-learning model or cloud service.

Validated: 31 automated assertions, 50,000 randomized decisions, physical output/RPM changes and firmware restore, service IPC, GUI, forced process termination and hung-process recovery. Long-duration thermal qualification and actual suspend/resume remain untested. This is an early release, not universal or safety-certified fan control.

The app/service executables are unsigned; the PawnIO installer is signed by its publisher. Keep Windows security enabled. The service starts in firmware mode after restart.
