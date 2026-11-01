Shader "EchoScrap/Arena Dust"
{
    Properties { [HDR] _BaseColor("Motes tint", Color) = (1,.82,.63,.7) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off ZWrite Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            float4 _EchoArenaShaftPositions[4], _EchoArenaShaftDirections[4], _EchoArenaShaftColors[4];
            int _EchoArenaShaftCount;
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float2 uv:TEXCOORD1; half4 color:COLOR; };
            Varyings Vert(Attributes i)
            {
                Varyings o; o.positionWS=TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS);o.uv=i.uv;o.color=i.color*_BaseColor;return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 p=i.uv*2-1;float radial=saturate(1-dot(p,p));
                float alpha=radial*radial*i.color.a;
                clip(alpha-.002);
                float sceneDepth=LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS)),_ZBufferParams);
                float particleDepth=-TransformWorldToView(i.positionWS).z;
                alpha*=saturate((sceneDepth-particleDepth)*3);
                float3 illumination=float3(.32,.2,.48);
                [unroll]for(int beam=0;beam<4;beam++)
                {
                    if(beam>=_EchoArenaShaftCount)break;
                    float3 fromLight=i.positionWS-_EchoArenaShaftPositions[beam].xyz;
                    float distanceToLight=max(.05,length(fromLight));
                    float cone=smoothstep(_EchoArenaShaftDirections[beam].w,min(.999,_EchoArenaShaftDirections[beam].w+.025),dot(fromLight/distanceToLight,_EchoArenaShaftDirections[beam].xyz));
                    float range=saturate(1-distanceToLight/max(.1,_EchoArenaShaftPositions[beam].w));
                    illumination+=_EchoArenaShaftColors[beam].rgb*min(4,_EchoArenaShaftColors[beam].w*.10)*cone*range;
                }
                return half4(i.color.rgb*illumination*(1+radial*1.6),alpha);
            }
            ENDHLSL
        }
    }
}
