// ---------------------------------------------------------------------------------------------
// Hudson / East River / harbour water + Central Park lakes.
// Port of createWaterMaterial() from js/tsl-materials.js, with the FFT slope texture replaced by
// four rotated procedural noise-gradient layers (same layer scales, RMS slopes and speeds).
//
// Day:  body colour, sky IBL, sun GGX and SSR carry the look.
// Night: an analytic lit skyline is reflected along the refracted ray so the rivers keep the
//        vertical light streaks that screen-space reflections cannot reach.
// ---------------------------------------------------------------------------------------------
Shader "Nyc/Water"
{
    Properties
    {
        _ShallowColor ("Estuary body", Color) = (0.086, 0.176, 0.172, 1)
        _DeepColor    ("Estuary silt", Color) = (0.180, 0.247, 0.212, 1)
        _LakeColor    ("Lake body", Color)    = (0.141, 0.196, 0.122, 1)
        _WaveStrength ("Wave strength", Range(0, 2)) = 1
        _WaveScale    ("Wave scale", Range(0.25, 4)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+10"
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
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _LakeColor;
                float  _WaveStrength;
                float  _WaveScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
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
                output.fogCoord = ComputeFogFactor(positions.positionCS.y);
                return output;
            }

            float2 NycNoiseGrad(float2 p)
            {
                const float e = 0.35;
                float n0 = NycNoise2D(p);
                float nx = NycNoise2D(p + float2(e, 0.0));
                float ny = NycNoise2D(p + float2(0.0, e));
                return float2(nx - n0, ny - n0) / e;
            }

            // One rotated, scrolling wave layer -> slope contribution.
            float2 WaveLayer(float2 p, float tile, float rot, float rms, float t, out float variance)
            {
                float c = cos(rot);
                float s = sin(rot);
                float2 q = float2(p.x * c - p.y * s, p.x * s + p.y * c) / tile;
                float speed = 0.8 * sqrt(tile / 9.0) / tile;
                q += float2(cos(0.3), sin(0.3)) * speed * t;
                float2 g = NycNoiseGrad(q);
                float2 slope = float2(g.x * c + g.y * s, g.y * c - g.x * s) * rms;
                variance = rms * rms * 0.0022;
                return slope;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 positionWS = input.positionWS;
                float3 geometricNormal = normalize(input.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(positionWS);
                float t = _NycTime;

                float pond = smoothstep(-1.2, -0.2, positionWS.y);
                float2 p = positionWS.xz * lerp(1.0, 2.6, pond) * _WaveScale;

                // Wind patches drifting with the breeze.
                float gustA = NycNoise2D(p * 0.0047 + float2(t * 0.011, t * -0.007));
                float gustB = NycNoise2D(p * 0.0135 + float2(t * -0.009, t * 0.013) + 31.7);
                float gust = smoothstep(0.18, 0.82, gustA * 0.62 + gustB * 0.38);
                float energy = lerp(0.45, 1.3, gust) * lerp(1.0, 0.42, pond) * _WaveStrength;

                float variance = energy * energy * 0.0022;
                float v;
                float2 slope = 0.0;
                slope += WaveLayer(p, 61.0, 0.00, 0.052, t, v); variance += v;
                slope += WaveLayer(p, 23.3, 1.93, 0.060, t, v); variance += v;
                slope += WaveLayer(p, 8.70, 4.07, 0.064, t, v); variance += v;
                slope += WaveLayer(p, 3.30, 2.71, 0.064, t, v); variance += v;
                slope *= energy;

                float flatTop = smoothstep(0.5, 0.9, geometricNormal.y);
                float3 waterNormal = normalize(lerp(geometricNormal,
                                                   float3(-slope.x, 1.0, -slope.y), flatTop));

                float roughness = clamp(pow(variance, 0.25) + pond * 0.05, 0.08, 0.65);
                float fresnel = pow(1.0 - saturate(dot(waterNormal, viewDir)), 5.0) * 0.98 + 0.02;
                float metallic = min(saturate(fresnel * 1.6), 0.9);

                float silt = NycNoise2D(positionWS.xz * 0.0031 + 7.3);
                float3 body = lerp(_ShallowColor.rgb, _DeepColor.rgb, smoothstep(0.25, 0.85, silt));
                float3 lake = _LakeColor.rgb * lerp(0.85, 1.15, silt);
                float3 albedo = lerp(body, lake, pond);

                // --- night skyline reflected along the mirror ray ----------------------------
                float3 R = reflect(-viewDir, waterNormal);
                float horiz = max(length(R.xz), 0.001);
                float tanEl = R.y / horiz;
                float night = smoothstep(0.55, 1.35, _NycInteriorGlow);

                float2 dir = R.xz / horiz;
                float2 sgn = float2(dir.x >= 0.0 ? 1.0 : -1.0, dir.y >= 0.0 ? 1.0 : -1.0);
                float2 inv = sgn / max(abs(dir), float2(0.0001, 0.0001));

                // Slab test against Manhattan's bulkhead bbox.
                float2 bmin = float2(_NycIslandBounds.x, _NycIslandBounds.y);
                float2 bmax = float2(_NycIslandBounds.z, _NycIslandBounds.w);
                float2 ta = (bmin - positionWS.xz) * inv;
                float2 tb = (bmax - positionWS.xz) * inv;
                float tn = max(min(ta.x, tb.x), min(ta.y, tb.y));
                float tf = min(max(ta.x, tb.x), max(ta.y, tb.y));
                float tHit = (tn >= 0.0 && tn <= tf) ? tn : 1e5;

                float2 hit = positionWS.xz + dir * tHit;
                float hRefl = tHit * tanEl;
                float u = hit.x + hit.y;

                float sigma2 = sqrt(variance) * 2.0;
                float sV = tHit * sigma2 + fwidth(hRefl) + 0.5;
                float sU = tHit * sigma2 * max(viewDir.y, 0.05) + fwidth(u) + 0.3;

                // Lots 24 m wide, 3.2 m window bays, 3.6 m floors.
                float lot = floor(u / 24.0);
                float r1 = NycHash2D(float2(lot, 5.1));
                float r2 = NycHash2D(float2(lot, 9.7));
                float r3 = NycHash2D(float2(lot, 13.3));

                float S = lerp(70.0, 215.0, smoothstep(0.0, 1.0, r1));
                float upper = smoothstep(sV * 0.5, sV * 2.0, S - hRefl);
                float ground = smoothstep(sV * 0.5, sV * 2.0, 4.5 - hRefl);

                float dens = lerp(0.2, 0.6, r2);
                float bayF = u / 3.2;
                float floorF = hRefl / 3.6;
                float litWin = step(NycHash2D(floor(float2(bayF, floorF)) + lot * 0.731), dens);
                float pane = NycFilteredPulse(frac(bayF), 0.16, 0.84, sU / 3.2)
                           * NycFilteredPulse(frac(floorF), 0.3, 0.88, sV / 3.6);
                float winDetail = 1.0 - smoothstep(0.7, 1.8, max(sU / 3.2, sV / 3.6));
                float windows = lerp(litWin * pane, dens * 0.394, 1.0 - winDetail);

                float3 tint = lerp(lerp(float3(1.0, 0.77, 0.54), float3(0.87, 0.90, 1.0), step(0.62, r3)),
                                   float3(0.96, 0.83, 0.67), saturate(sU / 40.0));
                float shopLit = lerp(step(0.35, r3), 0.7, saturate(sU / 40.0));

                float3 city = tint * windows * upper * 0.95
                            + float3(1.0, 0.81, 0.60) * ground * shopLit * 0.4;

                float region = smoothstep(_NycIslandBounds.y - 400.0, _NycIslandBounds.y - 100.0, positionWS.z)
                             * (1.0 - smoothstep(_NycIslandBounds.w + 200.0, _NycIslandBounds.w + 600.0, positionWS.z));

                float3 emission = city * fresnel * night * region * step(tHit, 1e4);

                InputData inputData = (InputData)0;
                inputData.positionWS = positionWS;
                inputData.normalWS = waterNormal;
                inputData.viewDirectionWS = viewDir;
                inputData.shadowCoord = TransformWorldToShadowCoord(positionWS);
                inputData.fogCoord = input.fogCoord;
                inputData.bakedGI = SampleSH(waterNormal);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);

                half smoothness = saturate(1.0h - roughness);
                half4 color = UniversalFragmentPBR(inputData, albedo, metallic, half3(0, 0, 0),
                                                   smoothness, 1.0h, emission, 1.0h);
                color.rgb = MixFog(color.rgb, inputData.fogCoord);
                return color;
            }
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
