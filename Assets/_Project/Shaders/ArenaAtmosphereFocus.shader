Shader "Hidden/EchoScrap/ArenaAtmosphereFocus"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            Name "Protected peripheral focus"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Focus
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D_X(_EchoArenaFocusMask);
            TEXTURE2D_X(_EchoArenaSharpTexture);
            float4 _EchoArenaTactical;
            float4 _EchoArenaFocusParams;
            float _EchoArenaFocusSharpDepth;
            float _EchoArenaDecorativeIntensity;

            float ActorMask(float2 uv, float2 texel)
            {
                float mask = SAMPLE_TEXTURE2D_X_LOD(_EchoArenaFocusMask, sampler_PointClamp, uv, 0).r;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                    mask = max(mask, SAMPLE_TEXTURE2D_X_LOD(_EchoArenaFocusMask, sampler_PointClamp,
                        uv + float2(x, y) * texel * max(3.5, 3.5 * GetScaledScreenParams().y / 1080.0), 0).r);
                return mask;
            }

            half4 Focus(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X_LOD(_EchoArenaSharpTexture, sampler_PointClamp, uv, 0);
                float2 texel = rcp(GetScaledScreenParams().xy);
                float rawDepth = SampleSceneDepth(uv);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float playerDepth = -TransformWorldToView(_EchoArenaTactical.xyz).z;
                // A depth slab, rather than a circular clear patch following the player.
                // The nearer plane falls out of focus faster, like a miniature lens.
                float separation = eyeDepth - playerDepth;
                float defocus = smoothstep(_EchoArenaFocusSharpDepth,
                    _EchoArenaFocusSharpDepth + max(1, _EchoArenaFocusParams.y),
                    abs(separation) * (separation < 0 ? 1.25 : 1));
                float baseRadius = min(20, max(0, _EchoArenaFocusParams.x)) * GetScaledScreenParams().y / 1080.0
                    * defocus * _EchoArenaDecorativeIntensity;
                if (baseRadius < .15 || _EchoArenaFocusParams.w < .5) return original;
                float protection = ActorMask(uv, texel);
                float radius = baseRadius * (1 - protection);
                if (radius < .15) return original;
                float3 color = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, uv, 0).rgb;
                float weight = 1;
                // Fixed disk distribution: stable in motion, with no animated sampling grain.
                [unroll] for (int sampleIndex = 0; sampleIndex < 24; sampleIndex++)
                {
                    float angle = sampleIndex * 2.39996323;
                    float2 offset = float2(cos(angle), sin(angle)) * sqrt((sampleIndex + .5) / 24.0);
                    float2 sampleUv = uv + offset * texel * radius;
                    float sampleDepth = LinearEyeDepth(SampleSceneDepth(sampleUv), _ZBufferParams);
                    // Reject in-focus foreground, but allow defocused scenery silhouettes
                    // and bright practical lamps to spread into photographic bokeh.
                    float sampleSeparation = abs(sampleDepth - playerDepth);
                    float sampleDefocus = smoothstep(_EchoArenaFocusSharpDepth, _EchoArenaFocusSharpDepth + 2, sampleSeparation);
                    float sameSurface = lerp(exp(-abs(sampleDepth - eyeDepth) * max(.01, _EchoArenaFocusParams.z)), 1, sampleDefocus * defocus);
                    float protectedNeighbor = SAMPLE_TEXTURE2D_X_LOD(_EchoArenaFocusMask, sampler_PointClamp, sampleUv, 0).r;
                    float3 tap = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, sampleUv, 0).rgb;
                    float highlight = 1 + min(1.5, max(0, dot(tap, float3(.2126,.7152,.0722)) - .7)) * defocus;
                    float tapWeight = sameSurface * (1 - protectedNeighbor) * highlight;
                    color += tap * tapWeight;
                    weight += tapWeight;
                }
                return half4(lerp(original.rgb, color / max(.001, weight), saturate(radius * .5)), original.a);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Bokeh prefilter"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Prefilter
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            half4 Prefilter(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 texel=rcp(GetScaledScreenParams().xy);
                // Four bilinear footprints cover a 4x4 source block. Tiny luminous
                // lamps cannot turn into a constellation of separate sampling taps.
                return .25 * (SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,input.texcoord+texel*float2(-1,-1),0)
                    + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,input.texcoord+texel*float2(1,-1),0)
                    + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,input.texcoord+texel*float2(-1,1),0)
                    + SAMPLE_TEXTURE2D_X_LOD(_BlitTexture,sampler_LinearClamp,input.texcoord+texel*float2(1,1),0));
            }
            ENDHLSL
        }
    }
    Fallback Off
}
