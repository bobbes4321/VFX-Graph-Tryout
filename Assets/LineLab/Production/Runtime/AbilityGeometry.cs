using Unity.Collections;
using Unity.Mathematics;

namespace LineLab.Production
{
    /// <summary>A single camera-facing or ground-facing card produced by a reusable layer.</summary>
    public struct Adornment
    {
        public float3 Position;
        public float3 X;
        public float3 Y;
        public float3 Color;
        public float2 Size;
        public float Alpha;
        public float TextureIndex;
    }

    /// <summary>
    /// Pure math shared by Burst renderers and generated HLSL. Recipes provide the
    /// appearance; motion kinds select reusable algorithms, never named abilities.
    /// </summary>
    public static class AbilityGeometry
    {
        // BEGIN SHARED MATH
        public static float Hash(float n)
        {
            return math.frac(math.sin(n * 127.1f + 311.7f) * 43758.5453f);
        }

        public static float4 SampleRamp(NativeArray<float4> ramps, int row, float t)
        {
            float sample = math.saturate(t) * 31;
            int first = (int)math.floor(sample);
            int next = math.min(first + 1, 31);
            int offset = row * 32;
            return math.lerp(ramps[offset + first], ramps[offset + next], sample - first);
        }

        public static float Envelope(
            LineData l,
            EffectRecipeData recipe,
            float clock,
            float release
        )
        {
            float age = math.max(0, clock - l.Shape.z);
            float charge = math.smoothstep(0, math.max(.0001f, recipe.Timing.x), age);
            float fade =
                l.Shape.w < -999
                    ? 1
                    : math.saturate(1 - (clock - l.Shape.w) / math.max(.0001f, release));
            return charge * fade * fade * l.Start.w;
        }

        public static LineData Strand(
            LineData l,
            EffectRecipeData recipe,
            RibbonRecipeData ribbon,
            float clock
        )
        {
            float envelope = Envelope(l, recipe, clock, recipe.Timing.y);
            l.Shape.y += ribbon.Shape.w;
            l.End.w *= ribbon.Shape.x;
            l.Color.xyz = math.lerp(
                l.Color.xyz * ribbon.Tint.xyz,
                new float3(1, 1, 1),
                ribbon.Shape.z
            );
            l.Color.w *= envelope * ribbon.Shape.y * ribbon.Tint.w;
            // Base records use z/w for lifecycle times. Expanded records no longer do.
            l.Shape.z = 0;
            l.Shape.w = 0;
            return l;
        }

        public static float3 Point(LineData l, EffectRecipeData recipe, float t, float clock)
        {
            float3 direction = math.normalizesafe(l.End.xyz - l.Start.xyz, new float3(1, 0, 0));
            float3 side = math.normalizesafe(
                math.cross(direction, new float3(0, 1, 0)),
                new float3(0, 0, 1)
            );
            float3 up = math.cross(side, direction);
            float envelope = math.sin(t * math.PI);
            float phase = l.Shape.y;
            float3 position = math.lerp(l.Start.xyz, l.End.xyz, t);
            float wave = t * recipe.Path.w - clock * recipe.Motion.x + phase;
            int motion = (int)recipe.Path.x;
            if (motion == 0)
            {
                position +=
                    envelope
                    * (
                        up * recipe.Path.y * (1 + .55f * math.sin(wave))
                        + side * recipe.Path.z * math.cos(wave)
                    );
            }
            else if (motion == 1)
            {
                float tick = math.floor(clock * recipe.Motion.z);
                float k = t * recipe.Motion.y;
                float cell = math.floor(k);
                float f = math.frac(k);
                float a = Hash(cell + tick * 31 + phase);
                float b = Hash(cell + 1 + tick * 31 + phase);
                float c = Hash(cell * 3 + tick * 19 + phase);
                float d = Hash((cell + 1) * 3 + tick * 19 + phase);
                position +=
                    envelope
                    * (
                        (math.lerp(a, b, f) - .5f) * recipe.Path.y * up
                        + (math.lerp(c, d, f) - .5f) * recipe.Path.z * side
                    );
            }
            else if (motion == 2)
            {
                position +=
                    envelope
                    * (up * recipe.Path.y * math.sin(wave) + side * recipe.Path.z * math.cos(wave));
            }

            return position;
        }

        public static float4 Tint(
            LineData l,
            EffectRecipeData recipe,
            RibbonRecipeData ribbon,
            NativeArray<float4> ramps,
            float t,
            float clock
        )
        {
            float pulse =
                recipe.Pulse.x
                + recipe.Pulse.y
                    * math.pow(
                        .5f
                            + .5f
                                * math.cos(t * recipe.Pulse.z - clock * recipe.Pulse.w + l.Shape.y),
                        3
                    );
            float4 ramp = SampleRamp(ramps, (int)ribbon.Surface.y, t);
            return new float4(l.Color.xyz * ramp.xyz * pulse, l.Color.w * ramp.w * l.Start.w);
        }

        public static float Width(
            LineData l,
            RibbonRecipeData ribbon,
            NativeArray<float4> ramps,
            float t
        )
        {
            return l.End.w * SampleRamp(ramps, (int)ribbon.Surface.z, t).x;
        }

