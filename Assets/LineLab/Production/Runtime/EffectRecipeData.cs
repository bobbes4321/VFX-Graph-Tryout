using System;
using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace LineLab.Production
{
    /// <summary>An immutable catalog index resolved before entering gameplay jobs.</summary>
    [Serializable]
    public struct EffectId : IEquatable<EffectId>
    {
        public int Value;

        public EffectId(int value)
        {
            Value = value;
        }

        public bool Equals(EffectId other)
        {
            return Value == other.Value;
        }

        public override string ToString()
        {
            return Value.ToString();
        }
    }

    // These explicit float4 layouts are shared with RecipeData.hlsl. Assets bake
    // into these records once; jobs and graphs never read ScriptableObjects.
    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct EffectRecipeData
    {
        public float4 Tint;
        public float4 Timing; // charge seconds, core release, retirement, base width
        public float4 Path; // motion kind, up amplitude, side amplitude, spatial frequency
        public float4 Motion; // speed, noise cells, noise ticks, starting phase
        public float4 Pulse; // base intensity, pulse depth, spatial frequency, speed
        public float4 Layout; // used ribbon slots, used secondary slots, reserved, reserved
    }

    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct RibbonRecipeData
    {
        public float4 Shape; // width multiplier, opacity, whiten, phase offset
        public float4 Surface; // texture tile, color ramp row, width curve row, reserved
        public float4 Tint;
    }

    [Serializable, StructLayout(LayoutKind.Sequential)]
    public struct LayerRecipeData
    {
        public float4 Layout; // motion kind, count, texture tile, color ramp row
        public float4 Size; // minimum, maximum, aspect ratio, fade exponent
        public float4 Motion; // cycles per second, orbit radius, orbit spread, spin
        public float4 Physics; // radial speed, upward speed, upward variation, gravity
        public float4 Appearance; // opacity, whiten, release seconds, size curve row
        public float4 Placement; // both/source/target, height, source scale, target scale
    }

    public static class EffectRecipeLayout
    {
        public const int RampSamples = 32;
        public const int EffectFloat4s = 6;
        public const int RibbonFloat4s = 3;
        public const int LayerFloat4s = 6;
        public const int MaximumAbilities = 5000;
        public const string GraphVersion = "v1";
    }
}
