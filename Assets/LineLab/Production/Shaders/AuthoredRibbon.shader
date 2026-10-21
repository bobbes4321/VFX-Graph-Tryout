Shader "LineLab/AuthoredRibbon"
{
    Properties
    {
        _MainTex ("Library texture atlas", 2D) = "white" {}
        _AtlasGrid ("Atlas grid", Float) = 1
        _Tile ("Tile (-1 for precomputed UVs)", Float) = -1
        [HideInInspector] _LineColorScale ("Line HDR reconstruction", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_local _ LINELAB_LINE_COLOR
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "AuthoredSurface.hlsl"
TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);
CBUFFER_START(UnityPerMaterial)
float _AtlasGrid;
float _Tile;
float _LineColorScale;
CBUFFER_END
struct Input
{
    float4 position : POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
};
struct Output
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
};
Output Vert(Input input)
{
    Output output;
    output.position = TransformObjectToHClip(input.position.xyz);
    output.uv = _Tile >= 0 ? AuthoredAtlasUv(input.uv, _Tile, _AtlasGrid) : input.uv;
    output.color = input.color;
#if defined(LINELAB_LINE_COLOR)
    output.color.rgb *= _LineColorScale;
#endif
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
