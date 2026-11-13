Shader "EchoScrap/UI/Lit Pixel Frame"
{
 Properties
 {
  [PerRendererData] _MainTex("Frame",2D)="white"{}
  _NormalMap("OpenGL normal data",2D)="bump"{}
  _LightDirection("Card key light",Vector)=(-0.35,0.45,0.85,0)
  _SpecularColor("Reflected light",Color)=(1,1,1,1)
  _Emphasis("Rarity highlight",Float)=0.12
  _StencilComp("Stencil Comparison",Float)=8
  _Stencil("Stencil ID",Float)=0
  _StencilOp("Stencil Operation",Float)=0
  _StencilWriteMask("Stencil Write Mask",Float)=255
  _StencilReadMask("Stencil Read Mask",Float)=255
  _ColorMask("Color Mask",Float)=15
 }
 SubShader
 {
  Tags{"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True"}
  Stencil{Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
  Cull Off ZWrite Off ZTest [unity_GUIZTestMode]
  Blend SrcAlpha OneMinusSrcAlpha ColorMask [_ColorMask]
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
   #include "UnityCG.cginc"
   #include "UnityUI.cginc"
   struct appdata{float4 vertex:POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;};
   struct v2f{float4 vertex:SV_POSITION;fixed4 color:COLOR;float2 uv:TEXCOORD0;float4 local:TEXCOORD1;};
   sampler2D _MainTex,_NormalMap;
   float4 _LightDirection,_ClipRect;
   fixed4 _SpecularColor;
   float _Emphasis;
   v2f vert(appdata i){v2f o;o.local=i.vertex;o.vertex=UnityObjectToClipPos(i.vertex);o.color=i.color;o.uv=i.uv;return o;}
   fixed4 frag(v2f i):SV_Target
   {
    fixed4 base=tex2D(_MainTex,i.uv)*i.color;
    float3 normal=normalize(tex2D(_NormalMap,i.uv).xyz*2-1);
    float3 light=normalize(_LightDirection.xyz);
    float diffuse=0.62+0.46*saturate(dot(normal,light));
    float3 halfVector=normalize(light+float3(0,0,1));
    float specular=pow(saturate(dot(normal,halfVector)),24)*_Emphasis;
    float metal=saturate((max(base.r,max(base.g,base.b))-0.08)*10);
    base.rgb=saturate(base.rgb*lerp(1,diffuse,metal)+_SpecularColor.rgb*specular*metal*i.color.rgb);
    #ifdef UNITY_UI_CLIP_RECT
    base.a*=UnityGet2DClipping(i.local.xy,_ClipRect);
    #endif
    return base;
   }
   ENDCG
  }
 }
}
