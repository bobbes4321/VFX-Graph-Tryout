using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

namespace LineLab.Production.Editor
{
    /// <summary>
    /// Runs authoring acceptance through actual AssetDatabase and graph compiler APIs.
    /// Creates missing proof examples; existing authored assets are never reset.
    /// </summary>
    public static class AuthoringAcceptance
    {
        const string ProofRoot = "Assets/LineLab/Recipes/Proof";
        const string ProofLibraryPath = "Assets/LineLab/Resources/Production/ProofLibrary.asset";
        const string ResultPath = "Logs/authoring-acceptance.json";
        const string ProgressPath = "Logs/authoring-acceptance-progress.txt";
        static bool queued;

        [Serializable]
        sealed class Result
        {
            public bool passed;
            public string startedUtc;
            public string finishedUtc;
            public string error;
            public string proofLibrary;
            public string[] createdAssets;
            public string[] checks;
            public string catalogHash;
            public int effectCount;
            public int ribbonSlots;
            public int layerSlots;
            public string[] stableKeys;
        }

        sealed class CameraSnapshot
        {
            public Camera Camera;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
            public Transform Parent;
        }

        [MenuItem("Line Lab/Authoring/Run Authoring Acceptance")]
        public static void QueueRun()
        {
            if (queued)
            {
                return;
            }

            queued = true;
            Directory.CreateDirectory("Logs");
            Progress("Queued authoring acceptance");
            // Return to the editor before imports/graph compilation begin.
            EditorApplication.delayCall += Run;
        }

        static void Run()
        {
            var result = new Result { startedUtc = DateTime.UtcNow.ToString("O") };
            var checks = new List<string>();
            var created = new List<string>();
            try
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    throw new InvalidOperationException("Run authoring acceptance in Edit Mode.");
                }

