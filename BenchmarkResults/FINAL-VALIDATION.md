# Final verification

- Both full release matrices passed their retained audit.json checks: 900 cells and 162,000 raw samples each.
- Reference matrix contains 124,567 fresh GPU samples; legacy matrix contains 127,500.
- Final source/asset manifest: all 48 SHA-256 hashes match the measured reference build.
- Both Windows players built successfully; Burst AOT library is present.
- Unmanaged command validation: 10 checks passed, including parallel production, generation reuse, stale commands and the 64-byte record layout.
- Standalone visual capture completed for all rendering configurations. See Screenshots/Backends/validation.txt for live particle and instancing checks.
- Interactive player scene/technique/count/workload controls were exercised. The player was returned to the VFX showcase.
- Report browser checks passed for workload/resolution/count filtering, twelve table rows including baseline, scaling chart paths, visual image loading and CSV export.
- Exported selection CSV was read back with Python's csv parser: twelve rows with the selected 512-line / 1440p / Overdraw configuration.
- Python compilation and extracted report JavaScript syntax checks passed. Browser reported no errors or warnings during these checks.
- Separate development allocation probe is retained and explained in the report; its timings do not enter the release comparison.

The player logs have no managed exceptions. Direct3D reports an unavailable optional info-queue interface during startup; rendering and measured frame timing functioned and passed validation.
