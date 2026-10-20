using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace LineLab.Production
{
    public enum AbilityOperation : byte
    {
        Begin,
        Follow,
        Release,
    }

    public struct AbilityCommand
    {
        public AbilityOperation Operation;
        public LineHandle Handle;
        public float3 Source;
        public float3 Target;
        public float Time;
        public int Style;
    }

    // Gameplay sends values only. Release retains the last endpoints for the tail.
    public struct AbilityWriter
    {
        public NativeQueue<AbilityCommand>.ParallelWriter Queue;

        public void Begin(LineHandle handle, float3 source, float3 target, int style, float time)
        {
            Queue.Enqueue(
                new AbilityCommand
                {
                    Operation = AbilityOperation.Begin,
                    Handle = handle,
                    Source = source,
                    Target = target,
                    Style = style,
                    Time = time,
                }
            );
        }

        public void Follow(LineHandle handle, float3 source, float3 target)
        {
            Queue.Enqueue(
                new AbilityCommand
                {
                    Operation = AbilityOperation.Follow,
                    Handle = handle,
                    Source = source,
                    Target = target,
                }
            );
        }

        public void Release(LineHandle handle, float time)
        {
            Queue.Enqueue(
                new AbilityCommand
                {
                    Operation = AbilityOperation.Release,
                    Handle = handle,
                    Time = time,
                }
            );
        }
    }

    // Owns persistent native storage. Gameplay jobs receive Writer, never this
    // managed owner. Lines has one record per ability; Strands has three per ability.
    public sealed class AbilityWorld : IDisposable
    {
        public NativeArray<LineData> Lines;
        public NativeArray<LineData> Strands;
        public NativeArray<uint> Generations;
        public NativeQueue<AbilityCommand> Commands;
        public AbilityWriter Writer => new AbilityWriter { Queue = Commands.AsParallelWriter() };

        public AbilityWorld(int capacity)
        {
            Lines = new NativeArray<LineData>(capacity, Allocator.Persistent);
            Strands = new NativeArray<LineData>(capacity * 3, Allocator.Persistent);
            Generations = new NativeArray<uint>(capacity, Allocator.Persistent);
            Commands = new NativeQueue<AbilityCommand>(Allocator.Persistent);
        }

        public JobHandle Consume(JobHandle producers, float clock)
        {
            return new ConsumeAbilities
            {
                Queue = Commands,
                Lines = Lines,
                Generations = Generations,
                Clock = clock,
            }.Schedule(producers);
        }

        public void Dispose()
        {
            Lines.Dispose();
            Strands.Dispose();
            Generations.Dispose();
            Commands.Dispose();
        }
    }

    [BurstCompile]
    public struct ConsumeAbilities : IJob
    {
        public NativeQueue<AbilityCommand> Queue;
        public NativeArray<LineData> Lines;
        public NativeArray<uint> Generations;
        public float Clock;

        public void Execute()
        {
            // A single consumer serializes state changes after all producers finish.
            // Generations prevent delayed commands from modifying a reused slot.
            while (Queue.TryDequeue(out var command))
            {
                int slot = command.Handle.Slot;
                if ((uint)slot >= (uint)Lines.Length)
                {
                    continue;
                }

                var line = Lines[slot];
                if (command.Operation == AbilityOperation.Begin)
                {
                    if (command.Handle.Generation <= Generations[slot])
                    {
                        continue;
                    }

                    Generations[slot] = command.Handle.Generation;
                    line = new LineData
                    {
                        Start = new float4(command.Source, 1),
                        End = new float4(command.Target, .22f),
                        Color = new float4(AbilityGeometry.Palette(command.Style), 1),
                        Shape = new float4(command.Style, slot * .719f, command.Time, -10000),
                    };
                }
                else if (command.Handle.Generation == Generations[slot] && line.Start.w > 0)
                {
                    if (command.Operation == AbilityOperation.Release)
                    {
                        // Shape.w is the release time; -10000 means still sustained.
                        // Only the first Release starts the tail.
                        if (line.Shape.w < -999)
                        {
                            line.Shape.w = command.Time;
                        }
                    }
                    else if (line.Shape.w < -999)
                    {
                        line.Start.xyz = command.Source;
                        line.End.xyz = command.Target;
                    }
                }

                Lines[slot] = line;
            }

            for (int slot = 0; slot < Lines.Length; slot++)
            {
                var line = Lines[slot];
                if (line.Shape.w > -999 && Clock - line.Shape.w > 1.4f)
                {
                    line.Start.w = 0;
                    Lines[slot] = line;
                }
            }
        }
    }

    [BurstCompile]
    public struct ExpandStrands : IJobParallelFor
    {
        [ReadOnly]
        public NativeArray<LineData> Lines;
        public NativeArray<LineData> Strands;
        public float Clock;

        public void Execute(int strandIndex)
        {
            // Each base record becomes a bright core, broad glow and thin accent.
            Strands[strandIndex] = AbilityGeometry.Strand(
                Lines[strandIndex / 3],
                strandIndex % 3,
                Clock
            );
        }
    }

    [BurstCompile]
    // Demo workload producer. Replace this with your ECS jobs in an actual game.
    public struct PlayAbilities : IJobParallelFor
    {
        public AbilityWriter Writer;
        public int Frame;
        public int PreviousFrame;
        public int Scenario;
        public int Count;
        public bool First;
        public bool Hero;
        public float Clock;

        public void Execute(int abilityIndex)
        {
            int rows = (Count + 19) / 20;
            float3 center = Hero
                ? new float3((abilityIndex - 1) * 12, 0, 0)
                : new float3(
                    (abilityIndex % 20 - 9.5f) / 19 * 36,
                    0,
                    ((abilityIndex / 20) - (rows - 1) * .5f) / math.max(1, rows - 1) * 19
                );
            if (Scenario == 2)
            {
                center *= .20f;
            }

            float3 from = center + new float3(-3.5f, 1.85f, -1.6f);
            float3 to = center + new float3(3.5f, 1.65f, 2.1f);
            to += new float3(
                .3f * math.sin(Clock * 1.2f + abilityIndex),
                .12f * math.sin(Clock * 2 + abilityIndex),
                .25f * math.cos(Clock * .8f + abilityIndex)
            );
            int staggeredFrame = Frame + abilityIndex * 23;
            int cycle = Scenario == 1 ? staggeredFrame / 480 : 0;
            int cycleFrame = staggeredFrame % 480;
            int previousStaggeredFrame = PreviousFrame + abilityIndex * 23;
            int previousCycle = previousStaggeredFrame / 480;
            int previousLocal = previousStaggeredFrame % 480;
            var handle = new LineHandle(abilityIndex, (uint)(cycle + 1));
            if (First || (Scenario == 1 && cycle != previousCycle))
            {
                Writer.Begin(
                    handle,
                    from,
                    to,
                    abilityIndex % 3,
                    Scenario == 1 ? Clock - cycleFrame / 60f : Clock - 2
                );
                if (Scenario == 1 && cycleFrame >= 330)
                {
                    Writer.Release(handle, Clock - (cycleFrame - 330) / 60f);
                }
            }
            else if (Scenario != 1 || cycleFrame < 330)
            {
                Writer.Follow(handle, from, to);
            }

            if (
                Scenario == 1
                && cycleFrame >= 330
                && (previousLocal < 330 || cycle != previousCycle)
            )
            {
                Writer.Release(handle, Clock - (cycleFrame - 330) / 60f);
            }
        }
    }
}
