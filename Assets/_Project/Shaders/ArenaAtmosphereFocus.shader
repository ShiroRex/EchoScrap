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
            float4 _EchoArenaTactical;
            float4 _EchoArenaFocusParams;
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
                half4 original = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
                float2 texel = rcp(GetScaledScreenParams().xy);
                float rawDepth = SampleSceneDepth(uv);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
                #endif
                float3 world = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float outside = smoothstep(_EchoArenaTactical.w, _EchoArenaTactical.w + max(3, _EchoArenaFocusParams.y),
                    distance(world.xz, _EchoArenaTactical.xz));
                float framing = max(smoothstep(.66, .98, uv.y), 1 - smoothstep(.02, .24, uv.y));
                float playerDepth = -TransformWorldToView(_EchoArenaTactical.xyz).z;
                float depthSeparation = smoothstep(7, 24, abs(eyeDepth - playerDepth));
                float baseRadius = min(3, max(0, _EchoArenaFocusParams.x)) * GetScaledScreenParams().y / 1080.0
                    * outside * framing * depthSeparation * _EchoArenaDecorativeIntensity;
                if (baseRadius < .15 || _EchoArenaFocusParams.w < .5) return original;
                float protection = ActorMask(uv, texel);
                float luminance = dot(original.rgb, float3(.2126, .7152, .0722));
                float highest = max(original.r, max(original.g, original.b));
                float lowest = min(original.r, min(original.g, original.b));
                float luminousThreat = max(smoothstep(.65, 1.25, luminance),
                    smoothstep(.3, .65, highest) * smoothstep(.12, .35, highest - lowest));
                protection = max(protection, luminousThreat);
                float radius = baseRadius * (1 - protection);
                if (radius < .15) return original;
                static const float2 offsets[8] =
                {
                    float2(1,0), float2(-1,0), float2(0,1), float2(0,-1),
                    float2(.707,.707), float2(-.707,.707), float2(.707,-.707), float2(-.707,-.707)
                };
                float3 color = original.rgb * 2;
                float weight = 2;
                [unroll] for (int sampleIndex = 0; sampleIndex < 8; sampleIndex++)
                {
                    float2 sampleUv = uv + offsets[sampleIndex] * texel * radius;
                    float sampleDepth = LinearEyeDepth(SampleSceneDepth(sampleUv), _ZBufferParams);
                    float sameSurface = exp(-abs(sampleDepth - eyeDepth) * max(.5, _EchoArenaFocusParams.z));
                    float protectedNeighbor = SAMPLE_TEXTURE2D_X_LOD(_EchoArenaFocusMask, sampler_PointClamp, sampleUv, 0).r;
                    float tapWeight = sameSurface * (1 - protectedNeighbor);
                    color += SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_LinearClamp, sampleUv, 0).rgb * tapWeight;
                    weight += tapWeight;
                }
                return half4(color / max(.001, weight), original.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
