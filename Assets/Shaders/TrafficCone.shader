// Cône de chantier : corps noir (avec nuances), contour orange, deux bandes orange fluo émissives.
// La hauteur sur le cône (0 = base, 1 = pointe) vient de _HeightAxis / _HeightRange, renseignés par le script ConeLook
// à partir des bounds du mesh : ça marche quel que soit l'axe ou l'échelle d'export du modèle.
Shader "Racer/TrafficCone"
{
    Properties
    {
        [Header(Corps)]
        _BodyColor ("Couleur du corps", Color) = (0.03, 0.03, 0.03, 1)
        _BodyShadeColor ("Couleur du corps (ombre)", Color) = (0.0, 0.0, 0.0, 1)
        _RimStrength ("Liseré de lumière", Range(0, 1)) = 0.2

        [Header(Bandes)]
        [HDR] _BandColor ("Couleur des bandes (orange fluo)", Color) = (4, 1.2, 0, 1)
        _Band1Center ("Bande basse : hauteur", Range(0, 1)) = 0.38
        _Band1Width ("Bande basse : épaisseur", Range(0, 0.5)) = 0.13
        _Band2Center ("Bande haute : hauteur", Range(0, 1)) = 0.66
        _Band2Width ("Bande haute : épaisseur", Range(0, 0.5)) = 0.1

        [Header(Contour)]
        [HDR] _OutlineColor ("Couleur du contour", Color) = (1.5, 0.45, 0, 1)
        _OutlineWidth ("Épaisseur du contour (m)", Range(0, 0.1)) = 0.025

        [Header(Forme remplie par ConeLook)]
        _HeightAxis ("Axe de hauteur (espace objet)", Vector) = (0, 1, 0, 0)
        _HeightRange ("Hauteur min / max (espace objet)", Vector) = (0, 1, 0, 0)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BodyColor, _BodyShadeColor;
            half _RimStrength;
            half4 _BandColor;
            float _Band1Center, _Band1Width, _Band2Center, _Band2Width;
            half4 _OutlineColor;
            float _OutlineWidth;
            float4 _HeightAxis, _HeightRange;
        CBUFFER_END

        // Hauteur normalisée du point sur le cône (0 = base, 1 = pointe)
        float ConeHeight(float3 positionOS)
        {
            float h = dot(positionOS, normalize(_HeightAxis.xyz));
            return saturate((h - _HeightRange.x) / max(_HeightRange.y - _HeightRange.x, 1e-5));
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
            #pragma multi_compile _ _SHADOWS_SOFT
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
                float height : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS);
                output.positionCS = pos.positionCS;
                output.positionWS = pos.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.height = ConeHeight(input.positionOS);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            float Band(float h, float center, float width, float aa)
            {
                return 1.0 - smoothstep(width * 0.5 - aa, width * 0.5 + aa, abs(h - center));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half light = saturate(dot(n, mainLight.direction) * 0.5 + 0.5) * lerp(0.35, 1.0, mainLight.shadowAttenuation);
                light = floor(light * 3.0 + 0.5) / 3.0; // 3 nuances, comme le reste de la DA
                half3 color = lerp(_BodyShadeColor.rgb, _BodyColor.rgb, light);
                color += pow(1.0 - saturate(dot(n, v)), 4.0) * _RimStrength * light;

                // Bandes fluo : émissives, elles ne dépendent pas de l'éclairage (le Bloom les fait briller)
                float aa = max(fwidth(input.height), 1e-4);
                float bands = max(Band(input.height, _Band1Center, _Band1Width, aa),
                                  Band(input.height, _Band2Center, _Band2Width, aa));
                color = lerp(color, _BandColor.rgb, bands);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // Contour orange : coque gonflée vers l'extérieur, dessinée par l'arrière.
        // On gonfle depuis l'axe du cône (et pas le long des normales) : les faces plates du low poly ne se séparent pas.
        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct OutlineAttributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct OutlineVaryings
            {
                float4 positionCS : SV_POSITION;
                half fogFactor : TEXCOORD0;
            };

            OutlineVaryings OutlineVert(OutlineAttributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                OutlineVaryings output;
                float3 axis = normalize(_HeightAxis.xyz);
                float h = dot(input.positionOS, axis);
                float3 centerOS = axis * (0.5 * (_HeightRange.x + _HeightRange.y));
                // Direction de gonflage : de l'axe vers le point, plus un peu vers le haut et le bas aux extrémités
                float3 radialOS = input.positionOS - axis * h;
                float3 dirOS = input.positionOS - centerOS;
                float3 positionWS = TransformObjectToWorld(input.positionOS);
                float3 radialWS = TransformObjectToWorldDir(radialOS + dirOS * 0.25);
                positionWS += radialWS * _OutlineWidth;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 OutlineFrag(OutlineVaryings input) : SV_Target
            {
                return half4(MixFog(_OutlineColor.rgb, input.fogFactor), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex RacerShadowVert
            #pragma fragment RacerShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "RacerPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex RacerDepthVert
            #pragma fragment RacerDepthFrag
            #pragma multi_compile_instancing
            #include "RacerPasses.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex RacerDepthVert
            #pragma fragment RacerNormalsFrag
            #pragma multi_compile_instancing
            #define RACER_NORMALS_MASK 0.5
            #include "RacerPasses.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
