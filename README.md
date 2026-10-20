# RTS Line Laboratory

For a guided explanation of the entire codebase, start with [the code walkthrough](Documentation/CodeOverview.md). It follows a healer command from Burst gameplay through native state to each renderer, maps every authored source file, and explains the benchmarks and report tools.

The new complete-ability scene is `Assets/LineLab/Scenes/03_Complete_Abilities.unity`, with its own player at `Builds/ProductionLab/ProductionLab.exe`. It adds three layered combat abilities, travelling particles, endpoint coronas, ground runes and retained release tails in an authored outpost environment. See [the complete-ability guide](Documentation/Production.md) and `BenchmarkResults/production/report.html`. The original line-only study below remains available.

Open `Assets/LineLab/Scenes/01_VFX_Showcase.unity` and press Play. The second scene is `02_Technique_Lab.unity`; Tab switches between them. The comparison scene has a technique selector, an eight-link visual atlas, line-count controls, and static/moving/churn/overdraw workloads. F12 saves a screenshot.

The standalone player is `Builds/LineLab/LineLab.exe`. The Results button opens `BenchmarkResults/report.html` in the project directory. Open that report for measured CPU/GPU costs, repeat ranges, scaling charts, and CSV export. Read `Documentation/Integration.md` for the unmanaged healer example and `Documentation/Methodology.md` for what each measurement does and does not include.

Start with [the measured findings](Documentation/Findings.md). The final reference matrix and audit are in `BenchmarkResults/reference`; `legacy-main-thread` retains the independently measured implementation before Burst LineRenderer preparation, and `allocation-probe` contains the separate development-player GC check. Both full matrices passed their audits: 1,800 cells and 324,000 raw samples in total.

## Techniques

1. VFX Graph, one system receiving a shared graphics buffer.
2. VFX Graph, one pooled object per line, instance batching disabled.
3. VFX Graph, one pooled object per line, automatic instance batching enabled.
4. Pooled Unity LineRenderers.
5. One ribbon mesh generated in Burst jobs.
6. Procedural GPU ribbons, one shared buffer and draw submission.
7. Shuriken, shared billboard segments updated through SetParticles.
8. Shuriken, one pooled billboard system per line.
9. Shuriken, shared mesh particles, GPU instancing disabled.
10. Shuriken, shared mesh particles, GPU instancing enabled.
11. Shuriken, direct Burst particle jobs writing native particle arrays.

A no-lines baseline runs the same native producer/consumer jobs and renders the same arena.

## Reproduce

Use the Unity menu **Line Lab → Build Windows player**, then in PowerShell:

```powershell
./Tools/benchmark.ps1 -Suite smoke
./Tools/benchmark.ps1 -Suite full
```

The full matrix has three repetitions, shuffled technique order, 1–5,000 line slots, 1080p and 1440p, four workload types, and 1/8/16/32-segment geometry tests. Not every dimension is crossed with every other dimension; the report only displays combinations actually measured. Logs and raw samples are retained per run. The report generator uses Python's standard library.

## Where the code is

* `Assets/LineLab/Runtime/LineCommands.cs`: blittable line data, parallel native command writer, Burst consumer, shared curve mathematics, deterministic workload producer.
* `Assets/LineLab/Runtime/HealerExample.cs`: complete Burst healer lifecycle example, independent of a specific ECS framework.
* `Assets/LineLab/Runtime/LineBackends.cs`: Unity presentation bridges and renderer-specific Burst jobs.
* `Assets/LineLab/Runtime/LineBenchmark.cs`: measurement protocol and raw export.
* `Assets/LineLab/Shaders/`: common line functions, procedural shader, ribbon shader, Shuriken instancing shader.
* `Assets/LineLab/Resources/LinesBuffer_*.vfx`: shared graph capacity variants. `LineSingle_*.vfx` and `LineSingleBatched_*.vfx`: per-line graphs with exposed endpoints.
* `Assets/LineLab/Editor/`: reproducible asset/scene generation, Windows player build, native lifecycle validation, and visual capture helpers.

The runtime does not depend on the CLI, the Pipeline package, Unity Entities, or any custom ECS. Editor graph generation uses reflection because Unity's VFX graph authoring model is internal; the player runs ordinary compiled VFX assets and public rendering APIs. Original project files are retained.

