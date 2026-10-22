using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace LineLab.Production
{
    // VfxPerLineBatched: setup, per-frame Unity calls, census and disposal.
    // Graph authoring: Editor/EffectGraphCompiler.cs; shader math: Shaders/AbilityCommon.hlsl.
    internal sealed class VfxPerLineBatchedTechnique : AbilityTechnique
    {
        readonly int count;
        readonly Camera camera;
        readonly VisualEffect[] effects;
        bool disposed;
        public override bool InstancingActive => true;

        public VfxPerLineBatchedTechnique(AbilityTechniqueContext context)
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
                string graph = AbilityBackend.GraphResourcePath(
                    catalog,
                    Technique.VfxPerLineBatched,
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

                effects = new VisualEffect[count];
                for (int i = 0; i < effects.Length; i++)
                {
                    var go = new GameObject("Pooled authored graph " + i);
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
                    effect.SetFloat("SpawnCore", catalog.AllocatedCoreQuadsPerAbility);
                    if (full)
                    {
                        for (int layer = 0; layer < catalog.LayerSlots; layer++)
                        {
                            effect.SetFloat("SpawnLayer" + layer, catalog.LayerCapacities[layer]);
                        }
                    }

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
            UploadBytes = 0;
            for (int i = 0; i < effects.Length; i++)
            {
                var effect = effects[i];
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
            // The facade owns the hierarchy and destroys every pooled VFX component.
        }
    }
}
