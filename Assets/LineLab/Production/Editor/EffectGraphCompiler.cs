using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;
using static LineLab.Production.Editor.VfxGraphAdapter;

namespace LineLab.Production.Editor
{
    /// <summary>
    /// Compiles a library topology into disposable graph outputs. Recipe values,
    /// textures and curves remain runtime tables; changing them does not recompile
    /// graphs. No source asset or scene is rewritten by this compiler.
    /// </summary>
    public static class EffectGraphCompiler
    {
        const string Root = "Assets/LineLab/Resources/Production/Generated";

        public static void CompileLibrary(
            EffectLibrary library,
            bool allCapacities = false,
            int requiredAbilityCount = 3,
            bool forceRebuild = false
        )
        {
            if (
                requiredAbilityCount < 1
                || requiredAbilityCount > EffectRecipeLayout.MaximumAbilities
            )
            {
                throw new ArgumentOutOfRangeException(nameof(requiredAbilityCount));
            }

            using (var catalog = new NativeEffectCatalog(library))
            {
                EnsureFolder(Root);
                EnsureFolder(
                    Root + "/" + catalog.LayoutKey + "_" + EffectRecipeLayout.GraphVersion
                );
                CreateMaterials();
                string compilerSignature = ComputeCompilerSignature();
                int capacity = Mathf.NextPowerOfTwo(requiredAbilityCount);
                foreach (bool full in new[] { false, true })
                {
                    if (allCapacities)
                    {
                        for (int current = 1; current <= 8192; current *= 2)
                        {
                            CompileGraph(
                                catalog,
                                true,
                                false,
                                full,
                                current,
                                forceRebuild,
                                compilerSignature
                            );
                        }
                    }
                    else
                    {
                        CompileGraph(
                            catalog,
                            true,
                            false,
                            full,
                            capacity,
                            forceRebuild,
                            compilerSignature
                        );
                        if (capacity != 4)
                        {
                            CompileGraph(
                                catalog,
                                true,
                                false,
                                full,
                                4,
                                forceRebuild,
                                compilerSignature
                            );
                        }
                    }

                    CompileGraph(catalog, false, false, full, 1, forceRebuild, compilerSignature);
                    CompileGraph(catalog, false, true, full, 1, forceRebuild, compilerSignature);
                }

                AssetDatabase.SaveAssets();
            }
        }

        static void CreateMaterials()
        {
            foreach (
                string name in new[]
                {
                    "AuthoredRibbon",
                    "AuthoredParticle",
                    "AuthoredCore",
                    "AuthoredAdornment",
                }
            )
            {
                string path = Root + "/" + name + ".mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                {
                    continue;
                }

                var shader = Shader.Find("LineLab/" + name);
                if (shader == null)
                {
                    throw new InvalidOperationException("Missing authored shader " + name);
                }

                var material = new Material(shader) { name = name, enableInstancing = true };
                AssetDatabase.CreateAsset(material, path);
            }
        }

        static void CompileGraph(
            NativeEffectCatalog catalog,
            bool shared,
            bool batched,
            bool full,
            int capacity,
            bool forceRebuild,
            string compilerSignature
        )
        {
            var technique =
                shared ? Technique.VfxBuffer
                : batched ? Technique.VfxPerLineBatched
                : Technique.VfxPerLine;
            string resourcePath = AbilityBackend.GraphResourcePath(
                catalog,
                technique,
                capacity,
                full
            );
            string path = "Assets/LineLab/Resources/" + resourcePath + ".vfx";
            var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path);
            var importer = AssetImporter.GetAtPath(path);
            if (
                asset != null
                && !forceRebuild
                && importer != null
                && importer.userData == compilerSignature
            )
            {
                return;
            }

