Shader "Coiriergraphics/WorldGrid"
{
    // Mobile version: same grid look as before, but much cheaper per pixel.
    // - One projection (the dominant axis) instead of 3 blended ones: with the old sharpness of 16
    //   the blend was almost always 100% one axis anyway.
    // - The tiny dots are gone: they faded out 5 m from the camera, which is always farther than that.
    // - Simple diffuse lighting (main light + ambient) instead of full PBR.
    Properties
    {
        [MainColor] _BaseColor ("ColorA", Color) = (0.105, 0.17, 0.22, 1)
        _LineColor ("ColorB", Color) = (1, 1, 1, 1)
        _DarkColor ("ColorC", Color) = (0.035, 0.055, 0.07, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // Same layout as before so existing materials and the SRP batcher keep working.
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _LineColor;
            half4 _DarkColor;
            half _Metallic;
            half _Smoothness;
        CBUFFER_END

        // Fixed pattern settings (meters, after object scale compensation).
        #define GRID_WIDTH 0.008
        #define GRID_DARKEN 0.45
        #define MAJOR_GRID_WIDTH 0.015
        #define MAJOR_GRID_OPACITY 0.20
        #define CROSS_ARM_LENGTH 0.0425
        #define CROSS_WIDTH 0.014
        #define MAJOR_CROSS_ARM_LENGTH 0.14
        #define MAJOR_CROSS_WIDTH 0.040
        #define CROSS_OPACITY 0.11875

        float Stroke(float distanceToCenter, float halfWidth, float pixelWidth)
        {
            float edge = max(min(pixelWidth * 0.5, halfWidth * 0.5), 0.000001);
            return 1.0 - smoothstep(halfWidth - edge, halfWidth + edge, distanceToCenter);
        }

        // x = 1m grid, y = 4m major grid, z = small crosses. Large 4m crosses returned separately.
        float3 Pattern2D(float2 p, out float majorCrossMask)
        {
            float2 pixel = max(fwidth(p), float2(0.000001, 0.000001));
            float footprint = max(pixel.x, pixel.y);

            float2 cell = frac(p + 0.5) - 0.5;
            float2 gridDist = abs(cell);
            float gridMask = max(
                Stroke(gridDist.x, GRID_WIDTH * 0.5, pixel.x),
                Stroke(gridDist.y, GRID_WIDTH * 0.5, pixel.y));
            gridMask *= 1.0 - smoothstep(0.025, 0.10, footprint);

            float2 majorDist = abs((frac(p / 4.0 + 0.5) - 0.5) * 4.0);
            float majorMask = max(
                Stroke(majorDist.x, MAJOR_GRID_WIDTH * 0.5, pixel.x),
                Stroke(majorDist.y, MAJOR_GRID_WIDTH * 0.5, pixel.y));

            float crossFade = 1.0 - smoothstep(0.025, 0.09, footprint);
            float crossMask = 0.0;
            majorCrossMask = 0.0;
            if (crossFade > 0.0) // far away (most of the screen): crosses are invisible, skip them
            {
                float horizontal = Stroke(abs(cell.y), CROSS_WIDTH * 0.5, pixel.y)
                                 * Stroke(abs(cell.x), CROSS_ARM_LENGTH, pixel.x);
                float vertical = Stroke(abs(cell.x), CROSS_WIDTH * 0.5, pixel.x)
                               * Stroke(abs(cell.y), CROSS_ARM_LENGTH, pixel.y);
                crossMask = max(horizontal, vertical);
                float2 intersection = floor(p + 0.5);
                float2 intersection4 = abs((frac(intersection / 4.0 + 0.5) - 0.5) * 4.0);
                crossMask *= step(0.5, min(intersection4.x, intersection4.y)) * crossFade;

                float majorHorizontal = Stroke(majorDist.y, MAJOR_CROSS_WIDTH * 0.5, pixel.y)
                                      * Stroke(majorDist.x, MAJOR_CROSS_ARM_LENGTH, pixel.x);
                float majorVertical = Stroke(majorDist.x, MAJOR_CROSS_WIDTH * 0.5, pixel.x)
                                    * Stroke(majorDist.y, MAJOR_CROSS_ARM_LENGTH, pixel.y);
                majorCrossMask = max(majorHorizontal, majorVertical) * crossFade;
            }

            return saturate(float3(gridMask, majorMask, crossMask));
        }

        float3 ObjectAxisScales()
        {
            float3x3 m = (float3x3)GetObjectToWorldMatrix();
            return float3(length(mul(m, float3(1, 0, 0))),
                          length(mul(m, float3(0, 1, 0))),
                          length(mul(m, float3(0, 0, 1))));
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 patternPos : TEXCOORD2;   // object-local meters
                half3 normalOS : TEXCOORD3;
                half3 ambient : TEXCOORD4;
                half fogFactor : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS);
                VertexNormalInputs nrm = GetVertexNormalInputs(input.normalOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                // Moving/rotating an object moves/rotates the grid. Scaling it preserves spacing.
                output.patternPos = input.positionOS * ObjectAxisScales();
                output.normalOS = input.normalOS;
                output.normalWS = nrm.normalWS;
                output.ambient = SampleSH(nrm.normalWS);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // Project the pattern along the dominant local axis
                half3 an = abs(input.normalOS);
                float3 p = input.patternPos;
                float2 uv = (an.y >= an.x && an.y >= an.z) ? p.xz : ((an.x >= an.z) ? p.yz : p.xy);

                float majorCross;
                float3 pattern = Pattern2D(uv, majorCross);

                half3 albedo = _BaseColor.rgb;
                albedo = lerp(albedo, _DarkColor.rgb, saturate(pattern.x * GRID_DARKEN));
                albedo = lerp(albedo, _LineColor.rgb, saturate(pattern.y * MAJOR_GRID_OPACITY));
                albedo = lerp(albedo, _LineColor.rgb, saturate(pattern.z * CROSS_OPACITY));
                albedo = lerp(albedo, _LineColor.rgb, saturate(majorCross));

                // Diffuse lighting: main light (+ its shadow when enabled) and ambient
                Light light = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half3 n = normalize(input.normalWS);
                half ndl = saturate(dot(n, light.direction));
                half3 color = albedo * (light.color * (ndl * light.shadowAttenuation) + input.ambient);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // Solid meshes cast shadows; the pattern only modifies their albedo.
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct ShadowVaryings { float4 positionCS : SV_POSITION; };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ShadowVaryings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirection = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }
            half4 ShadowFrag(ShadowVaryings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull Back
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            struct DepthAttributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct DepthVaryings { float4 positionCS : SV_POSITION; };
            DepthVaryings DepthVert(DepthAttributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                DepthVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                return output;
            }
            half4 DepthFrag(DepthVaryings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    FallBack Off
}
