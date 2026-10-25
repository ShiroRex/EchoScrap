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
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p) { float2 i=floor(p),f=frac(p); f=f*f*(3-2*f); return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y); }
            float fbm(float2 p) { return noise(p)*0.57+noise(p*2.03)*0.28+noise(p*4.13)*0.15; }
            half4 frag(Varyings input): SV_Target
            {
                float2 p=input.uv*2-1;
                float r=length(p);
                clip(1-r);
                float angle=atan2(p.y,p.x);
                float spin=angle+r*8-_Time.y*0.35+(fbm(p*13+_Time.y*0.025)-0.5)*0.55;
                float2 flow=float2(cos(spin),sin(spin))*r*5;
                float clouds=fbm(flow+float2(_Time.y*0.07,0));
                float dust=fbm(flow*6);
                float arms=pow(saturate(sin(spin*3)*0.5+0.5),3)*clouds*(0.35+dust*1.7);
                float ring=exp(-pow((r-0.94)*35,2));
                float stars=step(0.992,hash(floor(flow*25)))*pow(saturate(1-length(frac(flow*25)-0.5)*2),3);
                float core=exp(-r*16);
                float3 color=float3(0.008,0.003,0.035)+clouds*float3(0.10,0.015,0.23);
                color+=arms*lerp(float3(0.65,0.12,1.4),float3(0.35,0.7,1.6),saturate(1-r*2))*smoothstep(1,0.1,r);
                color+=core*float3(1.8,1.3,2.6)+ring*float3(0.5,0.12,1.8)+stars*float3(1.4,1.1,2);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
