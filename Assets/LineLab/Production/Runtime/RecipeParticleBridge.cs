using System.Diagnostics;
using UnityEngine;
using UnityEngine.ParticleSystemJobs;

namespace LineLab.Production
{
    /// <summary>Unity's required scheduling point for direct writes to native particle storage.</summary>
    public sealed class RecipeParticleBridge : MonoBehaviour
    {
        [System.NonSerialized]
        public SurfaceRecipeView Recipe;

        [System.NonSerialized]
        public bool Ready;

        [System.NonSerialized]
        public double LastCallbackMs;
        ParticleSystem system;

        void Awake()
        {
            system = GetComponent<ParticleSystem>();
        }

        void OnParticleUpdateJobScheduled()
        {
            if (!Ready || !Recipe.Lines.IsCreated)
            {
                return;
            }

            long start = Stopwatch.GetTimestamp();
            new UpdateSurfaceParticles { Recipe = Recipe }
                .Schedule(system, 64)
                .Complete();
            LastCallbackMs = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        }
    }
}
