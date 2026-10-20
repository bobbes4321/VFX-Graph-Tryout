# Final complete-effect verification

- Final Windows release build succeeded with zero errors. Its one build warning concerns the disabled Pipeline runtime configuration; the player does not run a CLI server.
- Ten native/Burst lifecycle checks passed, including parallel production, stale handles, frozen release endpoints, retained tails and skipped frame boundaries.
- Final full audit passed: 360 cells, 86,400 raw samples, 75,612 fresh GPU samples. See reference/audit.json.
- Final smoke preflight passed: twelve cells, 1,440 raw samples and 1,242 fresh GPU samples.
- All 125 source/asset hashes and three release/Burst binary hashes match the measured final build. The earlier line-only reference's 48 source/asset hashes still match too.
- Final standalone capture completed for all twelve configurations including baseline, plus three close views and the interactive diorama. Particle paths report 540 live particles for the three-ability complete recipe; per-ability Shuriken uses 17 presentation objects for three complete abilities.
- Report checks passed for measured-case filtering, twelve table rows, twelve scaling paths, image loading and CSV download. The downloaded CSV parsed as twelve correct 256-ability / 1440p / Overdraw / Complete records.
- Python compilation and extracted report JavaScript syntax checks passed. Browser console reported no errors or warnings.
- Native standalone UI checks passed for close-view focus, complete/core-only toggles and selecting the LineRenderer hybrid before returning to shared VFX.
- Release benchmark, final capture and interactive-player logs contain no managed exceptions, missing assets or shader errors.

The final player was left running with shared VFX, complete effects, three hero abilities, sustained movement and Renewal camera focus. The report is open in the app browser. Preflight/partial measurements remain explicitly excluded.
