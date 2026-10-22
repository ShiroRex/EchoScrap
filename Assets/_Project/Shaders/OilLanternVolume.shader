Shader "EchoScrap/Oil Lantern Volume"
{
    Properties { _BeamColor("Warm oil scattering", Color) = (1,0.65,0.29,0.06) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BeamColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 positionWS:TEXCOORD1; float3 normalWS:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.uv=v.uv;
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                clip(i.positionWS.y-0.035);
                float3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
                float side=pow(saturate(1-abs(dot(normalize(i.normalWS),view))),1.6);
                float ends=smoothstep(0,0.08,i.uv.y)*(1-smoothstep(0.65,1,i.uv.y));
                return half4(_BeamColor.rgb,_BeamColor.a*ends*(0.25+0.75*side));
            }
            ENDHLSL
        }
    }
}
