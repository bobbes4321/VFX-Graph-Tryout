using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;

namespace LineLab.Production.Editor
{
    /// <summary>Regression checks for the per-technique runtime organization.</summary>
    public static class TechniqueValidation
    {
        [UnityEditor.MenuItem("Line Lab/Validation/Validate technique organization")]
        public static void ValidateFromMenu() => Debug.Log(Run());

        [UnityEditor.MenuItem("Line Lab/Validation/Validate technique organization", true)]
        public static bool CanValidateFromMenu() =>
            !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode
            && !UnityEditor.EditorApplication.isCompiling;

        public static string Run()
        {
            if (Application.isPlaying)
                throw new InvalidOperationException(
                    "Run technique organization checks in Edit mode."
                );
            int checks = 0;
            void Check(bool condition, string description)
            {
                if (!condition)
                    throw new InvalidOperationException(description);
                checks++;
            }

            var preview = EditorSceneManager.NewPreviewScene();
            var cameraObject = new GameObject("Technique validation camera");
            SceneManager.MoveGameObjectToScene(cameraObject, preview);
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = new Vector3(10, 20, -15);
            try
            {
                using (
                    var catalog = new NativeEffectCatalog(
                        Resources.Load<EffectLibrary>("Production/EffectLibrary")
                    )
                )
                using (var world = new AbilityWorld(3, catalog))
                {
                    void Begin(uint generation)
                    {
                        for (int i = 0; i < 3; i++)
                            world.Writer.Begin(
                                new LineHandle(i, generation),
                                new float3(i, 2, 0),
                                new float3(i + 5, 2, 0),
                                new EffectId(i % catalog.EffectCount),
                                0
                            );
                        world.PreparePresentation(default, 1).Complete();
                    }

                    Begin(1);
                    foreach (bool full in new[] { false, true })
                    foreach (Technique technique in Enum.GetValues(typeof(Technique)))
                    {
                        bool vfx =
                            technique == Technique.VfxBuffer
                            || technique == Technique.VfxPerLine
                            || technique == Technique.VfxPerLineBatched;
                        bool particle =
                            technique >= Technique.ParticleBillboardShared
                            && technique <= Technique.ParticleJobs;
                        bool pooled = technique == Technique.ParticleBillboardPerLine;
                        string label = technique + "/full=" + full;
                        using (
                            var backend = new AbilityBackend(technique, 3, camera, full, catalog)
                        )
                        {
                            SceneManager.MoveGameObjectToScene(backend.Root, preview);
                            int streamCount = 1 + (full ? catalog.LayerSlots : 0);
                            var systems = backend.Root.GetComponentsInChildren<ParticleSystem>(
                                true
                            );
                            var graphs = backend.Root.GetComponentsInChildren<VisualEffect>(true);
                            int expectedSystems = particle ? streamCount * (pooled ? 3 : 1) : 0;
                            Check(
                                systems.Length == expectedSystems,
                                label + " particle system count"
                            );
                            Check(
                                graphs.Length
                                    == (vfx ? (technique == Technique.VfxBuffer ? 1 : 3) : 0),
                                label + " graph count"
                            );
                            Check(
                                backend.Root.GetComponentsInChildren<LineRenderer>(true).Length
                                    == (
                                        technique == Technique.LineRenderer
                                            ? 3 * catalog.RibbonSlots
                                            : 0
                                    ),
                                label + " ribbon count"
                            );
                            Check(
                                backend.Root.GetComponentsInChildren<MeshFilter>(true).Length
                                    == (technique == Technique.BurstMesh ? 1 : 0),
                                label + " combined mesh count"
                            );
                            Check(
                                backend.InstancingActive
                                    == (
                                        technique == Technique.VfxPerLineBatched
                                        || technique == Technique.ParticleMeshInstanced
                                    ),
                                label + " instancing"
                            );
                            foreach (var system in systems)
                                Check(
                                    system
                                        .GetComponent<ParticleSystemRenderer>()
                                        .enableGPUInstancing
                                        == (technique == Technique.ParticleMeshInstanced),
                                    label + " particle instancing"
                                );

                            int cards =
                                catalog.AllocatedCoreQuadsPerAbility
                                + (full ? catalog.LayerCapacities.Sum() : 0);
                            long copiedBytes =
                                3L * cards * (Marshal.SizeOf<ParticleSystem.Particle>() + 16);
                            long secondaryBytes = full && catalog.LayerSlots > 0 ? 3 * 64L : 0;
                            long expectedBytes = technique switch
                            {
                                Technique.VfxBuffer => 3 * 64L,
                                Technique.LineRenderer => 3L
                                    * catalog.RibbonSlots
                                    * (catalog.Segments + 1)
                                    * 12
                                    + secondaryBytes,
                                Technique.BurstMesh => 3L
                                    * catalog.RibbonSlots
                                    * catalog.Segments
                                    * 4
                                    * 36
                                    + secondaryBytes,
                                Technique.Procedural => 3L * catalog.RibbonSlots * 64
                                    + secondaryBytes,
                                _ => particle ? copiedBytes : 0,
                            };
                            backend.Present(world, 1, true);
                            Check(
                                backend.UploadBytes == expectedBytes,
                                label + " first-frame upload bytes"
                            );
                            Check(
                                backend.StreamParticleCounts().Length == streamCount,
                                label + " census shape"
                            );
                            if (particle)
                                Check(
                                    backend.LiveParticles() == 3 * cards,
                                    label + " initial particle population"
                                );
                            backend.Present(world, 1.1f, false);
                            Check(
                                backend.UploadBytes
                                    == (technique == Technique.ParticleJobs ? 0 : expectedBytes),
                                label + " steady-state upload bytes"
                            );

                            // Retire and reuse the same slots; pooled components must wake again.
                            for (int i = 0; i < 3; i++)
                                world.Writer.Release(new LineHandle(i, 1), 2);
                            world.PreparePresentation(default, 100).Complete();
                            backend.Present(world, 100, false);
                            if (
                                pooled
                                || technique == Technique.VfxPerLine
                                || technique == Technique.VfxPerLineBatched
                            )
                                Check(
                                    backend.Root.GetComponentsInChildren<ParticleSystem>().Length
                                        == 0
                                        && backend
                                            .Root.GetComponentsInChildren<VisualEffect>()
                                            .Length == 0,
                                    label + " pooled retirement"
                                );
                            Begin(2);
                            // Shuriken activation needs a player-loop turn between disable
                            // and enable. Its reactivation is covered by the play-mode probe.
                            if (!pooled)
                                backend.Present(world, 1, false);
                            if (
                                technique == Technique.VfxPerLine
                                || technique == Technique.VfxPerLineBatched
                            )
                                Check(
                                    backend.Root.GetComponentsInChildren<VisualEffect>().Length
                                        == 3,
                                    label + " VFX reactivation"
                                );
                            // Restore generation 1 for the next independent backend.
                            for (int i = 0; i < 3; i++)
                            {
                                world.Generations[i] = 0;
                            }
                            Begin(1);
                            backend.Dispose();
                            backend.Dispose();
                            Check(backend.Root == null, label + " idempotent hierarchy cleanup");
                        }
                    }
                }

                // A padded zero-card stream must remain safe for every particle variant.
                var library = ScriptableObject.CreateInstance<EffectLibrary>();
                var effect = UnityEngine.Object.Instantiate(
                    Resources.Load<EffectLibrary>("Production/EffectLibrary").Effects[0]
                );
                var layer = ScriptableObject.CreateInstance<ParticleLayerAsset>();
                try
                {
                    layer.Count = 0;
                    effect.Layers = new List<ParticleLayerAsset> { layer };
                    library.Effects = new List<EffectDefinition> { effect };
                    using (var catalog = new NativeEffectCatalog(library))
                    using (var world = new AbilityWorld(3, catalog))
                    {
                        world.PreparePresentation(default, 1).Complete();
                        for (
                            Technique technique = Technique.ParticleBillboardShared;
                            technique <= Technique.ParticleJobs;
                            technique++
                        )
                            using (
                                var backend = new AbilityBackend(
                                    technique,
                                    3,
                                    camera,
                                    true,
                                    catalog
                                )
                            )
                            {
                                SceneManager.MoveGameObjectToScene(backend.Root, preview);
                                backend.Present(world, 1, true);
                                Check(
                                    backend.StreamParticleCounts()[1] == 0,
                                    technique + " zero-card secondary stream"
                                );
                            }
                    }
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(layer);
                    UnityEngine.Object.DestroyImmediate(effect);
                    UnityEngine.Object.DestroyImmediate(library);
                }
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
            return checks
                + " technique topology, uploads, pooling, zero-card and disposal checks passed";
        }
    }
}
