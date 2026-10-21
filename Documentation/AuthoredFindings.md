# Authored effects and scaling

This study measures the refactored, asset-authored complete-effect renderer. Historical results in `BenchmarkResults/production/` describe the earlier hardcoded implementation and are kept separate.

## What changed for people using the project

An effect now composes reusable path, ribbon and particle-layer assets. Artists can duplicate a module, change its profile texture, HDR tint/gradient, size/width curve, speed, count, endpoint placement and release timing, and reuse it in several effects. The Effect Workbench edits those real assets and rebuilds the preview explicitly. It does not regenerate the scene or reset artwork.

Void Conduit demonstrates an additional traveling layer. Opal Transfer demonstrates composing an existing mote module with a newly authored module, an alternate profile, a gradient and a nontrivial size curve. The [artist workflow](ArtistWorkflow.md) explains both data operations and the point where a new algorithm requires programmer work. This is a portable vocabulary shared by all renderers, rather than unrestricted hand-authored VFX graphs.

Gameplay writes `Begin`, `Follow` and `Release` from Burst jobs using a cached `EffectId`. The managed owner bakes content and submits prepared native state to Unity renderers. The [healer integration](Integration.md) shows the actual job, dependency and ownership boundaries.

The adversarial review drove changes to authoring, native ownership, padding, HDR transfer, atlas orientation, zero-count streams, preview compilation, GUID preservation and configurable bounds. The [review](AdversarialReview.md) records the findings and limitations. [175 authoring checks](Validation/authoring-acceptance.json) and [47 native checks](Validation/native-validation.txt) passed. Standalone captures are under `Screenshots/Authored/`; their build identity and capture procedure are recorded in `capture-metadata.json`.

## What the high counts mean

The reference catalog reserves 96 ribbon cards and 84 secondary cards per ability: 180 total. Therefore 512 / 1,024 / 2,500 / 5,000 complete abilities request 92,160 / 184,320 / 450,000 / 900,000 cards. These counts are not a measurement of visible fragments or GPU memory.

VFX storage capacities also round up independently. In the measured graph compiler, shared graph selection rounds ability capacity to a power of two, then each stream's configured particle capacity rounds to a power of two. At 5,000 abilities, configured shared capacities total 1,933,312 particle slots for 900,000 spawned particles. The per-ability graph capacities total 1,180,000 slots across 5,000 components. These are graph configuration counts, not measured VRAM bytes or proof that every capacity slot executes each frame. Timing captures whatever work Unity actually performs.

The dense field has a fixed 36 × 19 m center footprint; Spread has a fixed 180 × 95 m footprint and a wider camera. Spread makes each line smaller on screen and changes background coverage. Each workload has its own matching baseline. This distinguishes practical density cases without claiming to isolate only overdraw.

All renderers use fixed library slots. Library composition and slot order can change padding and Shuriken orientation choices. There is no automatic LOD, compaction or large-map spatial culling. The environment retains six placeholder units; this isolates presentation scaling rather than rendering or simulating 5,000 RTS units.

## Reading the measurements

Use whole-frame CPU, render-thread and GPU values alongside explicit command/bridge timers. A smaller explicit bridge timer does not prove a smaller whole CPU frame. GPU deltas subtract matching whole-frame baseline medians; they are not isolated effect-pass timestamps. The asynchronous timing records do not establish same-frame CPU/GPU correlations.

Setup measures backend construction only. Native world allocation, old-pool disposal, catalog baking and deferred first use have different boundaries. API input bytes are calculated submission sizes; total-used-memory is a process counter, not isolated effect VRAM. Configured instancing is recorded separately from its measured outcome.

The byte counter covers explicit bulk buffer, mesh and particle submissions. It does not estimate Unity's internal packing of per-component scalar/vector setters. A per-ability VFX row showing zero bulk bytes therefore does not mean zero parameter updates or zero GPU traffic. Likewise, batch/SetPass counters describe Unity's recorded frame statistics; a hardware capture would be needed to assert exact GPU draw/dispatch grouping.

The benchmark runs an uncapped deterministic simulation advancing 1/60 second per rendered frame, with 120 warm-up and 240 measurement frames per cell. This measures throughput in the laboratory environment, rather than headroom in an already GPU-saturated RTS.

Scale covers complete sustained dense/spread effects through 5,000. The full matrix additionally covers smaller lifecycle, overlap, core-only and 1440p cases. It does not establish 5,000-count lifecycle or core-only rankings. See the [production guide](Production.md) for the exact matrix and counters.

## Completed measurements and current comparison

