using System;
using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace LineLab
{
    // Four float4s, identical to the HLSL buffer layout. No UnityEngine.Object references.
    [StructLayout(LayoutKind.Sequential)]
    public struct LineData
    {
        public float4 Start; // xyz position, w active
        public float4 End; // xyz position, w width
        public float4 Color;
        public float4 Shape; // x style, y phase; Production uses z/w for Begin/Release times
    }

    public enum LineOperation : byte
    {
        Begin,
        Update,
        Stop,
    }

    public struct LineHandle
    {
        public int Slot;
        public uint Generation;

        public LineHandle(int slot, uint generation)
        {
            Slot = slot;
            Generation = generation;
        }
    }

    public struct LineCommand
    {
        public LineOperation Operation;
        public LineHandle Handle;
        public LineData Data;
    }

    // Copy this value into any Burst job. The caller owns slots and monotonically
    // increasing generations. Give each entity/ability a unique slot.
    public struct LineWriter
    {
        public NativeQueue<LineCommand>.ParallelWriter Queue;

        public void Begin(
            LineHandle handle,
            float3 from,
            float3 to,
            float4 color,
            float width,
            int style = 0
        )
        {
            Queue.Enqueue(
                new LineCommand
                {
                    Operation = LineOperation.Begin,
                    Handle = handle,
                    Data = new LineData
                    {
                        Start = new float4(from, 1),
                        End = new float4(to, width),
                        Color = color,
                        Shape = new float4(style, handle.Slot * .173f, 0, 0),
                    },
                }
            );
        }

        public void Update(
            LineHandle handle,
            float3 from,
            float3 to,
            float4 color,
            float width,
            int style = 0
        )
        {
            Queue.Enqueue(
                new LineCommand
                {
                    Operation = LineOperation.Update,
                    Handle = handle,
                    Data = new LineData
                    {
                        Start = new float4(from, 1),
                        End = new float4(to, width),
                        Color = color,
                        Shape = new float4(style, handle.Slot * .173f, 0, 0),
                    },
                }
            );
        }

        public void Stop(LineHandle handle)
        {
            Queue.Enqueue(new LineCommand { Operation = LineOperation.Stop, Handle = handle });
        }
    }

    public sealed class LineWorld : IDisposable
    {
        public readonly NativeArray<LineData> Lines;
        public readonly NativeArray<uint> Generations;
        public readonly NativeArray<byte> Initialized;
        public NativeQueue<LineCommand> Commands;
        public LineWriter Writer => new LineWriter { Queue = Commands.AsParallelWriter() };

        public LineWorld(int capacity)
        {
            Lines = new NativeArray<LineData>(capacity, Allocator.Persistent);
            Generations = new NativeArray<uint>(capacity, Allocator.Persistent);
            Initialized = new NativeArray<byte>(capacity, Allocator.Persistent);
            Commands = new NativeQueue<LineCommand>(Allocator.Persistent);
        }

        public JobHandle Consume(JobHandle producers = default)
        {
            return new ConsumeCommands
            {
                Commands = Commands,
                Lines = Lines,
                Generations = Generations,
                Initialized = Initialized,
            }.Schedule(producers);
        }

        public void Dispose()
        {
            Lines.Dispose();
            Generations.Dispose();
            Initialized.Dispose();
            Commands.Dispose();
        }
    }

    [BurstCompile]
    public struct ConsumeCommands : IJob
    {
        public NativeQueue<LineCommand> Commands;
        public NativeArray<LineData> Lines;
        public NativeArray<uint> Generations;
        public NativeArray<byte> Initialized;

        public void Execute()
        {
            while (Commands.TryDequeue(out var command))
            {
                int i = command.Handle.Slot;
                if ((uint)i >= (uint)Lines.Length)
                {
                    continue;
                }

                uint generation = command.Handle.Generation;
                if (command.Operation == LineOperation.Begin)
                {
                    if (Initialized[i] != 0 && generation <= Generations[i])
                    {
                        continue;
                    }

                    Generations[i] = generation;
                    Initialized[i] = 1;
                    Lines[i] = command.Data;
                }
                else if (Initialized[i] != 0 && generation == Generations[i])
                {
                    if (command.Operation == LineOperation.Stop)
                    {
                        var line = Lines[i];
                        line.Start.w = 0;
                        Lines[i] = line;
                    }
                    else if (Lines[i].Start.w > 0)
                    {
                        Lines[i] = command.Data;
                    }
                }
            }
        }
    }

    public static class LineGeometry
    {
        public static float3 Point(LineData line, float t, float clock)
        {
            float3 p = math.lerp(line.Start.xyz, line.End.xyz, t);
            float envelope = math.sin(t * math.PI);
            if (line.Shape.x < .5f)
            {
                p.y += envelope * (.45f + .10f * math.sin(t * 16 - clock * 3 + line.Shape.y));
            }
            else if (line.Shape.x < 1.5f)
            {
                p.y +=
                    envelope
                    * .65f
                    * math.sin(t * 43 + math.floor(clock * 12) * 2.3f + line.Shape.y);
                p.z += envelope * .25f * math.cos(t * 37 - clock * 9 + line.Shape.y);
            }
            else if (line.Shape.x < 2.5f)
            {
                p.y += envelope * .16f * math.sin(t * 23 - clock * 8);
            }

            return p;
        }

        public static float4 Tint(LineData line, float t, float clock)
        {
            float intensity =
                .65f + .35f * math.pow(.5f + .5f * math.cos(t * 19 - clock * 5 + line.Shape.y), 3);
            if (line.Shape.x > 2.5f)
            {
                intensity *= math.frac(t * 9 - clock * .8f) < .65f ? 1 : .03f;
            }

            return new float4(line.Color.xyz * intensity, line.Color.w * line.Start.w);
        }

        public static void Segment(
            LineData line,
            int segment,
            int segments,
            float clock,
            float3 camera,
            out float3 midpoint,
            out float3 tangent,
            out float3 side,
            out float length,
            out float4 color
        )
        {
            float3 a = Point(line, (float)segment / segments, clock);
            float3 b = Point(line, (float)(segment + 1) / segments, clock);
            tangent = math.normalizesafe(b - a, new float3(1, 0, 0));
            midpoint = (a + b) * .5f;
            side = math.normalizesafe(
                math.cross(math.normalizesafe(camera - midpoint), tangent),
                new float3(0, 1, 0)
            );
            length = math.distance(a, b) * 1.035f;
            color = Tint(line, (segment + .5f) / segments, clock);
        }
    }

    public enum Workload
    {
        Static,
        Moving,
        Churn,
        Overdraw,
    }

    [BurstCompile]
    public struct DemoCommandsJob : IJobParallelFor
    {
        public LineWriter Writer;
        public float Clock;
        public int Frame;
        public bool First;
        public bool Gallery;
        public Workload Workload;
        public int Count;

        public void Execute(int i)
        {
            bool moving = Workload != Workload.Static;
            float clock = moving ? Clock : 0;
            int columns = Gallery ? 4 : math.max(1, (int)math.ceil(math.sqrt(Count * 1.7f)));
            int rows = (Count + columns - 1) / columns;
            float spacingX = Gallery ? 13f : 46f / columns;
            float spacingZ = Gallery ? 13f : 28f / math.max(1, rows);
            float3 center = new float3(
                (i % columns - (columns - 1) * .5f) * spacingX,
                1.5f,
                (i / columns - (rows - 1) * .5f) * spacingZ
            );
            float length = 7.8f; // Keep projected work per line comparable as the count grows.
            if (Workload == Workload.Overdraw)
            {
                center *= .13f;
                length = 20;
            }

            float3 a = center + new float3(-length * .5f, 0, math.sin(clock + i * .6f) * .4f);
            float3 b =
                center
                + new float3(
                    length * .5f,
                    math.sin(clock * .7f + i) * .25f,
                    math.cos(clock + i) * .6f
                );
            int style = i % 4; // Balanced visual workload: heal, arc, plasma, and order.
            float4 tint =
                style == 0 ? new float4(.04f, 1.0f, .57f, 1)
                : style == 1 ? new float4(.10f, .33f, 1, 1)
                : style == 2 ? new float4(1, .16f, .04f, 1)
                : new float4(.67f, .17f, 1.0f, 1);
            float width =
                Gallery ? .48f
                : Workload == Workload.Overdraw ? .7f
                : .16f;
            // Every 30 frames, stop half the pool for 15 frames. Re-begin uses a new generation.
            uint generation = Workload == Workload.Churn ? (uint)(Frame / 30 + 1) : 1u;
            var handle = new LineHandle(i, generation);
            if (First || (Workload == Workload.Churn && Frame % 30 == 0))
            {
                Writer.Begin(handle, a, b, tint, width, style);
            }
            else if (Workload == Workload.Churn && Frame % 30 == 15 && (i & 1) == 0)
            {
                Writer.Stop(handle);
            }
            else if (moving)
            {
                Writer.Update(handle, a, b, tint, width, style);
            }
        }
    }
}
