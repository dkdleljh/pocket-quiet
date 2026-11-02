# Pocket Quiet 0.2.0

Adds **무소음 · 최소 소음** (Silent), a low-temperature fan curve, while keeping the 30% minimum and thermal/stall protection. Silent does not mean fan stop or guaranteed 0dB. Sustained heat still requires high fan output.

Preset changes now preserve control history and output. Firmware recovery cannot be undone by a failed settings save. Missing/failed measurements are cleared, firmware confirmation is explicit, and protection events remain visible. All EC operations use the same model/BIOS gate. The service monitors its watchdog and writes logs off the cooling thread.

The UI adds applied/draft state, measurement time, watchdog health, curve preview, 2–12-point editing, time axes/gaps, single-instance activation and a completely isolated demo. A Windows power-efficiency shortcut and a sourced noise guide explain how to address persistent fan airflow noise and why speaker-based ANC is not provided.

The installer verifies package hashes, stages service updates, checks readiness and attempts rollback on failure. Run Install.cmd after replacing the full package; the installed Program Files service does not update when only the desktop files change.

See docs/VALIDATION.md for automated and physical verification. Long-duration load, acoustic dB reduction, actual sleep/hibernate cycles, other BIOS versions and the -L variant are not certified. App/service executables remain unsigned; the PawnIO installer has its publisher's signature.
