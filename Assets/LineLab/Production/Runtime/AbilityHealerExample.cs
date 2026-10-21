using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace LineLab.Production
{
    /// <summary>One entity's presentation state, stored as unmanaged ECS component data.</summary>
    public struct HealingPresentationState
    {
        public uint Generation;
        public byte WasHealing;
    }

    /// <summary>
    /// Example integration boundary. Resolve HealingEffect from the catalog once
    /// on the main thread; schedule this job after gameplay has updated inputs.
    /// Every healer owns one stable, exclusive slot for this world's lifetime.
    /// </summary>
    [BurstCompile]
    public struct PresentHealingJob : IJobParallelFor
    {
        public AbilityWriter Writer;
        public EffectId HealingEffect;
        public float Clock;

        [ReadOnly]
        public NativeArray<byte> IsHealing;

        [ReadOnly]
        public NativeArray<float3> HealerPositions;

        [ReadOnly]
        public NativeArray<float3> TargetPositions;
        public NativeArray<HealingPresentationState> State;

        public void Execute(int healerSlot)
        {
            HealingPresentationState state = State[healerSlot];
            bool healing = IsHealing[healerSlot] != 0;
            if (healing && state.WasHealing == 0)
            {
                // Generations are monotonic per slot; zero is reserved. An ECS
                // allocator can supply this generation instead of storing it here.
                if (state.Generation == uint.MaxValue)
                {
                    return;
                }

                state.Generation++;
                Writer.Begin(
                    new LineHandle(healerSlot, state.Generation),
                    HealerPositions[healerSlot],
                    TargetPositions[healerSlot],
                    HealingEffect,
                    Clock
                );
            }
            else if (healing)
            {
                Writer.Follow(
                    new LineHandle(healerSlot, state.Generation),
                    HealerPositions[healerSlot],
                    TargetPositions[healerSlot]
                );
            }
            else if (state.WasHealing != 0)
            {
                // Last positions remain available while authored secondary layers fade.
                Writer.Release(new LineHandle(healerSlot, state.Generation), Clock);
            }

            state.WasHealing = healing ? (byte)1 : (byte)0;
            State[healerSlot] = state;
        }
    }
}
