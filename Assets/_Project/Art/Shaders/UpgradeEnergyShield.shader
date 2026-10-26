Shader "EchoScrap/UpgradeEnergyShield"
{
    Properties
    {
        _MainTex ("Energy cells", 2D) = "white" {}
        [HDR] _Color ("Shield tint", Color) = (.25,.9,1,1)
        _Phase ("Simulation phase", Float) = 0
        _Glow ("Emission", Float) = 1.4
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Phase;
                float _Glow;
            CBUFFER_END
            struct Attributes {float4 positionOS:POSITION;float3 normalOS:NORMAL;float2 uv:TEXCOORD0;};
            struct Varyings {float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float2 uv:TEXCOORD2;};
            Varyings vert(Attributes input)
            {
                Varyings o;o.world=TransformObjectToWorld(input.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);
                o.normal=TransformObjectToWorldNormal(input.normalOS);o.uv=input.uv;return o;
            }
            half4 frag(Varyings input):SV_Target
            {
                float3 view=normalize(GetCameraPositionWS()-input.world);
                float rim=pow(1-saturate(dot(normalize(input.normal),view)),2.5);
                float2 uv=input.uv*float2(6,4)+float2(_Phase*.035,_Phase*.02);
                half4 cells=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,frac(uv));
                float scan=pow(.5+.5*sin(input.uv.y*35-_Phase*5),14)*.16;
                float pulse=.85+.15*sin(_Phase*3);
                float alpha=saturate((rim*.75+cells.a*.18+scan)*pulse)*_Color.a;
                return half4(_Color.rgb*_Glow*(.7+rim*.9+cells.r*.2),alpha);
            }
            ENDHLSL
        }
    }
}
