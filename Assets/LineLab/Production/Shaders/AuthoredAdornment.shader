Shader "LineLab/AuthoredAdornment"
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
StructuredBuffer<float4> _Lines, _Effects, _Layers, _Ramps;
TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);
uint _LayerSlots, _LayerSlot, _CardsPerAbility;
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
    uint ability = instance / _CardsPerAbility, particle = instance % _CardsPerAbility;
    AbilityLine beam;
    beam.start = _Lines[ability * 4];
    beam.end = _Lines[ability * 4 + 1];
    beam.color = _Lines[ability * 4 + 2];
    beam.shape = _Lines[ability * 4 + 3];
    uint effectIndex = (uint)beam.shape.x;
    EffectRecipeData recipe = AbilityLoadEffect(_Effects, effectIndex);
    LayerRecipeData layer = AbilityLoadLayer(_Layers, effectIndex * _LayerSlots + _LayerSlot);
    Adornment card;
    AbilityDecor(beam, recipe, layer, particle, _Clock, _Camera, _Ramps, card);
    uint corner = vertex % 6;
    float u = (corner == 1 || corner == 2 || corner == 4) ? 1 : 0;
    float v = (corner == 2 || corner == 4 || corner == 5) ? 1 : 0;
    Output output;
    output.position = TransformWorldToHClip(card.Position + (u - .5) * card.X * card.Size.x +
                                            (v - .5) * card.Y * card.Size.y);
    output.uv = AuthoredAtlasUv(float2(u, v), card.TextureIndex, _AtlasGrid);
    output.color = float4(card.Color, card.Alpha);
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
