using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace LineLab
{
    // Host for scenes 01/02: controls, placeholder actors, native demo workload
    // and presentation. The ECS integration uses LineWriter instead of this host.
    public sealed class LineLabController : MonoBehaviour
    {
        public bool Gallery = true;
        public bool GalleryLayout = true;
        public Technique Selected = Technique.VfxBuffer;
        public int LineCount = 8;
        public int Segments = 32;
        public Workload Workload = Workload.Moving;
        public LineWorld World { get; private set; }
        public LineBackend Backend { get; private set; }
        public double CommandMs { get; private set; }
        public double PresentMs { get; private set; }
        public double SetupMs { get; private set; }
        public double TeardownMs { get; private set; }
        public bool Benchmarking { get; set; }

        public string Status = "Live / unmanaged commands → presentation bridge";
        public float SimulationClock { get; private set; }
        public int SimulationFrame { get; private set; }

        bool first;
        Transform[] actors;
        GameObject actorRoot;
        GUIStyle title;
        GUIStyle heading;
        GUIStyle text;
        GUIStyle small;
        GUIStyle button;
        GUIStyle chosen;
        Texture2D panelTexture;
        float smoothMs;
        bool initializedGui;
        float lastGuiUpdate;
        string hudFrame = "";

        void Start()
        {
            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            if (GameObject.Find("Arena") == null)
            {
                LabArena.Create();
            }

            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "--benchmark") >= 0)
            {
                Benchmarking = true;
                GalleryLayout = false;
                StartCoroutine(LineBenchmark.Run(this, args));
            }
            else if (Array.IndexOf(args, "--visuals") >= 0)
            {
                StartCoroutine(LineBenchmark.Visuals(this, "Screenshots/Backends"));
            }
            else
            {
                Configure(Selected, LineCount, Segments, Workload, GalleryLayout);
            }
        }

        public void Configure(
            Technique technique,
            int count,
            int segments,
            Workload workload,
            bool gallery
        )
        {
            var watch = Stopwatch.StartNew();
            Backend?.Dispose();
            Backend = null;
            World?.Dispose();
            World = null;
            if (actorRoot != null)
            {
                Destroy(actorRoot);
            }

            actors = null;
            TeardownMs = watch.Elapsed.TotalMilliseconds;
            watch.Restart();
            Selected = technique;
            LineCount = count;
            Segments = segments;
            Workload = workload;
            GalleryLayout = gallery;
            Application.targetFrameRate = Benchmarking ? -1 : 60;
            World = new LineWorld(count);
            Backend = new LineBackend(
                technique,
                count,
                segments,
                Camera.main,
                Gallery && gallery && !Benchmarking
            );
            SimulationClock = 0;
            SimulationFrame = 0;
            first = true;
            SetupMs = watch.Elapsed.TotalMilliseconds;
            if (gallery)
            {
                CreateActors();
            }
        }

        void Update()
        {
            if (World == null)
            {
                return;
            }

            long startTicks = Stopwatch.GetTimestamp();
            // Fixed deterministic time in benchmarks. No backend gets a different path or speed.
            SimulationClock = Benchmarking
                ? SimulationFrame / 60f
                : SimulationClock + Time.unscaledDeltaTime;
            var handle = new DemoCommandsJob
            {
                Writer = World.Writer,
                Count = LineCount,
                Clock = SimulationClock,
                Frame = SimulationFrame,
                First = first,
                Gallery = GalleryLayout,
                Workload = Workload,
            }.Schedule(LineCount, 32);
            World.Consume(handle).Complete();
            CommandMs = (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency;
            startTicks = Stopwatch.GetTimestamp();
            float clock = Workload == Workload.Static ? 0 : SimulationClock;
            Backend.Present(World.Lines, clock, first, Workload != Workload.Static);
            // Particle callbacks run later in the engine frame. Include the last
            // completed callback duration in the aggregate bridge metric.
            PresentMs =
                (Stopwatch.GetTimestamp() - startTicks) * 1000d / Stopwatch.Frequency
                + Backend.ParticleCallbackMs;
            if (actors != null)
            {
                for (int i = 0; i < LineCount; i++)
                {
                    var l = World.Lines[i];
                    actors[i * 2].position = (Vector3)l.Start.xyz - Vector3.up * 1.5f;
                    actors[i * 2 + 1].position = (Vector3)l.End.xyz - Vector3.up * 1.5f;
                }
            }

            first = false;
            SimulationFrame++;
            smoothMs = Mathf.Lerp(smoothMs, Time.unscaledDeltaTime * 1000, .04f);
            if (!Benchmarking && Time.unscaledTime > lastGuiUpdate + .25f)
            {
                hudFrame =
                    $"{smoothMs:0.00} ms/frame   ·   {1000 / Mathf.Max(.001f, smoothMs):0} fps\nCommands {CommandMs:0.000} ms   /   Bridge {PresentMs:0.000} ms\n{Backend.ObjectCount:N0} presentation objects   ·   {LineCount * Segments:N0} segments";
                lastGuiUpdate = Time.unscaledTime;
            }

            if (!Benchmarking && Keyboard.current != null)
            {
                if (Keyboard.current.tabKey.wasPressedThisFrame)
                {
                    SceneManager.LoadScene(Gallery ? 1 : 0);
                }

                if (Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    Application.Quit();
                }

                if (Keyboard.current.f12Key.wasPressedThisFrame)
                {
                    Directory.CreateDirectory("Screenshots");
                    ScreenCapture.CaptureScreenshot(
                        Path.GetFullPath("Screenshots/LineLab-" + Selected + ".png")
                    );
                }
            }
        }

        void CreateActors()
        {
            actorRoot = new GameObject("Placeholder units / presentation only");
            actors = new Transform[LineCount * 2];
            for (int i = 0; i < LineCount; i++)
            {
                Color color =
                    i % 4 == 0 ? new Color(.1f, 1, .6f)
                    : i % 4 == 1 ? new Color(.2f, .5f, 1)
                    : i % 4 == 2 ? new Color(1, .3f, .1f)
                    : new Color(.7f, .3f, 1);
                for (int k = 0; k < 2; k++)
                {
                    actors[i * 2 + k] = LabArena.Unit(actorRoot.transform, color, k == 0);
                }
            }
        }

        void InitGui()
        {
            if (initializedGui)
            {
                return;
            }

            initializedGui = true;
            title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 29,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white },
                wordWrap = true,
            };
            heading = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(.26f, .91f, .78f) },
            };
            text = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                wordWrap = true,
                normal = { textColor = new Color(.77f, .83f, .9f) },
            };
            small = new GUIStyle(text) { fontSize = 11 };
            button = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 12,
                padding = new RectOffset(12, 8, 4, 4),
                normal = { textColor = new Color(.75f, .83f, .92f) },
            };
            var buttonBackground = new Texture2D(1, 1);
            buttonBackground.SetPixel(0, 0, new Color(.065f, .12f, .17f));
            buttonBackground.Apply();
            var hoverBackground = new Texture2D(1, 1);
            hoverBackground.SetPixel(0, 0, new Color(.10f, .22f, .28f));
            hoverBackground.Apply();
            button.normal.background = buttonBackground;
            button.hover.background = hoverBackground;
            button.active.background = hoverBackground;
            chosen = new GUIStyle(button)
            {
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(.2f, 1, .78f) },
            };
            panelTexture = new Texture2D(1, 1);
            panelTexture.SetPixel(0, 0, new Color(.025f, .045f, .073f, .96f));
            panelTexture.Apply();
        }

        void OnGUI()
        {
            if (Benchmarking)
            {
                return;
            }

            InitGui();
            float scale = Screen.height / 1080f;
            GUI.matrix = Matrix4x4.TRS(
                Vector3.zero,
                Quaternion.identity,
                new Vector3(scale, scale, 1)
            );
            float width = Screen.width / scale;
            GUI.DrawTexture(new Rect(24, 24, 318, 1032), panelTexture);
            GUILayout.BeginArea(new Rect(44, 43, 278, 990));
            GUILayout.Label("FIELD STUDY    /    001", heading);
            GUILayout.Space(10);
            GUILayout.Label(Gallery ? "VFX GRAPH\nLINE ATLAS" : "RTS LINE\nLABORATORY", title);
            GUILayout.Space(13);
            GUILayout.Label("HEAL  /  ARC  /  BEAM  /  COMMAND", small);
            GUILayout.Space(17);
            if (
                GUILayout.Button(
                    Gallery ? "OPEN TECHNIQUE COMPARISON   →" : "←   OPEN VFX SHOWCASE",
                    button,
                    GUILayout.Height(34)
                )
            )
            {
                SceneManager.LoadScene(Gallery ? 1 : 0);
            }

            GUILayout.Space(18);
            GUILayout.Label("RENDERING TECHNIQUE", heading);
            if (Gallery)
            {
                GUILayout.Label(
                    "VFX Graph / shared graphics buffer\n32 segments per line, computed on the GPU.",
                    text
                );
            }
            else
            {
                for (int i = 1; i < TechniqueInfo.Names.Length; i++)
                {
                    var t = (Technique)i;
                    if (
                        GUILayout.Button(
                            TechniqueInfo.Names[i],
                            Selected == t ? chosen : button,
                            GUILayout.Height(29)
                        )
                        && Selected != t
                    )
                    {
                        Configure(t, LineCount, Segments, Workload, GalleryLayout);
                    }
                }
            }

            GUILayout.Space(18);
            GUILayout.Label("WORKLOAD", heading);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("ATLAS", GalleryLayout ? chosen : button, GUILayout.Height(30)))
            {
                Configure(Selected, 8, 32, Workload.Moving, true);
            }

            if (
                GUILayout.Button(
                    "256 LINES",
                    !GalleryLayout ? chosen : button,
                    GUILayout.Height(30)
                )
            )
            {
                Configure(Selected, 256, 16, Workload.Moving, false);
            }

            GUILayout.EndHorizontal();
            if (!GalleryLayout)
            {
                GUILayout.BeginHorizontal();
                foreach (int count in new[] { 128, 512, 1024 })
                {
                    if (GUILayout.Button(count.ToString(), button))
                    {
                        Configure(Selected, count, 16, Workload, false);
                    }
                }

                GUILayout.EndHorizontal();
                foreach (Workload w in Enum.GetValues(typeof(Workload)))
                {
                    if (
                        GUILayout.Button(
                            w.ToString(),
                            Workload == w ? chosen : button,
                            GUILayout.Height(24)
                        )
                    )
                    {
                        Configure(Selected, LineCount, Segments, w, false);
                    }
                }
            }

            GUILayout.Space(15);
            GUILayout.Label("LIVE EDITOR / PLAYER PREVIEW", heading);
            GUILayout.Label(hudFrame, small);
            GUILayout.Space(12);
            GUILayout.Label(
                "Preview timings are diagnostic. Use the standalone benchmark results for comparisons.",
                small
            );
            GUILayout.Space(12);
            if (GUILayout.Button("OPEN MEASURED RESULTS   ↗", button, GUILayout.Height(30)))
            {
                string root = Application.isEditor
                    ? Directory.GetParent(Application.dataPath).FullName
                    : Path.GetFullPath(Path.Combine(Application.dataPath, "../../.."));
                Application.OpenURL(
                    new Uri(Path.Combine(root, "BenchmarkResults/report.html")).AbsoluteUri
                );
            }

            GUILayout.FlexibleSpace();
            GUILayout.Label(
                "TAB  Switch scene    F12  Screenshot\nUnmanaged commands · Burst jobs · URP 17.3",
                small
            );
            GUILayout.EndArea();
            GUI.Label(new Rect(width - 500, 37, 450, 35), "RTS  /  SIGNAL & CONNECTIONS", heading);
            if (GalleryLayout)
            {
                string[] labels =
                {
                    "01  /  RESTORATION",
                    "02  /  VOLTAIC ARC",
                    "03  /  PLASMA LINK",
                    "04  /  TACTICAL ORDER",
                };
                for (int i = 0; i < 4; i++)
                {
                    Vector3 p = Camera.main.WorldToScreenPoint(
                        new Vector3((i - 1.5f) * 13, 0, -10)
                    );
                    GUI.Label(
                        new Rect(p.x / scale - 90, (Screen.height - p.y) / scale + 14, 220, 30),
                        labels[i],
                        heading
                    );
                }
            }

            GUI.matrix = Matrix4x4.identity;
        }

        void OnDestroy()
        {
            Backend?.Dispose();
            World?.Dispose();
            if (panelTexture != null)
            {
                Destroy(panelTexture);
            }
        }
    }

    public static class LabArena
    {
        static Material floor;
        static Material edge;
        static Material body;
        static Material visor;
        static Material trim;

        static Material Material(string name, Color color, bool glow = false)
        {
            var m = new Material(Resources.Load<Material>("ArenaBaseMaterial")) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", .48f);
            m.SetFloat("_Metallic", .45f);
            if (glow)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", color * 2);
            }

            return m;
        }

        static Transform Shape(
            Transform parent,
            string name,
            PrimitiveType type,
            Vector3 position,
            Vector3 scale,
            Material material
        )
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            var collider = go.GetComponent<Collider>();
            if (collider != null)
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            go.GetComponent<Renderer>().sharedMaterial = material;
            return go.transform;
        }

        public static void Create()
        {
            floor = Material("Basalt / satin", new Color(.045f, .065f, .095f));
            edge = Material("Inlaid teal", new Color(.025f, .2f, .22f), true);
            body = Material("Unit / ceramic", new Color(.38f, .47f, .57f));
            visor = Material("Unit / obsidian", new Color(.018f, .035f, .055f));
            trim = Material("Unit / status", new Color(.1f, .75f, .6f), true);
            var root = new GameObject("Arena").transform;
            Shape(
                root,
                "Foundation",
                PrimitiveType.Cube,
                new Vector3(0, -.45f, 0),
                new Vector3(58, .7f, 38),
                floor
            );
            for (int x = -27; x <= 27; x += 3)
            {
                Shape(
                    root,
                    "Grid / longitudinal",
                    PrimitiveType.Cube,
                    new Vector3(x, -.085f, 0),
                    new Vector3(.018f, .012f, 36),
                    edge
                );
            }

            for (int z = -18; z <= 18; z += 3)
            {
                Shape(
                    root,
                    "Grid / transverse",
                    PrimitiveType.Cube,
                    new Vector3(0, -.085f, z),
                    new Vector3(56, .012f, .018f),
                    edge
                );
            }

            for (int i = 0; i < 4; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    Vector3 p = new Vector3((i - 1.5f) * 13, -.04f, (j - .5f) * 13);
                    Shape(
                        root,
                        "Demonstration plinth",
                        PrimitiveType.Cube,
                        p,
                        new Vector3(11.4f, .16f, 9.5f),
                        floor
                    );
                    Shape(
                        root,
                        "Status inset",
                        PrimitiveType.Cube,
                        p + new Vector3(0, .09f, -4.73f),
                        new Vector3(10.6f, .02f, .04f),
                        edge
                    );
                }
            }
        }

        public static Transform Unit(Transform parent, Color color, bool source)
        {
            if (body == null)
            {
                body = Material("Unit / ceramic", new Color(.38f, .47f, .57f));
                visor = Material("Unit / obsidian", new Color(.018f, .035f, .055f));
            }

            var glow = Material("Ability / status", color, true);
            var root = new GameObject(source ? "Caster" : "Target").transform;
            root.SetParent(parent);
            Shape(
                root,
                "Footplate",
                PrimitiveType.Cylinder,
                new Vector3(0, .07f, 0),
                new Vector3(1.6f, .07f, 1.6f),
                glow
            );
            Shape(
                root,
                "Body",
                PrimitiveType.Capsule,
                new Vector3(0, .98f, 0),
                new Vector3(.78f, .78f, .78f),
                body
            );
            Shape(
                root,
                "Helmet",
                PrimitiveType.Sphere,
                new Vector3(0, 1.91f, 0),
                new Vector3(.64f, .57f, .64f),
                visor
            );
            Shape(
                root,
                "Visor",
                PrimitiveType.Cube,
                new Vector3(0, 1.96f, -.28f),
                new Vector3(.46f, .10f, .12f),
                glow
            );
            Shape(
                root,
                "Shoulder / L",
                PrimitiveType.Cube,
                new Vector3(-.54f, 1.45f, 0),
                new Vector3(.4f, .45f, .7f),
                body
            );
            Shape(
                root,
                "Shoulder / R",
                PrimitiveType.Cube,
                new Vector3(.54f, 1.45f, 0),
                new Vector3(.4f, .45f, .7f),
                body
            );
            return root;
        }
    }
}
