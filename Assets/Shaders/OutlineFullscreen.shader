// Contours noirs (ou blancs) sur les arêtes de tous les modèles 3D, en plein écran.
// À utiliser avec le Renderer Feature "Full Screen Pass" de URP (voir README_Shaders.md).
//
// Principe : on compare la profondeur et les normales des pixels voisins (croix de Roberts).
// Une forte différence = une arête -> on dessine le contour.
// La route est exclue : son shader (Racer/RoadLines) écrit alpha = 1 dans la texture des normales,
// les pixels de route ne reçoivent donc jamais de contour (mais la voiture posée dessus, si).
Shader "Racer/OutlineFullscreen"
{
    Properties
    {
        _OutlineColor ("Couleur du contour", Color) = (0, 0, 0, 1)
        _Thickness ("Épaisseur (pixels)", Range(0.5, 4)) = 1.2
        _DepthThreshold ("Seuil profondeur (relatif)", Range(0.001, 0.5)) = 0.04
        _GrazingBoost ("Tolérance sols rasants", Range(0, 20)) = 6
        _NormalThreshold ("Seuil normales", Range(0.01, 1)) = 0.35
        _FadeStart ("Début fondu (m)", Float) = 60
        _FadeEnd ("Fin fondu (m)", Float) = 140
        _Desaturate ("Noir et blanc (0 = couleurs d'origine)", Range(0, 1)) = 1
        _Contrast ("Contraste", Range(0.5, 2)) = 1.1
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off Blend Off

        Pass
        {
            Name "Outline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            half4 _OutlineColor;
            float _Thickness, _DepthThreshold, _GrazingBoost, _NormalThreshold;
            float _FadeStart, _FadeEnd, _Desaturate, _Contrast;

            // Normale brute + masque route (alpha). En Forward / Forward+ les normales sont stockées telles quelles.
            float4 SampleNormalsRaw(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_CameraNormalsTexture, UnityStereoTransformScreenSpaceTex(uv));
            }

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // ----- Noir et blanc (avec nuances) -----
                half lum = dot(color.rgb, half3(0.2126, 0.7152, 0.0722));
                half3 bw = saturate((lum - 0.5) * _Contrast + 0.5) * max(1.0, lum); // garde le HDR pour le bloom
                color.rgb = lerp(color.rgb, bw.xxx, _Desaturate);

                float4 n0raw = SampleNormalsRaw(uv);
                if (n0raw.a > 0.5) return color; // pixel de route : pas de contour

                float2 t = _CameraDepthTexture_TexelSize.xy * _Thickness;
                float2 uv1 = uv + float2(-t.x,  t.y);
                float2 uv2 = uv + float2( t.x,  t.y);
                float2 uv3 = uv + float2(-t.x, -t.y);
                float2 uv4 = uv + float2( t.x, -t.y);

                float d0 = EyeDepth(uv);
                float d1 = EyeDepth(uv1), d2 = EyeDepth(uv2), d3 = EyeDepth(uv3), d4 = EyeDepth(uv4);

                float3 n0 = n0raw.xyz;
                float3 n1 = SampleNormalsRaw(uv1).xyz, n2 = SampleNormalsRaw(uv2).xyz;
                float3 n3 = SampleNormalsRaw(uv3).xyz, n4 = SampleNormalsRaw(uv4).xyz;

                // Sur un sol vu de biais la profondeur varie vite : on tolère plus de différence (évite les fausses lignes)
                float3 posWS = ComputeWorldSpacePosition(uv, SampleSceneDepth(uv), UNITY_MATRIX_I_VP);
                float3 viewDir = normalize(_WorldSpaceCameraPos - posWS);
                float nDotV = saturate(dot(n0, viewDir));
                float depthThreshold = _DepthThreshold * (1.0 + _GrazingBoost * (1.0 - nDotV));

                // Croix de Roberts sur la profondeur (relative à la distance, pour que ça marche de près comme de loin)
                float depthDiff = sqrt((d1 - d4) * (d1 - d4) + (d2 - d3) * (d2 - d3)) / max(d0, 0.0001);
                float depthEdge = step(depthThreshold, depthDiff);

                // Croix de Roberts sur les normales
                float3 nd1 = n1 - n4, nd2 = n2 - n3;
                float normalDiff = sqrt(dot(nd1, nd1) + dot(nd2, nd2));
                float normalEdge = step(_NormalThreshold, normalDiff);

                float edge = max(depthEdge, normalEdge);
                edge *= 1.0 - smoothstep(_FadeStart, _FadeEnd, d0); // les contours s'estompent au loin

                color.rgb = lerp(color.rgb, _OutlineColor.rgb, edge * _OutlineColor.a);
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
