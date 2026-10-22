using Unity.Collections;
using Unity.Mathematics;

namespace LineLab.Production
{
    /// <summary>The same card recipe feeds copied particles and direct particle jobs.</summary>
    public struct SurfaceRecipeView
    {
        [ReadOnly]
        public NativeArray<LineData> Lines;

        [ReadOnly]
        public NativeArray<LineData> Strands;

        [ReadOnly]
        public NativeArray<EffectRecipeData> Effects;

        [ReadOnly]
        public NativeArray<RibbonRecipeData> Ribbons;

        [ReadOnly]
        public NativeArray<LayerRecipeData> Layers;

        [ReadOnly]
        public NativeArray<float4> Ramps;
        public int RibbonSlots;
        public int LayerSlots;
        public int Segments;
        public int LayerSlot;
        public int CardsPerAbility;
        public bool Core;
        public float Clock;
        public float3 Camera;

        public Adornment Evaluate(int index)
        {
            if (Core)
            {
                int strandIndex = index / Segments;
                int segment = index % Segments;
                var line = Strands[strandIndex];
                int effectIndex = (int)line.Shape.x;
                var recipe = Effects[effectIndex];
                var ribbon = Ribbons[effectIndex * RibbonSlots + strandIndex % RibbonSlots];
                AbilityGeometry.Segment(
                    line,
                    recipe,
                    ribbon,
                    Ramps,
                    segment,
                    Segments,
                    Clock,
                    Camera,
                    out var midpoint,
                    out var tangent,
                    out var side,
                    out float length,
                    out var color
                );
                return new Adornment
                {
                    Position = midpoint,
                    X = tangent,
                    Y = side,
                    Size = new float2(
                        length,
                        AbilityGeometry.Width(line, ribbon, Ramps, (segment + .5f) / Segments)
                    ),
                    Color = color.xyz,
                    Alpha = color.w,
                    TextureIndex = ribbon.Surface.x,
                };
            }

            var ability = Lines[index / CardsPerAbility];
            int effect = (int)ability.Shape.x;
            var layer = Layers[effect * LayerSlots + LayerSlot];
            AbilityGeometry.Decor(
                ability,
                Effects[effect],
                layer,
                index % CardsPerAbility,
                Clock,
                Camera,
                Ramps,
                out var card
            );
            return card;
        }
    }
}
