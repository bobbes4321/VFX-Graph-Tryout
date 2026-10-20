using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Burst;
using Unity.Profiling;
using UnityEngine;

namespace LineLab.Production
{
    [Serializable]
    public class ProductionCell : BenchmarkCell
    {
        public bool fullStack;
        public int allocatedQuads;
        public string composition;
    }

    struct ProductionCase
    {
        public Technique Technique;
        public int Count;
        public int Scenario;
        public int Width;
        public int Rep;
        public bool Full;
    }

    // Builds the complete-effect matrix, runs warmed-up cells and exports raw
    // frames plus summaries. Tools/production_report.py audits and aggregates them.
    public static class ProductionBenchmark
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string F(double x)
        {
            return double.IsNaN(x) ? "" : x.ToString("0.######", Inv);
        }

        static double P(IEnumerable<double> values, double p)
        {
            var a = values
                .Where(x => !double.IsNaN(x) && !double.IsInfinity(x))
                .OrderBy(x => x)
                .ToArray();
            return a.Length == 0 ? double.NaN : a[(int)Math.Round((a.Length - 1) * p)];
        }

        static string Arg(string[] a, string k, string value)
        {
            int i = Array.IndexOf(a, k);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : value;
        }

        static double Last(ProfilerRecorder r)
        {
            return r.Valid ? r.LastValue : double.NaN;
        }

        static ProfilerRecorder Recorder(ProfilerCategory c, string n)
        {
            try
            {
                return ProfilerRecorder.StartNew(c, n, 1);
            }
            catch
            {
                return default;
            }
        }

        static List<ProductionCase> Cases(bool smoke)
        {
            var result = new List<ProductionCase>();
            var random = new System.Random(32194);
            void Group(int count, int scenario = 0, bool full = true, int width = 1920)
            {
                for (int rep = 0; rep < (smoke ? 1 : 3); rep++)
                {
                    foreach (
                        Technique t in Enum.GetValues(typeof(Technique))
                            .Cast<Technique>()
                            .OrderBy(_ => random.Next())
                    )
                    {
                        result.Add(
                            new ProductionCase
                            {
                                Technique = t,
                                Count = count,
                                Scenario = scenario,
                                Full = full,
                                Width = width,
                                Rep = rep,
                            }
                        );
                    }
                }
            }

            if (smoke)
            {
                Group(128);
                return result;
            }

            foreach (int count in new[] { 32, 128, 256, 512 })
            {
                Group(count);
            }

            Group(128, 1);
            Group(256, 1);
            Group(256, 2);
            Group(256, 0, false);
            Group(256, 0, true, 2560);
            Group(256, 2, true, 2560);
            return result;
        }

