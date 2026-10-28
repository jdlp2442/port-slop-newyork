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
                float4 _FogParams;      // x: fogDensity, y: fogHeight (900m), z: startDistance, w: maxDistance
                float4 _SunDir;         // xyz: sun direction, w: sunIntensity
                float4 _SunColor;       // rgb: sun color, a: nightFactor
                float4 _HazeColor;      // rgb: horizon fog color, a: inscatteringBoost
            CBUFFER_END
            float4 _NycSkyTopColor;
            float4 _NycSkyHorizonColor;

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

                // Sky background: keep original skybox
                float linearDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                if (rawDepth <= 0.00001 || linearDepth > 55000.0)
                {
                    return sceneColor;
                }

                float3 positionWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 cameraPosWS = _WorldSpaceCameraPos;
                float3 toFrag = positionWS - cameraPosWS;
                float dist = length(toFrag);
                float3 dir = toFrag / max(dist, 0.001);

                // Exact port of createAerialFogNode() from js/lighting.js:
                // Mean haze density exp(-y / H) along the ray between fragment and camera
                float H = max(_FogParams.y, 100.0);
                float ya = max(positionWS.y, 0.0);
                float yb = max(cameraPosWS.y, 0.0);
                float dy = yb - ya;
                float ea = exp(-ya / H);
                float eb = exp(-yb / H);
                float meanDensity = abs(dy) > 1.0 ? (ea - eb) * H / dy : ea;

                float tau = _FogParams.x * dist * meanDensity;
                float extinction = saturate(1.0 - exp(-(tau * tau)));

                // Sky-matched horizon color in view direction + subtle solar Mie forward scatter
                float3 horizonCol = length(_NycSkyHorizonColor.rgb) > 0.001 ? _NycSkyHorizonColor.rgb : _HazeColor.rgb;
                float3 topCol = length(_NycSkyTopColor.rgb) > 0.001 ? _NycSkyTopColor.rgb : horizonCol * 0.5;
                float elev = saturate(max(dir.y, 0.015) * 2.5);
                float3 skyColor = lerp(horizonCol, topCol, elev);

                float cosTheta = dot(dir, normalize(_SunDir.xyz));
                float sunGlow = pow(max(cosTheta, 0.0), 8.0) * 0.35 * (1.0 - _SunColor.a);
                skyColor = lerp(skyColor, _HazeColor.rgb, 0.35) + _SunColor.rgb * sunGlow;

                float3 result = lerp(sceneColor.rgb, skyColor, extinction);
                return float4(result, sceneColor.a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
