using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace LineLab.Production
{
    // VfxBuffer: setup, per-frame Unity calls, census and disposal.
    // Graph authoring: Editor/EffectGraphCompiler.cs; shader math: Shaders/AbilityCommon.hlsl.
    internal sealed class VfxBufferTechnique : AbilityTechnique
    {
        readonly int count;
        readonly Camera camera;
        readonly VisualEffect[] effects;
        readonly GraphicsBuffer lines;
        bool disposed;
        public override bool InstancingActive => false;

        public VfxBufferTechnique(AbilityTechniqueContext context)
            : base(context)
        {
            count = context.Count;
            camera = context.Camera;
            var catalog = context.Catalog;
            var resources = context.Resources;
            var bounds = context.Bounds;
            var Root = context.Root;
            bool full = context.Full;
            try
            {
                lines = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count * 4, 16);

                string graph = AbilityBackend.GraphResourcePath(
                    catalog,
                    Technique.VfxBuffer,
                    count,
                    full
                );
                var asset = Resources.Load<VisualEffectAsset>(graph);
                if (asset == null)
                {
                    throw new InvalidOperationException(
                        "This library topology needs compiling in the Effect Workbench. Missing graph: "
                            + graph
                    );
                }

                effects = new VisualEffect[1];
                for (int i = 0; i < effects.Length; i++)
                {
                    var go = new GameObject("Shared authored graph");
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
                    effect.SetFloat("SpawnCore", count * catalog.AllocatedCoreQuadsPerAbility);
                    if (full)
                    {
                        for (int layer = 0; layer < catalog.LayerSlots; layer++)
                        {
                            effect.SetFloat(
                                "SpawnLayer" + layer,
                                count * catalog.LayerCapacities[layer]
                            );
                        }
                    }

                    effect.SetGraphicsBuffer("Lines", lines);

                    effects[i] = effect;
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public override void Present(AbilityWorld world, float clock, bool first)
        {
            var cameraPosition = camera.transform.position;
            lines.SetData(world.Lines.Reinterpret<float4>(64));
            UploadBytes = count * 64L;
            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
                effect.SetFloat(EffectPropertyIds.Clock, clock);
                effect.SetVector3(EffectPropertyIds.CameraPosition, cameraPosition);
                if (first)
                {
                    effect.Reinit();
                }
            }
        }

        public override int[] StreamParticleCounts()
        {
            int streamCount = 1 + (Context.Full ? Context.Catalog.LayerSlots : 0);
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

        public override int LiveParticles
        {
            get
            {
                int total = 0;
                foreach (var effect in effects)
                    if (effect.gameObject.activeInHierarchy)
                        total += (int)effect.aliveParticleCount;
                return total;
            }
        }

        public override void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            lines?.Dispose();
            // The facade owns the hierarchy and destroys every pooled VFX component.
        }
    }
}
