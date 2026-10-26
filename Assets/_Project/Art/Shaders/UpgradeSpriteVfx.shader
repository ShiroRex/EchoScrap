Shader "EchoScrap/UpgradeSpriteVfx"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        [HDR] _Color ("Tint", Color) = (1,1,1,1)
        _Glow ("Emission", Float) = 1
        _Billboard ("Face Camera", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Source blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Destination blend", Float) = 10
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_ST;
                float _Glow;
                float _Billboard;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings o;
                float3 world=TransformObjectToWorld(input.positionOS.xyz);
                if(_Billboard>.5)
                {
                    float3 center=TransformObjectToWorld(float3(0,0,0));
                    float3 right=UNITY_MATRIX_I_V._m00_m10_m20;
                    float3 up=UNITY_MATRIX_I_V._m01_m11_m21;
                    float scaleX=length(GetObjectToWorldMatrix()._m00_m10_m20);
                    float scaleY=length(GetObjectToWorldMatrix()._m01_m11_m21);
                    world=center+right*input.positionOS.x*scaleX+up*input.positionOS.y*scaleY;
                }
                o.positionCS=TransformWorldToHClip(world);o.uv=TRANSFORM_TEX(input.uv,_MainTex);o.color=input.color;return o;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half4 tex=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv);
                half4 color=tex*input.color*_Color;color.rgb*=_Glow;return color;
            }
            ENDHLSL
        }
    }
}
