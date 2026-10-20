Shader "Coiriergraphics/WorldGrid"
{
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

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _LineColor;
            half4 _DarkColor;
            half _Metallic;
            half _Smoothness;
        CBUFFER_END

        // Fixed pattern settings (meters, after object scale compensation).
        #define TRIPLANAR_SHARPNESS 16.0
        #define DOT_SPACING 0.2
        #define DOT_DIAMETER 0.026
        #define DOT_DARKEN 0.30
        #define DOT_FADE_START 1.5
        #define DOT_FADE_END 5.0
        #define GRID_WIDTH 0.008
        #define GRID_DARKEN 0.45
        #define MAJOR_GRID_WIDTH 0.015
        #define MAJOR_GRID_OPACITY 0.20
        #define CROSS_ARM_LENGTH 0.0425
        #define CROSS_WIDTH 0.014
        #define MAJOR_CROSS_ARM_LENGTH 0.14
        #define MAJOR_CROSS_WIDTH 0.040
        #define CROSS_OPACITY 0.11875

        // Anti-alias the edge, but NEVER inflate tiny shapes by the full pixel
        // footprint. That was turning the crosses into huge white squares.
        float Stroke(float distanceToCenter, float halfWidth, float pixelWidth)
        {
            float edge = max(min(pixelWidth * 0.5, halfWidth * 0.5), 0.000001);
            return 1.0 - smoothstep(halfWidth - edge, halfWidth + edge, distanceToCenter);
        }

        // x = dots, y = 1m grid, z = 4m major grid, w = small crosses.
        // Larger 4m crosses are returned separately so they can force the full pattern color.
        float4 Pattern2D(float2 p, out float majorCrossMask)
        {
            float2 pixel = max(fwidth(p), float2(0.000001, 0.000001));
            float footprint = max(pixel.x, pixel.y);

            float2 dotIndex = floor(p / DOT_SPACING + 0.5);
            float2 dotLocal = p - dotIndex * DOT_SPACING;
            float dotMask = Stroke(length(dotLocal), DOT_DIAMETER * 0.5, length(pixel));
            // Entire dot rows and columns coinciding with a 1m grid line are absent.
            // This avoids visible circles straddling thin lines (even with AA).
            float2 distanceToGridIndex = abs((frac(dotIndex / 5.0 + 0.5) - 0.5) * 5.0);
            dotMask *= step(0.5, min(distanceToGridIndex.x, distanceToGridIndex.y));
            // Remove sub-pixel dots rather than allowing them to alias or expand.
            dotMask *= 1.0 - smoothstep(0.004, 0.014, footprint);

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

            // True plus signs: intersect two thin, finite line segments.
            float horizontal = Stroke(abs(cell.y), CROSS_WIDTH * 0.5, pixel.y)
                             * Stroke(abs(cell.x), CROSS_ARM_LENGTH, pixel.x);
            float vertical = Stroke(abs(cell.x), CROSS_WIDTH * 0.5, pixel.x)
                           * Stroke(abs(cell.y), CROSS_ARM_LENGTH, pixel.y);
            float crossMask = max(horizontal, vertical);
            // Suppress the ENTIRE cross at 1m intersections located on 4m lines.
            // Using the nearest integer grid intersection prevents partial crosses.
            float2 intersection = floor(p + 0.5);
            float2 intersection4 = abs((frac(intersection / 4.0 + 0.5) - 0.5) * 4.0);
            crossMask *= step(0.5, min(intersection4.x, intersection4.y));
            // Crosses are intentionally not drawn when they become sub-pixel.
            crossMask *= 1.0 - smoothstep(0.025, 0.09, footprint);

            // Larger crosses only at intersections of TWO 4m lines.
            float majorHorizontal = Stroke(majorDist.y, MAJOR_CROSS_WIDTH * 0.5, pixel.y)
                                  * Stroke(majorDist.x, MAJOR_CROSS_ARM_LENGTH, pixel.x);
            float majorVertical = Stroke(majorDist.x, MAJOR_CROSS_WIDTH * 0.5, pixel.x)
                                * Stroke(majorDist.y, MAJOR_CROSS_ARM_LENGTH, pixel.y);
            majorCrossMask = max(majorHorizontal, majorVertical);
            majorCrossMask *= 1.0 - smoothstep(0.025, 0.09, footprint);

            // Dots must not appear on ANY line work: 1m grid, 4m grid, 1m crosses, or 4m crosses.
            float occupiedByLines = saturate(max(max(gridMask, majorMask), max(crossMask, majorCrossMask)));
            dotMask *= 1.0 - occupiedByLines;

            return saturate(float4(dotMask, gridMask, majorMask, crossMask));
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
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 staticLightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float3 positionOS : TEXCOORD5;
                half3 normalOS : TEXCOORD6;
                half3 vertexLighting : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 4);
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
                output.positionOS = input.positionOS;
                output.normalOS = input.normalOS;
                output.normalWS = nrm.normalWS;
                output.vertexLighting = VertexLighting(pos.positionWS, nrm.normalWS);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);
                // Object-local projection, expressed in physical meters along each local axis.
                // Moving/rotating an object moves/rotates the grid. Scaling it preserves spacing.
                float3 patternPosition = input.positionOS * ObjectAxisScales();
                float3 localN = normalize((float3)input.normalOS);
                float3 weights = pow(abs(localN), TRIPLANAR_SHARPNESS);
                weights /= max(weights.x + weights.y + weights.z, 0.00001);

                float crossX, crossY, crossZ;
                float4 pattern = Pattern2D(patternPosition.yz, crossX) * weights.x
                               + Pattern2D(patternPosition.xz, crossY) * weights.y
                               + Pattern2D(patternPosition.xy, crossZ) * weights.z;
                float majorCross = saturate(crossX * weights.x + crossY * weights.y + crossZ * weights.z);

                // Tiny dark dots fade away with camera distance.
                float cameraDistance = distance(input.positionWS, _WorldSpaceCameraPos);
                float dotVisibility = 1.0 - smoothstep(DOT_FADE_START, DOT_FADE_END, cameraDistance);
                half3 albedo = _BaseColor.rgb;
                float darkening = saturate(pattern.x * DOT_DARKEN * dotVisibility
                                         + pattern.y * GRID_DARKEN);
                albedo = lerp(albedo, _DarkColor.rgb, darkening);
                // "Transparency" is expressed as a color blend factor toward Pattern Color.
                albedo = lerp(albedo, _LineColor.rgb,
                              saturate(pattern.z * MAJOR_GRID_OPACITY));
                albedo = lerp(albedo, _LineColor.rgb,
                              saturate(pattern.w * CROSS_OPACITY));
                // 4m crosses always use the full Pattern Color.
                albedo = lerp(albedo, _LineColor.rgb, saturate(majorCross));

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.metallic = _Metallic;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.normalWS = n;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                lighting.fogCoord = input.fogFactor;
                lighting.vertexLighting = input.vertexLighting;
                lighting.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, n);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, lighting.fogCoord);
                return color;
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
