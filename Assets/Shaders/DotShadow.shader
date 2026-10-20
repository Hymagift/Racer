// Ombre de la voiture façon trame : des points blancs, gros au centre, de plus en plus petits vers les bords.
// À mettre sur un Quad couché sous la voiture (le script CarDotShadow s'en occupe tout seul).
// Les tailles sont en mètres : le Quad peut être étiré, les points restent ronds.
Shader "Racer/DotShadow"
{
    Properties
    {
        [HDR] _Color ("Couleur des points", Color) = (1, 1, 1, 1)
        _Opacity ("Opacité", Range(0, 1)) = 0.9
        _DotSpacing ("Espacement des points (m)", Range(0.05, 1)) = 0.18
        _DotMaxSize ("Taille max d'un point (part de l'espacement)", Range(0.1, 1)) = 0.8
        _CornerRadius ("Arrondi des coins (m)", Range(0, 2)) = 0.6
        _Softness ("Dégradé vers les bords (m)", Range(0.01, 2)) = 0.7
        _GridAngle ("Angle de la trame (degrés)", Range(0, 90)) = 0
        [ToggleUI] _Staggered ("Points en quinconce", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "DotShadow"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1 // évite le scintillement avec la route juste en dessous

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Opacity;
                float _DotSpacing, _DotMaxSize, _CornerRadius, _Softness, _GridAngle, _Staggered;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 posM : TEXCOORD0;      // position sur le Quad en mètres (centre = 0)
                float2 halfSize : TEXCOORD1;  // demi-taille du Quad en mètres
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3x3 m = (float3x3)GetObjectToWorldMatrix();
                float2 scale = float2(length(mul(m, float3(1, 0, 0))), length(mul(m, float3(0, 1, 0))));
                output.positionCS = TransformObjectToHClip(input.positionOS);
                output.posM = input.positionOS.xy * scale;   // le Quad Unity va de -0.5 à 0.5 en X/Y
                output.halfSize = scale * 0.5;
                return output;
            }

            // Distance signée à un rectangle arrondi (négatif = dedans)
            float RoundedRectSDF(float2 p, float2 halfSize, float r)
            {
                r = min(r, min(halfSize.x, halfSize.y));
                float2 q = abs(p) - (halfSize - r);
                return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
            }

            // Densité de l'ombre (1 au centre -> 0 au bord)
            float ShadowDensity(float2 p, float2 halfSize)
            {
                float d = RoundedRectSDF(p, halfSize, _CornerRadius);
                return saturate(-d / _Softness);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float2 p = input.posM;

                // Trame tournée
                float a = radians(_GridAngle);
                float2x2 rot = float2x2(cos(a), -sin(a), sin(a), cos(a));
                float2 g = mul(rot, p) / _DotSpacing;

                // Une ligne sur deux décalée d'un demi-point (quinconce)
                float row = floor(g.y + 0.5);
                g.x += _Staggered * 0.5 * (row - 2.0 * floor(row * 0.5));

                float2 cell = floor(g + 0.5);
                float2 local = g - cell;

                // Centre du point ramené dans l'espace du Quad : la taille du point dépend de la densité à cet endroit
                float2 cellCenter = cell;
                cellCenter.x -= _Staggered * 0.5 * (row - 2.0 * floor(row * 0.5));
                float2 centerM = mul(transpose(rot), cellCenter * _DotSpacing);
                float density = ShadowDensity(centerM, input.halfSize);

                float radius = 0.5 * _DotMaxSize * sqrt(density); // aire du point proportionnelle à la densité
                float dist = length(local);
                float aa = max(fwidth(dist), 1e-4);
                float dotMask = 1.0 - smoothstep(radius - aa, radius + aa, dist);
                dotMask *= step(1e-3, density);

                half alpha = dotMask * _Color.a * _Opacity;
                clip(alpha - 0.001);
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
