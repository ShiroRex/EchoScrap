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
        float4 _EchoArenaScattering;
        float4 _EchoArenaShaftPositions[4];
        float4 _EchoArenaShaftDirections[4];
        float4 _EchoArenaShaftColors[4];
        float4 _EchoArenaShaftLightIndices;
        int _EchoArenaShaftCount;
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

        half AdditionalShadow(int index, float3 position)
        {
            if (index < 0) return 1;
            #if USE_CLUSTER_LIGHT_LOOP
                return GetAdditionalLight((uint)index, position, half4(1,1,1,1)).shadowAttenuation;
            #else
                Light light = GetAdditionalPerObjectLight(index, position);
                return AdditionalLightRealtimeShadow(index, position, light.direction);
            #endif
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

        // Clip the view ray to the positive, finite spotlight cone. Sampling this
        // short interval avoids the separated discs produced by a camera-wide march.
        bool ConeInterval(float3 ray, float3 origin, float3 axis, float cosine, float lengthLimit,
            float maximumDistance, out float enter, out float leave)
        {
            float3 p = _WorldSpaceCameraPos - origin;
            float z = dot(p, axis), dz = dot(ray, axis);
            enter = 0; leave = maximumDistance;
            if (abs(dz) < .00001)
            {
                if (z <= 0 || z >= lengthLimit) return false;
            }
            else
            {
                float cap0 = -z / dz, cap1 = (lengthLimit - z) / dz;
                enter = max(enter, min(cap0, cap1)); leave = min(leave, max(cap0, cap1));
            }
            if (leave <= enter) return false;
            float c2 = cosine * cosine;
            float a = c2 - dz * dz, b = 2 * (c2 * dot(p, ray) - z * dz), c = c2 * dot(p, p) - z * z;
            if (abs(a) < .00001)
            {
                if (abs(b) < .00001) { if (c > 0) return false; }
                else if (b > 0) leave = min(leave, -c / b);
                else enter = max(enter, -c / b);
            }
            else
            {
                float discriminant = b * b - 4 * a * c;
                if (discriminant < 0) { if (a > 0) return false; }
                else
                {
                    float root = sqrt(max(0, discriminant));
                    float r0 = (-b - root) / (2 * a), r1 = (-b + root) / (2 * a);
                    float low = min(r0, r1), high = max(r0, r1);
                    if (a > 0) { enter = max(enter, low); leave = min(leave, high); }
                    else if (min(leave, low) > enter + .00001) leave = min(leave, low);
                    else enter = max(enter, high);
                }
            }
            return leave > enter + .00001;
        }

        void IntegrateShafts(float3 ray, float maximumDistance, float2 pixel, inout float transmission, inout float3 scattering)
        {
            [unroll] for (int beam = 0; beam < 4; beam++)
            {
                if (beam >= _EchoArenaShaftCount) break;
                if (_EchoArenaShaftColors[beam].w < .01) continue;
                float enter, leave;
                if (!ConeInterval(ray, _EchoArenaShaftPositions[beam].xyz, _EchoArenaShaftDirections[beam].xyz,
                    _EchoArenaShaftDirections[beam].w, _EchoArenaShaftPositions[beam].w, maximumDistance, enter, leave)) continue;
                const int shaftSteps = 12;
                float segment = (leave - enter) / shaftSteps;
                float jitter = Hash31(float3(pixel, beam + 3));
                float beamTransmission = 1; float3 beamScattering = 0;
                [loop] for (int step = 0; step < shaftSteps; step++)
                {
                    float3 position = _WorldSpaceCameraPos + ray * (enter + (step + jitter) * segment);
                    float3 fromLight = position - _EchoArenaShaftPositions[beam].xyz;
                    float distanceToLight = max(.05, length(fromLight));
                    float cosine = dot(fromLight / distanceToLight, _EchoArenaShaftDirections[beam].xyz);
                    float cone = smoothstep(_EchoArenaShaftDirections[beam].w, min(.999, _EchoArenaShaftDirections[beam].w + .025), cosine);
                    float range = saturate(1 - distanceToLight / max(.1, _EchoArenaShaftPositions[beam].w));
                    float envelope = cone * smoothstep(.4, 1.4, distanceToLight) * smoothstep(0, .18, range);
                    if (envelope < .001) continue;
                    float3 moving = position + float3(_EchoArenaWind.x, 0, _EchoArenaWind.y) * _EchoArenaWind.z;
                    float grain = lerp(.65, 1, WorldNoise(moving * .48 + beam * 13));
                    float extinction = 1 - exp(-envelope * grain * _EchoArenaScattering.y * _EchoArenaDecorativeIntensity * segment);
                    float shadow = AdditionalShadow((int)_EchoArenaShaftLightIndices[beam], position);
                    float3 illumination = _EchoArenaAtmosphereColor.rgb * _EchoArenaScattering.x
                        + _EchoArenaShaftColors[beam].rgb * _EchoArenaShaftColors[beam].w * _EchoArenaScattering.z * range * range * shadow;
                    beamScattering += beamTransmission * extinction * illumination;
                    beamTransmission *= 1 - extinction;
                }
                scattering += transmission * beamScattering;
                transmission *= beamTransmission;
            }
        }

        void IntegrateLantern(float3 ray, float maximumDistance, inout float3 scattering)
        {
            if (_EchoArenaLanternLightIndex < 0 || _EchoArenaLanternColor.w <= 0) return;
            float enter, leave;
            if (!ConeInterval(ray, _EchoArenaLanternPosition.xyz, normalize(_EchoArenaLanternDirection.xyz),
                _EchoArenaLanternDirection.w, _EchoArenaLanternPosition.w, maximumDistance, enter, leave)) return;
            // Sample the short light interval independently of the distant atmosphere.
            // A camera-wide march could skip the whole beam as the camera moved.
            const int samples = 16;
            float segment = (leave - enter) / samples;
            [loop] for (int sampleIndex = 0; sampleIndex < samples; sampleIndex++)
            {
                float3 position = _WorldSpaceCameraPos + ray * (enter + (sampleIndex + .5) * segment);
                float3 fromLantern = position - _EchoArenaLanternPosition.xyz;
                float distanceToLight = max(.05, length(fromLantern));
                float cone = smoothstep(_EchoArenaLanternDirection.w, min(.999, _EchoArenaLanternDirection.w + .08),
                    dot(fromLantern / distanceToLight, normalize(_EchoArenaLanternDirection.xyz)));
                float attenuation = saturate(1 - distanceToLight / max(.1, _EchoArenaLanternPosition.w));
                float density = DensityAt(position);
                float shadow = AdditionalShadow(_EchoArenaLanternLightIndex, position);
                float transmission = exp(-density * (enter + (sampleIndex + .5) * segment));
                scattering += transmission * (1-exp(-density * segment)) * _EchoArenaLanternColor.rgb
                    * _EchoArenaLanternColor.w * cone * attenuation * attenuation * shadow * .075;
            }
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
                float3 illumination = _EchoArenaAtmosphereColor.rgb * _EchoArenaScattering.x
                    + sun.color * sun.shadowAttenuation * _EchoArenaAtmosphereColor.rgb * sunPhase;
                scattering += transmission * extinction * illumination;
                transmission *= 1 - extinction;
                if (transmission < .015) break;
            }
            IntegrateLantern(ray, lengthToIntegrate, scattering);
            IntegrateShafts(ray, lengthToIntegrate, input.positionCS.xy, transmission, scattering);
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
