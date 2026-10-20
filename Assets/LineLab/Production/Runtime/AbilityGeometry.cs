using Unity.Mathematics;

namespace LineLab.Production
{
    public struct Adornment
    {
        // One card's world-space frame, tint, dimensions and opacity.
        public float3 Position;
        public float3 X;
        public float3 Y;
        public float3 Color;
        public float2 Size;
        public float Alpha;
    }

    // Kept in sync with AbilityCommon.hlsl by Tools/sync_ability_math.py.
    public static class AbilityGeometry
    {
        public static float Hash(float n)
        {
            return math.frac(math.sin(n * 127.1f + 311.7f) * 43758.5453f);
        }

        public static float3 Palette(int style)
        {
            return style == 0 ? new float3(.16f, 1, .62f)
                : style == 1 ? new float3(.22f, .54f, 1)
                : new float3(1, .39f, .075f);
        }

        public static int LayerCount(int layer)
        {
            // Layer 0: travelling motes, 1: sparks, 2: coronas, 3: ground runes.
            return layer == 0 ? 48
                : layer == 1 ? 24
                : layer == 2 ? 8
                : 4;
        }

        public static float Envelope(LineData l, float clock, float release)
        {
            // Shape.z is Begin time; Shape.w is Release time or the sustain sentinel.
            float age = clock - l.Shape.z;
            float fade = l.Shape.w < -999 ? 1 : math.saturate(1 - (clock - l.Shape.w) / release);
            return math.smoothstep(0, .55f, age) * fade * fade * l.Start.w;
        }

        public static LineData Strand(LineData l, int strand, float clock)
        {
            l.Shape.y += strand * 2.094395f;
            l.End.w *=
                strand == 0 ? 1
                : strand == 1 ? 3.6f
                : .55f;
            l.Color.w *=
                Envelope(l, clock, .24f)
                * (
                    strand == 0 ? 1
                    : strand == 1 ? .075f
                    : .65f
                );
            if (strand == 0)
            {
                l.Color.xyz = math.lerp(l.Color.xyz, new float3(1, 1, 1), .45f);
            }

            return l;
        }

        public static float3 Point(LineData l, float t, float clock)
        {
            float3 direction = math.normalizesafe(l.End.xyz - l.Start.xyz, new float3(1, 0, 0));
            float3 side = math.normalizesafe(
                math.cross(direction, new float3(0, 1, 0)),
                new float3(0, 0, 1)
            );
            float3 up = math.cross(side, direction);
            float e = math.sin(t * math.PI);
            float phase = l.Shape.y;
            float3 p = math.lerp(l.Start.xyz, l.End.xyz, t);
            if (l.Shape.x < .5f)
            {
                p +=
                    e
                    * (
                        up * (.45f + .25f * math.sin(t * 18 - clock * 4 + phase))
                        + side * .22f * math.cos(t * 18 - clock * 4 + phase)
                    );
            }
            else if (l.Shape.x < 1.5f)
            {
                float tick = math.floor(clock * 18);
                float k = t * 19;
                float cell = math.floor(k);
                float f = math.frac(k);
                float a = Hash(cell + tick * 31 + phase);
                float b = Hash(cell + 1 + tick * 31 + phase);
                float c = Hash(cell * 3 + tick * 19 + phase);
                float d = Hash((cell + 1) * 3 + tick * 19 + phase);
                p +=
                    e
                    * (
                        (math.lerp(a, b, f) - .5f) * 1.25f * up
                        + (math.lerp(c, d, f) - .5f) * .75f * side
                    );
            }
            else
            {
                p +=
                    e
                    * (
                        up * .14f * math.sin(t * 26 - clock * 11 + phase)
                        + side * .12f * math.cos(t * 26 - clock * 11 + phase)
                    );
            }

            return p;
        }

        public static float4 Tint(LineData l, float t, float clock)
        {
            float pulse =
                .65f + .35f * math.pow(.5f + .5f * math.cos(t * 30 - clock * 12 + l.Shape.y), 3);
            return new float4(l.Color.xyz * pulse, l.Color.w * l.Start.w);
        }

