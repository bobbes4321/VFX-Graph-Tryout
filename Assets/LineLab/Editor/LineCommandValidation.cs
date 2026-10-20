using System;
using System.Collections.Generic;
using System.IO;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;

namespace LineLab.Editor
{
    public static class LineCommandValidation
    {
        [BurstCompile]
        struct ParallelBegin : IJobParallelFor
        {
            public LineWriter Writer;

            public void Execute(int i)
            {
                Writer.Begin(
                    new LineHandle(i, 1),
                    new float3(i, 1, 2),
                    new float3(i, 2, 3),
                    new float4(1),
                    .2f
                );
            }
        }

        [MenuItem("Line Lab/Validate unmanaged command lifecycle")]
        public static void RunMenu()
        {
            Run();
        }

        public static string Run()
        {
            var passed = new List<string>();
            void Check(bool value, string message)
            {
                if (!value)
                {
                    throw new Exception("FAILED: " + message);
                }

                passed.Add("PASS: " + message);
            }

            using (var world = new LineWorld(1024))
            {
                world
                    .Consume(new ParallelBegin { Writer = world.Writer }.Schedule(1024, 32))
                    .Complete();
                bool valid = true;
                for (int i = 0; i < 1024; i++)
                {
                    valid &= world.Lines[i].Start.x == i && world.Lines[i].Start.w == 1;
                }

                Check(valid, "1,024 parallel Burst producers start independent slots");
                var writer = world.Writer;
                var old = new LineHandle(0, 1);
                writer.Update(old, new float3(7), new float3(8), new float4(1), .4f);
                world.Consume().Complete();
                Check(world.Lines[0].Start.x == 7, "Update changes an active line");
                writer.Stop(old);
                world.Consume().Complete();
                Check(world.Lines[0].Start.w == 0, "Stop clears active flag");
                writer.Update(old, new float3(99), new float3(99), new float4(1), .1f);
                world.Consume().Complete();
                Check(
                    world.Lines[0].Start.w == 0,
                    "Delayed update cannot resurrect a stopped line"
                );
                writer.Begin(
                    new LineHandle(0, 2),
                    new float3(12),
                    new float3(13),
                    new float4(1),
                    .5f
                );
                world.Consume().Complete();
                Check(
                    world.Lines[0].Start.w == 1 && world.Lines[0].Start.x == 12,
                    "Slot reuse with newer generation succeeds"
                );
                writer.Stop(old);
                world.Consume().Complete();
                Check(world.Lines[0].Start.w == 1, "Stale stop cannot stop a reused slot");
                writer.Begin(
                    new LineHandle(0, 2),
                    new float3(90),
                    new float3(90),
                    new float4(1),
                    .1f
                );
                world.Consume().Complete();
                Check(world.Lines[0].Start.x == 12, "Duplicate Begin is idempotent");
                writer.Stop(new LineHandle(-1, 1));
                writer.Begin(new LineHandle(1024, 1), default, default, default, 1);
                world.Consume().Complete();
                Check(world.Lines[0].Start.x == 12, "Out-of-range handles are ignored safely");
                Check(
                    System.Runtime.InteropServices.Marshal.SizeOf<LineData>() == 64,
                    "CPU/GPU line layout is 64 bytes"
                );
                var zero = new LineData
                {
                    Start = new float4(0, 0, 0, 1),
                    End = new float4(0, 0, 0, .1f),
                    Color = new float4(1),
                };
                LineGeometry.Segment(
                    zero,
                    0,
                    16,
                    0,
                    new float3(0, 20, -10),
                    out var mid,
                    out var tangent,
                    out var side,
                    out var length,
                    out var color
                );
                Check(
                    math.all(math.isfinite(mid))
                        && math.all(math.isfinite(tangent))
                        && math.all(math.isfinite(side)),
                    "Degenerate endpoints stay finite in native geometry"
                );
            }

            Directory.CreateDirectory("BenchmarkResults");
            string text =
                "Burst enabled: " + BurstCompiler.IsEnabled + "\n" + string.Join("\n", passed);
            File.WriteAllText("BenchmarkResults/command-validation.txt", text);
            return text;
        }
    }
}
