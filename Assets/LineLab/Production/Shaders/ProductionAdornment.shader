Shader "LineLab/ProductionAdornment"
{
    Properties
    {
        _MainTex ("Profile", 2D) = "white" {}
    }
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "AbilityCommon.hlsl"
            StructuredBuffer<float4> _Lines;
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            int _Layer;
            float _Clock;
            float3 _Camera;
            struct V
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            V Vert(uint vertex : SV_VertexID, uint instance : SV_InstanceID)
            {
                uint n = AbilityLayerCount(_Layer), index = instance / n, j = instance % n;
                AbilityLine l;
                l.start = _Lines[index * 4];
                l.end = _Lines[index * 4 + 1];
                l.color = _Lines[index * 4 + 2];
                l.shape = _Lines[index * 4 + 3];
                Adornment d;
                AbilityDecor(l, _Layer, j, _Clock, _Camera, d);
                uint c = vertex % 6;
                float u = (c == 1 || c == 2 || c == 4) ? 1 : 0, v = (c == 2 || c == 4 || c == 5) ? 1 : 0;
                V o;
                o.position =
                    TransformWorldToHClip(d.Position + (u - .5) * d.X * d.Size.x + (v - .5) * d.Y * d.Size.y);
                o.uv = float2(u, v);
                o.color = float4(d.Color, d.Alpha);
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
