using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

namespace LineLab.Production
{
    /// <summary>
    /// Managed presentation boundary for a baked effect library. Gameplay only
    /// writes AbilityCommands; authoring assets are never consulted in a job.
    /// </summary>
    public sealed class AbilityBackend : IDisposable
    {
        readonly Technique technique;
        readonly NativeEffectCatalog catalog;
        readonly EffectRenderResources resources;
        readonly int count;
        readonly Camera camera;
        readonly CoreBackend core;
        readonly VisualEffect[] effects;
        readonly GraphicsBuffer lines;
        readonly Material[] materials;
        readonly ParticleSurfacePool[] layers;
        readonly Bounds bounds;
        public Bounds WorldBounds => bounds;

        public readonly GameObject Root;
        public readonly bool Full;
        bool disposed;
        public long UploadBytes { get; private set; }

        public double CallbackMs
        {
            get
            {
                double total = core?.ParticleCallbackMs ?? 0;
                if (layers != null)
                {
                    foreach (var layer in layers)
                    {
                        total += layer.CallbackMs;
                    }
                }

                return total;
            }
        }

        public bool InstancingActive =>
            technique == Technique.VfxPerLineBatched
            || (technique == Technique.ParticleMeshInstanced && core.InstancingActive);
        public int ObjectCount => Root.GetComponentsInChildren<Transform>(true).Length;
        public string Composition =>
            technique == Technique.Baseline ? "Environment and native commands / no effects"
            : IsVfx
                ? "Authored VFX graph / "
                    + catalog.RibbonSlots
                    + " ribbon slots + "
                    + (Full ? catalog.LayerSlots : 0)
                    + " secondary streams"
            : IsParticle
                ? "Authored native Shuriken / " + (Full ? catalog.LayerSlots + 1 : 1) + " streams"
            : "Authored ribbons + "
                + (Full ? catalog.LayerSlots : 0)
                + " procedural secondary streams";

        bool IsVfx =>
            technique == Technique.VfxBuffer
            || technique == Technique.VfxPerLine
            || technique == Technique.VfxPerLineBatched;

        bool IsParticle =>
            technique >= Technique.ParticleBillboardShared && technique <= Technique.ParticleJobs;

