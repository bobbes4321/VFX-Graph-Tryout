using UnityEngine;

namespace LineLab.Production
{
    /// <summary>Cache engine property names once, outside the per-ability loop.</summary>
    internal static class EffectPropertyIds
    {
        public static readonly int Start = Shader.PropertyToID("Start");
        public static readonly int End = Shader.PropertyToID("End");
        public static readonly int Tint = Shader.PropertyToID("Tint");
        public static readonly int Shape = Shader.PropertyToID("Shape");
        public static readonly int Clock = Shader.PropertyToID("Clock");
        public static readonly int CameraPosition = Shader.PropertyToID("CameraPosition");
        public static readonly int ShaderClock = Shader.PropertyToID("_Clock");
        public static readonly int ShaderCamera = Shader.PropertyToID("_Camera");
        public static readonly int Tile = Shader.PropertyToID("_Tile");
        public static readonly int LineColorScale = Shader.PropertyToID("_LineColorScale");
    }
}
