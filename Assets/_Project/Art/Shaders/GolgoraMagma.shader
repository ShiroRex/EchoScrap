Shader "EchoScrap/GolgoraMagma"
{
    Properties
    {
        [HDR] _BaseColor("Magma", Color) = (1,.38,.025,.8)
        _EffectAge("Simulation age", Float) = 0
        _Seed("Variation", Float) = 0
        _Boundary("Boundary only", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "FlowingMoltenCrust"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _EffectAge, _Seed, _Boundary;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; float2 uv : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS; output.world = position.positionWS; output.uv = input.uv;
                return output;
            }
            float2 Hash(float2 p) { return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3)))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 i=floor(p), f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(Hash(i).x,Hash(i+float2(1,0)).x,f.x),lerp(Hash(i+float2(0,1)).x,Hash(i+1).x,f.x),f.y);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 polar=input.uv*2-1;
                float radius=length(polar), angle=atan2(polar.y,polar.x), time=_EffectAge;
                float rimNoise=sin(angle*11+_Seed)*.009+sin(angle*19-_Seed*.7)*.006;
                float outer=1-smoothstep(.986,1,radius);
                if(_Boundary>.5)
                {
                    // The contour remains inside the exact damaging radius.
                    float inner=smoothstep(.928+rimNoise,.958+rimNoise,radius);
                    float wire=1-smoothstep(.007,.02,abs(radius-.981));
                    float flow=.86+.14*sin(angle*7-time*2.6);
                    return half4(lerp(_BaseColor.rgb*1.5,half3(3.2,1.8,.42),wire*.65)*flow,outer*inner*_BaseColor.a);
                }
                float2 p=input.world.xz*1.55+_Seed;
                float2 drift=float2(Noise(p*.63+time*.12),Noise(p*.63-time*.09+9))-.5;
                p+=drift*.62+float2(time*.105,-time*.073);
                float2 cell = floor(p), local = frac(p);
                float first = 10, second = 10;
                [unroll] for (int y=-1;y<=1;y++) [unroll] for (int x=-1;x<=1;x++)
                {
                    float2 offset = float2(x,y);
                    float2 h=Hash(cell+offset);
                    float2 delta = offset + h + .075*sin(time*.65+h*6.283) - local;
                    float d = dot(delta,delta);
                    if (d < first) { second=first; first=d; } else second=min(second,d);
                }
                float cracks=1-smoothstep(.035,.24,second-first);
                float wideFlow=Noise(p*.7+drift+float2(-time*.12,time*.07));
                float melt=saturate(cracks*.9+smoothstep(.63,.85,wideFlow)*.5);
                float grain=Noise(p*13);
                half3 crust=lerp(half3(.045,.025,.021),half3(.19,.07,.027),grain*.6+first*.3);
                half3 molten=lerp(half3(2.25,.18,.009),half3(4.4,1.65,.18),pow(melt,3));
                float heat=.89+.11*sin(time*2.5+p.x*.9+p.y*.7);
                half3 color=lerp(crust,molten*heat,melt)*lerp(half3(1,1,1),_BaseColor.rgb,.22);
                float edge=1-smoothstep(.015,.055,abs(radius-(.944+rimNoise)));
                color+=half3(1.3,.22,.012)*edge;
                return half4(color,outer*_BaseColor.a*lerp(.68,1,melt));
            }
            ENDHLSL
        }
    }
}
