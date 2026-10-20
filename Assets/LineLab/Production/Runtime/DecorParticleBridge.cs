using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.ParticleSystemJobs;

namespace LineLab.Production
{
    // One callback host per secondary layer; records job scheduling/completion cost.
    public sealed class DecorParticleBridge : MonoBehaviour
    {
        [NonSerialized]
        public NativeArray<LineData> Lines;

        [NonSerialized]
        public int Layer;

        [NonSerialized]
        public float Clock;

        [NonSerialized]
        public float3 CameraPosition;

        [NonSerialized]
        public bool Ready;

        [NonSerialized]
        public double LastCallbackMs;
        ParticleSystem system;

        void Awake()
        {
            system = GetComponent<ParticleSystem>();
        }

        void OnParticleUpdateJobScheduled()
        {
            if (!Ready || !Lines.IsCreated)
            {
                return;
            }

            long start = Stopwatch.GetTimestamp();
            new DecorParticleUpdate
            {
                Lines = Lines,
                Layer = Layer,
                Clock = Clock,
                Camera = CameraPosition,
            }
                .Schedule(system, 64)
                .Complete();
            LastCallbackMs = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        }
    }
}
