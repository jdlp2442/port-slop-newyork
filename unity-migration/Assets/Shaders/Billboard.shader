Shader "Nyc/Billboard"
{
    Properties
    {
        _ThemeIndex ("Theme Index (0..5)", Float) = 0
        _BaseColor ("Poster Tint", Color) = (1, 1, 1, 1)
        _EmissionMultiplier ("Emission Multiplier", Range(0.5, 6.0)) = 2.4
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry+15"
        }

        Pass
        {
            Name "UnlitBillboard"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Includes/NycCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ThemeIndex;
                float4 _BaseColor;
                float _EmissionMultiplier;
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
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            // Synthesizes the procedural poster graphics matching createBillboardCanvasTexture()
            float3 EvaluatePosterGraphic(float2 uv, int key)
            {
                float3 col = float3(0.05, 0.05, 0.05);

                if (key == 0)
                {
                    // Theme 0: Broadway Musical Marquee (Metropolis / Phantom)
                    float gradY = uv.y;
                    float3 bg = lerp(float3(0.16, 0.02, 0.07), float3(0.35, 0.04, 0.13), sin(gradY * 3.14159));
                    float frame = step(0.04, uv.x) * step(uv.x, 0.96) * step(0.08, uv.y) * step(uv.y, 0.92);
                    float innerFrame = step(0.06, uv.x) * step(uv.x, 0.94) * step(0.12, uv.y) * step(uv.y, 0.88);
                    float border = frame - innerFrame;

                    float headline = step(0.55, uv.y) * step(uv.y, 0.75) * (sin(uv.x * 80.0) > 0.0 ? 1.0 : 0.2);
                    float subhead = step(0.35, uv.y) * step(uv.y, 0.48) * (sin(uv.x * 120.0) > 0.0 ? 0.9 : 0.1);

                    col = bg + border * float3(1.0, 0.81, 0.35) * 1.5;
                    col += headline * float3(1.0, 0.95, 0.85) * 1.8;
                    col += subhead * float3(1.0, 0.78, 0.27) * 1.4;
                }
                else if (key == 1)
                {
                    // Theme 1: NASDAQ / Times Square MarketSite Financial Display
                    float3 bg = lerp(float3(0.015, 0.07, 0.17), float3(0.03, 0.15, 0.31), uv.y);
                    // Header bar
                    float isHeader = step(0.82, uv.y);
                    // Chart line & area
                    float chartX = uv.x;
                    float curve = 0.25 + 0.35 * (chartX * chartX) + sin(chartX * 14.0) * 0.04;
                    float inChart = step(0.18, uv.y) * step(uv.y, curve);
                    float chartLine = smoothstep(0.015, 0.0, abs(uv.y - curve));

                    col = bg;
                    col = lerp(col, float3(0.0, 0.32, 0.80), isHeader);
                    col += inChart * float3(0.0, 0.75, 0.45) * 0.4;
                    col += chartLine * float3(0.0, 0.96, 0.62) * 2.2;
                }
                else if (key == 2)
                {
                    // Theme 2: Gotham Cola / Red & White Dynamic Wave
                    float3 bg = lerp(float3(0.62, 0.02, 0.10), float3(0.85, 0.06, 0.16), uv.y);
                    float waveCenter = 0.55 + sin(uv.x * 6.28318) * 0.14;
                    float wave = smoothstep(0.08, 0.0, abs(uv.y - waveCenter));
                    float text = step(0.48, uv.y) * step(uv.y, 0.65) * (sin(uv.x * 90.0) > 0.0 ? 1.0 : 0.1);

                    col = bg + wave * float3(1.0, 1.0, 1.0) * 1.6;
                    col += text * float3(1.0, 0.95, 0.80) * 1.5;
                }
                else if (key == 3)
                {
                    // Theme 3: Fifth Avenue Luxury Watch & Couture (Chrono Genève)
                    float distCenter = length(uv - float2(0.24, 0.5));
                    float dial = smoothstep(0.22, 0.20, distCenter);
                    float dialRing = smoothstep(0.015, 0.0, abs(distCenter - 0.20));
                    float textRight = step(0.46, uv.x) * step(0.35, uv.y) * step(uv.y, 0.68)
                                    * (sin(uv.y * 50.0 + uv.x * 60.0) > 0.0 ? 0.9 : 0.15);

                    col = float3(0.04, 0.03, 0.025);
                    col += dial * float3(0.08, 0.07, 0.06);
                    col += dialRing * float3(0.95, 0.82, 0.45) * 2.0;
                    col += textRight * float3(0.92, 0.80, 0.55) * 1.5;
                }
                else if (key == 4)
                {
                    // Theme 4: NY1 Live Skyline News & Weather Broadcast
                    float3 bg = lerp(float3(0.04, 0.13, 0.27), float3(0.11, 0.34, 0.62), uv.y);
                    float isBreaking = step(0.60, uv.y) * step(uv.y, 0.72) * step(uv.x, 0.42);
                    float chyron = step(0.08, uv.y) * step(uv.y, 0.24);

                    col = bg;
                    col = lerp(col, float3(0.90, 0.10, 0.18) * 1.8, isBreaking);
                    col = lerp(col, float3(0.95, 0.97, 1.0), chyron);
                }
                else
                {
                    // Theme 5: Times Square Neon Nights & TKTS Booth
                    float3 bg = lerp(float3(0.12, 0.02, 0.22), float3(0.85, 0.11, 0.38), uv.x * 0.7 + uv.y * 0.3);
                    float sunDist = length(uv - float2(0.5, 0.55));
                    float neonSun = smoothstep(0.26, 0.24, sunDist);
                    float tktsBar = step(0.06, uv.y) * step(uv.y, 0.22);

                    col = bg + neonSun * float3(1.0, 0.75, 0.12) * 2.2;
                    col = lerp(col, float3(1.0, 0.25, 0.51) * 1.6, tktsBar);
                }

                return col;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 p = input.uv;
                float t = _NycTime;
                int key = ((int)round(_ThemeIndex) % 6 + 6) % 6;

                float3 basePoster = EvaluatePosterGraphic(p, key) * _BaseColor.rgb;

                // Fine RGB LED matrix pitch & cabinet bezel border
                float ledScanX = sin(p.x * 360.0) * 0.035 + 0.965;
                float ledScanY = sin(p.y * 200.0) * 0.045 + 0.955;
                float borderMask = smoothstep(0.0, 0.018, p.x)
                                 * smoothstep(0.0, 0.018, p.y)
                                 * (1.0 - smoothstep(0.982, 1.0, p.x))
                                 * (1.0 - smoothstep(0.982, 1.0, p.y));

                // Animated stage spotlight sweep & scrolling LED ticker pulse
                float sweepPos = sin(t * (1.15 + (float)key * 0.18)) * 0.38 + 0.5;
                float spotlightSweep = pow(saturate(1.0 - abs(p.x - sweepPos) * 2.4), 3.0) * 0.28;
                float tickerBand = 1.0 - step(0.20, p.y);
                float tickerPulse = smoothstep(0.2, 0.8, sin(p.x * 42.0 + t * 5.2)) * tickerBand * 0.22;

                float3 animatedCol = basePoster * (1.18 + spotlightSweep) + basePoster * tickerPulse;
                float3 finalCol = animatedCol * ledScanX * ledScanY * borderMask * _EmissionMultiplier;

                return half4(finalCol, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}