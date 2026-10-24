Shader "EchoScrap/PortalVortex"
{
    Properties { _Tint("Color", Color) = (0.4,0.05,1,1) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Tint;
            CBUFFER_END
            struct Attributes { float4 positionOS: POSITION; float2 uv: TEXCOORD0; };
            struct Varyings { float4 positionCS: SV_POSITION; float2 uv: TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz); o.uv=input.uv; return o;
            }
            half4 frag(Varyings input): SV_Target
            {
                float2 p=input.uv*2-1;
                float r=length(p);
                clip(1-r);
                float angle=atan2(p.y,p.x);
                float spiral=pow(saturate(sin(angle*4+r*23-_Time.y*1.7)*0.5+0.5),7);
                float ring=pow(saturate(1-abs(r-0.88)*10),3);
                float stars=pow(saturate(sin(p.x*157+p.y*83)*sin(p.y*137-p.x*71)),28);
                float3 color=lerp(float3(0.008,0.006,0.035), _Tint.rgb, spiral*smoothstep(0.08,0.9,r));
                color+=ring*float3(0.25,0.6,1.5)+stars*float3(0.4,0.7,1);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
