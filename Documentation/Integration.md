# Unmanaged ECS integration

The complete-effect API accepts value commands from Burst jobs. Gameplay never calls a GameObject, Transform, VisualEffect, LineRenderer, ParticleSystem, Material or graphics API. Resolve content once during managed setup; copy only an `EffectId` and `AbilityWriter` into gameplay jobs.

The executable example is [AbilityHealerExample.cs](../Assets/LineLab/Production/Runtime/AbilityHealerExample.cs), specifically `PresentHealingJob`. Its component-like inputs are native arrays; replace those with your custom ECS accessors. No Unity Entities dependency is required.

## Setup and content IDs

```csharp
// Managed presentation setup, once. Never run this inside a Burst job.
EffectLibrary library = Resources.Load<EffectLibrary>("Production/EffectLibrary");
var catalog = new NativeEffectCatalog(library);
EffectId healingEffect = catalog.GetEffectId("renewal");

var world = new AbilityWorld(capacity, catalog);
var backend = new AbilityBackend(
    Technique.VfxBuffer, capacity, camera, full: true, catalog: catalog,
    worldBounds: new Bounds(mapCenter, mapSize));
```

`StableKey` is the authored gameplay lookup key; asset names are display labels. An unknown key throws. Effect IDs are library indices valid for that baked catalog, not global permanent IDs. Rebuilding/reordering a library requires resolving IDs again and replacing the dependent presentation pools. The consumer rejects an invalid ID before committing its slot generation.

Keep the cached ID and writer tied to the same world/catalog lifetime. An old ID can still be numerically in range after reordering while referring to a different effect; range checking cannot detect that mistake. Re-resolve keys and update job inputs together during a rebuild.

Compile needed graphs in the Editor before building a player. The player loads compiled resources and baked content; it does not run graph authoring or editor reflection.

Supply a world-space bounds volume containing every endpoint, path excursion and release tail. The optional default is the laboratory's origin-centered 400 × 100 × 400 m volume. VFX, particle, mesh and procedural adapters use the supplied conservative volume; LineRenderer computes bounds from its points. Per-ability pools currently share that same volume. Spatial grouping, moving bounds and large-map culling remain game integration policies. Keep the rendering root at its identity transform because geometry is expressed in world coordinates.

## A real healer producer

`HealingPresentationState` stores `Generation` and `WasHealing` as unmanaged component data. `PresentHealingJob` receives the writer, cached effect ID, clock, healing flags, endpoint arrays and state. Each healer has one exclusively owned stable slot. `IsHealing` must already incorporate gameplay validity, including whether its target entity still exists.

Inside the Burst job, the lifecycle is:

```csharp
// On the transition from idle to healing:
state.Generation++;
var handle = new LineHandle(healerSlot, state.Generation);
Writer.Begin(handle, sourcePosition, targetPosition, HealingEffect, Clock);

// On subsequent healing ticks:
Writer.Follow(handle, sourcePosition, targetPosition);

// On the transition from healing to idle/interrupted/target invalid:
Writer.Release(handle, Clock);
```

These are separate state branches, not three calls issued together. The actual job guards generation exhaustion and retains state after each tick. Release keeps last endpoint values while authored layers fade; it does not keep target GameObjects alive. A repeated Release cannot restart the tail, and Follow cannot move a released effect.

## Schedule and present

```csharp
// Schedule after the ECS has produced current positions and valid healing flags.
JobHandle producers = new PresentHealingJob
{
    Writer = world.Writer,
    HealingEffect = healingEffect,
    Clock = visualClock,
    IsHealing = healingFlags,
    HealerPositions = healerPositions,
    TargetPositions = targetPositions,
    State = healingPresentationState,
}.Schedule(healingPresentationState.Length, 64, gameplayDependency);

// Depend on ALL producers. This schedules consumption and common ribbon expansion.
JobHandle ready = world.PreparePresentation(producers, visualClock);

// Complete once at the presentation boundary, as late as practical.
ready.Complete();
backend.Present(world, visualClock, firstFrame);
```

`PreparePresentation` hides catalog storage and renderer preparation from the external ECS. It returns a dependency, so unrelated jobs can overlap before completion. Do not complete once per healer. Unity component calls and GPU submissions occur in `Present` on the main thread after native inputs are ready.

A VFX-only integration can call `world.Consume(producers, visualClock)` instead and complete that handle before `Present`, because VFX derives its own ribbons. The benchmark deliberately uses `PreparePresentation` for every technique and baseline to retain a common command/expansion policy. Skipping work in a game is valid, but those savings are not silently included in the measured comparison.

