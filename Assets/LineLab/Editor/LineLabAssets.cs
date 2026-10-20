using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;

namespace LineLab.Editor
{
    // VFX authoring API is internal in 17.3. Reflection is confined to asset creation;
    // the shipped runtime uses only supported Unity APIs and real compiled .vfx assets.
    public static class LineLabAssets
    {
        const BindingFlags Flags =
            BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static;

        static Type T(string name)
        {
            return AppDomain
                .CurrentDomain.GetAssemblies()
                .Select(a => a.GetType(name))
                .First(t => t != null);
        }

        static object Call(object target, string method, params object[] args)
        {
            var type = target as Type ?? target.GetType();
            var candidates = type.GetMethods(Flags)
                .Where(m =>
                    m.Name == method
                    && m.GetParameters().Length >= args.Length
                    && m.GetParameters().Skip(args.Length).All(p => p.IsOptional)
                )
                .ToArray();
            foreach (var m in candidates)
            {
                var p = m.GetParameters();
                if (
                    args.Where((a, i) => a != null && !p[i].ParameterType.IsInstanceOfType(a)).Any()
                )
                {
                    continue;
                }

                var filled = args.Concat(p.Skip(args.Length).Select(x => x.DefaultValue)).ToArray();
                try
                {
                    return m.Invoke(target is Type ? null : target, filled);
                }
                catch (TargetInvocationException e)
                {
                    throw e.InnerException ?? e;
                }
            }

            throw new MissingMethodException(
                type.FullName,
                method + " (" + string.Join(",", args.Select(a => a?.GetType().Name)) + ")"
            );
        }

        static object Get(object target, string property)
        {
            return target
                .GetType()
                .GetProperties(Flags)
                .First(p => p.Name == property && p.GetIndexParameters().Length == 0)
                .GetValue(target);
        }

        static void Set(object target, string property, object value)
        {
            target.GetType().GetProperty(property, Flags).SetValue(target, value);
        }

        static object Node(string name)
        {
            return ScriptableObject.CreateInstance(T("UnityEditor.VFX." + name));
        }

        static void Setting(object o, string name, object value)
        {
            Call(o, "SetSettingValue", name, value);
        }

        static object Slot(object o, string name)
        {
            return ((IEnumerable)Get(o, "inputSlots"))
                .Cast<object>()
                .First(s =>
                    ((string)Get(s, "name") == name || (string)Get(s, "name") == "_" + name)
                );
        }

        static void Value(object o, string name, object value)
        {
            Set(Slot(o, name), "value", value);
        }

        static object Parameter(object graph, string name, Type type, object value, Vector2 pos)
        {
            var p = Node("VFXParameter");
            Call(p, "Init", type);
            Setting(p, "m_ExposedName", name);
            Setting(p, "m_Exposed", true);
            if (value != null)
            {
                Set(p, "value", value);
            }

            Set(p, "position", pos);
            Call(graph, "AddChild", p);
            Call(p, "AddNode", pos);
            return p;
        }

        static void Connect(object target, string slot, object parameter)
        {
            Call(Slot(target, slot), "Link", Call(parameter, "GetOutputSlot", 0));
        }

        [MenuItem("Line Lab/1 - Generate assets and scenes")]
        public static void Generate()
        {
            Directory.CreateDirectory("Assets/LineLab/Resources");
            CreateMaterials();
            CreateGraph(true);
            CreateGraph(false);
            CreateCapacityVariants();
            CreateShowcaseMotes();
            AssetDatabase.SaveAssets();
            CreateScene("01_VFX_Showcase", true);
            CreateScene("02_Technique_Lab", false);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene("Assets/LineLab/Scenes/01_VFX_Showcase.unity", true),
                new EditorBuildSettingsScene("Assets/LineLab/Scenes/02_Technique_Lab.unity", true),
            };
            PlayerSettings.companyName = "Line Laboratory";
            PlayerSettings.productName = "RTS Line Laboratory";
            PlayerSettings.runInBackground = true;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.enableFrameTimingStats = true;
            EditorSceneManager.OpenScene("Assets/LineLab/Scenes/01_VFX_Showcase.unity");
            Debug.Log("Line Lab assets, VFX graphs, and two scenes generated.");
        }

