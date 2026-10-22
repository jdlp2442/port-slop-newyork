Shader "Hidden/Nyc/GroundContactAO"
{
    Properties
    {
        _MainTex ("Source", 2D) = "white" {}
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

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

            CBUFFER_START(UnityPerMaterial)
                float4 _AOParams;   // x: intensity, y: radius, z: bias, w: maxDistance
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            // Pseudo-random rotation hash per pixel
            float2 Hash2(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(443.897, 441.423, 437.195));
                p3 += dot(p3, p3.yzx + 19.19);
                return frac((p3.xx + p3.yz) * p3.zy);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float4 sceneColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);

                #if UNITY_REVERSED_Z
                    float rawDepth = SampleSceneDepth(input.uv);
                #else
                    float rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, SampleSceneDepth(input.uv));
                #endif

                float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                if (rawDepth <= 0.0001 || linearDepth > _AOParams.w)
                {
                    return sceneColor;
                }

                float3 centerWS = ComputeWorldSpacePosition(input.uv, rawDepth, UNITY_MATRIX_I_VP);

                // Near-ground enhancement: contact shadow strongest near Y = 0 (roads/asphalt) and sidewalks
                float groundWeight = saturate(1.0 - abs(centerWS.y) * 0.25);

                float intensity = _AOParams.x * (0.45 + 0.55 * groundWeight);
                float radiusWS = _AOParams.y;
                float bias = _AOParams.z;

                // Screen-space sample radius scaled inversely by depth
                float2 screenRadius = (radiusWS / max(linearDepth, 0.5)) * float2(1.0, _ScreenParams.x / _ScreenParams.y);
                screenRadius = clamp(screenRadius, 0.001, 0.04);

                float2 rand = Hash2(input.uv * _ScreenParams.xy);
                float angle = rand.x * 6.2831853;
                float2x2 rot = float2x2(cos(angle), -sin(angle), sin(angle), cos(angle));

                float occlusion = 0.0;
                const int SAMPLES = 6;
                const float2 SAMPLE_OFFSETS[6] = {
                    float2(0.5, 0.2), float2(-0.4, 0.6), float2(-0.6, -0.3),
                    float2(0.3, -0.7), float2(0.8, -0.2), float2(-0.1, 0.9)
                };

                for (int i = 0; i < SAMPLES; i++)
                {
                    float2 offset = mul(rot, SAMPLE_OFFSETS[i]) * screenRadius;
                    float2 sampleUV = clamp(input.uv + offset, 0.001, 0.999);

                    #if UNITY_REVERSED_Z
                        float sRaw = SampleSceneDepth(sampleUV);
                    #else
                        float sRaw = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, SampleSceneDepth(sampleUV));
                    #endif

                    float3 sWS = ComputeWorldSpacePosition(sampleUV, sRaw, UNITY_MATRIX_I_VP);
                    float3 diff = sWS - centerWS;
                    float dist = length(diff);

                    // Occlusion factor if the neighbor is closer to camera and within contact sphere
                    if (dist > bias && dist < radiusWS)
                    {
                        float horizonAngle = max(0.0, diff.y / dist);
                        float rangeFalloff = 1.0 - (dist / radiusWS);
                        occlusion += horizonAngle * rangeFalloff;
                    }
                }

                occlusion = saturate((occlusion / float(SAMPLES)) * intensity * 2.2);
                float aoFactor = 1.0 - occlusion;

                // Modulate ambient darkness without tinting bright emissives
                float3 result = sceneColor.rgb * aoFactor;
                return float4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
