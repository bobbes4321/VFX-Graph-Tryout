using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.ParticleSystemJobs;

namespace LineLab.Production
{
    /// <summary>The same card recipe feeds copied particles and direct particle jobs.</summary>
    public struct SurfaceRecipeView
    {
        [ReadOnly]
        public NativeArray<LineData> Lines;

        [ReadOnly]
        public NativeArray<LineData> Strands;

        [ReadOnly]
        public NativeArray<EffectRecipeData> Effects;

        [ReadOnly]
        public NativeArray<RibbonRecipeData> Ribbons;

        [ReadOnly]
        public NativeArray<LayerRecipeData> Layers;

        [ReadOnly]
        public NativeArray<float4> Ramps;
        public int RibbonSlots;
        public int LayerSlots;
        public int Segments;
        public int LayerSlot;
        public int CardsPerAbility;
        public bool Core;
        public float Clock;
        public float3 Camera;

        public Adornment Evaluate(int index)
        {
            if (Core)
            {
                int strandIndex = index / Segments;
                int segment = index % Segments;
                var line = Strands[strandIndex];
                int effectIndex = (int)line.Shape.x;
                var recipe = Effects[effectIndex];
                var ribbon = Ribbons[effectIndex * RibbonSlots + strandIndex % RibbonSlots];
                AbilityGeometry.Segment(
                    line,
                    recipe,
                    ribbon,
                    Ramps,
                    segment,
                    Segments,
                    Clock,
                    Camera,
                    out var midpoint,
                    out var tangent,
                    out var side,
                    out float length,
                    out var color
                );
                return new Adornment
                {
                    Position = midpoint,
                    X = tangent,
                    Y = side,
                    Size = new float2(
                        length,
                        AbilityGeometry.Width(line, ribbon, Ramps, (segment + .5f) / Segments)
                    ),
                    Color = color.xyz,
                    Alpha = color.w,
                    TextureIndex = ribbon.Surface.x,
                };
            }

            var ability = Lines[index / CardsPerAbility];
            int effect = (int)ability.Shape.x;
            var layer = Layers[effect * LayerSlots + LayerSlot];
            AbilityGeometry.Decor(
                ability,
                Effects[effect],
                layer,
                index % CardsPerAbility,
                Clock,
                Camera,
                Ramps,
                out var card
            );
            return card;
        }
    }

    [BurstCompile]
    public struct BuildSurfaceParticles : IJobParallelFor
    {
        public SurfaceRecipeView Recipe;
        public NativeArray<ParticleSystem.Particle> Particles;
        public NativeArray<float4> CustomData;
        public bool Billboard;
        public float3 CameraRight;
        public float3 CameraUp;

        public void Execute(int index)
        {
            var card = Recipe.Evaluate(index);
            var rotation = new quaternion(
                new float3x3(card.X, card.Y, math.normalizesafe(math.cross(card.X, card.Y)))
            );
            float3 angles = math.degrees(math.EulerZXY(rotation));
            if (Billboard)
            {
                angles = new float3(
                    0,
                    0,
                    -math.degrees(
                        math.atan2(math.dot(card.X, CameraUp), math.dot(card.X, CameraRight))
                    )
                );
            }

            Particles[index] = new ParticleSystem.Particle
            {
                position = card.Position,
                rotation3D = angles,
                startSize3D = new Vector3(card.Size.x, card.Size.y, 1),
                startColor = new Color(1, 1, 1, card.Alpha),
                startLifetime = 1000000,
                remainingLifetime = 1000000,
                randomSeed = (uint)index + 1,
            };
            CustomData[index] = new float4(card.TextureIndex, card.Color);
        }
    }

    [BurstCompile]
    public struct UpdateSurfaceParticles : IJobParticleSystemParallelFor
    {
        public SurfaceRecipeView Recipe;

        public void Execute(ParticleSystemJobData data, int index)
        {
            var card = Recipe.Evaluate(index);
            var positions = data.positions;
            var rotations = data.rotations;
            var sizes = data.sizes;
            var colors = data.startColors;
            var ages = data.aliveTimePercent;
            var custom = data.customData1;
            positions[index] = card.Position;
            rotations[index] = math.EulerZXY(
                new quaternion(
                    new float3x3(card.X, card.Y, math.normalizesafe(math.cross(card.X, card.Y)))
                )
            );
            sizes[index] = new float3(card.Size.x, card.Size.y, 1);
            colors[index] = new Color(1, 1, 1, card.Alpha);
            ages[index] = 0;
            custom[index] = new float4(card.TextureIndex, card.Color);
        }
    }
}
