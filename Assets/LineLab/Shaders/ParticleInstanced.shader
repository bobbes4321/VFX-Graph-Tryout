Shader "LineLab/ParticleInstanced"
{
    Properties
    {
        _MainTex ("Profile", 2D) = "white" {}
    }
    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }
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
            #pragma instancing_options procedural:ParticleInstancingSetup
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // This renderer requests Position/Color/UV, with no animation-frame
            // stream. Match its 52-byte per-particle buffer exactly.
            struct LabParticleInstanceData
            {
                float3x4 transform;
                uint color;
            };
            #define UNITY_PARTICLE_INSTANCE_DATA LabParticleInstanceData
            #define UNITY_PARTICLE_INSTANCE_DATA_NO_ANIM_FRAME
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ParticlesInstancing.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            struct A
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };
            V Vert(A a)
            {
                UNITY_SETUP_INSTANCE_ID(a);
                V o;
                o.vertex = TransformObjectToHClip(a.vertex.xyz);
                o.uv = a.uv;
                o.color = a.color;
            #if defined(UNITY_PARTICLE_INSTANCING_ENABLED)
                uint packed = unity_ParticleInstanceData[unity_InstanceID].color;
                o.color = half4(packed & 255, (packed >> 8) & 255, (packed >> 16) & 255, (packed >> 24) & 255) /
                          255.0;
            #endif
                return o;
            }
            half4 Frag(V i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                return half4(i.color.rgb * tex.rgb, i.color.a * tex.a);
            }
            ENDHLSL
        }
    }
}
