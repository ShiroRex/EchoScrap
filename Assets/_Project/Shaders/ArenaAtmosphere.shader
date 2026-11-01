Shader "Hidden/EchoScrap/ArenaAtmosphere"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        float4 _EchoArenaAtmosphereColor;
        float4 _EchoArenaAtmosphereParams;
        float4 _EchoArenaMistParams;
        float4 _EchoArenaFarMistParams;
        float4 _EchoArenaWind;
        float4 _EchoArenaLanternPosition;
        float4 _EchoArenaLanternDirection;
        float4 _EchoArenaLanternColor;
        float4 _EchoArenaTactical;
        float4 _EchoArenaAirTexelSize;
        float4 _EchoArenaFocusParams;
        float4 _EchoArenaMistCenters[4];
        float4 _EchoArenaMistExtents[4];
        int _EchoArenaMistCount;
        int _EchoArenaRaymarchSteps;
        int _EchoArenaLanternLightIndex;
        float _EchoArenaDecorativeIntensity;
        TEXTURE2D_X(_EchoArenaAirTexture);

        float Hash31(float3 p)
        {
            p = frac(p * .1031);
            p += dot(p, p.yzx + 33.33);
            return frac((p.x + p.y) * p.z);
        }

        float WorldNoise(float3 p)
        {
            float3 i = floor(p), f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            float a = lerp(Hash31(i), Hash31(i + float3(1, 0, 0)), f.x);
            float b = lerp(Hash31(i + float3(0, 1, 0)), Hash31(i + float3(1, 1, 0)), f.x);
            float c = lerp(Hash31(i + float3(0, 0, 1)), Hash31(i + float3(1, 0, 1)), f.x);
            float d = lerp(Hash31(i + float3(0, 1, 1)), Hash31(i + float3(1, 1, 1)), f.x);
            return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
        }

        float HeightBand(float height, float bottom, float top)
        {
            float span = max(.1, top - bottom);
            return smoothstep(bottom, bottom + span * .25, height)
                * (1.0 - smoothstep(top - span * .45, top, height));
        }

        float DensityAt(float3 position)
        {
            float3 moving = position + float3(_EchoArenaWind.x, 0, _EchoArenaWind.y) * _EchoArenaWind.z;
            float height = max(0.0, position.y - _EchoArenaAtmosphereParams.w);
            float density = max(0, _EchoArenaAtmosphereParams.x) * exp(-height * max(.001, _EchoArenaAtmosphereParams.y));
            float bankDensity = 0;
            [unroll] for (int bank = 0; bank < 4; bank++)
            {
                if (bank >= _EchoArenaMistCount) break;
                float3 q = (position - _EchoArenaMistCenters[bank].xyz) / max(.1, _EchoArenaMistExtents[bank].xyz);
                float interior = saturate(1.0 - dot(q, q));
                bankDensity += interior * interior * max(0, _EchoArenaMistCenters[bank].w) * saturate(_EchoArenaMistExtents[bank].w);
            }
            if (bankDensity > .00001)
            {
                float broad = WorldNoise(moving * max(.002, _EchoArenaMistParams.w));
                float detail = WorldNoise(moving * max(.004, _EchoArenaMistParams.w * 2.7) + 17.0);
                // Noise erodes the ellipsoid inward: its boundary never reveals a flat box face.
                float erosion = smoothstep(.22, .76, broad * .72 + detail * .28);
                density += bankDensity * erosion * max(0, _EchoArenaMistParams.x);
            }
            float farWeight = smoothstep(_EchoArenaTactical.w + 6.0, _EchoArenaTactical.w + 35.0,
                distance(position.xz, _EchoArenaTactical.xz));
            float farDensity = max(0, _EchoArenaFarMistParams.x) * HeightBand(position.y, _EchoArenaFarMistParams.y,
                _EchoArenaFarMistParams.z) * farWeight;
            if (farDensity > .00001)
                density += farDensity * smoothstep(.25, .8, WorldNoise(moving * max(.002, _EchoArenaFarMistParams.w) + 43.0));
            return max(0, density) * _EchoArenaDecorativeIntensity;
        }

        float3 PositionAtDepth(float2 uv, float depth)
        {
            #if !UNITY_REVERSED_Z
                depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, depth);
            #endif
            return ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
        }

        half4 Integrate(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            float rawDepth = SampleSceneDepth(uv);
            float3 destination = PositionAtDepth(uv, rawDepth);
            float3 vectorToSurface = destination - _WorldSpaceCameraPos;
            float surfaceDistance = max(.001, length(vectorToSurface));
            float3 ray = vectorToSurface / surfaceDistance;
            float lengthToIntegrate = min(surfaceDistance, max(1, _EchoArenaAtmosphereParams.z));
            int count = clamp(_EchoArenaRaymarchSteps, 16, 48);
            float transmission = 1;
            float3 scattering = 0;
            [loop] for (int step = 0; step < count; step++)
            {
                // Smaller intervals near the ground preserve thin mist banks at half resolution.
                float u0 = (float)step / count, u1 = (float)(step + 1) / count;
                float begin = lengthToIntegrate * (1 - (1 - u0) * (1 - u0));
                float end = lengthToIntegrate * (1 - (1 - u1) * (1 - u1));
                float segment = end - begin;
                float3 position = _WorldSpaceCameraPos + ray * ((begin + end) * .5);
                float density = DensityAt(position);
                if (density < .00001) continue;
                float extinction = 1 - exp(-density * segment);
                Light sun = GetMainLight(TransformWorldToShadowCoord(position));
                float sunPhase = .22 + .18 * pow(saturate(dot(-ray, sun.direction)), 3);
                float3 illumination = _EchoArenaAtmosphereColor.rgb * .24
                    + sun.color * sun.shadowAttenuation * _EchoArenaAtmosphereColor.rgb * sunPhase;
                float3 fromLantern = position - _EchoArenaLanternPosition.xyz;
                float lanternDistance = max(.05, length(fromLantern));
                float inside = smoothstep(_EchoArenaLanternDirection.w, min(.999, _EchoArenaLanternDirection.w + .08),
                    dot(fromLantern / lanternDistance, normalize(_EchoArenaLanternDirection.xyz + .00001)));
                float attenuation = saturate(1 - lanternDistance / max(.1, _EchoArenaLanternPosition.w));
                if (_EchoArenaLanternLightIndex >= 0 && inside * attenuation > .001)
                {
                    half lanternShadow;
                    #if USE_CLUSTER_LIGHT_LOOP
                        Light lantern = GetAdditionalLight((uint)_EchoArenaLanternLightIndex, position, half4(1,1,1,1));
                        lanternShadow = lantern.shadowAttenuation;
                    #else
                        // Fullscreen passes have no per-object index list. Use the packed
                        // index explicitly if another renderer disables Forward+.
                        Light lantern = GetAdditionalPerObjectLight(_EchoArenaLanternLightIndex, position);
                        lanternShadow = AdditionalLightRealtimeShadow(_EchoArenaLanternLightIndex, position, lantern.direction);
                    #endif
                    illumination += _EchoArenaLanternColor.rgb * max(0, _EchoArenaLanternColor.w) * inside
                        * attenuation * attenuation * lanternShadow * .075;
                }
                scattering += transmission * extinction * illumination;
                transmission *= 1 - extinction;
                if (transmission < .015) break;
            }
            float opacity = 1 - transmission;
            float outside = smoothstep(_EchoArenaTactical.w, _EchoArenaTactical.w + max(2, _EchoArenaFocusParams.y),
                distance(destination.xz, _EchoArenaTactical.xz));
            float maxOpacity = lerp(.12, .44, outside);
            float limitedOpacity = min(opacity, maxOpacity);
            scattering *= limitedOpacity / max(.00001, opacity);
            return half4(scattering, limitedOpacity);
        }

        half4 Composite(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            float2 uv = input.texcoord;
            half4 original = SAMPLE_TEXTURE2D_X_LOD(_BlitTexture, sampler_PointClamp, uv, 0);
            float depth = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            float2 pixel = uv * _EchoArenaAirTexelSize.zw - .5;
            float2 origin = floor(pixel), fraction = frac(pixel);
            float4 air = 0, closestAir = 0;
            float totalWeight = 0, closestDifference = 1e20;
            [unroll] for (int y = 0; y < 2; y++)
            [unroll] for (int x = 0; x < 2; x++)
            {
                float2 sampleUv = (origin + float2(x, y) + .5) * _EchoArenaAirTexelSize.xy;
                float sampleDepth = LinearEyeDepth(SampleSceneDepth(sampleUv), _ZBufferParams);
                float difference = abs(sampleDepth - depth);
                float4 sampleAir = SAMPLE_TEXTURE2D_X_LOD(_EchoArenaAirTexture, sampler_PointClamp, sampleUv, 0);
                float2 spatial = lerp(1 - fraction, fraction, float2(x, y));
                float weight = spatial.x * spatial.y * exp(-difference * 2.0);
                air += sampleAir * weight;
                totalWeight += weight;
                if (difference < closestDifference) { closestDifference = difference; closestAir = sampleAir; }
            }
            air = totalWeight > .00001 ? air / totalWeight : closestAir;
            // Reapply protection at full resolution: neighbors beyond the tactical
            // boundary must not interpolate more than 12% fog onto protected pixels.
            float3 world = PositionAtDepth(uv, SampleSceneDepth(uv));
            float outside = smoothstep(_EchoArenaTactical.w, _EchoArenaTactical.w + max(2, _EchoArenaFocusParams.y),
                distance(world.xz, _EchoArenaTactical.xz));
            float limitedOpacity = min(air.a, lerp(.12, .44, outside));
            air.rgb *= limitedOpacity / max(.00001, air.a);
            air.a = limitedOpacity;
            return half4(original.rgb * (1 - saturate(air.a)) + air.rgb, original.a);
        }
        ENDHLSL
        Pass
        {
            Name "Integrate"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Integrate
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            ENDHLSL
        }
        Pass
        {
            Name "Composite"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Composite
            ENDHLSL
        }
    }
    Fallback Off
}
