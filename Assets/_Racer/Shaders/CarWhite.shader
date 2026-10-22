// Voiture en jeu : toute blanche (nuance grise franche côté ombre) + contour noir de la silhouette.
// Contour = 2e passe "coque inversée" (faces arrière gonflées), épaisseur constante à l'écran.
// La coque est gonflée selon des normales lissées que InGameLook range dans les tangentes (w = 2) :
// pas de trous aux arêtes vives des modèles low poly. Sans ça (mesh non lisible), on utilise la normale.
// Les arêtes intérieures (capot, vitres, roues...) sont dessinées à part par Racer/CarEdges.
// Mobile : 2 passes très simples, pas de texture, pas d'ombre temps réel, compatible SRP Batcher.
Shader "Racer/CarWhite"
{
    Properties
    {
        _BaseColor ("Blanc (côté lumière)", Color) = (1, 1, 1, 1)
        _ShadeColor ("Nuance (côté ombre)", Color) = (0.74, 0.74, 0.77, 1)
        _ShadeStep ("Seuil de la nuance", Range(0, 1)) = 0.45
        _ShadeSoftness ("Douceur de la nuance", Range(0.001, 0.5)) = 0.06
        _OutlineColor ("Couleur du contour", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Épaisseur du contour (px en 1080p)", Range(0, 10)) = 3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _ShadeColor;
            half _ShadeStep;
            half _ShadeSoftness;
            half4 _OutlineColor;
            float _OutlineWidth;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                Light light = GetMainLight();
                half ndl = dot(n, light.direction) * 0.5h + 0.5h; // demi-Lambert : jamais de noir
                half t = smoothstep(_ShadeStep - _ShadeSoftness, _ShadeStep + _ShadeSoftness, ndl);
                half3 color = lerp(_ShadeColor.rgb, _BaseColor.rgb, t);
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT; // xyz = normale lissée si w = 2 (voir InGameLook)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half fogFactor : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 n = abs(input.tangentOS.w - 2.0) < 0.5 ? input.tangentOS.xyz : input.normalOS;
                float4 cs = TransformObjectToHClip(input.positionOS.xyz);
                float3 nCS = mul((float3x3)UNITY_MATRIX_VP, TransformObjectToWorldNormal(n));

                // Gonfle en pixels (même épaisseur quelle que soit la distance et l'écran)
                float2 px = _ScreenParams.xy;
                float2 d = nCS.xy * px;
                float len = length(d);
                d = len > 1e-5 ? d / len : float2(0, 0);
                float w = _OutlineWidth * px.y / 1080.0;
                cs.xy += d * w * 2.0 / px * cs.w;

                output.positionCS = cs;
                output.fogFactor = ComputeFogFactor(cs.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(MixFog(_OutlineColor.rgb, input.fogFactor), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
