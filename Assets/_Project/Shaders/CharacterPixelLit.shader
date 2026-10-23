Shader "EchoScrap/Character Pixel Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Apollo atlas", 2D) = "white" {}
        [Normal] _BumpMap("OpenGL normal atlas", 2D) = "bump" {}
        [MainColor] _BaseColor("Tint / damage flash", Color) = (1,1,1,1)
        _EmissionColor("Damage accent", Color) = (0,0,0,0)
        _FrameRect("Frame UV rect", Vector) = (0,0.9375,0.125,0.0625)
        _AmbientStrength("Palette fill", Range(0,1)) = 0.55
        _NormalStrength("Relief", Range(0,2)) = 1
        _KeyStrength("Main light response", Range(0,4)) = 1.5
        _LocalStrength("Local light response", Range(0,2)) = 0.65
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
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_TexelSize;
                float4 _FrameRect;
                half4 _BaseColor, _EmissionColor;
                half _AmbientStrength, _NormalStrength, _KeyStrength, _LocalStrength;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 tangentOS:TANGENT; float2 uv:TEXCOORD0; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; half3 tangentWS:TEXCOORD2; half3 bitangentWS:TEXCOORD3; float2 uv:TEXCOORD4; };
            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs n=GetVertexNormalInputs(input.normalOS,input.tangentOS);
                o.positionCS=p.positionCS; o.positionWS=p.positionWS;
                // Keep the drawn proportions at this steep perspective angle. Depth remains physical,
                // but all sprite pixels use the foot's projection scale (no enlarged head / tiny boots).
                float footW=TransformObjectToHClip(float3(0,0,0)).w;
                o.positionCS.z*=footW/o.positionCS.w;
                o.positionCS.w=footW;
                o.normalWS=n.normalWS; o.tangentWS=n.tangentWS; o.bitangentWS=n.bitangentWS;
                o.uv=input.uv; return o;
            }
            half3 Contribution(half3 normal, Light light)
            {
                return saturate((dot(normal,light.direction)+0.15h)/1.15h)*light.color*light.distanceAttenuation*light.shadowAttenuation;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float2 inset=_BaseMap_TexelSize.xy*0.5;
                float2 uv=clamp(_FrameRect.xy+input.uv*_FrameRect.zw,_FrameRect.xy+inset,_FrameRect.xy+_FrameRect.zw-inset);
                half4 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv);
                clip(color.a-0.5h);
                half3 normalTS=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),_NormalStrength);
                half3 normal=normalize(normalTS.x*input.tangentWS+normalTS.y*input.bitangentWS+normalTS.z*input.normalWS);
                InputData inputData=(InputData)0;
                inputData.positionWS=input.positionWS;
                inputData.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(input.positionCS);
                Light mainLight=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 lighting=_AmbientStrength+Contribution(normal,mainLight)*_KeyStrength;
                half3 additional=0;
                #if defined(_ADDITIONAL_LIGHTS)
                    #if USE_CLUSTER_LIGHT_LOOP
                    UNITY_LOOP for(uint lightIndex=0;lightIndex<min(URP_FP_DIRECTIONAL_LIGHTS_COUNT,MAX_VISIBLE_LIGHTS);lightIndex++)
                        additional+=Contribution(normal,GetAdditionalLight(lightIndex,input.positionWS,half4(1,1,1,1)));
                    #endif
                    uint count=GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(count)
                        additional+=Contribution(normal,GetAdditionalLight(lightIndex,input.positionWS,half4(1,1,1,1)));
                    LIGHT_LOOP_END
                #endif
                // Compress bright lantern pulses smoothly instead of flattening their entire lit side.
                lighting+=additional/(1+additional)*_LocalStrength;
                half flash=saturate(max(_EmissionColor.r,max(_EmissionColor.g,_EmissionColor.b)));
                half3 albedo=lerp(color.rgb*_BaseColor.rgb,_BaseColor.rgb,flash*0.75h);
                return half4(albedo*min(lighting,1.4h),color.a);
            }
            ENDHLSL
        }
    }
}
