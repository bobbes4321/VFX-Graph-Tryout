# Complete ability findings on this PC

The complete-effect experiment changes the practical answer. Shared VFX and procedural GPU ribbons remain the strongest CPU choices, and per-ability VFX with automatic batching remains viable at hundreds of abilities. However, the extra particle layers are a substantial GPU cost, and several classic particle paths become competitive on GPU time at 1440p. The original line-only results should not be extrapolated to a full effect stack.

The final release run passed its audit: **360 cells, 86,400 raw frames and 75,612 fresh GPU samples**, three shuffled repetitions, RTX 3070 / i9-11900F / Direct3D 12 / Unity 6000.3.10f1. Build GUID: `16f57280f9d2445bbd462a711d2e7c76`. The retained partial/preflight runs are excluded.

## Representative complete-effect costs

256 sustained moving abilities at 1080p. Each ability has three 32-segment ribbons and 84 secondary quads: **46,080 quads total**. Values are medians of three run summaries. Main CPU is the whole-frame measurement. GPU delta is a paired whole-frame baseline subtraction, not isolated GPU-pass busy time. CPU and GPU times overlap; do not add them.

| Technique | GPU delta ms | Main CPU ms | Presentation bridge ms | Bridge p95 ms | Presentation objects |
|---|---:|---:|---:|---:|---:|
| No lines / baseline | 0.000 | 1.261 | 0.000 | 0.001 | 1 |
| GPU · procedural ribbons | 0.716 | 1.634 | 0.016 | 0.024 | 2 |
| VFX · shared buffer | 0.735 | 1.697 | 0.005 | 0.007 | 2 |
| VFX · per line / batching on | 0.735 | 2.339 | 0.149 | 0.247 | 257 |
| Shuriken · shared billboards | 0.736 | 4.636 | 2.685 | 3.243 | 7 |
| Shuriken · instanced mesh | 0.757 | 4.398 | 3.158 | 3.725 | 7 |
| Unity LineRenderer | 0.843 | 2.837 | 0.649 | 0.888 | 770 |
| Shuriken · Burst particle jobs | 0.853 | 5.893 | 1.481 | 1.696 | 7 |
| Burst · combined mesh | 0.899 | 2.582 | 0.808 | 1.136 | 2 |
| Shuriken · shared mesh | 1.039 | 6.953 | 4.846 | 5.812 | 7 |
| Shuriken · per-line billboards | 1.398 | 7.790 | 4.005 | 4.759 | 1282 |
| VFX · per line / batching off | 1.704 | 10.271 | 0.185 | 0.297 | 257 |

## What becomes visible with complete effects

**The secondary layers are not free.** At 256 moving abilities, the shared VFX core-only GPU delta was 0.169 ms; the complete effect was 0.735 ms. The estimated extra GPU span was 0.566 ms. Procedural ribbons increased from 0.175 to 0.716 ms, an extra 0.541 ms. The baseline-corrected difference is an estimate with run noise. These are the same three-strand cores in both cases; this comparison does not confuse extra core strands with secondary-layer cost.

**One GameObject does not imply one cheap effect system.** Each complete per-ability VFX component owns five GPU systems. At 256 abilities with batching disabled, the whole main-thread frame rose from 3.342 ms for the core-only graph to 10.271 ms for the complete graph. The property-submission bridge remained roughly 0.19 ms in both cases. Most of that additional CPU work therefore occurs outside the input setter loop. A benchmark of setters alone would miss it.

**Automatic VFX instance batching matters much more for this full stack.** At 512 complete abilities, the measured main-thread frame was 19.911 ms with batching disabled, 3.651 ms with automatic batching and 2.322 ms with a shared graph. Unbatched per-ability VFX had a 23.379 ms frame p95, exceeding the entire 16.67 ms budget for 60 fps in this test scene. Batched per-ability VFX had a 5.255 ms frame p95; shared VFX had 2.781 ms. This gives a concrete tradeoff between prefab convenience and CPU budget, rather than a blanket prohibition on per-ability objects.

