using System;
using System.Collections;
using System.IO;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LineLab.Production
{
    // Scene host: demo commands -> native consumer -> strand preparation -> renderer.
    // UI and benchmark orchestration live here; gameplay does not need this component.
    public sealed class ProductionController : MonoBehaviour
    {
        public AbilityWorld World { get; private set; }
        public AbilityBackend Backend { get; private set; }
        public NativeEffectCatalog Catalog { get; private set; }

        [Tooltip(
            "Reusable source library. Rebuild Preview applies edits; gameplay jobs only see baked IDs."
        )]
        public EffectLibrary Library;
        public static Action<EffectLibrary, int> BeforePreviewConfiguration;
        int previewEffect = -1;
        bool previewReleased;
        bool capturing;
        int effectPage;
        public Technique Technique = Technique.VfxBuffer;
        public int Count = 3;
        public int Scenario;
        public bool Full = true;
        public bool Hero = true;
        public bool Benchmarking;
        public float Clock { get; private set; }
        public int Frame { get; private set; }

        public double CommandsMs;
        public double BridgeMs;
        public double SetupMs;
        public bool Freeze;
        bool first = true;
        bool showMethods;
        int focus = -1;
        int previousSimulationFrame = -1;
        Camera viewCamera;
        float originalFarClipPlane = 180;
        float hudClock;
        string stats = "";
        string composition = "";
        Texture2D panel;
        GUIStyle title;
        GUIStyle eyebrow;
        GUIStyle label;
        GUIStyle button;
        GUIStyle active;
        GUIStyle small;
        Vector3 cameraTargetPosition;
        Vector3 cameraTargetLook;

        static string Arg(string[] args, string name, string fallback)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : fallback;
        }

        IEnumerator Start()
        {
            viewCamera = Camera.main;
            originalFarClipPlane = viewCamera.farClipPlane;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = 60;
            Application.runInBackground = true;
            var args = Environment.GetCommandLineArgs();
            string libraryName = Arg(args, "--library", "");
            if (!string.IsNullOrEmpty(libraryName))
            {
                Library = Resources.Load<EffectLibrary>("Production/" + libraryName);
                if (Library == null)
                {
                    throw new ArgumentException("Unknown effect library: " + libraryName);
                }
            }

            if (Library == null)
            {
                Library = Resources.Load<EffectLibrary>("Production/EffectLibrary");
            }

            Configure(Technique, 3, true, 0, true);
            SetFocus(-1, true);
            if (Array.IndexOf(args, "--production-benchmark") >= 0)
            {
                Benchmarking = true;
                Application.targetFrameRate = -1;
                yield return ProductionBenchmark.Run(this, args);
            }
            else if (Array.IndexOf(args, "--production-capture") >= 0)
            {
                Benchmarking = true;
                capturing = true;
                string effectKey = Arg(args, "--effect", "");
                if (!string.IsNullOrEmpty(effectKey))
                {
                    SetPreviewEffect(Catalog.GetEffectId(effectKey).Value);
                }

                yield return Capture(Arg(args, "--results", "Screenshots/Production"));
            }
        }

        public void Configure(Technique t, int count, bool full, int scenario, bool hero = false)
        {
            if (count < 1 || count > EffectRecipeLayout.MaximumAbilities)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (Catalog == null)
            {
                Catalog = new NativeEffectCatalog(Library);
            }

            if (Application.isEditor && !Benchmarking)
            {
                BeforePreviewConfiguration?.Invoke(Library, count);
            }

            var nextWorld = new AbilityWorld(count, Catalog);
            AbilityBackend nextBackend;
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                nextBackend = new AbilityBackend(
                    t,
                    count,
                    viewCamera ?? Camera.main,
                    full,
                    Catalog
                );
            }
            catch
            {
                nextWorld.Dispose();
                throw;
            }

            double setup =
                (System.Diagnostics.Stopwatch.GetTimestamp() - start)
                * 1000d
                / System.Diagnostics.Stopwatch.Frequency;
            Backend?.Dispose();
            World?.Dispose();
            Technique = t;
            Count = count;
            Full = full;
            Scenario = scenario;
            Hero = hero;
            Frame = 0;
            Clock = 0;
            first = true;
            previousSimulationFrame = -1;
            previewReleased = false;
            World = nextWorld;
            Backend = nextBackend;
            SetupMs = setup;
            composition = Backend.Composition;
        }

        public void RebuildLibrary()
        {
            var replacement = new NativeEffectCatalog(Library);
            var previous = Catalog;
            Catalog = replacement;
            if (previewEffect >= replacement.EffectCount)
            {
                previewEffect = -1;
            }

            try
            {
                Configure(Technique, Count, Full, Scenario, Hero);
            }
            catch
            {
                Catalog = previous;
                replacement.Dispose();
                throw;
            }

            previous?.Dispose();
        }

        public void SetPreviewEffect(int effect)
        {
            if (Catalog == null || (uint)effect >= (uint)Catalog.EffectCount)
            {
                throw new ArgumentOutOfRangeException(nameof(effect));
            }

            previewEffect = effect;
            Configure(Technique, 3, Full, 0, true);
            SetFocus(1);
        }

        public void ReleasePreview()
        {
            if (World == null || Benchmarking)
            {
                return;
            }

            for (int slot = 0; slot < Count; slot++)
            {
                World.Writer.Release(new LineHandle(slot, World.Generations[slot]), Clock);
            }

            previewReleased = true;
        }

        void Update()
        {
            if (World == null || Freeze)
            {
                return;
            }

            float time = Benchmarking
                ? Frame / 60f
                : Clock + Mathf.Min(Time.unscaledDeltaTime, .05f);
            // Render a deterministic 60 Hz simulation sequence in measured/capture runs.
            int frame = Benchmarking ? Frame : Mathf.FloorToInt(time * 60);
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            var produced = new PlayAbilities
            {
                Writer = World.Writer,
                Clock = time,
                Frame = frame,
                PreviousFrame = previousSimulationFrame,
                First = first,
                Hero = Hero,
                Scenario = Scenario,
                Count = Count,
                EffectCount = Catalog.EffectCount,
                PreviewEffect = Benchmarking && !capturing ? -1 : previewEffect,
                Released = previewReleased,
            }.Schedule(Count, 32);
            // One dependency chain and one completion boundary for all abilities.
            World.PreparePresentation(produced, time).Complete();
            CommandsMs =
                (System.Diagnostics.Stopwatch.GetTimestamp() - start)
                * 1000d
                / System.Diagnostics.Stopwatch.Frequency;
            start = System.Diagnostics.Stopwatch.GetTimestamp();
            // Managed Unity APIs begin here, after native state is ready to read.
            Backend.Present(World, time, first);
            BridgeMs =
                (System.Diagnostics.Stopwatch.GetTimestamp() - start)
                    * 1000d
                    / System.Diagnostics.Stopwatch.Frequency
                + Backend.CallbackMs;
            first = false;
            Clock = time;
            Frame++;
            previousSimulationFrame = frame;
            if (!Benchmarking)
            {
                viewCamera.transform.position = Vector3.Lerp(
                    viewCamera.transform.position,
                    cameraTargetPosition,
                    Time.unscaledDeltaTime * 3
                );
                var rot = Quaternion.LookRotation(cameraTargetLook - viewCamera.transform.position);
                viewCamera.transform.rotation = Quaternion.Slerp(
                    viewCamera.transform.rotation,
                    rot,
                    Time.unscaledDeltaTime * 3
                );
                hudClock += Time.unscaledDeltaTime;
                if (hudClock > .3f)
                {
                    hudClock = 0;
                    stats =
                        $"{Count:N0} abilities   /   {(Technique == Technique.Baseline ? 0 : (Full ? Catalog.AllocatedFullQuadsPerAbility : Catalog.AllocatedCoreQuadsPerAbility) * Count):N0} reserved quads\nCommands {CommandsMs:F3} ms   ·   Bridge {BridgeMs:F3} ms\nPreview only — use the standalone measured report.";
                }

                if (Keyboard.current?.f12Key.wasPressedThisFrame == true)
                {
                    Shot("Interactive-" + DateTime.Now.ToString("HHmmss"));
                }

                if (Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                {
                    Application.Quit();
                }
            }
        }

        public void SetFocus(int i, bool snap = false)
        {
            focus = i;
            cameraTargetLook = i < 0 ? new Vector3(0, 1, .5f) : new Vector3((i - 1) * 12, 1, .5f);
            cameraTargetPosition =
                i < 0 ? new Vector3(19, 28, -33) : cameraTargetLook + new Vector3(9.5f, 11, -14);
            if (Scenario == 3)
            {
                cameraTargetLook = new Vector3(0, 1, 0);
                cameraTargetPosition = new Vector3(95, 140, -165);
            }
            var presentationCamera = viewCamera ?? Camera.main;
            presentationCamera.farClipPlane =
                Scenario == 3 ? Mathf.Max(originalFarClipPlane, 500) : originalFarClipPlane;

            if (snap)
            {
                var c = viewCamera ?? Camera.main;
                c.transform.position = cameraTargetPosition;
                c.transform.LookAt(cameraTargetLook);
            }
        }

        void Shot(string name)
        {
            string root = Application.isEditor
                ? Directory.GetParent(Application.dataPath).FullName
                : Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
            string dir = Path.Combine(root, "Screenshots/Production");
            Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(Path.Combine(dir, name + ".png"));
        }

        IEnumerator Capture(string output)
        {
            output = Path.GetFullPath(output);
            Directory.CreateDirectory(output);
            Screen.SetResolution(1920, 1080, FullScreenMode.Windowed);
            yield return null;
            var check = new System.Collections.Generic.List<string>();
            foreach (Technique t in Enum.GetValues(typeof(Technique)))
            {
                Configure(t, 3, true, 0, true);
                SetFocus(-1, true);
                for (int j = 0; j < 150; j++)
                {
                    yield return null;
                }

                check.Add(
                    t
                        + ": "
                        + Backend.LiveParticles()
                        + " particles; "
                        + Backend.ObjectCount
                        + " objects; "
                        + Backend.Composition
                );
                ScreenCapture.CaptureScreenshot(Path.Combine(output, t + ".png"));
                yield return new WaitForEndOfFrame();
                yield return null;
            }

            for (int i = 0; i < Catalog.EffectCount; i++)
            {
                previewEffect = i;
                Configure(Technique.VfxBuffer, 3, true, 0, true);
                SetFocus(1, true);
                for (int j = 0; j < 120; j++)
                {
                    yield return null;
                }

                ScreenCapture.CaptureScreenshot(Path.Combine(output, "Hero" + i + ".png"));
                yield return new WaitForEndOfFrame();
                yield return null;
            }

            previewEffect = -1;
            Configure(Technique.VfxBuffer, 3, true, 0, true);
            SetFocus(-1, true);
            Benchmarking = false;
            for (int j = 0; j < 30; j++)
            {
                yield return null;
            }

            ScreenCapture.CaptureScreenshot(Path.Combine(output, "Showcase.png"));
            yield return new WaitForEndOfFrame();
            yield return null;
            File.WriteAllLines(Path.Combine(output, "validation.txt"), check);
            File.WriteAllText(Path.Combine(output, "COMPLETE.txt"), DateTime.UtcNow.ToString("O"));
            Application.Quit();
        }

        void Styles()
        {
            if (title != null)
            {
                return;
            }

            title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 46,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(.91f, .96f, .94f) },
            };
            eyebrow = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(.34f, 1, .76f) },
            };
            label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                wordWrap = true,
                normal = { textColor = new Color(.84f, .91f, .94f) },
            };
            small = new GUIStyle(label)
            {
                fontSize = 12,
                normal = { textColor = new Color(.58f, .70f, .75f) },
            };
            panel = new Texture2D(1, 1);
            panel.SetPixel(0, 0, new Color(.018f, .032f, .045f, .91f));
            panel.Apply();
            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(.065f, .10f, .13f, .94f));
            bg.Apply();
            button = new GUIStyle(GUI.skin.button)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                normal = { background = bg, textColor = new Color(.78f, .87f, .90f) },
                border = new RectOffset(0, 0, 0, 0),
            };
            active = new GUIStyle(button)
            {
                normal = { background = bg, textColor = new Color(.3f, 1, .75f) },
                fontStyle = FontStyle.Bold,
            };
        }

        bool Button(Rect r, string text, bool selected = false)
        {
            return GUI.Button(r, text, selected ? active : button);
        }

        void OnGUI()
        {
            if (Benchmarking || Backend == null)
            {
                return;
            }

            Styles();
            float scale = Screen.height / 1080f;
            float width = Screen.width / scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUI.Label(
                new Rect(44, 32, 700, 25),
                "AETHER    /    COMPLETE ABILITY FIELD STUDY",
                eyebrow
            );
            GUI.Label(new Rect(42, 60, 900, 70), "SIGNALS OF THE FRONTIER", title);
            GUI.Label(
                new Rect(45, 126, 750, 38),
                "Reusable effects. Authored layers. One unmanaged gameplay contract.",
                label
            );
            GUI.DrawTexture(new Rect(width - 360, 32, 316, 180), panel);
            GUI.Label(new Rect(width - 343, 47, 290, 25), "LIVE PRESENTATION", eyebrow);
            GUI.Label(new Rect(width - 343, 79, 280, 88), stats, small);
            GUI.DrawTexture(new Rect(32, 875, width - 64, 176), panel);
            if (Button(new Rect(48, 891, 118, 32), "DIORAMA", focus < 0))
            {
                previewEffect = -1;
                Configure(Technique, 3, Full, 0, true);
                SetFocus(-1);
            }

            string[] names = Catalog.EffectNames;
            effectPage = Math.Min(effectPage, (names.Length - 1) / 4);
            for (int visible = 0; visible < Math.Min(names.Length - effectPage * 4, 4); visible++)
            {
                int i = effectPage * 4 + visible;
                if (
                    Button(
                        new Rect(176 + visible * 148, 891, 138, 32),
                        names[i],
                        previewEffect == i
                    )
                )
                {
                    SetPreviewEffect(i);
                }
            }

            if (names.Length > 4 && Button(new Rect(1442, 891, 108, 32), "MORE EFFECTS"))
            {
                effectPage = (effectPage + 1) % ((names.Length + 3) / 4);
            }

            if (
                Button(
                    new Rect(1560, 891, 178, 32),
                    Library.name == "ArtistLibrary" ? "REFERENCE LIBRARY" : "ARTIST LIBRARY"
                )
            )
            {
                Library = Resources.Load<EffectLibrary>(
                    Library.name == "ArtistLibrary"
                        ? "Production/EffectLibrary"
                        : "Production/ArtistLibrary"
                );
                RebuildLibrary();
            }

            if (Button(new Rect(1748, 891, 118, 32), "RELEASE"))
            {
                ReleasePreview();
            }

            if (Button(new Rect(780, 891, 136, 32), "RESTART LINKS", Hero))
            {
                Configure(Technique, 3, Full, Scenario, true);
            }

            int[] counts = { 32, 128, 256, 512, 1024, 2500, 5000 };
            for (int i = 0; i < counts.Length; i++)
            {
                if (
                    Button(
                        new Rect(930 + i * 72, 891, 62, 32),
                        counts[i].ToString(),
                        !Hero && Count == counts[i]
                    )
                )
                {
                    Configure(Technique, counts[i], Full, Scenario);
                }
            }

            if (Button(new Rect(48, 935, 350, 34), TechniqueInfo.Names[(int)Technique]))
            {
                showMethods = !showMethods;
            }

            if (Button(new Rect(412, 935, 139, 34), "COMPLETE", Full))
            {
                Configure(Technique, Count, true, Scenario, Hero);
            }

            if (Button(new Rect(561, 935, 139, 34), "BEAM ONLY", !Full))
            {
                Configure(Technique, Count, false, Scenario, Hero);
            }

            string[] scenarios =
            {
                "SUSTAIN / MOVE",
                "CHARGE + RELEASE",
                "HEAVY OVERLAP",
                "SPREAD FIELD",
            };
            for (int i = 0; i < scenarios.Length; i++)
            {
                if (Button(new Rect(732 + i * 172, 935, 162, 34), scenarios[i], Scenario == i))
                {
                    Configure(Technique, Count, Full, i, Hero);
                }
            }

            if (Button(new Rect(width - 266, 935, 214, 34), "OPEN MEASURED REPORT"))
            {
                string root = Application.isEditor
                    ? Directory.GetParent(Application.dataPath).FullName
                    : Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
                Application.OpenURL(
                    new Uri(Path.Combine(root, "BenchmarkResults/authored/report.html")).AbsoluteUri
                );
            }

            GUI.Label(
                new Rect(48, 987, width - 100, 22),
                composition
                    + "    ·    "
                    + Catalog.Segments
                    + " segments / "
                    + Catalog.EffectCount
                    + " authored recipes / "
                    + Catalog.Fingerprint.Substring(0, 12),
                small
            );
            GUI.Label(
                new Rect(48, 1017, width - 100, 20),
                "Begin → Follow → Release from Burst jobs     /     Editable layers, curves, gradients and textures     /     F12 screenshot",
                small
            );
            if (showMethods)
            {
                GUI.DrawTexture(new Rect(32, 450, 400, 414), panel);
                for (int i = 0; i < TechniqueInfo.Names.Length; i++)
                {
                    if (
                        Button(
                            new Rect(46, 463 + i * 32, 370, 28),
                            TechniqueInfo.Names[i],
                            (int)Technique == i
                        )
                    )
                    {
                        Configure((Technique)i, Count, Full, Scenario, Hero);
                        showMethods = false;
                    }
                }
            }

            GUI.matrix = Matrix4x4.identity;
        }

        void OnDestroy()
        {
            Backend?.Dispose();
            World?.Dispose();
            Catalog?.Dispose();
            if (panel != null)
            {
                Destroy(panel);
            }
        }
    }
}
