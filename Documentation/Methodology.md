# Measurement protocol

The report answers a hardware-specific question: what do these implementations cost on this RTX 3070 / i9-11900F, with the supplied URP scene and line workloads? It does not claim an exact universal cost for a Unity feature.

## Matrix

* Moving, 1080p: 1, 32, 128, 256, 512, 1,024, 2,500 and 5,000 slots at 16 segments each.
* Moving, 1440p: 128, 256, 512 and 1,024 slots at 16 segments.
* Static, churn, and overdraw: 128/512 slots at 1080p; 512 slots at 1440p.
* Geometry density: 256/512 slots at one segment; 256 slots at eight and 32 segments, at 1080p.
* Every listed configuration includes all techniques and a no-lines baseline, with three repetitions. The smoke suite is only a functional preflight at 256 moving lines and is not the final statistical comparison.

The main full run uses a visible non-development Windows player, Burst enabled, Direct3D 12, no VSync, no frame-rate cap, 90 warmup frames and 180 measured frames per cell. The three repetitions run in one player process, with fresh pools and warmup for each cell; they are not three cold process launches. Technique order is shuffled within each configuration/repetition using seed 190626. Effects use a fixed 60 Hz simulation clock indexed by frame, so each backend gets the same endpoint sequence regardless of throughput.

The player must remain visible: Windows can skip rendering a hidden/minimized window. The harness rejects cells with missing GPU timing, insufficient live particles, or incorrect output resolution. The benchmark does not draw the interactive HUD or unit placeholders. It renders the same static arena and post-processing for every technique, including the baseline. The visual atlas is separate from timed runs. This avoids benchmarking UI string construction or changing character counts.

## Costs

* **GPU frame:** FrameTimingManager GPU time in milliseconds. This is the complete rendered frame, including shared scene/post-processing. The paired baseline delta estimates incremental work, with repeat variation shown. No fallback estimates are substituted for missing GPU samples.
* **CPU main/render:** The corresponding whole-frame FrameTimingManager fields, also in milliseconds. Present wait is exported separately. These values include engine work that happens after the bridge. Do not add CPU and GPU times; they overlap.
* **Commands:** Wall-clock time from scheduling the deterministic Burst producer through command consumption completion. This is common work, including baseline.
* **Bridge:** Wall-clock presentation method duration, including renderer-specific Burst job completion, uploads, property setters, and submission calls. The particle-job path includes the last completed callback duration because Unity requires scheduling in OnParticleUpdateJobScheduled. It excludes later render-thread/engine work, so it is not the total CPU cost of a technique.
* **Frame interval:** Actual unscaled frame interval. Useful for end-to-end headroom against 16.67 ms at 60 fps; presentation/OS scheduling may affect it.
* **Pool setup:** Synchronous initialization/resource creation, before warmup. It is outside steady-state samples. Some engine work and shader first-use costs are deferred. Raw teardown belongs to the previous pool and includes disposal calls, not the entire deferred Destroy lifecycle.
* **GC/batches/set-pass:** ProfilerRecorder counters where available. Missing is reported as missing. GC in a release player can be unavailable. Total used memory is process-wide, not isolated VRAM per technique.
* **Bulk-data input bytes:** Explicit line/vertex/particle arrays and LineRenderer position arrays submitted by this bridge. Scalar/vector property setters, gradients and internal Unity traffic are excluded. Zero means no explicit bulk array in this counter, not zero data transfer. They are not measured PCIe bytes; Unity can copy, batch or upload internally. Particle-job writes remain within native particle storage.

Frame timing data arrives asynchronously. Only newly stamped data is counted. Warmup prevents old configuration samples crossing into a measured cell; timing stamps are exported for audit. Command/bridge timers refer to the current CPU frame, while frame-timing fields refer to a recently completed frame. Per-row timestamps preserve that distinction.

