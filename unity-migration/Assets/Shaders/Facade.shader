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

                int style = (int)round(input.uv3.y);
                float tone = input.uv2.y;

                float winW = _WindowWidth;
                float flrH = _FloorHeight;
                float pier = _CornerPier;

                if (style == 1)      { winW = 2.4; flrH = 3.5; }
                else if (style == 2) { winW = 2.2; flrH = 3.4; }
                else if (style == 0) { winW = 2.6; flrH = 3.7; }
                else if (style == 3) { winW = 2.7; flrH = 3.9; pier = 0.0; }
                else if (style == 4) { winW = 2.5; flrH = 3.8; pier = 0.0; }
                else if (style == 5) { winW = 2.9; flrH = 4.1; pier = 0.0; }

                NycFacadeCell cell = NycBuildFacadeCell(
                    positionWS, normalWS,
                    input.uv0.xy, input.uv1.xy, input.uv2.xy, input.uv3.xy, input.color,
                    winW, flrH, pier);

                float canyonBand = smoothstep(0.0, 14.0, positionWS.y)
                                 * (1.0 - smoothstep(22.0, 75.0, positionWS.y));
                float3 canyonGlow = float3(0.91, 0.61, 0.41) * canyonBand * _NycCanyonBounce * 0.22;

                // Per-building distinct character, weathering, and architectural variation
                float bldSeed = input.color.b;
                float wear = frac(bldSeed * 7.13 + 0.31); // 0 = freshly restored stone, 1 = historical patina
                float streetPlinth = (1.0 - smoothstep(0.4, 4.2, positionWS.y)) * 0.18; // urban splash/soot at ground plinth
                float rainSoot = frac(sin(floor(cell.uCoord * 3.0) * 12.9898 + bldSeed * 78.233) * 43758.5453) * 0.08 * wear;
                float totalGrime = wear * 0.15 + streetPlinth + rainSoot;

                NycSurface s;
                if (style >= 3)
                {
                    // Curtain-Wall Skyscraper Glass (Styles 3, 4, 5)
                    NycCurtainParams cp;
                    float3 glassStyle3[3] = {
                        float3(0.010, 0.042, 0.084), // iconic deep teal-blue
                        float3(0.012, 0.052, 0.068), // architectural emerald-cyan
                        float3(0.008, 0.030, 0.072)  // navy evening reflection
                    };
                    cp.glassTint        = glassStyle3[min((int)(bldSeed * 3.0), 2)];
                    cp.frameTint        = float3(0.044, 0.074, 0.105) * lerp(0.85, 1.15, tone); // 0x3b4c5b
                    cp.windowWidth      = winW;
                    cp.floorHeight      = flrH;
                    cp.mullionThickness = 0.065;
                    cp.spandrelRatio    = 0.22;
                    cp.litProbability   = 0.60;
                    cp.warmth           = 0.52;
                    cp.reflectivity     = 0.92;

                    if (style == 4) // Dark Bronze / Charcoal Miesian Glass (Style 4)
                    {
                        float3 glassStyle4[2] = {
                            float3(0.017, 0.015, 0.013), // deep amber bronze
                            float3(0.014, 0.014, 0.016)  // smokey charcoal grey
                        };
                        cp.glassTint = glassStyle4[min((int)(bldSeed * 2.0), 1)];
                        cp.frameTint = float3(0.045, 0.037, 0.031) * lerp(0.85, 1.18, tone); // 0x3c3631
                        cp.mullionThickness = 0.06;
                        cp.spandrelRatio = 0.20;
                        cp.litProbability = 0.64;
                        cp.warmth = 0.72;
                        cp.reflectivity = 0.88;
                    }
                    else if (style == 5) // Silver / Ice-Blue Contemporary Supertall Glass (Style 5)
                    {
                        float3 glassStyle5[3] = {
                            float3(0.019, 0.062, 0.128), // deep ocean vision glass
                            float3(0.024, 0.056, 0.105), // sapphire crystal
                            float3(0.015, 0.070, 0.115)  // ice-cyan platinum
                        };
                        cp.glassTint = glassStyle5[min((int)(bldSeed * 3.0), 2)];
                        cp.frameTint = float3(0.115, 0.145, 0.175) * lerp(0.90, 1.12, tone); // slate-blue anodized aluminum
                        cp.mullionThickness = 0.07;
                        cp.spandrelRatio = 0.18;
                        cp.litProbability = 0.58;
                        cp.warmth = 0.48;
                        cp.reflectivity = 0.95;
                    }
                    s = NycEvaluateCurtain(cell, positionWS, normalWS, _WorldSpaceCameraPos, cp, canyonGlow);
                }
                else
                {
                    // Pre-War Masonry (Styles 0, 1, 2)
                    NycPrewarParams p;
                    float3 stonePalette0[4] = {
                        float3(0.460, 0.412, 0.335), // Indiana Buff Limestone
                        float3(0.505, 0.465, 0.385), // Warm Cream Bedford
                        float3(0.380, 0.355, 0.315), // Manhattan Grey Granite
                        float3(0.425, 0.375, 0.290)  // Golden Sandstone
                    };
                    float3 baseStone0 = stonePalette0[min((int)(bldSeed * 4.0), 3)];
                    p.stoneColor     = baseStone0 * lerp(0.86, 1.14, tone) * (1.0 - totalGrime);
                    p.trimColor      = p.stoneColor * 0.74;
                    p.glassColor     = float3(0.025, 0.040, 0.065); // 0x1a2836
                    p.crownColor     = _CrownColor.rgb;
                    p.windowWidth    = winW;
                    p.floorHeight    = flrH;
                    p.windowRatioX   = 0.50;
                    p.windowRatioY   = 0.58;
                    p.litProbability = 0.54;
                    p.warmth         = 0.80;
                    p.cornerPier     = pier;
                    p.crownGlowHeight = _CrownGlowHeight;

                    if (style == 1) // Historic Warm Red / Terracotta Brick (Style 1)
                    {
                        float3 brickPalette1[3] = {
                            float3(0.350, 0.115, 0.075), // Deep Red Prewar Brick
                            float3(0.415, 0.175, 0.105), // Warm Terracotta Clay
                            float3(0.310, 0.135, 0.090)  // Toasted Dark Red Brick
                        };
                        float3 baseBrick = brickPalette1[min((int)(bldSeed * 3.0), 2)];
                        p.stoneColor = baseBrick * lerp(0.86, 1.14, tone) * (1.0 - totalGrime * 0.9);
                        p.trimColor = float3(0.395, 0.335, 0.255) * (1.0 - wear * 0.10);
                        p.glassColor = float3(0.022, 0.035, 0.055);
                        p.litProbability = 0.56;
                        p.warmth = 0.86;
                    }
                    else if (style == 2) // Classic NYC Brownstone & Dark Masonry (Style 2)
                    {
                        float3 brownPalette2[3] = {
                            float3(0.205, 0.108, 0.070), // Rich Chocolate Brownstone
                            float3(0.245, 0.138, 0.088), // Warm Sienna Brownstone
                            float3(0.165, 0.085, 0.058)  // Dark Patinated Aged Brownstone
                        };
                        float3 baseBrown = brownPalette2[min((int)(bldSeed * 3.0), 2)];
                        p.stoneColor = baseBrown * lerp(0.85, 1.15, tone) * (1.0 - totalGrime * 1.1);
                        p.trimColor = baseBrown * 0.65;
                        p.glassColor = float3(0.020, 0.030, 0.048);
                        p.litProbability = 0.60;
                        p.warmth = 0.90;
                    }
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
                half3 specular = lerp(kDielectricSpec.rgb, s.albedo, s.metallic);
                half4 color = UniversalFragmentPBR(inputData, s.albedo, s.metallic, specular,
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
