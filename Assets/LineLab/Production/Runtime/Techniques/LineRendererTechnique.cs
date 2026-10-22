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
    // LineRenderer: complete core and procedural secondary stack.
    // Surfaces: Shaders/AuthoredRibbon.shader and AuthoredAdornment.shader.
    internal sealed class LineRendererTechnique : AbilityTechnique
    {
        readonly NativeEffectCatalog catalog;
        readonly Camera camera;
        readonly int strands;
        readonly Bounds bounds;
        readonly GameObject Root;
        readonly Material material;
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
        readonly SecondaryStreams secondary;
        bool disposed;

        public LineRendererTechnique(AbilityTechniqueContext context)
            : base(context)
        {
            catalog = context.Catalog;
            camera = context.Camera;
            bounds = context.Bounds;
            int abilities = context.Count;
            var resources = context.Resources;
            strands = checked(abilities * catalog.RibbonSlots);
            Root = new GameObject(
                "Core ribbons / " + TechniqueInfo.Names[(int)Technique.LineRenderer]
            );
            Root.transform.SetParent(context.Root.transform, false);
            try
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

                int ribbonIndex = (int)line.Shape.x * catalog.RibbonSlots + i % catalog.RibbonSlots;
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
                properties.SetFloat(EffectPropertyIds.Tile, catalog.Ribbons[ribbonIndex].Surface.x);
                properties.SetFloat(EffectPropertyIds.LineColorScale, colorScales[i]);
                renderer.SetPropertyBlock(properties);
            }

            UploadBytes = strands * (catalog.Segments + 1) * 12L;
            UploadBytes += secondary.UploadBytes;
            secondary.Draw(clock);
        }

        public override void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            secondary?.Dispose();
            if (positions.IsCreated)
                positions.Dispose();
            if (colors.IsCreated)
                colors.Dispose();
            if (colorScales.IsCreated)
                colorScales.Dispose();
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
}