        /// <summary>
        /// Creates a fixed pool inside the supplied world-space culling volume.
        /// Include every possible endpoint, path excursion and secondary tail.
        /// Roots remain at the identity transform: analytic geometry is world-space.
        /// Per-ability VFX/particles currently share this conservative volume; group
        /// pools spatially when tighter per-region culling matters to your game.
        /// LineRenderer continues to compute its bounds from actual ribbon points.
        /// </summary>
        public AbilityBackend(
            Technique technique,
            int count,
            Camera camera,
            bool full,
            NativeEffectCatalog catalog,
            Bounds? worldBounds = null
        )
        {
            if (count < 1 || count > EffectRecipeLayout.MaximumAbilities)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            bounds = worldBounds ?? new Bounds(Vector3.zero, new Vector3(400, 100, 400));
            if (
                !math.all(math.isfinite((float3)bounds.center))
                || !math.all(math.isfinite((float3)bounds.size))
                || !math.all((float3)bounds.size > 0)
            )
            {
                throw new ArgumentException(
                    "Culling bounds need a finite center and positive finite size.",
                    nameof(worldBounds)
                );
            }

            this.technique = technique;
            this.count = count;
            this.camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Full = full;
            Root = new GameObject("Authored effects / " + TechniqueInfo.Names[(int)technique]);
            try
            {
                if (technique == Technique.Baseline)
                {
                    return;
                }

                resources = new EffectRenderResources(catalog);
                if (IsVfx)
                {
                    bool shared = technique == Technique.VfxBuffer;
                    if (shared)
                    {
                        lines = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count * 4, 16);
                    }

                    string graph = GraphResourcePath(catalog, technique, count, full);
                    var asset = Resources.Load<VisualEffectAsset>(graph);
                    if (asset == null)
                    {
                        throw new InvalidOperationException(
                            "This library topology needs compiling in the Effect Workbench. Missing graph: "
                                + graph
                        );
                    }

                    effects = new VisualEffect[shared ? 1 : count];
                    for (int i = 0; i < effects.Length; i++)
                    {
                        var go = new GameObject(
                            shared ? "Shared authored graph" : "Pooled authored graph " + i
                        );
                        go.transform.SetParent(Root.transform, false);
                        var effect = go.AddComponent<VisualEffect>();
                        effect.visualEffectAsset = asset;
                        effect.resetSeedOnPlay = false;
                        effect.startSeed = 41;
                        if (!effect.HasVector3("BoundsCenter") || !effect.HasVector3("BoundsSize"))
                        {
                            throw new InvalidOperationException(
                                "Recompile this library in the Effect Workbench: its graph predates configurable culling bounds."
                            );
                        }

                        effect.SetVector3("BoundsCenter", bounds.center);
                        effect.SetVector3("BoundsSize", bounds.size);
                        resources.Bind(effect);
                        effect.SetFloat(
                            "SpawnCore",
                            (shared ? count : 1) * catalog.AllocatedCoreQuadsPerAbility
                        );
                        if (full)
                        {
                            for (int layer = 0; layer < catalog.LayerSlots; layer++)
                            {
                                effect.SetFloat(
                                    "SpawnLayer" + layer,
                                    (shared ? count : 1) * catalog.LayerCapacities[layer]
                                );
                            }
                        }

                        if (shared)
                        {
                            effect.SetGraphicsBuffer("Lines", lines);
                        }

                        effects[i] = effect;
                    }

                    return;
                }

                core = new CoreBackend(technique, count, camera, resources, bounds);
                core.Root.transform.SetParent(Root.transform, false);
                if (!full || catalog.LayerSlots == 0)
                {
                    return;
                }

                if (IsParticle)
                {
                    layers = new ParticleSurfacePool[catalog.LayerSlots];
                    for (int slot = 0; slot < layers.Length; slot++)
                    {
                        bool perAbility = technique == Technique.ParticleBillboardPerLine;
                        bool billboard =
                            (technique == Technique.ParticleBillboardShared || perAbility)
                            && !HasGroundLayer(slot);
                        layers[slot] = new ParticleSurfacePool(
                            Root.transform,
                            "Secondary layer " + slot,
                            count,
                            catalog.LayerCapacities[slot],
                            perAbility,
                            billboard,
                            technique == Technique.ParticleMeshInstanced,
                            technique == Technique.ParticleJobs,
                            resources,
                            bounds
                        );
                    }
                }
                else
                {
                    lines = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count * 4, 16);
                    materials = new Material[catalog.LayerSlots];
                    for (int slot = 0; slot < materials.Length; slot++)
                    {
                        var material = resources.Material("AuthoredAdornment");
                        material.SetBuffer("_Lines", lines);
                        material.SetInt("_LayerSlot", slot);
                        material.SetInt("_CardsPerAbility", catalog.LayerCapacities[slot]);
                        materials[slot] = material;
                    }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public static string GraphResourcePath(
            NativeEffectCatalog catalog,
            Technique technique,
            int count,
            bool full
        )
        {
            bool shared = technique == Technique.VfxBuffer;
            string mode =
                shared ? "Shared"
                : technique == Technique.VfxPerLineBatched ? "Batched"
                : "Single";
            return "Production/Generated/"
                + catalog.LayoutKey
                + "_"
                + EffectRecipeLayout.GraphVersion
                + "/Ability"
                + mode
                + (full ? "Full_" : "Beam_")
                + (shared ? math.ceilpow2(count) : 1);
        }

        bool HasGroundLayer(int slot)
        {
            for (int effect = 0; effect < catalog.EffectCount; effect++)
            {
                if ((int)catalog.Layers[effect * catalog.LayerSlots + slot].Layout.x == 3)
                {
                    return true;
                }
            }

            return false;
        }

        public void Present(AbilityWorld world, float clock, bool first)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AbilityBackend));
            }

            UploadBytes = 0;
            if (technique == Technique.Baseline)
            {
                return;
            }

            var cameraPosition = camera.transform.position;
            if (lines != null)
            {
                lines.SetData(world.Lines.Reinterpret<float4>(64));
                UploadBytes += count * 64L;
            }

            if (effects != null)
            {
                for (int i = 0; i < effects.Length; i++)
                {
                    var effect = effects[i];
                    if (technique != Technique.VfxBuffer)
                    {
                        var line = world.Lines[i];
                        bool active = line.Start.w > 0;
                        if (effect.gameObject.activeSelf != active)
                        {
                            effect.gameObject.SetActive(active);
                        }

                        if (!active)
                        {
                            continue;
                        }

                        effect.SetVector4(EffectPropertyIds.Start, line.Start);
                        effect.SetVector4(EffectPropertyIds.End, line.End);
                        effect.SetVector4(EffectPropertyIds.Tint, line.Color);
                        effect.SetVector4(EffectPropertyIds.Shape, line.Shape);
                    }

                    effect.SetFloat(EffectPropertyIds.Clock, clock);
                    effect.SetVector3(EffectPropertyIds.CameraPosition, cameraPosition);
                    if (first)
                    {
                        effect.Reinit();
                    }
                }

                return;
            }

            core.Present(world, clock, first);
            UploadBytes += core.UploadBytes;
            if (!Full)
            {
                return;
            }

            for (int slot = 0; slot < catalog.LayerSlots; slot++)
            {
                if (layers != null)
                {
                    var view = new SurfaceRecipeView
                    {
                        Lines = world.Lines,
                        Strands = world.Strands,
                        Effects = catalog.Effects,
                        Ribbons = catalog.Ribbons,
                        Layers = catalog.Layers,
                        Ramps = catalog.Ramps,
                        RibbonSlots = catalog.RibbonSlots,
                        LayerSlots = catalog.LayerSlots,
                        Segments = catalog.Segments,
                        LayerSlot = slot,
                        CardsPerAbility = catalog.LayerCapacities[slot],
                        Core = false,
                        Clock = clock,
                        Camera = cameraPosition,
                    };
                    layers[slot].Present(view, camera, first);
                    UploadBytes += layers[slot].UploadBytes;
                }
                else if (materials != null)
                {
                    var material = materials[slot];
                    material.SetFloat(EffectPropertyIds.ShaderClock, clock);
                    material.SetVector(EffectPropertyIds.ShaderCamera, cameraPosition);
                    Graphics.DrawProcedural(
                        material,
                        bounds,
                        MeshTopology.Triangles,
                        6,
                        count * catalog.LayerCapacities[slot],
                        camera,
                        null,
                        ShadowCastingMode.Off,
                        false
                    );
                }
            }
        }

        public int[] StreamParticleCounts()
        {
            int streamCount = 1 + (Full ? catalog.LayerSlots : 0);
            var totals = new int[streamCount];
            if (effects != null)
            {
                var names = new List<string>();
                foreach (var effect in effects)
                {
                    if (!effect.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    names.Clear();
                    effect.GetParticleSystemNames(names);
                    foreach (var name in names)
                    {
                        // Generated graph system names carry their explicit stream number.
                        int stream = name.StartsWith("Core") ? 0 : ParseLayer(name) + 1;
                        if ((uint)stream < (uint)totals.Length)
                        {
                            totals[stream] += (int)effect.GetParticleSystemInfo(name).aliveCount;
                        }
                    }
                }
            }
            else if (core != null)
            {
                totals[0] = core.LiveParticles;
                if (layers != null)
                {
                    for (int i = 0; i < layers.Length; i++)
                    {
                        totals[i + 1] = layers[i].LiveParticles;
                    }
                }
            }

            return totals;
        }

        static int ParseLayer(string name)
        {
            if (!name.StartsWith("Layer"))
            {
                return -2;
            }

            int end = 5;
            while (end < name.Length && char.IsDigit(name[end]))
            {
                end++;
            }

            return int.TryParse(name.Substring(5, end - 5), out int layer) ? layer : -2;
        }

        public int LiveParticles()
        {
            int total = 0;
            if (effects != null)
            {
                foreach (var effect in effects)
                {
                    if (effect.gameObject.activeInHierarchy)
                    {
                        total += (int)effect.aliveParticleCount;
                    }
                }
            }
            else
            {
                total = core?.LiveParticles ?? 0;
                if (layers != null)
                {
                    foreach (var layer in layers)
                    {
                        total += layer.LiveParticles;
                    }
                }
            }

            return total;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (layers != null)
            {
                foreach (var layer in layers)
                {
                    layer?.Dispose();
                }
            }

            core?.Dispose();
            if (materials != null)
            {
                foreach (var material in materials)
                {
                    EffectRenderResources.DestroyOwned(material);
                }
            }

            lines?.Dispose();
            resources?.Dispose();
            EffectRenderResources.DestroyOwned(Root);
        }
    }
}
