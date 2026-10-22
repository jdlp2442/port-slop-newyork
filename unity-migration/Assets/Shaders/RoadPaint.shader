// ---------------------------------------------------------------------------------------------
// Road markings (lane dashes, double yellow, stop bars, continental crosswalks, bike lanes).
// Port of createRoadPaintMaterial() from js/tsl-materials.js.
//
// Vertex contract written by RoadMarkingBuilder.cs:
//   uv0 = (metres along the strip, unused)
//   uv1 = (pattern period, duty cycle 0..1)
//   uv2 = (offset from the strip centre to this edge, in metres: x, z)
//   color = paint colour
//
// Strips thinner than ~1.5 px are widened on screen and faded back, so distant dashes dissolve
// into their mean coverage instead of flickering.
// ---------------------------------------------------------------------------------------------
Shader "Nyc/RoadPaint"
{
    Properties
    {
        _PaintRoughness ("Paint roughness", Range(0, 1)) = 0.62
        _WearAmount     ("Wear", Range(0, 1)) = 0.45
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/NycCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _PaintRoughness;
                float _WearAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 uv0        : TEXCOORD0;
                float4 uv1        : TEXCOORD1;
                float4 uv2        : TEXCOORD2;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 paint      : TEXCOORD2;   // along, period
                float  duty       : TEXCOORD3;
                float  widenInv   : TEXCOORD4;
                float4 color      : TEXCOORD5;
                float  fogCoord   : TEXCOORD6;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 sideOffset = float3(input.uv2.x, 0.0, input.uv2.y);

                // Screen-space width of half the strip; widen it to at least 0.75 px.
                float4 clipEdge = TransformWorldToHClip(positionWS);
                float4 clipCentre = TransformWorldToHClip(positionWS - sideOffset);
                float2 edgeNdc = clipEdge.xy / max(clipEdge.w, 1e-4);
                float2 centreNdc = clipCentre.xy / max(clipCentre.w, 1e-4);
                float2 deltaPx = (edgeNdc - centreNdc) * _ScreenParams.xy * 0.5;
                float halfWidthPx = length(deltaPx);
                float widen = clamp(0.75 / max(halfWidthPx, 0.001), 1.0, 16.0);
                widen = max(widen, 1.0);

                positionWS += sideOffset * (widen - 1.0);

                output.positionCS = TransformWorldToHClip(positionWS);
                output.positionWS = positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.paint = float2(input.uv0.x, max(input.uv1.x, 0.001));
                output.duty = saturate(input.uv1.y);
                output.widenInv = 1.0 / widen;
                output.color = input.color;
                output.fogCoord = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float PulseOnSoFar(float x, float duty)
            {
                return floor(x) * duty + clamp(frac(x) - (1.0 - duty) * 0.5, 0.0, duty);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = input.positionWS;
                float3 normalWS = normalize(input.normalWS);

                // Exact box filter of the dashes over the pixel footprint.
                float t = input.paint.x / input.paint.y;
                float footprint = max(fwidth(t), 0.0001);
                float duty = input.duty;
                float dashes = (PulseOnSoFar(t + footprint * 0.5, duty)
                              - PulseOnSoFar(t - footprint * 0.5, duty)) / footprint;

                float3 wet = NycWetMasks(NycPuddleField(positionWS.x, positionWS.z));
                float detail = smoothstep(70.0, 15.0, distance(positionWS, _WorldSpaceCameraPos));
                float wearNoise = lerp(0.5,
                                       NycNoise2D(positionWS.xz * 0.8) * 0.6 + NycNoise2D(positionWS.xz * 2.3) * 0.4,
                                       detail);
                float wear = smoothstep(0.1, 0.6, wearNoise) * 0.4 + 0.55;

                float alpha = saturate(dashes * input.widenInv * wear);
                if (alpha < 0.02) discard;

                float3 albedo = input.color.rgb
                              * lerp(1.0, 0.72, wet.y) * lerp(1.0, 0.86, wet.z);
                float roughness = lerp(lerp(_PaintRoughness, 0.42, wet.z), 0.2, wet.y);

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half4 color = UniversalFragmentPBR(inputData, albedo, 0.0h, half3(0, 0, 0),
                                                   roughness, 1.0h, 0.0h, alpha);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
