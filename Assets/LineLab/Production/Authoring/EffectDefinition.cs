using System.Collections.Generic;
using UnityEngine;

namespace LineLab.Production
{
    /// <summary>Artist-authored composition, independent of rendering technique.</summary>
    [CreateAssetMenu(menuName = "Line Lab/Effect Definition", fileName = "New Effect")]
    public sealed class EffectDefinition : ScriptableObject
    {
        [Tooltip("Permanent gameplay lookup key. Renaming an asset need not change this key.")]
        public string StableKey = "new-effect";

        [Header("Shared appearance")]
        [ColorUsage(true, true)]
        public Color Tint = Color.white;

        [Min(.001f), Tooltip("Base ribbon width in world metres.")]
        public float Width = .22f;

        [Min(.001f)]
        public float ChargeSeconds = .55f;

        [Min(.001f)]
        public float CoreReleaseSeconds = .24f;
        public LinePathAsset Path;

        [Header("Reusable layers")]
        [Tooltip("Ordered ribbon modules. Empty is allowed for particle-only effects. Maximum 8.")]
        public List<RibbonLayerAsset> Ribbons = new List<RibbonLayerAsset>();

        [Tooltip(
            "Ordered secondary modules. Reuse a module by referencing the same asset. Maximum 12."
        )]
        public List<ParticleLayerAsset> Layers = new List<ParticleLayerAsset>();
    }
}
