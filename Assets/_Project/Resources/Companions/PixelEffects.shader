Shader "EchoScrap/CompanionPixelEffects"
{
    Properties { _Tint("Palette",Color)=(1,1,1,1) _Kind("Effect",Float)=0 _Frame("Frame",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes {float4 positionOS:POSITION;float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;};
            CBUFFER_START(UnityPerMaterial)
            float4 _Tint;float _Kind;float _Frame;
            CBUFFER_END
            Varyings vert(Attributes v){Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.uv=v.uv;return o;}
            half4 frag(Varyings v):SV_Target
            {
                float2 p=(floor(v.uv*32)+.5)/16-1;
                float2 a=abs(p);float mask=0;float highlight=0;
                if(_Kind<.5) // Open shield silhouette, stepped rim and travelling rune.
                {
                    float width=.72-max(0,-p.y)*.55;
                    mask=step(a.x,width)*step(-.85,p.y)*step(p.y,.78);
                    mask*=max(step(width-.14,a.x),step(.65,p.y));
                    highlight=step(.75,frac((p.y+_Frame*.09)*2));
                }
                else if(_Kind<1.5){mask=step(min(a.x,a.y),.18)*step(max(a.x,a.y),.7);highlight=step(.25,p.y);}
                else if(_Kind<2.5){mask=step(a.x+a.y,.85);highlight=step(a.x+a.y,.45);}
                else if(_Kind<3.5){float w=.55-(p.y+.6)*.28+.12*sin(floor(p.y*8)+_Frame);mask=step(a.x,w)*step(-.65,p.y)*step(p.y,.85);highlight=step(a.x,w*.45)*step(p.y,.2);}
                else if(_Kind<4.5){mask=step(min(min(a.x,a.y),abs(a.x-a.y)),.08)*step(max(a.x,a.y),.82);highlight=step(max(a.x,a.y),.35);}
                else if(_Kind<5.5){float r=length(p);mask=step(.65,r)*step(r,.82);highlight=step(.6,sin(atan2(p.y,p.x)*8+_Frame*.4));}
                else if(_Kind<6.5){float z=abs(p.y);mask=step(abs(a.x-(.5-z*.6)),.09)*step(z,.75);highlight=step(0,p.y);}
                else {float d=a.x+a.y;mask=max(step(.65,d)*step(d,.83),step(min(a.x,a.y),.07)*step(max(a.x,a.y),.9));highlight=step(d,.6);}
                clip(mask-.1);
                return half4(lerp(_Tint.rgb*.75,min(1,_Tint.rgb+.35),highlight),_Tint.a);
            }
            ENDHLSL
        }
    }
}
