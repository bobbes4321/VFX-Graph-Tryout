using System;
using System.IO;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace LineLab.Production.Editor
{
    /// <summary>Native boundary and shared-recipe checks, independent of historical benchmark files.</summary>
    public static class ProductionValidation
    {
        private static bool IsUnmanaged<T>()
            where T : unmanaged
        {
            return true;
        }

        public static string Run()
        {
            int passed = 0;
            void Check(bool value, string name)
            {
                if (!value)
                {
                    throw new Exception("Production validation failed: " + name);
                }

                passed++;
            }

            EffectLibrary library = Resources.Load<EffectLibrary>("Production/EffectLibrary");
            Check(library != null, "Authored library is available");
            Check(
                Marshal.SizeOf<EffectRecipeData>() == 16 * EffectRecipeLayout.EffectFloat4s,
                "Effect GPU ABI"
            );
            Check(
                Marshal.SizeOf<RibbonRecipeData>() == 16 * EffectRecipeLayout.RibbonFloat4s,
                "Ribbon GPU ABI"
            );
            Check(
                Marshal.SizeOf<LayerRecipeData>() == 16 * EffectRecipeLayout.LayerFloat4s,
                "Layer GPU ABI"
            );
            Check(IsUnmanaged<AbilityCommand>(), "Commands are unmanaged values");
            using (var catalog = new NativeEffectCatalog(library))
            {
                Check(
                    catalog.GetEffectId(library.Effects[0].StableKey).Value == 0,
                    "Stable key resolves before jobs"
                );
                Check(
                    catalog.Ramps.Length % EffectRecipeLayout.RampSamples == 0,
                    "Ramp rows are complete"
                );
                Check(
                    catalog.Ribbons.Length == catalog.EffectCount * catalog.RibbonSlots,
                    "Ribbon table addressing"
                );
                Check(
                    catalog.Layers.Length == catalog.EffectCount * catalog.LayerSlots,
                    "Layer table addressing"
                );
                EffectRecipeData recipe = catalog.Effects[0];
                if (recipe.Layout.x > 0)
                {
                    RibbonRecipeData ribbon = catalog.Ribbons[0];
                    LineData probe = new LineData { End = new float4(0, 0, 0, 1) };
                    int row = (int)ribbon.Surface.z;
                    NativeArray<float4> mutableRamps = catalog.Ramps;
                    var original = new float4[EffectRecipeLayout.RampSamples];
                    for (int i = 0; i < original.Length; i++)
                    {
                        original[i] = mutableRamps[row * original.Length + i];
                        mutableRamps[row * original.Length + i] = new float4(2);
                    }

                    Check(
                        math.abs(AbilityGeometry.Width(probe, ribbon, catalog.Ramps, .37f) - 2)
                            < .0001f,
                        "Authored width ramp reaches geometry without style code"
                    );
                    for (int i = 0; i < original.Length; i++)
                    {
                        mutableRamps[row * original.Length + i] = original[i];
                    }

                    LayerRecipeData reusableLayer = catalog.Layers[0];
                    reusableLayer.Layout.z = 7;
                    LineData active = new LineData
                    {
                        Start = new float4(0, 2, 0, 1),
                        End = new float4(5, 2, 0, 1),
                        Color = new float4(1),
                        Shape = new float4(0, 0, -2, -10000),
                    };
                    AbilityGeometry.Decor(
                        active,
                        recipe,
                        reusableLayer,
                        0,
                        1,
                        new float3(10, 20, -15),
                        catalog.Ramps,
                        out Adornment card
                    );
                    Check(
                        card.TextureIndex == 7,
                        "Secondary texture is module data, independent of motion kind"
                    );
                }

                using (var world = new AbilityWorld(128, catalog))
                {
                    AbilityWriter writer = world.Writer;
                    LineHandle handle = new LineHandle(0, 1);
                    writer.Begin(
                        handle,
                        new float3(0, 2, 0),
                        new float3(8, 2, 0),
                        new EffectId(0),
                        0
                    );
                    world.Consume(default, 1).Complete();
                    Check(
                        world.Lines[0].Start.w == 1 && world.Generations[0] == 1,
                        "Unmanaged begin"
                    );
                    Check(
                        world.Lines[0].End.w == recipe.Timing.w
                            && world.Lines[0].Color.Equals(recipe.Tint),
                        "Begin uses authored width and color"
                    );
                    writer.Release(handle, 2);
                    writer.Release(handle, 2.1f);
                    writer.Follow(handle, new float3(99), new float3(99));
                    world.Consume(default, 2.1f).Complete();
                    Check(
                        world.Lines[0].Start.x == 0 && world.Lines[0].Shape.w == 2,
                        "First release freezes endpoints and tail time"
                    );
                    Check(
                        AbilityGeometry.Envelope(
                            world.Lines[0],
                            recipe,
                            2 + recipe.Timing.y + .01f,
                            recipe.Timing.y
                        ) == 0,
                        "Core uses authored release duration"
                    );
                    world.Consume(default, 2 + recipe.Timing.z + .01f).Complete();
                    Check(world.Lines[0].Start.w == 0, "Retirement uses longest authored tail");
                    writer.Begin(
                        new LineHandle(0, 2),
                        new float3(1),
                        new float3(9),
                        new EffectId(0),
                        4
                    );
                    writer.Release(handle, 4);
                    writer.Begin(handle, new float3(99), new float3(99), new EffectId(0), 4);
                    world.Consume(default, 4).Complete();
                    Check(
                        world.Lines[0].Shape.w < -999
                            && world.Generations[0] == 2
                            && world.Lines[0].Start.x == 1,
                        "Stale generation cannot mutate reused slot"
                    );
                    writer.Begin(
                        new LineHandle(0, 3),
                        new float3(99),
                        new float3(99),
                        new EffectId(catalog.EffectCount),
                        4
                    );
                    world.Consume(default, 4).Complete();
                    Check(
                        world.Generations[0] == 2 && world.Lines[0].Start.x == 1,
                        "Unknown effect does not consume generation"
                    );
                    writer.Begin(
                        new LineHandle(0, 3),
                        new float3(float.NaN),
                        new float3(9),
                        new EffectId(0),
                        4
                    );
                    writer.Begin(
                        new LineHandle(0, 3),
                        new float3(0),
                        new float3(float.PositiveInfinity),
                        new EffectId(0),
                        4
                    );
                    writer.Begin(
                        new LineHandle(0, 3),
                        new float3(0),
                        new float3(9),
                        new EffectId(0),
                        float.NaN
                    );
                    world.Consume(default, 4).Complete();
                    Check(world.Generations[0] == 2, "Nonfinite begin is rejected");
                    writer.Follow(new LineHandle(0, 2), new float3(float.NaN), new float3(9));
                    writer.Release(new LineHandle(0, 2), float.NaN);
                    writer.Release(new LineHandle(0, 2), 3);
                    writer.Begin(
                        new LineHandle(-1, 1),
                        new float3(0),
                        new float3(9),
                        new EffectId(0),
                        4
                    );
                    writer.Begin(
                        new LineHandle(128, 1),
                        new float3(0),
                        new float3(9),
                        new EffectId(0),
                        4
                    );
                    writer.Begin(
                        new LineHandle(1, 0),
                        new float3(0),
                        new float3(9),
                        new EffectId(0),
                        4
                    );
                    world.Consume(default, 4).Complete();
                    Check(
                        world.Lines[0].Start.x == 1
                            && world.Lines[0].Shape.w < -999
                            && world.Lines[1].Start.w == 0,
                        "Invalid follow/release/slot/generation cannot mutate state"
                    );
                }

                using (var world = new AbilityWorld(5000, catalog))
                {
                    var producer = new PlayAbilities
                    {
                        Writer = world.Writer,
                        Frame = 0,
                        PreviousFrame = -1,
                        First = true,
                        Count = 5000,
                        EffectCount = catalog.EffectCount,
                        PreviewEffect = -1,
                    };
                    JobHandle consumed = world.Consume(producer.Schedule(5000, 32), 0);
                    new ExpandStrands
                    {
                        Lines = world.Lines,
                        Strands = world.Strands,
                        Effects = catalog.Effects,
                        Ribbons = catalog.Ribbons,
                        RibbonSlots = catalog.RibbonSlots,
                        Clock = 0,
                    }
                        .Schedule(world.Strands.Length, 32, consumed)
                        .Complete();
                    int active = 0;
                    foreach (LineData line in world.Lines)
                    {
                        if (line.Start.w > 0)
                        {
                            active++;
                        }
                    }

                    Check(active == 5000, "5000 parallel producers consumed without loss");
                    Check(
                        world.Strands.Length == 5000 * catalog.RibbonSlots,
                        "5000 dynamic ribbon capacity"
                    );
                    Check(
                        (int)world.Lines[4999].Shape.x == 4999 % catalog.EffectCount,
                        "Producer cycles authored catalog"
                    );
                    producer.First = false;
                    producer.Frame = 600;
                    producer.PreviousFrame = 599;
                    producer.Clock = 10;
                    world.Consume(producer.Schedule(5000, 32), 10).Complete();
                    Check(
                        world.Generations[0] == 1 && world.Lines[0].Start.w == 1,
                        "Sustain survives benchmark cycle boundary"
                    );
                }

                using (var world = new AbilityWorld(1, catalog))
                {
                    var producer = new PlayAbilities
                    {
                        Writer = world.Writer,
                        Count = 1,
                        Frame = 0,
                        PreviousFrame = -1,
                        First = true,
                        Scenario = 1,
                        EffectCount = catalog.EffectCount,
                        PreviewEffect = -1,
                    };
                    world.Consume(producer.Schedule(1, 1), 0).Complete();
                    producer.First = false;
                    producer.Frame = 350;
                    producer.PreviousFrame = 320;
                    producer.Clock = 350 / 60f;
                    world.Consume(producer.Schedule(1, 1), producer.Clock).Complete();
                    Check(
                        math.abs(world.Lines[0].Shape.w - 5.5f) < .001f,
                        "Release survives skipped render frames"
                    );
                    producer.Frame = 500;
                    producer.PreviousFrame = 470;
                    producer.Clock = 500 / 60f;
                    world.Consume(producer.Schedule(1, 1), producer.Clock).Complete();
                    Check(
                        world.Generations[0] == 2 && world.Lines[0].Shape.w < -999,
                        "Reuse survives skipped cycle frame"
                    );
                }

                // Exercise every authored recipe and every module against coincident
                // endpoints too. This catches NaNs that can silently poison a GPU batch.
                for (int effectIndex = 0; effectIndex < catalog.EffectCount; effectIndex++)
                {
                    recipe = catalog.Effects[effectIndex];
                    LineData line = new LineData
                    {
                        Start = new float4(0, 2, 0, 1),
                        End = new float4(0, 2, 0, recipe.Timing.w),
                        Color = recipe.Tint,
                        Shape = new float4(effectIndex, .7f, -2, -10000),
                    };
                    for (int ribbonSlot = 0; ribbonSlot < (int)recipe.Layout.x; ribbonSlot++)
                    {
                        RibbonRecipeData ribbon = catalog.Ribbons[
                            effectIndex * catalog.RibbonSlots + ribbonSlot
                        ];
                        LineData strand = AbilityGeometry.Strand(line, recipe, ribbon, 1);
                        float3 point = AbilityGeometry.Point(strand, recipe, .5f, 1);
                        float4 tint = AbilityGeometry.Tint(
                            strand,
                            recipe,
                            ribbon,
                            catalog.Ramps,
                            .5f,
                            1
                        );
                        float width = AbilityGeometry.Width(strand, ribbon, catalog.Ramps, .5f);
                        Check(
                            math.all(math.isfinite(point))
                                && math.all(math.isfinite(tint))
                                && math.isfinite(width),
                            "Finite authored ribbon " + effectIndex + ":" + ribbonSlot
                        );
                    }

                    for (int layerSlot = 0; layerSlot < (int)recipe.Layout.y; layerSlot++)
                    {
                        LayerRecipeData layer = catalog.Layers[
                            effectIndex * catalog.LayerSlots + layerSlot
                        ];
                        AbilityGeometry.Decor(
                            line,
                            recipe,
                            layer,
                            0,
                            1,
                            new float3(10, 20, -15),
                            catalog.Ramps,
                            out Adornment card
                        );
                        Check(
                            math.all(math.isfinite(card.Position))
                                && math.all(math.isfinite(card.Size))
                                && math.all(math.isfinite(card.X))
                                && math.isfinite(card.Alpha)
                                && card.TextureIndex == layer.Layout.z,
                            "Finite authored layer " + effectIndex + ":" + layerSlot
                        );
                    }
                }
            }

            string message =
                passed + " native recipe, lifecycle, ABI and 5000-capacity checks passed";
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/production-recipe-validation.txt", message);
            return message;
        }
    }
}
