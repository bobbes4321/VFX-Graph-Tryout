using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace LineLab.Production.Editor
{
    /// <summary>Standard Unity asset editing plus an explicit, nondestructive pool rebuild.</summary>
    public sealed class EffectAuthoringWorkbench : EditorWindow
    {
        [SerializeField]
        EffectLibrary library;

        [SerializeField]
        int selectedEffect;
        Vector2 scroll;
        string validationMessage;
        bool validationFailed;
        readonly Dictionary<int, UnityEditor.Editor> editors =
            new Dictionary<int, UnityEditor.Editor>();
        readonly HashSet<int> expanded = new HashSet<int>();

        [MenuItem("Line Lab/Authoring/Effect Workbench")]
        public static void Open()
        {
            var window = GetWindow<EffectAuthoringWorkbench>("Effect Workbench");
            window.minSize = new Vector2(420, 460);
            if (window.library == null)
            {
                window.library =
                    Resources.Load<EffectLibrary>("Production/ArtistLibrary")
                    ?? Resources.Load<EffectLibrary>("Production/EffectLibrary");
            }

            window.Show();
        }

        public static void Open(EffectLibrary selectedLibrary)
        {
            Open();
            GetWindow<EffectAuthoringWorkbench>().library = selectedLibrary;
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Reusable line effects", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Edit source assets below. Changes use Unity Undo. Rebuild Preview applies edits to the live player without regenerating the scene or overwriting assets.",
                MessageType.Info
            );
            library = (EffectLibrary)
                EditorGUILayout.ObjectField("Library", library, typeof(EffectLibrary), false);
            if (library == null)
            {
                if (GUILayout.Button("Create missing sample recipes"))
                {
                    EffectAuthoringPresets.EnsureSampleAssets();
                    library = Resources.Load<EffectLibrary>("Production/ArtistLibrary");
                }

                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate / allocation summary"))
                {
                    Validate();
                }

                if (GUILayout.Button("Save assets"))
                {
                    AssetDatabase.SaveAssets();
                }
            }

            if (GUILayout.Button("Compile Library for current preview capacity"))
            {
                CompileLibrary();
            }

            if (!string.IsNullOrEmpty(validationMessage))
            {
                EditorGUILayout.HelpBox(
                    validationMessage,
                    validationFailed ? MessageType.Error : MessageType.Info
                );
            }

            string[] names =
                library.Effects == null
                    ? Array.Empty<string>()
                    : library
                        .Effects.Select(effect => effect == null ? "<missing recipe>" : effect.name)
                        .ToArray();
            if (names.Length > 0)
            {
                selectedEffect = Mathf.Clamp(selectedEffect, 0, names.Length - 1);
                selectedEffect = EditorGUILayout.Popup("Preview effect", selectedEffect, names);
            }

            using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying || names.Length == 0))
            {
                if (GUILayout.Button("Rebuild Preview with this library and effect"))
                {
                    RebuildPreview();
                }
            }

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.LabelField(
                    "Enter Play Mode in the complete-abilities scene to preview.",
                    EditorStyles.wordWrappedMiniLabel
                );
            }

            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawAsset(library, "Library and ordered catalog", true);
            if (names.Length > 0 && library.Effects[selectedEffect] != null)
            {
                EffectDefinition effect = library.Effects[selectedEffect];
                DrawAsset(effect, "Selected effect: " + effect.name, true);
                DrawAsset(effect.Path, "Path module");
                if (effect.Ribbons != null)
                {
                    for (int i = 0; i < effect.Ribbons.Count; i++)
                    {
                        DrawAsset(effect.Ribbons[i], "Ribbon " + i);
                    }
                }

                if (effect.Layers != null)
                {
                    for (int i = 0; i < effect.Layers.Count; i++)
                    {
                        DrawAsset(effect.Layers[i], "Particle layer " + i);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        void DrawAsset(UnityEngine.Object asset, string label, bool defaultExpanded = false)
        {
            if (asset == null)
            {
                return;
            }

            int id = asset.GetInstanceID();
            if (!editors.TryGetValue(id, out UnityEditor.Editor editor) || editor == null)
            {
                editor = UnityEditor.Editor.CreateEditor(asset);
                editors[id] = editor;
                if (defaultExpanded)
                {
                    expanded.Add(id);
                }
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool open = EditorGUILayout.Foldout(
                    expanded.Contains(id),
                    label + " - " + asset.name,
                    true
                );
                if (open)
                {
                    expanded.Add(id);
                }
                else
                {
                    expanded.Remove(id);
                }

                if (!open)
                {
                    return;
                }

                EditorGUILayout.ObjectField("Source asset", asset, asset.GetType(), false);
                editor.OnInspectorGUI();
            }
        }

        void Validate()
        {
            try
            {
                using (var catalog = new NativeEffectCatalog(library))
                    validationMessage =
                        catalog.EffectCount
                        + " effects; "
                        + catalog.RibbonSlots
                        + " reserved ribbon slots x "
                        + catalog.Segments
                        + " segments; secondary slot capacities ["
                        + string.Join(", ", catalog.LayerCapacities)
                        + "]. Core/full quads per ability: "
                        + catalog.AllocatedCoreQuadsPerAbility
                        + "/"
                        + catalog.AllocatedFullQuadsPerAbility
                        + ". Texture tiles: "
                        + catalog.TextureCount
                        + ". Content SHA-256: "
                        + catalog.Fingerprint;
                validationFailed = false;
            }
            catch (Exception exception)
            {
                validationMessage = exception.Message;
                validationFailed = true;
            }
        }

        void CompileLibrary()
        {
            try
            {
                NativeEffectCatalog.ValidateLibrary(library);
                EffectGraphCompiler.CompileLibrary(
                    library,
                    false,
                    PreviewCount(FindPreviewController())
                );
                validationFailed = false;
                validationMessage =
                    "Library compiled for the current preview capacity. Source assets and scene preserved.";
            }
            catch (Exception exception)
            {
                validationFailed = true;
                validationMessage = (exception.InnerException ?? exception).Message;
            }
        }

        static MonoBehaviour FindPreviewController()
        {
            return Resources
                .FindObjectsOfTypeAll<MonoBehaviour>()
                .FirstOrDefault(component =>
                    component != null
                    && component.gameObject.scene.IsValid()
                    && component.GetType().FullName == "LineLab.Production.ProductionController"
                );
        }

        static int PreviewCount(MonoBehaviour controller)
        {
            if (controller == null)
            {
                return 3;
            }

            FieldInfo countField = controller
                .GetType()
                .GetField("Count", BindingFlags.Instance | BindingFlags.Public);
            return countField != null && countField.FieldType == typeof(int)
                ? Mathf.Clamp(
                    (int)countField.GetValue(controller),
                    1,
                    EffectRecipeLayout.MaximumAbilities
                )
                : 3;
        }

        void RebuildPreview()
        {
            Validate();
            if (validationFailed)
            {
                return;
            }

            try
            {
                // Reflection avoids forcing the reusable authoring editor to depend on a demo host.
                MonoBehaviour controller = FindPreviewController();
                if (controller == null)
                {
                    throw new InvalidOperationException(
                        "Open the complete-abilities scene and enter Play Mode first."
                    );
                }

                Type type = controller.GetType();
                FieldInfo field = type.GetField(
                    "Library",
                    BindingFlags.Instance | BindingFlags.Public
                );
                if (field == null || field.FieldType != typeof(EffectLibrary))
                {
                    throw new InvalidOperationException(
                        "The preview host must expose public EffectLibrary Library."
                    );
                }

                MethodInfo rebuild = type.GetMethod("RebuildLibrary", Type.EmptyTypes);
                MethodInfo select = type.GetMethod("SetPreviewEffect", new[] { typeof(int) });
                if (rebuild == null || select == null)
                {
                    throw new InvalidOperationException(
                        "The preview host is missing RebuildLibrary() or SetPreviewEffect(int)."
                    );
                }

                // Compile only this preview's required capacity. Full scale generation is
                // a separate build operation, so an artist edit does not rebuild every pool.
                EffectGraphCompiler.CompileLibrary(library, false, PreviewCount(controller));
                Undo.RecordObject(controller, "Select effect preview library");
                field.SetValue(controller, library);
                rebuild.Invoke(controller, null);
                select.Invoke(controller, new object[] { selectedEffect });
                validationMessage =
                    "Preview rebuilt from source assets. Scene and authored assets preserved.";
            }
            catch (Exception exception)
            {
                validationFailed = true;
                validationMessage = (exception.InnerException ?? exception).Message;
            }
        }

        void OnDisable()
        {
            foreach (UnityEditor.Editor editor in editors.Values)
            {
                if (editor != null)
                {
                    DestroyImmediate(editor);
                }
            }

            editors.Clear();
        }
    }

    [CustomEditor(typeof(EffectLibrary))]
    public sealed class EffectLibraryInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            if (GUILayout.Button("Open Effect Workbench"))
            {
                EffectAuthoringWorkbench.Open((EffectLibrary)target);
            }
        }
    }
}
