# Authored complete abilities / AETHER

The current scene is `Assets/LineLab/Scenes/03_Complete_Abilities.unity`. The authored standalone target is `Builds/AuthoredLab/AuthoredLab.exe`. This is a refactored implementation and content model; its performance must be measured independently of the historical complete-effect player.

The original 360-cell complete-effect reference remains under `BenchmarkResults/production/`, with its original `Builds/ProductionLab/ProductionLab.exe` and [historical findings](ProductionFindings.md). Those measurements describe the earlier implementation, not the current authored code. The original line-only player/data also remain separate. New authored results go to fresh directories under `BenchmarkResults/authored/`. The current comparison selects 612 audited source cells and 146,880 raw frames, including corrected full-field Spread measurements through 5,000. Earlier clipped-camera rows are excluded. See the [report index](../BenchmarkResults/authored/report.html) and [measured findings](AuthoredFindings.md); incomplete runs are not successful measurements.

## Preview and authoring

Open the scene and press Play. Use the reference/artist library switch, effect choices, technique selector, complete/core toggle, lifecycle/overlap/spread workloads and count controls. Recipe choices come from the selected library and paginate four at a time; they are not a fixed list of effect names. Preview timing is diagnostic, not the controlled standalone benchmark.

Open **Line Lab > Authoring > Effect Workbench** to edit reusable source assets, inspect allocations, compile the current preview capacity and explicitly rebuild preview. [Artist Workflow](ArtistWorkflow.md) explains making a new line or traveling mote without code. [Code Overview](CodeOverview.md) maps the files; [Integration](Integration.md) shows the actual Burst healer example.

The default `Production/EffectLibrary` resource has Renewal, Storm Lance and Solar Tether. `Production/ArtistLibrary` adds VoidConduit. The reference recipes share three ribbons, 32 segments, 48 traveling motes, 24 sparks, eight coronas and four runes: 96 core / 180 full quads per ability. These are authored defaults. Counts, gradients, profiles, size curves, speeds, path settings, release tails and reusable module composition are editable. VoidConduit adds a second traveling layer and different topology, so its library reserves more work than the reference catalog.

The outpost environment, armored placeholders and profile artwork are original project assets. The example is an elevated 3D RTS presentation, with retained endpoint values during release. All motion is analytic from time/seed/endpoints, which makes deterministic cross-renderer comparison practical. Arbitrary historical trails, collision-driven smoke, distortion, dynamic effect lighting and depth collisions require separate implementations.

## Source versus compiled outputs

`Assets/LineLab/Recipes/` holds authored paths/ribbons/particle modules/effects. Libraries are assets under `Assets/LineLab/Resources/Production/`. **Create Missing Sample Recipes** creates absent samples only; it does not reset edited assets.

Compilation outputs are under `Assets/LineLab/Resources/Production/Generated/<LayoutKey>_v1/`. `EffectRecipeLayout.GraphVersion` is v1. The layout key identifies segment and slot/capacity structure; changing recipe colors/curves/motion values changes baked tables and the content fingerprint without requiring a new topology graph.

`EffectGraphCompiler.CompileLibrary(library, allCapacities, requiredAbilityCount, forceRebuild)` reuses graphs only when their importer signature matches the current compiler, adapter, shared shader math, recipe ABI, Unity version and graph version. An explicit force rebuild removes/recreates graph nodes inside the existing asset, retaining its GUID. It does not rewrite source recipes, imported texture profiles, custom graphs outside Generated, or the scene. Reflection against Unity's internal VFX model is isolated in the editor-only adapter. Players load ordinary compiled graphs and use public rendering APIs.

The workbench compiles only the currently needed capacity and per-ability variants. Full library compilation covers power-of-two shared capacities through 8,192 so a 5,000-ability pool can load the appropriate graph. Actual VFX storage may round individual system capacities further; allocated logical cards and engine capacity are different counters.

