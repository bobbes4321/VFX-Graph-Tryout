# Adversarial authoring and scalability review

Review date: 20 October 2026. This is a read-only source review of the original complete-ability implementation, before the authoring/scalability refactor. Findings describe that starting point; they are not a claim that later fixes have been verified. The review did not operate the Unity Editor or rerun historical benchmarks.

The original experiment is useful for its controlled renderer comparison, but its production namespace is not yet a production authoring model. Formatting does not fix the underlying issue: effect content is embedded in executable recipes, generated graphs, material construction and demo selection code. Creating a fourth effect or a different travelling mote currently requires a programmer to coordinate changes in several places.

## Prioritized findings

### P1 — Effects are three hardcoded programs rather than reusable authored content

Evidence: `Runtime/AbilityGeometry.cs`, especially `Palette`, `LayerCount`, `Strand`, `Point`, `Tint` and `Decor`; `Runtime/AbilityWorld.cs`, `ConsumeAbilities` and `PlayAbilities`; `Runtime/ProductionController.cs`, `OnGUI`.

`Palette(int style)` silently maps every value except 0/1 to the third color. The same numeric style selects motion in `Point` and multiple unrelated behaviors in `Decor`. Three strands, 32 segments, four secondary kinds, counts 48/24/8/4, width .22, charge .55 seconds, layer release durations and retirement 1.4 seconds are executable constants. Demo names and `abilityIndex % 3` encode a separate copy of the content catalog. A new style cannot have a different number of strands, omit runes, reuse a spark module or add a second mote layer without structural edits.

Required fix: an effect recipe references reusable path, ribbon and secondary-layer assets. Artists edit named fields with units, tooltips, ranges, HDR gradients and curves. Duplicate/reuse should be ordinary Unity asset workflows. Bake assets once into validated blittable tables for jobs and GPU buffers. Keep the command consumer ignorant of specific effect names or visual algorithms.

Acceptance: create a fourth effect asset using existing motion building blocks; give it two ribbons and two differently configured mote layers; add it to the catalog and preview it without editing C#, HLSL, generator logic or UI names. Change one reused mote asset and see the change in two effects. No style-specific branch should be necessary in command consumption.

### P1 — Regeneration destroys the content an artist would naturally edit

Evidence: `Editor/ProductionAssets.cs`, `Generate`, `RefreshArt`, `Save`, `CreateGraph`, `CreateScene`. `CreateGraph` deletes existing graph assets; `Save` overwrites serialized textures/materials; `Generate` and `RefreshArt` reconstruct the showcase scene. The docs explicitly invite opening the generated graphs, without making the edit-loss boundary prominent.

Required fix: distinguish authored source assets from disposable compiled outputs. Rebuilding tables/graphs must not overwrite authored recipes, imported texture sources, custom graphs or the scene. Preserve generated graph GUIDs where references can exist. Separate initial scene/sample creation from rebuilding runtime data. Quarantine reflection-based VFX generation behind an editor-only adapter with useful compatibility failures.

Acceptance: edit a recipe, material/texture assignment and scene camera, rebuild, close/reopen Unity and confirm all edits survive. Rebuild a generated graph twice and confirm its GUID remains stable. A user-provided graph outside the generated folder must never be touched. Sample initialization must preserve existing sample assets.

### P1 — Asset fields must actually affect every renderer

Evidence: `Runtime/AbilityBackend.cs` and `Runtime/CoreBackend.cs` load fixed resource names; `Editor/ProductionAssets.cs` binds one profile per fixed layer; `Shaders/ProductionAdornment.shader` uses one `_Layer`; `Tools/sync_ability_math.py` copies embedded algorithms, not authored data.

A cosmetic ScriptableObject palette would leave the real problem intact. The data needs to flow through native consumers, CPU geometry, Shuriken updates, procedural shaders and VFX graphs. A texture or color option that only one backend honors would invalidate the controlled comparison.

Required fix: a shared baked catalog describes effect/path/ribbon/layer parameters, sampled gradients/curves and texture routing. Preserve a shared CPU/GPU recipe definition or mechanically verify the two implementations. Material/texture differences may require atlas addressing or batches, but expose those real costs rather than silently ignoring content.

