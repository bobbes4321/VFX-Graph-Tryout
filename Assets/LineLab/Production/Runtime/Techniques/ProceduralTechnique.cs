using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace LineLab.Production
{
    // Procedural: complete core and procedural secondary stack.
    // Surfaces: Shaders/AuthoredCore.shader and AuthoredAdornment.shader.
    internal sealed class ProceduralTechnique : AbilityTechnique
    {
        readonly NativeEffectCatalog catalog;
        readonly Camera camera;
        readonly int strands;
        readonly Bounds bounds;
        readonly GameObject Root;
        readonly Material material;
        readonly GraphicsBuffer buffer;
        readonly SecondaryStreams secondary;
        bool disposed;

        public ProceduralTechnique(AbilityTechniqueContext context)
            : base(context)
        {
            catalog = context.Catalog;
            camera = context.Camera;
            bounds = context.Bounds;
            int abilities = context.Count;
            var resources = context.Resources;
            strands = checked(abilities * catalog.RibbonSlots);
            Root = new GameObject(
                "Core ribbons / " + TechniqueInfo.Names[(int)Technique.Procedural]
            );
            Root.transform.SetParent(context.Root.transform, false);
            try
            {
                material = resources.Material("AuthoredCore");
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, strands * 4, 16);
                material.SetBuffer("_Lines", buffer);
                material.SetInt("_Segments", catalog.Segments);
                secondary = new SecondaryStreams(context);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public override void Present(AbilityWorld world, float clock, bool first)
        {
            UploadBytes = 0;
            secondary.Upload(world);
            buffer.SetData(world.Strands.Reinterpret<float4>(64));
            UploadBytes = strands * 64L;
            material.SetFloat(EffectPropertyIds.ShaderClock, clock);
            material.SetVector(EffectPropertyIds.ShaderCamera, camera.transform.position);
            Graphics.DrawProcedural(
                material,
                bounds,
                MeshTopology.Triangles,
                catalog.Segments * 6,
                strands,
                camera,
                null,
                ShadowCastingMode.Off,
                false
            );
            UploadBytes += secondary.UploadBytes;
            secondary.Draw(clock);
        }

        public override void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            secondary?.Dispose();
            buffer?.Dispose();
            EffectRenderResources.DestroyOwned(material);
            EffectRenderResources.DestroyOwned(Root);
        }

        sealed class SecondaryStreams : IDisposable
        {
            readonly AbilityTechniqueContext context;
            readonly GraphicsBuffer lines;
            readonly Material[] materials;
            public long UploadBytes { get; private set; }

            public SecondaryStreams(AbilityTechniqueContext context)
            {
                this.context = context;
                if (!context.Full || context.Catalog.LayerSlots == 0)
                    return;
                try
                {
                    lines = new GraphicsBuffer(
                        GraphicsBuffer.Target.Structured,
                        context.Count * 4,
                        16
                    );
                    materials = new Material[context.Catalog.LayerSlots];
                    for (int slot = 0; slot < materials.Length; slot++)
                    {
                        var material = context.Resources.Material("AuthoredAdornment");
                        materials[slot] = material;
                        material.SetBuffer("_Lines", lines);
                        material.SetInt("_LayerSlot", slot);
                        material.SetInt("_CardsPerAbility", context.Catalog.LayerCapacities[slot]);
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Upload(AbilityWorld world)
            {
                UploadBytes = 0;
                if (lines == null)
                    return;
                lines.SetData(world.Lines.Reinterpret<float4>(64));
                UploadBytes = context.Count * 64L;
            }

            public void Draw(float clock)
            {
                if (materials == null)
                    return;
                for (int slot = 0; slot < materials.Length; slot++)
                {
                    var material = materials[slot];
                    material.SetFloat(EffectPropertyIds.ShaderClock, clock);
                    material.SetVector(
                        EffectPropertyIds.ShaderCamera,
                        context.Camera.transform.position
                    );
                    Graphics.DrawProcedural(
                        material,
                        context.Bounds,
                        MeshTopology.Triangles,
                        6,
                        context.Count * context.Catalog.LayerCapacities[slot],
                        context.Camera,
                        null,
                        ShadowCastingMode.Off,
                        false
                    );
                }
            }

            public void Dispose()
            {
                if (materials != null)
                    foreach (var material in materials)
                        EffectRenderResources.DestroyOwned(material);
                lines?.Dispose();
            }
        }
    }
}