Direct Shuriken jobs additionally schedule/complete within Unity's `OnParticleUpdateJobScheduled` callback. The presentation adapter owns that boundary. Gameplay producers remain unchanged when switching renderer techniques.

## Slot, ordering and ownership contract

- Reserve one slot per simultaneously active ability, in the configured 1-5,000 capacity range. This proof of concept does not supply an ECS entity allocator.
- Begin with a newer generation replaces the previous visual in that slot, including any unfinished release tail. Allocate a separate slot when an old tail must overlap a new cast. The healer example deliberately allows only one visual per healer slot.
- Use monotonically increasing generations per slot. Generation zero is reserved by the complete-effect world. Do not wrap a generation counter; reclaim/reset an exhausted slot only with an explicit world lifetime strategy.
- Give each slot one authoritative producer. Different slots can be produced concurrently. Concurrent conflicting commands for the same slot have no guaranteed meaningful order; resolve conflicts in the ECS or establish job dependencies.
- Combine every producer dependency before consuming. Do not enqueue while the consumer drains the same queue.
- Validate ECS entity identity/generation and liveness before providing positions. A target index alone is not a sufficient entity validity contract.
- The persistent native queue can grow. Warm expected peak throughput if native allocation spikes matter.
- Writers and read-only views are borrowed values, not independent owners. Never keep using them after the owning world/catalog is disposed.

```csharp
// Shutdown or structural rebuild: first stop scheduling new producers,
// complete outstanding jobs and ensure particle callbacks no longer read inputs.
ready.Complete();
backend.Dispose();
world.Dispose();
catalog.Dispose();
```

The backend owns Unity render pools, buffers and preparation storage. The world owns native state/commands and borrows catalog tables. The catalog owns immutable recipe tables, sampled ramps and its runtime atlas. Disposal order matters. Public catalog tables are read-only borrowed views (`EffectRecipes`, `RibbonRecipes`, `LayerRecipes`, `RampSamples`); internal adapters and explicitly trusted editor tools access the underlying arrays.

The scene controller's preview rebuild is a concrete implementation of this boundary. Ordinary field/content edits are applied together when a fresh catalog/pool is built, rather than mutating native tables while jobs read them.

## Content and packed data

An effect references a path, ordered ribbons and ordered secondary modules. The reference library's three ribbons and 48/24/8/4 cards are editable defaults. Runtime jobs consume flattened float4 records, sampled gradients/curves and texture indices. They never evaluate ScriptableObjects, strings, AnimationCurve or Gradient objects.

`EffectRecipeData.cs` documents every packed component. `LineData` is 64 bytes and carries world-space endpoints, active state, width, tint, effect index, deterministic phase and timestamps. Expanded strand records are a presentation representation with documented repurposed fields. Use writer calls rather than constructing render records in gameplay.

Each library reserves the largest ribbon count and particle count at each slot across its recipes. Padding keeps inactive slots valid but can cost GPU/particle work. Adding/removing/reordering a layer is a structural rebuild. Spatial compaction, culling and LOD are separate policies.

Slot ordering therefore affects packaging cost: adding a large module to one recipe can increase reserved work for every ability using that library. Keep similar layer roles aligned across recipes, inspect the workbench allocation summary, and split libraries when their topology differs substantially. If any recipe places a ground layer at a particular slot, the Shuriken billboard adapter uses mesh cards for that entire secondary stream so its world-plane orientation remains correct. This fallback can change another recipe's renderer cost even when that recipe itself has no ground card in that slot.

Creating a new effect or a mote variant from the supported motion vocabulary is an asset operation. Adding an entirely new motion algorithm, material/blend model, distortion pass, collision behavior or historical trail requires a programmer to extend and validate the relevant CPU/GPU adapters. The reusable layer assets do not imply arbitrary VFX Graph or Shuriken module authoring support.

See [Artist Workflow](ArtistWorkflow.md) for no-code recipe variants and reusable motes, [Code Overview](CodeOverview.md) for renderer internals, and [Production](Production.md) for measured-counter interpretation.

## Original line-only integration

Scenes 01/02 retain the independent `LineWorld` / `LineWriter` API and [HealerLinesJob](../Assets/LineLab/Runtime/HealerExample.cs). Its `Begin`, `Update` and immediate `Stop` accept explicit width/tint/style values rather than authored complete-effect IDs. Its first Begin can use generation zero because it tracks initialization separately. Do not mix that historical API with `AbilityWriter.Begin/Follow/Release`.

The original minimal frame is producer -> `LineWorld.Consume` -> complete -> `LineBackend.Present`. The renderer table and historical implementation are described in [the archived walkthrough](History/CodeOverview-v1.md).
