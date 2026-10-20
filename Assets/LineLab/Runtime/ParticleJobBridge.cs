using System;
using System.Diagnostics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.ParticleSystemJobs;

namespace LineLab
{
    // Unity requires particle jobs to be scheduled from this callback. Keeping the
    // scheduling point explicit also makes native storage ownership unambiguous.
    public sealed class ParticleJobBridge : MonoBehaviour
    {
        [NonSerialized]
        public NativeArray<LineData> Lines;

        [NonSerialized]
        public int Segments;

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
            new UpdateParticlesJob
            {
                Lines = Lines,
                Segments = Segments,
                Clock = Clock,
                Camera = CameraPosition,
            }
                .Schedule(system, 64)
                .Complete();
            LastCallbackMs = (Stopwatch.GetTimestamp() - start) * 1000d / Stopwatch.Frequency;
        }
    }
}
