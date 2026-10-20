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

namespace LineLab
{
    public enum Technique
    {
        Baseline,
        VfxBuffer,
        VfxPerLine,
        LineRenderer,
        BurstMesh,
        Procedural,
        ParticleBillboardShared,
        ParticleBillboardPerLine,
        ParticleMeshShared,
        ParticleMeshInstanced,
        ParticleJobs,
        VfxPerLineBatched,
    }

    public static class TechniqueInfo
    {
        public static readonly string[] Names =
        {
            "No lines / baseline",
            "VFX / shared buffer",
            "VFX / pooled per line",
            "Unity LineRenderer / pooled",
            "Burst / combined ribbon mesh",
            "GPU / procedural ribbons",
            "Shuriken / shared billboards",
            "Shuriken / per-line billboards",
            "Shuriken / shared mesh",
            "Shuriken / instanced mesh",
            "Shuriken / Burst particle jobs",
            "VFX / per line + batching",
        };
    }

    [BurstCompile]
    public struct BuildLineRendererJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<LineData> Lines;

        [NativeDisableParallelForRestriction]
        public NativeArray<Vector3> Positions;

        [NativeDisableParallelForRestriction]
        public NativeArray<float4> Colors;
        public int Segments;
        public float Clock;

        public void Execute(int index)
        {
            var line = Lines[index];
            if (line.Start.w == 0)
            {
                return;
            }

            int offset = index * (Segments + 1);
            for (int j = 0; j <= Segments; j++)
            {
                Positions[offset + j] = LineGeometry.Point(line, (float)j / Segments, Clock);
            }

            for (int j = 0; j < 8; j++)
            {
                Colors[index * 8 + j] = LineGeometry.Tint(line, j / 7f, Clock);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RibbonVertex
    {
        public float3 Position;
        public float4 Color;
        public float2 Uv;
    }

    [BurstCompile]
    public struct BuildRibbonJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<LineData> Lines;

        [Unity.Collections.NativeDisableParallelForRestriction]
        public NativeArray<RibbonVertex> Vertices;
        public int Segments;
        public float Clock;
        public float3 Camera;

        public void Execute(int index)
        {
            int lineIndex = index / Segments;
            int segment = index % Segments;
            var line = Lines[lineIndex];
            LineGeometry.Segment(
                line,
                segment,
                Segments,
                Clock,
                Camera,
                out var mid,
                out var tangent,
                out var side,
                out float length,
                out var color
            );
            int v = index * 4;
            for (int k = 0; k < 4; k++)
            {
                float u = k == 1 || k == 2 ? 1 : 0;
                float y = k >= 2 ? 1 : 0;
                Vertices[v + k] = new RibbonVertex
                {
                    Position =
                        mid + tangent * ((u - .5f) * length) + side * ((y - .5f) * line.End.w),
                    Uv = new float2(u, y),
                    Color = color,
                };
            }
        }
    }

    [BurstCompile]
    public struct BuildParticlesJob : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<LineData> Lines;
        public NativeArray<ParticleSystem.Particle> Particles;
        public int Segments;
        public float Clock;
        public float3 Camera;
        public bool Billboard;
        public float3 CameraRight;
        public float3 CameraUp;

        public void Execute(int index)
        {
            var line = Lines[index / Segments];
            LineGeometry.Segment(
                line,
                index % Segments,
                Segments,
                Clock,
                Camera,
                out var mid,
                out var tangent,
                out var side,
                out float length,
                out var color
            );
            var rotation = new quaternion(
                new float3x3(tangent, side, math.normalizesafe(math.cross(tangent, side)))
            );
            float3 angles = math.degrees(math.EulerZXY(rotation));
            if (Billboard)
            {
                angles = new float3(
                    0,
                    0,
                    -math.degrees(
                        math.atan2(math.dot(tangent, CameraUp), math.dot(tangent, CameraRight))
                    )
                );
            }

            Particles[index] = new ParticleSystem.Particle
            {
                position = mid,
                rotation3D = angles,
                startSize3D = new Vector3(length, line.End.w, 1),
                startColor = new Color(color.x, color.y, color.z, color.w),
                startLifetime = 1000000,
                remainingLifetime = 1000000,
                randomSeed = (uint)index + 1,
            };
        }
    }