        public static IEnumerator Run(ProductionController lab, string[] args)
        {
            bool smoke = Arg(args, "--suite", "full") == "smoke";
            string output = Path.GetFullPath(
                Arg(args, "--results", "BenchmarkResults/production/reference")
            );
            Directory.CreateDirectory(output);
            int warm = 120;
            int samples = smoke ? 120 : 240;
            var meta = new RunMetadata
            {
                startedUtc = DateTime.UtcNow.ToString("O"),
                unity = Application.unityVersion,
                gpu = SystemInfo.graphicsDeviceName,
                cpu = SystemInfo.processorType,
                api = SystemInfo.graphicsDeviceType.ToString(),
                os = SystemInfo.operatingSystem,
                suite = smoke ? "production-smoke" : "production-full",
                commandLine = Environment.CommandLine,
                notes =
                    "Complete ability stack. Three 32-segment strands, 48 travelling motes, 24 ballistic sparks, eight corona cards and four ground runes. VFX: one five-system graph. Shuriken: native layers. Line/mesh paths: explicit procedural particle companion. Same environment and native command/strand jobs in baseline. Fixed 60 Hz simulation, randomized seed 32194, pooled lifecycle with 1.4s release tails. Fresh whole-frame timing only; bridge includes last completed PS callbacks, excludes deferred engine work. Alpha-zero fixed slots are not compacted.",
                buildGuid = Application.buildGUID,
                driver = SystemInfo.graphicsDeviceVersion,
                gpuMemoryMB = SystemInfo.graphicsMemorySize,
                systemMemoryMB = SystemInfo.systemMemorySize,
                processorCount = SystemInfo.processorCount,
                warmupFrames = warm,
                measurementFrames = samples,
                seed = 32194,
                burstEnabled = BurstCompiler.IsEnabled,
                developmentBuild = Debug.isDebugBuild,
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(),
            };
            File.WriteAllText(
                Path.Combine(output, "metadata.json"),
                JsonUtility.ToJson(meta, true)
            );
            var cases = Cases(smoke);
            var timings = new FrameTiming[1];
            var rows = new FrameSample[samples];
            var gc = Recorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            var batches = Recorder(ProfilerCategory.Render, "Batches Count");
            var memory = Recorder(ProfilerCategory.Memory, "Total Used Memory");
            var setPass = Recorder(ProfilerCategory.Render, "SetPass Calls Count");
            using (var summary = new StreamWriter(Path.Combine(output, "summary.jsonl")))
            using (var raw = new StreamWriter(Path.Combine(output, "raw-frames.csv")))
            {
                raw.WriteLine(
                    "cell,frame,timing_stamp,fresh_timing,frame_ms,gpu_ms,main_ms,render_ms,present_wait_ms,commands_ms,bridge_ms,gc_bytes,batches,setpass,api_input_bytes,total_used_memory_bytes"
                );
                for (int cell = 0; cell < cases.Count; cell++)
                {
                    var c = cases[cell];
                    Screen.SetResolution(
                        c.Width,
                        c.Width == 1920 ? 1080 : 1440,
                        FullScreenMode.Windowed
                    );
                    yield return null;
                    yield return null;
                    lab.Configure(c.Technique, c.Count, c.Full, c.Scenario);
                    lab.SetFocus(-1, true);
                    yield return null;
                    GC.Collect();
                    yield return null;
                    for (int i = 0; i < warm; i++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                    }

                    int observed = lab.Backend.LiveParticles();
                    int objects = lab.Backend.ObjectCount;
                    ulong stamp = 0;
                    for (int i = 0; i < samples; i++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                        bool fresh =
                            FrameTimingManager.GetLatestTimings(1, timings) > 0
                            && timings[0].frameStartTimestamp != stamp;
                        if (fresh)
                        {
                            stamp = timings[0].frameStartTimestamp;
                        }

                        var t = timings[0];
                        rows[i] = new FrameSample
                        {
                            frame = Time.frameCount,
                            timingStamp = stamp,
                            freshTiming = fresh ? 1 : 0,
                            wall = Time.unscaledDeltaTime * 1000,
                            gpu = fresh && t.gpuFrameTime > 0 ? t.gpuFrameTime : double.NaN,
                            main = fresh ? t.cpuMainThreadFrameTime : double.NaN,
                            render = fresh ? t.cpuRenderThreadFrameTime : double.NaN,
                            wait = fresh ? t.cpuMainThreadPresentWaitTime : double.NaN,
                            command = lab.CommandsMs,
                            bridge = lab.BridgeMs,
                            gc = Last(gc),
                            batches = Last(batches),
                            setPass = Last(setPass),
                            upload = lab.Backend.UploadBytes,
                            native = Last(memory),
                        };
                    }

                    var r = new ProductionCell
                    {
                        technique = c.Technique.ToString(),
                        workload = new[] { "Sustain", "Lifecycle", "Overdraw" }[c.Scenario],
                        count = c.Count,
                        segments = 32,
                        width = Screen.width,
                        height = Screen.height,
                        repetition = c.Rep,
                        order = cell,
                        samples = samples,
                        objects = objects,
                        observedParticles = observed,
                        fullStack = c.Full,
                        allocatedQuads =
                            c.Technique == Technique.Baseline ? 0 : c.Count * (c.Full ? 180 : 96),
                        composition = lab.Backend.Composition,
                        developmentBuild = Debug.isDebugBuild,
                        timingEnabled = meta.frameTimingEnabled,
                        setupMs = lab.SetupMs,
                        teardownMs = double.NaN,
                        instancingRequested =
                            c.Technique == Technique.ParticleMeshInstanced
                            || c.Technique == Technique.VfxPerLineBatched,
                        instancingEnabled = lab.Backend.InstancingActive,
                        frameMedianMs = P(rows.Select(f => f.wall), .5),
                        frameP95Ms = P(rows.Select(f => f.wall), .95),
                        gpuMedianMs = P(rows.Select(f => f.gpu), .5),
                        gpuP95Ms = P(rows.Select(f => f.gpu), .95),
                        mainMedianMs = P(rows.Select(f => f.main), .5),
                        renderMedianMs = P(rows.Select(f => f.render), .5),
                        presentWaitMedianMs = P(rows.Select(f => f.wait), .5),
                        commandMedianMs = P(rows.Select(f => f.command), .5),
                        bridgeMedianMs = P(rows.Select(f => f.bridge), .5),
                        gcMedianBytes = P(rows.Select(f => f.gc), .5),
                        gcMaxBytes = P(rows.Select(f => f.gc), 1),
                        batchesMedian = P(rows.Select(f => f.batches), .5),
                        setPassMedian = P(rows.Select(f => f.setPass), .5),
                        uploadBytes = P(rows.Select(f => f.upload), .5),
                        nativeMemoryBytes = P(rows.Select(f => f.native), .5),
                        validGpuSamples = rows.Count(f => !double.IsNaN(f.gpu)),
                    };
                    bool particles =
                        c.Technique == Technique.VfxBuffer
                        || c.Technique == Technique.VfxPerLine
                        || c.Technique == Technique.VfxPerLineBatched
                        || (
                            c.Technique >= Technique.ParticleBillboardShared
                            && c.Technique <= Technique.ParticleJobs
                        );
                    if (
                        r.instancingRequested != r.instancingEnabled
                        || r.validGpuSamples < 20
                        || r.width != c.Width
                        || r.height != (c.Width == 1920 ? 1080 : 1440)
                        || (particles && observed < c.Count * (c.Full ? 180 : 96) / 2)
                    )
                    {
                        string error =
                            "INVALID "
                            + cell
                            + " GPU samples "
                            + r.validGpuSamples
                            + " particles "
                            + observed;
                        File.WriteAllText(Path.Combine(output, "INVALID.txt"), error);
                        Debug.LogError(error);
                        Application.Quit(2);
                        yield break;
                    }

                    summary.WriteLine(JsonUtility.ToJson(r));
                    summary.Flush();
                    foreach (var f in rows)
                    {
                        raw.WriteLine(
                            string.Join(
                                ",",
                                new[]
                                {
                                    cell.ToString(),
                                    f.frame.ToString(),
                                    f.timingStamp.ToString(),
                                    f.freshTiming.ToString(),
                                    F(f.wall),
                                    F(f.gpu),
                                    F(f.main),
                                    F(f.render),
                                    F(f.wait),
                                    F(f.command),
                                    F(f.bridge),
                                    F(f.gc),
                                    F(f.batches),
                                    F(f.setPass),
                                    F(f.upload),
                                    F(f.native),
                                }
                            )
                        );
                    }

                    raw.Flush();
                    string progress =
                        $"{cell + 1}/{cases.Count} {c.Technique} {c.Count} {r.workload} full={c.Full} GPU={F(r.gpuMedianMs)} bridge={F(r.bridgeMedianMs)} particles={observed}";
                    File.WriteAllText(Path.Combine(output, "progress.txt"), progress);
                    Debug.Log(progress);
                }
            }

            gc.Dispose();
            batches.Dispose();
            memory.Dispose();
            setPass.Dispose();
            File.WriteAllText(Path.Combine(output, "COMPLETE.txt"), DateTime.UtcNow.ToString("O"));
            Application.Quit();
        }
    }
}
