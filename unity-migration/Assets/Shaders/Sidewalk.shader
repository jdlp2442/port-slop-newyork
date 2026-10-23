// ---------------------------------------------------------------------------------------------
// NYC concrete sidewalk flags: 1.5 m scored expansion joints, per-flag tone, granite grit and
// post-rain damp patches (drains better than the roadway, so fewer pools).
// Port of createSidewalkMaterial() from js/tsl-materials.js.
// ---------------------------------------------------------------------------------------------
Shader "Nyc/Sidewalk"
{
    Properties
    {
        _ConcreteA  ("Concrete light", Color) = (0.557, 0.553, 0.522, 1)
        _ConcreteB  ("Concrete dark", Color)  = (0.451, 0.447, 0.420, 1)
        _FlagSize   ("Flag size (m)", Float)  = 1.5
        _JointWidth ("Joint width (m)", Range(0.005, 0.12)) = 0.042
        _BumpScale  ("Relief scale", Range(0, 4)) = 1
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
                float4 _ConcreteA;
                float4 _ConcreteB;
                float  _FlagSize;
                float  _JointWidth;
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
                float camDist = distance(positionWS, _WorldSpaceCameraPos);
                float detail = smoothstep(190.0, 16.0, camDist);
                float pixelSize = length(fwidth(positionWS.xz));

                float panel = _FlagSize;
                float panelHash = NycHash2D(floor(float2(wx / panel, wz / panel)));
                float tone = NycNoise2D(float2(wx * 0.48, wz * 0.48));
                float grit = (NycNoise2D(float2(wx * 8.0, wz * 8.0)) - 0.5) * 0.04 * detail
                           * smoothstep(0.08, 0.02, pixelSize);

                float joints = max(NycGridLine(wx, panel, _JointWidth),
                                   NycGridLine(wz, panel, _JointWidth)) * detail;

                // Better-drained surface: +0.06 raises the water line.
                float swField = NycNoise2D(float2(wx * 0.14 + 7.4, wz * 0.14 - 13.1)) * 0.65
                              + NycNoise2D(float2(wx * 0.38, wz * 0.38)) * 0.35;
                float3 swWet = NycWetMasks(swField, 0.06);
                float swPuddle = swWet.y * smoothstep(0.5, 0.9, normalWS.y);

                float3 concrete = lerp(_ConcreteB.rgb, _ConcreteA.rgb, tone)
                                * ((panelHash - 0.5) * 0.16 + 1.0)
                                + grit;
                concrete *= lerp(1.0, 0.72, max(swWet.x, swPuddle)) * lerp(1.0, 0.8, swWet.z);
                concrete *= (1.0 - joints * 0.45);

                float wetRough = lerp(0.90 - panelHash * 0.06, 0.55, max(swWet.z, swWet.x * 0.7));
                float roughness = lerp(wetRough, 0.2, swPuddle);

                float swFresnel = NycWaterFresnel(viewDir);
                float metallic = min(saturate(swFresnel * 1.6) * max(swPuddle, swWet.z * 0.3), 0.85);

                float grain = (NycNoise2D(float2(wx * 3.5, wz * 3.5)) - 0.5) * 0.002 * smoothstep(0.06, 0.015, pixelSize);
                float height = (grain - joints * 0.012) * detail * (1.0 - swPuddle) * _BumpScale;
                float3 bumpedNormal = NycBumpNormal(height, positionWS, normalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = bumpedNormal;
                inputData.viewDirectionWS = viewDir;
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(bumpedNormal);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half smoothness = saturate(1.0h - roughness);
                half4 color = UniversalFragmentPBR(inputData, concrete, metallic, half3(0, 0, 0),
                                                   smoothness, NycCanyonAO(positionWS), 0.0h, 1.0h);
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
