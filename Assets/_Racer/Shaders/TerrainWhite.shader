// Terrain en jeu : base blanche, facettes low poly en 2 nuances franches + pentes raides un peu plus grises
// (le relief se lit contre le ciel blanc et sous le décor noir). Au loin, fondu vers le blanc du ciel
// (cache l'apparition des morceaux de terrain). Pas de texture, une seule passe : léger sur mobile.
// _BaseMap n'est pas utilisé : il existe seulement parce que LowPolyTerrainGenerator lui assigne sa texture.
Shader "Racer/TerrainWhite"
{
    Properties
    {
        [MainColor] _BaseColor ("Blanc (côté lumière)", Color) = (1, 1, 1, 1)
        _ShadeColor ("Nuance (côté ombre)", Color) = (0.84, 0.84, 0.86, 1)
        _SlopeColor ("Pentes raides", Color) = (0.7, 0.7, 0.72, 1)
        _ShadeStep ("Seuil de la nuance", Range(0, 1)) = 0.55
        _ShadeSoftness ("Douceur de la nuance", Range(0.001, 0.5)) = 0.04
        _SlopeStart ("Pente à partir de (normale Y)", Range(0, 1)) = 0.75
        _SlopeEnd ("Pente maximale (normale Y)", Range(0, 1)) = 0.45
        [MainTexture] [NoScaleOffset] _BaseMap ("Inutilisé", 2D) = "white" {}
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _ShadeColor;
                half4 _SlopeColor;
                half _ShadeStep, _ShadeSoftness;
                half _SlopeStart, _SlopeEnd;
            CBUFFER_END

            float4 _RacerDistanceFade; // global (InGameLook) : x = début, y = fin (m), z = force. 0 = pas de fondu.

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 n = normalize(input.normalWS);
                Light light = GetMainLight();
                half ndl = dot(n, light.direction) * 0.5h + 0.5h;
                half t = smoothstep(_ShadeStep - _ShadeSoftness, _ShadeStep + _ShadeSoftness, ndl);
                half3 color = lerp(_ShadeColor.rgb, _BaseColor.rgb, t);

                half slope = smoothstep(_SlopeStart, _SlopeEnd, n.y);
                color = lerp(color, color * _SlopeColor.rgb, slope);

                float dist = distance(input.positionWS, _WorldSpaceCameraPos);
                float fade = saturate((dist - _RacerDistanceFade.x) / max(_RacerDistanceFade.y - _RacerDistanceFade.x, 1e-3));
                color = lerp(color, half3(1, 1, 1), fade * _RacerDistanceFade.z);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
