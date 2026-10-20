using System;
using System.IO;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace LineLab.Production.Editor
{
    public static class ProductionValidation
    {
        public static string Run()
        {
            int passed = 0;
            void Check(bool value, string name)
            {
                if (!value)
                {
                    throw new Exception(name);
                }

                passed++;
            }

            using (var w = new AbilityWorld(128))
            {
                var writer = w.Writer;
                var h = new LineHandle(0, 1);
                writer.Begin(h, new float3(0, 2, 0), new float3(8, 2, 0), 0, 0);
                w.Consume(default, 1).Complete();
                Check(w.Lines[0].Start.w == 1 && w.Generations[0] == 1, "Unmanaged begin");
                writer.Release(h, 2);
                writer.Follow(h, new float3(99), new float3(99));
                w.Consume(default, 2.1f).Complete();
                Check(
                    w.Lines[0].Start.x == 0 && w.Lines[0].Shape.w == 2,
                    "Release freezes endpoints"
                );
                Check(
                    AbilityGeometry.Envelope(w.Lines[0], 2.4f, .24f) == 0
                        && AbilityGeometry.Envelope(w.Lines[0], 2.4f, 1.1f) > 0,
                    "Particle tail outlives core"
                );
                w.Consume(default, 3.5f).Complete();
                Check(w.Lines[0].Start.w == 0, "Tail retires");
                writer.Begin(new LineHandle(0, 2), new float3(1), new float3(9), 1, 4);
                writer.Release(h, 4);
                w.Consume(default, 4).Complete();
                Check(w.Lines[0].Shape.w < -999 && w.Generations[0] == 2, "Stale release ignored");
                writer.Begin(h, new float3(99), new float3(99), 2, 4);
                w.Consume(default, 4).Complete();
                Check(w.Lines[0].Start.x == 1, "Stale begin ignored");
            }

            using (var w = new AbilityWorld(128))
            {
                var job = new PlayAbilities
                {
                    Writer = w.Writer,
                    Frame = 0,
                    PreviousFrame = -1,
                    Clock = 0,
                    First = true,
                    Count = 128,
                };
                w.Consume(job.Schedule(128, 16), 0).Complete();
                int active = 0;
                foreach (var l in w.Lines)
                {
                    if (l.Start.w > 0)
                    {
                        active++;
                    }
                }

                Check(active == 128, "Parallel ability producers");
                job.Frame = 600;
                job.PreviousFrame = 599;
                job.Clock = 10;
                job.First = false;
                w.Consume(job.Schedule(128, 16), 10).Complete();
                Check(
                    w.Generations[0] == 1 && w.Lines[0].Start.w == 1,
                    "Sustain survives cycle boundary"
                );
            }

            using (var w = new AbilityWorld(1))
            {
                var j = new PlayAbilities
                {
                    Writer = w.Writer,
                    Count = 1,
                    Frame = 0,
                    PreviousFrame = -1,
                    Clock = 0,
                    First = true,
                    Scenario = 1,
                };
                w.Consume(j.Schedule(1, 1), 0).Complete();
                j.First = false;
                j.Frame = 350;
                j.PreviousFrame = 320;
                j.Clock = 350 / 60f;
                w.Consume(j.Schedule(1, 1), j.Clock).Complete();
                Check(
                    math.abs(w.Lines[0].Shape.w - 5.5f) < .001f,
                    "Release survives skipped render frames"
                );
                j.Frame = 500;
                j.PreviousFrame = 470;
                j.Clock = 500 / 60f;
                w.Consume(j.Schedule(1, 1), j.Clock).Complete();
                Check(
                    w.Generations[0] == 2 && w.Lines[0].Shape.w < -999,
                    "Pooled reuse survives skipped cycle frame"
                );
            }

            string message = passed + " complete-ability lifecycle checks passed";
            File.WriteAllText("BenchmarkResults/production/native-validation.txt", message);
            return message;
        }
    }
}
