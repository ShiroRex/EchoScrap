Shader "EchoScrap/Flashlight Rescue Lens"
{
    Properties
    {
        _CoreColor ("Núcleo amarillo Apollo", Color) = (0.90980392, 0.75686275, 0.43921569, 1)
        _EdgeColor ("Periferia naranja Apollo", Color) = (0.85490196, 0.52549020, 0.24313725, 1)
        _ContrastColor ("Respaldo del emisor Apollo", Color) = (0.14117647, 0.08235294, 0.15294118, 1)
        _Geometry ("Alcance / semiángulo / suavizado / borde suave", Vector) = (7, 0.5236, 0.08, 0.06)
        _BeamShape ("Caída radial / mínimo lateral", Vector) = (0.35, 0.45, 0, 0)
        _Appearance ("Opacidad / borde suave / emisión / brillo marcas", Vector) = (0.30, 0.10, 0, 1)
        _EmitterState ("Pulsos / emisión / contacto / piloto", Vector) = (0, 0, 0, 0.18)
        _EmitterGeometry ("Semitamaño / separación / activo / tenue", Vector) = (0.06, 0.20, 0.95, 0.35)
        [Toggle] _EmitterOnly ("Emisor", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20" }
        Pass
        {
            Name "RescueLens"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _EdgeColor;
                half4 _ContrastColor;
                float4 _Geometry;
                float4 _BeamShape;
                float4 _Appearance;
                float4 _EmitterState;
                float4 _EmitterGeometry;
                float _EmitterOnly;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 Emitter(float2 position2D)
            {
                float diamond = abs(position2D.x) + abs(position2D.y + 0.19);
                float lens = 1.0 - smoothstep(0.075, 0.09, diamond);
                float backing = 1.0 - smoothstep(0.105, 0.12, diamond);
                float marks = 0.0;
                float faintMarks = 0.0;
                float markBacking = 0.0;
                float halfSize = clamp(_EmitterGeometry.x, 0.01, 0.10);
                float spacing = clamp(_EmitterGeometry.y, halfSize * 2.5, 0.25);

                [unroll] for (int i = 0; i < 4; i++)
                {
                    float2 offset = position2D - float2((i - 1.5) * spacing, 0.03);
                    float dist = max(abs(offset.x), abs(offset.y));
                    float antialias = max(0.001, fwidth(dist));
                    float mark = 1.0 - smoothstep(halfSize - antialias, halfSize + antialias, dist);
                    markBacking = max(markBacking, 1.0 - smoothstep(halfSize + 0.012, halfSize + 0.020, dist));
                    faintMarks += mark * saturate(_EmitterGeometry.w);
                    marks += mark * step(i + 0.5, _EmitterState.x);
                }

                float contactShape = 1.0 - smoothstep(0.044, 0.058, abs(position2D.x) + abs(position2D.y - 0.20));
                float contact = contactShape * saturate(_EmitterState.z);
                float finalAccent = saturate(_Appearance.z);
                float lensLight = lens * saturate(_EmitterState.w + _EmitterState.y * 0.6 + finalAccent);
                lensLight = max(lensLight, backing * finalAccent * 0.65);
                float light = max(lensLight, marks * saturate(_EmitterGeometry.z) * saturate(_Appearance.w));
                light = max(light, max(faintMarks, contact * 0.9));
                float supportAlpha = max(backing * 0.3, markBacking * 0.8);
                float alpha = light + supportAlpha * (1.0 - light);
                half3 color = (_CoreColor.rgb * light + _ContrastColor.rgb * supportAlpha * (1.0 - light))
                    / max(0.0001, alpha);
                return half4(color, saturate(alpha));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                if (_EmitterOnly > 0.5)
                {
                    return Emitter(input.positionOS.xy);
                }

                float range = max(0.001, _Geometry.x);
                float halfAngle = max(0.001, _Geometry.y);
                float2 position2D = input.positionOS.xz;
                float radius = length(position2D);
                float angle = atan2(abs(position2D.x), max(0.00001, position2D.y));
                float sideDistance = radius * sin(halfAngle - angle);
                float arcDistance = range - radius;
                float distanceToEdge = min(sideDistance, arcDistance);
                float mask = smoothstep(0.0, range * _Geometry.z, arcDistance) *
                    smoothstep(0.0, max(0.001, radius * sin(halfAngle) * _Geometry.z), sideDistance);
                float axial = 1.0 - saturate(angle / halfAngle);
                float radial = 1.0 - saturate(radius / range);
                float falloff = pow(radial, max(0.05, _BeamShape.x)) * lerp(saturate(_BeamShape.y), 1.0, axial * axial);
                float fillAlpha = mask * falloff * _Appearance.x;

                float width = min(_Geometry.w, range * sin(halfAngle) * 0.08);
                float softWidth = max(0.001, width * 4.0 + range * _Geometry.z);
                float edgeWeight = 1.0 - smoothstep(0.0, softWidth, distanceToEdge);
                float edgeBlend = edgeWeight * saturate(_Appearance.y);
                half3 fillColor = lerp(_EdgeColor.rgb, _CoreColor.rgb, saturate(axial * radial));
                half3 color = lerp(fillColor, _EdgeColor.rgb, edgeBlend);
                return half4(color, saturate(fillAlpha));
            }
            ENDHLSL
        }
    }
}
