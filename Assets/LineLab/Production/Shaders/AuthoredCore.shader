Shader "LineLab/AuthoredCore"
{
    Properties { _MainTex("Library texture atlas",2D)="white"{} _AtlasGrid("Atlas grid",Float)=1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
#pragma target 4.5
#pragma vertex Vert
#pragma fragment Frag
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "AbilityCommon.hlsl"
#include "AuthoredSurface.hlsl"
StructuredBuffer<float4> _Lines, _Effects, _Ribbons, _Ramps;
TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);
uint _RibbonSlots, _Segments;
float _Clock, _AtlasGrid;
float3 _Camera;
struct Output
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
};
Output Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
{
    uint segment = vertex / 6, corner = vertex % 6;
    float u = (corner == 1 || corner == 2 || corner == 4) ? 1 : 0;
    float v = (corner == 2 || corner == 4 || corner == 5) ? 1 : 0;
    AbilityLine beam;
    beam.start = _Lines[instance * 4];
    beam.end = _Lines[instance * 4 + 1];
    beam.color = _Lines[instance * 4 + 2];
    beam.shape = _Lines[instance * 4 + 3];
    uint effectIndex = (uint)beam.shape.x;
    EffectRecipeData recipe = AbilityLoadEffect(_Effects, effectIndex);
    RibbonRecipeData ribbon =
        AbilityLoadRibbon(_Ribbons, effectIndex * _RibbonSlots + instance % _RibbonSlots);
    float t = (segment + .5) / _Segments;
    float3 a = AbilityPoint(beam, recipe, (float)segment / _Segments, _Clock);
    float3 b = AbilityPoint(beam, recipe, (float)(segment + 1) / _Segments, _Clock);
    float3 midpoint = (a + b) * .5;
    float3 tangent = normalizesafe(b - a, float3(1, 0, 0));
    float3 side = normalizesafe(cross(normalizesafe(_Camera - midpoint, float3(0, 0, -1)), tangent),
                                float3(0, 1, 0));
    Output output;
    output.position =
        TransformWorldToHClip(midpoint + (u - .5) * tangent * distance(a, b) * 1.035 +
                              (v - .5) * side * AbilityWidth(beam, ribbon, _Ramps, t));
    output.uv = AuthoredAtlasUv(float2(u, v), ribbon.Surface.x, _AtlasGrid);
    output.color = AbilityTint(beam, recipe, ribbon, _Ramps, t, _Clock);
    return output;
}
half4 Frag(Output input) : SV_Target
{
    half4 profile = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
    return half4(input.color.rgb * profile.rgb, input.color.a * profile.a);
}
            ENDHLSL
        }
    }
}
