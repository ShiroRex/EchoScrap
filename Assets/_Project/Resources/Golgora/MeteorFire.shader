Shader "EchoScrap/GolgoraMeteorFire"
{
    Properties
    {
        _MainTex("Ash atlas", 2D) = "white" {}
        [HDR] _BaseColor("Tint", Color) = (3.8,.48,.035,1)
        _Mode("Slag / fire / smoke / ember", Float) = 1
        _EffectAge("Simulation age", Float) = 0
        _Seed("Variation", Float) = 0
        _Opacity("Opacity", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Destination", Float) = 1
        _ZWrite("Depth write", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "FurnaceSlagAndWake"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha [_DstBlend]
            ZWrite [_ZWrite]
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Mode, _EffectAge, _Seed, _Opacity;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 local:TEXCOORD0; float3 normal:TEXCOORD1; float2 uv:TEXCOORD2; half4 color:COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                o.local=input.positionOS.xyz; o.normal=TransformObjectToWorldNormal(input.normalOS);
                o.uv=input.uv; o.color=input.color; return o;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i),Hash(i+float2(1,0)),f.x),lerp(Hash(i+float2(0,1)),Hash(i+1),f.x),f.y);
            }
            half4 Frag(Varyings input):SV_Target
            {
                float age=_EffectAge, seed=_Seed;
                if(_Mode<.5)
                {
                    clip(input.color.a*_Opacity-.003);
                    float3 p=input.local*5.2+seed;
                    float n=Noise(p.xz+Noise(p.xy)*2.4);
                    float edges=min(input.uv.x,min(input.uv.y,1-input.uv.x-input.uv.y));
                    float fracture=(1-smoothstep(.009,.038,edges))*lerp(.68,1,Noise(p.xy*3));
                    float vein=1-smoothstep(.015,.055,abs(n-.5));
                    float heat=saturate(max(fracture,vein*.94));
                    float pulse=.91+.09*sin(age*6+p.y*1.7);
                    half3 rock=_BaseColor.rgb*(.65+.35*Noise(p.xz*4))*input.color.rgb;
                    float light=.62+.38*saturate(dot(normalize(input.normal+float3(.001,.001,.001)),normalize(float3(-.3,1,-.5))));
                    half3 molten=lerp(half3(3.4,.24,.008),half3(6,3.15,.78),pow(heat,5));
                    return half4(lerp(rock*light,molten*pulse,heat),input.color.a*_Opacity);
                }
                if(_Mode<1.5)
                {
                    float2 uv=input.uv;
                    float edge=abs(frac(uv.x)-.5)*2;
                    float n=Noise(float2(uv.x*6+seed,uv.y*9-age*12));
                    n+=.43*Noise(float2(uv.x*15-seed,uv.y*23-age*21));
                    float tongue=saturate(1-edge-(n-.55)*.6);
                    float torn=smoothstep(.04,.38,tongue)*(1-smoothstep(.72,1.04,uv.y));
                    float core=pow(saturate(1-edge*1.7),2.4)*(1-uv.y*.55);
                    half3 fire=lerp(half3(1.5,.08,.008),_BaseColor.rgb,tongue);
                    fire+=half3(1.6,.85,.2)*core;
                    return half4(fire,torn*input.color.a*_BaseColor.a*_Opacity);
                }
                if(_Mode<2.5)
                {
                    half4 atlas=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv);
                    float shape=saturate(atlas.a);
                    half3 ash=_BaseColor.rgb*(.7+atlas.rgb*.7);
                    return half4(ash,shape*input.color.a*_BaseColor.a*_Opacity);
                }
                float2 p=input.uv*2-1;
                float spark=saturate(1-abs(p.x)*1.6)*saturate(1-abs(p.y));
                return half4(_BaseColor.rgb,pow(spark,1.4)*input.color.a*_Opacity);
            }
            ENDHLSL
        }
    }
}
