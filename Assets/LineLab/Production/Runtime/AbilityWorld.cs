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

    /// <summary>Value-only message: safe to construct inside a Burst job.</summary>
    public struct AbilityCommand
    {
        public AbilityOperation Operation;
        public LineHandle Handle;
        public float3 Source;
        public float3 Target;
        public float Time;
        public EffectId Effect;
    }

    /// <summary>
    /// Copy this writer into gameplay jobs. Slot allocation and monotonically
    /// increasing generations belong to the caller's ECS entity allocator.
    /// </summary>
    public struct AbilityWriter
    {
        public NativeQueue<AbilityCommand>.ParallelWriter Queue;

        public void Begin(
            LineHandle handle,
            float3 source,
            float3 target,
            EffectId effect,
            float time
        )
        {
            Queue.Enqueue(
                new AbilityCommand
                {
                    Operation = AbilityOperation.Begin,
                    Handle = handle,
                    Source = source,
                    Target = target,
                    Effect = effect,
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

    /// <summary>
    /// Owns mutable native state and borrows immutable catalog records. Complete
    /// every producer, consumer and renderer job before disposal; dispose the
    /// world before the catalog. Consume must depend on ALL jobs using Writer.
    /// </summary>
    public sealed class AbilityWorld : IDisposable
    {
        public NativeArray<LineData> Lines;
        public NativeArray<LineData> Strands;
        public NativeArray<uint> Generations;
        public NativeQueue<AbilityCommand> Commands;
        private readonly NativeArray<EffectRecipeData> effects;
        private readonly NativeArray<RibbonRecipeData> ribbons;
        private readonly int ribbonSlots;
        public AbilityWriter Writer => new AbilityWriter { Queue = Commands.AsParallelWriter() };

        public AbilityWorld(int capacity, NativeEffectCatalog catalog)
        {
            if (capacity < 1 || capacity > EffectRecipeLayout.MaximumAbilities)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (catalog == null || !catalog.Effects.IsCreated || catalog.EffectCount < 1)
            {
                throw new ArgumentException(
                    "Bake a valid effect catalog before creating a world.",
                    nameof(catalog)
                );
            }

            effects = catalog.Effects;
            ribbons = catalog.Ribbons;
            ribbonSlots = catalog.RibbonSlots;
            Lines = new NativeArray<LineData>(capacity, Allocator.Persistent);
            Strands = new NativeArray<LineData>(
                checked(capacity * catalog.RibbonSlots),
                Allocator.Persistent
            );
            Generations = new NativeArray<uint>(capacity, Allocator.Persistent);
            Commands = new NativeQueue<AbilityCommand>(Allocator.Persistent);
        }

        public JobHandle Consume(JobHandle producers, float clock)
        {
            if (!math.isfinite(clock))
            {
                throw new ArgumentOutOfRangeException(nameof(clock));
            }

            return new ConsumeAbilities
            {
                Queue = Commands,
                Lines = Lines,
                Generations = Generations,
                Effects = effects,
                Clock = clock,
            }.Schedule(producers);
        }

        /// <summary>
        /// Presentation scheduling convenience. Producers remain Burst jobs;
        /// complete this returned dependency once before submitting Unity draws.
        /// A VFX-only integration may use Consume directly when CPU strands are unused.
        /// </summary>
        public JobHandle PreparePresentation(JobHandle producers, float clock)
        {
            return new ExpandStrands
            {
                Lines = Lines,
                Strands = Strands,
                Effects = effects,
                Ribbons = ribbons,
                RibbonSlots = ribbonSlots,
                Clock = clock,
            }.Schedule(Strands.Length, 64, Consume(producers, clock));
        }

        public void Dispose()
        {
            if (Lines.IsCreated)
            {
                Lines.Dispose();
            }

            if (Strands.IsCreated)
            {
                Strands.Dispose();
            }

            if (Generations.IsCreated)
            {
                Generations.Dispose();
            }

            if (Commands.IsCreated)
            {
                Commands.Dispose();
            }
        }
    }

    [BurstCompile]
    public struct ConsumeAbilities : IJob
    {
        public NativeQueue<AbilityCommand> Queue;
        public NativeArray<LineData> Lines;
        public NativeArray<uint> Generations;

        [ReadOnly]
        public NativeArray<EffectRecipeData> Effects;
        public float Clock;

        public void Execute()
        {
            while (Queue.TryDequeue(out AbilityCommand command))
            {
                int slot = command.Handle.Slot;
                if ((uint)slot >= (uint)Lines.Length || command.Handle.Generation == 0)
                {
                    continue;
                }

                LineData line = Lines[slot];
                if (command.Operation == AbilityOperation.Begin)
                {
                    if (
                        command.Handle.Generation <= Generations[slot]
                        || (uint)command.Effect.Value >= (uint)Effects.Length
                        || !math.all(math.isfinite(command.Source))
                        || !math.all(math.isfinite(command.Target))
                        || !math.isfinite(command.Time)
                    )
                    {
                        continue;
                    }

                    EffectRecipeData recipe = Effects[command.Effect.Value];
                    Generations[slot] = command.Handle.Generation;
                    line = new LineData
                    {
                        Start = new float4(command.Source, 1),
                        End = new float4(command.Target, recipe.Timing.w),
                        Color = recipe.Tint,
                        Shape = new float4(
                            command.Effect.Value,
                            slot * .719f + recipe.Motion.w,
                            command.Time,
                            -10000
                        ),
                    };
                }
                else if (command.Handle.Generation == Generations[slot] && line.Start.w > 0)
                {
                    if (command.Operation == AbilityOperation.Release)
                    {
                        if (
                            line.Shape.w < -999
                            && math.isfinite(command.Time)
                            && command.Time >= line.Shape.z
                        )
                        {
                            line.Shape.w = command.Time;
                        }
                    }
                    else if (
                        command.Operation == AbilityOperation.Follow
                        && line.Shape.w < -999
                        && math.all(math.isfinite(command.Source))
                        && math.all(math.isfinite(command.Target))
                    )
                    {
                        line.Start.xyz = command.Source;
                        line.End.xyz = command.Target;
                    }
                }

                Lines[slot] = line;
            }

            for (int slot = 0; slot < Lines.Length; slot++)
            {
                LineData line = Lines[slot];
                if (line.Start.w <= 0)
                {
                    continue;
                }

                EffectRecipeData recipe = Effects[(int)line.Shape.x];
                if (line.Shape.w > -999 && Clock - line.Shape.w >= recipe.Timing.z)
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

        [ReadOnly]
        public NativeArray<EffectRecipeData> Effects;

        [ReadOnly]
        public NativeArray<RibbonRecipeData> Ribbons;
        public NativeArray<LineData> Strands;
        public int RibbonSlots;
        public float Clock;

        public void Execute(int strandIndex)
        {
            LineData line = Lines[strandIndex / RibbonSlots];
            int slot = strandIndex % RibbonSlots;
            int effectIndex = (int)line.Shape.x;
            EffectRecipeData recipe = Effects[effectIndex];
            if (line.Start.w <= 0 || slot >= (int)recipe.Layout.x)
            {
                Strands[strandIndex] = default;
                return;
            }

            line = AbilityGeometry.Strand(
                line,
                recipe,
                Ribbons[effectIndex * RibbonSlots + slot],
                Clock
            );
            line.Shape.z = slot;
            Strands[strandIndex] = line;
        }
    }

    /// <summary>Deterministic benchmark producer; production gameplay uses AbilityWriter directly.</summary>
    [BurstCompile]
    public struct PlayAbilities : IJobParallelFor
    {
        public AbilityWriter Writer;
        public int Frame;
        public int PreviousFrame;
        public int Scenario;
        public int Count;
        public int EffectCount;
        public int PreviewEffect;
        public bool First;
        public bool Hero;
        public bool Released;
        public float Clock;

        public void Execute(int abilityIndex)
        {
            if (Released)
            {
                return;
            }

            int rows = (Count + 19) / 20;
            float3 center = Hero
                ? new float3((abilityIndex - 1) * 12, 0, 0)
                : new float3(
                    (abilityIndex % 20 - 9.5f) / 19 * 36,
                    0,
                    (abilityIndex / 20 - (rows - 1) * .5f) / math.max(1, rows - 1) * 19
                );
            if (Scenario == 2)
            {
                center *= .20f;
            }
            else if (Scenario == 3)
            {
                center *= 5;
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
            int previousStaggered = PreviousFrame + abilityIndex * 23;
            int previousCycle = previousStaggered / 480;
            int previousLocal = previousStaggered % 480;
            LineHandle handle = new LineHandle(abilityIndex, (uint)(cycle + 1));
            if (First || (Scenario == 1 && cycle != previousCycle))
            {
                int effect =
                    PreviewEffect >= 0 ? PreviewEffect : abilityIndex % math.max(1, EffectCount);
                Writer.Begin(
                    handle,
                    from,
                    to,
                    new EffectId(effect),
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