        public static void Decor(
            LineData l,
            int layer,
            int j,
            float clock,
            float3 camera,
            out Adornment d
        )
        {
            // Analytic motion lets CPU particles and GPU effects share the recipe
            // without retaining a different simulation history for each backend.
            d = default;
            float seed = j * 1.371f + l.Shape.y * 3.7f;
            float h = Hash(seed);
            float env = Envelope(l, clock, layer == 1 ? 1.1f : .8f);
            float angle = seed * 9 + clock;
            d.Color = l.Color.xyz;
            d.Alpha = env;
            if (layer == 0)
            {
                float t = math.frac(j / 48f + clock * (l.Shape.x < .5f ? .38f : .68f));
                d.Position = Point(l, t, clock);
                float r = (.18f + h * .43f) * math.sin(t * math.PI);
                d.Position += new float3(
                    math.cos(angle * 2) * r,
                    math.sin(angle * 2) * r,
                    math.cos(angle * 1.7f) * r
                );
                float size = .075f + h * .16f;
                d.Size = new float2(size * (l.Shape.x > 1.5f ? 2.8f : 1), size);
                d.Alpha *= math.sin(t * math.PI) * .95f;
                d.Color = math.lerp(d.Color, new float3(1, 1, 1), .35f);
            }
            else if (layer == 1)
            {
                float age = math.frac(clock * (l.Shape.x < .5f ? .65f : 1.7f) + h * 5);
                float a = seed * 6.283185f;
                float3 velocity = new float3(math.cos(a), .6f + Hash(seed + 4) * 1.6f, math.sin(a));
                d.Position =
                    (j % 4 == 0 ? l.Start.xyz : l.End.xyz)
                    + velocity * age * (l.Shape.x < .5f ? 1.35f : 2.7f);
                d.Position.y -= age * age * (l.Shape.x < .5f ? -.3f : 1.3f);
                d.Size = new float2(.035f + h * .045f, (.12f + h * .22f) * (1 - age * .7f));
                d.Alpha *= math.pow(1 - age, 2);
            }
            else if (layer == 2)
            {
                d.Position = j < 4 ? l.Start.xyz : l.End.xyz;
                int band = j % 4;
                float size =
                    (
                        band == 0 ? .85f
                        : band == 1 ? 1.65f
                        : band == 2 ? 2.8f
                        : 4.1f
                    ) * (1 + .09f * math.sin(clock * 7 + seed));
                d.Size = new float2(size, size);
                d.Alpha *=
                    band == 0 ? .85f
                    : band == 1 ? .35f
                    : band == 2 ? .16f
                    : .04f;
                angle = clock * (j % 2 == 0 ? 1 : -.4f) + seed;
            }
            else
            {
                d.Position = j < 2 ? l.Start.xyz : l.End.xyz;
                d.Position.y = .19f;
                float size = (j < 2 ? 2.8f : 3.7f) * (j % 2 == 0 ? 1 : 1.22f);
                d.Size = new float2(size, size);
                d.Alpha *= j % 2 == 0 ? .42f : .17f;
                angle = clock * (j % 2 == 0 ? .3f : -.25f) + l.Shape.y;
            }

            float3 view = math.normalizesafe(camera - d.Position, new float3(0, 0, -1));
            float3 right =
                layer == 3
                    ? new float3(1, 0, 0)
                    : math.normalizesafe(
                        math.cross(new float3(0, 1, 0), view),
                        new float3(1, 0, 0)
                    );
            float3 up = layer == 3 ? new float3(0, 0, 1) : math.cross(view, right);
            d.X = right * math.cos(angle) + up * math.sin(angle);
            d.Y = -right * math.sin(angle) + up * math.cos(angle);
        }

        // CPU-only segment interface used by the copied baseline renderer bridges.
        public static void Segment(
            LineData l,
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
            float3 a = Point(l, (float)segment / segments, clock);
            float3 b = Point(l, (float)(segment + 1) / segments, clock);
            mid = (a + b) * .5f;
            tangent = math.normalizesafe(b - a, new float3(1, 0, 0));
            side = math.normalizesafe(
                math.cross(math.normalizesafe(camera - mid), tangent),
                new float3(0, 1, 0)
            );
            length = math.distance(a, b) * 1.035f;
            color = Tint(l, (segment + .5f) / segments, clock);
        }
    }
}