        public static string QueueGenerate()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Generate();
                    File.WriteAllText("BenchmarkResults/generation-status.txt", "SUCCESS");
                }
                catch (Exception e)
                {
                    File.WriteAllText("BenchmarkResults/generation-status.txt", e.ToString());
                    Debug.LogException(e);
                }
            };
            return "Generation queued";
        }

        static void Save(UnityEngine.Object asset, string name)
        {
            string path = "Assets/LineLab/Resources/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(asset, existing);
                UnityEngine.Object.DestroyImmediate(asset);
            }
            else
            {
                AssetDatabase.CreateAsset(asset, path);
            }
        }

        static void CreateMaterials()
        {
            var arena = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            arena.EnableKeyword("_EMISSION");
            arena.SetColor("_EmissionColor", new Color(.01f, .01f, .01f));
            Save(arena, "ArenaBaseMaterial");
            var tex = new Texture2D(16, 128, TextureFormat.RGBAHalf, false, true)
            {
                name = "HDR beam cross section",
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 16; x++)
                {
                    float d = Mathf.Abs((y + .5f) / 128 * 2 - 1);
                    float a = Mathf.Exp(-d * d * 9) * .65f + Mathf.Exp(-d * d * 130) * .35f;
                    tex.SetPixel(x, y, new Color(2.8f, 2.8f, 2.8f, a));
                }
            }

            tex.Apply();
            Save(tex, "BeamProfile");
            var quad = new Mesh { name = "Segment quad" };
            quad.vertices = new[]
            {
                new Vector3(-.5f, -.5f, 0),
                new Vector3(.5f, -.5f, 0),
                new Vector3(.5f, .5f, 0),
                new Vector3(-.5f, .5f, 0),
            };
            quad.uv = new[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1),
            };
            quad.colors32 = Enumerable.Repeat(new Color32(255, 255, 255, 255), 4).ToArray();
            quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            quad.RecalculateBounds();
            Save(quad, "SegmentQuad");
            MakeMaterial("RibbonMaterial", "LineLab/Ribbon");
            MakeMaterial("ProceduralMaterial", "LineLab/ProceduralRibbon");
            MakeMaterial("ParticleInstancedMaterial", "LineLab/ParticleInstanced");
            void MakeMaterial(string name, string shader)
            {
                var m = new Material(Shader.Find(shader))
                {
                    name = name,
                    enableInstancing = name.Contains("Instanced"),
                };
                m.SetTexture(
                    "_MainTex",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        "Assets/LineLab/Resources/BeamProfile.asset"
                    )
                );
                Save(m, name);
            }
        }

        static void CreateGraph(bool shared)
        {
            string path =
                "Assets/LineLab/Resources/" + (shared ? "LinesBuffer" : "LineSingle") + ".vfx";
            if (File.Exists(path))
            {
                AssetDatabase.DeleteAsset(path);
            }

            var asset = Call(
                T("UnityEditor.VisualEffectAssetEditorUtility"),
                "CreateNewAsset",
                path
            );
            var resource = Call(
                T("UnityEditor.VFX.VisualEffectObjectExtensions"),
                "GetResource",
                asset
            );
            var graph = Call(
                T("UnityEditor.VFX.VisualEffectResourceExtensions"),
                "GetOrCreateGraph",
                resource
            );
            var instancing = resource.GetType().GetProperty("instancingMode", Flags);
            instancing.SetValue(resource, Enum.Parse(instancing.PropertyType, "Disabled"));
            var spawn = Node("VFXBasicSpawner");
            var init = Node("VFXBasicInitialize");
            var update = Node("VFXBasicUpdate");
            var output = Node("VFXPlanarPrimitiveOutput");
            object[] contexts = { spawn, init, update, output };
            for (int i = 0; i < contexts.Length; i++)
            {
                Set(contexts[i], "position", new Vector2(450, i * 250));
                Call(graph, "AddChild", contexts[i]);
            }

            Call(init, "LinkFrom", spawn);
            Call(update, "LinkFrom", init);
            Call(output, "LinkFrom", update);
            Setting(init, "capacity", shared ? 262144u : 64u);
            Setting(update, "ageParticles", false);
            Setting(update, "reapParticles", false);
            Setting(
                update,
                "integration",
                Enum.Parse(T("UnityEditor.VFX.VFXBasicUpdate+VFXIntegrationMode"), "None")
            );
            Setting(
                output,
                "blendMode",
                Enum.ToObject(T("UnityEditor.VFX.VFXAbstractRenderedOutput+BlendMode"), 0)
            );
            var burst = Node("VFXSpawnerBurst");
            Call(spawn, "AddChild", burst);
            var spawnCount = Parameter(graph, "SpawnCount", typeof(float), 16f, new Vector2(0, 0));
            Connect(burst, "Count", spawnCount);
            Value(
                output,
                "mainTexture",
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/LineLab/Resources/BeamProfile.asset"
                )
            );
            var bounds = Slot(init, "bounds");
            var box = Get(bounds, "value");
            var boxType = box.GetType();
            boxType.GetField("size").SetValue(box, new Vector3(200, 100, 200));
            Set(bounds, "value", box);
            var custom = Node("Block.CustomHLSL");
            Call(update, "AddChild", custom);
            string args = shared
                ? "in StructuredBuffer<float4> Lines, "
                : "in float4 Start, in float4 End, in float4 Tint, in float4 Shape, ";
            string load = shared
                ? @"