            object resource;
            object graph;
            if (asset == null)
            {
                asset = (VisualEffectAsset)Call(
                    Type("UnityEditor.VisualEffectAssetEditorUtility"),
                    "CreateNewAsset",
                    path
                );
                resource = Call(
                    Type("UnityEditor.VFX.VisualEffectObjectExtensions"),
                    "GetResource",
                    asset
                );
                graph = Call(
                    Type("UnityEditor.VFX.VisualEffectResourceExtensions"),
                    "GetOrCreateGraph",
                    resource
                );
            }
            else
            {
                resource = Call(
                    Type("UnityEditor.VFX.VisualEffectObjectExtensions"),
                    "GetResource",
                    asset
                );
                graph = Call(
                    Type("UnityEditor.VFX.VisualEffectResourceExtensions"),
                    "GetOrCreateGraph",
                    resource
                );
                // Rebuild in place. The asset's GUID and references survive.
                Call(graph, "RemoveAllChildren");
            }

            var instancing = Get(resource, "instancingMode").GetType();
            Set(resource, "instancingMode", Enum.Parse(instancing, batched ? "Auto" : "Disabled"));
            var inputs = new Dictionary<string, object>();
            int row = 0;
            void Input(string name, Type type, object value)
            {
                inputs.Add(
                    name,
                    Parameter(graph, name, type, value, new Vector2(-650, row++ * 100))
                );
            }

            Input("Clock", typeof(float), 0f);
            Input("CameraPosition", typeof(Vector3), new Vector3(19, 28, -33));
            Input("BoundsCenter", typeof(Vector3), Vector3.zero);
            Input("BoundsSize", typeof(Vector3), new Vector3(400, 100, 400));
            Input("Atlas", typeof(Texture2D), Texture2D.whiteTexture);
            Input("AtlasGrid", typeof(Vector2), Vector2.one);
            foreach (string buffer in new[] { "Effects", "Ribbons", "Layers", "Ramps" })
            {
                Input(buffer, typeof(GraphicsBuffer), null);
            }

            if (shared)
            {
                Input("Lines", typeof(GraphicsBuffer), null);
            }
            else
            {
                Input("Start", typeof(Vector4), new Vector4(-3, 1.8f, 0, 1));
                Input("End", typeof(Vector4), new Vector4(3, 1.6f, 2, .22f));
                Input("Tint", typeof(Vector4), Vector4.one);
                Input("Shape", typeof(Vector4), new Vector4(0, 0, -2, -10000));
            }

