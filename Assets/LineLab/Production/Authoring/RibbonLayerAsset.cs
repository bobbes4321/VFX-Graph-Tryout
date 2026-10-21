using UnityEngine;

namespace LineLab.Production
{
    /// <summary>One reusable ribbon, for example a hot core or broad low-opacity halo.</summary>
    [CreateAssetMenu(menuName = "Line Lab/Ribbon Layer", fileName = "New Ribbon")]
    public sealed class RibbonLayerAsset : ScriptableObject
    {
        [Header("Shape")]
        [Min(0)]
        public float WidthMultiplier = 1;

        [Range(0, 1)]
        public float Opacity = 1;

        [
            Range(0, 1),
            Tooltip("Mix the effect's tint toward white before multiplying this layer's tint.")
        ]
        public float Whiten = .45f;

        [Tooltip("Offset from the path phase, in radians.")]
        public float PhaseOffset;

        [Tooltip("Width along normalized source-to-target distance.")]
        public AnimationCurve WidthAlongLine = AnimationCurve.Linear(0, 1, 1, 1);

        [Header("Surface")]
        [Tooltip("Null uses a white profile. Textures are packed into the catalog atlas once.")]
        public Texture2D Profile;

        [ColorUsage(true, true)]
        public Color Tint = Color.white;

        [
            GradientUsage(true),
            Tooltip("Color and alpha along normalized source-to-target distance.")
        ]
        public Gradient ColorAlongLine = new Gradient();
    }
}
