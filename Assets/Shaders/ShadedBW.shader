// Shader noir et blanc "à plat" pour la DA : éclairage découpé en quelques nuances de gris (cel shading).
// Pour la voiture, le décor (terrain, glissières) et tout modèle qui doit suivre la DA.
// Les couleurs données par le jeu (teinte du garage, couleurs du terrain) sont converties en gris si "Noir et blanc" = 1.
Shader "Racer/ShadedBW"
{
    Properties
    {
        _BaseColor ("Couleur (éclairée)", Color) = (1, 1, 1, 1)
        _ShadeColor ("Couleur (ombre)", Color) = (0.25, 0.25, 0.25, 1)
        [IntRange] _Steps ("Nombre de nuances", Range(1, 6)) = 3
        _ShadeOffset ("Décalage ombre/lumière", Range(-1, 1)) = 0
        _RimStrength ("Liseré de lumière", Range(0, 1)) = 0.25
        _Desaturate ("Noir et blanc", Range(0, 1)) = 1
        // Utilisées par les scripts (material.color / mainTexture) : gardées pour qu'ils ne cassent pas la DA
        [MainColor] _Tint ("Teinte (scripts)", Color) = (1, 1, 1, 1)
        [MainTexture] _BaseMap ("Texture (inutilisée)", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadeColor;
            half _Steps, _ShadeOffset, _RimStrength, _Desaturate;
            half4 _Tint;
            float4 _BaseMap_ST;
        CBUFFER_END
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
                half fogFactor : TEXCOORD2;
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
                output.fogFactor = ComputeFogFactor(pos.positionCS.z);
                return output;
            }

            half3 ToGray(half3 c) { return lerp(c, dot(c, half3(0.2126, 0.7152, 0.0722)).xxx, _Desaturate); }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half light = saturate(dot(n, mainLight.direction) * 0.5 + 0.5 + _ShadeOffset); // demi-Lambert
                light *= lerp(0.35, 1.0, mainLight.shadowAttenuation);                          // ombres portées

                // Découpage en paliers : des nuances franches au lieu d'un dégradé
                half steps = max(1.0, round(_Steps));
                light = saturate(floor(light * steps + 0.5) / steps);

                half3 lit = ToGray(_BaseColor.rgb * _Tint.rgb);
                half3 shade = ToGray(_ShadeColor.rgb * _Tint.rgb);
                half3 color = lerp(shade, lit, light);

                // Liseré clair sur les bords (côté éclairé) pour détacher les formes du fond
                half rim = pow(1.0 - saturate(dot(n, v)), 4.0) * _RimStrength * light;
                color += rim;

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
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
            #include "RacerPasses.hlsl"
            ENDHLSL
        }
    }
    FallBack Off
}
