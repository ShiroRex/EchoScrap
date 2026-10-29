Shader "EchoScrap/UI/MenuBackdrop"
{
    Properties
    {
        [PerRendererData] _MainTex("UI Texture", 2D) = "white" {}
        _AnimationTime("Presentation seconds", Float) = 0
        _Aspect("Viewport aspect", Float) = 1.777778
        _FlowSpeed("Flow speed", Range(0, .3)) = .095
        _Portal("Portal center and radius", Vector) = (.25, .52, .3, 0)
        _Foreground("Surface: background / rim / character", Float) = 0
        _SpriteExposure("Character brightness", Range(1, 2)) = 1.65
        _Ink("Deep blue", Color) = (.025, .044, .085, 1)
        _Teal("Echo teal", Color) = (.075, .32, .38, 1)
        _Violet("Violet", Color) = (.24, .15, .34, 1)
        _Copper("Copper", Color) = (.55, .27, .15, 1)
        _Gold("Warm seam", Color) = (.72, .51, .28, 1)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Name "EchoSingularity"
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            float _AnimationTime, _Aspect, _FlowSpeed, _Foreground, _SpriteExposure;
            float4 _Portal;
            sampler2D _MainTex;
            half4 _Ink, _Teal, _Violet, _Copper, _Gold;
            struct Attributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }
            float Hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                if (_Foreground > 1.5)
                {
                    half4 sprite = tex2D(_MainTex, input.uv) * input.color;
                    sprite.rgb *= _SpriteExposure;
                    return sprite;
                }
                float time = _AnimationTime * _FlowSpeed;
                float2 p = (input.uv - _Portal.xy) * float2(_Aspect, 1) / max(.01, _Portal.z);
                float radius = length(p);
                float angle = atan2(p.y, p.x);
                float turn = angle - time * .6 + .7 * sin(angle * 3 + radius * 5 - time * .4);
                float energy = pow(saturate(.5 + .5 * sin(turn * 4 - radius * 12 - time * 2.6)), 3);
                float ringDistance = (radius - .87) / .11;
                float filamentDistance = (radius - .88) / .23;
                float ring = exp(-ringDistance * ringDistance);
                float filaments = exp(-filamentDistance * filamentDistance) * energy;
                float halo = exp(-abs(radius - .9) * 5);
                half3 ringColor = lerp(_Violet.rgb * 1.7, _Teal.rgb * 1.8, .5 + .5 * sin(angle + .5));
                float warm = pow(saturate(.5 + .5 * sin(angle - .8)), 5);
                ringColor = lerp(ringColor, _Copper.rgb * 1.9, warm * .8);
                half3 light = ringColor * (ring * (.6 + energy * .9) + filaments * .8 + halo * .09);
                float wire = 1 - smoothstep(.005, .014 + fwidth(radius), abs(radius - .647));
                light += lerp(_Teal.rgb, _Gold.rgb, warm) * wire * 1.25;
                float horizon = smoothstep(.60, .65, radius);
                half3 eye = half3(.002, .003, .009);
                if (_Foreground > .5)
                {
                    // Drawn after the sprites: the actual aperture and energy hide their crossing.
                    clip(1.12 - radius);
                    float cover = 1 - smoothstep(.92, 1.12, radius);
                    half3 color = lerp(eye, light, horizon);
                    return half4(saturate(color) * input.color.rgb, cover * input.color.a);
                }
                float2 space = (input.uv - .5) * float2(_Aspect, 1);
                float cloud = .5 + .5 * sin(space.x * 4 + sin(space.y * 5 + time * .2) * 1.2);
                half3 nebula = lerp(_Ink.rgb * .35, _Violet.rgb * .18, cloud);
                float2 starCell = floor(space * 95);
                float2 starLocal = frac(space * 95) - .5;
                float star = step(.993, Hash(starCell)) * exp(-dot(starLocal, starLocal) * 160);
                nebula += half3(.45, .65, .8) * star * .32;
                nebula += ringColor * halo * .11;
                half3 background = lerp(eye, nebula + light, horizon);
                float grain = Hash(floor(input.position.xy)) - .5;
                background = saturate(background + grain * .0015 * horizon);
                return half4(background * input.color.rgb, input.color.a);
            }
            ENDCG
        }
    }
}