uint index = (attributes.particleId / Segments) * 4;
l.start = Lines[index];
l.end = Lines[index + 1];
l.color = Lines[index + 2];
l.shape = Lines[index + 3];"
                : @"
l.start = Start;
l.end = End;
l.color = Tint;
l.shape = Shape;";
            string code =
                "#include \"Assets/LineLab/Shaders/LineCommon.hlsl\"\nvoid DrawLine(inout VFXAttributes attributes, "
                + args
                + "in uint Segments, in float Clock, in float3 CameraPosition)\n{\n"
                + "LabLine l; "
                + load
                + @"

uint segment = attributes.particleId % Segments;
float t = (segment + .5) / Segments;
"
                + "float3 a=LabPoint(l,(float)segment/Segments,Clock); float3 b=LabPoint(l,(float)(segment+1)/Segments,Clock); float3 mid=(a+b)*.5;\n"
                + "float3 tangent=normalize(b-a+float3(1e-7,0,0)); float3 side=normalize(cross(normalize(CameraPosition-mid),tangent)); float4 color=LabTint(l,t,Clock);\n"
                + @"

attributes.position = mid;
attributes.axisX = tangent;
attributes.axisY = side;
attributes.axisZ = normalize(cross(tangent, side));
"
                + @"

attributes.size = 1;
attributes.scaleX = distance(a, b) * 1.035;
attributes.scaleY = l.end.w;
attributes.scaleZ = 1;
attributes.color = color.rgb;
attributes.alpha = color.a;
}";
            Set(custom, "sourceCode", code);
            var segments = Parameter(graph, "Segments", typeof(uint), 16u, new Vector2(0, 150));
            Connect(custom, "Segments", segments);
            var clock = Parameter(graph, "Clock", typeof(float), 0f, new Vector2(0, 250));
            Connect(custom, "Clock", clock);
            var camera = Parameter(
                graph,
                "CameraPosition",
                typeof(Vector3),
                new Vector3(0, 40, -35),
                new Vector2(0, 350)
            );
            Connect(custom, "CameraPosition", camera);
            if (shared)
            {
                Connect(
                    custom,
                    "Lines",
                    Parameter(graph, "Lines", typeof(GraphicsBuffer), null, new Vector2(0, 450))
                );
            }
            else
            {
                foreach (string name in new[] { "Start", "End", "Tint", "Shape" })
                {
                    Connect(
                        custom,
                        name,
                        Parameter(
                            graph,
                            name,
                            typeof(Vector4),
                            name == "Tint" ? Vector4.one
                                : name == "End" ? new Vector4(3, 1, 0, .3f)
                                : new Vector4(-3, 1, 0, 1),
                            new Vector2(
                                -250,
                                450
                                    + Array.IndexOf(new[] { "Start", "End", "Tint", "Shape" }, name)
                                        * 100
                            )
                        )
                    );
                }
            }

            Call(graph, "UpdateSubAssets");
            Call(resource, "WriteAsset");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        public static void CreateCapacityVariants()
        {
            foreach (bool shared in new[] { true, false })
            {
                for (
                    int capacity = shared ? 16 : 8;
                    capacity <= (shared ? 262144 : 32);
                    capacity *= 2
                )
                {
                    foreach (bool batched in shared ? new[] { false } : new[] { false, true })
                    {
                        string original =
                            "Assets/LineLab/Resources/"
                            + (shared ? "LinesBuffer" : "LineSingle")
                            + ".vfx";
                        string path =
                            "Assets/LineLab/Resources/"
                            + (
                                shared ? "LinesBuffer"
                                : batched ? "LineSingleBatched"
                                : "LineSingle"
                            )
                            + "_"
                            + capacity
                            + ".vfx";
                        if (File.Exists(path))
                        {
                            AssetDatabase.DeleteAsset(path);
                        }

                        AssetDatabase.CopyAsset(original, path);
                        var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path);
                        var resource = Call(
                            T("UnityEditor.VFX.VisualEffectObjectExtensions"),
                            "GetResource",
                            asset
                        );
                        var graph = Call(
                            T("UnityEditor.VFX.VisualEffectResourceExtensions"),
                            "GetOrCreateGraph",
                            resource
                        );
                        var init = ((IEnumerable)Get(graph, "children"))
                            .Cast<object>()
                            .First(n => n.GetType().Name == "VFXBasicInitialize");
                        Setting(init, "capacity", (uint)capacity);
                        if (batched)
                        {
                            var property = resource.GetType().GetProperty("instancingMode", Flags);
                            property.SetValue(resource, Enum.Parse(property.PropertyType, "Auto"));
                        }

                        Call(graph, "UpdateSubAssets");
                        Call(resource, "WriteAsset");
                        AssetDatabase.ImportAsset(path);
                    }
                }
            }
        }

        public static void CreateShowcaseMotes()
        {
            var tex = new Texture2D(64, 64, TextureFormat.RGBAHalf, false, true)
            {
                name = "Energy mote profile",
                wrapMode = TextureWrapMode.Clamp,
            };
            for (int y = 0; y < 64; y++)
            {
                for (int x = 0; x < 64; x++)
                {
                    float dx = (x + .5f) / 32 - 1;
                    float dy = (y + .5f) / 32 - 1;
                    float r = dx * dx + dy * dy;
                    float a = Mathf.Exp(-r * 5) * .15f + Mathf.Exp(-r * 32) * .85f;
                    tex.SetPixel(x, y, new Color(4, 4, 4, a));
                }
            }

            tex.Apply();
            Save(tex, "MoteProfile");
            const string path = "Assets/LineLab/Resources/ShowcaseMotes.vfx";
            if (File.Exists(path))
            {
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.CopyAsset("Assets/LineLab/Resources/LinesBuffer_512.vfx", path);
            var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path);
            var resource = Call(
                T("UnityEditor.VFX.VisualEffectObjectExtensions"),
                "GetResource",
                asset
            );
            var graph = Call(
                T("UnityEditor.VFX.VisualEffectResourceExtensions"),
                "GetOrCreateGraph",
                resource
            );
            var contexts = ((IEnumerable)Get(graph, "children")).Cast<object>().ToArray();
            var update = contexts.First(n => n.GetType().Name == "VFXBasicUpdate");
            var block = ((IEnumerable)Get(update, "children"))
                .Cast<object>()
                .First(n => n.GetType().Name == "CustomHLSL");
            string code =
                "#include \"Assets/LineLab/Shaders/LineCommon.hlsl\"\nvoid DrawMotes(inout VFXAttributes attributes, in StructuredBuffer<float4> Lines, in uint Segments, in float Clock, in float3 CameraPosition)\n{\n"
                + @"

uint index = (attributes.particleId / Segments) * 4;
uint mote = attributes.particleId % Segments;
LabLine l;
l.start = Lines[index];
l.end = Lines[index + 1];
l.color = Lines[index + 2];
l.shape = Lines[index + 3];
"
                + "float phase=mote*.6180339; float t=frac(phase+Clock*(.19+l.shape.x*.07)); float3 p=LabPoint(l,t,Clock); float e=sin(t*3.14159265); float radius=(.07+frac(mote*.371)*.25)*e;\n"
                + "p.y+=sin(phase*31+Clock*4)*radius; p.z+=cos(phase*23+Clock*3)*radius; float size=.10+frac(mote*.17)*.12;\n"
                + @"
if (mote < 4)
{
    p = mote < 2 ? l.start.xyz : l.end.xyz;
    size = mote % 2 == 0 ? .9 : .4;
}
"
                + "float3 view=normalize(CameraPosition-p); float3 right=normalize(cross(float3(0,1,0),view));\n"
                + @"

attributes.position = p;
attributes.axisX = right;
attributes.axisY = cross(view, right);
attributes.axisZ = view;
attributes.size = size;
attributes.scaleX = 1;
attributes.scaleY = 1;
attributes.scaleZ = 1;
attributes.color = l.color.rgb;
attributes.alpha = l.start.w * .8;
}";
            Set(block, "sourceCode", code);
            Value(
                contexts.First(n => n.GetType().Name == "VFXPlanarPrimitiveOutput"),
                "mainTexture",
                AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Assets/LineLab/Resources/MoteProfile.asset"
                )
            );
            // The replacement function keeps the same input names, so existing exposed links remain.
            Call(graph, "UpdateSubAssets");
            Call(resource, "WriteAsset");
            AssetDatabase.ImportAsset(path);
        }

        static void CreateScene(string name, bool gallery)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraGo = new GameObject("RTS Camera", typeof(Camera), typeof(AudioListener));
            var camera = cameraGo.GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 43, -36);
            camera.transform.rotation = Quaternion.Euler(51, 0, 0);
            camera.orthographic = true;
            camera.orthographicSize = 23;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.012f, .019f, .033f);
            camera.allowHDR = true;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var volume = new GameObject(
                "Atmosphere / bloom",
                typeof(Volume)
            ).GetComponent<Volume>();
            volume.isGlobal = true;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(.7f);
            bloom.threshold.Override(.65f);
            bloom.scatter.Override(.65f);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(.28f);
            vignette.smoothness.Override(.8f);
            string profilePath = "Assets/LineLab/Resources/" + name + "Volume.asset";
            if (AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath) != null)
            {
                AssetDatabase.DeleteAsset(profilePath);
            }

            AssetDatabase.CreateAsset(profile, profilePath);
            AssetDatabase.AddObjectToAsset(bloom, profile);
            AssetDatabase.AddObjectToAsset(vignette, profile);
            volume.sharedProfile = profile;
            var light = new GameObject("Key / moonlight", typeof(Light)).GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(.65f, .8f, 1);
            light.intensity = 1.8f;
            light.transform.rotation = Quaternion.Euler(48, -35, 0);
            light.shadows = LightShadows.None;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.18f, .23f, .32f);
            var lab = new GameObject(
                "RTS Line Laboratory",
                typeof(LineLabController)
            ).GetComponent<LineLabController>();
            lab.Gallery = gallery;
            EditorSceneManager.SaveScene(scene, "Assets/LineLab/Scenes/" + name + ".unity");
        }

        [MenuItem("Line Lab/2 - Build Windows player")]
        public static void Build()
        {
            Directory.CreateDirectory("Builds/LineLab");
            var result = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = EditorBuildSettings.scenes.Select(s => s.path).ToArray(),
                    locationPathName = "Builds/LineLab/LineLab.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                }
            );
            File.WriteAllText(
                "Builds/build-status.txt",
                result.summary.result
                    + " / "
                    + result.summary.totalErrors
                    + " errors / "
                    + result.summary.totalTime
            );
        }

        public static string QueueBuild()
        {
            EditorApplication.delayCall += Build;
            return "Build queued";
        }

        public static void RefreshPresentation()
        {
            CreateMaterials();
            CreateScene("01_VFX_Showcase", true);
            CreateScene("02_Technique_Lab", false);
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene("Assets/LineLab/Scenes/01_VFX_Showcase.unity");
        }
    }
}
