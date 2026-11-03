Shader "EchoScrap/Warehouse Boundary"
{
    Properties
    {
        _BaseColor("Background", Color)=(0.008,0.009,0.014,1)
        _RoomBounds("Room XZ bounds", Vector)=(0,0,1,1)
        _FadeWidth("Fade width", Float)=8
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-30" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _RoomBounds;
                float _FadeWidth;
            CBUFFER_END
            struct V { float4 position:SV_POSITION; float3 world:TEXCOORD0; };
            V Vert(float4 position:POSITION)
            {
                V o; o.position=TransformObjectToHClip(position.xyz); o.world=TransformObjectToWorld(position.xyz); return o;
            }
            half4 Frag(V i):SV_Target
            {
                float2 outside=max(max(_RoomBounds.xy-i.world.xz,i.world.xz-_RoomBounds.zw),0);
                half alpha=smoothstep(0,max(.1,_FadeWidth),max(outside.x,outside.y));
                return half4(_BaseColor.rgb,alpha);
            }
            ENDHLSL
        }
    }
}