Acceptance: alter mote count, texture, HDR tint/gradient, size curve, speed and orbit radius in the Inspector; compare VFX, procedural and Shuriken captures at the same deterministic clock. All must respond to those fields. Unsupported modes must produce explicit validation errors, not substitute a default. Demonstrate at least one actual source texture swap and one gradient with different endpoint colors.

### P1 — Scaling to 5,000 requires coordinated capacity and benchmark changes

Evidence: `Editor/ProductionAssets.cs`, capacity loop stops at 512; `Runtime/AbilityBackend.cs`, shared graphs are selected using `ceilpow2(count)`; `Runtime/ProductionBenchmark.cs`, matrix and expected quads; `Tools/production_report.py`, fixed 360-cell audit and 180/96 recipe counts; `Runtime/ProductionController.cs`, count buttons; `Tools/Templates/production_report.html`, selectors.

5,000 complete reference abilities mean 900,000 logical quads. The existing shared graph selection would request a missing 8,192-ability graph. VFX capacities are themselves rounded per system, so logical allocations and actual particle capacity differ. Per-ability particle pools also mean 25,000 particle systems. Merely adding a button or raising one constant does not provide a valid experiment.

Required fix: one declared capacity/workload specification feeds allocation, UI and matrix metadata. Record actual topology/capacity, authored catalog identity and quad count. Scale all techniques, including slow object-heavy cases, or record an explicit failed/unsupported cell with its reason; never silently omit an inconvenient technique. Preserve old runs and identify the new implementation separately.

Acceptance: standalone capture and audited sustained/full/core/lifecycle comparisons at 1,000, 2,000 and 5,000; include all techniques and matching baselines/repetitions for each selected workload. Confirm observed particle counts and renderer allocations, meaningful GPU samples, actual window dimensions and report filters. Do not describe original 512-count results as measurements of refactored authored code.

### P2 — Unmanaged integration needs a content and ownership contract

Evidence: `Runtime/AbilityWorld.cs`, `AbilityCommand.Style`, public native arrays, `ConsumeAbilities`, `Dispose`; `Documentation/Production.md`, slot/generation ownership and manual job completion requirements.

The value-only queue and generation checks are a sound starting point. However, arbitrary style integers are accepted, there is no catalog validity contract, native storage is freely mutable, shutdown assumes callers completed every job, and retirement is unrelated to actual authored layer tails. Generation ordering and same-slot producer ordering remain caller responsibilities.

Required fix: a small blittable effect ID returned by the baked catalog; Begin validates it before committing a generation. Expose read-only native views where possible and state who owns storage/dependencies. Retire from baked lifetime metadata, including the longest layer release tail. Document one authoritative producer per slot, generation monotonicity, and explicit completion/disposal boundaries. Keep managed assets and UnityEngine rendering calls outside all gameplay writers.

Acceptance: invalid ID cannot access tables or consume the slot generation; a changed authored release duration changes actual retirement; stale commands and post-release follows remain ignored; concurrent independent-slot producers work under Burst; the integration example obtains effect IDs during setup and uses only blittable values inside a job.

### P2 — Renderer and demo responsibilities are too entangled

Evidence: `Runtime/AbilityWorld.cs` contains native state and deterministic demo producer; `Runtime/ProductionController.cs` contains scheduling, benchmark launch, camera, capture, immediate-mode UI, resource ownership and file paths; `Runtime/AbilityBackend.cs` constructs all topology and owns several renderer families; `Runtime/CoreBackend.cs` retains unused copied VFX/showcase branches.

Required fix: isolate authored asset/baking model, native lifecycle/math, rendering adapters, showcase workload/UI and benchmark orchestration. This need not become a large framework: small cohesive files with explicit inputs are sufficient. A programmer should be able to copy the runtime integration without importing screenshot logic and the benchmark menu.

