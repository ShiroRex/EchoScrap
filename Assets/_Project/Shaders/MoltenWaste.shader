Shader "EchoScrap/MoltenWaste"
{
    Properties
    {
        _BaseMap("Source texture",2D)="white"{}
        [HDR] _EmissionColor("Molten channels",Color)=(1.2,0.36,0.055,1)
        _BaseColor("Cooled slag",Color)=(0.08,0.055,0.035,1)
        _Cutoff("Cutoff",Float)=0.5
        _Cull("Cull",Float)=2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _EMISSION
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST, _EmissionColor, _BaseColor;
            CBUFFER_END
            struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct V { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; half fog:TEXCOORD2; };
            V vert(A v)
            {
                V o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.positionWS);
                o.normalWS=TransformObjectToWorldNormal(v.normalOS); o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            half4 frag(V i):SV_Target
            {
                float2 p=i.positionWS.xz*1.6+_BaseMap_ST.zw, cell=floor(p), f=frac(p);
                float first=10,second=10;
                [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
                {
                    float2 q=float2(x,y); float2 offset=q+float2(hash(cell+q),hash(cell+q+71.3))-f;
                    float d=dot(offset,offset); if(d<first) { second=first;first=d; } else second=min(second,d);
                }
                float channels=1-smoothstep(0.015,0.16,second-first);
                float grain=hash(floor(i.positionWS.xz*38));
                InputData data=(InputData)0; data.positionWS=i.positionWS; data.normalWS=normalize(i.normalWS);
                data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
                data.shadowCoord=TransformWorldToShadowCoord(i.positionWS); data.bakedGI=SampleSH(data.normalWS);
                data.vertexLighting=VertexLighting(i.positionWS,data.normalWS);
                data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS); data.shadowMask=half4(1,1,1,1);
                SurfaceData surface=(SurfaceData)0; surface.albedo=half3(0.035,0.025,0.02)*(0.7+grain*0.5);
                surface.emission=_EmissionColor.rgb*(channels*2.2+pow(saturate(1-first),8)*0.15);
                surface.alpha=1;surface.occlusion=1;surface.metallic=0.15;surface.smoothness=0.16;surface.normalTS=half3(0,0,1);
                half4 color=UniversalFragmentPBR(data,surface);color.rgb=MixFog(color.rgb,i.fog);return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
        UsePass "Universal Render Pipeline/Lit/DepthNormals"
    }
}
