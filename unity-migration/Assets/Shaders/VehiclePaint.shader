// ---------------------------------------------------------------------------------------------
// Instanced vehicle paint. One draw per batch via Graphics.RenderMeshInstanced.
// uv1.x carries the part id written by VehicleMeshFactory:
//   0 body · 1 glass · 2 headlight · 3 taillight · 4 tyre
// Headlights and taillights become emissive as the interior glow (night) rises.
// ---------------------------------------------------------------------------------------------
Shader "Nyc/VehiclePaint"
{
    Properties
    {
        _BaseColor ("Paint", Color) = (1.0, 0.745, 0.078, 1)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.62
        _Metallic ("Metallic", Range(0, 1)) = 0.35
        _HeadlightBoost ("Headlight emission", Float) = 4.0
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
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/NycCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Smoothness;
                float  _Metallic;
                float  _HeadlightBoost;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv1        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  part       : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
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
                output.part = input.uv1.x;
                output.fogCoord = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float part = input.part;
                float3 normalWS = normalize(input.normalWS);

                float3 albedo;
                float smoothness;
                float metallic;
                float3 emission = 0;

                float night = saturate((_NycInteriorGlow - 0.45) * 1.6);

                if (part < 0.5)                  // body
                {
                    albedo = _BaseColor.rgb;
                    smoothness = _Smoothness;
                    metallic = _Metallic;
                }
                else if (part < 1.5)             // glass
                {
                    albedo = float3(0.055, 0.075, 0.095);
                    smoothness = 0.94;
                    metallic = 0.10;
                }
                else if (part < 2.5)             // headlight
                {
                    albedo = float3(0.85, 0.85, 0.8);
                    smoothness = 0.85;
                    metallic = 0.0;
                    emission = float3(1.0, 0.92, 0.78) * night * _HeadlightBoost;
                }
                else if (part < 3.5)             // taillight
                {
                    albedo = float3(0.35, 0.03, 0.03);
                    smoothness = 0.80;
                    metallic = 0.0;
                    emission = float3(1.0, 0.07, 0.04) * (0.25 + night * 2.2);
                }
                else                              // tyre
                {
                    albedo = float3(0.025, 0.025, 0.028);
                    smoothness = 0.18;
                    metallic = 0.0;
                }

                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half4 color = UniversalFragmentPBR(inputData, albedo, metallic, half3(0, 0, 0),
                                                   smoothness, 1.0h, emission, 1.0h);
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
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
