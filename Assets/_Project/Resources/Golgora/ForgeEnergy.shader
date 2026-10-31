Shader "EchoScrap/GolgoraForgeEnergy"
{
    Properties
    {
        [HDR] _BaseColor("Forge tint", Color) = (1,.38,.04,1)
        _MainTex("Painted smoke / spark atlas", 2D) = "white" {}
        _Atlas("Columns, rows, frames, reserved", Vector) = (1,1,1,0)
        _Kind("Boundary / warning / atlas / spark / ribbon / slag / wave / molten / crown", Float) = 0
        _Progress("Attack progress", Float) = 0
        _EffectAge("Simulation clock (pause aware)", Float) = 0
        _Detail("Decorative detail", Float) = 1
        _Seed("Variation", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+8" "RenderType"="Transparent" }
        Pass
        {
            Name "ForgeEnergy"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _Atlas;
                float _Kind, _Progress, _EffectAge, _Detail, _Seed;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            Varyings Vert(Attributes i) { Varyings o; o.positionCS=TransformObjectToHClip(i.positionOS.xyz); o.uv=i.uv; o.color=i.color; return o; }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float Noise(float2 p)
            {
                float2 c=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(c),Hash(c+float2(1,0)),f.x),lerp(Hash(c+float2(0,1)),Hash(c+1),f.x),f.y);
            }
            float Fissures(float2 p)
            {
                float2 c=floor(p), f=frac(p); float first=8, second=8;
                [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
                {
                    float2 n=float2(x,y); float2 q=n+float2(Hash(c+n),Hash(c+n+19.3))-f; float d=dot(q,q);
                    if(d<first) { second=first; first=d; } else second=min(second,d);
                }
                return 1-smoothstep(.018,.08,second-first);
            }
            half4 Frag(Varyings i):SV_Target
            {
                float2 p=i.uv*2-1; float r=length(p); float angle=atan2(p.x,p.y);
                float age=_EffectAge, progress=saturate(_Progress);
                half3 rgb=_BaseColor.rgb*i.color.rgb; float alpha=_BaseColor.a*i.color.a;
                if(_Kind<.5)
                {
                    // The outer mesh edge is the exact gameplay boundary; only its interior is engraved.
                    float ticks=pow(saturate(cos(angle*36)),18);
                    float racing=pow(saturate(cos(angle*3-age*(2+progress*7))),14)*_Detail;
                    rgb*=1+ticks*.45+racing*.7;
                }
                else if(_Kind<1.5)
                {
                    float cracks=Fissures(p*7.5+_Seed);
                    float reveal=1-smoothstep(progress-.045,progress+.045,r);
                    float pulse=.5+.5*sin(age*(5+progress*15)-r*20);
                    float band=pow(saturate(cos((r-progress)*27)),18)*reveal;
                    float ticks=pow(saturate(cos(angle*36)),22)*smoothstep(.86,.94,r);
                    float heat=(cracks*.65*(.25+.75*_Detail)+band*.24)*reveal;
                    rgb=lerp(half3(.12,.045,.022),rgb*(1.1+progress),saturate(heat+ticks));
                    alpha*=saturate(.16+reveal*.2+heat*(.45+.2*pulse)+ticks*.5);
                    alpha*=1-smoothstep(.985,1,r);
                }
                else if(_Kind<2.5)
                {
                    float frame=min(_Atlas.z-1,floor(progress*_Atlas.z));
                    float2 tile=float2(fmod(frame,_Atlas.x),_Atlas.y-1-floor(frame/_Atlas.x));
                    float2 uv=(clamp(i.uv,.002,.998)+tile)/_Atlas.xy;
                    half4 art=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,uv);
                    rgb*=art.rgb; alpha*=art.a;
                }
                else if(_Kind<3.5)
                {
                    float taper=pow(saturate(1-abs(p.x)*(1.6+abs(p.y)*2.5)),2);
                    float tail=smoothstep(-1,-.65,p.y)*(1-smoothstep(.6,1,p.y));
                    float serration=.65+.35*Noise(float2(p.y*17+_Seed,age*8));
                    rgb=lerp(rgb,half3(2.7,1.6,.6),pow(saturate(1-length(p*float2(3,1))),5));
                    alpha*=taper*tail*serration;
                }
                else if(_Kind<4.5)
                {
                    float head=progress*1.7-.3;
                    float ribbon=smoothstep(head-.5,head-.05,i.uv.x)*(1-smoothstep(head,head+.12,i.uv.x));
                    float edges=pow(saturate(1-abs(p.y)),.65);
                    float veins=Noise(float2(i.uv.x*95-_Seed,i.uv.y*8+age*12));
                    rgb*=.65+veins*1.3;
                    rgb=lerp(rgb,half3(2.2,1.1,.32),pow(saturate(1-abs(p.y)*4),6)*.6);
                    alpha*=ribbon*edges*(.45+veins*.55);
                }
                else if(_Kind<5.5)
                {
                    float crust=Noise(i.uv*14+_Seed);
                    rgb=lerp(half3(.055,.035,.026),rgb,Fissures(i.uv*4+_Seed)*.6+crust*.22);
                }
                else if(_Kind<6.5)
                {
                    float rough=Noise(float2(angle*13,age*3))* .075;
                    float rim=1-smoothstep(.035,.12,abs(r-(.82+rough)));
                    float breaks=.35+.65*Noise(float2(angle*24,age*8));
                    rgb*=.5+breaks; alpha*=rim*breaks;
                }
                else if(_Kind<7.5)
                {
                    float crack=Fissures(p*5.5+_Seed);
                    float falloff=1-smoothstep(.35,1,r);
                    rgb=lerp(half3(.09,.027,.012),rgb*1.8,crack);
                    alpha*=falloff*(.36+.64*crack);
                }
                else if(_Kind<8.5)
                {
                    float teeth=pow(saturate(.5+.5*cos(i.uv.x*75.398)),5);
                    float filaments=Noise(float2(i.uv.x*85,i.uv.y*11-age*3));
                    alpha*=(1-smoothstep(.15,1,i.uv.y))*(.15+teeth*.55+filaments*.3);
                    rgb*=.65+teeth+filaments*.5;
                }
                else
                {
                    float width=pow(saturate(1-abs(p.y)),.7);
                    float grain=Noise(float2(i.uv.x*45-age*9,i.uv.y*12));
                    rgb*=.7+grain*.8; alpha*=width*(.6+grain*.4);
                }
                return half4(rgb,saturate(alpha));
            }
            ENDHLSL
        }
    }
}
