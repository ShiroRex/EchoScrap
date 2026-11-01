Shader "EchoScrap/WastelandGround"
{
    Properties { _BaseColor("Tint",Color)=(1,1,1,1) _BaseMap("Base",2D)="white" {} _Cutoff("Cutoff",Float)=0.5 _Cull("Cull",Float)=2 _ReliefStrength("Micro relief",Range(0,1))=0.35 _Smoothness("Dry ground smoothness",Range(0,1))=0.07 _RoughnessVariation("Roughness variation",Range(0,0.2))=0.04 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float _ReliefStrength, _Smoothness, _RoughnessVariation, _Cutoff, _Cull;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; float3 normalWS:TEXCOORD1; half fog:TEXCOORD2; };
            Varyings vert(Attributes v)
            {
                Varyings o; o.positionWS=TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS=TransformWorldToHClip(o.positionWS); o.normalWS=TransformObjectToWorldNormal(v.normalOS);
                o.fog=ComputeFogFactor(o.positionCS.z); return o;
            }
            float hash(float2 p) { return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453); }
            float noise(float2 p)
            {
                float2 i=floor(p),f=frac(p); f=f*f*(3-2*f);
                return lerp(lerp(hash(i),hash(i+float2(1,0)),f.x),lerp(hash(i+float2(0,1)),hash(i+1),f.x),f.y);
            }
            void GroundPattern(Varyings i, out half3 dirt, out float height, out half smoothness)
            {
                float2 p=i.positionWS.xz;
                float broad=noise(p*0.19)*0.65+noise(p*0.73)*0.35;
                float grit=noise(p*43);
                float2 grid=p*1.7,cell=floor(grid),f=frac(grid);
                float first=10,second=10;
                [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
                {
                    float2 q=float2(x,y); float2 offset=q+float2(hash(cell+q),hash(cell+q+71.3))-f;
                    float d=dot(offset,offset); if(d<first){second=first;first=d;}else second=min(second,d);
                }
                float crack=1-smoothstep(0.008,0.035,second-first);
                float pebble=pow(saturate(noise(p*7.2)),9)*3;
                float rock=smoothstep(0.08,0.35,1-normalize(i.normalWS).y);
                dirt=lerp(half3(0.30,0.20,0.11),half3(0.56,0.41,0.24),broad);
                dirt*=lerp(0.90,1.09,grit); dirt*=1-crack*0.20;
                dirt=lerp(dirt,half3(0.37,0.34,0.28),saturate(pebble+rock*0.65));
                height=broad*0.025+grit*0.001-crack*0.004+pebble*0.009;
                smoothness=saturate(_Smoothness+(broad-0.5)*_RoughnessVariation-crack*0.02+saturate(pebble)*0.02);
            }
            half3 GroundNormal(Varyings i, float height)
            {
                float3 normal=normalize(i.normalWS);
                float3 dx=ddx(i.positionWS), dy=ddy(i.positionWS);
                float3 tangentX=cross(dy,normal), tangentY=cross(normal,dx);
                float determinant=dot(dx,tangentX);
                float3 gradient=(ddx(height)*tangentX+ddy(height)*tangentY)
                    *sign(determinant)/max(abs(determinant),0.00001);
                return normalize(normal-gradient*_ReliefStrength);
            }
            half4 frag(Varyings i):SV_Target
            {
                half3 dirt; float height; half smoothness;
                GroundPattern(i,dirt,height,smoothness);
                InputData data=(InputData)0;
                data.positionWS=i.positionWS; data.positionCS=i.positionCS; data.normalWS=GroundNormal(i,height);
                data.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);
                data.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
                data.bakedGI=SampleSH(data.normalWS); data.vertexLighting=VertexLighting(i.positionWS,data.normalWS);
                data.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);
                data.shadowMask=half4(1,1,1,1);
                SurfaceData surface=(SurfaceData)0;
                surface.albedo=dirt*_BaseColor.rgb; surface.alpha=1; surface.occlusion=1;
                surface.smoothness=smoothness; surface.specular=half3(0.1,0.1,0.1); surface.normalTS=half3(0,0,1);
                half4 color=UniversalFragmentPBR(data,surface); color.rgb=MixFog(color.rgb,i.fog); return color;
            }
            half4 GroundNormalsFrag(Varyings i):SV_Target
            {
                half3 dirt; float height; half smoothness;
                GroundPattern(i,dirt,height,smoothness);
                float3 normalWS=GroundNormal(i,height);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS=PackNormalOctQuadEncode(normalWS);
                return half4(PackFloat2To888(saturate(octNormalWS*0.5+0.5)),0);
            #else
                return half4(normalWS,0);
            #endif
            }
            struct DepthVaryings { float4 positionCS:SV_POSITION; };
            DepthVaryings DepthVert(Attributes input)
            {
                DepthVaryings o; o.positionCS=TransformObjectToHClip(input.positionOS.xyz); return o;
            }
            half4 DepthFrag(DepthVaryings i):SV_Target { return 0; }
            float3 _LightDirection, _LightPosition;
            DepthVaryings ShadowVert(Attributes input)
            {
                DepthVaryings o;
                float3 world=TransformObjectToWorld(input.positionOS.xyz);
                float3 normal=TransformObjectToWorldNormal(input.normalOS);
            #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection=normalize(_LightPosition-world);
            #else
                float3 lightDirection=_LightDirection;
            #endif
                o.positionCS=ApplyShadowClamping(TransformWorldToHClip(ApplyShadowBias(world,normal,lightDirection)));
                return o;
            }
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Cull [_Cull]
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            Cull [_Cull] ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            Cull [_Cull] ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode"="DepthNormalsOnly" }
            Cull [_Cull] ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment GroundNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
}