Acceptance: a code map identifies the exact minimal runtime entry points and dependencies; adding an effect does not touch the controller. Remove or explain unreachable copied branches, use named layer/path modes and document packed native/GPU layouts at their definitions. Avoid exposing large public mutable ownership surfaces merely to avoid writing a view.

### P2 — Benchmark proof must track new content, not only code and fixed counts

Evidence: `Runtime/ProductionBenchmark.cs`, metadata and allocation validation; `Tools/production_report.py`, recipe checks; existing historical manifests in the results folders.

With editable recipes, a source hash and executable GUID cannot alone tell a reader which visual content was measured. Fixed `180` and `96` checks would either reject useful authoring or silently keep describing obsolete work. The current live-particle validity threshold accepts half the expected allocation; aggregate count alone also cannot prove each layer rendered correctly.

Required fix: export recipe/catalog digest, supported algorithm/schema version, per-effect layer counts, actual baked topology, source/generated asset manifest, suite specification, logical quads and reserved capacities. Derive audit expectations from that snapshot. Retain deterministic visual parity captures for authored test variants. A missing layer must not pass just because the core contributes most particles.

Acceptance: changing only an asset field changes the measured content digest. Reports identify the immutable run and build, derive their selectors/counts from data, and reject incomplete matrices or incompatible catalog snapshots. Original reference artifacts remain readable and untouched.

### P2 — Pool behavior needs explicit limitations at high counts

Evidence: `Runtime/AbilityBackend.cs`, `Present`, `DecorParticles` and `DecorParticleUpdate`; `Runtime/CoreBackend.cs`, `Present`; broad fixed bounds in both backends; `Documentation/Production.md`, lifecycle differences.

Shared methods execute fixed slots even at alpha zero. Per-ability VFX disables its entire object after retirement, whereas the per-ability Shuriken path only disables core pools and continues submitting secondary particles. Fixed broad bounds and dense 36×19 m distribution do not model large-map culling. A 5,000-count result will conflate renderer scaling and extreme overlap unless its density assumptions are clear.

Required fix: maintain a deliberately comparable fixed-slot suite and describe it honestly; either harmonize whole-effect retirement or explicitly measure/document the different policies. Include dense overlap and a spread-out scenario if claiming RTS applicability; any adaptive LOD, culling or compaction should be a named separate case rather than an invisible optimization.

Acceptance: counts and costs explicitly distinguish requested slots, active abilities, logical visible quads and reserved work. Retired per-ability secondary systems do not keep unintended active submissions. Captures show both dense and spatially distributed large-count arrangements when those workloads are included.

## Practical architecture

1. **Authoring assets:** `EffectDefinition` references a path plus a list of reusable ribbon and secondary-layer definitions. Each layer owns a texture/profile, HDR gradient, size/opacity curves and mode-specific fields. Named modes form a limited, documented vocabulary: smooth arc/helix/jitter paths; travelling/orbiting motes, endpoint ballistic sparks, endpoint coronas and world-plane runes. It is reasonable that a completely new motion algorithm requires a programmer; creating a variation of an existing algorithm should not.
2. **Bake boundary:** the managed catalog validates authoring, assigns session IDs, packs native/GPU tables and LUTs and resolves texture addressing. Save source-to-baked mapping and maximum allocation summary. Runtime jobs receive the immutable tables and IDs, never ScriptableObjects, Gradients, Materials or Transforms.
3. **Native world:** handle lifecycle, endpoint state and release timing. Keep state meaning separate from render packing. One dependency chain consumes commands after producers, prepares the needed views and completes once before Unity calls.
4. **Rendering adapters:** accept the same native state/catalog. Shared GPU methods use tables/buffers; per-ability methods bind the same effect IDs/parameters. Structural allocations have an explicit rebuild boundary. Runtime color/position changes must not reconstruct entire pools.
5. **Author preview:** an Inspector/editor window chooses an effect and technique, displays validation/allocation cost, duplicates assets, adds/reuses layers and previews deterministic motion with restart/release. Use standard Undo, dirty/save and asset ping/open behavior. Preview actions must not reset the scene or rebuild every VFX capacity variant.
6. **Experiments:** historical line study stays frozen; the authored complete-effect experiment gets its own build/run identity and data-driven scale suite. Reports carry all implementation caveats and content snapshots.