The release player does not expose the GC allocation counter on this setup. A separate development-player smoke run measures allocations at 256 moving lines, with 120 samples per technique. Its timings are excluded from the release comparison. The allocation table excludes its first two samples because delayed counters include the harness's pre-measurement particle-validation array allocations. Unfiltered raw maxima remain visible. The remaining samples all match the 368-byte/frame baseline; this establishes no incremental steady-state managed allocation in this probe, rather than zero allocation throughout initialization or every possible workload.

The report aggregates each independent run to a median and p95, then uses the median of those run summaries. Min/max repeat ranges show observed reproducibility. Paired baseline deltas match resolution, count, geometry density, workload and repetition. They can be negative when costs fall below noise. Do not turn tiny differences into a ranking claim without considering those ranges.

## Workloads and fidelity

**Static:** Initialize once, freeze curves and endpoints, avoid redundant data uploads. VFX simulation pauses after four initialization frames and retains cached GPU attributes; a static custom mesh needs no reconstruction. The particle-job system also pauses after its first frame.

**Moving:** Update every line each frame. Normal line length remains 7.8 metres at every count; centers fill the same arena, so higher counts increase overlap rather than quietly shortening the lines. CPU methods, including LineRenderer point/color sampling, sample the curve in Burst. Renderer API submissions stay on the main thread. VFX and procedural property IDs are cached. VFX and procedural methods sample the same formula on the GPU.

**Churn:** Every 30 simulation frames, Begin all slots using a new generation. At frame 15, Stop half. Per-line objects are pooled and disabled/re-enabled; shared systems keep allocated inactive slots. This measures pooled lifecycle cost, not repeated Instantiate/Destroy. The particle count field sampled during warmup can reflect either half of the churn cycle; it is not an expected fixed active count.

**Overdraw:** Concentrate 20-metre, 0.7-metre-wide lines in a small region. This increases transparent overlap and separates geometry/dispatch cost from pixel fill cost.

Styles cycle evenly through healing, electrical arc, plasma and dashed order (the one-line case uses healing). All techniques use the same curve formula, line width, common HDR profile texture, additive blending, camera and segment budget. LineRenderer makes a connected strip, with an eight-key color gradient, while other methods use overlapping independent quads. Billboards use engine-generated facing. Shuriken colors quantize to Color32. These differences are documented rather than pretending the methods produce identical pixels. One-segment cases are straight ribbons and intentionally trade away curvature.

VFX graph capacity is selected from compiled power-of-two variants. Per-line VFX is measured with both disabled and automatic asset instance batching. Mesh Shuriken is measured with and without a compatible procedural GPU-instancing shader. Particle systems are explicitly positioned persistent particles; emission/automatic movement are disabled. SetParticles paths are paused. The direct particle-job path remains playing so Unity invokes OnParticleUpdateJobScheduled; its callback schedules and completes the Burst job explicitly. Results apply to this persistent tether use case, not to a separate smoke/collision/sub-emitter workload.

## Important remaining production choices

The test arena is small and fully visible. Large-map spatial culling, compacting sparse active slots, dirty-range uploads, camera movement, multiple cameras, occlusion, lit effects, shadows, distortion, volumetrics and a full RTS's competing GPU workload require additional measurements in the actual game. The current data deliberately does not extrapolate those costs. Graphics API/driver/pipeline changes can alter the ranking.

Primary documentation: [Frame Timing Manager](https://docs.unity3d.com/6000.0/Documentation/Manual/frame-timing-manager.html), [Shuriken GPU instancing](https://docs.unity3d.com/6000.0/Documentation/Manual/PartSysInstancing.html), [particle jobs](https://docs.unity3d.com/6000.0/Documentation/Manual/particle-system-job-system-integration.html), [VFX Custom HLSL](https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.3/manual/Block-CustomHLSL.html).







The retained legacy-main-thread run uses main-thread LineRenderer point sampling and string-based VFX property setters. It has its own 900 cells and 162,000 raw samples. The reference run uses Burst LineRenderer preparation and cached property IDs; the final recommendations use the reference run.



CPU bridge values are wall times including job scheduling/completion waits. The main/render fields do not sum active CPU time across worker cores. Competing ECS jobs can change contention and available parallelism in the actual game.

