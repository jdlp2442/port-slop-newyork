Shader "Hidden/Nyc/DualThresholdBloom"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
        _BloomTex ("Bloom", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZTest Always ZWrite Off Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct Attributes
        {
            uint vertexID : SV_VertexID;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv         : TEXCOORD0;
        };

        TEXTURE2D(_MainTex);
        SAMPLER(sampler_MainTex);
        TEXTURE2D(_BloomTex);
        SAMPLER(sampler_BloomTex);

        CBUFFER_START(UnityPerMaterial)
            float4 _ThresholdParams; // x: threshold, y: softKnee, z: peakLimit (8.0), w: intensity
            float4 _GradingParams;   // x: exposure, y: contrast, z: saturation, w: vignette
            float4 _ColorBalance;    // rgb: balance, a: unused
        CBUFFER_END

        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
            output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
            return output;
        }
        ENDHLSL

        // Pass 0: Dual-threshold Prefilter (Extract bright emitters with peak limiting)
        Pass
        {
            Name "BloomPrefilter"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragPrefilter
            #pragma target 4.5

            half4 FragPrefilter(Varyings input) : SV_Target
            {
                float4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                // Peak limiter from post-processing.js:
                // max(max(col.r, col.g), col.b)
                float peak = max(max(col.r, col.g), col.b);
                float peakLimit = _ThresholdParams.z > 0.0 ? _ThresholdParams.z : 8.0;
                float3 limited = col.rgb * (peakLimit / max(peak, peakLimit));

                // Soft knee dual-threshold
                float threshold = _ThresholdParams.x;
                float knee = _ThresholdParams.y * threshold + 0.0001;

                float brightness = max(max(limited.r, limited.g), limited.b);
                float soft = brightness - threshold + knee;
                soft = clamp(soft, 0.0, 2.0 * knee);
                soft = (soft * soft) / (4.0 * knee);

                float weight = max(soft, brightness - threshold) / max(brightness, 0.0001);
                weight = saturate(weight);

                return float4(limited * weight, 1.0);
            }
            ENDHLSL
        }

        // Pass 1: Gaussian Blur Horizontal / Vertical
        Pass
        {
            Name "BloomBlur"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragBlur
            #pragma target 4.5

            float4 _BlurOffset; // xy: delta uv

            half4 FragBlur(Varyings input) : SV_Target
            {
                float2 offset = _BlurOffset.xy;
                float4 sum = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * 0.227027;
                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + offset * 1.384615) * 0.316216;
                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - offset * 1.384615) * 0.316216;
                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv + offset * 3.230769) * 0.070270;
                sum += SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv - offset * 3.230769) * 0.070270;
                return sum;
            }
            ENDHLSL
        }

        // Pass 2: Composite & Color Grade
        Pass
        {
            Name "BloomComposite"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragComposite
            #pragma target 4.5

            half4 FragComposite(Varyings input) : SV_Target
            {
                float4 baseCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                float4 bloomCol = SAMPLE_TEXTURE2D(_BloomTex, sampler_BloomTex, input.uv);

                float intensity = _ThresholdParams.w;
                float3 color = baseCol.rgb + bloomCol.rgb * intensity;

                // Color Balance
                color *= _ColorBalance.rgb;

                // Tone luminance & cool shadow / warm highlight split from post-processing.js
                float luma = dot(color, float3(0.2126, 0.7152, 0.0722));
                float3 shadows = lerp(float3(0.965, 1.005, 1.045), float3(1.0, 1.0, 1.0), smoothstep(0.02, 0.60, luma));
                float3 highlights = lerp(float3(1.0, 1.0, 1.0), float3(1.035, 1.005, 0.965), smoothstep(0.60, 3.0, luma));
                color *= (shadows * highlights);

                // Middle gray contrast pivot (0.18)
                float contrast = _GradingParams.y;
                color *= pow(max(luma, 0.0001) / 0.18, contrast - 1.0);

                // Vibrance & Saturation
                float gray = dot(color, float3(0.2126, 0.7152, 0.0722));
                float chroma = max(max(color.r, color.g), color.b) - min(min(color.r, color.g), color.b);
                float maxVal = max(max(color.r, color.g), color.b);
                float vibrance = 0.10 * (1.0 - chroma / max(maxVal, 0.0001));
                float sat = _GradingParams.z + vibrance;
                color = max(lerp(float3(gray, gray, gray), color, sat), 0.0);

                // Vignette
                float aspect = _ScreenParams.x / _ScreenParams.y;
                float2 uvShift = (input.uv - 0.5) * float2(aspect, 1.0);
                float radius = length(uvShift) / length(float2(aspect, 1.0) * 0.5);
                float vignette = 1.0 - smoothstep(0.35, 1.0, radius) * _GradingParams.w;
                color *= vignette;

                return float4(color, baseCol.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