To build, use **Line Lab > 4 - Build authored combat effects player**. That operation compiles both sample libraries and writes a distinct AuthoredLab build plus source/binary manifests. **Line Lab > Authoring > Create New Sample Scene** is a separate explicit command requiring a new scene path; compilation does not recreate your edited scene.

## Renderer packaging

| Technique | Complete stack |
| --- | --- |
| Shared VFX | One component, a shared base-record buffer, core stream and one GPU stream per reserved secondary slot |
| Per-ability VFX, batching off/Auto | One pooled component per ability with the same tables/atlas and individual endpoints/timing |
| LineRenderer | Reserved ribbons as connected LineRenderers with Burst preparation, plus procedural secondary streams |
| Combined Burst mesh | One generated core mesh, plus the same explicit procedural secondary companion |
| Procedural | GPU core cards plus one draw/stream per reserved secondary slot |
| Shared Shuriken billboards | Shared core and secondary pools; world-plane cards use mesh orientation where required |
| Per-ability Shuriken | Core and secondary pools per ability, with whole-effect activation policy |
| Shared mesh / instanced mesh Shuriken | Same CPU card recipes with ordinary or GPU-instanced mesh rendering |
| Direct particle jobs | Native particle/custom-data updates inside Unity's required particle callbacks |
| Baseline | Same environment and common native commands/ribbon expansion; no effect renderer |

All methods consume the same baked recipe data. The updated particle path carries HDR RGB in custom float data; alpha still passes through the quantized particle color channel. This replaces historical RGB Color32 clipping, and is one reason historical performance/content cannot be relabeled as the new implementation. Connected joins, gradient interpolation and billboard frames remain real visual differences.

A LineRenderer full-stack result includes its procedural secondary companion. Shuriken GPU instancing affects rendering; it does not move CPU particle ownership to GPU simulation. Procedural shaders reconstruct analytic card state per vertex invocation, while VFX computes/stores attributes and then renders outputs; a ranking measures that implementation tradeoff, not an isolated GPU stage.

## Allocation and limits

The catalog supports eight ribbons, twelve secondary slots, up to 4,096 particles per layer, 1-128 segments and 64 texture tiles including default white. The ability limit is 5,000. Validation rejects unsupported modes, missing references, duplicate keys and invalid/nonfinite values rather than silently truncating content.

Every ability reserves the maximum ribbon count and per-slot particle count across its selected library. Unused ribbons have zero opacity and unused particle entries zero count. This fixed-slot policy deliberately exposes reserved work; transparent cards can still execute. Broad bounds and dense placement are not a full large-map culling solution. Spread is a distinct workload rather than a hidden optimization.

Catalog baking samples gradients/curves into 32-entry rows and creates a linear RGBAHalf atlas with 256-pixel tiles, duplicated two-pixel borders and no mipmaps. Unreadable textures use setup-time ARGBHalf GPU readback. Atlas hashing uses exact raw half-float bytes. The content snapshot records native tables, ramps, topology, names and stable keys. IDs are resolved before gameplay jobs; no managed content API is called by Begin/Follow/Release.

## Run new standalone measurements

```powershell
./Tools/production_benchmark.ps1 -Suite smoke
./Tools/production_benchmark.ps1 -Suite scale
./Tools/production_benchmark.ps1 -Suite full
```

The script launches the visible AuthoredLab player with Direct3D 12, waits for completion, retains the player log, and audits/reports into a unique run directory. It never overwrites the historical production reference. `-Warmup` and `-Samples` override the defaults of 120 and 240 frames; both accept 20-10,000. Minimized players can skip GPU work, so keep the visible benchmark running normally.

Current suite specification:

