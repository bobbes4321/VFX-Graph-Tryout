// Explicit float4 ABI. Keep in step with EffectRecipeData.cs.
#ifndef ABILITY_RECIPE_DATA_INCLUDED
#define ABILITY_RECIPE_DATA_INCLUDED

struct EffectRecipeData
{
    float4 Tint;
    float4 Timing;
    float4 Path;
    float4 Motion;
    float4 Pulse;
    float4 Layout;
};

struct RibbonRecipeData
{
    float4 Shape;
    float4 Surface;
    float4 Tint;
};

struct LayerRecipeData
{
    float4 Layout;
    float4 Size;
    float4 Motion;
    float4 Physics;
    float4 Appearance;
    float4 Placement;
};

EffectRecipeData AbilityLoadEffect(StructuredBuffer<float4> data, uint index)
{
    uint offset = index * 6;
    EffectRecipeData recipe;
    recipe.Tint = data[offset];
    recipe.Timing = data[offset + 1];
    recipe.Path = data[offset + 2];
    recipe.Motion = data[offset + 3];
    recipe.Pulse = data[offset + 4];
    recipe.Layout = data[offset + 5];
    return recipe;
}

RibbonRecipeData AbilityLoadRibbon(StructuredBuffer<float4> data, uint index)
{
    uint offset = index * 3;
    RibbonRecipeData recipe;
    recipe.Shape = data[offset];
    recipe.Surface = data[offset + 1];
    recipe.Tint = data[offset + 2];
    return recipe;
}

LayerRecipeData AbilityLoadLayer(StructuredBuffer<float4> data, uint index)
{
    uint offset = index * 6;
    LayerRecipeData recipe;
    recipe.Layout = data[offset];
    recipe.Size = data[offset + 1];
    recipe.Motion = data[offset + 2];
    recipe.Physics = data[offset + 3];
    recipe.Appearance = data[offset + 4];
    recipe.Placement = data[offset + 5];
    return recipe;
}
#endif