    [BurstCompile]
    public struct UpdateParticlesJob : IJobParticleSystemParallelFor
    {
        [ReadOnly]
        public NativeArray<LineData> Lines;
        public int Segments;
        public float Clock;
        public float3 Camera;

        public void Execute(ParticleSystemJobData particles, int index)
        {
            var line = Lines[index / Segments];
            LineGeometry.Segment(
                line,
                index % Segments,
                Segments,
                Clock,
                Camera,
                out var mid,
                out var tangent,
                out var side,
                out float length,
                out var color
            );
            var positions = particles.positions;
            var rotations = particles.rotations;
            var sizes = particles.sizes;
            var colors = particles.startColors;
            var ages = particles.aliveTimePercent;
            positions[index] = mid;
            rotations[index] = math.EulerZXY(
                new quaternion(
                    new float3x3(tangent, side, math.normalizesafe(math.cross(tangent, side)))
                )
            );
            sizes[index] = new float3(length, line.End.w, 1);
            colors[index] = new Color(color.x, color.y, color.z, color.w);
            ages[index] = 0;
        }
    }

    // Unity calls stay here. All gameplay-facing calls use LineWriter, regardless of backend.
    public sealed class LineBackend : IDisposable
    {
        static readonly int VfxSegments = Shader.PropertyToID("Segments");
        static readonly int VfxSpawnCount = Shader.PropertyToID("SpawnCount");
        static readonly int VfxLines = Shader.PropertyToID("Lines");
        static readonly int VfxClock = Shader.PropertyToID("Clock");
        static readonly int VfxCamera = Shader.PropertyToID("CameraPosition");
        static readonly int VfxStart = Shader.PropertyToID("Start");
        static readonly int VfxEnd = Shader.PropertyToID("End");
        static readonly int VfxTint = Shader.PropertyToID("Tint");
        static readonly int VfxShape = Shader.PropertyToID("Shape");
        static readonly int ShaderLines = Shader.PropertyToID("_Lines");
        static readonly int ShaderSegments = Shader.PropertyToID("_Segments");
        static readonly int ShaderClock = Shader.PropertyToID("_Clock");
        static readonly int ShaderCamera = Shader.PropertyToID("_Camera");
        public readonly Technique Technique;
        public readonly int Count;
        public readonly int Segments;
        public readonly GameObject Root;
        readonly Camera camera;
        readonly Material ribbon;
        readonly Material procedural;
        readonly Material particleMaterial;
        readonly GraphicsBuffer buffer;
        readonly VisualEffect[] effects;
        readonly LineRenderer[] renderers;
        readonly ParticleSystem[] systems;
        readonly Mesh mesh;
        readonly Mesh quad;
        readonly NativeArray<RibbonVertex> vertices;
        readonly NativeArray<ParticleSystem.Particle> particles;
        readonly NativeArray<Vector3> linePositions;
        readonly NativeArray<float4> lineColors;
        readonly Gradient gradient;
        readonly GradientColorKey[] colorKeys;
        readonly GradientAlphaKey[] alphaKeys;
        readonly ParticleJobBridge particleJobBridge;
        readonly Bounds bounds = new Bounds(Vector3.zero, new Vector3(200, 100, 200));
        int presentedFrames;
        public long UploadBytes { get; private set; }
        public int ObjectCount => Root.transform.childCount + 1;
        public int ParticleCount => Count * Segments;
        public double ParticleCallbackMs =>
            particleJobBridge != null ? particleJobBridge.LastCallbackMs : 0;
        public bool InstancingActive =>
            Technique == Technique.VfxPerLineBatched
            || (
                systems != null
                && systems.Length > 0
                && systems[0].GetComponent<ParticleSystemRenderer>().enableGPUInstancing
            );

        public LineBackend(
            Technique technique,
            int count,
            int segments,
            Camera camera,
            bool showcase = false
        )
        {
            Technique = technique;
            Count = count;
            Segments = segments;
            this.camera = camera;
            Root = new GameObject(TechniqueInfo.Names[(int)technique]);
            ribbon = UnityEngine.Object.Instantiate(Resources.Load<Material>("RibbonMaterial"));
            quad = Resources.Load<Mesh>("SegmentQuad");
            if (technique == Technique.VfxBuffer || technique == Technique.Procedural)
            {
                buffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    math.max(4, count * 4),
                    16
                );
            }