Open the [consolidated comparison](../BenchmarkResults/authored/comparison/report.html) or [report index](../BenchmarkResults/authored/report.html). The consolidated view selects 612 audited source cells / 146,880 frames across 17 conditions, with all twelve configurations and 131,078 fresh GPU timing samples. It does not pool populations across runs: each row retains its source build, source cell orders and original matched-baseline delta. Smoke, superseded rows and diagnostic runs remain separately accessible; the interrupted parent and its completed-prefix excerpt are not counted twice. Earlier clipped Spread rows are excluded.

Hardware: RTX 3070 / i9-11900F, Direct3D 12, Unity 6000.3.10f1 release players with Burst. Values below are medians of three per-repetition medians. The reports retain repeat ranges, p95 values, setup costs, counters and raw source data.

At 5,000 complete effects in the dense 1080p field:

| Technique | Whole frame p50 (ms) | GPU frame p50 (ms) | GPU delta (ms) | Explicit bridge p50 (ms) | Objects |
| --- | ---: | ---: | ---: | ---: | ---: |
| No lines / baseline | 1.269 | 0.835 | 0.000 | 0.000 | 1 |
| VFX · shared buffer | 11.067 | 10.725 | 9.853 | 0.025 | 2 |
| GPU · procedural ribbons | 10.941 | 10.863 | 9.960 | 0.101 | 2 |
| VFX · per line / batching on | 21.549 | 10.956 | 10.114 | 4.022 | 5001 |
| Unity LineRenderer | 29.589 | 13.569 | 12.739 | 11.799 | 15002 |
| Burst · combined mesh | 35.300 | 15.147 | 14.296 | 33.257 | 2 |
| Shuriken · shared billboards | 101.132 | 20.181 | 19.330 | 91.043 | 7 |
| Shuriken · instanced mesh | 104.160 | 33.153 | 32.316 | 101.653 | 7 |
| Shuriken · shared mesh | 145.946 | 22.120 | 21.270 | 134.905 | 7 |
| Shuriken · Burst particle jobs | 162.965 | 46.748 | 45.911 | 82.810 | 7 |
| Shuriken · per-line billboards | 142.452 | 23.066 | 22.229 | 90.026 | 25002 |
| VFX · per line / batching off | 262.729 | 37.430 | 36.594 | 3.280 | 5001 |

Each delta uses its row's own source baseline, which need not be the displayed baseline row. A low bridge value is not the entire CPU cost: per-instance VFX spends substantial time in later Unity work. CPU and GPU times overlap and must not be added together. These are the complete stacks, including secondary effects.

Whole-frame p50 milliseconds in dense 1080p:

| Technique | 512 | 1,024 | 2,500 | 5,000 |
| --- | ---: | ---: | ---: | ---: |
| VFX · shared buffer | 1.929 | 3.095 | 6.152 | 11.067 |
| GPU · procedural ribbons | 1.874 | 3.112 | 6.011 | 10.941 |
| VFX · per line / batching on | 3.339 | 5.461 | 9.875 | 21.549 |
| Unity LineRenderer | 4.032 | 8.347 | 15.955 | 29.589 |
| VFX · per line / batching off | 24.962 | 50.019 | 125.781 | 262.729 |

For hundreds of effects, Auto-batched per-ability VFX is a credible alternative to a shared-buffer graph in this scene. Batching disabled has a very different result. At 5,000, shared VFX and procedural rendering met this laboratory's 16.67 ms whole-frame threshold; per-ability Auto did not. This is not a 60 FPS guarantee for an RTS already consuming CPU/GPU budget.

Earlier Spread rows used the original 180 m far clip after the camera moved beyond that range. They clipped much of the field and are excluded from the consolidated full-field comparison. A corrected run uses a 500 m far clip, records camera properties and rejects any Spread cell whose active endpoints are outside the frustum. The 5,000-effect preview verified all 10,000 endpoints visible and 900,000 particles alive. Corrected full-field measurements replace the earlier clipped rows.

The [corrected Spread run](../BenchmarkResults/authored/run-20261020-190758-208-0a44c5/report/report.html) passed all 144 cells / 34,560 frames, including all eleven techniques plus baseline at four counts and three repetitions. At 5,000 complete effects in that wide 1080p field:

