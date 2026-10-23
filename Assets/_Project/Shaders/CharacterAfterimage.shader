Shader "EchoScrap/Character Afterimage"
{
    Properties
    {
        _BaseMap("Atlas",2D)="white" {}
        _BaseColor("Tint",Color)=(0.95,0.78,0.36,0.5)
        _FrameRect("Frame UV rect",Vector)=(0,0.9375,0.125,0.0625)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _FrameRect, _BaseMap_TexelSize;
                half4 _BaseColor;
            CBUFFER_END
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(float4 p:POSITION,float2 uv:TEXCOORD0) { V o;o.p=TransformObjectToHClip(p.xyz);o.uv=uv;return o; }
            half4 Frag(V i):SV_Target
            {
                float2 inset=_BaseMap_TexelSize.xy*0.5;
                float2 uv=clamp(_FrameRect.xy+i.uv*_FrameRect.zw,_FrameRect.xy+inset,_FrameRect.xy+_FrameRect.zw-inset);
                half a=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).a;
                return half4(_BaseColor.rgb,a*_BaseColor.a);
            }
            ENDHLSL
        }
    }
}
