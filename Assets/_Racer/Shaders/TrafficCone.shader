Shader "Racer/TrafficCone"
{
    // Cône de chantier : corps orange, bandes blanches "réfléchissantes", socle sombre.
    // Les bandes sont calculées à partir de la hauteur dans l'espace de l'objet (le mesh n'a pas d'UV utilisables) :
    // elles suivent le cône quand il vole et tourne.
    Properties
    {
        [MainColor] _BaseColor ("Orange", Color) = (1, 0.36, 0.02, 1)
        _BandColor ("Bandes", Color) = (1, 1, 1, 1)
        _FootColor ("Socle", Color) = (0.12, 0.12, 0.12, 1)
        _BandGlow ("Brillance des bandes", Range(0, 1)) = 0.2
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35

        [Header(Forme du cone en metres depuis le pivot du mesh)]
        _UpAxis ("Axe vertical du mesh (espace objet)", Vector) = (0, 0, 1, 0)
        _BodyBottom ("Haut du socle", Float) = -0.16
        _BodyTop ("Pointe", Float) = 0.66

        [Header(Bandes de 0 bas du corps a 1 pointe)]
        _Band1 ("Bande basse (debut, fin)", Vector) = (0.26, 0.40, 0, 0)
        _Band2 ("Bande haute (debut, fin)", Vector) = (0.52, 0.66, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _BandColor;
            half4 _FootColor;
            half _BandGlow;
            half _Smoothness;
            float4 _UpAxis;
            float _BodyBottom;
            float _BodyTop;
            float4 _Band1;
            float4 _Band2;
        CBUFFER_END

        // Hauteur en mètres le long de l'axe vertical du mesh. La position objet est remise à l'échelle :
        // le résultat ne dépend pas de l'échelle d'import du FBX (Blender : x100 sur le transform, /100 sur les sommets).
        float ConeHeight(float3 positionOS)
        {
            float3x3 m = (float3x3)GetObjectToWorldMatrix();
            float3 scales = float3(length(mul(m, float3(1, 0, 0))),
                                   length(mul(m, float3(0, 1, 0))),
                                   length(mul(m, float3(0, 0, 1))));
            return dot(positionOS * scales, normalize(_UpAxis.xyz));
        }

        // 1 dans la bande [a, b], 0 ailleurs, avec un bord anti-aliasé
        float Band(float t, float2 ab, float aa)
        {
            return smoothstep(ab.x - aa, ab.x + aa, t) * (1.0 - smoothstep(ab.y - aa, ab.y + aa, t));
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
            #pragma multi_compile _ _FORWARD_PLUS
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
                float height : TEXCOORD2;
                half3 vertexSH : TEXCOORD3;
                half fogFactor : TEXCOORD4;
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
                output.normalWS = nrm.normalWS;
                output.height = ConeHeight(input.positionOS);
                output.vertexSH = SampleSH(nrm.normalWS);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 n = normalize(input.normalWS);

                float t = (input.height - _BodyBottom) / max(_BodyTop - _BodyBottom, 0.0001);
                float aa = max(fwidth(t), 0.002);
                float band = saturate(Band(t, _Band1.xy, aa) + Band(t, _Band2.xy, aa));
                float foot = 1.0 - smoothstep(-aa, aa, t);

                half3 albedo = lerp(_BaseColor.rgb, _BandColor.rgb, band);
                albedo = lerp(albedo, _FootColor.rgb, foot);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = albedo;
                surface.alpha = 1;
                surface.metallic = 0;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.emission = _BandColor.rgb * (band * (1.0 - foot) * _BandGlow);

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.normalWS = n;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                lighting.fogCoord = input.fogFactor;
                lighting.bakedGI = input.vertexSH;
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, lighting.fogCoord);
                return color;
            }
            ENDHLSL
        }

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
            ColorMask R
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
            half4 DepthFrag(DepthVaryings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }

        // Profondeur + normales : utilisé par les effets écran (SSAO, contours basés sur les normales)
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing
            struct DNAttributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };
            DNVaryings DepthNormalsVert(DNAttributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                DNVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 DepthNormalsFrag(DNVaryings input) : SV_Target
            {
                return half4(NormalizeNormalPerPixel(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