**Classic particles can trade CPU for a small GPU saving in this richer recipe.** At 256 complete abilities / 1440p / heavy overlap, shared billboards recorded a 1.124 ms GPU delta versus 1.166 ms for shared VFX and 1.157 ms for procedural ribbons. Their whole GPU-frame repeat ranges were 2.465–2.474 ms, 2.502–2.512 ms and 2.483–2.510 ms respectively. Main-thread frames were 4.835, 2.639 and 2.567 ms. That is roughly 0.04 ms lower reported GPU time for shared billboards, at about 2.2 ms more main-thread time than shared VFX. It is a small, hardware/workload-specific saving, not a general Shuriken GPU advantage. At 1080p / 256 sustained abilities, shared billboards and shared VFX were essentially tied on GPU delta (0.736 / 0.735 ms).

**Mesh-particle instancing becomes useful at higher density.** At 512 complete moving abilities / 1080p, ordinary shared mesh particles measured 3.657 ms total GPU and 13.422 ms main-thread frame time. Instanced mesh particles measured 2.788 ms GPU and 8.620 ms main-thread time. In this setup the rendering path matters at higher particle counts, even though it was less decisive for the original simple lines.

**Direct particle jobs remove an array submission, not all particle-system cost.** At 512 abilities, the particle-job bridge was 2.775 ms, but the main-thread frame was 11.476 ms. Native simulation/render preparation and scheduling remain relevant. Read whole-frame data alongside bridge timers.

**The line/mesh choices are explicit hybrids.** Their three ribbon strands use the named renderer; a procedural GPU companion supplies motes, sparks, coronas and ground runes. These full-stack costs do not describe LineRenderer or a CPU mesh magically owning free particles. Per-ability Shuriken packs its strands into one core system plus four secondary systems; per-ability VFX owns all five systems in one graph/component.

## Practical choice

- Start with **shared VFX** for a graph-oriented full-effect workflow and low CPU submission cost, or **procedural GPU rendering** for a code-oriented shader system. Their current implementations use different buffer/vertex strategies; neither is a universal theoretical optimum.
- Use **per-ability VFX with automatic batching** when independent effect authoring and lifecycles justify the measured CPU premium. Keep checking batching support as the graph evolves. Do not extrapolate from the disabled-batching variant.
- Keep **shared Shuriken billboards and instanced mesh particles** as credible candidates when CPU headroom exists and GPU time is the constraint. The observed GPU differences at 1440p are small enough that measuring the actual game is essential.
- A **LineRenderer + GPU particle companion** remains practical and familiar, but uses three line components per ability. A **Burst mesh + companion** gives geometry ownership at the cost of CPU construction and larger uploads.

## Reproduction and boundaries

Open `03_Complete_Abilities.unity` or `Builds/ProductionLab/ProductionLab.exe`. The report is `BenchmarkResults/production/report.html`; its selectors expose complete/core-only, 1080p/1440p, sustain/lifecycle/overdraw and scaling cases. Every timing has raw data, build/source hashes and configuration snapshots in `BenchmarkResults/production/reference`.

This is a fixed six-unit outpost environment, with stress links filling the same footprint at every count. It is not a complete RTS workload. Effects use analytic trajectories and additive cards; collision, distortion, history-based trails/smoke, dynamic effect lights and large-map spatial culling are not tested. Lifecycle retirement also differs: per-ability VFX disables its complete component, while per-ability Shuriken retains invisible secondary particles after disabling its core. Disabling those secondary pools as a group remains an available optimization. Shared paths do not compact inactive slots. See `Production.md` for exact packaging, unmanaged integration, bandwidth/vertex differences and the protocol.

The original 48 source/asset hashes still match the earlier line-only reference. All 125 source/asset hashes for the new release match the measured complete-effect build.
