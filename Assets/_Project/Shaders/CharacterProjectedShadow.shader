Shader "EchoScrap/Character Projected Shadow"
{
    Properties
    {
        _BaseMap("Animated silhouette", 2D) = "white" {}
        _FrameRect("Frame UV rect", Vector) = (0,0.9375,0.125,0.0625)
        _BaseColor("Shadow", Color) = (0.005,0.006,0.009,0.65)
        _GroundHeight("Floor height", Float) = 0.015
        _Length("Projection length", Range(0.1,1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-21" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_TexelSize, _FrameRect;
                half4 _BaseColor;
                float _GroundHeight, _Length;
            CBUFFER_END
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; };
            V Vert(float4 p:POSITION, float2 uv:TEXCOORD0)
            {
                V o;
                float3 world=TransformObjectToWorld(p.xyz);
                float3 light=GetMainLight().direction;
                float height=max(0,world.y-_GroundHeight);
                world.xz-=light.xz*height/max(abs(light.y),0.35)*_Length;
                world.y=_GroundHeight;
                o.p=TransformWorldToHClip(world); o.uv=uv; return o;
            }
            half Alpha(float2 uv)
            {
                float2 inset=_BaseMap_TexelSize.xy*0.5;
                uv=clamp(uv,_FrameRect.xy+inset,_FrameRect.xy+_FrameRect.zw-inset);
                return SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv).a;
            }
            half4 Frag(V i):SV_Target
            {
                float2 uv=_FrameRect.xy+i.uv*_FrameRect.zw;
                float2 stepUV=_BaseMap_TexelSize.xy*0.8;
                half alpha=Alpha(uv)*0.4h;
                alpha+=(Alpha(uv+float2(stepUV.x,0))+Alpha(uv-float2(stepUV.x,0))
                    +Alpha(uv+float2(0,stepUV.y))+Alpha(uv-float2(0,stepUV.y)))*0.15h;
                return half4(_BaseColor.rgb,alpha*_BaseColor.a*lerp(1,0.55,saturate(i.uv.y)));
            }
            ENDHLSL
        }
    }
}
