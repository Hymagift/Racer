// DA en jeu : objet en aplat (2 nuances franches) + contour de la silhouette + traits sur les arêtes.
//   Voitures : blanc, traits noirs (Resources/InGame/CarWhite).  Décor : noir, traits blancs (Resources/InGame/DecorBlack).
// Tout est dans UN mesh préparé par InGameLook (1 seul material) :
//   - sommets de l'objet : tangente xyz = normale lissée (w = 2) pour gonfler le contour sans trous aux arêtes vives ;
//   - quads posés sur les arêtes (uv3.x = 1) : tangente xyz = direction de l'arête, uv3.y = -1/+1 (début/fin),
//     uv3.z = -1/+1 (côté). Ils sont écartés en pixels à l'écran : trait d'épaisseur constante.
// 2 passes (aplat + arêtes, puis contour en coque inversée), pas de texture ni de post-process : léger sur mobile.
// Sans mesh préparé (FBX sans Read/Write), on garde l'aplat et le contour (gonflé selon la normale).
Shader "Racer/Outlined"
{
    Properties
    {
        _BaseColor ("Couleur (côté lumière)", Color) = (1, 1, 1, 1)
        _ShadeColor ("Nuance (côté ombre)", Color) = (0.74, 0.74, 0.77, 1)
        _ShadeStep ("Seuil de la nuance", Range(0, 1)) = 0.45
        _ShadeSoftness ("Douceur de la nuance", Range(0.001, 0.5)) = 0.06
        _LineColor ("Couleur des traits", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Épaisseur du contour (px en 1080p)", Range(0, 10)) = 3
        _EdgeWidth ("Épaisseur des arêtes (px en 1080p)", Range(0, 10)) = 2.2
        _DepthBias ("Arêtes : décalage vers la caméra (m)", Range(0, 0.2)) = 0.015
        _FadeStart ("Traits plus fins à partir de (m)", Float) = 40
        _FadeEnd ("Traits les plus fins à (m)", Float) = 120
        _FarScale ("Épaisseur au loin (x)", Range(0, 1)) = 0.4
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
            half4 _LineColor;
            float _OutlineWidth;
            float _EdgeWidth;
            float _DepthBias;
            float _FadeStart, _FadeEnd, _FarScale;
        CBUFFER_END

        float _RacerLineBoost; // global : photos du garage (traits plus épais). 0 = normal.
        float4 _RacerDistanceFade; // global (InGameLook) : x = début, y = fin (m), z = force. 0 = pas de fondu.

        // Fondu vers le blanc du ciel au loin (cache l'apparition du décor)
        half3 DistanceFade(half3 color, float3 posWS)
        {
            float dist = distance(posWS, _WorldSpaceCameraPos);
            float fade = saturate((dist - _RacerDistanceFade.x) / max(_RacerDistanceFade.y - _RacerDistanceFade.x, 1e-3));
            return lerp(color, half3(1, 1, 1), fade * _RacerDistanceFade.z);
        }

        // Pixels (en 1080p) -> pixels de l'écran, plus fins au loin
        float LinePixels(float width, float3 posWS)
        {
            float dist = distance(posWS, _WorldSpaceCameraPos);
            float far = saturate((dist - _FadeStart) / max(_FadeEnd - _FadeStart, 1e-3));
            return width * (1.0 + _RacerLineBoost) * lerp(1.0, _FarScale, far) * _ScreenParams.y / 1080.0;
        }
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
                float4 tangentOS : TANGENT;
                float4 edge : TEXCOORD3; // x = 1 sur les quads d'arêtes
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                half fogFactor : TEXCOORD1;
                half isEdge : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = posWS;
                float4 cs;
                if (input.edge.x > 0.5)
                {
                    float3 dirWS = TransformObjectToWorldDir(input.tangentOS.xyz);
                    float halfW = LinePixels(_EdgeWidth, posWS) * 0.5;

                    // Légèrement vers la caméra pour passer devant la surface sans scintiller
                    float3 toCam = _WorldSpaceCameraPos - posWS;
                    posWS += toCam * (_DepthBias / max(length(toCam), 1e-4));

                    cs = TransformWorldToHClip(posWS);
                    float4 cs2 = TransformWorldToHClip(posWS + dirWS * 0.1);
                    float2 px = _ScreenParams.xy;
                    float2 d = (cs2.xy / cs2.w - cs.xy / cs.w) * px;
                    float len = length(d);
                    d = len > 1e-5 ? d / len : float2(1, 0);
                    float2 perp = float2(-d.y, d.x);
                    float2 offPx = (perp * input.edge.z + d * input.edge.y) * halfW; // prolongé aux bouts : coins fermés
                    cs.xy += offPx * 2.0 / px * cs.w;
                }
                else
                {
                    cs = TransformWorldToHClip(posWS);
                }
                output.positionCS = cs;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(cs.z);
                output.isEdge = input.edge.x;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 color;
                if (input.isEdge > 0.5h)
                {
                    color = _LineColor.rgb;
                }
                else
                {
                    half3 n = normalize(input.normalWS);
                    Light light = GetMainLight();
                    half ndl = dot(n, light.direction) * 0.5h + 0.5h; // demi-Lambert
                    half t = smoothstep(_ShadeStep - _ShadeSoftness, _ShadeStep + _ShadeSoftness, ndl);
                    color = lerp(_ShadeColor.rgb, _BaseColor.rgb, t);
                }
                color = DistanceFade(color, input.positionWS);
                return half4(MixFog(color, input.fogFactor), 1);
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
                float4 tangentOS : TANGENT;
                float4 edge : TEXCOORD3;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half fogFactor : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = 0;
                if (input.edge.x > 0.5)
                {
                    // Les quads d'arêtes ne font pas partie de la coque : hors écran, ignorés
                    output.positionCS = float4(2, 2, 2, 1);
                    output.fogFactor = 0;
                    return output;
                }

                float3 n = abs(input.tangentOS.w - 2.0) < 0.5 ? input.tangentOS.xyz : input.normalOS;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float4 cs = TransformWorldToHClip(posWS);
                float3 nCS = mul((float3x3)UNITY_MATRIX_VP, TransformObjectToWorldNormal(n));

                float2 px = _ScreenParams.xy;
                float2 d = nCS.xy * px;
                float len = length(d);
                d = len > 1e-5 ? d / len : float2(0, 0);
                cs.xy += d * LinePixels(_OutlineWidth, posWS) * 2.0 / px * cs.w;

                output.positionCS = cs;
                output.positionWS = posWS;
                output.fogFactor = ComputeFogFactor(cs.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(MixFog(DistanceFade(_LineColor.rgb, input.positionWS), input.fogFactor), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
