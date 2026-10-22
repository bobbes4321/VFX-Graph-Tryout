using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.ParticleSystemJobs;
using UnityEngine.Rendering;

namespace LineLab.Production
{
    // ParticleMeshShared: complete core/secondary pools, native preparation, Unity copies and cleanup.
    // Surface: Shaders/AuthoredParticle.shader (including Custom1 HDR and instancing).
    internal sealed class ParticleMeshSharedTechnique : AbilityTechnique
    {
        readonly Stream core;
        readonly Stream[] layers;
        bool disposed;

        public ParticleMeshSharedTechnique(AbilityTechniqueContext context)
            : base(context)
        {
            try
            {
                var root = new GameObject(
                    "Core ribbons / " + TechniqueInfo.Names[(int)Technique.ParticleMeshShared]
                );
                root.transform.SetParent(context.Root.transform, false);
                core = new Stream(
                    root.transform,
                    "Ribbon particles",
                    context.Count,
                    context.Catalog.AllocatedCoreQuadsPerAbility,
                    false,
                    context.Resources,
                    context.Bounds
                );
                if (!context.Full || context.Catalog.LayerSlots == 0)
                    return;
                layers = new Stream[context.Catalog.LayerSlots];
                for (int slot = 0; slot < layers.Length; slot++)
                    layers[slot] = new Stream(
                        context.Root.transform,
                        "Secondary layer " + slot,
                        context.Count,
                        context.Catalog.LayerCapacities[slot],
                        false,
                        context.Resources,
                        context.Bounds
                    );
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public override void Present(AbilityWorld world, float clock, bool first)
        {
            core.Present(Context.Recipe(world, clock, true), Context.Camera, first);
            UploadBytes = core.UploadBytes;
            if (layers == null)
                return;
            for (int slot = 0; slot < layers.Length; slot++)
            {
                layers[slot]
                    .Present(Context.Recipe(world, clock, false, slot), Context.Camera, first);
                UploadBytes += layers[slot].UploadBytes;
            }
        }

        public override bool InstancingActive => false;
        public override double CallbackMs
        {
            get
            {
                double total = core.CallbackMs;
                if (layers != null)
                    foreach (var layer in layers)
                        total += layer.CallbackMs;
                return total;
            }
        }
        public override int LiveParticles
        {
            get
            {
                int total = core.LiveParticles;
                if (layers != null)
                    foreach (var layer in layers)
                        total += layer.LiveParticles;
                return total;
            }
        }

        public override int[] StreamParticleCounts()
        {
            var totals = base.StreamParticleCounts();
            totals[0] = core.LiveParticles;
            if (layers != null)
                for (int slot = 0; slot < layers.Length; slot++)
                    totals[slot + 1] = layers[slot].LiveParticles;
            return totals;
        }

        public override void Dispose()
        {
            if (disposed)
                return;
            disposed = true;
            if (layers != null)
                foreach (var layer in layers)
                    layer?.Dispose();
            core?.Dispose();
        }

        sealed class Stream : IDisposable
        {
            readonly ParticleSystem[] systems;
            readonly NativeArray<ParticleSystem.Particle> particles;
            readonly NativeArray<Unity.Mathematics.float4> customData;
            readonly List<Vector4>[] custom;
            readonly bool[] initialized;
            readonly Material material;
            readonly int cardsPerAbility;

            readonly bool billboard;
            bool disposed;
            public double CallbackMs => 0;
            public long UploadBytes { get; private set; }
            public bool InstancingEnabled { get; }

            public int LiveParticles
            {
                get
                {
                    int count = 0;
                    foreach (var system in systems)
                    {
                        if (system.gameObject.activeInHierarchy)
                        {
                            count += system.particleCount;
                        }
                    }

                    return count;
                }
            }

            public Stream(
                Transform root,
                string name,
                int abilityCount,
                int cardsPerAbility,
                bool billboard,
                EffectRenderResources resources,
                Bounds bounds
            )
            {
                this.cardsPerAbility = cardsPerAbility;
                this.billboard = billboard;
                InstancingEnabled = false;
                if (cardsPerAbility == 0)
                {
                    systems = Array.Empty<ParticleSystem>();
                    custom = Array.Empty<List<Vector4>>();
                    initialized = Array.Empty<bool>();
                    return;
                }

                try
                {
                    int count = checked(abilityCount * cardsPerAbility);
                    particles = new NativeArray<ParticleSystem.Particle>(
                        count,
                        Allocator.Persistent
                    );
                    customData = new NativeArray<Unity.Mathematics.float4>(
                        count,
                        Allocator.Persistent
                    );
                    systems = new ParticleSystem[1];
                    custom = new List<Vector4>[systems.Length];
                    initialized = new bool[systems.Length];
                    material = resources.Material("AuthoredParticle");
                    for (int i = 0; i < systems.Length; i++)
                    {
                        var go = new GameObject(name + (" / shared"));
                        go.transform.SetParent(root, false);
                        var system = go.AddComponent<ParticleSystem>();
                        var main = system.main;
                        main.playOnAwake = false;
                        main.loop = false;
                        main.simulationSpace = ParticleSystemSimulationSpace.World;
                        main.maxParticles = count;
                        main.startSpeed = 0;
                        main.startLifetime = 1000000;
                        main.startSize3D = true;
                        main.startRotation3D = true;
                        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                        var emission = system.emission;
                        emission.enabled = false;
                        var shape = system.shape;
                        shape.enabled = false;
                        var data = system.customData;
                        data.enabled = true;
                        data.SetMode(
                            ParticleSystemCustomData.Custom1,
                            ParticleSystemCustomDataMode.Vector
                        );
                        data.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 4);
                        var renderer = go.GetComponent<ParticleSystemRenderer>();
                        renderer.sharedMaterial = material;
                        renderer.renderMode = billboard
                            ? ParticleSystemRenderMode.Billboard
                            : ParticleSystemRenderMode.Mesh;
                        renderer.alignment = billboard
                            ? ParticleSystemRenderSpace.View
                            : ParticleSystemRenderSpace.World;
                        if (!billboard)
                        {
                            renderer.mesh = Resources.Load<Mesh>("SegmentQuad");
                        }

                        renderer.enableGPUInstancing = false;
                        renderer.applyActiveColorSpace = false;
                        renderer.sortMode = ParticleSystemSortMode.None;
                        renderer.localBounds = bounds;
                        renderer.shadowCastingMode = ShadowCastingMode.Off;
                        renderer.receiveShadows = false;
                        renderer.SetActiveVertexStreams(
                            new List<ParticleSystemVertexStream>
                            {
                                ParticleSystemVertexStream.Position,
                                ParticleSystemVertexStream.Color,
                                ParticleSystemVertexStream.UV,
                                ParticleSystemVertexStream.Custom1XYZW,
                            }
                        );
                        systems[i] = system;
                        custom[i] = new List<Vector4>(main.maxParticles);
                        for (int p = 0; p < main.maxParticles; p++)
                        {
                            custom[i].Add(Vector4.zero);
                        }
                    }
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            public void Present(SurfaceRecipeView recipe, Camera camera, bool first)
            {
                UploadBytes = 0;
                if (cardsPerAbility == 0)
                {
                    return;
                }

                new BuildSurfaceParticles
                {
                    Recipe = recipe,
                    Particles = particles,
                    CustomData = customData,
                    Billboard = billboard,
                    CameraRight = camera.transform.right,
                    CameraUp = camera.transform.up,
                }
                    .Schedule(particles.Length, 64)
                    .Complete();
                for (int i = 0; i < systems.Length; i++)
                {
                    var system = systems[i];
                    if (!initialized[i])
                    {
                        system.Play();
                        system.Pause();

                        initialized[i] = true;
                    }

                    int offset = 0;
                    int length = particles.Length;
                    system.SetParticles(particles.GetSubArray(offset, length), length);
                    var values = custom[i];
                    for (int p = 0; p < length; p++)
                    {
                        var value = customData[offset + p];
                        values[p] = new Vector4(value.x, value.y, value.z, value.w);
                    }

                    system.SetCustomParticleData(values, ParticleSystemCustomData.Custom1);
                    UploadBytes += length * (long)(Marshal.SizeOf<ParticleSystem.Particle>() + 16);
                }
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                if (particles.IsCreated)
                {
                    particles.Dispose();
                }

                if (customData.IsCreated)
                {
                    customData.Dispose();
                }

                EffectRenderResources.DestroyOwned(material);
            }
        }

        [BurstCompile]
        struct BuildSurfaceParticles : IJobParallelFor
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
    }
}
