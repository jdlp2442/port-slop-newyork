Shader "Hidden/Nyc/AtmosphericScattering"
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
            Name "AtmosphericScattering"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _FogParams;      // x: fogDensity, y: heightFalloff, z: startDistance, w: maxDistance
                float4 _SunDir;         // xyz: sun direction, w: sunIntensity
                float4 _SunColor;       // rgb: sun color, a: nightFactor
                float4 _HazeColor;      // rgb: haze color, a: inscatteringBoost
            CBUFFER_END

            // Rayleigh phase function: P(theta) = 3/(16*PI) * (1 + cos^2(theta))
            float RayleighPhase(float cosTheta)
            {
                return 0.0596831 * (1.0 + cosTheta * cosTheta);
            }

            // Mie phase function (Henyey-Greenstein approximation, g = 0.76)
            float HenyeyGreensteinPhase(float cosTheta, float g)
            {
                float g2 = g * g;
                float denom = 1.0 + g2 - 2.0 * g * cosTheta;
                return (1.0 - g2) / (4.0 * 3.14159265 * pow(max(denom, 0.0001), 1.5));
            }

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

                // Sky background: keep original skybox unless atmospheric haze softly blankets the horizon
                float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                if (rawDepth <= 0.00001 || linearDepth > 55000.0)
                {
                    return sceneColor;
                }

                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 cameraPosWS = _WorldSpaceCameraPos;
                float3 ray = positionWS - cameraPosWS;
                float rayDistance = length(ray);
                float3 rayDir = ray / max(rayDistance, 0.001);

                // Density height integration: rho(y) = rho0 * exp(-falloff * max(y, 0))
                float density = _FogParams.x;
                float falloff = max(_FogParams.y, 0.0001);
                float startDist = _FogParams.z;
                float maxDist = _FogParams.w;

                float effectiveDist = clamp(rayDistance - startDist, 0.0, maxDist);
                float h0 = max(cameraPosWS.y, 0.0);
                float h1 = max(positionWS.y, 0.0);
                float avgH = (h0 + h1) * 0.5;

                // Optical depth along line of sight
                float heightAtten = exp(-avgH * falloff);
                float opticalDepth = density * effectiveDist * heightAtten;
                float extinction = saturate(1.0 - exp(-opticalDepth));

                // Sun inscattering
                float cosTheta = dot(rayDir, normalize(_SunDir.xyz));
                float rPhase = RayleighPhase(cosTheta);
                float mPhase = HenyeyGreensteinPhase(cosTheta, 0.76);

                float night = _SunColor.a;
                float3 sunRadiance = _SunColor.rgb * _SunDir.w;
                float3 inscatter = (_HazeColor.rgb * rPhase + sunRadiance * mPhase * 0.35) * _HazeColor.a;

                // Night haze shift toward deep blue/grey city glow
                float3 nightHaze = float3(0.04, 0.06, 0.11) * (1.0 - exp(-rayDistance * 0.0008));
                inscatter = lerp(inscatter, nightHaze, night);

                float3 result = lerp(sceneColor.rgb, inscatter, extinction);
                return float4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
