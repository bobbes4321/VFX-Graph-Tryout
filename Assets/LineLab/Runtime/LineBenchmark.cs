using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.VFX;

namespace LineLab
{
    [Serializable]
    public class BenchmarkCell
    {
        public string technique;
        public string workload;
        public int count;
        public int segments;
        public int width;
        public int height;
        public int repetition;
        public int order;
        public int samples;
        public int validGpuSamples;
        public int objects;
        public int observedParticles;
        public bool instancingRequested;
        public bool instancingEnabled;
        public bool developmentBuild;
        public bool timingEnabled;
        public double setupMs;
        public double teardownMs;
        public double frameMedianMs;
        public double frameP95Ms;
        public double gpuMedianMs;
        public double gpuP95Ms;
        public double mainMedianMs;
        public double renderMedianMs;
        public double presentWaitMedianMs;
        public double commandMedianMs;
        public double bridgeMedianMs;
        public double gcMedianBytes;
        public double gcMaxBytes;
        public double batchesMedian;
        public double setPassMedian;
        public double uploadBytes;
        public double nativeMemoryBytes;
    }

    [Serializable]
    public class RunMetadata
    {
        public string startedUtc;
        public string unity;
        public string gpu;
        public string cpu;
        public string api;
        public string os;
        public string suite;
        public string commandLine;
        public string notes;
        public string buildGuid;
        public string driver;
        public int gpuMemoryMB;
        public int systemMemoryMB;
        public int processorCount;
        public int warmupFrames;
        public int measurementFrames;
        public int seed;
        public bool burstEnabled;
        public bool developmentBuild;
        public bool frameTimingEnabled;
    }

    struct FrameSample
    {
        public int frame;
        public int freshTiming;
        public ulong timingStamp;
        public double wall;
        public double gpu;
        public double main;
        public double render;
        public double wait;
        public double command;
        public double bridge;
        public double gc;
        public double batches;
        public double setPass;
        public double upload;
        public double native;
    }

    struct Case
    {
        public Technique technique;
        public Workload workload;
        public int count;
        public int segments;
        public int width;
        public int height;
        public int repetition;
    }

