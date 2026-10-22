// ---------------------------------------------------------------------------------------------
// Physical Preetham sky (three.js SkyMesh port) + a cheap Beer-style cloud deck.
// Matches js/lighting.js TIME_PRESETS: turbidity, rayleigh, mieCoefficient, mieDirectionalG,
// cloudCoverage, cloudDensity, skyExposure, nightFactor.
//
// Assign to RenderSettings.skybox (or a Volume "Visual Environment" override).
// ---------------------------------------------------------------------------------------------
Shader "Nyc/Sky"
{
    Properties
    {
        _Turbidity       ("Turbidity", Range(1, 20)) = 8.0
        _Rayleigh        ("Rayleigh", Range(0, 4)) = 2.8
        _MieCoefficient  ("Mie coefficient", Range(0, 0.1)) = 0.008
        _MieDirectionalG ("Mie directional G", Range(0, 1)) = 0.88
        _SunDir          ("Sun direction", Vector) = (-0.44, 0.58, -0.68, 0)
        _Exposure        ("Sky exposure", Float) = 0.148
        _NightFactor     ("Night factor", Range(0, 1)) = 0
        _NightColor      ("Night sky", Color) = (0.043, 0.082, 0.157, 1)
        _CloudCoverage   ("Cloud coverage", Range(0, 1)) = 0.36
        _CloudDensity    ("Cloud density", Range(0, 1)) = 0.38
        _CloudElevation  ("Cloud elevation", Range(0, 1)) = 0.52
        _CloudScale      ("Cloud scale", Float) = 0.00018
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Sky"
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Includes/NycCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _Turbidity;
                float  _Rayleigh;
                float  _MieCoefficient;
                float  _MieDirectionalG;
                float4 _SunDir;
                float  _Exposure;
                float  _NightFactor;
                float4 _NightColor;
                float  _CloudCoverage;
                float  _CloudDensity;
                float  _CloudElevation;
                float  _CloudScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 direction  : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            // --- Preetham ------------------------------------------------------------------
            static const float3 kTotalRayleigh = float3(5.804542996261093e-6, 1.3562911419845635e-5, 3.0265902468824876e-5);

            float3 SkyRadiance(float3 direction, float3 sunDir)
            {
                const float3 up = float3(0.0, 1.0, 0.0);
                const float pi = 3.141592653589793;
                const float sunE = 1000.0 * 0.04;

                float3 totalMie = float3(0.2, 0.21, 0.2) * 0.434 * _MieCoefficient;
                float sunfade = 1.0 - saturate(1.0 - exp(sunDir.y / 450000.0));
                float rayleighCoefficient = _Rayleigh - (1.0 - sunfade);
                float3 betaR = kTotalRayleigh * rayleighCoefficient;
                float3 betaM = totalMie * _Turbidity;

                float zenithAngle = acos(max(0.0, dot(up, direction)));
                float inv = 1.0 / (cos(zenithAngle) + 0.15 * pow(max(93.885 - (zenithAngle * 180.0 / pi), 0.001), -1.253));
                float3 sR = kTotalRayleigh * inv;
                float3 sM = totalMie * inv;

                float3 Fex = exp(-(betaR * sR + betaM * sM));

                float cosTheta = dot(direction, sunDir);
                float rayleighPhase = 3.0 / (16.0 * pi) * (1.0 + cosTheta * cosTheta);
                float g = _MieDirectionalG;
                float miePhase = 3.0 / (8.0 * pi)
                               * ((1.0 - g * g) * pow(cosTheta, 2.0))
                               / ((2.0 + g * g) * pow(max(1.0 + g * g - 2.0 * g * cosTheta, 1e-4), 1.5));

                float3 Lin = pow(max(sunE * ((betaR * rayleighPhase + betaM * miePhase) / (betaR + betaM))
                                     * (1.0 - Fex), 0.0), float3(1.5, 1.5, 1.5));
                Lin *= lerp(float3(1, 1, 1),
                            pow(max(sunE * ((betaR * rayleighPhase + betaM * miePhase) / (betaR + betaM)) * Fex, 0.0),
                                float3(0.5, 0.5, 0.5)),
                            saturate(pow(1.0 - dot(up, sunDir), 5.0)));

                float3 L0 = float3(0.1, 0.1, 0.1) * Fex;
                float sunAngularDiameterCos = cos(radians(0.53));
                float sundisk = smoothstep(sunAngularDiameterCos, sunAngularDiameterCos + 0.00002, cosTheta);
                float3 Lsun = L0 * sundisk * 10.0;

                return (Lin + L0) * 0.04 + float3(0.0, 0.0003, 0.00075);
            }

            // --- clouds --------------------------------------------------------------------
            float CloudFbm(float2 p)
            {
                float v = 0.0;
                float a = 0.5;
                for (int i = 0; i < 4; i++)
                {
                    v += a * NycNoise2D(p);
                    p = p * 2.03 + 17.1;
                    a *= 0.5;
                }
                return v;
            }

            float3 CloudLayer(float3 direction, float3 sunDir)
            {
                if (direction.y <= 0.02) return float3(0.0, 0.0, 0.0);

                float elev = max(direction.y, 0.02);
                float2 uv = direction.xz / elev * _CloudScale * 900.0;
                uv += float2(_NycTime * 0.0015, _NycTime * 0.0009);

                float fbm = CloudFbm(uv * 2.2);
                float shape = smoothstep(1.0 - _CloudCoverage, 1.0 - _CloudCoverage + 0.28, fbm);
                float density = saturate(shape * (_CloudDensity * 1.8));
                density *= smoothstep(0.02, _CloudElevation * 0.5 + 0.05, elev);

                float sunAmount = saturate(dot(direction, sunDir) * 0.5 + 0.5);
                float3 lit = lerp(_NycSkyHorizonColor.rgb, _NycSunColor.rgb * 1.6, pow(sunAmount, 6.0));
                float3 shade = lerp(_NycSkyTopColor.rgb * 0.55, _NycSkyHorizonColor.rgb, 0.35);

                return lerp(shade, lit, saturate(fbm * 1.4)) * density;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 direction = normalize(input.direction);
                float3 sunDir = normalize(_SunDir.xyz);

                float3 sky = SkyRadiance(direction, sunDir) * _Exposure;
                sky = lerp(sky, _NightColor.rgb, _NightFactor);

                float3 clouds = CloudLayer(direction, sunDir);
                clouds *= lerp(1.0, 0.12, _NightFactor);
                sky = sky * (1.0 - saturate(clouds * 0.5)) + clouds;

                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
