#ifndef NYC_PASSES_INCLUDED
#define NYC_PASSES_INCLUDED

// ---------------------------------------------------------------------------------------------
// Shared ShadowCaster / DepthOnly / DepthNormals vertex+fragment bodies.
// Each shader declares the (short) Pass blocks and calls these.
// Requires: Core.hlsl + Shadows.hlsl already included.
// ---------------------------------------------------------------------------------------------

float3 _LightDirection;

float4 NycShadowPositionHClip(float3 positionWS, float3 normalWS)
{
    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, _LightDirection));
#if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    return positionCS;
}

struct NycDepthAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct NycDepthVaryings
{
    float4 positionCS : SV_POSITION;
    float3 normalWS   : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

NycDepthVaryings NycDepthVertex(NycDepthAttributes input)
{
    NycDepthVaryings output = (NycDepthVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
    output.positionCS = positions.positionCS;
    output.normalWS = TransformObjectToWorldNormal(input.normalOS);
    return output;
}

float4 NycShadowFragment(NycDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    return 0;
}

float NycDepthOnlyFragment(NycDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    return input.positionCS.z;
}

float4 NycDepthNormalsFragment(NycDepthVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    return float4(NormalizeNormalPerPixel(input.normalWS), 0.0);
}

#endif // NYC_PASSES_INCLUDED
