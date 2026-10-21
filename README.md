# RTS Line Laboratory

This Unity project compares eleven ways to render RTS links and complete layered abilities while keeping gameplay commands unmanaged and Burst-compatible. The current complete-effect implementation is also a reusable authoring proof of concept: designers compose paths, ribbons, traveling motes, endpoint coronas and ground cards through assets rather than hardcoded style branches.

Open `Assets/LineLab/Scenes/03_Complete_Abilities.unity` and press Play. Switch between the three-effect reference library and the four-effect artist library, choose recipes/renderers/workloads, and scale up to 5,000 slots. Use **Line Lab > Authoring > Effect Workbench** for inline module editing, allocation validation, compilation and explicit preview rebuilds.

Start with:

- [Artist Workflow](Documentation/ArtistWorkflow.md): add a new line or reusable mote without C#.
- [Code Overview](Documentation/CodeOverview.md): complete current source map and content-to-pixels architecture.
- [Integration](Documentation/Integration.md): actual Burst healer job, EffectId lookup and ownership contract.
- [Production Guide](Documentation/Production.md): rendering stacks, build, suites and timing interpretation.
- [Adversarial Review](Documentation/AdversarialReview.md): original findings and acceptance requirements.
- [Measured authored findings](Documentation/AuthoredFindings.md): audited scaling through 5,000 and interpretation limits.
- [Report index](BenchmarkResults/authored/report.html): completed audited runs and visual examples.

The authored player target is `Builds/AuthoredLab/AuthoredLab.exe`. Build it using **Line Lab > 4 - Build authored combat effects player**, then run:

```powershell
./Tools/production_benchmark.ps1 -Suite smoke
./Tools/production_benchmark.ps1 -Suite scale
./Tools/production_benchmark.ps1 -Suite full
```

New runs use unique directories under `BenchmarkResults/authored/`, including catalog/content/suite identity, raw samples, census and audited reports. The scale suite includes all techniques and baseline at 512, 1,024, 2,500 and 5,000 complete abilities in sustain and spread layouts. The authored scaling run passed its audit with 288 cells and 69,120 raw frames. The report index distinguishes audited results from incomplete or failed runs; the emitted audit/run artifacts determine what actually finished.

The [consolidated comparison](BenchmarkResults/authored/comparison/report.html) selects 612 audited source cells and 146,880 raw frames across seventeen conditions and all twelve configurations, with source/build/cell provenance. It includes the corrected 144-cell wide-field run through 5,000 effects and excludes earlier clipped-camera rows. See the findings for the census-readback fix, HDR color correction and exact coverage limits.

## Techniques

1. Shared-buffer VFX Graph.
2. Pooled per-ability VFX Graph, batching disabled.
3. Pooled per-ability VFX Graph, Auto instance batching.
4. Unity LineRenderer ribbons.
5. Combined mesh generated in Burst.
6. Procedural GPU ribbons/cards.
7. Shared Shuriken billboards with SetParticles.
8. Per-ability Shuriken pools.
9. Shared Shuriken mesh particles.
10. GPU-instanced Shuriken mesh particles.
11. Direct Burst particle jobs.

Complete LineRenderer/mesh stacks include an explicitly measured procedural secondary companion. Baseline renders the same environment and performs common native command/ribbon work. CPU/GPU values have different boundaries; GPU delta is a whole-frame baseline estimate, not an isolated effect-pass timestamp.

## Historical studies

The original line-only scenes remain `01_VFX_Showcase.unity` and `02_Technique_Lab.unity`. Their player is `Builds/LineLab/LineLab.exe`. See [line findings](Documentation/Findings.md), `BenchmarkResults/reference/`, `legacy-main-thread/` and `allocation-probe/`. The two original full matrices contain 1,800 cells and 324,000 raw samples in total. Reproduce that separate study with `Tools/benchmark.ps1`.

The earlier complete-ability study remains in `BenchmarkResults/production/`, with `Builds/ProductionLab/ProductionLab.exe` and [historical complete-effect findings](Documentation/ProductionFindings.md). Its 360-cell measurements describe the earlier hardcoded implementation. They are preserved, not reassigned to the authored refactor. The [archived code walkthrough](Documentation/History/CodeOverview-v1.md) describes that version.

The runtime has no Unity Entities or custom ECS dependency. Managed owners create native storage once; gameplay jobs receive only native writers and value IDs. Editor-only graph compilation uses a reflection adapter; the player runs compiled assets and public rendering APIs. Original environment/profile artwork is included without external asset-license requirements.
