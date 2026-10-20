Shader "LineLab/ProceduralRibbon"
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
            #include "LineCommon.hlsl"
            StructuredBuffer<float4> _Lines;
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            int _Segments;
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
                uint s = vertex / 6, c = vertex % 6;
                float u = (c == 1 || c == 2 || c == 4) ? 1 : 0;
                float v = (c == 2 || c == 4 || c == 5) ? 1 : 0;
                LabLine l;
                l.start = _Lines[instance * 4];
                l.end = _Lines[instance * 4 + 1];
                l.color = _Lines[instance * 4 + 2];
                l.shape = _Lines[instance * 4 + 3];
                float3 a = LabPoint(l, (float)s / _Segments, _Clock),
                       b = LabPoint(l, (float)(s + 1) / _Segments, _Clock);
                float3 mid = (a + b) * .5, tangent = normalize(b - a + float3(1e-7, 0, 0));
                float3 side = normalize(cross(normalize(_Camera - mid), tangent));
                float3 p = mid + (u - .5) * (b - a) * 1.035 + (v - .5) * l.end.w * side;
                V o;
                o.position = TransformWorldToHClip(p);
                o.uv = float2(u, v);
                o.color = LabTint(l, (s + .5) / _Segments, _Clock);
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
