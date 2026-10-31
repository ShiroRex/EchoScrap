Shader "EchoScrap/GolgoraRingFire"
{
    Properties
    {
        _Layer("Layer", Float) = 0
        _EffectAge("Simulation age", Float) = 0
        _Detail("Decorative intensity", Range(0,1)) = 1
        _ArcLength("Arc metres", Float) = 20
        _Fade("Opacity", Range(0,1)) = 1
        [HDR] _HotColor("White hot slag", Color) = (3.5,2.6,1.15,1)
        [HDR] _FireColor("Molten orange", Color) = (2.2,.38,.035,1)
        _CoalColor("Cooling crust", Color) = (.055,.018,.014,1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "SlagFire"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Layer, _EffectAge, _Detail, _ArcLength, _Fade;
                half4 _HotColor, _FireColor, _CoalColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS; output.world = position.positionWS; output.uv = input.uv;
                return output;
            }
            float Hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7))) * 43758.5453); }
            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p); f = f*f*(3-2*f);
                return lerp(lerp(Hash(cell),Hash(cell+float2(1,0)),f.x),
                            lerp(Hash(cell+float2(0,1)),Hash(cell+1),f.x),f.y);
            }
            float Turbulence(float2 p) { return Noise(p)*.63 + Noise(p*2.13+11.7)*.27 + Noise(p*4.37+3.1)*.1; }
            half4 Frag(Varyings input) : SV_Target
            {
                float age = _EffectAge * _Detail;
                float2 uv = input.uv;
                float x = uv.x * max(1,_ArcLength);
                if (_Layer > 2.5)
                {
                    float grain = Noise(input.world.xz*18);
                    float flecks = smoothstep(.24,.65,grain);
                    return half4(_CoalColor.rgb*(.55+grain),flecks*.58*_Fade*_Detail);
                }
                if (_Layer > 1.5)
                {
                    float grain = Noise(input.world.xy*37+input.world.z*9);
                    half3 slag = lerp(_CoalColor.rgb,_FireColor.rgb,step(.48,grain));
                    slag = lerp(slag,_HotColor.rgb,pow(saturate(grain),7));
                    return half4(slag,.95*_Fade*_Detail);
                }
                if (_Layer > .5)
                {
                    float height = 1-abs(uv.y*2-1);
                    float flame = Turbulence(float2(x*3.5+Noise(float2(x*.8,age))*.8,height*3.7-age*3.2));
                    float threads = Noise(float2(x*13-age*.9,height*9-age*5));
                    float tongue = .32+.68*flame;
                    float torn = 1-smoothstep(tongue-.16,tongue+.05,height*.83);
                    float heat = saturate(1-height*.85+flame*.3);
                    half3 color = lerp(half3(.65,.035,.008),_FireColor.rgb,smoothstep(.12,.65,heat));
                    color = lerp(color,_HotColor.rgb,pow(saturate(heat),5)*(.65+threads*.35));
                    float edge = smoothstep(0,.09,height);
                    return half4(color,torn*edge*(.7+threads*.25)*_Fade*_Detail);
                }
                // The footprint remains fully readable with detail disabled. Its edges are never displaced.
                float radialEdge = abs(uv.y*2-1);
                float grain = Turbulence(float2(x*4+age*.4,uv.y*6-age*1.8));
                float distortion = Noise(float2(x*1.5,uv.y*4-age))*.9;
                float crack = pow(1-abs(sin(x*8.7+uv.y*12+distortion*7)),13);
                float hotVein = saturate(crack*.85+pow(grain,3)*1.2);
                half3 crust = lerp(_CoalColor.rgb,half3(.4,.045,.008),grain);
                half3 color = lerp(crust,_FireColor.rgb,hotVein);
                color = lerp(color,_HotColor.rgb,hotVein*hotVein*.6);
                float edge = smoothstep(.86,.99,radialEdge);
                color = lerp(color,lerp(_FireColor.rgb,_HotColor.rgb,.45),edge*.8);
                color = lerp(lerp(_FireColor.rgb,_HotColor.rgb,edge*.65),color,.32+.68*_Detail);
                return half4(color,.94*_Fade);
            }
            ENDHLSL
        }
    }
}
