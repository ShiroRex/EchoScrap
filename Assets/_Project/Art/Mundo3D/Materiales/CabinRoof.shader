Shader "EchoScrap/Cabin Roof"
{
    Properties
    {
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Float) = 1
        _MetallicGlossMap ("Metallic Map", 2D) = "white" {}
        _Smoothness ("Smoothness", Range(0,1)) = 1
        [ToggleOff] _SpecularHighlights ("Specular Highlights", Float) = 0
        [ToggleOff] _EnvironmentReflections ("Environment Reflections", Float) = 0
        [ToggleOff] _ReceiveShadows ("Receive Shadows", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "DisableBatching" = "True" }
        Cull [_Cull]
        ZWrite On

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Random.hlsl"

        float4 _EchoCabinReveal;

        void RevealCombat(float3 positionWS, float4 positionCS)
        {
            // The cabin mesh is centered vertically. Keep the walls below its upper panels solid.
            if (_EchoCabinReveal.w == 0 || positionWS.y <= GetObjectToWorldMatrix()._m13 + .4
                || positionWS.y <= _EchoCabinReveal.y)
                return;

            float3 cameraToPlayer = _EchoCabinReveal.xyz - _WorldSpaceCameraPos;
            float3 cameraToRoof = positionWS - _WorldSpaceCameraPos;
            float depth = dot(cameraToRoof, cameraToPlayer) / max(.001, dot(cameraToPlayer, cameraToPlayer));
            if (depth <= .01 || depth >= 1)
                return;

            // Measure the opening at the player's depth so the nearby combat stays visible.
            float distanceFromPlayer = length(cameraToRoof / depth - cameraToPlayer);
            float coverage = smoothstep(3.5, 5.5, distanceFromPlayer);
            clip(coverage - InterleavedGradientNoise(floor(positionCS.xy), 0) - .0001);
        }

        float3 CabinWorldPosition(float4 positionCS)
        {
            float depth = positionCS.z;
            #if !UNITY_REVERSED_Z
            depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
            #endif
            return ComputeWorldSpacePosition(GetNormalizedScreenSpaceUV(positionCS), depth, UNITY_MATRIX_I_VP);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LitPassVertex
            #pragma fragment CabinFragment
            #pragma shader_feature_local_fragment _SPECULARHIGHLIGHTS_OFF
            #pragma shader_feature_local_fragment _ENVIRONMENTREFLECTIONS_OFF
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #define _NORMALMAP
            #define _METALLICSPECGLOSSMAP
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

            void CabinFragment(Varyings input, out half4 color : SV_Target0
                #ifdef _WRITE_RENDERING_LAYERS
                , out uint renderingLayers : SV_Target1
                #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                RevealCombat(input.positionWS, input.positionCS);
                LitPassFragment(input, color
                    #ifdef _WRITE_RENDERING_LAYERS
                    , renderingLayers
                    #endif
                );
            }
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ColorMask R

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment CabinDepth
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"

            half CabinDepth(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                if (_EchoCabinReveal.w != 0)
                    RevealCombat(CabinWorldPosition(input.positionCS), input.positionCS);
                return DepthOnlyFragment(input);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormalsOnly" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment CabinNormals
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"
            #define _NORMALMAP
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitDepthNormalsPass.hlsl"

            void CabinNormals(Varyings input, out half4 normal : SV_Target0
                #ifdef _WRITE_RENDERING_LAYERS
                , out uint renderingLayers : SV_Target1
                #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                if (_EchoCabinReveal.w != 0)
                    RevealCombat(CabinWorldPosition(input.positionCS), input.positionCS);
                DepthNormalsFragment(input, normal
                    #ifdef _WRITE_RENDERING_LAYERS
                    , renderingLayers
                    #endif
                );
            }
            ENDHLSL
        }
    }
}
