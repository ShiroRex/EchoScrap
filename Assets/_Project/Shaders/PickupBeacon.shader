Shader "EchoScrap/Pickup Beacon"
{
    Properties { [HDR] _BaseColor("Beacon green", Color) = (0.24,1,0.36,0.5) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            Varyings Vert(Attributes i) { Varyings o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; return o; }
            half4 Frag(Varyings i):SV_Target
            {
                float edge=saturate(1-abs(i.uv.x*2-1));
                float core=pow(edge,6);
                float fade=pow(1-i.uv.y,1.5)*smoothstep(0,0.04,i.uv.y);
                return half4(_BaseColor.rgb, _BaseColor.a*fade*(0.15*edge+0.85*core));
            }
            ENDHLSL
        }
    }
}
