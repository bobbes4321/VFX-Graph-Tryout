# Unmanaged gameplay integration

Every renderer consumes the same `LineWorld`. Gameplay never calls a GameObject, VFX component, LineRenderer, ParticleSystem, Material, or graphics API. A job receives a `LineWriter`, which wraps `NativeQueue<LineCommand>.ParallelWriter`.

```csharp
// Inside a Burst job: an ability begins.
var handle = new LineHandle(ability.LineSlot, ++ability.LineGeneration);
lines.Begin(handle, healerPosition, targetPosition,
    new float4(.04f, 1f, .57f, 1f), width: .16f, style: 0);

// In subsequent ticks: follow both moving entities.
lines.Update(handle, healerPosition, targetPosition,
    new float4(.04f, 1f, .57f, 1f), width: .16f, style: 0);

// Healing ends, the target dies, or the ability is interrupted.
lines.Stop(handle);
```

`HealerExample.cs` implements the entire state transition inside `HealerLinesJob`, including target validity, start detection, movement updates, stop detection and generation increments. Change its native component arrays to your ECS accessors. The renderer choice does not appear in that job.

## Scheduling boundary

```csharp
// Initialization, once, on the presentation thread.
var world = new LineWorld(capacity: 1024);
var backend = new LineBackend(Technique.VfxBuffer, 1024, 16, camera);

// Schedule after position updates in your ECS.
JobHandle producers = new HealerLinesJob {
    Healers = healerComponents,
    EntityPositions = positions,
    Lines = world.Writer
}.Schedule(healerComponents.Length, 64, movementDependency);

// Drain commands only after ALL writers have finished.
JobHandle consumed = world.Consume(producers);

// Presentation boundary. Complete as late as practical before rendering.
consumed.Complete();
backend.Present(world.Lines, visualClock, firstFrame);

// Shutdown only after outstanding jobs have completed.
backend.Dispose();
world.Dispose();
```

Unity rendering APIs are called by the presentation bridge on the main thread. Starting, updating and stopping from gameplay remains unmanaged and Burst-compatible for every backend, including per-line VFX/LineRenderer/GameObject implementations. The bridge is measured separately because its managed API work still costs CPU time.

This proof of concept completes presentation jobs immediately so their full schedule/wait cost is visible. A real ECS can overlap producer jobs with unrelated work and complete once at the render boundary. Avoid completing once per entity.

## Handle and ordering contract

* Reserve one slot per simultaneously active ability. A slot is an integer, not an object reference. The example does not contain a general-purpose entity allocator.
* Assign monotonically increasing generations when reusing a slot. A stale stop/update cannot affect a newer generation. Duplicate Begin is ignored. An update cannot revive a stopped generation.
* Give a slot one authoritative producer. Jobs may write different slots concurrently. Multiple producers writing different updates to the same slot have no deterministic ordering; resolve that conflict in the ECS first.
* If different jobs start and update the same slot in the same frame, order them with job dependencies. The consumer runs after all producers.
* Target identifiers in the example are indices. A real ECS should validate its entity generations before supplying positions, and enqueue Stop when an entity/ability is destroyed.
* The queue grows in native memory as necessary. Warm it to your expected peak and keep it persistent. The measured steady-state workloads do this during warmup.
* Complete pending jobs before switching/disposal. Native container lifetime remains your scheduler's responsibility.

The benchmark deliberately uses fixed slot counts. Shared VFX and procedural buffers keep inactive slots; per-line pools disable inactive objects. An actual game can compact active lines before upload, or partition shared systems spatially for culling. Those are useful production extensions, and are not silently credited as optimizations in these results.

## What changes between techniques

| Bridge | Presentation work | Authoring/maintenance |
|---|---|---|
| Shared VFX | Upload line records once, bind buffer once, update common clock/camera | One graph; index the buffer by particle ID/segment. Capacity must cover the pool. |
| Per-line VFX | Pool objects; set Start/End/Tint/Shape on each component | Convenient isolated effect authoring. Automatic batching is an explicit separate test. |
| LineRenderer | Build positions/color keys in Burst; submit native position slices, width and eight-key gradients on each component | Familiar components and connected joins. Main-thread engine calls still scale per line. |
| Burst mesh | Sample and build vertices in a parallel Burst job, upload one mesh | Full geometry control; CPU work and larger vertex uploads. |
| Procedural GPU | Upload endpoints/parameters, submit one instanced procedural draw | Compact data path; maintain the shader and bounds yourself. |
| Shared Shuriken | Build native particles in Burst and submit one SetParticles array | Familiar renderer/modules; particle array copy and CPU renderer work remain. |
| Per-line Shuriken | Same particle builder, one slice/submission per pooled system | Simple independent effect lifecycles; more component/API work. |
| Instanced Shuriken | Same CPU particles, GPU-instanced mesh rendering | GPU instancing affects rendering; simulation remains CPU-side. |
| Shuriken particle jobs | Write positions/rotations/sizes/colors directly to Shuriken arrays | Less copying, but couples job scheduling to ParticleSystem storage. |

The shared HLSL and Burst math implement four styles: restoration, electrical arc, plasma beam and dashed order. The common HDR cross-section texture supplies a bright core and falloff. The dedicated VFX showcase adds a second graph with traveling motes and endpoint coronas. This decorative layer is excluded from the matched comparison and all timed runs. No downloaded artwork or external licenses are required.



## Runtime package versions

This project resolves Burst 1.8.28, Collections 2.6.2, Mathematics 1.3.2, URP 17.3.0 and VFX Graph 17.3.0 under Unity 6000.3.10f1. The runtime has no Unity Entities dependency. Render roots are kept at the identity transform and line data uses world coordinates.

