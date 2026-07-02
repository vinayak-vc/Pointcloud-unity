// PointForge point cloud shader (URP).
// Vertices are pulled from a StructuredBuffer written by the native streaming
// engine (20-byte stride: float3 position, packed rgba, packed
// intensity/classification). Each point is expanded to a screen-aligned quad
// (two triangles, 6 vertices) in the vertex stage — no Mesh objects involved.
// Opaque, depth-writing, depth-tested: no transparency artifacts by design.
Shader "PointForge/Points" {
    Properties {
        _PointSize ("Point Size (pixels)", Range(1, 32)) = 3
    }
    SubShader {
        Tags {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        Pass {
            Name "PointForgeForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Mirror of pf::GpuVertex (20 bytes). packedRgba little-endian:
            // r = byte0, g = byte1, b = byte2, a = byte3.
            // packedExtra: intensity = low 16 bits, classification = bits 16-23.
            struct PointForgeVertex {
                float3 positionPF;
                uint packedRgba;
                uint packedExtra;
            };

            StructuredBuffer<PointForgeVertex> _Points;
            float4x4 _PointForgeLocalToWorld;
            float _PointSize;
            // 0 = RGB, 1 = elevation, 2 = intensity, 3 = classification, 4 = LOD level
            float _ColorMode;
            float _NodeLevel;          // set per node from the octree hierarchy
            float2 _ZBoundsPF;         // (minZ, sizeZ) of the cloud in centred PF space

            half3 Turbo(float t) {
                // Compact turbo-like gradient: blue -> cyan -> green -> yellow -> red.
                t = saturate(t);
                return saturate(half3(
                    1.61 * t - 0.4,
                    sin(t * 3.14159),
                    1.0 - 1.61 * t));
            }

            half3 ClassColor(uint c) {
                // Hash a classification code into a stable distinct hue.
                float h = frac((float)c * 0.61803398875);
                float3 k = abs(frac(h + float3(0.0, 1.0 / 3.0, 2.0 / 3.0)) * 6.0 - 3.0) - 1.0;
                return half3(saturate(k));
            }

            struct Varyings {
                float4 positionCS : SV_POSITION;
                half3 color : COLOR0;
            };

            static const float2 kCorners[6] = {
                float2(-1, -1), float2(1, -1), float2(1, 1),
                float2(-1, -1), float2(1, 1), float2(-1, 1)
            };

            Varyings Vert(uint vertexId : SV_VertexID) {
                uint pointIndex = vertexId / 6u;
                uint corner = vertexId - pointIndex * 6u;
                PointForgeVertex v = _Points[pointIndex];

                float4 positionWS = mul(_PointForgeLocalToWorld, float4(v.positionPF, 1.0));
                float4 positionCS = mul(UNITY_MATRIX_VP, positionWS);

                // Expand to a fixed-pixel-size square in clip space.
                float2 offsetNDC = kCorners[corner] * _PointSize / _ScreenParams.xy;
                positionCS.xy += offsetNDC * positionCS.w;

                Varyings output;
                output.positionCS = positionCS;

                uint rgba = v.packedRgba;
                half3 rgb = half3(
                    (rgba & 0xFFu) / 255.0,
                    ((rgba >> 8u) & 0xFFu) / 255.0,
                    ((rgba >> 16u) & 0xFFu) / 255.0);

                uint mode = (uint)_ColorMode;
                if (mode == 1u) {
                    float t = (_ZBoundsPF.y > 0.0) ? (v.positionPF.z - _ZBoundsPF.x) / _ZBoundsPF.y : 0.0;
                    rgb = Turbo(t);
                } else if (mode == 2u) {
                    float intensity = (v.packedExtra & 0xFFFFu) / 65535.0;
                    rgb = half3(intensity, intensity, intensity);
                } else if (mode == 3u) {
                    rgb = ClassColor((v.packedExtra >> 16u) & 0xFFu);
                } else if (mode == 4u) {
                    rgb = Turbo(_NodeLevel / 12.0);
                }
                output.color = rgb;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target {
                return half4(input.color, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