| Suite | Configurations | Repetitions / cells |
| --- | --- | --- |
| smoke | 128 complete moving abilities at 1080p | One repetition, 12 cells |
| scale | 512 / 1,024 / 2,500 / 5,000 complete abilities; sustain and spread at 1080p | Three repetitions, 288 cells |
| spread | 512 / 1,024 / 2,500 / 5,000 complete abilities in the wide field at 1080p | Three repetitions, 144 cells |
| full | Sustain at 32 / 128 / 256 / 512 / 1,024 / 2,500 / 5,000; lifecycle 128/256; overlap 256; core-only 256; sustain/overlap 256 at 1440p | Three repetitions, 468 cells |
| workloads | Lifecycle 128/256; overlap 256; core-only 256; sustain/overlap 256 at 1440p | Three repetitions, 216 cells |
| line-hdr | Dense 32 through 5,000; Spread 512 through 5,000; all additional workloads, LineRenderer and baseline only | Three repetitions, 102 cells |

Each group includes all eleven techniques and baseline with deterministic shuffled order, except the focused `line-hdr` suite, which includes only LineRenderer and baseline. The emitted suite manifest, rather than this descriptive table, is the authoritative planned matrix. Missing/failed/incomplete cells cannot be interpreted as successful results. High-count object-heavy methods may be expensive to construct and run; their costs must remain visible rather than dropping them from comparisons.

Spread moves the camera farther from the field and extends its far clip to 500 m. Each cell records camera position, direction, field of view and clip planes, and requires both endpoints of every active line to lie inside the camera frustum. This proves endpoint coverage, rather than visibility of every particle or fragment. Earlier Spread rows with the original 180 m far clip are retained as clipped diagnostics and excluded from the current comparison.

The runner emits build identity, catalog digest, source/generated/binary manifest information, immutable content/suite snapshots, raw rows, cell summaries and per-stream particle census. Requested slots, active ability counts, logical authored cards, reserved work and observed particles are different quantities. A complete core does not prove that every secondary stream rendered; the census is per stream.

The revised census freezes native inputs for 16 unmeasured render frames to let asynchronous GPU alive counts settle, then resumes simulation and procedural submissions for eight unmeasured recovery frames. This avoids comparing a changing native owner count with an earlier GPU readback. The precise protocol is recorded in authored-metadata.json. Earlier sustained runs retain their original protocol and build identity.

The [consolidated comparison](../BenchmarkResults/authored/comparison/report.html) selects current rows from independently audited runs. It retains each row's source orders and original matched-baseline delta, without pooling populations. A completed-prefix excerpt is labeled as a derivation; its failed parent remains failed. [Measured findings](AuthoredFindings.md) document the interrupted lifecycle census, corrected LineRenderer and source selection.

## Exactly what timing means

`CommandsMs` brackets producer scheduling, native command consumption, common ribbon expansion and the completion wait. The common expansion is included in baseline and every technique even when a GPU method derives its own ribbons. A VFX-only game can omit it through the lower-level Consume path, but the benchmark does not credit that optimization.

`BridgeMs` brackets explicit presentation preparation/uploads/component submissions, plus the last completed direct-particle callback duration. It excludes later internal Unity work. Whole-frame main-thread, render-thread, present-wait and GPU values come separately from FrameTimingManager. Timers measure elapsed wall time, not worker-core execution totals.

GPU delta subtracts the matching baseline's whole-frame median per repetition and then aggregates repetitions. No GPU timestamp surrounds an individual effect pass. FrameTiming results are asynchronous: a CSV row's explicit CPU timer and GPU record need not describe the same rendered frame. Only fresh nonzero GPU timestamps are accepted; missing counters remain missing. Interpret aggregate distributions, not row-by-row causal correlations.

Simulation advances 1/60 second per rendered benchmark frame. VSync and the frame cap are disabled during measured runs. This is an uncapped deterministic-throughput experiment in the rendered environment, not proof of a saturated RTS's 60 FPS headroom. Calculated bulk input bytes are explicit API input estimates rather than PCIe traffic; total-used-memory is not isolated effect VRAM; configured instancing is not a hardware capture.

[Methodology](Methodology.md) explains the shared measurement conventions. Historical findings remain labeled historical until independently audited authored results are appended.
