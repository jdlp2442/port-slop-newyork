Shader "Hidden/Nyc/GroundContactAO"
{
    Properties
    {
        _BlitTexture ("Source", 2D) = "black" {}
        _MainTex ("Fallback Source", 2D) = "black" {}
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "GroundContactPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _AOParams;   // x: intensity, y: radius, z: bias, w: maxDistance
            CBUFFER_END

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord.xy;

                float4 sceneColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                #if UNITY_REVERSED_Z
                    float rawDepth = SampleSceneDepth(uv);
                #else
                    float rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, SampleSceneDepth(uv));
                #endif

                float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                if (rawDepth <= 0.0001 || linearDepth > _AOParams.w)
                {
                    return sceneColor;
                }

                float3 centerWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

                // Strictly contact shadow near ground (y between -0.30 and 0.22).
                // Sky, upper buildings, facades and upper vehicles are completely unaffected (0 noise).
                if (centerWS.y > 0.22 || centerWS.y < -0.30)
                {
                    return sceneColor;
                }

                float groundWeight = smoothstep(0.22, 0.0, centerWS.y);
                float intensity = _AOParams.x * groundWeight;
                if (intensity <= 0.001)
                {
                    return sceneColor;
                }

                float radiusWS = _AOParams.y;
                float bias = _AOParams.z;

                // Screen-space sample radius scaled inversely by depth
                float2 screenRadius = (radiusWS / max(linearDepth, 0.5)) * float2(1.0, _ScreenParams.x / _ScreenParams.y);
                screenRadius = clamp(screenRadius, 0.001, 0.035);

                // Deterministic, smooth 8-point sampling pattern - NO stochastic per-pixel noise
                const int SAMPLES = 8;
                const float2 SAMPLE_OFFSETS[8] = {
                    float2( 0.35,  0.15), float2(-0.25,  0.40), float2(-0.45, -0.20),
                    float2( 0.20, -0.45), float2( 0.65, -0.15), float2(-0.10,  0.65),
                    float2(-0.65,  0.25), float2( 0.40,  0.55)
                };

                float occlusion = 0.0;
                float totalWeight = 0.0;

                for (int i = 0; i < SAMPLES; i++)
                {
                    float2 offset = SAMPLE_OFFSETS[i] * screenRadius;
                    float2 sampleUV = clamp(uv + offset, 0.001, 0.999);

                    #if UNITY_REVERSED_Z
                        float sRaw = SampleSceneDepth(sampleUV);
                    #else
                        float sRaw = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, SampleSceneDepth(sampleUV));
                    #endif

                    float3 sWS = ComputeWorldSpacePosition(sampleUV, sRaw, UNITY_MATRIX_I_VP);
                    float3 diff = sWS - centerWS;
                    float dist = length(diff);

                    // Depth-aware bilateral weight: only occlude if sample is on the same ground plane/contact geometry
                    float depthDiff = abs(sWS.y - centerWS.y);
                    if (dist > bias && dist < radiusWS && depthDiff < 0.6)
                    {
                        float horizonAngle = max(0.0, diff.y / dist);
                        float rangeFalloff = smoothstep(radiusWS, bias, dist);
                        float bilateralWeight = smoothstep(0.6, 0.0, depthDiff);
                        occlusion += horizonAngle * rangeFalloff * bilateralWeight;
                    }
                    totalWeight += 1.0;
                }

                occlusion = saturate((occlusion / totalWeight) * intensity * 2.0);
                float aoFactor = 1.0 - occlusion;

                float3 result = sceneColor.rgb * aoFactor;
                return float4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