            if (technique == Technique.Procedural)
            {
                procedural = UnityEngine.Object.Instantiate(
                    Resources.Load<Material>("ProceduralMaterial")
                );
                procedural.SetBuffer(ShaderLines, buffer);
                procedural.SetInt(ShaderSegments, segments);
            }
            else if (
                technique == Technique.VfxBuffer
                || technique == Technique.VfxPerLine
                || technique == Technique.VfxPerLineBatched
            )
            {
                bool shared = technique == Technique.VfxBuffer;
                effects = new VisualEffect[
                    shared
                        ? showcase
                            ? 2
                            : 1
                        : count
                ];
                int capacity = math.max(
                    shared ? 16 : 8,
                    math.ceilpow2(shared ? count * segments : segments)
                );
                string graphName =
                    shared ? "LinesBuffer"
                    : technique == Technique.VfxPerLineBatched ? "LineSingleBatched"
                    : "LineSingle";
                var graph = Resources.Load<VisualEffectAsset>(graphName + "_" + capacity);
                if (graph == null)
                {
                    throw new InvalidOperationException(
                        "Missing VFX capacity variant " + graphName + "_" + capacity
                    );
                }

                for (int i = 0; i < effects.Length; i++)
                {
                    var go = new GameObject(shared ? "Shared GPU line system" : "Pooled VFX " + i);
                    go.transform.SetParent(Root.transform);
                    var vfx = go.AddComponent<VisualEffect>();
                    vfx.visualEffectAsset =
                        shared && i == 1
                            ? Resources.Load<VisualEffectAsset>("ShowcaseMotes")
                            : graph;
                    vfx.resetSeedOnPlay = false;
                    vfx.startSeed = 41;
                    int effectSegments = shared && i == 1 ? 48 : segments;
                    vfx.SetUInt(VfxSegments, (uint)effectSegments);
                    vfx.SetFloat(VfxSpawnCount, shared ? count * effectSegments : effectSegments);
                    if (shared)
                    {
                        vfx.SetGraphicsBuffer(VfxLines, buffer);
                    }

                    effects[i] = vfx;
                }
            }
            else if (technique == Technique.LineRenderer)
            {
                renderers = new LineRenderer[count];
                linePositions = new NativeArray<Vector3>(
                    count * (segments + 1),
                    Allocator.Persistent
                );
                lineColors = new NativeArray<float4>(count * 8, Allocator.Persistent);
                gradient = new Gradient();
                colorKeys = new GradientColorKey[8];
                alphaKeys = new GradientAlphaKey[2];
                for (int i = 0; i < count; i++)
                {
                    var go = new GameObject("Pooled LineRenderer " + i);
                    go.transform.SetParent(Root.transform);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.sharedMaterial = ribbon;
                    lr.positionCount = segments + 1;
                    lr.useWorldSpace = true;
                    lr.alignment = LineAlignment.View;
                    lr.textureMode = LineTextureMode.Stretch;
                    lr.numCapVertices = 0;
                    lr.numCornerVertices = 0;
                    lr.shadowCastingMode = ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    renderers[i] = lr;
                }
            }
            else if (technique == Technique.BurstMesh)
            {
                vertices = new NativeArray<RibbonVertex>(
                    count * segments * 4,
                    Allocator.Persistent
                );
                mesh = new Mesh
                {
                    name = "Burst combined ribbons",
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
                var indices = new int[count * segments * 6];
                for (int i = 0; i < count * segments; i++)
                {
                    int v = i * 4;
                    int n = i * 6;
                    indices[n] = v;
                    indices[n + 1] = v + 1;
                    indices[n + 2] = v + 2;
                    indices[n + 3] = v;
                    indices[n + 4] = v + 2;
                    indices[n + 5] = v + 3;
                }

                mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
                mesh.bounds = bounds;
                Root.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = Root.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = ribbon;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
            }
            else if (technique >= Technique.ParticleBillboardShared)
            {
                bool perLine = technique == Technique.ParticleBillboardPerLine;
                bool billboard = technique == Technique.ParticleBillboardShared || perLine;
                bool instanced = technique == Technique.ParticleMeshInstanced;
                particleMaterial = UnityEngine.Object.Instantiate(
                    Resources.Load<Material>(
                        instanced ? "ParticleInstancedMaterial" : "RibbonMaterial"
                    )
                );
                particles = new NativeArray<ParticleSystem.Particle>(
                    count * segments,
                    Allocator.Persistent
                );
                systems = new ParticleSystem[perLine ? count : 1];
                for (int i = 0; i < systems.Length; i++)
                {
                    var go = new GameObject(perLine ? "Pooled Shuriken " + i : "Shared Shuriken");
                    go.transform.SetParent(Root.transform);
                    var ps = go.AddComponent<ParticleSystem>();
                    var main = ps.main;
                    main.playOnAwake = false;
                    main.loop = false;
                    main.simulationSpace = ParticleSystemSimulationSpace.World;
                    main.maxParticles = perLine ? segments : count * segments;
                    main.startSpeed = 0;
                    main.startLifetime = 1000000;
                    main.startSize3D = true;
                    main.startRotation3D = true;
                    main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
                    var emission = ps.emission;
                    emission.enabled = false;
                    var shape = ps.shape;
                    shape.enabled = false;
                    var r = go.GetComponent<ParticleSystemRenderer>();
                    r.sharedMaterial = particleMaterial;
                    r.applyActiveColorSpace = false; // Native line colors are already linear.
                    r.renderMode = billboard
                        ? ParticleSystemRenderMode.Billboard
                        : ParticleSystemRenderMode.Mesh;
                    r.alignment = billboard
                        ? ParticleSystemRenderSpace.View
                        : ParticleSystemRenderSpace.World;
                    if (!billboard)
                    {
                        r.mesh = quad;
                    }

                    r.enableGPUInstancing = instanced;
                    r.sortMode = ParticleSystemSortMode.None;
                    r.shadowCastingMode = ShadowCastingMode.Off;
                    r.receiveShadows = false;
                    r.localBounds = bounds;
                    r.SetActiveVertexStreams(
                        new List<ParticleSystemVertexStream>
                        {
                            ParticleSystemVertexStream.Position,
                            ParticleSystemVertexStream.Color,
                            ParticleSystemVertexStream.UV,
                        }
                    );
                    systems[i] = ps;
                    if (technique == Technique.ParticleJobs)
                    {
                        particleJobBridge = go.AddComponent<ParticleJobBridge>();
                    }
                }
            }
        }

