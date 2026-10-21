Shader "LineLab/AuthoredParticle"
{
    Properties
    {
        _MainTex ("Library texture atlas", 2D) = "white" {}
        _AtlasGrid ("Atlas grid", Float) = 1
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
#pragma target 4.5
#pragma vertex Vert
#pragma fragment Frag
#pragma multi_compile_instancing
#pragma instancing_options procedural : ParticleInstancingSetup
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
// Position, Color, UV and Custom1XYZW: 48 + 4 + 16 = 68 bytes.
// Custom1 carries texture tile and unquantized HDR RGB. Alpha retains
// Shuriken's Color32 precision; RGB no longer silently clips artist HDR.
struct AuthoredParticleInstance
{
    float3x4 transform;
    uint color;
    float4 custom;
};
#define UNITY_PARTICLE_INSTANCE_DATA AuthoredParticleInstance
#define UNITY_PARTICLE_INSTANCE_DATA_NO_ANIM_FRAME
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ParticlesInstancing.hlsl"
#include "AuthoredSurface.hlsl"
TEXTURE2D(_MainTex);
SAMPLER(sampler_MainTex);
float _AtlasGrid;
struct Input
{
    float4 position : POSITION;
    float4 uv : TEXCOORD0;
    float2 extra : TEXCOORD1;
    half4 color : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct Output
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
    float4 color : COLOR;
};
Output Vert(Input input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    Output output;
    output.position = TransformObjectToHClip(input.position.xyz);
    float4 custom = float4(input.uv.zw, input.extra.xy);
    float alpha = input.color.a;
#if defined(UNITY_PARTICLE_INSTANCING_ENABLED)
    custom = unity_ParticleInstanceData[unity_InstanceID].custom;
    alpha = ((unity_ParticleInstanceData[unity_InstanceID].color >> 24) & 255) / 255.0;
#endif
    output.uv = AuthoredAtlasUv(input.uv.xy, custom.x, _AtlasGrid);
    output.color = float4(custom.yzw, alpha);
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
