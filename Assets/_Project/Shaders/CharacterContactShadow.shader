Shader "EchoScrap/Character Contact Shadow"
{
    Properties { _BaseColor("Shadow",Color)=(0.025,0.02,0.035,0.42) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(float4 p:POSITION,float2 uv:TEXCOORD0) { V o;o.p=TransformObjectToHClip(p.xyz);o.uv=uv;return o; }
            half4 Frag(V i):SV_Target { float2 p=i.uv*2-1;half a=saturate(1-dot(p,p));return half4(_BaseColor.rgb,_BaseColor.a*a*a); }
            ENDHLSL
        }
    }
}
