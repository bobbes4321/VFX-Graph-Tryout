using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace LineLab.Production
{
    // BurstMesh: complete core and procedural secondary stack.
    // Surfaces: Shaders/AuthoredRibbon.shader and AuthoredAdornment.shader.
    internal sealed class BurstMeshTechnique : AbilityTechnique
    {
        readonly NativeEffectCatalog catalog;
        readonly Camera camera;
        readonly int strands;
        readonly Bounds bounds;
        readonly GameObject Root;
        readonly Material material;
        readonly Mesh mesh;
        readonly NativeArray<CoreVertex> vertices;
        readonly SecondaryStreams secondary;
        bool disposed;

        public BurstMeshTechnique(AbilityTechniqueContext context)
            : base(context)
        {
            catalog = context.Catalog;
            camera = context.Camera;
            bounds = context.Bounds;
            int abilities = context.Count;
            var resources = context.Resources;
            strands = checked(abilities * catalog.RibbonSlots);
            Root = new GameObject(
                "Core ribbons / " + TechniqueInfo.Names[(int)Technique.BurstMesh]
            );
            Root.transform.SetParent(context.Root.transform, false);
            try
            {
                material = resources.Material("AuthoredRibbon");
                vertices = new NativeArray<CoreVertex>(
                    checked(strands * catalog.Segments * 4),
                    Allocator.Persistent
                );
                mesh = new Mesh
                {
                    name = "Authored combined ribbons",
                    indexFormat = IndexFormat.UInt32,
                };
                mesh.MarkDynamic();
                mesh.SetVertexBufferParams(
                    vertices.Length,
                    new VertexAttributeDescriptor(
                        VertexAttribute.Position,
                        VertexAttributeFormat.Float32,
                        3
                    ),
                    new VertexAttributeDescriptor(
                        VertexAttribute.Color,
                        VertexAttributeFormat.Float32,
                        4
                    ),
                    new VertexAttributeDescriptor(
                        VertexAttribute.TexCoord0,
                        VertexAttributeFormat.Float32,
                        2
                    )
                );
                var indices = new int[checked(strands * catalog.Segments * 6)];
                for (int q = 0; q < indices.Length / 6; q++)
                {
                    int vertex = q * 4;
                    int at = q * 6;
                    indices[at] = vertex;
                    indices[at + 1] = vertex + 1;
                    indices[at + 2] = vertex + 2;
                    indices[at + 3] = vertex;
                    indices[at + 4] = vertex + 2;
                    indices[at + 5] = vertex + 3;
                }

                mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
                mesh.bounds = bounds;
                Root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = Root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
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
            var recipe = Context.Recipe(world, clock, true);
            new BuildCoreMesh
            {
                Recipe = recipe,
                Vertices = vertices,
                AtlasGrid = catalog.AtlasGrid,
            }
                .Schedule(strands * catalog.Segments, 64)
                .Complete();
            mesh.SetVertexBufferData(
                vertices,
                0,
                0,
                vertices.Length,
                0,
                MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            );
            UploadBytes = vertices.Length * 36L;
            UploadBytes += secondary.UploadBytes;
            secondary.Draw(clock);
        }

        public override void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            secondary?.Dispose();
            if (vertices.IsCreated)
                vertices.Dispose();
            EffectRenderResources.DestroyOwned(mesh);
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

    [StructLayout(LayoutKind.Sequential)]
    public struct CoreVertex
    {
        public float3 Position;
        public float4 Color;
        public float2 Uv;
    }

    [BurstCompile]
    public struct BuildCoreMesh : IJobParallelFor
    {
        public SurfaceRecipeView Recipe;

        [NativeDisableParallelForRestriction]
        public NativeArray<CoreVertex> Vertices;
        public int AtlasGrid;

        public void Execute(int index)
        {
            var card = Recipe.Evaluate(index);
            for (int corner = 0; corner < 4; corner++)
            {
                float u = corner == 1 || corner == 2 ? 1 : 0;
                float v = corner >= 2 ? 1 : 0;
                Vertices[index * 4 + corner] = new CoreVertex
                {
                    Position =
                        card.Position
                        + (u - .5f) * card.X * card.Size.x
                        + (v - .5f) * card.Y * card.Size.y,
                    Color = new float4(card.Color, card.Alpha),
                    Uv = EffectRenderResources.AtlasUv(
                        new float2(u, v),
                        card.TextureIndex,
                        AtlasGrid
                    ),
                };
            }
        }
    }
}
