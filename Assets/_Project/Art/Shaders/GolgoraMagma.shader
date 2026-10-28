Shader "EchoScrap/GolgoraMagma"
{
    Properties { [HDR] _BaseColor("Magma", Color) = (1,.38,.025,.8) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "MoltenCrust"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 world : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS; output.world = position.positionWS;
                return output;
            }
            float2 Hash(float2 p) { return frac(sin(float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3)))) * 43758.5453); }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = input.world.xz * 2.1;
                float2 cell = floor(p), local = frac(p);
                float first = 10, second = 10;
                [unroll] for (int y=-1;y<=1;y++) [unroll] for (int x=-1;x<=1;x++)
                {
                    float2 offset = float2(x,y);
                    float2 delta = offset + Hash(cell+offset) - local;
                    float d = dot(delta,delta);
                    if (d < first) { second=first; first=d; } else second=min(second,d);
                }
                float cracks = 1-smoothstep(.025,.15,second-first);
                float pulse = .88 + .12*sin(_Time.y*2.2+p.x*.6+p.y*.4);
                half3 crust = half3(.085,.028,.018);
                half3 molten = _BaseColor.rgb * (1.5 + pulse);
                return half4(lerp(crust,molten,cracks),_BaseColor.a);
            }
            ENDHLSL
        }
    }
}