## Required hands-on authoring proof

Before calling the refactor complete, exercise these actual workflows and record the resulting asset paths/captures:

- Duplicate Renewal into a new named effect, change its ribbon gradient and path speed, and preview it without source edits.
- Create a reusable travelling mote variant with a different source texture, stretched shape, size curve and travel speed; use it in two effects and verify shared edits propagate.
- Add/remove/reorder a secondary layer and disable the core; confirm topology rebuild and preview explain the resulting allocated work.
- Give a layer a longer release tail and verify it stays visible until its configured end without retaining a source/target GameObject.
- Rebuild generated assets, close/reopen and confirm authored assets and scene edits survive.
- Run a Burst gameplay producer using the baked effect ID and unchanged Begin/Follow/Release concepts.
- Capture VFX, procedural and Shuriken versions of the new recipe at the same clock; document genuine renderer limitations instead of papering them over.
- Run and audit the 5,000-ability scale matrix in a standalone player. Report actual measured results and failed cells separately from expectations.

## Verification status

Initial review complete. No runtime modifications were made by the reviewer. Subsequent source-review status is recorded below; runtime acceptance belongs to the separate Unity/standalone validation results.

## Second and third source passes

The implementation now has actual reusable effect/path/ribbon/particle assets, native and GPU recipe tables, sampled curves/gradients, an HDR profile atlas, baked gameplay IDs and configurable lifecycle retirement. The workbench edits ordinary source assets, while topology compilation writes separate generated resources. The compiler preserves existing graph GUIDs on explicit rebuild and source initialization preserves existing samples. Benchmark expectations derive from content/suite snapshots and compare each particle stream exactly.

The second source pass found concrete defects rather than style issues. Parent fixes are present in the inspected source for all of these:

- Reserved secondary slots now suppress cards beyond each recipe's actual count, using safe unit axes for inactive cards.
- Particle census no longer contains the dangling `else` that returned zero for non-VFX renderers.
- Secondary opacity now honors effect tint alpha.
- Endpoint vertical offsets and source/target scales are applied to endpoint algorithms; corona/ground curves use normalized card indices rather than undocumented animation time.
- Contextual inspectors expose the actual corona pulse controls and hide irrelevant fade fields.
- Public catalog views are read-only; owned mutable native arrays are internal.
- Backend/resource construction has cleanup paths for partial failures; editor destruction uses the appropriate immediate path.

The third pass inspected the new graph compiler, controller, workbench and benchmark wiring. It found these integration defects, reported to the parent for correction:

1. **Atlas row orientation (P1):** installed VFX Graph `VFXCommon.hlsl:GetSubUV` counts flipbook indices from the top row, whereas the authored atlas and other shaders count from the bottom. Passing the raw tile index makes VFX sample a different texture. Convert tile indices at the VFX boundary or adopt one consistent storage convention everywhere. Acceptance requires a visibly distinct asymmetric source texture in VFX and another renderer at the same clock.
2. **Zero-count streams (P1):** an allowed disabled layer/empty catalog slot has capacity zero, but generated source divides/modulos by cards per ability and initializes that stream's capacity. Omit the stream safely or use a nonzero inert denominator/capacity with zero spawn; keep census semantics explicit. Particle-system zero-capacity configuration needs the corresponding handling. Exercise both a zero-count layer and an effect library with no secondary layers.
3. **Preview capacity transition (P1):** workbench compilation targets the current count, while `SetPreviewEffect` switches to three hero instances. A new topology compiled at 128 or 5,000 can therefore immediately request the missing four-capacity shared graph. Compile the hero capacity as well, or preserve the selected count when choosing effects.
4. **Generated graph cache version (P2):** graph reuse is keyed by topology; generator logic changes can leave an existing topology's compiled graph stale. Version generated layouts or force intentional rebuild after generator changes while preserving GUIDs.

