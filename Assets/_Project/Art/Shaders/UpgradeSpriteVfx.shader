Shader "EchoScrap/UpgradeSpriteVfx"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        [HDR] _Color ("Tint", Color) = (1,1,1,1)
        _Glow ("Emission", Float) = 1
        _Billboard ("Face Camera", Float) = 0
        _Phase ("Simulation phase", Float) = 0
        _VisualKind ("Signal shape", Float) = 0
        _SoftDistance ("Depth intersection fade", Float) = 0
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
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_ST;
                float _Glow;
                float _Billboard;
                float _Phase;
                float _VisualKind;
                float _SoftDistance;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 world : TEXCOORD1; float3 normal : TEXCOORD2; half4 color : COLOR; };
            Varyings vert(Attributes input)
            {
                Varyings o;
                if (_VisualKind > 3.5 && _VisualKind < 4.5)
                {
                    float growth = smoothstep(0, .28, _Phase);
                    input.positionOS.y = -.4 + (input.positionOS.y + .4) * lerp(.08, 1, growth);
                    input.positionOS.xz *= lerp(.72, 1, growth);
                }
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
                o.positionCS=TransformWorldToHClip(world);o.uv=TRANSFORM_TEX(input.uv,_MainTex);o.color=input.color;
                o.world=world;o.normal=TransformObjectToWorldNormal(input.normalOS);return o;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half4 color;
                if (_VisualKind < .5) color=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,input.uv)*input.color*_Color;
                else
                {
                    float2 p=input.uv*2-1;
                    float r=length(p), angle=atan2(p.y,p.x), alpha=0;
                    color=input.color*_Color;
                    if (_VisualKind < 1.5)
                    {
                        float jagged=sin(angle*9+_Phase*5)*.012+sin(angle*17-_Phase*3)*.008;
                        float outer=1-smoothstep(.025,.075,abs(r-(.83+jagged)));
                        float inner=1-smoothstep(.018,.05,abs(r-.69));
                        float broken=smoothstep(.07,.2,abs(sin(angle*4+_Phase*.8)));
                        alpha=(outer*.72+inner*.16)*broken;
                    }
                    else if (_VisualKind < 2.5)
                    {
                        float head=1-smoothstep(.08,.28,abs(p.x)/max(.1,1-p.y));
                        float body=(1-smoothstep(.04,.12,abs(p.x)))*smoothstep(-1,-.55,p.y);
                        alpha=max(head*smoothstep(-.15,.8,p.y),body*.65)*(1-smoothstep(.9,1,abs(p.y)));
                    }
                    else if (_VisualKind < 3.5)
                    {
                        alpha=(1-smoothstep(.025,.11,abs(r-.72)))*smoothstep(-.3,.85,p.y);
                        alpha+=.24*(1-smoothstep(.025,.07,abs(r-.9)))*smoothstep(-.2,.8,p.y);
                    }
                    else if (_VisualKind < 4.5)
                    {
                        Light key=GetMainLight();
                        float diffuse=.3+saturate(dot(normalize(input.normal),key.direction))*.65;
                        color.rgb*=diffuse*lerp(half3(.65,.79,.9),key.color,.45);
                        color.a*=.92;
                        return color;
                    }
                    else if (_VisualKind < 6.5)
                    {
                        float speed=_VisualKind>5.5?9:-7;
                        float arms=pow(saturate(.5+.5*sin((angle+r*7-_Phase*speed)*3)),15);
                        alpha=arms*(1-smoothstep(.8,1,r))*smoothstep(.04,.2,r)*.44;
                        alpha+=(1-smoothstep(.018,.045,abs(r-.87)))*.2;
                    }
                    else
                    {
                        float axis=max(1-smoothstep(.018,.09,abs(p.x)),1-smoothstep(.018,.09,abs(p.y)));
                        float diagonal=max(1-smoothstep(.015,.055,abs(p.x-p.y)),1-smoothstep(.015,.055,abs(p.x+p.y)))*.45;
                        alpha=max(axis,diagonal)*(1-smoothstep(.15,.9,r));
                    }
                    color.a*=saturate(alpha);
                }
                if (_SoftDistance>.001)
                {
                    float depth=SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
                    float scene=LinearEyeDepth(depth,_ZBufferParams);
                    float particle=-TransformWorldToView(input.world).z;
                    color.a*=saturate((scene-particle)/_SoftDistance);
                }
                color.rgb*=_Glow;return color;
            }
            ENDHLSL
        }
    }
}
