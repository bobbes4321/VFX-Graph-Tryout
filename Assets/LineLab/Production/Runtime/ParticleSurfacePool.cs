using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;

namespace LineLab.Production
{
    /// <summary>
    /// Persistent Shuriken storage for one ribbon stream or reusable secondary
    /// layer. Custom1XYZW stores the atlas tile and unclipped HDR color.
    /// </summary>
    public sealed class ParticleSurfacePool : IDisposable
    {
        readonly ParticleSystem[] systems;
        readonly RecipeParticleBridge bridge;
        readonly NativeArray<ParticleSystem.Particle> particles;
        readonly NativeArray<Unity.Mathematics.float4> customData;
        readonly List<Vector4>[] custom;
        readonly bool[] initialized;
        readonly Material material;
        readonly int cardsPerAbility;
        readonly int abilityCount;
        readonly bool billboard;
        readonly bool perAbility;
        bool disposed;
        public double CallbackMs => bridge != null ? bridge.LastCallbackMs : 0;
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

        public ParticleSurfacePool(
            Transform root,
            string name,
            int abilityCount,
            int cardsPerAbility,
            bool perAbility,
            bool billboard,
            bool instanced,
            bool directJobs,
            EffectRenderResources resources,
            Bounds bounds
        )
        {
            this.abilityCount = abilityCount;
            this.cardsPerAbility = cardsPerAbility;
            this.perAbility = perAbility;
            this.billboard = billboard;
            InstancingEnabled = instanced;
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
                particles = new NativeArray<ParticleSystem.Particle>(count, Allocator.Persistent);
                customData = new NativeArray<Unity.Mathematics.float4>(count, Allocator.Persistent);
                systems = new ParticleSystem[perAbility ? abilityCount : 1];
                custom = new List<Vector4>[systems.Length];
                initialized = new bool[systems.Length];
                material = resources.Material("AuthoredParticle");
                for (int i = 0; i < systems.Length; i++)
                {
                    var go = new GameObject(name + (perAbility ? " / ability " + i : " / shared"));
                    go.transform.SetParent(root, false);
                    var system = go.AddComponent<ParticleSystem>();
                    var main = system.main;
                    main.playOnAwake = false;
                    main.loop = false;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.maxParticles = perAbility ? cardsPerAbility : count;
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

                    renderer.enableGPUInstancing = instanced;
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

                    if (directJobs)
                    {
                        bridge = go.AddComponent<RecipeParticleBridge>();
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

            if (bridge != null)
            {
                bridge.Recipe = recipe;
                bridge.Ready = true;
                if (!first)
                {
                    return;
                }
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
                bool active = !perAbility || recipe.Lines[i].Start.w > 0;
                if (system.gameObject.activeSelf != active)
                {
                    system.gameObject.SetActive(active);
                }

                if (!active)
                {
                    continue;
                }

                if (!initialized[i])
                {
                    system.Play();
                    if (bridge == null)
                    {
                        system.Pause();
                    }

                    initialized[i] = true;
                }

                int offset = perAbility ? i * cardsPerAbility : 0;
                int length = perAbility ? cardsPerAbility : particles.Length;
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
            if (bridge != null)
            {
                bridge.Ready = false;
            }

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
}
