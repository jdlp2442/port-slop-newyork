Shader "Nyc/LandmarkFacade"
{
    Properties
    {
        [Header(Materials)]
        _StoneColor        ("Stone / Frame Color", Color) = (0.90, 0.88, 0.83, 1)
        _GlassColor        ("Glass Color", Color)         = (0.08, 0.14, 0.22, 1)
        _NightLightColor   ("Interior Light Color", Color)= (1.00, 0.82, 0.52, 1)

        [Header(Window Grid)]
        _WindowWidth       ("Window Width (m)", Float)    = 2.8
        _FloorHeight       ("Floor Height (m)", Float)    = 3.8
        _WindowRatioX      ("Window Width Ratio", Range(0.1, 0.95))  = 0.62
        _WindowRatioY      ("Window Height Ratio", Range(0.1, 0.95)) = 0.68
        _PierWidth         ("Corner Pier Width (m)", Float) = 0.5

        [Header(PBR)]
        _Smoothness        ("Smoothness (Glass)", Range(0, 1)) = 0.88
        _StoneSmoothness   ("Smoothness (Stone)", Range(0, 1)) = 0.35
        _Metallic          ("Metallic", Range(0, 1))           = 0.15
        _NightLitProb      ("Night Lit Windows", Range(0, 1)) = 0.62
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
                float4 _StoneColor;
                float4 _GlassColor;
                float4 _NightLightColor;
                float  _WindowWidth;
                float  _FloorHeight;
                float  _WindowRatioX;
                float  _WindowRatioY;
                float  _PierWidth;
                float  _Smoothness;
                float  _StoneSmoothness;
                float  _Metallic;
                float  _NightLitProb;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 uv0        : TEXCOORD0;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 uv0        : TEXCOORD2;
                float4 color      : TEXCOORD3;
                float  fogCoord   : TEXCOORD4;
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

                // Horizontal vs Vertical faces
                float isUp = abs(normalWS.y);
                float isRoof = step(0.65, isUp);

                // Facade coordinates: use wall normal to choose planar axis
                float u = abs(normalWS.x) > abs(normalWS.z) ? positionWS.z : positionWS.x;
                float v = positionWS.y;

                float winW = max(0.5, _WindowWidth);
                float flrH = max(0.5, _FloorHeight);

                float cellU = frac(u / winW);
                float cellV = frac(v / flrH);
                float2 cellId = float2(floor(u / winW), floor(v / flrH));

                float halfMarginX = (1.0 - _WindowRatioX) * 0.5;
                float halfMarginY = (1.0 - _WindowRatioY) * 0.5;

                // Window aperture mask with anti-aliased edges
                float inWinX = smoothstep(halfMarginX - 0.02, halfMarginX + 0.02, cellU) *
                               (1.0 - smoothstep(1.0 - halfMarginX - 0.02, 1.0 - halfMarginX + 0.02, cellU));
                float inWinY = smoothstep(halfMarginY - 0.02, halfMarginY + 0.02, cellV) *
                               (1.0 - smoothstep(1.0 - halfMarginY - 0.02, 1.0 - halfMarginY + 0.02, cellV));
                float isWindow = inWinX * inWinY * (1.0 - isRoof);

                // 3D Parallax interior room raymarching (isometric room box behind facade aperture)
                float alongZ = step(abs(normalWS.z), abs(normalWS.x));
                float tSign = lerp(-sign(normalWS.z), sign(normalWS.x), alongZ);
                float3 tangent = float3(-normalWS.z, 0.0, normalWS.x) * tSign;
                float3 rayWorld = -viewDir;
                float dirX = dot(rayWorld, tangent);
                float dirY = rayWorld.y;
                float dirZ = max(abs(dot(rayWorld, normalWS)), 0.05);

                float winInnerU = saturate((cellU - halfMarginX) / max(0.01, 1.0 - 2.0 * halfMarginX));
                float winInnerV = saturate((cellV - halfMarginY) / max(0.01, 1.0 - 2.0 * halfMarginY));

                float roomW = winW * 2.0;
                float roomH = flrH * 0.92;
                float roomDepth = roomH * 1.5;
                float originX = (frac(u / roomW) - 0.5) * roomW;
                float originY = (winInnerV - 0.5) * (roomH * 0.65);

                float halfW = roomW * 0.5;
                float halfH = roomH * 0.5;
                float tFarX = (halfW - originX * sign(dirX)) / max(abs(dirX), 0.0001);
                float tFarY = (halfH - originY * sign(dirY)) / max(abs(dirY), 0.0001);
                float tFarZ = roomDepth / dirZ;
                float tRoom = min(min(tFarX, tFarY), tFarZ);
                float3 hitRoom = float3(originX + dirX * tRoom, originY + dirY * tRoom, dirZ * tRoom);
                float3 qRoom = saturate(float3(hitRoom.x / roomW + 0.5, hitRoom.y / roomH + 0.5, hitRoom.z / roomDepth));

                float onBack = step(tFarZ, tFarX) * step(tFarZ, tFarY);
                float onCeil = step(tFarY, tFarX) * step(tFarY, tFarZ) * step(0.0, dirY);
                float onFloor = step(tFarY, tFarX) * step(tFarY, tFarZ) * (1.0 - step(0.0, dirY));

                // Office troffer lighting panels & partition walls
                float ceilLight = smoothstep(0.18, 0.12, abs(frac(qRoom.x * 3.0) - 0.5)) *
                                  smoothstep(0.20, 0.14, abs(frac(qRoom.z * 3.0) - 0.5));
                float3 roomLightCol = lerp(_NightLightColor.rgb, float3(0.82, 0.90, 1.0), frac(cellId.x * 0.37));
                float3 roomShell = lerp(float3(0.42, 0.44, 0.46), float3(0.88, 0.86, 0.82), onCeil * (ceilLight * 1.4 + 0.3));
                roomShell = lerp(roomShell, float3(0.25, 0.23, 0.20), onFloor);
                float3 roomInterior = roomShell * (1.0 - qRoom.z * 0.45);

                // Reveal / shadow inside window opening
                float reveal = 1.0 - smoothstep(halfMarginX, halfMarginX + 0.06, cellU) * 0.25 -
                                     (1.0 - smoothstep(1.0 - halfMarginY - 0.08, 1.0 - halfMarginY, cellV)) * 0.35;

                // Night window lighting
                float nightFactor = saturate((_NycInteriorGlow - 0.45) * 1.6);
                float randWindow = frac(sin(dot(cellId, float2(12.9898, 78.233))) * 43758.5453);
                float isLit = step(1.0 - _NightLitProb, randWindow) * step(0.05, nightFactor);
                float3 roomGlow = roomLightCol * (0.8 + 0.4 * frac(randWindow * 7.1)) * isLit * nightFactor * 2.2;

                // Fresnel sky reflection on glass
                float fresnel = pow(saturate(1.0 - dot(viewDir, normalWS)), 3.0);
                float3 reflectedSky = _NycSkyHorizonColor.rgb * (fresnel * 0.75 + 0.15);

                float3 glazedInterior = lerp(roomInterior * float3(0.75, 0.82, 0.80), _GlassColor.rgb, 0.32);
                float3 glassCol = lerp(glazedInterior * reveal, reflectedSky, fresnel * 0.8 + 0.2);
                float3 stoneCol = _StoneColor.rgb;

                // Rich landmark roof: tar seams, gravel ballast & ponding water
                float rollU = positionWS.x * 0.50;
                float seamU = 1.0 - smoothstep(0.0, 0.06, abs(frac(rollU) - 0.5) * 2.0);
                float rollV = positionWS.z * 0.16;
                float seamV = 1.0 - smoothstep(0.0, 0.05, abs(frac(rollV) - 0.5) * 2.0);
                float tarSeams = saturate(seamU * 0.85 + seamV * 0.45);
                float puddle = smoothstep(0.62, 0.76, NycNoise2D(positionWS.xz * 0.08));
                float3 roofCol = lerp(_StoneColor.rgb * 0.55, float3(0.07, 0.07, 0.08), tarSeams * 0.65);
                roofCol = lerp(roofCol, float3(0.035, 0.045, 0.055), puddle * 0.85);

                float3 albedo = lerp(stoneCol, roofCol, isRoof);
                albedo = lerp(albedo, glassCol, isWindow);

                float3 emission = isWindow * roomGlow;
                half roughness = lerp(1.0h - _StoneSmoothness, 1.0h - _Smoothness, isWindow);
                roughness = lerp(roughness, lerp(0.85h, 0.06h, puddle), isRoof);
                half smoothness = saturate(1.0h - roughness);
                half metallic = lerp(_Metallic * 0.2h, _Metallic, isWindow);

                half3 specular = lerp(kDielectricSpec.rgb, albedo, metallic);

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = normalWS;
                inputData.viewDirectionWS = viewDir;
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half4 color = UniversalFragmentPBR(inputData, albedo, metallic, specular,
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
                output.positionCS = positions.positionCS;
                return output;
            }

            half4 ShadowFrag(NycDepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