        public void Present(
            NativeArray<LineData> lines,
            float clock,
            bool first,
            bool dynamic = true
        )
        {
            UploadBytes = 0;
            presentedFrames++;
            float3 cam = camera.transform.position;
            if (Technique == Technique.Baseline)
            {
                return;
            }

            if (!first && !dynamic && Technique != Technique.Procedural)
            {
                // Once initialized, static VFX can keep cached GPU attributes.
                if (effects != null && presentedFrames == 5)
                {
                    foreach (var effect in effects)
                    {
                        effect.pause = true;
                    }
                }

                if (particleJobBridge != null && presentedFrames == 2)
                {
                    particleJobBridge.Ready = false;
                    systems[0].Pause();
                    particleJobBridge.LastCallbackMs = 0;
                }

                return;
            }

            if (buffer != null && (first || dynamic))
            {
                buffer.SetData(lines.Reinterpret<float4>(64), 0, 0, Count * 4);
                UploadBytes = Count * 64L;
            }

            if (Technique == Technique.Procedural)
            {
                procedural.SetFloat(ShaderClock, clock);
                procedural.SetVector(ShaderCamera, (Vector3)cam);
                Graphics.DrawProcedural(
                    procedural,
                    bounds,
                    MeshTopology.Triangles,
                    Segments * 6,
                    Count,
                    camera,
                    null,
                    ShadowCastingMode.Off,
                    false,
                    0
                );
            }
            else if (effects != null)
            {
                for (int i = 0; i < effects.Length; i++)
                {
                    var vfx = effects[i];
                    if (Technique != Technique.VfxBuffer)
                    {
                        var l = lines[i];
                        bool active = l.Start.w > 0;
                        if (vfx.gameObject.activeSelf != active)
                        {
                            vfx.gameObject.SetActive(active);
                        }

                        if (!active)
                        {
                            continue;
                        }

                        vfx.SetVector4(VfxStart, l.Start);
                        vfx.SetVector4(VfxEnd, l.End);
                        vfx.SetVector4(VfxTint, l.Color);
                        vfx.SetVector4(VfxShape, l.Shape);
                    }

                    vfx.SetFloat(VfxClock, clock);
                    vfx.SetVector3(VfxCamera, cam);
                    if (first)
                    {
                        vfx.Reinit();
                    }
                }
            }
            else if (renderers != null)
            {
                new BuildLineRendererJob
                {
                    Lines = lines,
                    Positions = linePositions,
                    Colors = lineColors,
                    Segments = Segments,
                    Clock = clock,
                }
                    .Schedule(Count, 32)
                    .Complete();
                for (int i = 0; i < Count; i++)
                {
                    var line = lines[i];
                    var lr = renderers[i];
                    lr.enabled = line.Start.w > 0;
                    if (!lr.enabled)
                    {
                        continue;
                    }

                    lr.SetPositions(linePositions.GetSubArray(i * (Segments + 1), Segments + 1));
                    lr.widthMultiplier = line.End.w;
                    for (int k = 0; k < 8; k++)
                    {
                        var c = lineColors[i * 8 + k];
                        colorKeys[k] = new GradientColorKey(new Color(c.x, c.y, c.z), k / 7f);
                    }

                    alphaKeys[0] = new GradientAlphaKey(line.Color.w, 0);
                    alphaKeys[1] = new GradientAlphaKey(line.Color.w, 1);
                    gradient.SetKeys(colorKeys, alphaKeys);
                    lr.colorGradient = gradient;
                }

                UploadBytes = Count * (Segments + 1) * 12L; // API input bytes; Unity internal transfers not exposed.
            }
            else if (mesh != null)
            {
                new BuildRibbonJob
                {
                    Lines = lines,
                    Vertices = vertices,
                    Segments = Segments,
                    Clock = clock,
                    Camera = cam,
                }
                    .Schedule(Count * Segments, 64)
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
            else if (systems != null)
            {
                if (particleJobBridge != null)
                {
                    particleJobBridge.Lines = lines;
                    particleJobBridge.Segments = Segments;
                    particleJobBridge.Clock = clock;
                    particleJobBridge.CameraPosition = cam;
                    particleJobBridge.Ready = true;
                }

                if (Technique == Technique.ParticleJobs && !first)
                {
                    return;
                }

                new BuildParticlesJob
                {
                    Lines = lines,
                    Particles = particles,
                    Segments = Segments,
                    Clock = clock,
                    Camera = cam,
                    Billboard =
                        Technique == Technique.ParticleBillboardShared
                        || Technique == Technique.ParticleBillboardPerLine,
                    CameraRight = camera.transform.right,
                    CameraUp = camera.transform.up,
                }
                    .Schedule(Count * Segments, 64)
                    .Complete();
                for (int i = 0; i < systems.Length; i++)
                {
                    if (systems.Length > 1)
                    {
                        bool active = lines[i].Start.w > 0;
                        if (systems[i].gameObject.activeSelf != active)
                        {
                            systems[i].gameObject.SetActive(active);
                        }

                        if (!active)
                        {
                            continue;
                        }
                    }

                    if (first)
                    {
                        systems[i].Play();
                        if (Technique != Technique.ParticleJobs)
                        {
                            systems[i].Pause();
                        }
                    }

                    if (systems.Length > 1)
                    {
                        systems[i]
                            .SetParticles(particles.GetSubArray(i * Segments, Segments), Segments);
                    }
                    else
                    {
                        systems[i].SetParticles(particles, Count * Segments);
                    }
                }

                UploadBytes = particles.Length * (long)Marshal.SizeOf<ParticleSystem.Particle>();
            }
        }

        public void Dispose()
        {
            if (particleJobBridge != null)
            {
                particleJobBridge.Ready = false;
            }

            if (vertices.IsCreated)
            {
                vertices.Dispose();
            }

            if (particles.IsCreated)
            {
                particles.Dispose();
            }

            if (linePositions.IsCreated)
            {
                linePositions.Dispose();
            }

            if (lineColors.IsCreated)
            {
                lineColors.Dispose();
            }

            buffer?.Dispose();
            if (mesh != null)
            {
                UnityEngine.Object.Destroy(mesh);
            }

            UnityEngine.Object.Destroy(Root);
            UnityEngine.Object.Destroy(ribbon);
            if (procedural != null)
            {
                UnityEngine.Object.Destroy(procedural);
            }

            if (particleMaterial != null)
            {
                UnityEngine.Object.Destroy(particleMaterial);
            }
        }
    }
}
