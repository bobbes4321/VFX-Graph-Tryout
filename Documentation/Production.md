# Complete ability experiment / AETHER

Open `Assets/LineLab/Scenes/03_Complete_Abilities.unity`, or run `Builds/ProductionLab/ProductionLab.exe`. The original two scenes, player and line-only measurements are retained. The new player contains only the new scene.

Read [the measured complete-effect findings](ProductionFindings.md) for the conclusions and representative CPU/GPU costs.

The diorama has three complete abilities: **Renewal**, **Storm Lance** and **Solar Tether**. Click their named buttons for close views. Switch the rendering method using the method button at the bottom. Choose Complete/Core only, pooled charge/release, heavy overlap, or 32/128/256/512 links. Preview timers are diagnostic; open `BenchmarkResults/production/report.html` for the measured comparison.

## What is rendered

Each ability allocates three 32-segment ribbons, 48 travelling motes, 24 ballistic sparks, eight layered endpoint-corona cards and four rotating ground-rune cards: **180 quads per complete ability**. The core-only case keeps the three ribbons (96 quads). Shapes, movement, textures, seed, width and color recipes match across renderers, subject to documented differences in joins, gradients, billboard orientation and Color32 quantization.

The effect starts with a 0.55-second charge envelope. Release fades the core quickly while the secondary layers finish; after 1.4 seconds the slot retires. Release freezes the last known endpoints, so a disappearing target does not require a managed Transform. A new generation can reuse the slot safely. All trajectories are analytic; there is no hidden managed particle simulation credited to a GPU backend.

The scene includes original generated terrain, erosion rocks, beveled metal props, armoured units, detailed energy profiles, lighting, soft shadows, color grading and bloom. These assets were made in this project; no third-party download or attribution requirement is involved.

## The meaningful packaging differences

| Technique | Complete effect implementation |
|---|---|
| Shared VFX | One component, one shared 64-byte-per-ability buffer, five GPU systems inside one graph. Core expansion also happens on the GPU. |
| Per-ability VFX, batching off/on | One component/graph per ability. Each graph owns its three strands and all four secondary layers. This is the convenient isolated ability/prefab authoring path. Both instance-batching settings are measured. |
| LineRenderer | Three pooled LineRenderers per ability, with Burst preparation. A four-layer procedural GPU companion renders the free particles and ground cards. |
| Burst combined mesh | One CPU/Burst-generated mesh for the core ribbons, plus the same explicit procedural companion. |
| Procedural ribbons | One GPU core draw plus four secondary-layer draws. Both use endpoint buffers. |
| Shared Shuriken billboards | One shared core particle system and four secondary systems. Ground runes use mesh cards for world orientation. |
| Per-line Shuriken billboards | One core system containing all three strands, plus four secondary systems per ability: a five-system pooled ability. |
| Shared mesh / instanced mesh Shuriken | Native core and secondary particles, with ordinary or compatible GPU-instanced rendering. |
| Direct Burst particle jobs | Five particle-job callbacks write core/secondary native storage directly. Unity requires scheduling in OnParticleUpdateJobScheduled. The bridge includes the last completed callback duration. |

LineRenderer itself does not provide free particles, endpoint surfaces or arbitrary ground orientation. Its measured full stack explicitly includes the companion. Likewise, GPU instancing of Shuriken changes rendering, not ownership of its CPU particle simulation. The report does not hide these differences behind a common technique name.

## Unmanaged integration

```csharp
// Inside Burst gameplay jobs. Slot ownership and generations come from your ECS.
var handle = new LineHandle(ability.Slot, ++ability.Generation);
abilities.Begin(handle, sourcePosition, targetPosition, style: 0, time: clock);

// While the ability remains active, follow moving entities.
abilities.Follow(handle, sourcePosition, targetPosition);

// Ability ends, target disappears, or the player interrupts it.
// No managed object is required. The visual tail retains its last endpoints.
abilities.Release(handle, time: clock);
```

`AbilityWriter` wraps a `NativeQueue<AbilityCommand>.ParallelWriter`. The consumer ignores stale generations, repeated release cannot reset the tail, and Follow cannot move a released effect. Use one authoritative writer per slot and schedule the consumer after every producer. Validate entity identity/liveness in your ECS before sending positions.

```csharp
// Presentation setup, once.
var world = new AbilityWorld(capacity);
var renderer = new AbilityBackend(Technique.VfxBuffer, capacity, camera, full: true);

// Each presentation tick, after all unmanaged producers finish:
JobHandle consumed = world.Consume(producers, clock);
new ExpandStrands {
    Lines = world.Lines, Strands = world.Strands, Clock = clock
}.Schedule(capacity * 3, 64, consumed).Complete();
renderer.Present(world, clock, firstFrame);

// Dispose only after outstanding producers/consumers and particle callbacks finish.
renderer.Dispose();
world.Dispose();
```