| Technique | Whole frame p50 (ms) | GPU frame p50 (ms) | GPU delta (ms) | Explicit bridge p50 (ms) |
| --- | ---: | ---: | ---: | ---: |
| No lines / baseline | 1.623 | 0.458 | 0.000 | 0.001 |
| VFX · shared buffer | 2.552 | 1.138 | 0.672 | 0.021 |
| GPU · procedural ribbons | 2.374 | 1.425 | 0.968 | 0.080 |
| VFX · per line / batching on | 17.422 | 2.597 | 2.139 | 4.958 |
| Unity LineRenderer | 29.766 | 9.238 | 8.686 | 12.129 |
| Burst · combined mesh | 34.332 | 6.350 | 5.892 | 32.592 |
| Shuriken · shared billboards | 100.671 | 31.074 | 30.617 | 90.573 |
| Shuriken · instanced mesh | 101.845 | 34.571 | 33.925 | 99.546 |
| Shuriken · shared mesh | 145.162 | 34.888 | 34.430 | 134.126 |
| Shuriken · Burst particle jobs | 161.824 | 34.804 | 34.346 | 81.319 |
| Shuriken · per-line billboards | 141.907 | 31.927 | 31.470 | 89.488 |
| VFX · per line / batching off | 252.557 | 14.275 | 13.341 | 3.197 |

Smaller screen coverage substantially lowers shared VFX/procedural GPU-frame costs. Component-heavy and copied-particle stacks retain substantial CPU costs. Shared VFX has the smaller GPU delta here, while procedural has the smaller whole-frame median; those are different measurements. The baseline's GPU medians ranged from 0.381 to 0.934 ms, so inspect matched deltas and repeat ranges rather than subtracting the displayed median yourself. Endpoint coverage does not guarantee every path excursion, particle or fragment is visible. Neither workload isolates overdraw alone.

## Particle-system tradeoffs and additional workloads

The [remaining-workload run](../BenchmarkResults/authored/run-20261020-184209-382-da299f/report/report.html) passed all 216 cells, including lifecycle at 128/256, heavy overlap, core-only and 1440p cases. GPU-instanced Shuriken mesh particles at 256 effects / 1440p / heavy overlap measured 0.801 ms GPU-frame delta versus shared VFX's 0.811 ms; whole-frame times were 5.377 versus 2.282 ms. That tiny GPU difference is not a strong ranking, particularly across the repeat ranges and with whole-frame GPU timing. It does not justify assuming Shuriken always saves GPU work.

Copied HDR particle/custom-data preparation and direct native-particle jobs both become expensive at high counts in this implementation. GPU instancing does not transfer simulation/preparation ownership to the GPU. These measurements do not cover every shader-offloaded Shuriken design or native module combination. A CPU-bound row's GPU-frame delta is not an isolated GPU busy-time measurement.

Core-only and lifecycle cases remain limited to the smaller counts in their recorded matrix. There is no claim of a measured 5,000-count lifecycle/core-only ranking.

## Corrections, failed runs and provenance

The original broader run stopped at its first lifecycle census because native state and asynchronous VFX alive counts differed by one instance. The failed parent remains unchanged. Its 252 fully completed sustained cells were retained as an independently audited [completed-prefix excerpt](../BenchmarkResults/authored/run-20261020-175024-380-d5a1d2-completed-prefix/report/report.html), with parent hashes and failure details in derivation.json. The revised driver freezes native inputs for 16 unmeasured frames before its census and resumes for eight unmeasured recovery frames. The subsequent workload run passed exact stream counts. Timed sampling is still 120 warm-up / 240 measurement frames per cell.

A separate engine probe showed that LineRenderer bakes colors into UNorm8 and clamps raw HDR values. The adapter now normalizes its eight gradient samples in Burst, transfers a per-ribbon multiplier, and reconstructs HDR in a LineRenderer-only shader variant. It also caches unchanged width curves. Actual baked geometry plus material parameters verified HDR reconstruction above 2. The [focused 102-cell LineRenderer run](../BenchmarkResults/authored/run-20261020-184837-229-c685cb/report/report.html) passed and takes priority in the comparison. The reference ribbons were within LDR, so earlier reference images did not lose HDR; their earlier adapter timings remain labeled as that version.

The earlier measured player is archived in Builds/AuthoredLab-bfbad434. The current Builds/AuthoredLab player is build bb6050f6ba5b410fa1541f07362d4b79 and built with zero errors. Its changes from the focused LineRenderer/workload player concern the Spread camera and its benchmark validation; other renderer implementations are unchanged. Source/binary manifests live with the runs. The first failed smoke export and interrupted full run remain marked failed; they are not silently reassigned as complete experiments.

The native particle reactivation probe also confirmed a retired per-ability owner disables, then returns with 96 core particles, 384 baked vertices and positive alpha. See [engine validation records](Validation/particle-reactivation.txt) and [HDR reconstruction](Validation/line-hdr-reconstruction.json).