        public static void Decor(
            LineData l,
            EffectRecipeData recipe,
            LayerRecipeData layer,
            int j,
            float clock,
            float3 camera,
            NativeArray<float4> ramps,
            out Adornment d
        )
        {
            d = default;
            // Pools reserve the largest count in this library slot. A recipe with
            // fewer motes must keep its extra cards invisible on CPU and GPU.
            if (j >= (int)layer.Layout.y || l.Start.w <= 0)
            {
                d.X = new float3(1, 0, 0);
                d.Y = new float3(0, 1, 0);
                return;
            }

            float seed = j * 1.371f + l.Shape.y * 3.7f;
            float random = Hash(seed);
            float envelope = Envelope(l, recipe, clock, layer.Appearance.z);
            int motion = (int)layer.Layout.x;
            float count = math.max(1, layer.Layout.y);
            float progress = math.frac(clock * layer.Motion.x + random);
            float angle = seed * 9 + clock * layer.Motion.w;
            float minimum = layer.Size.x;
            float maximum = layer.Size.y;
            float size = math.lerp(minimum, maximum, random);
            float placement = layer.Placement.x;
            bool source = placement < .5f ? j % 2 == 0 : placement < 1.5f;
            float3 endpoint = source ? l.Start.xyz : l.End.xyz;
            d.Position = endpoint;
            d.Color = math.lerp(l.Color.xyz, new float3(1, 1, 1), layer.Appearance.y);
            d.Alpha = envelope * layer.Appearance.x * l.Color.w;
            d.TextureIndex = layer.Layout.z;
            if (motion == 0)
            {
                progress = math.frac(j / count + clock * layer.Motion.x);
                d.Position = Point(l, recipe, progress, clock);
                float radius =
                    (layer.Motion.y + random * layer.Motion.z) * math.sin(progress * math.PI);
                d.Position += new float3(
                    math.cos(angle * 2) * radius,
                    math.sin(angle * 2) * radius,
                    math.cos(angle * 1.7f) * radius
                );
                d.Alpha *= math.pow(math.max(0, math.sin(progress * math.PI)), layer.Size.w);
            }
            else if (motion == 1)
            {
                float direction = seed * 6.283185f;
                float3 velocity = new float3(
                    math.cos(direction) * layer.Physics.x,
                    layer.Physics.y + Hash(seed + 4) * layer.Physics.z,
                    math.sin(direction) * layer.Physics.x
                );
                d.Position += velocity * progress;
                d.Position.y -= progress * progress * layer.Physics.w;
                d.Alpha *= math.pow(1 - progress, layer.Size.w);
            }
            else if (motion == 2)
            {
                size *= math.max(0, 1 + layer.Motion.y * math.sin(clock * layer.Motion.x + seed));
            }
            else if (motion == 3)
            {
                d.Position.y = layer.Placement.y;
            }

            if (motion >= 2)
            {
                float cardsAtEndpoint = placement < .5f ? math.ceil(count * .5f) : count;
                float localCard = placement < .5f ? math.floor(j * .5f) : j;
                progress = cardsAtEndpoint <= 1 ? 0 : localCard / (cardsAtEndpoint - 1);
            }

            if (motion != 0)
            {
                if (motion != 3)
                {
                    d.Position.y += layer.Placement.y;
                }

                size *= source ? layer.Placement.z : layer.Placement.w;
            }

            float4 color = SampleRamp(ramps, (int)layer.Layout.w, progress);
            size *= SampleRamp(ramps, (int)layer.Appearance.w, progress).x;
            d.Size = new float2(size * layer.Size.z, size);
            d.Color *= color.xyz;
            d.Alpha *= color.w;
            float3 view = math.normalizesafe(camera - d.Position, new float3(0, 0, -1));
            float3 right =
                motion == 3
                    ? new float3(1, 0, 0)
                    : math.normalizesafe(
                        math.cross(new float3(0, 1, 0), view),
                        new float3(1, 0, 0)
                    );
            float3 up = motion == 3 ? new float3(0, 0, 1) : math.cross(view, right);
            d.X = right * math.cos(angle) + up * math.sin(angle);
            d.Y = -right * math.sin(angle) + up * math.cos(angle);
        }

        // END SHARED MATH
        /// <summary>Returns a unit frame; callers apply Width at the segment midpoint.</summary>
        public static void Segment(
            LineData l,
            EffectRecipeData recipe,
            RibbonRecipeData ribbon,
            NativeArray<float4> ramps,
            int segment,
            int segments,
            float clock,
            float3 camera,
            out float3 mid,
            out float3 tangent,
            out float3 side,
            out float length,
            out float4 color
        )
        {
            float3 a = Point(l, recipe, (float)segment / segments, clock);
            float3 b = Point(l, recipe, (float)(segment + 1) / segments, clock);
            mid = (a + b) * .5f;
            tangent = math.normalizesafe(b - a, new float3(1, 0, 0));
            side = math.normalizesafe(
                math.cross(math.normalizesafe(camera - mid), tangent),
                new float3(0, 1, 0)
            );
            length = math.distance(a, b) * 1.035f;
            color = Tint(l, recipe, ribbon, ramps, (segment + .5f) / segments, clock);
        }
    }
}
