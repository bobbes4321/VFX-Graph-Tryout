# Findings on this PC

The practical default is **one shared-buffer VFX Graph** when graph authoring and rich effects matter, or **procedural GPU ribbons** when you want a focused shader-driven line system. Their costs at a few hundred lines are very close in this workload. Both accept the same unmanaged command stream.

**Per-line VFX is viable at a few hundred lines, especially with automatic instance batching enabled.** It is not CPU-free: at 512 moving lines the measured main-thread frame was 1.528 ms with automatic batching, 4.588 ms with batching disabled, and 0.973 ms for shared VFX. These are complete frames in this light test scene, not isolated effect costs. At 5,000 lines the corresponding main-thread times were 7.498, 40.671 and 1.147 ms.

**Shuriken did not provide a GPU advantage over shared VFX or procedural ribbons in the representative 256/512-line moving tests.** Its mesh-instancing and particle-job options remain useful and are measured separately. Do not assume “CPU particles” automatically means cheaper GPU rendering.

## Representative measured costs

1920×1080, four line styles, moving endpoints, 16 segments per line. Each value is the median of three run summaries. GPU delta is a paired whole-frame baseline subtraction; bridge time includes renderer preparation, job completion waits and engine API submissions. Native command production/consumption is common and is reported separately in the explorer.

| Technique | 256: GPU Δ ms | 256: bridge ms | 512: GPU Δ ms | 512: bridge ms | 512: main frame ms |
|---|---:|---:|---:|---:|---:|
| VFX · shared buffer | 0.032 | 0.005 | 0.036 | 0.005 | 0.973 |
| GPU · procedural ribbons | 0.030 | 0.008 | 0.035 | 0.008 | 1.006 |
| VFX · per line / batching off | 0.045 | 0.158 | 0.069 | 0.300 | 4.588 |
| VFX · per line / batching on | 0.052 | 0.158 | 0.075 | 0.290 | 1.528 |
| Unity LineRenderer | 0.055 | 0.179 | 0.120 | 0.377 | 1.682 |
| Burst · combined mesh | 0.122 | 0.130 | 0.217 | 0.243 | 1.444 |
| Shuriken · shared billboards | 0.074 | 0.208 | 0.172 | 0.417 | 1.395 |
| Shuriken · per-line billboards | 0.115 | 0.608 | 0.215 | 1.264 | 3.031 |
| Shuriken · shared mesh | 0.084 | 0.227 | 0.169 | 0.706 | 1.719 |
| Shuriken · instanced mesh | 0.081 | 0.204 | 0.148 | 0.423 | 1.322 |
| Shuriken · Burst particle jobs | 0.077 | 0.137 | 0.172 | 0.236 | 1.613 |

The report exposes repeat ranges, p95, worst sampled bridge spikes, 1440p, static, churn, heavy overlap, geometry-density sweeps and raw CSV export. Small differences should be judged against those ranges.

## What the experiment establishes

* **VFX Graph is fast enough for this task.** Shared VFX rendered 5,000 links / 80,000 segment particles with roughly 0.93 ms total GPU frame time (0.52 ms paired GPU delta) and 1.15 ms main-thread frame time in this scene. That case had noticeable repeat variation; inspect its range rather than treating those rounded numbers as constants.
* **GameObject/instance count matters on the CPU.** At 5,000 links, disabling VFX instance batching produced a roughly 40.7 ms main-thread frame; automatic batching reduced that to about 7.5 ms. A shared system remained near 1.1 ms. The difference is visible well before 5,000 objects; whether a few hundred is acceptable depends on the game's remaining CPU budget.
* **Burst preparation changes the LineRenderer result substantially.** At 256 links, bridge time fell from about 1.310 ms with main-thread point/color sampling to about 0.179 ms with Burst preparation. Both versions are retained as independently measured matrices. The reference comparison uses the Burst version.
* **Bridge timers alone miss engine work.** For example, per-line VFX engine processing is more expensive than its setter loop alone suggests. Likewise, Shuriken's particle-job path avoids a bulk SetParticles copy, but still has native simulation/render preparation. Compare main/render/GPU frame data as well as the bridge.
* **GPU instancing is not a guaranteed win.** The Shuriken instanced mesh path is a real, visually verified instanced shader, but at 256 links its GPU time was close to the ordinary mesh path. At higher counts/workloads the tradeoff changes.
* **Static and sparse workloads deserve separate treatment.** Static VFX simulation pauses after initialization. Churn tests pooled starts/stops; shared fixed-slot paths keep inactive slots and alpha-zero geometry. The results do not silently assume active-list compaction or spatial partitioning.
* **One quad per link is a different fidelity choice.** It reduces curvature and color/dash sampling quality. Compare its costs only if that simpler appearance fits the game.

## Choosing a production path

| Path | Strong reason to choose it | Main tradeoff |
|---|---|---|
| Shared VFX | Rich GPU effects, one bulk endpoint upload, graph editing | Buffer indexing, capacity management and shared bounds |
| Per-line VFX with batching | Simple independent effect/prefab authoring and lifecycles | More component/property/engine CPU work; batching support depends on the graph |
| Procedural ribbons | Compact endpoint data and very little CPU submission work | Maintain shader geometry, bounds and visual features yourself |
| Burst-prepared LineRenderer | Familiar joins, width curves and materials | Per-renderer calls and gradient work remain on the main thread |
| Burst combined mesh | Full geometry control and one renderer | More CPU-generated vertex data and bulk upload bandwidth |
| Shared Shuriken / particle jobs | Familiar particle modules and CPU-side integration | Particle copies or callback scheduling plus engine render preparation |
| Per-line Shuriken | Independent classic effect lifecycles | Component/system overhead scales with line count |

Gameplay integration is equally simple for all of them: Begin, Update and Stop enqueue unmanaged commands from Burst. The main authoring difference is the graph/shader/component workflow, not the healer's gameplay code. See `Integration.md` and `HealerExample.cs`.

## Evidence and limits

Two release matrices contain **1,800 cells and 324,000 raw frame samples**. Both passed the audit for completeness, matching raw GPU medians, unique fresh GPU timestamps, required live particle counts, instancing configuration and actual output resolution. Source/binary hashes and build/configuration snapshots are retained with each run.

A separate development-player allocation probe at 256 moving lines found **zero incremental steady-state managed allocation** for every rendering configuration: all matched the baseline's 368 bytes/frame. The first two samples contain delayed allocation counters from the harness's particle-validation arrays; the report excludes those two samples from its steady-state allocation table and preserves the unfiltered maxima and raw data. Pool creation is outside that interval. Release-frame timings and development allocation measurements are kept separate.

The reference build is Windows / Direct3D 12 / Unity 6000.3.10f1 on an RTX 3070 and i9-11900F, with no VSync or FPS cap. The 60 fps budget is 16.67 ms for the **whole game**. This small arena does not include RTS AI, animation, terrain complexity or a competing game GPU workload. CPU wall times include waits and do not sum worker-core cycles. Whole-frame GPU spans are not isolated GPU-pass busy time. See `Methodology.md` for fidelity differences, inactive-slot behavior, counter limitations and the exact matrix.
