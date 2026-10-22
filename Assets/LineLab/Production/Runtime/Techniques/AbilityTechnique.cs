using System;
using UnityEngine;

namespace LineLab.Production
{
    // Shared contract only. Technique-specific setup, jobs, presentation and cleanup
    // live together in the adjacent technique files (see README.md).
    internal abstract class AbilityTechnique : IDisposable
    {
        protected readonly AbilityTechniqueContext Context;

        protected AbilityTechnique(AbilityTechniqueContext context)
        {
            Context = context;
        }

        public long UploadBytes { get; protected set; }
        public virtual double CallbackMs => 0;
        public virtual bool InstancingActive => false;
        public virtual int LiveParticles => 0;

        public virtual int[] StreamParticleCounts() =>
            new int[1 + (Context.Full ? Context.Catalog.LayerSlots : 0)];

        public abstract void Present(AbilityWorld world, float clock, bool first);
        public abstract void Dispose();
    }

    internal sealed class AbilityTechniqueContext
    {
        public readonly GameObject Root;
        public readonly int Count;
        public readonly Camera Camera;
        public readonly bool Full;
        public readonly NativeEffectCatalog Catalog;
        public readonly EffectRenderResources Resources;
        public readonly Bounds Bounds;

        public AbilityTechniqueContext(
            GameObject root,
            int count,
            Camera camera,
            bool full,
            NativeEffectCatalog catalog,
            EffectRenderResources resources,
            Bounds bounds
        )
        {
            Root = root;
            Count = count;
            Camera = camera;
            Full = full;
            Catalog = catalog;
            Resources = resources;
            Bounds = bounds;
        }

        public bool HasGroundLayer(int slot)
        {
            for (int effect = 0; effect < Catalog.EffectCount; effect++)
                if ((int)Catalog.Layers[effect * Catalog.LayerSlots + slot].Layout.x == 3)
                    return true;
            return false;
        }

        public SurfaceRecipeView Recipe(AbilityWorld world, float clock, bool core, int slot = 0) =>
            new SurfaceRecipeView
            {
                Lines = world.Lines,
                Strands = world.Strands,
                Effects = Catalog.Effects,
                Ribbons = Catalog.Ribbons,
                Layers = Catalog.Layers,
                Ramps = Catalog.Ramps,
                RibbonSlots = Catalog.RibbonSlots,
                LayerSlots = Catalog.LayerSlots,
                Segments = Catalog.Segments,
                Core = core,
                Clock = clock,
                Camera = Camera.transform.position,
                LayerSlot = slot,
                CardsPerAbility = core
                    ? Catalog.AllocatedCoreQuadsPerAbility
                    : Catalog.LayerCapacities[slot],
            };
    }
}
