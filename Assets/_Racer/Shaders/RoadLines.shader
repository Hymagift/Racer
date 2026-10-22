// Route : asphalte sombre éclairé + bandes blanches émissives (bords pleins + ligne centrale en pointillés).
// Les lignes sont calculées à partir des UV du mesh généré par ZigzagRoadGenerator :
//   u = 0 (bord gauche) -> 1 (bord droit), perpendiculairement à la route, même dans les diagonales ;
//   v = distance le long de la route / uvTileLength, continue d'un chunk à l'autre et malgré les rebases.
// Les lignes restent donc toujours au bon endroit, quelle que soit la forme générée.
//   uv2.x = largeur réelle de la route à cet endroit (m) : elle se rétrécit avec la difficulté, les lignes suivent.
// IMPORTANT : _UVTileLength doit être égal à uvTileLength du générateur.
Shader "Racer/RoadLines"
{
    Properties
    {
        [Header(Asphalte)]
        _BaseColor ("Couleur asphalte", Color) = (0.06, 0.06, 0.06, 1)
        _Grain ("Grain de l'asphalte", Range(0, 1)) = 0.25
        _GrainSize ("Taille du grain (m)", Range(0.02, 1)) = 0.12
        _AmbientStrength ("Ambiance", Range(0, 2)) = 1

        [Header(Lignes)]
        [HDR] _LineEmission ("Couleur émissive des lignes", Color) = (3, 3, 3, 1)
        _RoadWidth ("Largeur de route si le mesh n'en donne pas (m)", Float) = 9
        _UVTileLength ("Longueur d'UV (= uvTileLength)", Float) = 8
        _EdgeLineWidth ("Largeur lignes de bord (m)", Range(0, 1)) = 0.2
        _EdgeLineInset ("Retrait lignes de bord (m)", Range(0, 2)) = 0.3
        [ToggleUI] _CenterLine ("Ligne centrale", Float) = 1
        _CenterLineWidth ("Largeur ligne centrale (m)", Range(0, 1)) = 0.15
        [IntRange] _DashesPerTile ("Pointillés par longueur d'UV", Range(1, 8)) = 2
        _DashFill ("Part tracée du pointillé", Range(0.05, 1)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _Grain;
            float _GrainSize;
            half _AmbientStrength;
            half4 _LineEmission;
            float _RoadWidth, _UVTileLength;
            float _EdgeLineWidth, _EdgeLineInset;
            float _CenterLine, _CenterLineWidth;
            float _DashesPerTile, _DashFill;
        CBUFFER_END

        float4 _RacerDistanceFade; // global (InGameLook) : x = début, y = fin (m), z = force. 0 = pas de fondu.

        // Bande anti-aliasée : 1 à moins de halfWidth du centre, 0 au-delà
        float Band(float dist, float halfWidth, float aa)
        {
            return 1.0 - smoothstep(halfWidth - aa, halfWidth + aa, dist);
        }

        // Masque des lignes (0..1) à partir des UV de la route
        float RoadLineMask(float2 uv, float roadWidth)
        {
            float x = (uv.x - 0.5) * roadWidth;           // mètres depuis le centre, perpendiculairement
            float aaX = max(fwidth(x), 1e-4);

            // Lignes de bord pleines
            float edgeCenter = roadWidth * 0.5 - _EdgeLineInset - _EdgeLineWidth * 0.5;
            float edges = Band(abs(abs(x) - edgeCenter), _EdgeLineWidth * 0.5, aaX);

            // Ligne centrale en pointillés. Un nombre ENTIER de pointillés par longueur d'UV :
            // le motif reste continu quand le générateur fait un rebase (décalage d'un multiple de uvTileLength).
            float along = uv.y * round(_DashesPerTile);
            float aaV = max(fwidth(along), 1e-4);
            float f = frac(along);
            float dash = smoothstep(0.0, aaV, f) * (1.0 - smoothstep(_DashFill - aaV, _DashFill, f));
            if (_DashFill >= 0.999) dash = 1.0;
            float center = Band(abs(x), _CenterLineWidth * 0.5, aaX) * dash * _CenterLine;

            return saturate(max(edges, center));
        }

        // Largeur donnée par le générateur dans uv2.x (0 si le mesh n'a pas d'uv2 : on prend _RoadWidth)
        float RoadWidthFrom(float2 uv2)
        {
            return uv2.x > 0.01 ? uv2.x : _RoadWidth;
        }

        float Hash12(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
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
                float2 uv : TEXCOORD0;
                float2 uv2 : TEXCOORD1;
                float2 uv3 : TEXCOORD2; // x = centre de la route en X, y = demi-largeur en X (0 si absent)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                float roadWidth : TEXCOORD4;
                float3 across : TEXCOORD5; // x local, centre, demi-largeur : position en travers exacte
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
                output.uv = input.uv;
                output.roadWidth = RoadWidthFrom(input.uv2);
                output.across = float3(input.positionOS.x, input.uv3.x, input.uv3.y);
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 n = normalize(input.normalWS);

                // Position en travers calculée depuis la géométrie (centre et demi-largeur constants sur chaque ligne
                // du mesh) plutôt que depuis l'UV interpolé : l'UV se tord dans les quads en trapèze (virages,
                // route qui rétrécit) et les lignes faisaient des zigzags.
                float2 uv = input.uv;
                if (input.across.z > 0.01) uv.x = (input.across.x - input.across.y) / (2.0 * input.across.z) + 0.5;

                // Grain de l'asphalte en mètres (u * largeur, v * longueur d'UV) pour des nuances de gris
                float2 meters = float2(uv.x * input.roadWidth, uv.y * _UVTileLength);
                float grain = Hash12(floor(meters / _GrainSize));
                half3 albedo = _BaseColor.rgb * (1.0 + (grain - 0.5) * 2.0 * _Grain);

                // Éclairage simple : soleil (avec ombres portées) + ambiance
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half nDotL = saturate(dot(n, mainLight.direction));
                half3 lit = mainLight.color * nDotL * mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                lit += SampleSH(n) * _AmbientStrength;
                half3 color = albedo * lit;

                // Lignes émissives (HDR : active le Bloom pour qu'elles brillent)
                float lines = RoadLineMask(uv, input.roadWidth);
                color = lerp(color, _LineEmission.rgb, lines);

                // Fondu vers le blanc du ciel au loin, en vraie partie seulement (global posé par InGameLook)
                float dist = distance(input.positionWS, _WorldSpaceCameraPos);
                float fade = saturate((dist - _RacerDistanceFade.x) / max(_RacerDistanceFade.y - _RacerDistanceFade.x, 1e-3));
                color = lerp(color, half3(1, 1, 1), fade * _RacerDistanceFade.z);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
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
            half DepthFrag(DepthVaryings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }

        // Normales (texture des normales URP). Alpha = 1 marque les pixels de route, pour un éventuel contour plein écran.
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_instancing

            struct NormalsAttributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct NormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                half3 normalWS : TEXCOORD0;
            };

            NormalsVaryings NormalsVert(NormalsAttributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                NormalsVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 NormalsFrag(NormalsVaryings input) : SV_Target
            {
                return half4(normalize(input.normalWS), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
