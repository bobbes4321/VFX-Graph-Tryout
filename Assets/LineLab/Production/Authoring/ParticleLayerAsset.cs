using UnityEngine;

namespace LineLab.Production
{
    public enum ParticleMotionKind
    {
        Traveling = 0,
        Ballistic = 1,
        Corona = 2,
        Ground = 3,
    }

    public enum EffectEndpoint
    {
        Both = 0,
        Source = 1,
        Target = 2,
    }

    /// <summary>A reusable analytic layer. Duplicate this asset to create a new mote or corona.</summary>
    [CreateAssetMenu(menuName = "Line Lab/Particle Layer", fileName = "New Particle Layer")]
    public sealed class ParticleLayerAsset : ScriptableObject
    {
        [Header("Layer")] public ParticleMotionKind Kind = ParticleMotionKind.Traveling;

        [
            Range(0, 4096),
            Tooltip("Reserved particles per ability for this layer. Zero disables it.")
        ]
        public int Count = 48;

        public Texture2D Profile;

        [Header("Color")] [ColorUsage(true, true)]
        public Color Tint = Color.white;

        [
            GradientUsage(true),
            Tooltip(
                "Traveling: distance along line. Ballistic: normalized age. Corona/Ground: normalized particle index."
            )
        ]
        public Gradient ColorOverLife = new Gradient();

        [Range(0, 1)] public float Opacity = .95f;

        [Range(0, 1)] public float Whiten = .35f;

        [
            Min(.001f),
            Tooltip("Fade after Release, in seconds. Also contributes to ability retirement.")
        ]
        public float ReleaseSeconds = .8f;

        [Header("Size (world metres)")] [Min(0)]
        public float MinimumSize = .075f;

        [Min(0)] public float MaximumSize = .235f;

        [Min(.001f), Tooltip("Width divided by height.")]
        public float AspectRatio = 1;

        [Tooltip(
            "Traveling: distance along line. Ballistic: normalized age. Corona/Ground: normalized particle index."
        )]
        public AnimationCurve SizeOverLife = AnimationCurve.Linear(0, 1, 1, 1);

        [Min(0)] public float FadeExponent = 1;

        [Header("Motion")]
        [Tooltip(
            "Traveling/Ballistic: normalized motion cycles per second. Negative reverses motion. Corona: pulse angular speed in radians per second."
        )]
        public float CyclesPerSecond = .38f;

        [
            Min(0),
            Tooltip(
                "Traveling: orbit radius in world metres. Corona: dimensionless size-pulse depth."
            )
        ]
        public float OrbitRadius = .18f;

        [Min(0)] public float OrbitSpread = .43f;

        [Tooltip("Rotation speed in radians per second.")]
        public float Spin = 1;

        [Header("Ballistic motion")] [Min(0)] public float RadialSpeed = 1.35f;
        public float UpwardSpeed = .81f;

        [Min(0)] public float UpwardVariation = 2.16f;

        [Tooltip("Positive pulls downward; negative makes particles float upward.")]
        public float Gravity = -.3f;

        [Header("Endpoint placement")] public EffectEndpoint Endpoint = EffectEndpoint.Both;

        [Tooltip("Ground: absolute world Y. Other endpoint layers: vertical offset.")]
        public float Height = .19f;

        [Min(0)] public float SourceScale = 1;

        [Min(0)] public float TargetScale = 1;
    }
}
