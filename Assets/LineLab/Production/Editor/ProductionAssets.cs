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

namespace LineLab.Production.Editor
{
    // Editor-only authoring: textures, meshes, materials, five-system VFX graphs,
    // the outpost scene and its standalone build. Never called by gameplay jobs.
    public static class ProductionAssets
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

        const string Root = "Assets/LineLab/Resources/Production/";

        public static string QueueGenerate()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Generate();
                    File.WriteAllText("BenchmarkResults/production-generation.txt", "SUCCESS");
                }
                catch (Exception e)
                {
                    File.WriteAllText("BenchmarkResults/production-generation.txt", e.ToString());
                    Debug.LogException(e);
                }
            };
            return "Production scene generation queued";
        }

        static void Save(UnityEngine.Object o, string name)
        {
            o.name = name;
            string p = Root + name + ".asset";
            var old = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(p);
            if (old != null)
            {
                EditorUtility.CopySerialized(o, old);
                UnityEngine.Object.DestroyImmediate(o);
            }
            else
            {
                AssetDatabase.CreateAsset(o, p);
            }
        }

        [MenuItem("Line Lab/3 - Generate complete combat effects")]
        public static void Generate()
        {
            Directory.CreateDirectory(Root);
            CreateTextures();
            CreateMaterials();
            CreateMeshes();
            foreach (bool full in new[] { false, true })
            {
                for (int cap = 1; cap <= 512; cap *= 2)
                {
                    CreateGraph(true, false, full, cap);
                }

                CreateGraph(false, false, full, 1);
                CreateGraph(false, true, full, 1);
            }

            CreateScene();
            AssetDatabase.SaveAssets();
            Debug.Log("AETHER complete ability showcase generated");
        }

        public static void RefreshArt()
        {
            CreateTextures();
            CreateMaterials();
            CreateMeshes();
            CreateScene();
            AssetDatabase.SaveAssets();
        }

        static void CreateTextures()
        {
            for (int kind = -1; kind < 4; kind++)
            {
                int size = kind == -1 ? 256 : 256;
                var tex = new Texture2D(size, size, TextureFormat.RGBAHalf, false, true)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float u = (x + .5f) / size;
                        float v = (y + .5f) / size;
                        float dx = u * 2 - 1;
                        float dy = v * 2 - 1;
                        float r = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Atan2(dy, dx);
                        float noise = Mathf.PerlinNoise(u * 13 + 41, v * 13 + 8);
                        float fine = Mathf.PerlinNoise(u * 42 + 4, v * 42 + 13);
                        float alpha = 0;
                        float hdr = 3.2f;
                        if (kind == -1)
                        {
                            float q = Mathf.Abs(dy);
                            alpha =
                                Mathf.Exp(-q * q * 160) * .7f
                                + Mathf.Exp(-q * q * 15) * .22f * (.45f + noise)
                                + Mathf.Exp(-q * q * 5) * .035f;
                            hdr = 2.4f;
                        }

                        if (kind == 0)
                        {
                            alpha =
                                Mathf.Exp(-r * r * 65) * .8f
                                + Mathf.Exp(-r * r * 14) * .18f
                                + Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 2)), 26)
                                    * Mathf.Exp(-r * 9)
                                    * .2f;
                        }

                        if (kind == 1)
                        {
                            alpha =
                                Mathf.Exp(-dx * dx * 105)
                                * Mathf.Pow(Mathf.Max(0, 1 - Mathf.Abs(dy)), 1.2f);
                        }

                        if (kind == 2)
                        {
                            float wav =
                                r
                                + Mathf.Sin(a * 11 + r * 17) * .035f
                                + Mathf.Sin(a * 23 - r * 9) * .018f;
                            alpha =
                                Mathf.Exp(-r * r * 95) * .65f
                                + Mathf.Exp(-Mathf.Pow((wav - .43f) * 15, 2)) * (.10f + fine * .3f)
                                + Mathf.Exp(-r * r * 8) * .08f;
                            alpha +=
                                Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 3 + .4f)), 25)
                                * Mathf.Exp(-r * 7)
                                * .23f;
                        }

                        if (kind == 3)
                        {
                            float rings =
                                Mathf.Exp(-Mathf.Pow((r - .73f) * 90, 2)) * .65f
                                + Mathf.Exp(-Mathf.Pow((r - .83f) * 120, 2)) * .3f;
                            float glyph =
                                Mathf.Pow(Mathf.Max(0, Mathf.Cos(a * 12)), 34)
                                * Mathf.Exp(-Mathf.Pow((r - .56f) * 18, 2));
                            float ticks =
                                Mathf.Pow(Mathf.Max(0, Mathf.Cos(a * 36)), 25)
                                * Mathf.Exp(-Mathf.Pow((r - .91f) * 45, 2))
                                * .75f;
                            alpha = (rings + glyph + ticks) * (Mathf.Sin(a * 3) > .25f ? .25f : 1);
                            hdr = 2.6f;
                        }

                        if (kind != -1)
                        {
                            alpha *= 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.93f, 1, r));
                        }

                        tex.SetPixel(x, y, new Color(hdr, hdr, hdr, Mathf.Clamp01(alpha)));
                    }
                }

                tex.Apply();
                Save(tex, kind == -1 ? "CoreProfile" : "Profile" + kind);
            }

            var ground = new Texture2D(128, 128, TextureFormat.RGBA32, true, true)
            {
                wrapMode = TextureWrapMode.Repeat,
            };
            for (int y = 0; y < 128; y++)
            {
                for (int x = 0; x < 128; x++)
                {
                    float n =
                        Mathf.PerlinNoise(x * .09f, y * .09f) * .5f
                        + Mathf.PerlinNoise(x * .37f, y * .37f) * .5f;
                    ground.SetPixel(
                        x,
                        y,
                        new Color(.60f + n * .4f, .60f + n * .4f, .60f + n * .4f, 1)
                    );
                }
            }

            ground.Apply();
            Save(ground, "StoneGrain");
        }

        static void CreateMaterials()
        {
            void Lit(string name, Color color, float metallic = 0, float glow = 0)
            {
                var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.SetColor("_BaseColor", color);
                m.SetFloat("_Metallic", metallic);
                m.SetFloat("_Smoothness", metallic > .1f ? .40f : .12f);
                if (glow > 0)
                {
                    m.EnableKeyword("_EMISSION");
                    m.SetColor("_EmissionColor", color * glow);
                }

                Save(m, name);
            }

            Lit("TerrainMaterial", new Color(.23f, .28f, .29f));
            Lit("Stone", new Color(.18f, .23f, .25f));
            Lit("StoneLight", new Color(.33f, .36f, .32f));
            Lit("Platform", new Color(.055f, .095f, .12f), .7f);
            Lit("Deck", new Color(.18f, .25f, .27f), .65f);
            Lit("Dark", new Color(.015f, .025f, .032f), .5f);
            Lit("Armor", new Color(.60f, .71f, .69f), .6f);
            Lit("ArmorDark", new Color(.055f, .085f, .105f), .7f);
            Lit("Brass", new Color(.40f, .24f, .095f), .8f);
            Lit("MintGlow", new Color(.04f, .9f, .49f), .2f, 3);
            Lit("BlueGlow", new Color(.10f, .38f, 1), .2f, 3);
            Lit("GoldGlow", new Color(1, .32f, .055f), .2f, 3);
            Lit("Mineral", new Color(.17f, .6f, .58f), .3f, .5f);
            foreach (string name in new[] { "TerrainMaterial", "Stone", "StoneLight" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(Root + name + ".asset");
                m.SetTexture(
                    "_BaseMap",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "StoneGrain.asset")
                );
                m.SetTextureScale("_BaseMap", new Vector2(18, 18));
                EditorUtility.SetDirty(m);
            }

            void Effect(string name, string shader, string texture, bool instanced = false)
            {
                var m = new Material(Shader.Find(shader)) { enableInstancing = instanced };
                m.SetTexture(
                    "_MainTex",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(Root + texture + ".asset")
                );
                Save(m, name);
            }

            Effect("RibbonMaterial", "LineLab/Ribbon", "CoreProfile");
            Effect("ProceduralMaterial", "LineLab/ProductionCore", "CoreProfile");
            Effect("ParticleInstancedMaterial", "LineLab/ParticleInstanced", "CoreProfile", true);
            for (int layer = 0; layer < 4; layer++)
            {
                Effect("Decor" + layer, "LineLab/Ribbon", "Profile" + layer);
                Effect(
                    "DecorInstanced" + layer,
                    "LineLab/ParticleInstanced",
                    "Profile" + layer,
                    true
                );
                Effect("DecorProcedural" + layer, "LineLab/ProductionAdornment", "Profile" + layer);
            }
        }

        static void CreateMeshes()
        {
            CreateBevelBox();
            var terrain = new Mesh { name = "Sculpted basalt field" };
            const int nx = 96;
            const int nz = 80;
            var v = new Vector3[(nx + 1) * (nz + 1)];
            var uv = new Vector2[v.Length];
            var triangles = new int[nx * nz * 6];
            for (int z = 0; z <= nz; z++)
            {
                for (int x = 0; x <= nx; x++)
                {
                    float wx = (x / (float)nx - .5f) * 105;
                    float wz = (z / (float)nz - .5f) * 90;
                    float edge = Mathf.SmoothStep(
                        0,
                        1,
                        Mathf.Max(Mathf.Abs(wx) - 19, Mathf.Abs(wz) - 8) * .12f
                    );
                    float h =
                        (
                            Mathf.PerlinNoise(wx * .07f + 30, wz * .07f + 20) * 3.6f
                            + Mathf.PerlinNoise(wx * .25f + 4, wz * .25f + 7) * .65f
                            - .7f
                        ) * edge
                        - .18f;
                    v[z * (nx + 1) + x] = new Vector3(wx, h, wz);
                    uv[z * (nx + 1) + x] = new Vector2(x / (float)nx, z / (float)nz);
                }
            }

            for (int z = 0; z < nz; z++)
            {
                for (int x = 0; x < nx; x++)
                {
                    int q = (z * nx + x) * 6;
                    int a = z * (nx + 1) + x;
                    triangles[q] = a;
                    triangles[q + 1] = a + nx + 1;
                    triangles[q + 2] = a + 1;
                    triangles[q + 3] = a + 1;
                    triangles[q + 4] = a + nx + 1;
                    triangles[q + 5] = a + nx + 2;
                }
            }

            terrain.vertices = v;
            terrain.uv = uv;
            terrain.triangles = triangles;
            terrain.RecalculateNormals();
            terrain.RecalculateBounds();
            Save(terrain, "Terrain");
            var points = new Vector3[21];
            for (int ring = 0; ring < 3; ring++)
            {
                for (int j = 0; j < 7; j++)
                {
                    float a = j * Mathf.PI * 2 / 7 + ring * .19f;
                    float r =
                        ring == 0 ? .8f
                        : ring == 1 ? 1
                        : .43f;
                    points[ring * 7 + j] = new Vector3(
                        Mathf.Cos(a) * r,
                        ring * .68f,
                        Mathf.Sin(a) * r
                    );
                }
            }

            var verts = new System.Collections.Generic.List<Vector3>();
            var ids = new System.Collections.Generic.List<int>();
            void Tri(int a, int b, int c)
            {
                ids.Add(verts.Count);
                verts.Add(points[a]);
                ids.Add(verts.Count);
                verts.Add(points[b]);
                ids.Add(verts.Count);
                verts.Add(points[c]);
            }

            for (int ring = 0; ring < 2; ring++)
            {
                for (int j = 0; j < 7; j++)
                {
                    int a = ring * 7 + j;
                    int b = ring * 7 + (j + 1) % 7;
                    Tri(a, b, a + 7);
                    Tri(b, b + 7, a + 7);
                }
            }

            for (int j = 1; j < 6; j++)
            {
                Tri(14, 14 + j, 15 + j);
            }

            var rock = new Mesh { name = "Faceted erosion stone" };
            rock.SetVertices(verts);
            ids.Reverse();
            rock.SetTriangles(ids, 0);
            rock.RecalculateNormals();
            rock.RecalculateBounds();
            Save(rock, "Rock");
        }

        static void CreateBevelBox()
        {
            var v = new System.Collections.Generic.List<Vector3>();
            var ids = new System.Collections.Generic.List<int>();
            void Polygon(Vector3[] points, Vector3 normal)
            {
                if (
                    Vector3.Dot(Vector3.Cross(points[1] - points[0], points[2] - points[0]), normal)
                    < 0
                )
                {
                    Array.Reverse(points);
                }

                int q = v.Count;
                v.AddRange(points);
                for (int i = 1; i < points.Length - 1; i++)
                {
                    ids.AddRange(new[] { q, q + i, q + i + 1 });
                }
            }

            for (int axis = 0; axis < 3; axis++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 n = Vector3.zero;
                    n[axis] = sign;
                    Vector3 a = Vector3.zero;
                    a[(axis + 1) % 3] = .4f;
                    Vector3 b = Vector3.zero;
                    b[(axis + 2) % 3] = .4f;
                    Polygon(
                        new[]
                        {
                            n * .5f + a + b,
                            n * .5f - a + b,
                            n * .5f - a - b,
                            n * .5f + a - b,
                        },
                        n
                    );
                }
            }

            for (int axis = 0; axis < 3; axis++)
            {
                for (int sa = -1; sa <= 1; sa += 2)
                {
                    for (int sb = -1; sb <= 1; sb += 2)
                    {
                        Vector3 a = Vector3.zero;
                        a[axis] = sa;
                        Vector3 b = Vector3.zero;
                        b[(axis + 1) % 3] = sb;
                        Vector3 along = Vector3.zero;
                        along[(axis + 2) % 3] = .4f;
                        Polygon(
                            new[]
                            {
                                a * .5f + b * .4f + along,
                                a * .5f + b * .4f - along,
                                a * .4f + b * .5f - along,
                                a * .4f + b * .5f + along,
                            },
                            a + b
                        );
                    }
                }
            }

            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sy = -1; sy <= 1; sy += 2)
                {
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        Polygon(
                            new[]
                            {
                                new Vector3(sx * .5f, sy * .4f, sz * .4f),
                                new Vector3(sx * .4f, sy * .5f, sz * .4f),
                                new Vector3(sx * .4f, sy * .4f, sz * .5f),
                            },
                            new Vector3(sx, sy, sz)
                        );
                    }
                }
            }

            var m = new Mesh { name = "Machined bevel block" };
            m.SetVertices(v);
            m.SetTriangles(ids, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            Save(m, "BevelBox");
        }

        static void CreateGraph(bool shared, bool batched, bool full, int cap)
        {
            string name =
                "Ability"
                + (
                    shared ? "Shared"
                    : batched ? "Batched"
                    : "Single"
                )
                + (full ? "Full_" : "Beam_")
                + cap;
            string path = Root + name + ".vfx";
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
            var property = resource.GetType().GetProperty("instancingMode", Flags);
            property.SetValue(
                resource,
                Enum.Parse(property.PropertyType, batched ? "Auto" : "Disabled")
            );
            var clock = Parameter(graph, "Clock", typeof(float), 0f, new Vector2(-400, 0));
            var camera = Parameter(
                graph,
                "CameraPosition",
                typeof(Vector3),
                new Vector3(18, 28, -30),
                new Vector2(-400, 100)
            );
            object lines = null;
            object[] inputs = null;
            if (shared)
            {
                lines = Parameter(
                    graph,
                    "Lines",
                    typeof(GraphicsBuffer),
                    null,
                    new Vector2(-400, 200)
                );
            }
            else
            {
                inputs = new[]
                {
                    Parameter(
                        graph,
                        "Start",
                        typeof(Vector4),
                        new Vector4(-3, 1, 0, 1),
                        new Vector2(-400, 200)
                    ),
                    Parameter(
                        graph,
                        "End",
                        typeof(Vector4),
                        new Vector4(3, 1, 0, .22f),
                        new Vector2(-400, 300)
                    ),
                    Parameter(graph, "Tint", typeof(Vector4), Vector4.one, new Vector2(-400, 400)),
                    Parameter(
                        graph,
                        "Shape",
                        typeof(Vector4),
                        new Vector4(0, 0, -2, -10000),
                        new Vector2(-400, 500)
                    ),
                };
            }

            for (int layer = -1; layer < (full ? 4 : 0); layer++)
            {
                int particles = layer == -1 ? 96 : AbilityGeometry.LayerCount(layer);
                var spawn = Node("VFXBasicSpawner");
                var init = Node("VFXBasicInitialize");
                var update = Node("VFXBasicUpdate");
                var output = Node("VFXPlanarPrimitiveOutput");
                object[] contexts = { spawn, init, update, output };
                for (int i = 0; i < 4; i++)
                {
                    Set(contexts[i], "position", new Vector2(200 + (layer + 1) * 520, i * 300));
                    Call(graph, "AddChild", contexts[i]);
                }

                Call(init, "LinkFrom", spawn);
                Call(update, "LinkFrom", init);
                Call(output, "LinkFrom", update);
                Setting(init, "capacity", (uint)Mathf.NextPowerOfTwo(cap * particles));
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
                Value(
                    output,
                    "mainTexture",
                    AssetDatabase.LoadAssetAtPath<Texture2D>(
                        Root + (layer == -1 ? "CoreProfile" : "Profile" + layer) + ".asset"
                    )
                );
                var bounds = Slot(init, "bounds");
                var box = Get(bounds, "value");
                box.GetType().GetField("size").SetValue(box, new Vector3(150, 80, 150));
                Set(bounds, "value", box);
                var burst = Node("VFXSpawnerBurst");
                Call(spawn, "AddChild", burst);
                Connect(
                    burst,
                    "Count",
                    Parameter(
                        graph,
                        layer == -1 ? "SpawnCore" : "SpawnLayer" + layer,
                        typeof(float),
                        (float)particles,
                        new Vector2(-100, (layer + 1) * 100)
                    )
                );
                var custom = Node("Block.CustomHLSL");
                Call(update, "AddChild", custom);
                string args = shared
                    ? "in StructuredBuffer<float4> Lines, "
                    : "in float4 Start, in float4 End, in float4 Tint, in float4 Shape, ";
                string load = shared
                    ? "uint offset=(attributes.particleId/"
                        + particles
                        + @"

)*4;
l.start = Lines[offset];
l.end = Lines[offset + 1];
l.color = Lines[offset + 2];
l.shape = Lines[offset + 3];"
                    : @"
l.start = Start;
l.end = End;
l.color = Tint;
l.shape = Shape;";
                string body =
                    layer == -1
                        ? @"

uint strand = (attributes.particleId % 96) / 32;
uint s = attributes.particleId % 32;
l = AbilityStrand(l, strand, Clock);
float3 a = AbilityPoint(l, (float)s / 32, Clock), b = AbilityPoint(l, (s + 1.0) / 32, Clock);
float3 mid = (a + b) * .5;
float3 x = normalizesafe(b - a, float3(1, 0, 0));
float3 y = normalizesafe(cross(normalize(CameraPosition - mid), x), float3(0, 1, 0));
float4 c = AbilityTint(l, (s + .5) / 32, Clock);
attributes.position = mid;
attributes.axisX = x;
attributes.axisY = y;
attributes.axisZ = cross(x, y);
attributes.size = 1;
attributes.scaleX = distance(a, b) * 1.035;
attributes.scaleY = l.end.w;
attributes.scaleZ = 1;
attributes.color = c.rgb;
attributes.alpha = c.a;"
                        : @"
Adornment d;AbilityDecor(l,"
                            + layer
                            + ",attributes.particleId%"
                            + particles
                            + @"

,Clock,CameraPosition,d);
attributes.position = d.Position;
attributes.axisX = d.X;
attributes.axisY = d.Y;
attributes.axisZ = cross(d.X, d.Y);
attributes.size = 1;
attributes.scaleX = d.Size.x;
attributes.scaleY = d.Size.y;
attributes.scaleZ = 1;
attributes.color = d.Color;
attributes.alpha = d.Alpha;";
                Set(
                    custom,
                    "sourceCode",
                    "#include \"Assets/LineLab/Production/Shaders/AbilityCommon.hlsl\"\nvoid DrawAbility(inout VFXAttributes attributes, "
                        + args
                        + "in float Clock, in float3 CameraPosition)\n{ AbilityLine l;"
                        + load
                        + body
                        + " }"
                );
                Connect(custom, "Clock", clock);
                Connect(custom, "CameraPosition", camera);
                if (shared)
                {
                    Connect(custom, "Lines", lines);
                }
                else
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Connect(custom, new[] { "Start", "End", "Tint", "Shape" }[i], inputs[i]);
                    }
                }
            }

            Call(graph, "UpdateSubAssets");
            Call(resource, "WriteAsset");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            File.WriteAllText("BenchmarkResults/production-generation.txt", name);
        }

        static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ProductionArt.Build();
            var cam = new GameObject(
                "Cinematic RTS camera",
                typeof(Camera),
                typeof(AudioListener)
            ).GetComponent<Camera>();
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(23, 29, -34);
            cam.transform.LookAt(new Vector3(0, 1, 1));
            cam.fieldOfView = 43;
            cam.nearClipPlane = .2f;
            cam.farClipPlane = 180;
            cam.allowHDR = true;
            cam.backgroundColor = new Color(.045f, .09f, .13f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var key = new GameObject("Sun / soft shadows", typeof(Light)).GetComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(1, .86f, .68f);
            key.intensity = 2.4f;
            key.shadows = LightShadows.Soft;
            key.transform.rotation = Quaternion.Euler(42, -35, 0);
            key.shadowStrength = .8f;
            var fill = new GameObject("Cold sky fill", typeof(Light)).GetComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(.35f, .60f, 1);
            fill.intensity = .55f;
            fill.transform.rotation = Quaternion.Euler(25, 140, 0);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.22f, .30f, .39f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = .009f;
            RenderSettings.fogColor = cam.backgroundColor;
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(.9f);
            bloom.intensity.Override(.65f);
            bloom.scatter.Override(.7f);
            var tone = profile.Add<Tonemapping>(true);
            tone.mode.Override(TonemappingMode.ACES);
            var adjust = profile.Add<ColorAdjustments>(true);
            adjust.postExposure.Override(.55f);
            adjust.contrast.Override(10);
            adjust.saturation.Override(-8);
            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(.2f);
            vignette.smoothness.Override(.75f);
            string path = Root + "Atmosphere.asset";
            if (File.Exists(path))
            {
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.CreateAsset(profile, path);
            foreach (var c in profile.components)
            {
                AssetDatabase.AddObjectToAsset(c, profile);
            }

            new GameObject("Cinematic atmosphere", typeof(Volume))
                .GetComponent<Volume>()
                .sharedProfile = profile;
            new GameObject("AETHER / Complete ability laboratory", typeof(ProductionController));
            EditorSceneManager.SaveScene(
                scene,
                "Assets/LineLab/Scenes/03_Complete_Abilities.unity"
            );
            var old = EditorBuildSettings
                .scenes.Where(s => !s.path.EndsWith("03_Complete_Abilities.unity"))
                .ToList();
            old.Add(
                new EditorBuildSettingsScene(
                    "Assets/LineLab/Scenes/03_Complete_Abilities.unity",
                    true
                )
            );
            EditorBuildSettings.scenes = old.ToArray();
        }

        [MenuItem("Line Lab/4 - Build complete combat effects player")]
        public static void Build()
        {
            Directory.CreateDirectory("Builds/ProductionLab");
            var r = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/LineLab/Scenes/03_Complete_Abilities.unity" },
                    locationPathName = "Builds/ProductionLab/ProductionLab.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                }
            );
            File.WriteAllText(
                "Builds/production-build-status.txt",
                r.summary.result
                    + " / "
                    + r.summary.totalErrors
                    + " errors / "
                    + r.summary.totalTime
            );
        }

        public static string QueueBuild()
        {
            EditorApplication.delayCall += Build;
            return "Production build queued";
        }
    }
}
