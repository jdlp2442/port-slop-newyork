// ---------------------------------------------------------------------------------------------
// NYC pre-war masonry facade (limestone, terracotta, running-bond brick, brownstone).
// Port of createPreWarBuildingMaterial() from js/tsl-materials.js.
//
// Reads the baked facade attributes:
//   uv0 = (metres along the wall, world Y)   uv1 = (wall width, box height)
//   uv2 = (wall kind, tone)                  uv3 = (box top Y, style)
// ---------------------------------------------------------------------------------------------
Shader "Nyc/Facade"
{
    Properties
    {
        [Header(Palette)]
        _StoneColor        ("Stone / brick", Color) = (0.847, 0.804, 0.722, 1)
        _TrimColor         ("Trim stone", Color)    = (0.620, 0.576, 0.498, 1)
        _GlassColor        ("Window glass", Color)  = (0.086, 0.141, 0.196, 1)
        _CrownColor        ("Crown flood", Color)   = (1.0, 0.878, 0.639, 1)

        [Header(Grid)]
        _WindowWidth       ("Window width (m)", Float) = 2.6
        _FloorHeight       ("Floor height (m)", Float) = 3.7
        _WindowRatioX      ("Window width ratio", Range(0.2, 0.9)) = 0.5
        _WindowRatioY      ("Window height ratio", Range(0.2, 0.9)) = 0.58
        _CornerPier        ("Corner pier (m)", Float) = 1.2

        [Header(Lighting)]
        _LitProbability    ("Lit windows", Range(0, 1)) = 0.54
        _Warmth            ("Warmth", Range(0, 1)) = 0.8
        _CrownGlowHeight   ("Crown flood height (m)", Float) = 9999
        _BumpScale         ("Relief scale", Range(0, 3)) = 1
        _InteriorFade      ("Interior detail distance", Float) = 140
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
            #include "Includes/NycFacade.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _StoneColor;
                float4 _TrimColor;
                float4 _GlassColor;
                float4 _CrownColor;
                float  _WindowWidth;
                float  _FloorHeight;
                float  _WindowRatioX;
                float  _WindowRatioY;
                float  _CornerPier;
                float  _LitProbability;
                float  _Warmth;
                float  _CrownGlowHeight;
                float  _BumpScale;
                float  _InteriorFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 uv0        : TEXCOORD0;
                float4 uv1        : TEXCOORD1;
                float4 uv2        : TEXCOORD2;
                float4 uv3        : TEXCOORD3;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 uv0        : TEXCOORD2;
                float4 uv1        : TEXCOORD3;
                float4 uv2        : TEXCOORD4;
                float4 uv3        : TEXCOORD5;
                float4 color      : TEXCOORD6;
                float  fogCoord   : TEXCOORD7;
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
                output.uv0 = input.uv0;
                output.uv1 = input.uv1;
                output.uv2 = input.uv2;
                output.uv3 = input.uv3;
                output.color = input.color;
                output.fogCoord = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = input.positionWS;
                float3 normalWS = normalize(input.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(positionWS);

                NycFacadeCell cell = NycBuildFacadeCell(
                    positionWS, normalWS,
                    input.uv0.xy, input.uv1.xy, input.uv2.xy, input.uv3.xy, input.color,
                    _WindowWidth, _FloorHeight, _CornerPier);

                NycPrewarParams p;
                p.stoneColor     = _StoneColor.rgb;
                p.trimColor      = _TrimColor.rgb;
                p.glassColor     = _GlassColor.rgb;
                p.crownColor     = _CrownColor.rgb;
                p.windowWidth    = _WindowWidth;
                p.floorHeight    = _FloorHeight;
                p.windowRatioX   = _WindowRatioX;
                p.windowRatioY   = _WindowRatioY;
                p.litProbability = _LitProbability;
                p.warmth         = _Warmth;
                p.cornerPier     = _CornerPier;
                p.crownGlowHeight = _CrownGlowHeight;

                float canyonBand = smoothstep(0.0, 14.0, positionWS.y)
                                 * (1.0 - smoothstep(22.0, 75.0, positionWS.y));
                float3 canyonGlow = float3(0.91, 0.61, 0.41) * canyonBand * _NycCanyonBounce * 0.22;

                NycSurface s;
                if (input.uv3.y >= 2.5)
                {
                    NycCurtainParams cp;
                    cp.glassTint        = _GlassColor.rgb;
                    cp.frameTint        = _TrimColor.rgb;
                    cp.windowWidth      = _WindowWidth;
                    cp.floorHeight      = _FloorHeight;
                    cp.mullionThickness = 0.06;
                    cp.spandrelRatio    = 0.32;
                    cp.litProbability   = _LitProbability;
                    cp.warmth           = _Warmth;
                    cp.reflectivity     = 0.75;
                    s = NycEvaluateCurtain(cell, positionWS, normalWS, _WorldSpaceCameraPos, cp, canyonGlow);
                }
                else
                {
                    s = NycEvaluatePrewar(cell, positionWS, normalWS, _WorldSpaceCameraPos, p, canyonGlow);
                }

                float camDist = distance(positionWS, _WorldSpaceCameraPos);
                float detailFade = (1.0 - smoothstep(_InteriorFade * 0.7, _InteriorFade, camDist))
                                 * (1.0 - cell.isRoof);
                float3 bumpedNormal = NycBumpNormal(s.height * _BumpScale * detailFade, positionWS, normalWS);

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = bumpedNormal;
                inputData.viewDirectionWS = viewDir;
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(bumpedNormal);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half smoothness = saturate(1.0h - s.roughness);
                half4 color = UniversalFragmentPBR(inputData, s.albedo, s.metallic, half3(0, 0, 0),
                                                   smoothness, s.ao, s.emission, 1.0h);
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

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex NycDepthVertex
            #pragma fragment NycDepthNormalsFragment
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Includes/NycPasses.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
