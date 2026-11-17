Shader "EchoScrap/Character Contact Shadow"
{
    Properties { _BaseColor("Shadow",Color)=(0.005,0.006,0.009,0.85) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-20" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(half4, _BaseColor)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct A { float4 p:POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V Vert(A input)
            {
                V o=(V)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, o);
                o.p=TransformObjectToHClip(input.p.xyz);
                o.uv=input.uv;
                return o;
            }
            half4 Frag(V i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 baseColor=UNITY_ACCESS_INSTANCED_PROP(Props, _BaseColor);
                float2 p=i.uv*2-1;
                half a=1-smoothstep(0.12,1,dot(p,p));
                return half4(baseColor.rgb,baseColor.a*a);
            }
            ENDHLSL
        }
    }
}
