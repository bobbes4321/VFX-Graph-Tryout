# Authoring reusable line effects

The complete-ability experiment now has source assets rather than named styles hidden in geometry code. The original line-only experiment remains a separate historical comparison.

## Start here

1. Open `Assets/LineLab/Scenes/03_Complete_Abilities.unity`.
2. If recipe assets do not exist yet, run **Line Lab > Authoring > Create Missing Sample Recipes**. It creates only absent assets; existing recipes, textures, scenes and graphs are preserved.
3. Open **Line Lab > Authoring > Effect Workbench**. Choose **ArtistLibrary** to see **VoidConduit** as well as the three reference recipes.
4. Enter Play Mode. Select a recipe, then press **Rebuild Preview with this library and effect**. This is an explicit pool rebuild: structural changes become visible together.
5. **Compile Library for current preview capacity** explicitly prepares generated renderer graphs. Preview rebuild performs this step automatically for the current ability count; it does not compile every benchmark capacity.
6. Edit an inline module or its source asset in the normal Inspector. Unity Undo applies. **Save assets** persists source edits; **Rebuild Preview** applies them to the running preview.

Validation reports reserved ribbon/particle slots, quads per ability, texture count and the baked content fingerprint. Errors identify the source asset and field. Rebuilding preview preserves scene camera edits and source assets; creating the initial scene is a different operation.

## Asset responsibilities

| Asset | Purpose | Typical edit |
| --- | --- | --- |
| `EffectLibrary` | Ordered recipe catalog and shared ribbon segment count | Add a recipe; rebuild IDs and pools after reordering |
| `EffectDefinition` | Gameplay key, HDR tint, base width, charge/release timing and module references | Duplicate Renewal and name a new ability |
| `LinePathAsset` | Reusable centerline shape and brightness pulse | Slow a helix; change lightning cell density |
| `RibbonLayerAsset` | One ribbon's width, gradient, texture, opacity and phase | Add a broad halo or narrow white filament |
| `ParticleLayerAsset` | One secondary layer's motion, count, profile, gradient, size and release tail | Make long reverse-travelling motes |

Source assets live in `Assets/LineLab/Recipes/`. Libraries live in `Assets/LineLab/Resources/Production/`. Referenced texture pixels are copied into a runtime atlas; originals are never modified.

`EffectLibrary` is the three-recipe reference catalog. `ArtistLibrary` adds VoidConduit and a fifth secondary slot, intentionally changing allocated topology. Both share the original recipe assets. Their different fingerprints and allocations must not be treated as the same benchmark workload.

## Add a new line without C#

1. Duplicate an `EffectDefinition` in the Project window. Set its asset name and unique **Stable Key**, such as `medic-chain`.
2. Reuse a path, or duplicate it before editing. Editing a shared module changes all effects using it.
3. Reuse, duplicate, remove or reorder ribbon references. Set **Width Along Line** with a curve and source-to-target color with the HDR **Color Along Line** gradient.
4. Add the definition to the library's **Effects** list.
5. Validate and rebuild preview.

The asset name is the display name. The stable key is the gameplay lookup key; renaming an asset need not change it. IDs are session-local indices assigned by library order. Resolve keys again after rebuilding the catalog.

Programmers must replace cached IDs and world/renderer pools together during a library rebuild. An old numeric ID can remain in range after reordering but select a different effect; the stable key is the identity to retain across sessions.

An empty ribbon list creates a particle-only effect. An empty secondary list creates a core-only effect. A missing reference is an error: remove the slot or assign a module. A particle count of zero disables its layer.

## Make and reuse a new mote

Create **Assets > Create > Line Lab > Particle Layer**, or duplicate `RenewalMotes`.

- Choose **Traveling**, which evaluates particle positions along the current source-to-target path.
- Change **Profile**. Null deliberately means a white card. Size and orbit are in world metres; aspect ratio means width divided by height.
- Set **Cycles Per Second**. Positive values travel toward the target; negative values reverse travel.
- Edit **Color Over Life** and **Size Over Life**. Traveling layers sample normalized distance along the line. Ballistic layers sample normalized age. Corona and ground layers sample normalized particle index. Corona pulse speed controls size oscillation in radians per second, separately from texture/color playback.
- Reference the same module in two definitions. Its edits propagate to both; duplicate it first when only one should change.
- Rebuild and compare renderers. Supported building-block variations require no renderer-specific script/shader edits.

VoidConduit demonstrates two ribbons, a source-to-target gradient, a different width curve and reversed path motion. It combines the shared `RenewalMotes` asset with `VoidDrift`: spark profile, stretched aspect ratio, reverse travel, a different size curve and a 2.4-second release tail. Its additional secondary slot proves composition is not restricted to the original four layers.

Release time controls each layer's fade after Release. Retirement uses the longest active module/core tail. Native endpoint values remain available during the tail; source/target GameObjects need not remain alive.

