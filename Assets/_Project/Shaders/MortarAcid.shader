Shader "EchoScrap/Mortar Acid"
{
    Properties
    {
        [MainTexture] _BaseMap("Aseprite color atlas", 2D) = "white" {}
        [MainColor] _BaseColor("Tint and drying", Color) = (1,1,1,1)
        _GlowMap("Acid emission mask", 2D) = "black" {}
        [HDR] _GlowColor("Acid glow", Color) = (0.25,0.7,0.08,1)
        _GlowStrength("Glow intensity", Float) = 0.6
        _FrameRect("Frame UV rect", Vector) = (0,0,0.25,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_GlowMap); SAMPLER(sampler_GlowMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_TexelSize;
                float4 _FrameRect;
                half4 _BaseColor, _GlowColor;
                half _GlowStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half fog:TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.uv=input.uv;
                output.fog=ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 inset=_BaseMap_TexelSize.xy*0.5;
                float2 uv=clamp(_FrameRect.xy+input.uv*_FrameRect.zw,_FrameRect.xy+inset,_FrameRect.xy+_FrameRect.zw-inset);
                half4 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv)*_BaseColor;
                clip(color.a-0.01h);
                color.rgb+=SAMPLE_TEXTURE2D(_GlowMap,sampler_GlowMap,uv).r*_GlowColor.rgb*_GlowStrength;
                color.rgb=MixFog(color.rgb,input.fog);
                return color;
            }
            ENDHLSL
        }
    }
}
