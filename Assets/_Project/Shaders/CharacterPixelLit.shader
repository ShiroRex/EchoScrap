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
        _StatusTint("Cold status tint", Color) = (1,1,1,1)
        _StatusAmount("Cold status coverage", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        HLSLINCLUDE
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
                half4 _StatusTint;
                half _StatusAmount;
            CBUFFER_END
            // All geometry passes sample exactly the same animation frame and alpha cutoff.
            float2 SpriteAtlasUv(float2 uv)
            {
                float2 inset=_BaseMap_TexelSize.xy*0.5;
                return clamp(_FrameRect.xy+uv*_FrameRect.zw,_FrameRect.xy+inset,_FrameRect.xy+_FrameRect.zw-inset);
            }
            half4 SpriteSample(float2 uv)
            {
                half4 color=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv);
                clip(color.a-0.5h);
                return color;
            }
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
                float2 uv=SpriteAtlasUv(input.uv);
                half4 color=SpriteSample(uv);
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
                half luminance=dot(color.rgb,half3(.299h,.587h,.114h));
                half3 cold=max(.08h,luminance)*_StatusTint.rgb*1.45h;
                half3 albedo=lerp(color.rgb*_BaseColor.rgb,cold,saturate(_StatusAmount));
                surface.albedo=lerp(albedo,_BaseColor.rgb,flash*0.55h);
                surface.alpha=color.a;
                surface.normalTS=normalTS;
                surface.occlusion=1;
                half3 reactor=lerp(_GlowColor.rgb,_StatusTint.rgb,saturate(_StatusAmount));
                surface.emission=saturate(_EmissionColor.rgb)*flash*.16h
                    + SAMPLE_TEXTURE2D(_GlowMap,sampler_GlowMap,uv).r * reactor * _GlowStrength * (1.0h-.6h*_StatusAmount);
                // URP Simple Lit: scene ambient/probes, directional and local lights, shadows and AO.
                // No permanent color fill, light clamp or specular sheen on the cloth.
                half4 result=UniversalFragmentBlinnPhong(inputData,surface);
                result.rgb=MixFog(result.rgb,ComputeFogFactor(TransformWorldToHClip(input.positionWS).z));
                return result;
            }
            struct DepthVaryings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
            DepthVaryings DepthVert(Attributes input)
            {
                DepthVaryings o;
                o.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                o.uv=input.uv;
                return o;
            }
            half4 DepthFrag(DepthVaryings input):SV_Target
            {
                SpriteSample(SpriteAtlasUv(input.uv));
                return 0;
            }
            half4 NormalsFrag(Varyings input, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                float2 uv=SpriteAtlasUv(input.uv);
                SpriteSample(uv);
                half3 normalTS=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv),_NormalStrength);
                float3 normalWS=normalize(normalTS.x*input.tangentWS+normalTS.y*input.bitangentWS+normalTS.z*input.normalWS);
                normalWS*=IS_FRONT_VFACE(face,1,-1);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS=PackNormalOctQuadEncode(normalWS);
                half3 packedNormalWS=PackFloat2To888(saturate(octNormalWS*0.5+0.5));
                return half4(packedNormalWS,0);
            #else
                return half4(normalWS,0);
            #endif
            }
            half4 FocusMaskFrag(DepthVaryings input):SV_Target
            {
                SpriteSample(SpriteAtlasUv(input.uv));
                return half4(1,1,1,1);
            }
            half4 OccludedCollectorFrag(DepthVaryings input):SV_Target
            {
                half4 sprite=SpriteSample(SpriteAtlasUv(input.uv));
                half detail=dot(sprite.rgb,half3(.299h,.587h,.114h));
                return half4(half3(.95h,.58h,.22h)*lerp(.55h,1.0h,detail),.8h);
            }
            float3 _LightDirection;
            float3 _LightPosition;
            DepthVaryings ShadowVert(Attributes input)
            {
                DepthVaryings o;
                float3 world=TransformObjectToWorld(input.positionOS.xyz);
                float3 normal=TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection=normalize(_LightPosition-world);
            #else
                float3 lightDirection=_LightDirection;
            #endif
                // Bias the double-sided upright card on the side facing the casting light.
                normal*=dot(normal,lightDirection)<0 ? -1 : 1;
                o.positionCS=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(world,normal,lightDirection)));
                o.uv=input.uv;
                return o;
            }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
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
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile_fog
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull Off ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull Off ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode"="DepthNormalsOnly" }
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
        Pass
        {
            Name "ArenaFocusMask"
            Tags { "LightMode"="ArenaFocusMask" }
            Cull Off ZWrite Off ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment FocusMaskFrag
            ENDHLSL
        }
        Pass
        {
            Name "ArenaOccludedCollector"
            Tags { "LightMode"="ArenaOccludedCollector" }
            Cull Off ZWrite Off ZTest Greater
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref 1 WriteMask 1 Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment OccludedCollectorFrag
            ENDHLSL
        }
        Pass
        {
            Name "ArenaCollectorFocusMask"
            Tags { "LightMode"="ArenaCollectorFocusMask" }
            Cull Off ZWrite Off ZTest Always
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment FocusMaskFrag
            ENDHLSL
        }
    }
}
