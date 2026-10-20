using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace LineLab.Examples
{
    // Replace these arrays with your ECS component accessors. None of this API
    // depends on Entities, MonoBehaviour, Transform, or a particular renderer.
    public struct HealerState
    {
        public float3 Position;
        public int TargetEntity;
        public int LineSlot; // uniquely assigned by the ECS, not a Unity instance ID
        public uint LineGeneration;
        public byte WantsToHeal;
        public byte LineWasActive;
    }

    [BurstCompile]
    public struct HealerLinesJob : IJobParallelFor
    {
        public NativeArray<HealerState> Healers;

        [ReadOnly]
        public NativeArray<float3> EntityPositions;
        public LineWriter Lines;

        public void Execute(int entity)
        {
            var healer = Healers[entity];
            bool targetValid = (uint)healer.TargetEntity < (uint)EntityPositions.Length;
            bool healing = healer.WantsToHeal != 0 && targetValid;
            if (healing)
            {
                float3 from = healer.Position + new float3(0, 1.5f, 0);
                float3 to = EntityPositions[healer.TargetEntity] + new float3(0, 1.0f, 0);
                float4 mint = new float4(.04f, 1, .57f, 1);
                if (healer.LineWasActive == 0)
                {
                    healer.LineGeneration++;
                    Lines.Begin(
                        new LineHandle(healer.LineSlot, healer.LineGeneration),
                        from,
                        to,
                        mint,
                        .16f,
                        style: 0
                    );
                }
                else
                {
                    Lines.Update(
                        new LineHandle(healer.LineSlot, healer.LineGeneration),
                        from,
                        to,
                        mint,
                        .16f,
                        style: 0
                    );
                }
            }
            else if (healer.LineWasActive != 0)
            {
                Lines.Stop(new LineHandle(healer.LineSlot, healer.LineGeneration));
            }

            healer.LineWasActive = healing ? (byte)1 : (byte)0;
            Healers[entity] = healer;
        }
    }
}