The scaling suite currently measures complete sustained effects in dense and spread-out arrangements at 512, 1,024, 2,500 and 5,000 abilities. Core-only and pooled lifecycle cases remain smaller in the full suite. This is an explicit coverage limit; do not describe the scale suite as a 5,000-count lifecycle or core-only comparison unless those cases are added.

No Unity controls were used by this reviewer. Source inspection confirms architecture and fixes, not rendering parity, player performance or authoring persistence after reopening Unity. Those require the hands-on workflows above and the measured standalone audit.

## Final static review status

All three P1 integration findings from the third pass are now resolved in the inspected source:

- VFX texture indices explicitly convert the authored atlas's bottom-up rows into VFX flipbook's top-down rows. The atlas dimensions are passed into the generated custom block.
- Zero-card native pools allocate no particle systems, zero-spawn VFX streams retain valid capacity, and generated indexing uses a denominator of at least one.
- Incremental compilation includes the three-hero/four-capacity shared graph as well as the current preview capacity.

The generated graph path includes `EffectRecipeLayout.GraphVersion`, addressing cache invalidation when generator logic changes. Developers must bump that version or force an in-place rebuild when changing emitted graph structure. Native catalog tables have public read-only views. The per-ability particle path tracks first initialization per pool so an initially inactive ability can initialize later. Corona pulse dimensions are clamped nonnegative. Constructor cleanup and safe editor destruction remain present.

An `Editor/AuthoringAcceptance.cs` fixture now exercises real asset creation/reuse, cloned source edits, exact source preservation, native/GPU struct sizes, stable graph identities, zero-layer compilation and native lifecycle/scaling validation. This is stronger evidence than documentation alone. At this source-review checkpoint its runtime result was pending; inspect the resulting `Logs/authoring-acceptance.json` and standalone visual/benchmark audits for execution status. This reviewer did not run it or substitute static inspection for its outcome.

No additional P1 defect was found in the final static pass. The remaining production limitations should be preserved in the integration guide:

- **Conservative render bounds:** the backend now accepts optional world-space bounds and passes them to VFX, mesh, procedural and particle adapters. The default remains the laboratory's origin-centered 400×100×400 m volume. Per-ability pools share this volume; spatial grouping and automatic large-world culling remain integration work.
- **Catalog-wide reserved slots:** the largest count at each ordered ribbon/secondary slot determines reserved work for every ability in that library. Adding or reordering a heavy recipe can change the cost of unrelated recipes. Align reusable slot roles or split libraries to control this padding. If any recipe puts a ground layer at a slot, the Shuriken billboard adapter uses mesh cards for that entire secondary stream to preserve world orientation.
- **Session-local IDs and slot ownership:** resolve stable keys again after catalog rebuild/reordering. One authoritative producer owns each slot and its monotonic generation. Starting a new generation in the same slot replaces the previous visual, including its release tail; overlapping tails require separately owned slots. Complete all native dependencies before disposing owners.
- **A defined artist vocabulary:** existing path/mote/spark/corona/rune variants, texture profiles, HDR gradients, curves, counts and composition are data operations. An entirely new motion algorithm, blending model, distortion pass, collision behavior or historical trail still requires implementation and parity work.
- **Sampling and packaging costs:** curves/gradients use 32 samples, profile tiles are resampled into a fixed-resolution atlas, LineRenderer uses its gradient/curve interface, and Shuriken alpha remains quantized. These are actual adapter constraints, not evidence that every renderer is pixel-identical.
- **Measurement coverage:** the scaling suite establishes complete sustained dense/spread cases through 5,000. Lifecycle and core-only scaling are covered only if explicitly included in the executed suite. There is no isolated effect-pass GPU timestamp, automatic large-map culling, active-slot compaction or saturated-RTS background workload.

These limitations are practical boundaries of this proof of concept. They should be reported candidly rather than hidden behind a production-ready label. Final acceptance still requires the actual editor authoring workflow, deterministic visual comparisons and audited standalone measurements.

## Integration execution checkpoint