            int streams = 1 + (full ? catalog.LayerSlots : 0);
            for (int stream = 0; stream < streams; stream++)
            {
                int cards =
                    stream == 0
                        ? catalog.AllocatedCoreQuadsPerAbility
                        : catalog.LayerCapacities[stream - 1];
                var spawn = Node("VFXBasicSpawner");
                var initialize = Node("VFXBasicInitialize");
                var update = Node("VFXBasicUpdate");
                var output = Node("VFXPlanarPrimitiveOutput");
                object[] contexts = { spawn, initialize, update, output };
                for (int context = 0; context < contexts.Length; context++)
                {
                    Set(
                        contexts[context],
                        "position",
                        new Vector2(100 + stream * 540, context * 300)
                    );
                    Call(graph, "AddChild", contexts[context]);
                }

                Call(initialize, "LinkFrom", spawn);
                Call(update, "LinkFrom", initialize);
                Call(output, "LinkFrom", update);
                var data = Call(initialize, "GetData");
                Set(data, "title", stream == 0 ? "Core" : "Layer" + (stream - 1));
                Setting(
                    initialize,
                    "capacity",
                    (uint)Mathf.NextPowerOfTwo(Math.Max(1, checked(capacity * cards)))
                );
                Setting(update, "ageParticles", false);
                Setting(update, "reapParticles", false);
                Setting(
                    update,
                    "integration",
                    Enum.Parse(Type("UnityEditor.VFX.VFXBasicUpdate+VFXIntegrationMode"), "None")
                );
                Setting(
                    output,
                    "blendMode",
                    Enum.ToObject(Type("UnityEditor.VFX.VFXAbstractRenderedOutput+BlendMode"), 0)
                );
                Setting(
                    output,
                    "uvMode",
                    Enum.Parse(Type("UnityEditor.VFX.VFXAbstractParticleOutput+UVMode"), "Flipbook")
                );
                Setting(output, "flipbookBlendFrames", false);
                Connect(output, "mainTexture", inputs["Atlas"]);
                Connect(output, "flipBookSize", inputs["AtlasGrid"]);
                // Manual bounds are exposed inputs, not a hidden sample-arena limit.
                var boundsSlot = Slot(initialize, "bounds");
                var boundsChildren = ((IEnumerable)Get(boundsSlot, "children"))
                    .Cast<object>()
                    .ToArray();
                foreach (
                    var binding in new[] { ("center", "BoundsCenter"), ("size", "BoundsSize") }
                )
                {
                    object child = boundsChildren.Single(slot =>
                        (string)Get(slot, "name") == binding.Item1
                    );
                    Call(child, "Link", Call(inputs[binding.Item2], "GetOutputSlot", 0));
                }

                var burst = Node("VFXSpawnerBurst");
                Call(spawn, "AddChild", burst);
                string spawnName = stream == 0 ? "SpawnCore" : "SpawnLayer" + (stream - 1);
                var count = Parameter(
                    graph,
                    spawnName,
                    typeof(float),
                    (float)cards,
                    new Vector2(-300, stream * 100)
                );
                Connect(burst, "Count", count);
                var custom = Node("Block.CustomHLSL");
                Call(update, "AddChild", custom);
                Set(custom, "sourceCode", Source(catalog, shared, stream, cards));
                foreach (
                    string name in new[]
                    {
                        "Clock",
                        "CameraPosition",
                        "AtlasGrid",
                        "Effects",
                        "Ribbons",
                        "Layers",
                        "Ramps",
                    }
                )
                {
                    Connect(custom, name, inputs[name]);
                }

                if (shared)
                {
                    Connect(custom, "Lines", inputs["Lines"]);
                }
                else
                {
                    foreach (string name in new[] { "Start", "End", "Tint", "Shape" })
                    {
                        Connect(custom, name, inputs[name]);
                    }
                }
            }

            Call(graph, "UpdateSubAssets");
            Call(resource, "WriteAsset");
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            importer = AssetImporter.GetAtPath(path);
            if (importer == null)
            {
                throw new InvalidOperationException("Generated graph has no importer: " + path);
            }