A new Begin in the same slot replaces its previous visual, including an unfinished tail. If a new cast and the previous release must overlap, gameplay needs a separate slot for each. The supplied healer example intentionally owns one visual slot per healer.

## Building blocks and explicit limits

Paths: helix, lightning, wave and straight. Secondary motion: traveling/orbiting particles, analytic ballistic endpoint particles, endpoint coronas and ground-plane cards. Endpoint layers can choose source, target or both. A completely new motion algorithm requires matching CPU/GPU implementation; ordinary variations of these algorithms are asset edits.

Limits: eight ribbons, twelve secondary layers, 4,096 particles per layer, 1-128 ribbon segments, and 64 texture tiles including default white. Validation rejects unsupported enums, duplicate/empty keys, missing references and invalid/nonfinite values. Curves are sampled at 32 positions; narrower details are not retained.

Profiles are resampled into 256-pixel uniform atlas tiles with duplicated two-pixel borders and no mipmaps. Linear RGBAHalf storage and ARGBHalf readback preserve HDR profile values; invalid/nonfinite pixels produce an actionable error. Unreadable imported textures work through GPU readback during setup. All reference renderers sample full tiles consistently; duplicated borders trade a small padded edge for filtering isolation. This atlas suits effect profiles, not high-resolution character art. Inspector tint/gradient colors convert to linear once during baking.

Allocation is uniform across recipes in a library. Every ability reserves the maximum ribbon count and largest particle count at each secondary slot. Unused slots have zero opacity/count. Adding one large module can increase reserved work for every ability; the workbench displays that cost. Adaptive compaction/LOD would be a separate rendering policy.

Keep comparable layer roles at the same slot across recipes where practical, and consider separate libraries for substantially different compositions. For the Shuriken billboard techniques, a ground layer in any recipe makes that whole secondary slot use mesh cards, preserving world-plane orientation. Consequently, adding or reordering one recipe can change the cost of another recipe in the same library. This is a packaging constraint, not an artist field silently being ignored.

A new motion algorithm, blend/material model, distortion pass, collision response or historical trail still requires a programmer and cross-renderer validation. Existing profiles, gradients, size curves, motion parameters and supported module composition remain artist-editable asset operations.

## Programmer boundary

`NativeEffectCatalog(EffectLibrary)` validates and compiles managed assets once. It owns persistent native arrays, sampled ramps and the atlas. Public callers receive read-only borrowed views (`EffectRecipes`, `RibbonRecipes`, `LayerRecipes`, `RampSamples`), valid until catalog disposal. Internal renderer adapters share the underlying native storage without copying. Burst gameplay receives value IDs, handles and native commands.

```csharp
// Managed setup, before scheduling jobs.
var catalog = new NativeEffectCatalog(library);
EffectId healingEffect = catalog.GetEffectId("renewal");

// Copy healingEffect into the gameplay producer job.
// The job uses the normal Begin / Follow / Release writer API.
// It never reads strings, ScriptableObjects, Gradients, curves or Textures.
```

Complete dependent jobs, dispose rendering buffers/pools, then dispose the native world/catalog according to their owners' contract. Never dispose a catalog while a job or renderer reads it. A pool rebuild follows the same order and resolves fresh IDs. Unknown keys throw rather than silently choosing another effect.

`EffectRecipeData.cs` documents every packed float4. `NativeEffectCatalog` preserves source-to-slot order and pads unused slots. The SHA-256 fingerprint includes schema, segments/topology, stable keys, baked records, sampled ramps and the exact raw half-float atlas bytes, without Unity instance IDs. The atlas is never converted to clipped eight-bit color for hashing. Display names remain metadata.

The workbench calls the demo host's `Library`, `RebuildLibrary()` and `SetPreviewEffect(int)` hooks. Reusable gameplay does not depend on the workbench or demo scene.

## Verification

LineRenderer's native vertex colors are UNorm8. The adapter normalizes its eight gradient samples in Burst and restores HDR through a per-ribbon shader multiplier. This preserves brightness above one while retaining quantization/interpolation differences from the other renderers. The real-engine probe and focused benchmark are linked from the measured findings.

This guide describes the implemented source workflow and explicit limits. The main integration task records actual Unity compilation, authoring checks, renderer parity captures and standalone measurements separately. Rerunning sample initialization creates missing assets but does not reset edited assets.

The optional **Line Lab > Authoring > Run Authoring Acceptance** command exercises real AssetDatabase workflows in Edit Mode. It creates missing proof variants/modules under `Recipes/Proof`, uses them in a separate ProofLibrary, and writes `Logs/authoring-acceptance.json`. The log's passed/checks fields, rather than merely running the menu command, establish whether those acceptance checks completed. Existing edited proof assets are preserved.