The final compiled editor implementation passed [175 authoring acceptance checks](Validation/authoring-acceptance.json) and [47 native validation checks](Validation/native-validation.txt). The acceptance fixture creates and composes Opal Transfer and Opal Motes, exercises reusable modules through the real asset database, checks graph GUID preservation and zero streams, and verifies source assets and camera transforms survived compilation. This records a completed in-editor run; it does not claim a manual Unity close/reopen test. The source now also supports supplied world bounds and per-generated-asset compiler/math signatures, replacing manual-only stale-cache prevention. Standalone rendering and timing audits are recorded separately.

## Final integration and measurement review

The supplied `worldBounds` API is present and validates finite center/size with strictly positive dimensions. VFX exposes center/size inputs, while meshes, procedural submissions and particle pools receive the same conservative volume. LineRenderer derives bounds from its points. The root identity-transform requirement and per-ability use of one conservative region are documented in Integration; this is configurable culling input, not automatic spatial partitioning.

Graph compilation now compares `AssetImporter.userData` against a signature of the graph compiler, reflection adapter, generated shared math, recipe shader ABI, Unity version and graph version before reusing an asset. Mismatches trigger an in-place rebuild, preserving GUIDs. Artist content values still enter through baked tables, so ordinary color/curve edits do not invalidate topology unnecessarily.

Integration testing found two additional engine-facing issues after the earlier static passes:

- LineRenderer's UNorm8 vertex colors clamp direct HDR gradients. The adapter now normalizes its eight color samples in Burst and reconstructs HDR with a per-ribbon shader multiplier. The multiplier preserves interpolation while retaining the engine's color quantization. The focused 102-cell LineRenderer run is audited and takes precedence over older adapter timings; a baked-geometry/material probe verifies reconstruction. This is an actual engine validation result, not an assumption based on the C# `Color` type.
- Asynchronous GPU alive counts could trail lifecycle activation by one instance. Census now freezes native inputs for 16 unmeasured frames, takes exact stream counts, then resumes for eight unmeasured recovery frames before timed sampling. The subsequent 216-cell workload run passed. Those census/recovery frames are outside the 240 recorded frames. The earlier interrupted parent remains failed; only its independently audited 252-cell completed prefix is retained, with parent hashes and failure details.

The cross-run comparison is a derived view of audited aggregate rows. Each row retains its source build, cell orders and original matching-baseline delta; populations are not pooled. A displayed baseline can therefore differ from the source baseline used for another row's delta. Different implementation/census revisions remain explicit in source provenance. This is a valid way to present corrected measurements provided it is labeled as a derived comparison, as it currently is.

A final camera check found that the older Spread camera was farther from the field than its 180 m far clip. Those Spread rows are excluded from the current combined view. The corrected implementation uses at least 500 m far clip for Spread, records camera parameters and rejects a cell unless all active endpoints are inside the viewport and clipping planes. At this review checkpoint, the corrected 144-cell Spread run was still executing; no final completion or full-run audit is asserted here. Endpoint coverage is a deliberate visibility sanity check, not proof that every corona edge, particle excursion or fragment lies on screen. Dense and Spread workloads retain different screen coverage and must be compared as different density/camera conditions.

No outstanding P1 source or measurement-validity defect was found in this final pass. The remaining work at this checkpoint is the corrected Spread run's completion/audit and final report/count synchronization. Source architecture, authoring checks and previously audited measurements are complete within the limitations listed above; their results do not establish arbitrary VFX feature support or performance inside a fully loaded RTS.

## Completed measurement closure

The corrected Spread run subsequently passed all 144 cells, with 34,560 raw frames and 31,250 fresh GPU timing samples. Its exact census and camera coverage checks passed throughout. The consolidated comparison now selects 612 source cells / 146,880 frames across all 17 conditions, with 131,078 fresh GPU timing samples. Earlier clipped Spread rows remain excluded, and each selected row retains its source build, orders and matched-baseline delta. Findings, report index and current build identity were synchronized to this completed result.

After closing the Editor for standalone timing and reopening the project, the acceptance fixture passed all 175 checks again with no new source assets required. Native validation also passed all 47 checks again. This verifies that the persisted examples load and remain usable after reopening; it does not substitute for testing arbitrary future artist edits.
