// ---------------------------------------------------------------------------------------------
// Manhattan roadway: footprint-filtered aggregate, patchwork pours, tar seams, manholes and
// post-rain wetness (damp stains -> film -> standing puddles that mirror the street through SSR).
// Port of createRoadSurfaceMaterial() from js/tsl-materials.js.
// ---------------------------------------------------------------------------------------------
Shader "Nyc/RoadSurface"
{
    Properties
    {
        _AsphaltDark   ("Asphalt dark", Color)  = (0.169, 0.180, 0.200, 1)
        _AsphaltLight  ("Asphalt light", Color) = (0.275, 0.290, 0.318, 1)
        _ManholeColor  ("Manhole cast iron", Color) = (0.290, 0.263, 0.235, 1)
        _BumpScale     ("Aggregate relief", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/NycCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _AsphaltDark;
                float4 _AsphaltLight;
                float4 _ManholeColor;
                float  _BumpScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  fogCoord   : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogCoord = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = input.positionWS;
                float3 normalWS = normalize(input.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(positionWS);

                float wx = positionWS.x;
                float wz = positionWS.z;

                float3 wet = NycWetMasks(NycPuddleField(wx, wz));
                float damp = wet.x, puddle = wet.y, film = wet.z;

                float pixelSize = length(fwidth(positionWS.xz));
                float camDist = distance(positionWS, _WorldSpaceCameraPos);
                float detail = smoothstep(240.0, 22.0, camDist);
                float waterFresnel = NycWaterFresnel(viewDir);

                // --- albedo ------------------------------------------------------------------
                float microAggregate = lerp(0.5, NycNoise2D(float2(wx * 16.0, wz * 16.0)),
                                            smoothstep(0.06, 0.02, pixelSize));
                float fineGrain = lerp(0.5, NycNoise2D(float2(wx * 3.2, wz * 3.2)),
                                       smoothstep(0.4, 0.12, pixelSize));
                float patchNoise = NycNoise2D(float2(wx * 0.18, wz * 0.18));
                float oilyStain = smoothstep(0.52, 0.84, NycNoise2D(float2(wx * 0.42, wz * 0.42))) * detail;
                float tarSeam = (1.0 - smoothstep(0.0, 0.04, abs(patchNoise - 0.52))) * 0.42 * detail;

                float3 baseAsphalt = lerp(_AsphaltDark.rgb, _AsphaltLight.rgb,
                                          fineGrain * 0.36 + microAggregate * 0.22 + patchNoise * 0.26)
                                   * (1.0 - oilyStain * 0.32) * (1.0 - tarSeam);

                float2 mhCell = floor(float2(wx, wz) / 14.0);
                float2 mhCentre = (mhCell + float2(NycHash2D(mhCell + 13.7), NycHash2D(mhCell + 29.1)) * 0.6 + 0.2) * 14.0;
                float mhDist = length(float2(wx, wz) - mhCentre);
                float isManhole = (1.0 - smoothstep(0.52, 0.60, mhDist))
                                * step(0.66, NycHash2D(mhCell)) * detail;
                float mhRings = sin(mhDist * 36.0) * 0.15 + 0.85;

                float wetDarkening = lerp(lerp(1.0, 0.72, damp), 0.6, puddle) * lerp(1.0, 0.8, film);
                float3 albedo = lerp(baseAsphalt, _ManholeColor.rgb * mhRings, isManhole) * wetDarkening;

                // --- roughness / metalness ---------------------------------------------------
                float microRough = lerp(0.04, NycNoise2D(positionWS.xz * 12.0) * 0.08,
                                        smoothstep(0.08, 0.03, pixelSize));
                float wetRough = lerp(0.90 + microRough, 0.38, max(film * 0.9, damp * 0.8));
                float roughness = lerp(wetRough, 0.10, puddle);
                float metallic = saturate(waterFresnel * 1.6) * max(puddle, film * 0.6);
                metallic = min(metallic, 0.85);

                // --- normal ------------------------------------------------------------------
                float coarseFade = smoothstep(0.08, 0.025, pixelSize);
                float fineFade = smoothstep(0.02, 0.006, pixelSize) * (1.0 - film * 0.6);
                float coarseGrit = (NycNoise2D(float2(wx * 8.5, wz * 8.5)) - 0.5) * 0.0032 * coarseFade;
                float fineGrit = (NycNoise2D(float2(wx * 36.0, wz * 36.0)) - 0.5) * 0.0016 * fineFade;
                float height = (coarseGrit + fineGrit) * (1.0 - puddle) * _BumpScale;
                float3 bumpedNormal = NycBumpNormal(height, positionWS, normalWS);

                // --- emission: sky + Times Square neon on the wet film -----------------------
                float roadFresnel = pow(saturate(1.0 - abs(viewDir.y)), 2.3);
                float3 skyMirror = lerp(_NycSkyHorizonColor.rgb, _NycSkyTopColor.rgb, 0.40) * 0.10;
                float tsDist = saturate(1.0 - length(float2(wx + 212.0, wz + 45.0)) / 145.0);
                float neonPatch = NycNoise2D(float2(wx * 0.15, wz * 0.15));
                float3 neonCol = lerp(float3(1.0, 0.16, 0.52), float3(0.16, 0.83, 1.0), neonPatch) * 0.32;
                float3 reflectCol = lerp(skyMirror, neonCol, tsDist * 0.65);
                float3 emission = reflectCol * max(puddle, film * 0.35) * roadFresnel
                                * _NycInteriorGlow * 0.07;

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = bumpedNormal;
                inputData.viewDirectionWS = viewDir;
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(bumpedNormal);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half4 color = UniversalFragmentPBR(inputData, albedo, metallic, half3(0, 0, 0),
                                                   roughness, NycCanyonAO(positionWS), emission, 1.0h);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            #include "Includes/NycPasses.hlsl"

            NycDepthVaryings ShadowVert(NycDepthAttributes input)
            {
                NycDepthVaryings output = (NycDepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = NycShadowPositionHClip(positions.positionWS, normalWS);
                output.normalWS = normalWS;
                return output;
            }

            half4 ShadowFrag(NycDepthVaryings input) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex NycDepthVertex
            #pragma fragment NycDepthOnlyFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/NycPasses.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
