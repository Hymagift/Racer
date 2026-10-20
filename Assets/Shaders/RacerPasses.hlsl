// Passes communes aux shaders Racer : ombres portées et normales (pour le contour plein écran).
// Valeurs d'alpha écrites dans la texture des normales (lues par Racer/OutlineFullscreen) :
//   0   = objet normal (contour noir)
//   0.5 = cône (pas de contour noir autour, il a déjà son contour orange)
//   1   = route (pas de contour sur ses pixels)
#ifndef RACER_PASSES_INCLUDED
#define RACER_PASSES_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

#ifndef RACER_NORMALS_MASK
#define RACER_NORMALS_MASK 0.0
#endif

struct RacerAttributes
{
    float3 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct RacerVaryings
{
    float4 positionCS : SV_POSITION;
    half3 normalWS : TEXCOORD0;
};

// ----- ShadowCaster -----
float3 _LightDirection;
float3 _LightPosition;

RacerVaryings RacerShadowVert(RacerAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    RacerVaryings output;
    float3 positionWS = TransformObjectToWorld(input.positionOS);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
        float3 lightDirection = normalize(_LightPosition - positionWS);
    #else
        float3 lightDirection = _LightDirection;
    #endif
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirection));
    #if UNITY_REVERSED_Z
        positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
    #else
        positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
    #endif
    output.positionCS = positionCS;
    output.normalWS = normalWS;
    return output;
}
half4 RacerShadowFrag(RacerVaryings input) : SV_Target { return 0; }

// ----- DepthOnly / DepthNormals -----
RacerVaryings RacerDepthVert(RacerAttributes input)
{
    UNITY_SETUP_INSTANCE_ID(input);
    RacerVaryings output;
    output.positionCS = TransformObjectToHClip(input.positionOS);
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    return output;
}
half RacerDepthFrag(RacerVaryings input) : SV_Target { return input.positionCS.z; }
half4 RacerNormalsFrag(RacerVaryings input) : SV_Target { return half4(normalize(input.normalWS), RACER_NORMALS_MASK); }

#endif
