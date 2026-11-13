Shader "EchoScrap/UpgradeEnergyShield"
{
    Properties
    {
        _MainTex ("Energy cells", 2D) = "white" {}
        [HDR] _Color ("Shield tint", Color) = (.25,.9,1,1)
        _Phase ("Simulation phase", Float) = 0
        _Glow ("Emission", Float) = 1
        _ImpactStrength ("Impact ripple", Range(0,1)) = 0
        _ImpactDirection ("Impact direction", Vector) = (0,0,1,0)
        _FrontOpacity ("Front surface", Range(0,1)) = .20
        _BackOpacity ("Rear surface", Range(0,1)) = .09
        _RimOpacity ("Curved rim", Range(0,1)) = .55
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Phase;
                float _Glow;
                float _ImpactStrength;
                float3 _ImpactDirection;
                float _FrontOpacity, _BackOpacity, _RimOpacity;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;};
            Varyings vert(Attributes input)
            {
                Varyings o;o.world=TransformObjectToWorld(input.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);
                o.normal=TransformObjectToWorldNormal(input.normalOS);o.uv=input.uv;return o;
            }
            half4 frag(Varyings input, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                float3 view=normalize(GetCameraPositionWS()-input.world);
                float3 normal=normalize(input.normal);
                float front=IS_FRONT_VFACE(face,1.0,0.0);
                float facing=abs(dot(normal,view));
                float rim=pow(1-facing,2.6);
                float2 p=input.uv*float2(20,13)+float2(_Phase*.06,0);
                float2 repeat=float2(1,1.73205);
                float2 a=frac(p/repeat)*repeat-repeat*.5;
                float2 b=frac((p-repeat*.5)/repeat)*repeat-repeat*.5;
                float2 cell=dot(a,a)<dot(b,b)?a:b;
                cell=abs(cell);
                float hex=max(cell.x*.866025+cell.y*.5,cell.y);
                float edges=smoothstep(.39,.44,hex);
                float scan=exp(-pow((input.uv.y-frac(_Phase*.19))*24,2));
                float pulse=.91+.09*sin(_Phase*2.4);
                float impactDot=clamp(dot(normal,normalize(_ImpactDirection+float3(0,0,.0001))),-1,1);
                float distance=acos(impactDot)/3.141593;
                float ripple=exp(-pow((distance-(1-_ImpactStrength)*.8)*20,2))*_ImpactStrength;
                float contact=pow(saturate(impactDot),18)*_ImpactStrength*_ImpactStrength;
                float surface=lerp(_BackOpacity,_FrontOpacity,front);
                float alpha=(rim*_RimOpacity+edges*surface*.35+scan*surface*.3+ripple*.3+contact*.16)*pulse*_Color.a;
                alpha*=lerp(.42,1.0,front);
                half3 color=_Color.rgb*_Glow*(.55+rim*.32+ripple*.3)*lerp(.72,1.0,front);
                return half4(color,saturate(alpha));
            }
            ENDHLSL
        }
    }
}
