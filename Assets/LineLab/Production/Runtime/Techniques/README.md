# Technique code comparison

These are the running implementations used by `03_Complete_Abilities.unity` and the authored benchmark. Each technique's C# setup, frame submission, preparation jobs, secondary streams, statistics and cleanup are in one file. Start with its constructor, then `Present`, then its local jobs and `Dispose`.

| Technique | Implementation | Main code cost to compare |
| --- | --- | --- |
| Shared-buffer VFX Graph | [VfxBufferTechnique.cs](VfxBufferTechnique.cs) | One graph; upload the ability endpoint buffer each frame |
| Per-ability VFX Graph, batching disabled | [VfxPerLineTechnique.cs](VfxPerLineTechnique.cs) | Pooled components; update each active component's inputs |
| Per-ability VFX Graph, Auto batching | [VfxPerLineBatchedTechnique.cs](VfxPerLineBatchedTechnique.cs) | Same component submissions; selects the compiled Auto-batched graph |
| Unity LineRenderer | [LineRendererTechnique.cs](LineRendererTechnique.cs) | Burst positions/HDR gradients, pooled ribbon submissions, procedural secondaries |
| Burst combined mesh | [BurstMeshTechnique.cs](BurstMeshTechnique.cs) | Burst vertex generation and combined mesh upload, procedural secondaries |
| Procedural GPU ribbons/cards | [ProceduralTechnique.cs](ProceduralTechnique.cs) | Strand/ability buffer uploads and procedural core/secondary draws |
| Shared Shuriken billboards | [ParticleBillboardSharedTechnique.cs](ParticleBillboardSharedTechnique.cs) | Burst preparation; one `SetParticles`/custom-data copy per stream |
| Per-ability Shuriken | [ParticleBillboardPerLineTechnique.cs](ParticleBillboardPerLineTechnique.cs) | Per-ability system activation, particle subarrays and custom-data copies |
| Shared Shuriken mesh | [ParticleMeshSharedTechnique.cs](ParticleMeshSharedTechnique.cs) | Mesh particle renderer, native preparation and copied submissions |
| GPU-instanced Shuriken mesh | [ParticleMeshInstancedTechnique.cs](ParticleMeshInstancedTechnique.cs) | Mesh instancing enabled; particle preparation/copies still run on CPU |
| Direct Burst particle jobs | [RecipeParticleBridge.cs](RecipeParticleBridge.cs) | Seed once; callback jobs update native particle arrays and HDR custom data |

The direct-job file retains the `RecipeParticleBridge` filename and original script GUID because Unity's callback is a `MonoBehaviour`. It also contains the entire `ParticleJobsTechnique`, its pools, seed job and update job.

The baseline creates no technique implementation. [AbilityBackend.cs](../AbilityBackend.cs) keeps the public entry point, validation, technique selection and shared ownership. [AbilityTechnique.cs](AbilityTechnique.cs) contains only the common contract and recipe-view construction. Gameplay/lifetime, authored tables, analytic recipe evaluation and GPU table/atlas binding remain shared in `AbilityWorld`, `NativeEffectCatalog`, `AbilityGeometry`, `SurfaceRecipeView` and `EffectRenderResources`.

Similar techniques intentionally repeat their renderer code here so comparing two files exposes the actual work. Keep common visual math in the shared geometry/data layer; when changing renderer plumbing, check the corresponding techniques together. [TechniqueValidation.cs](../../Editor/TechniqueValidation.cs) checks component topology, upload accounting, retirement/reactivation, zero-card layers and disposal across all techniques.

Unity shaders and VFX assets retain their native file formats. Each technique file names its shader dependencies at the top. `AuthoredRibbon.shader` serves LineRenderer/combined mesh; `AuthoredCore.shader` and `AuthoredAdornment.shader` serve procedural draws; `AuthoredParticle.shader` serves particle streams. `Editor/EffectGraphCompiler.cs` generates the shared/per-ability VFX graphs and configures batching. Those GPU programs share `AbilityCommon.hlsl`, `RecipeData.hlsl` and `AuthoredSurface.hlsl` where applicable.

The original line-only study under `Assets/LineLab/Runtime/` and historical benchmark source snapshots retain their historical organization.

Run the Editor regression checks from **Line Lab > Validation > Validate technique organization**. For a live presentation check, enter Play mode in scene 03 and execute `Documentation/Validation/TechniquePresentationProbe.cs` with Unity CLI `eval_file`; it waits for real frames across all 24 core/full configurations and checks populations, stream census and direct-job callbacks. `Documentation/Validation/ParticleReactivationProbe.cs` separately checks visible pooled-particle geometry after retirement and reuse across frames.
