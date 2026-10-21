using UnityEditor;
using UnityEngine;

namespace LineLab.Production.Editor
{
    /// <summary>Show fields that matter to the selected motion building block.</summary>
    [CustomEditor(typeof(ParticleLayerAsset))]
    [CanEditMultipleObjects]
    public sealed class ParticleLayerInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Field("Kind");
            Field("Count");
            Field("Profile");
            EditorGUILayout.Space();
            Heading("Color and release");
            Fields("Tint", "ColorOverLife", "Opacity", "Whiten", "ReleaseSeconds");
            Heading("Size in world metres");
            Fields("MinimumSize", "MaximumSize", "AspectRatio", "SizeOverLife");
            var kind = (ParticleMotionKind)serializedObject.FindProperty("Kind").enumValueIndex;
            bool mixed = serializedObject.FindProperty("Kind").hasMultipleDifferentValues;
            if (
                mixed
                || kind == ParticleMotionKind.Traveling
                || kind == ParticleMotionKind.Ballistic
            )
            {
                Field("FadeExponent");
            }

            Heading("Motion");
            if (mixed || kind == ParticleMotionKind.Traveling)
            {
                Fields("CyclesPerSecond", "OrbitRadius", "OrbitSpread", "Spin");
            }

            if (mixed || kind == ParticleMotionKind.Ballistic)
            {
                Fields(
                    "CyclesPerSecond",
                    "RadialSpeed",
                    "UpwardSpeed",
                    "UpwardVariation",
                    "Gravity",
                    "Spin"
                );
            }

            if (mixed || kind == ParticleMotionKind.Corona)
            {
                Field("CyclesPerSecond", "Pulse speed (rad/s)");
                Field("OrbitRadius", "Pulse amplitude");
            }

            if (mixed || kind == ParticleMotionKind.Corona || kind == ParticleMotionKind.Ground)
            {
                Field("Spin");
            }

            if (mixed || kind != ParticleMotionKind.Traveling)
            {
                Heading("Endpoint placement");
                Fields("Endpoint", "Height", "SourceScale", "TargetScale");
            }

            serializedObject.ApplyModifiedProperties();
        }

        void Fields(params string[] names)
        {
            foreach (string name in names)
            {
                Field(name);
            }
        }

        void Field(string name)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(name), true);
        }

        void Field(string name, string label)
        {
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty(name),
                new GUIContent(label),
                true
            );
        }

        static void Heading(string title)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }
    }

    [CustomEditor(typeof(LinePathAsset))]
    [CanEditMultipleObjects]
    public sealed class LinePathInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            Field("Kind");
            var kind = (LinePathKind)serializedObject.FindProperty("Kind").enumValueIndex;
            bool mixed = serializedObject.FindProperty("Kind").hasMultipleDifferentValues;
            if (mixed || kind != LinePathKind.Straight)
            {
                Fields("UpAmplitude", "SideAmplitude");
                if (mixed || kind == LinePathKind.Lightning)
                {
                    Fields("NoiseCells", "NoiseTicks");
                }

                if (mixed || kind == LinePathKind.Helix || kind == LinePathKind.Wave)
                {
                    Fields("SpatialFrequency", "Speed");
                }

                Field("StartingPhase");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Brightness pulse", EditorStyles.boldLabel);
            Fields("BaseIntensity", "PulseDepth", "PulseFrequency", "PulseSpeed");
            serializedObject.ApplyModifiedProperties();
        }

        void Fields(params string[] names)
        {
            foreach (string name in names)
            {
                Field(name);
            }
        }

        void Field(string name)
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(name), true);
        }
    }
}
