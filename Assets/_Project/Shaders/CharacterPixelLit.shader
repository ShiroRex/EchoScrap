Shader "EchoScrap/Character Pixel Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Apollo atlas", 2D) = "white" {}
        [Normal] _BumpMap("OpenGL normal atlas", 2D) = "bump" {}
        [MainColor] _BaseColor("Tint / damage flash", Color) = (1,1,1,1)
        _EmissionColor("Damage accent", Color) = (0,0,0,0)
        _GlowMap("Reactor emission mask", 2D) = "black" {}
        [HDR] _GlowColor("Reactor glow", Color) = (0,0,0,0)
        _GlowStrength("Reactor intensity", Float) = 0
        _FrameRect("Frame UV rect", Vector) = (0,0.9375,0.125,0.0625)
        _NormalStrength("Relief", Range(0,2)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Pass
        {
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off ZWrite On
            // Keep the translucent lantern volume behind the readable character silhouette.
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            TEXTURE2D(_GlowMap); SAMPLER(sampler_GlowMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_TexelSize;
                float4 _FrameRect;
                half4 _BaseColor, _EmissionColor;
                half4 _GlowColor;
                half _NormalStrength, _GlowStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half3 tangentWS:TEXCOORD2; half3 bitangentWS:TEXCOORD3; float2 uv:TEXCOORD4; half3 vertexLighting:TEXCOORD5; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n=GetVertexNormalInputs(input.normalOS,input.tangentOS);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS;
                o.normalWS=n.normalWS; o.tangentWS=n.tangentWS; o.bitangentWS=n.bitangentWS;
                o.vertexLighting=VertexLighting(p.positionWS,n.normalWS);
                o.uv=input.uv; return o;
            }
            half4 Frag(Varyings input, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                float2 inset=_BaseMap_TexelSize.xy*0.5;
                float2 uv=clamp(_FrameRect.xy+input.uv*_FrameRect.zw,_FrameRect.xy+inset,_FrameRect.xy+_FrameRect.zw-inset);
                half4 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv);
                clip(color.a-0.5h);
                half3 normalTS=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),_NormalStrength);
                half3 normal=normalize(normalTS.x*input.tangentWS+normalTS.y*input.bitangentWS+normalTS.z*input.normalWS);
                normal*=IS_FRONT_VFACE(face,1,-1);
                InputData inputData=(InputData)0;
                inputData.positionWS=input.positionWS;
                inputData.positionCS=input.positionCS;
                inputData.normalWS=normal;
                inputData.viewDirectionWS=GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord=TransformWorldToShadowCoord(input.positionWS);
                inputData.bakedGI=SampleSH(normal);
                inputData.vertexLighting=input.vertexLighting;
                inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(input.positionCS);
                inputData.shadowMask=half4(1,1,1,1);
                half flash=saturate(max(_EmissionColor.r,max(_EmissionColor.g,_EmissionColor.b)));
                SurfaceData surface=(SurfaceData)0;
                surface.albedo=lerp(color.rgb*_BaseColor.rgb,_BaseColor.rgb,flash*0.75h);
                surface.alpha=color.a;
                surface.normalTS=normalTS;
                surface.occlusion=1;
                surface.emission=_EmissionColor.rgb*flash
                    + SAMPLE_TEXTURE2D(_GlowMap,sampler_GlowMap,uv).r * _GlowColor.rgb * _GlowStrength;
                // URP Simple Lit: scene ambient/probes, directional and local lights, shadows and AO.
                // No permanent color fill, light clamp or specular sheen on the cloth.
                half4 result=UniversalFragmentBlinnPhong(inputData,surface);
                result.rgb=MixFog(result.rgb,ComputeFogFactor(TransformWorldToHClip(input.positionWS).z));
                return result;
            }
            ENDHLSL
        }
    }
}
