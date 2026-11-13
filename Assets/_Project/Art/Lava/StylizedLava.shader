Shader "EchoScrap/StylizedLava"
{
    Properties
    {
        _BaseMap("Detalle original",2D)="white"{}
        _LavaTime("Tiempo de combate",Float)=0
    }
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
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float _LavaTime;
            CBUFFER_END
            struct A { float4 positionOS:POSITION;float2 uv:TEXCOORD0; };
            struct V { float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1; };
            V vert(A i){V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.uv=i.uv;return o;}
            float hash(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453);}
            float noise(float2 p){float2 a=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(a),hash(a+float2(1,0)),f.x),lerp(hash(a+float2(0,1)),hash(a+1),f.x),f.y);}
            half4 frag(V i):SV_Target
            {
                float t=_LavaTime;
                float2 p=i.world.xz*.8+float2(t*.06,-t*.04);
                p+=float2(noise(p+t*.08),noise(p+9-t*.06))*.65;
                float n=noise(p)+noise(p*2.1)*.22;
                float cracks=1-smoothstep(.035,.13,abs(n-.58));
                float pools=smoothstep(.65,.9,n);
                float hot=saturate(cracks+pools*.8);
                float wave=.5+.5*sin(n*28-t*1.7);
                float3 crust=lerp(float3(.08,.009,.018),float3(.42,.025,.008),saturate(n));
                float3 molten=lerp(float3(1.5,.12,.005),float3(2.4,1.05,.08),wave*hot);
                float detail=dot(SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,i.uv).rgb,float3(.3,.5,.2));
                return half4(lerp(crust,molten,hot)*lerp(.65,1.1,detail),1);
            }
            ENDHLSL
        }
    }
}
