using UnityEngine;

namespace LineLab.Production
{
    public enum LinePathKind
    {
        Helix = 0,
        Lightning = 1,
        Wave = 2,
        Straight = 3,
    }

    /// <summary>Reusable centerline motion. Values are baked once before jobs run.</summary>
    [CreateAssetMenu(menuName = "Line Lab/Path", fileName = "New Path")]
    public sealed class LinePathAsset : ScriptableObject
    {
        [Tooltip(
            "Algorithm used by every renderer; a new algorithm requires CPU/GPU implementation."
        )]
        public LinePathKind Kind = LinePathKind.Helix;

        [Header("Displacement (world metres)")]
        [Min(0)]
        public float UpAmplitude = .45f;

        [Min(0)]
        public float SideAmplitude = .22f;

        [Header("Motion")]
        [Min(0), Tooltip("Radians along the entire line, not cycles per metre.")]
        public float SpatialFrequency = 18;

        [Tooltip("Phase speed in radians per second.")]
        public float Speed = 4;

        [Min(1), Tooltip("Lightning interpolation cells along the line.")]
        public float NoiseCells = 19;

        [Min(0), Tooltip("Lightning changes per second; zero freezes the noise.")]
        public float NoiseTicks = 18;

        [Tooltip("Shared phase offset in radians.")]
        public float StartingPhase;

        [Header("Brightness pulse")]
        [Min(0)]
        public float BaseIntensity = .65f;

        [Min(0)]
        public float PulseDepth = .35f;

        [Min(0)]
        public float PulseFrequency = 30;
        public float PulseSpeed = 12;
    }
}
