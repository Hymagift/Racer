// Traits noirs sur les arêtes de la voiture en jeu (arêtes vives, bords ouverts, frontières entre materials).
// Le mesh est fabriqué par InGameLook : chaque arête = un quad de 4 sommets posés sur ses 2 extrémités.
//   tangente xyz = direction de l'arête (suit les os des roues comme la position)
//   uv.x = -1 (début) / +1 (fin) : prolonge le trait d'une demi-épaisseur pour fermer les coins
//   uv.y = -1 / +1 : côté du trait
// Le vertex shader écarte les sommets en pixels perpendiculairement à l'arête vue à l'écran :
// trait d'épaisseur constante, 1 seul draw call, aucun post-process (mobile).
Shader "Racer/CarEdges"
{
    Properties
    {
        _EdgeColor ("Couleur des arêtes", Color) = (0, 0, 0, 1)
        _EdgeWidth ("Épaisseur (px en 1080p)", Range(0, 10)) = 2.2
        _DepthBias ("Décalage vers la caméra (m)", Range(0, 0.1)) = 0.015
    }

    SubShader
    {
        // Après la carrosserie : le test de profondeur cache les arêtes de l'autre côté de la voiture
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry+1" }

        Pass
        {
            Name "Edges"
            Tags { "LightMode"="UniversalForward" }
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _EdgeColor;
                float _EdgeWidth;
                float _DepthBias;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half fogFactor : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 dirWS = TransformObjectToWorldDir(input.tangentOS.xyz);

                // Légèrement vers la caméra pour passer devant la carrosserie sans scintiller
                float3 toCam = _WorldSpaceCameraPos - posWS;
                posWS += toCam * (_DepthBias / max(length(toCam), 1e-4));

                float4 cs = TransformWorldToHClip(posWS);
                float4 cs2 = TransformWorldToHClip(posWS + dirWS * 0.1);

                float2 px = _ScreenParams.xy;
                float2 d = (cs2.xy / cs2.w - cs.xy / cs.w) * px;
                float len = length(d);
                d = len > 1e-5 ? d / len : float2(1, 0);
                float2 perp = float2(-d.y, d.x);

                float halfW = _EdgeWidth * 0.5 * px.y / 1080.0;
                float2 offPx = (perp * input.uv.y + d * input.uv.x) * halfW;
                cs.xy += offPx * 2.0 / px * cs.w;

                output.positionCS = cs;
                output.fogFactor = ComputeFogFactor(cs.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(MixFog(_EdgeColor.rgb, input.fogFactor), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
