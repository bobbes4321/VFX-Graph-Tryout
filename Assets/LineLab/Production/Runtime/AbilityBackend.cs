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
using UnityEngine.VFX;

namespace LineLab.Production
{
    [BurstCompile]
    // Prepare Shuriken input for SetParticles. This is the copying particle path.
    public struct DecorParticles : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<LineData> Lines;
        public NativeArray<ParticleSystem.Particle> Particles;
        public int Layer;
        public float Clock;
        public float3 Camera;
        public float3 CameraRight;
        public float3 CameraUp;
        public bool Billboard;

        public void Execute(int i)
        {
            int n = AbilityGeometry.LayerCount(Layer);
            AbilityGeometry.Decor(Lines[i / n], Layer, i % n, Clock, Camera, out var d);
            var rotation = new quaternion(
                new float3x3(d.X, d.Y, math.normalizesafe(math.cross(d.X, d.Y)))
            );
            float3 angles = math.degrees(math.EulerZXY(rotation));
            if (Billboard)
            {
                angles = new float3(
                    0,
                    0,
                    -math.degrees(math.atan2(math.dot(d.X, CameraUp), math.dot(d.X, CameraRight)))
                );
            }

            Particles[i] = new ParticleSystem.Particle
            {
                position = d.Position,
                rotation3D = angles,
                startSize3D = new Vector3(d.Size.x, d.Size.y, 1),
                startColor = new Color(d.Color.x, d.Color.y, d.Color.z, d.Alpha),
                startLifetime = 1000000,
                remainingLifetime = 1000000,
                randomSeed = (uint)i + 1,
            };
        }
    }

    [BurstCompile]
    // Write Shuriken's native arrays directly from its required scheduling callback.
    public struct DecorParticleUpdate : IJobParticleSystemParallelFor
    {
        [ReadOnly]
        public NativeArray<LineData> Lines;
        public int Layer;
        public float Clock;
        public float3 Camera;

        public void Execute(ParticleSystemJobData p, int i)
        {
            int n = AbilityGeometry.LayerCount(Layer);
            AbilityGeometry.Decor(Lines[i / n], Layer, i % n, Clock, Camera, out var d);
            var positions = p.positions;
            var sizes = p.sizes;
            var rotations = p.rotations;
            var colors = p.startColors;
            var ages = p.aliveTimePercent;
            positions[i] = d.Position;
            sizes[i] = new float3(d.Size.x, d.Size.y, 1);
            rotations[i] = math.EulerZXY(
                new quaternion(new float3x3(d.X, d.Y, math.normalizesafe(math.cross(d.X, d.Y))))
            );
            colors[i] = new Color(d.Color.x, d.Color.y, d.Color.z, d.Alpha);
            ages[i] = 0;
        }
    }

    // Full-stack boundary. VFX owns all five systems in one graph. Mesh/line paths
    // explicitly use a procedural adornment companion; Shuriken owns four PS layers.
    public sealed class AbilityBackend : IDisposable
    {
        static readonly int LinesId = Shader.PropertyToID("Lines");
        static readonly int ClockId = Shader.PropertyToID("Clock");
        static readonly int CameraId = Shader.PropertyToID("CameraPosition");
        static readonly int StartId = Shader.PropertyToID("Start");
        static readonly int EndId = Shader.PropertyToID("End");
        static readonly int TintId = Shader.PropertyToID("Tint");
        static readonly int ShapeId = Shader.PropertyToID("Shape");
        static readonly int SLines = Shader.PropertyToID("_Lines");
        static readonly int SClock = Shader.PropertyToID("_Clock");
        static readonly int SCamera = Shader.PropertyToID("_Camera");
        readonly Technique technique;
        readonly int count;
        readonly Camera camera;
        readonly CoreBackend core;
        readonly VisualEffect[] vfx;
        readonly GraphicsBuffer buffer;
        readonly Material[] materials;
        readonly ParticleSystem[][] systems;
        readonly NativeArray<ParticleSystem.Particle>[] particles;
        readonly DecorParticleBridge[] bridges;
        readonly Bounds bounds = new Bounds(Vector3.zero, new Vector3(150, 80, 150));
        public readonly GameObject Root;
        public readonly bool Full;
        public long UploadBytes { get; private set; }

        public double CallbackMs
        {
            get
            {
                double v = core?.ParticleCallbackMs ?? 0;
                if (bridges != null)
                {
                    foreach (var b in bridges)
                    {
                        v += b.LastCallbackMs;
                    }
                }

                return v;
            }
        }

        public bool InstancingActive =>
            technique == Technique.VfxPerLineBatched
            || (
                technique == Technique.ParticleMeshInstanced
                && core.InstancingActive
                && (
                    !Full
                    || systems[0][0].GetComponent<ParticleSystemRenderer>().enableGPUInstancing
                )
            );
        public int ObjectCount => Root.GetComponentsInChildren<Transform>(true).Length;
        public string Composition =>
            technique == Technique.Baseline ? "No effects / common environment and native jobs"
            : !Full ? "Three core ribbons / secondary layers omitted"
            : IsVfx ? "One graph / core + four GPU particle systems"
            : IsParticle ? "Shuriken / core + four native particle layers"
            : "Ribbon renderer + four procedural GPU particle layers";

        bool IsVfx =>
            technique == Technique.VfxBuffer
            || technique == Technique.VfxPerLine
            || technique == Technique.VfxPerLineBatched;

        bool IsParticle =>
            technique >= Technique.ParticleBillboardShared && technique <= Technique.ParticleJobs;

        public AbilityBackend(Technique t, int count, Camera camera, bool full)
        {
            technique = t;
            this.count = count;
            this.camera = camera;
            Full = full;
            Root = new GameObject("Complete ability / " + TechniqueInfo.Names[(int)t]);
            if (t == Technique.Baseline)
            {
                return;
            }

            bool shared = t == Technique.VfxBuffer;
            if (IsVfx)
            {
                if (shared)
                {
                    buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count * 4, 16);
                }

                int capacity = shared ? math.ceilpow2(count) : 1;
                string name =
                    "Production/Ability"
                    + (
                        shared ? "Shared"
                        : t == Technique.VfxPerLineBatched ? "Batched"
                        : "Single"
                    )
                    + (full ? "Full_" : "Beam_")
                    + capacity;
                var asset = Resources.Load<VisualEffectAsset>(name);
                if (asset == null)
                {
                    throw new InvalidOperationException("Missing complete graph " + name);
                }

                vfx = new VisualEffect[shared ? 1 : count];
                for (int i = 0; i < vfx.Length; i++)
                {
                    var go = new GameObject(
                        shared ? "Shared five-system graph" : "Complete pooled ability " + i
                    );
                    go.transform.SetParent(Root.transform);
                    var e = go.AddComponent<VisualEffect>();
                    e.visualEffectAsset = asset;
                    e.resetSeedOnPlay = false;
                    e.startSeed = 41;
                    e.SetFloat("SpawnCore", (shared ? count : 1) * 96);
                    if (full)
                    {
                        for (int layer = 0; layer < 4; layer++)
                        {
                            e.SetFloat(
                                "SpawnLayer" + layer,
                                (shared ? count : 1) * AbilityGeometry.LayerCount(layer)
                            );
                        }
                    }

                    if (shared)
                    {
                        e.SetGraphicsBuffer(LinesId, buffer);
                    }

                    vfx[i] = e;
                }

                return;
            }

            core = new CoreBackend(t, count * 3, 32, camera);
            core.Root.transform.SetParent(Root.transform);
            if (!full)
            {
                return;
            }

            materials = new Material[4];
            if (!IsParticle)
            {
                buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, count * 4, 16);
                for (int layer = 0; layer < 4; layer++)
                {
                    materials[layer] = UnityEngine.Object.Instantiate(
                        Resources.Load<Material>("Production/DecorProcedural" + layer)
                    );
                    materials[layer].SetBuffer(SLines, buffer);
                    materials[layer].SetInt("_Layer", layer);
                }
            }
            else
            {
                bool per = t == Technique.ParticleBillboardPerLine;
                bool instanced = t == Technique.ParticleMeshInstanced;
                bool jobs = t == Technique.ParticleJobs;
                systems = new ParticleSystem[4][];
                particles = new NativeArray<ParticleSystem.Particle>[4];
                if (jobs)
                {
                    bridges = new DecorParticleBridge[4];
                }

                for (int layer = 0; layer < 4; layer++)
                {
                    materials[layer] = UnityEngine.Object.Instantiate(
                        Resources.Load<Material>(
                            "Production/" + (instanced ? "DecorInstanced" : "Decor") + layer
                        )
                    );
                    int n = AbilityGeometry.LayerCount(layer);
                    particles[layer] = new NativeArray<ParticleSystem.Particle>(
                        count * n,
                        Allocator.Persistent
                    );
                    systems[layer] = new ParticleSystem[per ? count : 1];
                    for (int i = 0; i < systems[layer].Length; i++)
                    {
                        var go = new GameObject("Layer " + layer + " / pool " + i);
                        go.transform.SetParent(Root.transform);
                        var ps = go.AddComponent<ParticleSystem>();
                        var m = ps.main;
                        m.playOnAwake = false;
                        m.loop = false;
                        m.simulationSpace = ParticleSystemSimulationSpace.World;
                        m.maxParticles = (per ? 1 : count) * n;
                        m.startSpeed = 0;
                        m.startLifetime = 1000000;
                        m.startSize3D = true;
                        m.startRotation3D = true;
                        m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                        var emit = ps.emission;
                        emit.enabled = false;
                        var shape = ps.shape;
                        shape.enabled = false;
                        var r = go.GetComponent<ParticleSystemRenderer>();
                        r.sharedMaterial = materials[layer];
                        r.applyActiveColorSpace = false;
                        bool billboard =
                            layer != 3 && (t == Technique.ParticleBillboardShared || per);
                        r.renderMode = billboard
                            ? ParticleSystemRenderMode.Billboard
                            : ParticleSystemRenderMode.Mesh;
                        r.alignment = billboard
                            ? ParticleSystemRenderSpace.View
                            : ParticleSystemRenderSpace.World;
                        if (!billboard)
                        {
                            r.mesh = Resources.Load<Mesh>("SegmentQuad");
                        }

                        r.enableGPUInstancing = instanced;
                        r.sortMode = ParticleSystemSortMode.None;
                        r.localBounds = bounds;
                        r.shadowCastingMode = ShadowCastingMode.Off;
                        r.receiveShadows = false;
                        r.SetActiveVertexStreams(
                            new List<ParticleSystemVertexStream>
                            {
                                ParticleSystemVertexStream.Position,
                                ParticleSystemVertexStream.Color,
                                ParticleSystemVertexStream.UV,
                            }
                        );
                        systems[layer][i] = ps;
                        if (jobs)
                        {
                            bridges[layer] = go.AddComponent<DecorParticleBridge>();
                        }
                    }
                }
            }
        }

        public void Present(AbilityWorld world, float clock, bool first)
        {
            UploadBytes = 0;
            if (technique == Technique.Baseline)
            {
                return;
            }

            float3 cam = camera.transform.position;
            if (buffer != null)
            {
                buffer.SetData(world.Lines.Reinterpret<float4>(64));
                UploadBytes = count * 64L;
            }

            if (vfx != null)
            {
                for (int i = 0; i < vfx.Length; i++)
                {
                    var e = vfx[i];
                    if (technique != Technique.VfxBuffer)
                    {
                        var l = world.Lines[i];
                        bool active = l.Start.w > 0;
                        if (e.gameObject.activeSelf != active)
                        {
                            e.gameObject.SetActive(active);
                        }

                        if (!active)
                        {
                            continue;
                        }

                        e.SetVector4(StartId, l.Start);
                        e.SetVector4(EndId, l.End);
                        e.SetVector4(TintId, l.Color);
                        e.SetVector4(ShapeId, l.Shape);
                    }

                    e.SetFloat(ClockId, clock);
                    e.SetVector3(CameraId, cam);
                    if (first)
                    {
                        e.Reinit();
                    }
                }

                return;
            }

            core.Present(world.Strands, clock, first);
            UploadBytes += core.UploadBytes;
            if (!Full)
            {
                return;
            }

            for (int layer = 0; layer < 4; layer++)
            {
                if (systems == null)
                {
                    var m = materials[layer];
                    m.SetFloat(SClock, clock);
                    m.SetVector(SCamera, (Vector3)cam);
                    Graphics.DrawProcedural(
                        m,
                        bounds,
                        MeshTopology.Triangles,
                        6,
                        count * AbilityGeometry.LayerCount(layer),
                        camera,
                        null,
                        ShadowCastingMode.Off,
                        false,
                        0
                    );
                }
                else
                {
                    int n = AbilityGeometry.LayerCount(layer);
                    bool jobs = bridges != null;
                    bool billboard =
                        layer != 3
                        && (
                            technique == Technique.ParticleBillboardShared
                            || technique == Technique.ParticleBillboardPerLine
                        );
                    if (jobs)
                    {
                        var b = bridges[layer];
                        b.Lines = world.Lines;
                        b.Layer = layer;
                        b.Clock = clock;
                        b.CameraPosition = cam;
                        b.Ready = true;
                        if (!first)
                        {
                            continue;
                        }
                    }

                    new DecorParticles
                    {
                        Lines = world.Lines,
                        Particles = particles[layer],
                        Layer = layer,
                        Clock = clock,
                        Camera = cam,
                        Billboard = billboard,
                        CameraRight = camera.transform.right,
                        CameraUp = camera.transform.up,
                    }
                        .Schedule(count * n, 64)
                        .Complete();
                    for (int i = 0; i < systems[layer].Length; i++)
                    {
                        var ps = systems[layer][i];
                        if (first)
                        {
                            ps.Play();
                            if (!jobs)
                            {
                                ps.Pause();
                            }
                        }

                        if (systems[layer].Length > 1)
                        {
                            ps.SetParticles(particles[layer].GetSubArray(i * n, n), n);
                        }
                        else
                        {
                            ps.SetParticles(particles[layer], count * n);
                        }
                    }

                    UploadBytes +=
                        particles[layer].Length * (long)Marshal.SizeOf<ParticleSystem.Particle>();
                }
            }
        }

        public int LiveParticles()
        {
            int n = 0;
            foreach (var e in Root.GetComponentsInChildren<VisualEffect>())
            {
                n += (int)e.aliveParticleCount;
            }

            foreach (var ps in Root.GetComponentsInChildren<ParticleSystem>())
            {
                n += ps.particleCount;
            }

            return n;
        }

        public void Dispose()
        {
            if (bridges != null)
            {
                foreach (var b in bridges)
                {
                    b.Ready = false;
                }
            }

            core?.Dispose();
            buffer?.Dispose();
            if (particles != null)
            {
                foreach (var p in particles)
                {
                    if (p.IsCreated)
                    {
                        p.Dispose();
                    }
                }
            }

            if (materials != null)
            {
                foreach (var m in materials)
                {
                    UnityEngine.Object.Destroy(m);
                }
            }

            UnityEngine.Object.Destroy(Root);
        }
    }
}
