Shader "EchoScrap/Oil Lantern Volume"
{
    Properties { _BeamColor("Warm oil scattering", Color) = (1,0.65,0.29,0.06) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Stencil { Ref 1 ReadMask 1 Comp NotEqual Pass Keep }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BeamColor;
            CBUFFER_END
            float _EchoArenaAtmosphereActive;
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; float3 positionWS:TEXCOORD1; float3 normalWS:TEXCOORD2; };
            Varyings Vert(Attributes v)
            {
                Varyings o;
                o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.uv=v.uv;
                return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                // The exterior raymarch supplies the illuminated air. Keep this cheap
                // fallback for the warehouse and renderers without the atmosphere pass.
                clip(.5-_EchoArenaAtmosphereActive);
                float sceneDepth=LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS)),_ZBufferParams);
                float beamDepth=-TransformWorldToView(i.positionWS).z;
                float intersection=saturate((sceneDepth-beamDepth)/.18);
                float3 view=GetWorldSpaceNormalizeViewDir(i.positionWS);
                float side=pow(saturate(1-abs(dot(normalize(i.normalWS),view))),1.6);
                // Keep the short path from the lantern to its first ground intersection
                // visible; a long source fade made the spotlight appear detached.
                float ends=smoothstep(0,0.012,i.uv.y)*(1-smoothstep(0.65,1,i.uv.y));
                float sourceScattering=12*(1-smoothstep(0.02,0.16,i.uv.y));
                float opacity=min(0.35,_BeamColor.a*ends*(0.25+0.75*side+sourceScattering));
                return half4(_BeamColor.rgb,opacity*intersection);
            }
            ENDHLSL
        }
    }
}