                Progress("Snapshot source files and loaded scene cameras");
                Dictionary<string, string> sources = SnapshotSources();
                List<CameraSnapshot> cameras = SnapshotCameras();
                EffectLibrary artist = Required<EffectLibrary>(
                    "Assets/LineLab/Resources/Production/ArtistLibrary.asset"
                );
                EffectDefinition renewal = Required<EffectDefinition>(
                    "Assets/LineLab/Recipes/Renewal.asset"
                );
                ParticleLayerAsset renewalMotes = Required<ParticleLayerAsset>(
                    "Assets/LineLab/Recipes/Particles/RenewalMotes.asset"
                );
                Texture2D alternateProfile = Required<Texture2D>(
                    "Assets/LineLab/Resources/Production/Profile1.asset"
                );
                EnsureFolder(ProofRoot);
                ParticleLayerAsset opalMotes = CreateMissing(
                    ProofRoot + "/OpalMotes.asset",
                    () =>
                    {
                        var module = Object.Instantiate(renewalMotes);
                        module.name = "Opal Motes";
                        module.Count = 12;
                        module.Profile = alternateProfile;
                        module.CyclesPerSecond = -.32f;
                        module.MinimumSize = .12f;
                        module.MaximumSize = .24f;
                        module.ColorOverLife = new Gradient
                        {
                            colorKeys = new[]
                            {
                                new GradientColorKey(new Color(.35f, 1.4f, 2.4f), 0),
                                new GradientColorKey(new Color(2.2f, .45f, 1.6f), 1),
                            },
                            alphaKeys = new[]
                            {
                                new GradientAlphaKey(1, 0),
                                new GradientAlphaKey(1, 1),
                            },
                        };
                        module.SizeOverLife = new AnimationCurve(
                            new Keyframe(0, .15f),
                            new Keyframe(.35f, 1.4f),
                            new Keyframe(.8f, .65f),
                            new Keyframe(1, .1f)
                        );
                        return module;
                    },
                    created
                );
                EffectDefinition opal = CreateMissing(
                    ProofRoot + "/OpalTransfer.asset",
                    () =>
                    {
                        if (renewal.Ribbons.Count < 2)
                        {
                            throw new InvalidOperationException(
                                "The Renewal proof source needs two ribbon references."
                            );
                        }

                        var effect = Object.Instantiate(renewal);
                        effect.name = "Opal Transfer";
                        effect.StableKey = "opal-transfer";
                        effect.Tint = new Color(.8f, .9f, 1);
                        effect.Ribbons = new List<RibbonLayerAsset>
                        {
                            renewal.Ribbons[0],
                            renewal.Ribbons[1],
                        };
                        effect.Layers = new List<ParticleLayerAsset> { renewalMotes, opalMotes };
                        return effect;
                    },
                    created
                );
                EffectLibrary proof = CreateMissing(
                    ProofLibraryPath,
                    () =>
                    {
                        var library = ScriptableObject.CreateInstance<EffectLibrary>();
                        library.name = "Proof Library";
                        library.Segments = artist.Segments;
                        library.Effects = new List<EffectDefinition>(artist.Effects) { opal };
                        return library;
                    },
                    created
                );
                result.proofLibrary = ProofLibraryPath;
                Check(
                    proof.Effects.Contains(opal),
                    "Proof library includes the duplicated Opal Transfer asset",
                    checks
                );
                Check(
                    opal.Layers.Contains(renewalMotes) && opal.Layers.Contains(opalMotes),
                    "New effect composes an existing mote module and a new mote module",
                    checks
                );
                using (var catalog = new NativeEffectCatalog(proof))
                using (var repeated = new NativeEffectCatalog(proof))
                {
                    result.catalogHash = catalog.Fingerprint;
                    result.effectCount = catalog.EffectCount;
                    result.ribbonSlots = catalog.RibbonSlots;
                    result.layerSlots = catalog.LayerSlots;
                    result.stableKeys = catalog.EffectKeys;
                    Check(
                        catalog.Fingerprint == repeated.Fingerprint,
                        "Unchanged authoring rebakes deterministically",
                        checks
                    );
                    Check(
                        catalog.GetEffectId("opal-transfer").Value == proof.Effects.IndexOf(opal),
                        "Gameplay resolves the new stable key without a style switch",
                        checks
                    );
                    Check(
                        catalog.EffectCount == proof.Effects.Count,
                        "Baked ID count follows the proof library (five recipes for untouched samples)",
                        checks
                    );
                    Check(
                        catalog.EffectRecipes.Length == catalog.EffectCount
                            && catalog.RibbonRecipes.Length
                                == catalog.RibbonSlots * catalog.EffectCount
                            && catalog.LayerRecipes.Length
                                == catalog.LayerSlots * catalog.EffectCount
                            && catalog.RampSamples.Length % EffectRecipeLayout.RampSamples == 0,
                        "Public immutable native views expose complete tables",
                        checks
                    );
                }

                Check(
                    Marshal.SizeOf<EffectRecipeData>() == 96
                        && Marshal.SizeOf<RibbonRecipeData>() == 48
                        && Marshal.SizeOf<LayerRecipeData>() == 96,
                    "CPU/GPU recipe ABI sizes",
                    checks
                );
                Progress(
                    "Test reusable modules, curves, counts and endpoint semantics on isolated clones"
                );
                CheckClonePropagation(renewal, renewalMotes, checks);
                CheckEndpointSemantics(checks);
                Progress("Force compile proof graphs twice and check stable asset identities");
                CheckGraphIdempotence(proof, checks);
                Progress(
                    "Compile all-zero secondary topology and construct zero-card native pools"
                );
                CheckZeroLayers(renewal, checks);
                Progress("Run native lifecycle and 5000-capacity validation");
                checks.Add(ProductionValidation.Run());
                foreach (KeyValuePair<string, string> source in sources)
                {
                    Check(
                        File.Exists(source.Key)
                            && Hash(File.ReadAllBytes(source.Key)) == source.Value,
                        "Preserved source bytes: " + source.Key,
                        checks
                    );
                }

