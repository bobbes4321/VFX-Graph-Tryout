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

    [BurstCompile]
    public struct BuildCoreLinePositions : IJobParallelFor
    {
        public SurfaceRecipeView Recipe;

        [NativeDisableParallelForRestriction]
        public NativeArray<Vector3> Positions;

        [NativeDisableParallelForRestriction]
        public NativeArray<float4> Colors;

        public NativeArray<float> ColorScales;

        public void Execute(int index)
        {
            var line = Recipe.Strands[index];
            int effectIndex = (int)line.Shape.x;
            var effect = Recipe.Effects[effectIndex];
            var ribbon = Recipe.Ribbons[
                effectIndex * Recipe.RibbonSlots + index % Recipe.RibbonSlots
            ];
            for (int sample = 0; sample <= Recipe.Segments; sample++)
            {
                Positions[index * (Recipe.Segments + 1) + sample] = AbilityGeometry.Point(
                    line,
                    effect,
                    (float)sample / Recipe.Segments,
                    Recipe.Clock
                );
            }

            float colorScale = 1;
            for (int sample = 0; sample < 8; sample++)
            {
                float4 color = AbilityGeometry.Tint(
                    line,
                    effect,
                    ribbon,
                    Recipe.Ramps,
                    sample / 7f,
                    Recipe.Clock
                );
                Colors[index * 8 + sample] = color;
                colorScale = math.max(colorScale, math.cmax(color.xyz));
            }
            // LineRenderer stores UNorm8 colors. Preserve HDR with a shared
            // per-ribbon multiplier; the shader restores RGB after interpolation.
            ColorScales[index] = colorScale;
            for (int sample = 0; sample < 8; sample++)
            {
                float4 color = Colors[index * 8 + sample];
                color.xyz /= colorScale;
                Colors[index * 8 + sample] = color;
            }
        }
    }

    /// <summary>Ribbon-only adapters. VFX owns its complete graph in AbilityBackend.</summary>
    public sealed class CoreBackend : IDisposable
    {
        readonly Technique technique;
        readonly NativeEffectCatalog catalog;
        readonly EffectRenderResources resources;
        readonly Camera camera;
        readonly int abilities;
        readonly int strands;
        readonly Bounds bounds;
        readonly Material material;
        readonly GraphicsBuffer buffer;
        readonly Mesh mesh;
        readonly NativeArray<CoreVertex> vertices;
        readonly NativeArray<Vector3> positions;
        readonly NativeArray<float4> colors;
        readonly NativeArray<float> colorScales;
        readonly int[] assignedRibbonRecipes;
        readonly LineRenderer[] renderers;
        readonly AnimationCurve[] widthCurves;
        readonly Gradient gradient;
        readonly GradientColorKey[] colorKeys;
        readonly GradientAlphaKey[] alphaKeys;
        readonly MaterialPropertyBlock properties;
        readonly ParticleSurfacePool particles;
        bool disposed;
        public readonly GameObject Root;
        public long UploadBytes { get; private set; }
        public double ParticleCallbackMs => particles?.CallbackMs ?? 0;
        public bool InstancingActive => particles?.InstancingEnabled ?? false;
        public int LiveParticles => particles?.LiveParticles ?? 0;

        /// <summary>
        /// Shared geometry is evaluated in world space. Keep Root at identity;
        /// worldBounds drives mesh, procedural and particle culling. LineRenderer
        /// derives its bounds from the actual positions supplied each frame.
        /// </summary>
        public CoreBackend(
            Technique technique,
            int abilities,
            Camera camera,
            EffectRenderResources resources,
            Bounds worldBounds
        )
        {
            this.technique = technique;
            this.abilities = abilities;
            this.resources = resources;
            this.catalog = resources.Catalog;
            this.camera = camera;
            this.bounds = worldBounds;
            strands = checked(abilities * catalog.RibbonSlots);
            Root = new GameObject("Core ribbons / " + TechniqueInfo.Names[(int)technique]);
            try
            {
                if (technique == Technique.LineRenderer)
                {
                    material = resources.Material("AuthoredRibbon");
                    material.EnableKeyword("LINELAB_LINE_COLOR");
                    positions = new NativeArray<Vector3>(
                        strands * (catalog.Segments + 1),
                        Allocator.Persistent
                    );
                    colors = new NativeArray<float4>(strands * 8, Allocator.Persistent);
                    colorScales = new NativeArray<float>(strands, Allocator.Persistent);
                    assignedRibbonRecipes = new int[strands];
                    Array.Fill(assignedRibbonRecipes, -1);
                    renderers = new LineRenderer[strands];
                    gradient = new Gradient();
                    colorKeys = new GradientColorKey[8];
                    alphaKeys = new GradientAlphaKey[8];
                    properties = new MaterialPropertyBlock();
                    widthCurves = new AnimationCurve[catalog.Ribbons.Length];
                    for (int r = 0; r < widthCurves.Length; r++)
                    {
                        var keys = new Keyframe[EffectRecipeLayout.RampSamples];
                        for (int k = 0; k < keys.Length; k++)
                        {
                            keys[k] = new Keyframe(
                                k / 31f,
                                AbilityGeometry
                                    .SampleRamp(
                                        catalog.Ramps,
                                        (int)catalog.Ribbons[r].Surface.z,
                                        k / 31f
                                    )
                                    .x
                            );
                        }

                        widthCurves[r] = new AnimationCurve(keys);
                    }

                    for (int i = 0; i < strands; i++)
                    {
                        var go = new GameObject("Pooled ribbon " + i);
                        go.transform.SetParent(Root.transform, false);
                        var renderer = go.AddComponent<LineRenderer>();
                        renderer.sharedMaterial = material;
                        renderer.positionCount = catalog.Segments + 1;
                        renderer.useWorldSpace = true;
                        renderer.alignment = LineAlignment.View;
                        renderer.textureMode = LineTextureMode.Stretch;
                        renderer.numCapVertices = 0;
                        renderer.numCornerVertices = 0;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        renderers[i] = renderer;
                    }
                }
                else if (technique == Technique.BurstMesh)
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
                }
                else if (technique == Technique.Procedural)
                {
                    material = resources.Material("AuthoredCore");
                    buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, strands * 4, 16);
                    material.SetBuffer("_Lines", buffer);
                    material.SetInt("_Segments", catalog.Segments);
                }
                else
                {
                    bool perAbility = technique == Technique.ParticleBillboardPerLine;
                    bool billboard = perAbility || technique == Technique.ParticleBillboardShared;
                    particles = new ParticleSurfacePool(
                        Root.transform,
                        "Ribbon particles",
                        abilities,
                        catalog.AllocatedCoreQuadsPerAbility,
                        perAbility,
                        billboard,
                        technique == Technique.ParticleMeshInstanced,
                        technique == Technique.ParticleJobs,
                        resources,
                        bounds
                    );
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public void Present(AbilityWorld world, float clock, bool first)
        {
            UploadBytes = 0;
            var recipe = Recipe(world, clock);
            if (particles != null)
            {
                particles.Present(recipe, camera, first);
                UploadBytes = particles.UploadBytes;
            }
            else if (renderers != null)
            {
                new BuildCoreLinePositions
                {
                    Recipe = recipe,
                    Positions = positions,
                    Colors = colors,
                    ColorScales = colorScales,
                }
                    .Schedule(strands, 32)
                    .Complete();
                for (int i = 0; i < strands; i++)
                {
                    var line = world.Strands[i];
                    var renderer = renderers[i];
                    renderer.enabled = line.Start.w > 0 && line.Color.w > 0;
                    if (!renderer.enabled)
                    {
                        continue;
                    }

                    int ribbonIndex =
                        (int)line.Shape.x * catalog.RibbonSlots + i % catalog.RibbonSlots;
                    renderer.SetPositions(
                        positions.GetSubArray(i * (catalog.Segments + 1), catalog.Segments + 1)
                    );
                    renderer.widthMultiplier = line.End.w;
                    if (assignedRibbonRecipes[i] != ribbonIndex)
                    {
                        renderer.widthCurve = widthCurves[ribbonIndex];
                        assignedRibbonRecipes[i] = ribbonIndex;
                    }
                    for (int k = 0; k < 8; k++)
                    {
                        var color = colors[i * 8 + k];
                        colorKeys[k] = new GradientColorKey(
                            new Color(color.x, color.y, color.z),
                            k / 7f
                        );
                        alphaKeys[k] = new GradientAlphaKey(color.w, k / 7f);
                    }

                    gradient.SetKeys(colorKeys, alphaKeys);
                    renderer.colorGradient = gradient;
                    properties.SetFloat(
                        EffectPropertyIds.Tile,
                        catalog.Ribbons[ribbonIndex].Surface.x
                    );
                    properties.SetFloat(EffectPropertyIds.LineColorScale, colorScales[i]);
                    renderer.SetPropertyBlock(properties);
                }

                UploadBytes = strands * (catalog.Segments + 1) * 12L;
            }
            else if (mesh != null)
            {
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
            }
            else
            {
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
            }
        }

        SurfaceRecipeView Recipe(AbilityWorld world, float clock)
        {
            return new SurfaceRecipeView
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
                Core = true,
                Clock = clock,
                Camera = camera.transform.position,
                CardsPerAbility = catalog.AllocatedCoreQuadsPerAbility,
            };
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            particles?.Dispose();
            buffer?.Dispose();
            if (vertices.IsCreated)
            {
                vertices.Dispose();
            }

            if (positions.IsCreated)
            {
                positions.Dispose();
            }

            if (colors.IsCreated)
            {
                colors.Dispose();
            }
            if (colorScales.IsCreated)
            {
                colorScales.Dispose();
            }

            EffectRenderResources.DestroyOwned(material);
            EffectRenderResources.DestroyOwned(mesh);
            EffectRenderResources.DestroyOwned(Root);
        }
    }
}