            importer.userData = compilerSignature;
            // Persist the signature without triggering another graph compilation.
            AssetDatabase.WriteImportSettingsIfDirty(path);
        }

        static string ComputeCompilerSignature()
        {
            // Content values live in runtime tables. Only code/package changes
            // invalidate generated graphs; topology already forms their asset path.
            string[] sources =
            {
                "Assets/LineLab/Production/Editor/EffectGraphCompiler.cs",
                "Assets/LineLab/Production/Editor/VfxGraphAdapter.cs",
                "Assets/LineLab/Production/Shaders/AbilityCommon.hlsl",
                "Assets/LineLab/Production/Shaders/RecipeData.hlsl",
            };
            using (var bytes = new MemoryStream())
            using (var writer = new BinaryWriter(bytes, Encoding.UTF8, true))
            {
                writer.Write("LineLab.AuthoredGraph.Compiler.v1");
                writer.Write(Application.unityVersion);
                writer.Write(EffectRecipeLayout.GraphVersion);
                writer.Write(typeof(VisualEffectAsset).Assembly.FullName);
                foreach (string source in sources)
                {
                    writer.Write(source);
                    byte[] contents = File.ReadAllBytes(source);
                    writer.Write(contents.Length);
                    writer.Write(contents);
                }

                writer.Flush();
                using (SHA256 sha = SHA256.Create())
                {
                    return "LineLab.AuthoredGraph.v1:"
                        + BitConverter
                            .ToString(sha.ComputeHash(bytes.ToArray()))
                            .Replace("-", "")
                            .ToLowerInvariant();
                }
            }
        }

        static string Source(NativeEffectCatalog catalog, bool shared, int stream, int cards)
        {
            cards = Math.Max(1, cards);
            string input = shared
                ? "in StructuredBuffer<float4> Lines,"
                : "in float4 Start, in float4 End, in float4 Tint, in float4 Shape,";
            string load = shared
                ? $@"
    uint lineIndex = attributes.particleId / {cards};
    beam.start = Lines[lineIndex * 4];
    beam.end = Lines[lineIndex * 4 + 1];
    beam.color = Lines[lineIndex * 4 + 2];
    beam.shape = Lines[lineIndex * 4 + 3];"
                : @"
    beam.start = Start;
    beam.end = End;
    beam.color = Tint;
    beam.shape = Shape;";
            string body =
                stream == 0
                    ? $@"
    uint ribbonSlot = (attributes.particleId % {cards}) / {catalog.Segments};
    uint segment = attributes.particleId % {catalog.Segments};
    RibbonRecipeData ribbon = AbilityLoadRibbon(Ribbons, effectIndex * {catalog.RibbonSlots} + ribbonSlot);
    beam = AbilityStrand(beam, recipe, ribbon, Clock);
    if (ribbonSlot >= recipe.Layout.x) beam.color.a = 0;
    float t = (segment + 0.5) / {catalog.Segments};
    float3 a = AbilityPoint(beam, recipe, (float)segment / {catalog.Segments}, Clock);
    float3 b = AbilityPoint(beam, recipe, (segment + 1.0) / {catalog.Segments}, Clock);
    float3 midpoint = (a + b) * 0.5;
    float3 x = normalizesafe(b - a, float3(1,0,0));
    float3 y = normalizesafe(cross(normalizesafe(CameraPosition-midpoint,float3(0,0,-1)),x),float3(0,1,0));
    float4 tint = AbilityTint(beam, recipe, ribbon, Ramps, t, Clock);
    attributes.position = midpoint;
    attributes.axisX = x;
    attributes.axisY = y;
    attributes.axisZ = cross(x,y);
    attributes.scaleX = distance(a,b) * 1.035;
    attributes.scaleY = AbilityWidth(beam,ribbon,Ramps,t);
    attributes.color = tint.rgb;
    attributes.alpha = tint.a;
    attributes.texIndex = (AtlasGrid.y - 1 - floor(ribbon.Surface.x / AtlasGrid.x)) * AtlasGrid.x + fmod(ribbon.Surface.x, AtlasGrid.x);"
                    : $@"
    LayerRecipeData layer = AbilityLoadLayer(Layers, effectIndex * {catalog.LayerSlots} + {stream - 1});
    Adornment card;
    AbilityDecor(beam,recipe,layer,attributes.particleId % {cards},Clock,CameraPosition,Ramps,card);
    attributes.position = card.Position;
    attributes.axisX = card.X;
    attributes.axisY = card.Y;
    attributes.axisZ = cross(card.X,card.Y);
    attributes.scaleX = card.Size.x;
    attributes.scaleY = card.Size.y;
    attributes.color = card.Color;
    attributes.alpha = card.Alpha;
    attributes.texIndex = (AtlasGrid.y - 1 - floor(card.TextureIndex / AtlasGrid.x)) * AtlasGrid.x + fmod(card.TextureIndex, AtlasGrid.x);";
            return $@"#include ""Assets/LineLab/Production/Shaders/AbilityCommon.hlsl""
void DrawAuthoredAbility(inout VFXAttributes attributes,
    {input}
    in StructuredBuffer<float4> Effects, in StructuredBuffer<float4> Ribbons,
    in StructuredBuffer<float4> Layers, in StructuredBuffer<float4> Ramps,
    in float Clock, in float3 CameraPosition, in float2 AtlasGrid)
{{
    AbilityLine beam;
    {load}
    uint effectIndex = (uint)beam.shape.x;
    EffectRecipeData recipe = AbilityLoadEffect(Effects,effectIndex);
    {body}
    attributes.size = 1;
    attributes.scaleZ = 1;
}}";
        }
    }
}