                foreach (CameraSnapshot camera in cameras)
                {
                    Transform transform = camera.Camera != null ? camera.Camera.transform : null;
                    Check(
                        transform != null
                            && transform.position.Equals(camera.Position)
                            && transform.rotation.Equals(camera.Rotation)
                            && transform.localPosition.Equals(camera.LocalPosition)
                            && transform.localRotation.Equals(camera.LocalRotation)
                            && transform.localScale.Equals(camera.LocalScale)
                            && transform.parent == camera.Parent,
                        "Preserved scene camera: "
                            + (camera.Camera == null ? "missing" : camera.Camera.name),
                        checks
                    );
                }

                result.passed = true;
                Progress("PASS: " + checks.Count + " authoring acceptance checks");
                Debug.Log("Authoring acceptance passed. See " + ResultPath);
            }
            catch (Exception exception)
            {
                result.error = exception.ToString();
                Progress("FAIL: " + exception.Message);
                Debug.LogException(exception);
            }
            finally
            {
                result.finishedUtc = DateTime.UtcNow.ToString("O");
                result.checks = checks.ToArray();
                result.createdAssets = created.ToArray();
                File.WriteAllText(ResultPath, JsonUtility.ToJson(result, true));
                queued = false;
            }
        }

        static void CheckClonePropagation(
            EffectDefinition source,
            ParticleLayerAsset sourceMotes,
            List<string> checks
        )
        {
            var shared = Object.Instantiate(sourceMotes);
            var sparse = Object.Instantiate(sourceMotes);
            var first = Object.Instantiate(source);
            var second = Object.Instantiate(source);
            var library = ScriptableObject.CreateInstance<EffectLibrary>();
            float originalSpeed = shared.CyclesPerSecond;
            try
            {
                first.StableKey = "acceptance-a";
                second.StableKey = "acceptance-b";
                first.Layers = new List<ParticleLayerAsset> { shared };
                second.Layers = new List<ParticleLayerAsset> { shared };
                library.Effects = new List<EffectDefinition> { first, second };
                string before;
                using (var catalog = new NativeEffectCatalog(library))
                {
                    before = catalog.Fingerprint;
                }

                shared.CyclesPerSecond = originalSpeed + .173f;
                using (var changed = new NativeEffectCatalog(library))
                {
                    Check(
                        changed.LayerRecipes[0].Motion.x == shared.CyclesPerSecond
                            && changed.LayerRecipes[changed.LayerSlots].Motion.x
                                == shared.CyclesPerSecond,
                        "Editing one reusable mote module updates both referring recipes",
                        checks
                    );
                    Check(
                        before != changed.Fingerprint,
                        "Content fingerprint changes after a module edit",
                        checks
                    );
                }

                shared.CyclesPerSecond = originalSpeed;
                shared.Count = 48;
                sparse.Count = 12;
                second.Layers = new List<ParticleLayerAsset> { sparse };
                using (var catalog = new NativeEffectCatalog(library))
                using (NativeArray<float4> ramps = CopyRamps(catalog))
                {
                    Check(
                        catalog.LayerCapacities[0] == 48
                            && catalog.LayerRecipes[catalog.LayerSlots].Layout.y == 12,
                        "Shared pool reserves the maximum while each recipe keeps its own count",
                        checks
                    );
                    EffectRecipeData recipe = catalog.EffectRecipes[1];
                    LayerRecipeData layer = catalog.LayerRecipes[catalog.LayerSlots];
                    LineData line = ProbeLine(recipe);
                    AbilityGeometry.Decor(
                        line,
                        recipe,
                        layer,
                        12,
                        1,
                        new float3(10, 20, -15),
                        ramps,
                        out Adornment padding
                    );
                    Check(
                        padding.Alpha == 0
                            && padding.X.Equals(new float3(1, 0, 0))
                            && padding.Y.Equals(new float3(0, 1, 0)),
                        "Recipe padding produces transparent cards with a valid frame",
                        checks
                    );
                    line.Color.w = 0;
                    AbilityGeometry.Decor(
                        line,
                        recipe,
                        layer,
                        1,
                        1,
                        new float3(10, 20, -15),
                        ramps,
                        out Adornment hidden
                    );
                    Check(hidden.Alpha == 0, "Effect alpha hides secondary layers too", checks);
                    Check(
                        layer.Motion.x == sparse.CyclesPerSecond,
                        "New mote modules need no runtime style registration",
                        checks
                    );
                }
            }
            finally
            {
                shared.CyclesPerSecond = originalSpeed;
                // Only unsaved clones were changed. Source assets and artist Undo history remain intact.
                Object.DestroyImmediate(library);
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
                Object.DestroyImmediate(shared);
                Object.DestroyImmediate(sparse);
            }
        }

        static void CheckEndpointSemantics(List<string> checks)
        {
            var ramps = new NativeArray<float4>(64, Allocator.Temp);
            try
            {
                for (int sample = 0; sample < 32; sample++)
                {
                    ramps[sample] = math.lerp(
                        new float4(2, .1f, .1f, 1),
                        new float4(.1f, .1f, 2, 1),
                        sample / 31f
                    );
                    ramps[32 + sample] = new float4(1);
                }

                var recipe = new EffectRecipeData { Timing = new float4(.1f, 1, 1, 1) };
                LineData line = ProbeLine(recipe);
                var layer = new LayerRecipeData
                {
                    Layout = new float4(2, 4, 0, 0),
                    Size = new float4(1, 1, 1, 1),
                    Appearance = new float4(1, 0, 1, 1),
                    Placement = new float4(1, .6f, 2, 3),
                };
                float3 camera = new float3(10, 20, -15);
                AbilityGeometry.Decor(
                    line,
                    recipe,
                    layer,
                    0,
                    1,
                    camera,
                    ramps,
                    out Adornment source
                );
                Check(
                    math.abs(source.Position.y - (line.Start.y + .6f)) < .0001f
                        && math.abs(source.Size.y - 2) < .0001f,
                    "Corona source height and scale apply",
                    checks
                );
                layer.Placement.x = 2;
                AbilityGeometry.Decor(
                    line,
                    recipe,
                    layer,
                    0,
                    1,
                    camera,
                    ramps,
                    out Adornment target
                );
                Check(
                    math.abs(target.Position.y - (line.End.y + .6f)) < .0001f
                        && math.abs(target.Size.y - 3) < .0001f,
                    "Corona target height and scale apply",
                    checks
                );
                layer.Placement.x = 0;
                AbilityGeometry.Decor(line, recipe, layer, 1, 1, camera, ramps, out Adornment both);
                Check(
                    both.Position.x == line.End.x && math.abs(both.Size.y - 3) < .0001f,
                    "Both-endpoint modules alternate their endpoint and scale",
                    checks
                );
                layer.Layout.x = 3;
                AbilityGeometry.Decor(
                    line,
                    recipe,
                    layer,
                    0,
                    1,
                    camera,
                    ramps,
                    out Adornment ground
                );
                Check(
                    math.abs(ground.Position.y - .6f) < .0001f
                        && math.abs(ground.Size.y - 2) < .0001f,
                    "Ground height is absolute world Y with endpoint-specific scale",
                    checks
                );
                AbilityGeometry.Decor(
                    line,
                    recipe,
                    layer,
                    0,
                    7,
                    camera,
                    ramps,
                    out Adornment later
                );
                AbilityGeometry.Decor(
                    line,
                    recipe,
                    layer,
                    2,
                    1,
                    camera,
                    ramps,
                    out Adornment nextRing
                );
                Check(
                    ground.Color.Equals(later.Color) && !ground.Color.Equals(nextRing.Color),
                    "Static endpoint ramps use particle index rather than animation time",
                    checks
                );
            }
            finally
            {
                ramps.Dispose();
            }
        }

        static void CheckGraphIdempotence(EffectLibrary library, List<string> checks)
        {
            EffectGraphCompiler.CompileLibrary(library, false, 3, true);
            var identities = new Dictionary<string, string>();
            using (var catalog = new NativeEffectCatalog(library))
            {
                foreach (
                    Technique technique in new[]
                    {
                        Technique.VfxBuffer,
                        Technique.VfxPerLine,
                        Technique.VfxPerLineBatched,
                    }
                )
                {
                    foreach (bool full in new[] { false, true })
                    {
                        string path =
                            "Assets/LineLab/Resources/"
                            + AbilityBackend.GraphResourcePath(catalog, technique, 3, full)
                            + ".vfx";
                        Check(
                            AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(path) != null,
                            "Compiled proof graph: " + path,
                            checks
                        );
                        identities[path] = AssetDatabase.AssetPathToGUID(path);
                    }
                }
            }

            EffectGraphCompiler.CompileLibrary(library, false, 3, true);
            foreach (KeyValuePair<string, string> asset in identities)
            {
                Check(
                    AssetDatabase.AssetPathToGUID(asset.Key) == asset.Value,
                    "Force compilation preserves graph GUID: " + asset.Key,
                    checks
                );
            }
        }

        static void CheckZeroLayers(EffectDefinition source, List<string> checks)
        {
            var effect = Object.Instantiate(source);
            var empty = ScriptableObject.CreateInstance<ParticleLayerAsset>();
            var library = ScriptableObject.CreateInstance<EffectLibrary>();
            Scene preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Acceptance zero-card pool")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                empty.Count = 0;
                effect.StableKey = "acceptance-zero";
                effect.Layers = new List<ParticleLayerAsset> { empty };
                library.Effects = new List<EffectDefinition> { effect };
                EffectGraphCompiler.CompileLibrary(library, false, 3, true);
                using (var catalog = new NativeEffectCatalog(library))
                using (var resources = new EffectRenderResources(catalog))
                {
                    Check(
                        catalog.LayerCapacities.Length == 1 && catalog.LayerCapacities[0] == 0,
                        "All-zero secondary catalog keeps a safe padded table",
                        checks
                    );
                    var camera = root.AddComponent<Camera>();
                    foreach (bool perAbility in new[] { false, true })
                    {
                        using (
                            var pool = new ParticleSurfacePool(
                                root.transform,
                                "Empty stream",
                                3,
                                0,
                                perAbility,
                                true,
                                false,
                                false,
                                resources,
                                new Bounds(Vector3.zero, Vector3.one)
                            )
                        )
                        {
                            pool.Present(default, camera, true);
                            Check(
                                pool.LiveParticles == 0 && pool.UploadBytes == 0,
                                "Zero-card native pool is safe; per ability=" + perAbility,
                                checks
                            );
                        }
                    }

                    string path =
                        "Assets/LineLab/Resources/"
                        + AbilityBackend.GraphResourcePath(catalog, Technique.VfxBuffer, 3, true)
                        + ".vfx";
                    var graph = Required<VisualEffectAsset>(path);
                    var vfxObject = new GameObject("Acceptance zero-spawn graph")
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                    };
                    SceneManager.MoveGameObjectToScene(vfxObject, preview);
                    var vfx = vfxObject.AddComponent<VisualEffect>();
                    vfx.visualEffectAsset = graph;
                    Check(
                        vfx.HasFloat("SpawnLayer0") && vfx.GetFloat("SpawnLayer0") == 0,
                        "Zero-card graph exposes a zero burst",
                        checks
                    );
                    MethodInfo sourceMethod = typeof(EffectGraphCompiler).GetMethod(
                        "Source",
                        BindingFlags.Static | BindingFlags.NonPublic
                    );
                    string hlsl = (string)
                        sourceMethod.Invoke(null, new object[] { catalog, true, 1, 0 });
                    Check(
                        hlsl.Contains("particleId % 1")
                            && !hlsl.Contains(" / 0")
                            && !hlsl.Contains(" % 0"),
                        "Zero-card generated indexing uses a safe denominator",
                        checks
                    );
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
                Object.DestroyImmediate(library);
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(empty);
            }
        }

        static NativeArray<float4> CopyRamps(NativeEffectCatalog catalog)
        {
            var source = catalog.RampSamples;
            var copy = new NativeArray<float4>(source.Length, Allocator.Temp);
            for (int i = 0; i < source.Length; i++)
            {
                copy[i] = source[i];
            }

            return copy;
        }

        static LineData ProbeLine(EffectRecipeData recipe)
        {
            return new LineData
            {
                Start = new float4(-2, 2, 0, 1),
                End = new float4(7, 4, 0, recipe.Timing.w),
                Color = new float4(1),
                Shape = new float4(0, .7f, -2, -10000),
            };
        }

        static Dictionary<string, string> SnapshotSources()
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (
                string guid in AssetDatabase.FindAssets("", new[] { "Assets/LineLab/Recipes" })
            )
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (File.Exists(path))
                {
                    paths.Add(path);
                }
            }

            foreach (
                string guid in AssetDatabase.FindAssets(
                    "t:Scene",
                    new[] { "Assets/LineLab/Scenes" }
                )
            )
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (File.Exists(path))
                {
                    paths.Add(path);
                }
            }

            foreach (string library in new[] { "EffectLibrary", "ArtistLibrary", "ProofLibrary" })
            {
                string path = "Assets/LineLab/Resources/Production/" + library + ".asset";
                if (File.Exists(path))
                {
                    paths.Add(path);
                }
            }

            foreach (string path in paths.ToArray())
            {
                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                {
                    if (File.Exists(dependency) && !dependency.Contains("/Generated/"))
                    {
                        paths.Add(dependency);
                    }
                }
            }

            foreach (string path in paths.ToArray())
            {
                if (File.Exists(path + ".meta"))
                {
                    paths.Add(path + ".meta");
                }
            }

            return paths.ToDictionary(path => path, path => Hash(File.ReadAllBytes(path)));
        }

        static List<CameraSnapshot> SnapshotCameras()
        {
            var snapshots = new List<CameraSnapshot>();
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (Camera camera in root.GetComponentsInChildren<Camera>(true))
                    {
                        Transform transform = camera.transform;
                        snapshots.Add(
                            new CameraSnapshot
                            {
                                Camera = camera,
                                Position = transform.position,
                                Rotation = transform.rotation,
                                LocalPosition = transform.localPosition,
                                LocalRotation = transform.localRotation,
                                LocalScale = transform.localScale,
                                Parent = transform.parent,
                            }
                        );
                    }
                }
            }

            return snapshots;
        }

        static T CreateMissing<T>(string path, Func<T> create, List<string> created)
            where T : ScriptableObject
        {
            T existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
            {
                return existing;
            }

            if (File.Exists(path))
            {
                throw new InvalidOperationException(
                    "An asset of another type already occupies " + path
                );
            }

            T asset = create();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssetIfDirty(asset);
            created.Add(path);
            return asset;
        }

        static T Required<T>(string path)
            where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new InvalidOperationException("Missing acceptance source: " + path);
            }

            return asset;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separator = path.LastIndexOf('/');
            string parent = path.Substring(0, separator);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
        }

        static void Check(bool condition, string name, List<string> checks)
        {
            if (!condition)
            {
                throw new InvalidOperationException("Authoring acceptance: " + name);
            }

            checks.Add(name);
        }

        static string Hash(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter
                    .ToString(sha.ComputeHash(bytes))
                    .Replace("-", "")
                    .ToLowerInvariant();
            }
        }

        static void Progress(string message)
        {
            File.WriteAllText(ProgressPath, DateTime.UtcNow.ToString("O") + " " + message);
        }
    }
}
