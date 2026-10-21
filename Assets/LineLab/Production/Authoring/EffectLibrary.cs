using System.Collections.Generic;
using UnityEngine;

namespace LineLab.Production
{
    /// <summary>Stable ordered catalog. IDs are indices and remain valid until the catalog is rebuilt.</summary>
    [CreateAssetMenu(menuName = "Line Lab/Effect Library", fileName = "EffectLibrary")]
    public sealed class EffectLibrary : ScriptableObject
    {
        [
            Range(1, 128),
            Tooltip(
                "Segments per ribbon. Changing structural fields requires rebuilding preview pools."
            )
        ]
        public int Segments = 32;

        [Tooltip(
            "Order defines session-local EffectId values. Resolve StableKey during setup, never in a job."
        )]
        public List<EffectDefinition> Effects = new List<EffectDefinition>();
    }
}