    // Original line-only measurement protocol. Timing/export happens in the
    // standalone player; Tools/report.py builds the interactive comparison.
    public static class LineBenchmark
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string Argument(string[] args, string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        static double Median(IEnumerable<double> values)
        {
            return Percentile(values, .5);
        }

        static double Percentile(IEnumerable<double> values, double p)
        {
            var a = values.Where(v => !double.IsNaN(v)).OrderBy(v => v).ToArray();
            return a.Length == 0 ? double.NaN : a[(int)Math.Round((a.Length - 1) * p)];
        }

        static string F(double d)
        {
            return double.IsNaN(d) ? "" : d.ToString("0.######", Inv);
        }

        static ProfilerRecorder Recorder(ProfilerCategory category, string name)
        {
            try
            {
                return ProfilerRecorder.StartNew(category, name, 1);
            }
            catch
            {
                return default;
            }
        }

        static double Last(ProfilerRecorder recorder)
        {
            return recorder.Valid ? recorder.LastValue : double.NaN;
        }

        static List<Case> Cases(string suite)
        {
            var list = new List<Case>();
            var random = new System.Random(190626);
            int reps = suite == "smoke" ? 1 : 3;
            void Group(int count, int width, int height, Workload workload, int segments = 16)
            {
                for (int r = 0; r < reps; r++)
                {
                    var shuffled = Enum.GetValues(typeof(Technique))
                        .Cast<Technique>()
                        .OrderBy(_ => random.Next())
                        .ToArray();
                    foreach (var t in shuffled)
                    {
                        list.Add(
                            new Case
                            {
                                technique = t,
                                count = count,
                                width = width,
                                height = height,
                                workload = workload,
                                segments = segments,
                                repetition = r,
                            }
                        );
                    }
                }
            }

            if (suite == "smoke")
            {
                Group(256, 1920, 1080, Workload.Moving);
                return list;
            }

            foreach (int n in new[] { 1, 32, 128, 256, 512, 1024, 2500, 5000 })
            {
                Group(n, 1920, 1080, Workload.Moving);
            }

            foreach (int n in new[] { 128, 256, 512, 1024 })
            {
                Group(n, 2560, 1440, Workload.Moving);
            }

            foreach (Workload w in new[] { Workload.Static, Workload.Churn, Workload.Overdraw })
            {
                foreach (int n in new[] { 128, 512 })
                {
                    Group(n, 1920, 1080, w);
                }

                Group(512, 2560, 1440, w);
            }

            Group(256, 1920, 1080, Workload.Moving, 1);
            Group(512, 1920, 1080, Workload.Moving, 1);
            Group(256, 1920, 1080, Workload.Moving, 8);
            Group(256, 1920, 1080, Workload.Moving, 32);
            return list;
        }

        public static IEnumerator Run(LineLabController lab, string[] args)
        {
            lab.Benchmarking = true;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;
            string suite = Argument(args, "--suite", "full");
            string output = Path.GetFullPath(
                Argument(
                    args,
                    "--results",
                    "BenchmarkResults/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                )
            );
            Directory.CreateDirectory(output);
            if (File.Exists(Path.Combine(output, "COMPLETE.txt")))
            {
                File.Delete(Path.Combine(output, "COMPLETE.txt"));
            }

            if (File.Exists(Path.Combine(output, "INVALID.txt")))
            {
                File.Delete(Path.Combine(output, "INVALID.txt"));
            }

            int warm = suite == "smoke" ? 90 : 90;
            int samples = suite == "smoke" ? 120 : 180;
            var metadata = new RunMetadata
            {
                startedUtc = DateTime.UtcNow.ToString("O"),
                unity = Application.unityVersion,
                gpu = SystemInfo.graphicsDeviceName,
                cpu = SystemInfo.processorType,
                api = SystemInfo.graphicsDeviceType.ToString(),
                os = SystemInfo.operatingSystem,
                suite = suite,
                commandLine = string.Join(" ", args),
                gpuMemoryMB = SystemInfo.graphicsMemorySize,
                systemMemoryMB = SystemInfo.systemMemorySize,
                processorCount = SystemInfo.processorCount,
                warmupFrames = warm,
                measurementFrames = samples,
                seed = 190626,
                burstEnabled = Unity.Burst.BurstCompiler.IsEnabled,
                developmentBuild = Debug.isDebugBuild,
                frameTimingEnabled = FrameTimingManager.IsFeatureEnabled(),
                buildGuid = Application.buildGUID,
                driver = SystemInfo.graphicsDeviceVersion,
                notes =
                    "Visible standalone windowed; four styles cycle evenly (heal/arc/plasma/order); VSync off; no FPS cap; fixed 60 Hz simulation clock; same arena and native command producer in baseline; randomized technique order within each repetition. GPU/CPU frame timing is delayed: fresh timestamped samples only, no synthetic replacement. Bridge timer includes job waits and API submission but not later engine rendering. Setup is pool creation, excluded from steady-state. Upload bytes are application API inputs, not a PCIe measurement. Churn toggles half the lines every 15 frames; shared systems retain invisible slots. LineRenderer uses connected joins and an 8-key color gradient; other methods use independent overlapping quads. Particle colors quantize to Color32. Particle jobs use mesh particles without GPU instancing.",
            };
            File.WriteAllText(
                Path.Combine(output, "metadata.json"),
                JsonUtility.ToJson(metadata, true)
            );
            var cases = Cases(suite);
            var frames = new FrameSample[samples];
            var timings = new FrameTiming[1];
            var gc = Recorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            var batches = Recorder(ProfilerCategory.Render, "Batches Count");
            var setPass = Recorder(ProfilerCategory.Render, "SetPass Calls Count");
            var memory = Recorder(ProfilerCategory.Memory, "Total Used Memory");
            using (var summary = new StreamWriter(Path.Combine(output, "summary.jsonl"), false))
            using (var raw = new StreamWriter(Path.Combine(output, "raw-frames.csv"), false))
            {
                raw.WriteLine(
                    "cell,frame,timing_timestamp,fresh_timing,wall_ms,gpu_ms,main_ms,render_ms,present_wait_ms,commands_ms,bridge_ms,gc_bytes,batches,setpass,api_input_bytes,total_used_memory_bytes"
                );
                for (int cell = 0; cell < cases.Count; cell++)
                {
                    var c = cases[cell];
                    Screen.SetResolution(c.width, c.height, FullScreenMode.Windowed);
                    yield return null;
                    yield return null;
                    lab.Configure(c.technique, c.count, c.segments, c.workload, false);
                    // Destroy() retires the previous pool at end of frame, then collect before warmup.
                    yield return null;
                    GC.Collect();
                    yield return null;
                    for (int i = 0; i < warm; i++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                    }

                    int observed = 0;
                    if (
                        c.technique == Technique.VfxBuffer
                        || c.technique == Technique.VfxPerLine
                        || c.technique == Technique.VfxPerLineBatched
                    )
                    {
                        foreach (
                            var vfx in lab.Backend.Root.GetComponentsInChildren<VisualEffect>()
                        )
                        {
                            observed += (int)vfx.aliveParticleCount;
                        }
                    }
                    else if (c.technique >= Technique.ParticleBillboardShared)
                    {
                        foreach (
                            var ps in lab.Backend.Root.GetComponentsInChildren<ParticleSystem>()
                        )
                        {
                            observed += ps.particleCount;
                        }
                    }

                    ulong lastStamp = 0;
                    for (int i = 0; i < samples; i++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                        uint available = FrameTimingManager.GetLatestTimings(1, timings);
                        bool fresh = available > 0 && timings[0].frameStartTimestamp != lastStamp;
                        if (fresh)
                        {
                            lastStamp = timings[0].frameStartTimestamp;
                        }

                        var t = timings[0];
                        frames[i] = new FrameSample
                        {
                            frame = Time.frameCount,
                            timingStamp = lastStamp,
                            freshTiming = fresh ? 1 : 0,
                            wall = Time.unscaledDeltaTime * 1000,
                            gpu = fresh && t.gpuFrameTime > 0 ? t.gpuFrameTime : double.NaN,
                            main = fresh ? t.cpuMainThreadFrameTime : double.NaN,
                            render = fresh ? t.cpuRenderThreadFrameTime : double.NaN,
                            wait = fresh ? t.cpuMainThreadPresentWaitTime : double.NaN,
                            command = lab.CommandMs,
                            bridge = lab.PresentMs,
                            gc = Last(gc),
                            batches = Last(batches),
                            setPass = Last(setPass),
                            upload = lab.Backend.UploadBytes,
                            native = Last(memory),
                        };
                    }

                    var result = new BenchmarkCell
                    {
                        technique = c.technique.ToString(),
                        workload = c.workload.ToString(),
                        count = c.count,
                        segments = c.segments,
                        width = Screen.width,
                        height = Screen.height,
                        repetition = c.repetition,
                        order = cell,
                        samples = samples,
                        objects = lab.Backend.ObjectCount,
                        observedParticles = observed,
                        instancingRequested =
                            c.technique == Technique.ParticleMeshInstanced
                            || c.technique == Technique.VfxPerLineBatched,
                        instancingEnabled = lab.Backend.InstancingActive,
                        developmentBuild = Debug.isDebugBuild,
                        timingEnabled = metadata.frameTimingEnabled,
                        setupMs = lab.SetupMs,
                        teardownMs = lab.TeardownMs,
                        frameMedianMs = Median(frames.Select(f => f.wall)),
                        frameP95Ms = Percentile(frames.Select(f => f.wall), .95),
                        gpuMedianMs = Median(frames.Select(f => f.gpu)),
                        gpuP95Ms = Percentile(frames.Select(f => f.gpu), .95),
                        mainMedianMs = Median(frames.Select(f => f.main)),
                        renderMedianMs = Median(frames.Select(f => f.render)),
                        presentWaitMedianMs = Median(frames.Select(f => f.wait)),
                        commandMedianMs = Median(frames.Select(f => f.command)),
                        bridgeMedianMs = Median(frames.Select(f => f.bridge)),
                        gcMedianBytes = Median(frames.Select(f => f.gc)),
                        gcMaxBytes = frames
                            .Where(f => !double.IsNaN(f.gc))
                            .Select(f => f.gc)
                            .DefaultIfEmpty(double.NaN)
                            .Max(),
                        batchesMedian = Median(frames.Select(f => f.batches)),
                        setPassMedian = Median(frames.Select(f => f.setPass)),
                        uploadBytes = Median(frames.Select(f => f.upload)),
                        nativeMemoryBytes = Median(frames.Select(f => f.native)),
                        validGpuSamples = frames.Count(f => !double.IsNaN(f.gpu)),
                    };
                    bool isVfx =
                        c.technique == Technique.VfxBuffer
                        || c.technique == Technique.VfxPerLine
                        || c.technique == Technique.VfxPerLineBatched;
                    bool isParticle =
                        c.technique >= Technique.ParticleBillboardShared
                        && c.technique <= Technique.ParticleJobs;
                    int minimumParticles =
                        c.count * c.segments / (c.workload == Workload.Churn ? 2 : 1);
                    if (
                        result.validGpuSamples < 20
                        || ((isVfx || isParticle) && observed < minimumParticles)
                        || Screen.width != c.width
                        || Screen.height != c.height
                    )
                    {
                        string failure =
                            $"INVALID cell {cell}: GPU samples {result.validGpuSamples}, particles {observed} (minimum {minimumParticles}), resolution {Screen.width}x{Screen.height} requested {c.width}x{c.height}. Keep the player visible and inspect the log.";
                        File.WriteAllText(Path.Combine(output, "INVALID.txt"), failure);
                        Debug.LogError(failure);
                        Application.Quit(2);
                        yield break;
                    }

                    summary.WriteLine(JsonUtility.ToJson(result));
                    summary.Flush();
                    foreach (var f in frames)
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
                        $"{cell + 1}/{cases.Count} {c.technique} {c.count} {c.workload} {Screen.width}x{Screen.height} r{c.repetition} GPU={F(result.gpuMedianMs)}ms bridge={F(result.bridgeMedianMs)}ms particles={observed}";
                    File.WriteAllText(Path.Combine(output, "progress.txt"), progress);
                    Debug.Log(progress);
                }
            }

            gc.Dispose();
            batches.Dispose();
            setPass.Dispose();
            memory.Dispose();
            File.WriteAllText(Path.Combine(output, "COMPLETE.txt"), DateTime.UtcNow.ToString("O"));
            Application.Quit(0);
        }

        public static IEnumerator Visuals(LineLabController lab, string output)
        {
            lab.Benchmarking = true;
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            if (File.Exists(Path.Combine(output, "COMPLETE.txt")))
            {
                File.Delete(Path.Combine(output, "COMPLETE.txt"));
            }

            var validation = new List<string>();
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return null;
            foreach (Technique t in Enum.GetValues(typeof(Technique)))
            {
                lab.Configure(t, 8, 32, Workload.Moving, true);
                for (int i = 0; i < 120; i++)
                {
                    yield return null;
                }

                int particles =
                    lab.Backend.Root.GetComponentsInChildren<VisualEffect>()
                        .Sum(v => (int)v.aliveParticleCount)
                    + lab.Backend.Root.GetComponentsInChildren<ParticleSystem>()
                        .Sum(p => p.particleCount);
                validation.Add(
                    t
                        + ": "
                        + particles
                        + " live particles; "
                        + lab.Backend.ObjectCount
                        + " objects; instancing flag "
                        + lab.Backend.InstancingActive
                );
                ScreenCapture.CaptureScreenshot(Path.Combine(output, t + ".png"));
                yield return new WaitForEndOfFrame();
                yield return null;
            }

            File.WriteAllLines(Path.Combine(output, "validation.txt"), validation);
            lab.Benchmarking = false;
            lab.Gallery = true;
            lab.Configure(Technique.VfxBuffer, 8, 32, Workload.Moving, true);
            for (int i = 0; i < 120; i++)
            {
                yield return null;
            }

            ScreenCapture.CaptureScreenshot(Path.Combine(output, "Showcase.png"));
            yield return new WaitForEndOfFrame();
            yield return null;
            lab.Gallery = false;
            lab.Configure(Technique.VfxBuffer, 8, 32, Workload.Moving, true);
            for (int i = 0; i < 60; i++)
            {
                yield return null;
            }

            ScreenCapture.CaptureScreenshot(Path.Combine(output, "TechniqueLab.png"));
            yield return new WaitForEndOfFrame();
            yield return null;
            File.WriteAllText(Path.Combine(output, "COMPLETE.txt"), "Visual captures complete");
            Application.Quit();
        }
    }
}
