using System;
using Unity.Mathematics;
using UnityEngine;

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
        readonly AbilityTechnique implementation;
        readonly Bounds bounds;
        public Bounds WorldBounds => bounds;

        public readonly GameObject Root;
        public readonly bool Full;
        bool disposed;
        public long UploadBytes => implementation?.UploadBytes ?? 0;
        public double CallbackMs => implementation?.CallbackMs ?? 0;
        public bool InstancingActive => implementation?.InstancingActive ?? false;
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
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));
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
                var context = new AbilityTechniqueContext(
                    Root,
                    count,
                    camera,
                    full,
                    catalog,
                    resources,
                    bounds
                );
                implementation = technique switch
                {
                    Technique.VfxBuffer => new VfxBufferTechnique(context),
                    Technique.VfxPerLine => new VfxPerLineTechnique(context),
                    Technique.VfxPerLineBatched => new VfxPerLineBatchedTechnique(context),
                    Technique.LineRenderer => new LineRendererTechnique(context),
                    Technique.BurstMesh => new BurstMeshTechnique(context),
                    Technique.Procedural => new ProceduralTechnique(context),
                    Technique.ParticleBillboardShared => new ParticleBillboardSharedTechnique(
                        context
                    ),
                    Technique.ParticleBillboardPerLine => new ParticleBillboardPerLineTechnique(
                        context
                    ),
                    Technique.ParticleMeshShared => new ParticleMeshSharedTechnique(context),
                    Technique.ParticleMeshInstanced => new ParticleMeshInstancedTechnique(context),
                    Technique.ParticleJobs => new ParticleJobsTechnique(context),
                    _ => throw new ArgumentOutOfRangeException(nameof(technique)),
                };
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

        public void Present(AbilityWorld world, float clock, bool first)
        {
            if (disposed)
                throw new ObjectDisposedException(nameof(AbilityBackend));
            implementation?.Present(world, clock, first);
        }

        public int[] StreamParticleCounts() =>
            implementation?.StreamParticleCounts() ?? new int[1 + (Full ? catalog.LayerSlots : 0)];

        public int LiveParticles() => implementation?.LiveParticles ?? 0;

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            implementation?.Dispose();
            resources?.Dispose();
            EffectRenderResources.DestroyOwned(Root);
        }
    }
}
