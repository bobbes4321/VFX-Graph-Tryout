using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
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
        public string catalogHash;
        public int allocatedCorePerAbility;
        public int allocatedFullPerAbility;
        public int activeAbilities;
        public int[] activeEffectCounts;
        public int logicalQuads;
        public int[] expectedStreamParticles;
        public int[] observedStreamParticles;
        public Vector3 cameraPosition;
        public Vector3 cameraForward;
        public float cameraFieldOfView;
        public float cameraNearClip;
        public float cameraFarClip;
        public int visibleEndpoints;
        public int requiredEndpoints;
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

        static string FormatNumber(double value)
        {
            return double.IsNaN(value) ? "" : value.ToString("0.######", Inv);
        }

        static double Percentile(IEnumerable<double> values, double percentile)
        {
            double[] ordered = values
                .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
                .OrderBy(value => value)
                .ToArray();
            return ordered.Length == 0
                ? double.NaN
                : ordered[(int)Math.Round((ordered.Length - 1) * percentile)];
        }

        static string GetArgument(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : fallback;
        }

        static double LastSample(ProfilerRecorder recorder)
        {
            return recorder.Valid ? recorder.LastValue : double.NaN;
        }

        static ProfilerRecorder StartRecorder(ProfilerCategory category, string counterName)
        {
            try
            {
                return ProfilerRecorder.StartNew(category, counterName, 1);
            }
            catch
            {
                return default;
            }
        }

        [Serializable]
        public sealed class PlannedCell
        {
            public int order;
            public string technique;
            public string workload;
            public int count;
            public int width;
            public int height;
            public int repetition;
            public bool fullStack;
        }

        [Serializable]
        public sealed class SuiteManifest
        {
            public int schemaVersion = 1;
            public string suite;
            public int repetitions;
            public bool enforceSpreadFrustum = true;
            public int seed = 32194;
            public PlannedCell[] cells;
        }

        [Serializable]
        public sealed class ContentSnapshot
        {
            public int schemaVersion = 1;
            public string catalogHash;
            public string layoutKey;
            public string[] effectNames;
            public string[] effectKeys;
            public int segments;
            public int ribbonSlots;
            public int layerSlots;
            public int[] layerCapacities;
            public int allocatedCorePerAbility;
            public int allocatedFullPerAbility;
            public EffectRecipeData[] effects;
            public RibbonRecipeData[] ribbons;
            public LayerRecipeData[] layers;
            public Unity.Mathematics.float4[] ramps;
        }

        [Serializable]
        public sealed class AuthoredMetadata
        {
            public int schemaVersion = 1;
            public string catalogHash;
            public string contentSnapshotSha256;
            public string suiteSha256;
            public int expectedCells;
            public int repetitions;
            public string particleCensus =
                "After warmup, freeze native inputs for 16 unmeasured frames so asynchronous GPU alive counts settle; take census, then resume for 8 unmeasured recovery frames. Shared systems retain every allocated slot; per-ability systems contain only active components. Logical quads include transparent cards.";
        }

        static string Workload(int scenario)
        {
            return new[] { "Sustain", "Lifecycle", "Overdraw", "Spread" }[scenario];
        }

        static List<ProductionCase> BuildCases(string suite)
        {
            bool smoke = suite == "smoke" || suite == "authored-smoke";
            var result = new List<ProductionCase>();
            var random = new System.Random(32194);
            void Group(int count, int scenario = 0, bool full = true, int width = 1920)
            {
                for (int rep = 0; rep < (smoke ? 1 : 3); rep++)
                {
                    foreach (
                        Technique technique in Enum.GetValues(typeof(Technique))
                            .Cast<Technique>()
                            .OrderBy(_ => random.Next())
                    )
                    {
                        result.Add(
                            new ProductionCase
                            {
                                Technique = technique,
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
            }
            else if (suite == "scale" || suite == "spread")
            {
                foreach (int count in new[] { 512, 1024, 2500, 5000 })
                {
                    if (suite == "scale")
                    {
                        Group(count);
                    }
                    Group(count, 3);
                }
            }
            else if (suite == "full" || suite == "workloads" || suite == "line-hdr")
            {
                if (suite != "workloads")
                {
                    foreach (int count in new[] { 32, 128, 256, 512, 1024, 2500, 5000 })
                    {
                        Group(count);
                    }
                }
                if (suite == "line-hdr")
                {
                    foreach (int count in new[] { 512, 1024, 2500, 5000 })
                    {
                        Group(count, 3);
                    }
                }

                Group(128, 1);
                Group(256, 1);
                Group(256, 2);
                Group(256, 0, false);
                Group(256, 0, true, 2560);
                Group(256, 2, true, 2560);
            }
            else
            {
                throw new ArgumentException("Unknown benchmark suite: " + suite);
            }

            if (suite == "line-hdr")
            {
                result.RemoveAll(cell =>
                    cell.Technique != Technique.LineRenderer && cell.Technique != Technique.Baseline
                );
            }
            return result;
        }

        static int FrameArgument(string[] args, string name, int fallback)
        {
            if (
                !int.TryParse(GetArgument(args, name, fallback.ToString(Inv)), out int frames)
                || frames < 20
                || frames > 10000
            )
            {
                throw new ArgumentException(name + " must be between 20 and 10000 frames.");
            }

            return frames;
        }

        static string WriteManifest(string output, string filename, object value)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonUtility.ToJson(value, true));
            File.WriteAllBytes(Path.Combine(output, filename), bytes);
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter
                    .ToString(sha.ComputeHash(bytes))
                    .Replace("-", "")
                    .ToLowerInvariant();
            }
        }

        static void WriteAuthoredManifests(
            string output,
            string suite,
            List<ProductionCase> cases,
            NativeEffectCatalog catalog
        )
        {
            var snapshot = new ContentSnapshot
            {
                catalogHash = catalog.Fingerprint,
                layoutKey = catalog.LayoutKey,
                effectNames = catalog.EffectNames,
                effectKeys = catalog.EffectKeys,
                segments = catalog.Segments,
                ribbonSlots = catalog.RibbonSlots,
                layerSlots = catalog.LayerSlots,
                layerCapacities = catalog.LayerCapacities,
                allocatedCorePerAbility = catalog.AllocatedCoreQuadsPerAbility,
                allocatedFullPerAbility = catalog.AllocatedFullQuadsPerAbility,
                effects = catalog.Effects.ToArray(),
                ribbons = catalog.Ribbons.ToArray(),
                layers = catalog.Layers.ToArray(),
                ramps = catalog.Ramps.ToArray(),
            };
            // Unity silently omits arrays of non-serializable structs. Reject an
            // incomplete export before collecting any measurements.
            var roundTrip = JsonUtility.FromJson<ContentSnapshot>(JsonUtility.ToJson(snapshot));
            if (
                roundTrip.effects == null
                || roundTrip.effects.Length != snapshot.effects.Length
                || roundTrip.ribbons == null
                || roundTrip.ribbons.Length != snapshot.ribbons.Length
                || roundTrip.layers == null
                || roundTrip.layers.Length != snapshot.layers.Length
                || roundTrip.ramps == null
                || roundTrip.ramps.Length != snapshot.ramps.Length
            )
            {
                throw new InvalidOperationException(
                    "Recipe snapshot failed serialization round-trip."
                );
            }
            var planned = cases
                .Select(
                    (testCase, index) =>
                        new PlannedCell
                        {
                            order = index,
                            technique = testCase.Technique.ToString(),
                            workload = Workload(testCase.Scenario),
                            count = testCase.Count,
                            width = testCase.Width,
                            height = testCase.Width == 1920 ? 1080 : 1440,
                            repetition = testCase.Rep,
                            fullStack = testCase.Full,
                        }
                )
                .ToArray();
            int repetitions = suite == "smoke" || suite == "authored-smoke" ? 1 : 3;
            string snapshotHash = WriteManifest(output, "content-snapshot.json", snapshot);
            string suiteHash = WriteManifest(
                output,
                "suite.json",
                new SuiteManifest
                {
                    suite = suite,
                    repetitions = repetitions,
                    cells = planned,
                }
            );
            WriteManifest(
                output,
                "authored-metadata.json",
                new AuthoredMetadata
                {
                    catalogHash = catalog.Fingerprint,
                    contentSnapshotSha256 = snapshotHash,
                    suiteSha256 = suiteHash,
                    expectedCells = cases.Count,
                    repetitions = repetitions,
                }
            );
        }

        // Outside the timed interval: prove each renderer has the intended pools,
        // rather than accepting a fast result with a missing secondary system.
        static void Census(
            ProductionController lab,
            ProductionCase cell,
            out int active,
            out int logical,
            out int[] expected,
            out int[] observed,
            out int[] activeEffects
        )
        {
            NativeEffectCatalog catalog = lab.Catalog;
            active = 0;
            logical = 0;
            activeEffects = new int[catalog.EffectCount];
            foreach (LineData line in lab.World.Lines)
            {
                if (line.Start.w <= 0)
                {
                    continue;
                }

                active++;
                int effectIndex = (int)line.Shape.x;
                activeEffects[effectIndex]++;
                EffectRecipeData recipe = catalog.Effects[effectIndex];
                logical += (int)recipe.Layout.x * catalog.Segments;
                if (cell.Full)
                {
                    for (int slot = 0; slot < (int)recipe.Layout.y; slot++)
                    {
                        logical += (int)
                            catalog.Layers[effectIndex * catalog.LayerSlots + slot].Layout.y;
                    }
                }
            }

            observed = lab.Backend.StreamParticleCounts();
            expected = new int[1 + (cell.Full ? catalog.LayerSlots : 0)];
            bool particle =
                cell.Technique.ToString().StartsWith("Vfx", StringComparison.Ordinal)
                || cell.Technique.ToString().StartsWith("Particle", StringComparison.Ordinal);
            if (particle)
            {
                bool perAbility =
                    cell.Technique == Technique.VfxPerLine
                    || cell.Technique == Technique.VfxPerLineBatched
                    || cell.Technique == Technique.ParticleBillboardPerLine;
                int owners = perAbility ? active : cell.Count;
                expected[0] = owners * catalog.AllocatedCoreQuadsPerAbility;
                for (int slot = 1; slot < expected.Length; slot++)
                {
                    expected[slot] = owners * catalog.LayerCapacities[slot - 1];
                }
            }

            if (cell.Technique == Technique.Baseline)
            {
                logical = 0;
            }
        }

        static int VisibleEndpoints(AbilityWorld world, Camera camera)
        {
            int visible = 0;
            foreach (LineData line in world.Lines)
            {
                if (line.Start.w <= 0)
                {
                    continue;
                }
                visible += EndpointVisible(camera, line.Start.xyz) ? 1 : 0;
                visible += EndpointVisible(camera, line.End.xyz) ? 1 : 0;
            }
            return visible;
        }

        static bool EndpointVisible(Camera camera, Vector3 endpoint)
        {
            Vector3 viewport = camera.WorldToViewportPoint(endpoint);
            return viewport.z >= camera.nearClipPlane
                && viewport.z <= camera.farClipPlane
                && viewport.x >= 0
                && viewport.x <= 1
                && viewport.y >= 0
                && viewport.y <= 1;
        }

        public static IEnumerator Run(ProductionController lab, string[] args)
        {
            string suite = GetArgument(args, "--suite", "scale");
            var cases = BuildCases(suite);
            string output = Path.GetFullPath(
                GetArgument(
                    args,
                    "--results",
                    "BenchmarkResults/authored/run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")
                )
            );
            if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
            {
                throw new IOException(
                    "Benchmark output must be empty; refusing to overwrite " + output
                );
            }

            Directory.CreateDirectory(output);
            int warm = FrameArgument(args, "--warmup", 120);
            int samples = FrameArgument(args, "--samples", 240);
            WriteAuthoredManifests(output, suite, cases, lab.Catalog);
            var meta = new RunMetadata
            {
                startedUtc = DateTime.UtcNow.ToString("O"),
                unity = Application.unityVersion,
                gpu = SystemInfo.graphicsDeviceName,
                cpu = SystemInfo.processorType,
                api = SystemInfo.graphicsDeviceType.ToString(),
                os = SystemInfo.operatingSystem,
                suite = "authored-" + suite,
                commandLine = Environment.CommandLine,
                notes =
                    "Authored effect catalog; exact recipes and topology retained in content-snapshot.json. Fixed 60 Hz simulation, shuffled technique order with seed 32194, fresh pools per cell and three repetitions in one process (one for smoke). Baseline retains environment and native commands/strand jobs. GPU deltas are whole-frame baseline differences, not isolated timestamps. CPU bridge includes last completed particle callbacks and excludes deferred engine work. Setup measures backend construction only; native-world allocation and subsequent first-use work are outside it. Fixed shared slots are not compacted. GPU timings arrive asynchronously; CSV CPU/GPU rows do not imply same-frame correlation. Dense Sustain uses a fixed 36x19 m footprint; Spread uses a fixed wider 180x95 m footprint and a wider camera at every count. Spread makes lines smaller on screen, so it is a separate density workload rather than an isolated CPU comparison.",
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
            var timings = new FrameTiming[1];
            var rows = new FrameSample[samples];
            var gc = StartRecorder(ProfilerCategory.Memory, "GC Allocated In Frame");
            var batches = StartRecorder(ProfilerCategory.Render, "Batches Count");
            var memory = StartRecorder(ProfilerCategory.Memory, "Total Used Memory");
            var setPass = StartRecorder(ProfilerCategory.Render, "SetPass Calls Count");
            using (var summary = new StreamWriter(Path.Combine(output, "summary.jsonl")))
            using (var raw = new StreamWriter(Path.Combine(output, "raw-frames.csv")))
            {
                raw.WriteLine(
                    "cell,frame,timing_stamp,fresh_timing,frame_ms,gpu_ms,main_ms,render_ms,present_wait_ms,commands_ms,bridge_ms,gc_bytes,batches,setpass,api_input_bytes,total_used_memory_bytes"
                );
                for (int cell = 0; cell < cases.Count; cell++)
                {
                    var testCase = cases[cell];
                    Screen.SetResolution(
                        testCase.Width,
                        testCase.Width == 1920 ? 1080 : 1440,
                        FullScreenMode.Windowed
                    );
                    yield return null;
                    yield return null;
                    lab.Configure(
                        testCase.Technique,
                        testCase.Count,
                        testCase.Full,
                        testCase.Scenario
                    );
                    lab.SetFocus(-1, true);
                    yield return null;
                    GC.Collect();
                    yield return null;
                    for (int i = 0; i < warm; i++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                    }

                    // Alive counts arrive asynchronously from the GPU. Freeze
                    // native inputs outside sampling so transitions cannot race
                    // the readback; persistent VFX/particle pools keep updating.
                    lab.Freeze = true;
                    for (int settle = 0; settle < 16; settle++)
                    {
                        yield return null;
                    }
                    Census(
                        lab,
                        testCase,
                        out int activeAbilities,
                        out int logicalQuads,
                        out int[] expectedStreams,
                        out int[] observedStreams,
                        out int[] activeEffects
                    );
                    int observed = observedStreams.Sum();
                    Camera presentationCamera = Camera.main;
                    int visibleEndpoints = VisibleEndpoints(lab.World, presentationCamera);
                    int requiredEndpoints = activeAbilities * 2;
                    if (testCase.Scenario == 3 && visibleEndpoints != requiredEndpoints)
                    {
                        File.WriteAllText(
                            Path.Combine(output, "INVALID.txt"),
                            $"Spread frustum rejected: {visibleEndpoints}/{requiredEndpoints} endpoints visible; far={presentationCamera.farClipPlane}"
                        );
                        Application.Quit(2);
                        yield break;
                    }
                    int objects = lab.Backend.ObjectCount;
                    lab.Freeze = false;
                    // Resume procedural submissions and let the census work leave
                    // recorder frames before sampling resumes.
                    for (int recovery = 0; recovery < 8; recovery++)
                    {
                        FrameTimingManager.CaptureFrameTimings();
                        yield return null;
                    }
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

                        var timing = timings[0];
                        rows[i] = new FrameSample
                        {
                            frame = Time.frameCount,
                            timingStamp = stamp,
                            freshTiming = fresh ? 1 : 0,
                            wall = Time.unscaledDeltaTime * 1000,
                            gpu =
                                fresh && timing.gpuFrameTime > 0 ? timing.gpuFrameTime : double.NaN,
                            main = fresh ? timing.cpuMainThreadFrameTime : double.NaN,
                            render = fresh ? timing.cpuRenderThreadFrameTime : double.NaN,
                            wait = fresh ? timing.cpuMainThreadPresentWaitTime : double.NaN,
                            command = lab.CommandsMs,
                            bridge = lab.BridgeMs,
                            gc = LastSample(gc),
                            batches = LastSample(batches),
                            setPass = LastSample(setPass),
                            upload = lab.Backend.UploadBytes,
                            native = LastSample(memory),
                        };
                    }

                    var result = new ProductionCell
                    {
                        technique = testCase.Technique.ToString(),
                        workload = Workload(testCase.Scenario),
                        count = testCase.Count,
                        segments = lab.Catalog.Segments,
                        width = Screen.width,
                        height = Screen.height,
                        repetition = testCase.Rep,
                        order = cell,
                        samples = samples,
                        objects = objects,
                        observedParticles = observed,
                        fullStack = testCase.Full,
                        allocatedQuads =
                            testCase.Technique == Technique.Baseline
                                ? 0
                                : testCase.Count
                                    * (
                                        testCase.Full
                                            ? lab.Catalog.AllocatedFullQuadsPerAbility
                                            : lab.Catalog.AllocatedCoreQuadsPerAbility
                                    ),
                        catalogHash = lab.Catalog.Fingerprint,
                        allocatedCorePerAbility = lab.Catalog.AllocatedCoreQuadsPerAbility,
                        allocatedFullPerAbility = lab.Catalog.AllocatedFullQuadsPerAbility,
                        activeAbilities = activeAbilities,
                        activeEffectCounts = activeEffects,
                        logicalQuads = logicalQuads,
                        expectedStreamParticles = expectedStreams,
                        observedStreamParticles = observedStreams,
                        cameraPosition = presentationCamera.transform.position,
                        cameraForward = presentationCamera.transform.forward,
                        cameraFieldOfView = presentationCamera.fieldOfView,
                        cameraNearClip = presentationCamera.nearClipPlane,
                        cameraFarClip = presentationCamera.farClipPlane,
                        visibleEndpoints = visibleEndpoints,
                        requiredEndpoints = requiredEndpoints,
                        composition = lab.Backend.Composition,
                        developmentBuild = Debug.isDebugBuild,
                        timingEnabled = meta.frameTimingEnabled,
                        setupMs = lab.SetupMs,
                        teardownMs = double.NaN,
                        instancingRequested =
                            testCase.Technique == Technique.ParticleMeshInstanced
                            || testCase.Technique == Technique.VfxPerLineBatched,
                        instancingEnabled = lab.Backend.InstancingActive,
                        frameMedianMs = Percentile(rows.Select(f => f.wall), .5),
                        frameP95Ms = Percentile(rows.Select(f => f.wall), .95),
                        gpuMedianMs = Percentile(rows.Select(f => f.gpu), .5),
                        gpuP95Ms = Percentile(rows.Select(f => f.gpu), .95),
                        mainMedianMs = Percentile(rows.Select(f => f.main), .5),
                        renderMedianMs = Percentile(rows.Select(f => f.render), .5),
                        presentWaitMedianMs = Percentile(rows.Select(f => f.wait), .5),
                        commandMedianMs = Percentile(rows.Select(f => f.command), .5),
                        bridgeMedianMs = Percentile(rows.Select(f => f.bridge), .5),
                        gcMedianBytes = Percentile(rows.Select(f => f.gc), .5),
                        gcMaxBytes = Percentile(rows.Select(f => f.gc), 1),
                        batchesMedian = Percentile(rows.Select(f => f.batches), .5),
                        setPassMedian = Percentile(rows.Select(f => f.setPass), .5),
                        uploadBytes = Percentile(rows.Select(f => f.upload), .5),
                        nativeMemoryBytes = Percentile(rows.Select(f => f.native), .5),
                        validGpuSamples = rows.Count(f => !double.IsNaN(f.gpu)),
                    };
                    if (
                        result.instancingRequested != result.instancingEnabled
                        || result.validGpuSamples < 20
                        || result.width != testCase.Width
                        || result.height != (testCase.Width == 1920 ? 1080 : 1440)
                        || !expectedStreams.SequenceEqual(observedStreams)
                    )
                    {
                        string error =
                            "INVALID "
                            + cell
                            + " GPU samples "
                            + result.validGpuSamples
                            + " particles "
                            + observed
                            + " expected streams ["
                            + string.Join(",", expectedStreams)
                            + "] observed ["
                            + string.Join(",", observedStreams)
                            + "]";
                        File.WriteAllText(Path.Combine(output, "INVALID.txt"), error);
                        Debug.LogError(error);
                        Application.Quit(2);
                        yield break;
                    }

                    summary.WriteLine(JsonUtility.ToJson(result));
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
                                    FormatNumber(f.wall),
                                    FormatNumber(f.gpu),
                                    FormatNumber(f.main),
                                    FormatNumber(f.render),
                                    FormatNumber(f.wait),
                                    FormatNumber(f.command),
                                    FormatNumber(f.bridge),
                                    FormatNumber(f.gc),
                                    FormatNumber(f.batches),
                                    FormatNumber(f.setPass),
                                    FormatNumber(f.upload),
                                    FormatNumber(f.native),
                                }
                            )
                        );
                    }

                    raw.Flush();
                    string progress =
                        $"{cell + 1}/{cases.Count} {testCase.Technique} {testCase.Count} {result.workload} full={testCase.Full} GPU={FormatNumber(result.gpuMedianMs)} bridge={FormatNumber(result.bridgeMedianMs)} particles={observed}";
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
