Shader "EchoScrap/GolgoraFoundryArmor"
{
    Properties
    {
        _BaseMap("Armor detail", 2D) = "white" {}
        _BaseColor("Armor tint", Color) = (1,1,1,1)
        _EmissionMap("Molten detail", 2D) = "black" {}
        [HDR] _EmissionColor("Core heat", Color) = (1.65,.56,.085,1)
        _Cutoff("Alpha cutoff", Range(0,1)) = .5
        _Cull("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "FoundryArmor"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _EMISSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _EmissionColor;
                half _Cutoff;
            CBUFFER_END
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; half3 normalWS:TEXCOORD1; float2 uv:TEXCOORD2; half fog:TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO };
            Varyings Vert(Attributes input)
            {
                Varyings output; UNITY_SETUP_INSTANCE_ID(input); UNITY_TRANSFER_INSTANCE_ID(input,output); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS=p.positionCS; output.positionWS=p.positionWS;
                output.normalWS=TransformObjectToWorldNormal(input.normalOS); output.uv=TRANSFORM_TEX(input.uv,_BaseMap);
                output.fog=ComputeFogFactor(p.positionCS.z); return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 detail=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,input.uv).rgb;
                half3 molten=SAMPLE_TEXTURE2D(_EmissionMap,sampler_EmissionMap,input.uv).rgb;
                half luminance=dot(detail,half3(.2126,.7152,.0722));
                // The source emission texture contains the entire albedo. Select red/orange seams
                // instead of emitting pale stone and armor, which previously washed out the silhouette.
                half hotMask=smoothstep(.23,.58,molten.r-max(molten.g,molten.b))*smoothstep(.28,.75,molten.r);
                half3 armor=lerp(half3(.055,.062,.07),half3(.24,.225,.205),luminance)*_BaseColor.rgb;
                half3 n=normalize(input.normalWS); half3 v=GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light main=GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse=saturate(dot(n,main.direction));
                half3 lighting=SampleSH(n)+half3(.22,.24,.27)+main.color*diffuse*main.shadowAttenuation;
                half rim=pow(1-saturate(dot(n,v)),3)*.18;
                half3 color=armor*lighting+half3(.32,.24,.17)*rim+_EmissionColor.rgb*hotMask;
                return half4(MixFog(color,input.fog),1);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
