// Fumée stylisée (façon "Stylized Smoke" de Gabriel Aguiar, adaptée au Particle System et au mobile) :
// - des boules de fumée OPAQUES (mesh bosselé), éclairées en 2 tons (toon) ;
// - elles disparaissent par ÉROSION : un bruit 3D découpe la boule au lieu d'un fondu transparent.
// L'avancement de l'érosion vient de l'alpha de la couleur de la particule
// (Color over Lifetime : alpha 1 = boule pleine, alpha 0 = complètement rongée).
Shader "Racer/StylizedSmoke"
{
    Properties
    {
        _LightColor ("Couleur éclairée", Color) = (1, 1, 1, 1)
        _ShadowColor ("Couleur ombre", Color) = (0.6, 0.6, 0.6, 1)
        _ShadowThreshold ("Seuil de l'ombre", Range(-1, 1)) = 0.05
        _ShadowSoftness ("Douceur de l'ombre", Range(0.001, 0.5)) = 0.04
        _NoiseScale ("Échelle du bruit d'érosion", Float) = 2.2
        _EdgeWidth ("Épaisseur du liseré d'érosion", Range(0, 0.2)) = 0.06
        _EdgeColor ("Couleur du liseré", Color) = (0.4, 0.4, 0.4, 1)
    }

    SubShader
    {
        Tags { "RenderType" = "TransparentCutout" "Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _LightColor;
            half4 _ShadowColor;
            half4 _EdgeColor;
            float _ShadowThreshold;
            float _ShadowSoftness;
            float _NoiseScale;
            float _EdgeWidth;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            half4 color       : COLOR;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 normalWS   : TEXCOORD0;
            float3 noisePos   : TEXCOORD1;
            half4 color       : COLOR;
        };

        // Bruit de valeur 3D (pas de texture à fournir)
        float Hash(float3 p)
        {
            p = frac(p * 0.3183099 + 0.1);
            p *= 17.0;
            return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
        }

        float ValueNoise(float3 x)
        {
            float3 i = floor(x);
            float3 f = frac(x);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(lerp(Hash(i + float3(0, 0, 0)), Hash(i + float3(1, 0, 0)), f.x),
                             lerp(Hash(i + float3(0, 1, 0)), Hash(i + float3(1, 1, 0)), f.x), f.y),
                        lerp(lerp(Hash(i + float3(0, 0, 1)), Hash(i + float3(1, 0, 1)), f.x),
                             lerp(Hash(i + float3(0, 1, 1)), Hash(i + float3(1, 1, 1)), f.x), f.y), f.z);
        }

        float SmokeNoise(float3 p)
        {
            float n = ValueNoise(p) * 0.65 + ValueNoise(p * 2.3 + 7.1) * 0.35;
            return saturate((n - 0.15) / 0.7); // étale le bruit sur 0..1 pour que l'érosion aille jusqu'au bout
        }

        Varyings Vert(Attributes v)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            // Le bruit suit la normale : il tourne avec chaque boule (rotation aléatoire) au lieu de glisser dessus
            o.noisePos = o.normalWS * _NoiseScale;
            o.color = v.color;
            return o;
        }

        // Renvoie de combien le pixel dépasse le front d'érosion (et le jette s'il est rongé)
        float Erode(Varyings i)
        {
            float erosion = 1.0 - i.color.a;
            float d = SmokeNoise(i.noisePos) - erosion * 1.02;
            clip(d);
            return d;
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings i) : SV_Target
            {
                float d = Erode(i);

                // Éclairage 2 tons : blanc côté soleil, gris côté ombre
                float ndl = dot(normalize(i.normalWS), GetMainLight().direction);
                float lit = smoothstep(_ShadowThreshold - _ShadowSoftness, _ShadowThreshold + _ShadowSoftness, ndl);
                half3 col = lerp(_ShadowColor.rgb, _LightColor.rgb, lit) * i.color.rgb;

                // Petit liseré plus sombre au bord des trous d'érosion
                float edge = step(d, _EdgeWidth) * step(0.001, 1.0 - i.color.a);
                col = lerp(col, _EdgeColor.rgb * i.color.rgb, edge);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDepth

            half FragDepth(Varyings i) : SV_Target
            {
                Erode(i);
                return 0;
            }
            ENDHLSL
        }
    }
}