The common strand expansion is deliberately included in every measured baseline and backend, even when VFX computes strands on the GPU. A production VFX-only integration can omit that unused CPU expansion. All Unity rendering and component calls remain in the main-thread presentation boundary. Avoid completing jobs separately for individual entities.

The procedural implementation uploads the three expanded core records, plus the base records for its secondary-layer companion. Shared VFX uploads only the base record and expands all three strands in its graph. This is a difference between these implementations, rather than an intrinsic bandwidth requirement of procedural rendering: a custom shader could also expand strands from a single base record. The CSV bulk-input column makes the current traffic explicit.

There is also a GPU-work difference: these procedural shaders reconstruct a card's recipe in each vertex invocation, while VFX updates particle attributes in compute and reuses them for its output geometry. The procedural path avoids VFX system dispatch/storage overhead, but repeats more recipe math. The measured ranking includes that tradeoff; it does not isolate the cost of each individual GPU stage.

## Authoring and code map

- `Runtime/AbilityWorld.cs`: unmanaged commands, generation-safe native state, lifecycle and deterministic demo producers.
- `Runtime/AbilityGeometry.cs`: common Burst curve, color, envelope and adornment recipes.
- `Shaders/AbilityCommon.hlsl`: GPU equivalent generated by `python Tools/sync_ability_math.py`; edit the C# recipe and regenerate to keep them synchronized.
- `Runtime/AbilityBackend.cs`: complete VFX and secondary-layer presentation paths.
- `Runtime/CoreBackend.cs`: separate copy of the original renderer bridges using the new core recipes/materials. The original reference implementations remain unchanged.
- `Runtime/ProductionArt.cs`: environment and unit authoring.
- `Editor/ProductionAssets.cs`: textures, meshes, materials, five-system VFX assets, atmosphere, scene and separate player builder.
- `Editor/ProductionValidation.cs`: ten meaningful lifecycle checks, including stale commands, parallel producers, release tails and skipped render-frame boundaries.

Paths above are relative to `Assets/LineLab/Production`. Runtime resources are under `Assets/LineLab/Resources/Production`. Shared graphs are `AbilitySharedFull_*`; per-ability graphs are `AbilitySingleFull_1` and `AbilityBatchedFull_1`. Core-only graphs use `Beam` instead of `Full`. They are real compiled VFX assets and can be opened in the VFX editor. The five columns contain core, motes, sparks, coronas and ground runes. Per-ability exposed inputs are Start, End, Tint, Shape, Clock and CameraPosition; SpawnCore and SpawnLayer0–3 set the allocation. Shared graphs read Lines instead of individual endpoint inputs.

The generated shared VFX capacity variants cover 1–512 abilities. Extend the generator's capacity loop and rebuild before requesting a larger shared VFX pool. Per-ability graphs use the same single-ability asset for every pooled instance.

## Reproduce

Use **Line Lab → Generate complete combat effects**, then **Line Lab → Build complete combat effects player**. The generator uses Editor APIs and rebuilds only the new assets and scene.

```powershell
./Tools/production_benchmark.ps1 -Suite smoke
./Tools/production_benchmark.ps1 -Suite full
```

The full matrix has 360 cells: eleven methods plus baseline, three shuffled repetitions, 32/128/256/512 complete moving abilities at 1080p; lifecycle at 128/256; 256 heavy overlap; 256 core-only; and 256 moving/overlap at 1440p. It uses 120 warmup frames and 240 measured frames per cell. All stress counts fill the same 36×19 m footprint; line lengths do not shrink with count. Only fresh GPU timing stamps are accepted. The report audits raw counts, repetitions, timestamps and GPU medians before producing output. Unsupported counters are missing, never invented.

The six-unit diorama and native command/strand jobs remain identical in all timed cases, including baseline. UI is hidden. This measures effect stacks in a rendered environment, not a full RTS with hundreds of animated units, AI or a contested GPU budget. Fixed-slot alpha-zero particles still execute. There is no active compaction, depth collision, dynamic effect lighting, distortion, history-dependent smoke or arbitrary historical trails in this experiment. Those features need dedicated implementations and measurements in the actual game.

Lifecycle retirement differs between the implemented pools: per-ability VFX disables its entire component once the tail ends. Per-ability Shuriken disables the core system, while its four secondary pools keep their allocated alpha-zero particles. Disabling those secondary GameObjects as a group is a further optimization available to Shuriken; the lifecycle results do not credit an optimization this implementation does not perform. All shared systems retain fixed slots. Bounds are deliberately broad and the benchmark does not measure large-map spatial culling.
