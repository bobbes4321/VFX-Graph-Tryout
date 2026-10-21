using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace LineLab.Production.Editor
{
    /// <summary>Explicit source initialization, topology compilation and standalone builds.</summary>
    public static class ProductionAssets
    {
        [MenuItem("Line Lab/3 - Compile authored effect libraries")]
        public static void Generate()
        {
            EffectAuthoringPresets.EnsureSampleAssets();
            EffectGraphCompiler.CompileLibrary(
                Resources.Load<EffectLibrary>("Production/EffectLibrary"),
                true
            );
            EffectGraphCompiler.CompileLibrary(
                Resources.Load<EffectLibrary>("Production/ArtistLibrary"),
                true
            );
            Debug.Log(
                "Authored libraries compiled. Recipes, imported textures and the scene were preserved."
            );
        }

        public static string QueueGenerate()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Generate();
                    File.WriteAllText("Logs/authored-generation.txt", "SUCCESS");
                }
                catch (Exception exception)
                {
                    File.WriteAllText("Logs/authored-generation.txt", exception.ToString());
                    Debug.LogException(exception);
                }
            };
            return "Authored library compilation queued";
        }

        [MenuItem("Line Lab/Authoring/Create New Sample Scene")]
        public static void CreateNewScene()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create an additional effect showcase",
                "Authored_Abilities",
                "unity",
                "Choose a new scene path. Existing scenes are never overwritten."
            );
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (File.Exists(path))
            {
                throw new InvalidOperationException("Choose a new path; the scene already exists.");
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            ProductionArt.Build();
            var camera = new GameObject(
                "RTS camera",
                typeof(Camera),
                typeof(AudioListener)
            ).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(19, 28, -33);
            camera.transform.LookAt(new Vector3(0, 1, .5f));
            camera.fieldOfView = 43;
            camera.allowHDR = true;
            camera.backgroundColor = new Color(.045f, .09f, .13f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            var sun = new GameObject("Sun", typeof(Light)).GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1, .86f, .68f);
            sun.intensity = 2.4f;
            sun.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(42, -35, 0);
            new GameObject("Atmosphere", typeof(Volume)).GetComponent<Volume>().sharedProfile =
                Resources.Load<VolumeProfile>("Production/Atmosphere");
            new GameObject("Authored effect laboratory", typeof(ProductionController));
            EditorSceneManager.SaveScene(scene, path);
        }

        [MenuItem("Line Lab/4 - Build authored combat effects player")]
        public static void Build()
        {
            Generate();
            const string directory = "Builds/AuthoredLab";
            Directory.CreateDirectory(directory);
            var report = BuildPipeline.BuildPlayer(
                new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/LineLab/Scenes/03_Complete_Abilities.unity" },
                    locationPathName = directory + "/AuthoredLab.exe",
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                }
            );
            File.WriteAllText(
                "Builds/authored-build-status.txt",
                report.summary.result
                    + " / "
                    + report.summary.totalErrors
                    + " errors / "
                    + report.summary.totalTime
            );
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    "Authored player build failed. Inspect the Unity Console."
                );
            }

            WriteSourceManifest(directory);
            File.WriteAllText(
                directory + "/build-report.json",
                JsonUtility.ToJson(
                    new BuildInfo
                    {
                        unity = Application.unityVersion,
                        buildGuid = report.summary.guid.ToString(),
                        result = report.summary.result.ToString(),
                        errors = report.summary.totalErrors,
                        totalBytes = report.summary.totalSize,
                        utc = DateTime.UtcNow.ToString("O"),
                    },
                    true
                )
            );
        }

        [Serializable]
        sealed class BuildInfo
        {
            public string unity;
            public string buildGuid;
            public string result;
            public string utc;
            public int errors;
            public ulong totalBytes;
        }

        [Serializable]
        sealed class Manifest
        {
            public string utc;
            public Entry[] files;
        }

        [Serializable]
        sealed class Entry
        {
            public string path;
            public string sha256;
        }

        static void WriteSourceManifest(string directory)
        {
            string[] roots = { "Assets/LineLab", "ProjectSettings", "Packages" };
            var files = roots
                .SelectMany(root => Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(path => new Entry { path = path.Replace('\\', '/'), sha256 = Hash(path) })
                .ToArray();
            File.WriteAllText(
                directory + "/source-manifest.json",
                JsonUtility.ToJson(
                    new Manifest { utc = DateTime.UtcNow.ToString("O"), files = files },
                    true
                )
            );
            var binaries = new[]
            {
                directory + "/AuthoredLab.exe",
                directory + "/AuthoredLab_Data/Managed/Assembly-CSharp.dll",
                directory + "/AuthoredLab_Data/Plugins/x86_64/lib_burst_generated.dll",
            }
                .Where(File.Exists)
                .Select(path => new Entry { path = path.Replace('\\', '/'), sha256 = Hash(path) })
                .ToArray();
            File.WriteAllText(
                directory + "/binary-manifest.json",
                JsonUtility.ToJson(
                    new Manifest { utc = DateTime.UtcNow.ToString("O"), files = binaries },
                    true
                )
            );
        }

        static string Hash(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var algorithm = SHA256.Create())
                return string.Concat(
                    algorithm.ComputeHash(stream).Select(value => value.ToString("x2"))
                );
        }

        public static string QueueBuild()
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    Build();
                }
                catch (Exception exception)
                {
                    File.WriteAllText("Builds/authored-build-status.txt", exception.ToString());
                    Debug.LogException(exception);
                }
            };
            return "Authored standalone build queued";
        }
    }
}
