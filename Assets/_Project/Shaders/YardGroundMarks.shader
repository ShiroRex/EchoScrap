Shader "EchoScrap/YardGroundMarks"
{
    Properties { _BaseColor("Dirt stain",Color)=(0.035,0.025,0.015,0.35) _Tracks("Wheel tracks",Float)=0 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor; float _Tracks;
            CBUFFER_END
            struct A { float4 p:POSITION;float3 n:NORMAL;float2 uv:TEXCOORD0; };
            struct V { float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 n:TEXCOORD1;float2 uv:TEXCOORD2;half fog:TEXCOORD3; };
            V vert(A v) { V o;o.world=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(v.n);o.uv=v.uv;o.fog=ComputeFogFactor(o.p.z);return o; }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p) { float2 i=floor(p),f=frac(p);f=f*f*(3-2*f);return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y); }
            half4 frag(V i):SV_Target
            {
                float n=noise(i.world.xz*1.2);
                float radius=length((i.uv-.5)*2);
                float alpha=(1-smoothstep(.30,.98,radius+n*.18))*_BaseColor.a;
                if(_Tracks>.5)
                {
                    float x=(i.uv.x-.5)*2.7;
                    float wheel=1-smoothstep(.075,.19,abs(abs(x)-.72));
                    float tread=lerp(.65,1,smoothstep(.2,.6,frac(i.uv.y*3+abs(x)*2)));
                    alpha=wheel*tread*(.4+n*.6)*_BaseColor.a;
                }
                InputData data=(InputData)0;data.positionWS=i.world;data.normalWS=normalize(i.n);
                data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.world);data.shadowCoord=TransformWorldToShadowCoord(i.world);
                data.bakedGI=SampleSH(data.normalWS);data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.p);data.shadowMask=1;
                SurfaceData surface=(SurfaceData)0;surface.albedo=_BaseColor.rgb;surface.alpha=1;surface.occlusion=1;surface.normalTS=half3(0,0,1);
                half4 color=UniversalFragmentPBR(data,surface);color.rgb=MixFog(color.rgb,i.fog);color.a=alpha;return color;
            }
            ENDHLSL
        }
    }
}
