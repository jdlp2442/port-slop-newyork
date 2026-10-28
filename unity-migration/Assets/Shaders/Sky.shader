// ---------------------------------------------------------------------------------------------
// Physical Preetham sky: faithful port of three/addons/objects/SkyMesh.js (three r187, WebGPU).
// This is NOT the classic WebGL Sky.js: SkyMesh.js computes sunE with an earth-shadow hack,
// uses zenith optical lengths of 8.4E3 / 1.25E3 and a MieConst-based betaM. The classic
// constants (constant sunE = 40, sR = totalRayleigh) collapse Fex to 1 and render a black sky.
//
// Source of truth: https://raw.githubusercontent.com/mrdoob/three.js/dev/examples/jsm/objects/SkyMesh.js
// The repo's js/lighting.js wraps the raw output: dayCol = raw * uSkyExposure, then
// mix(dayCol, nightSkyColor, nightFactor) — reproduced by _Exposure / _NightFactor below.
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
        _ShowSunDisc     ("Show sun disc", Float) = 1
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
                float  _ShowSunDisc;
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

            // --- Preetham constants (SkyMesh.js) -------------------------------------------------
            static const float3 kTotalRayleigh = float3(5.804542996261093e-6, 1.3562911419845635e-5, 3.0265902468824876e-5);
            static const float3 kMieConst       = float3(1.8399918514433978e14, 2.7798023919660528e14, 4.0790479543861094e14);
            static const float  kCutoffAngle    = 1.6110731556870734;  // pi / 1.95
            static const float  kSteepness      = 1.5;
            static const float  kEE             = 1000.0;
            static const float  kRayleighZenith = 8.4e3;
            static const float  kMieZenith      = 1.25e3;
            static const float  kSunAngularCos  = 0.9999566769464484;  // 66 arc seconds
            static const float  kThreeOver16Pi  = 0.05968310365946075;
            static const float  kOneOver4Pi     = 0.07957747154594767;
            static const float  kE              = 2.718281828459045;
            // js/lighting.js:153 sets cloudSpeed once; the drift is in UV units per second.
            static const float  kCloudSpeed     = 0.000015;

            // --- clouds: sinless hash gradient noise (SkyMesh.js) --------------------------------
            float2 SkyGradient(float2 i)
            {
                float3 p = frac(i.xyx * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yzx + 33.33);
                return frac((p.xx + p.yz) * p.zy) * 2.0 - 1.0;
            }

            float SkyNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);  // quintic fade
                float a = dot(SkyGradient(i), f);
                float b = dot(SkyGradient(i + float2(1.0, 0.0)), f - float2(1.0, 0.0));
                float c = dot(SkyGradient(i + float2(0.0, 1.0)), f - float2(0.0, 1.0));
                float d = dot(SkyGradient(i + float2(1.0, 1.0)), f - float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y) * 1.6;  // ~[-1,1]
            }

            float SkyFbm(float2 position, float2 drift)
            {
                float2 p = position;
                float result = 0.0;
                float amplitude = 1.0;
                for (int k = 0; k < 4; k++)
                {
                    result += amplitude * SkyNoise(p);
                    amplitude *= 0.5;
                    p = p * 2.0 + drift;
                }
                return result;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 direction = normalize(input.direction);
                float3 sunDir = normalize(_SunDir.xyz);

                // --- varyings of SkyMesh.js (uniform across the skybox draw) --------------------
                // earth shadow hack: sun intensity falls to 0 at the horizon cutoff
                float zenithAngleCos = clamp(sunDir.y, -1.0, 1.0);
                float sunE = kEE * max(0.0, 1.0 - pow(kE, -(kCutoffAngle - acos(zenithAngleCos)) / kSteepness));

                float sunfade = 1.0 - clamp(1.0 - exp(sunDir.y / 450000.0), 0.0, 1.0);
                float rayleighCoefficient = _Rayleigh - (1.0 - sunfade);
                float3 betaR = kTotalRayleigh * rayleighCoefficient;

                float cMie = 0.2 * _Turbidity * 10e-18;
                float3 totalMie = 0.434 * cMie * kMieConst;
                float3 betaM = totalMie * _MieCoefficient;

                // --- optical depth -----------------------------------------------------------------
                float zenithAngle = acos(max(0.0, direction.y));
                float inv = 1.0 / (cos(zenithAngle) + 0.15 * pow(max(93.885 - (zenithAngle * 180.0 / NYC_PI), 0.001), -1.253));
                float sR = kRayleighZenith * inv;
                float sM = kMieZenith * inv;
                float3 Fex = exp(-(betaR * sR + betaM * sM));

                // --- in-scattering ----------------------------------------------------------------
                float cosTheta = dot(direction, sunDir);

                float rc = cosTheta * 0.5 + 0.5;
                float rPhase = kThreeOver16Pi * (1.0 + rc * rc);
                float3 betaRTheta = betaR * rPhase;

                float g2 = _MieDirectionalG * _MieDirectionalG;
                float mInv = 1.0 / pow(max(1.0 - 2.0 * _MieDirectionalG * cosTheta + g2, 1e-6), 1.5);
                float mPhase = kOneOver4Pi * (1.0 - g2) * mInv;
                float3 betaMTheta = betaM * mPhase;

                float3 betaSum = max(betaR + betaM, 1e-7);
                float3 inscatter = sunE * (betaRTheta + betaMTheta) / betaSum;

                float3 Lin = pow(max(inscatter * (1.0 - Fex), 0.0), 1.5);
                Lin *= lerp(float3(1.0, 1.0, 1.0),
                            pow(max(inscatter * Fex, 0.0), 0.5),
                            clamp(pow(1.0 - sunDir.y, 5.0), 0.0, 1.0));

                float3 L0 = float3(0.1, 0.1, 0.1) * Fex;

                // composition + solar disc (66 arc seconds, additive, hidden when baking env maps)
                float sundisc = clamp((cosTheta - kSunAngularCos) * 50000.0, 0.0, 1.0) * _ShowSunDisc;
                float3 sundiscColor = min(sunE * Fex, 80.0) * 760.0 * sundisc;

                float3 texColor = (Lin + L0) * 0.04 + sundiscColor + float3(0.0, 0.0003, 0.00075);

                // --- clouds: Beer-Powder self-shadowing + Henyey-Greenstein silver lining --------
                if (direction.y > 0.0 && _CloudCoverage > 0.0)
                {
                    float elevation = lerp(1.0, 0.1, _CloudElevation);
                    float2 cloudUV = direction.xz / (direction.y * elevation) * _CloudScale;
                    cloudUV += _NycTime * kCloudSpeed;

                    float evolve = _NycTime * kCloudSpeed * 300.0;
                    float cloudNoise = saturate(SkyFbm(cloudUV * 1000.0, evolve) * 0.7 + 0.5);

                    float region = SkyNoise(cloudUV * 300.0) * 0.37 + 0.5;
                    float cov = saturate(_CloudCoverage + (region - 0.5) * 0.6);

                    float threshold = 1.0 - cov;
                    float cloudMask = smoothstep(threshold, threshold + 0.3, cloudNoise);
                    float horizonFade = smoothstep(0.0, 0.03 + 0.06 * _CloudElevation, direction.y);
                    cloudMask *= horizonFade;

                    float dayFactor = smoothstep(-0.08, 0.3, sunDir.y);
                    float3 sunColor = sunE * Fex * 0.22 * 0.04;   // 0.22 ~ albedo/pi, 0.04 = exposure
                    float3 skyAmbient = Lin * 0.04 + float3(0.0, 0.0003, 0.00075);

                    float depth = max(0.0, cloudNoise - threshold);
                    float beer = exp(depth * -4.0);
                    float powder = 1.0 - beer * beer;              // beer*beer == exp(-8*depth)
                    float shade = lerp(0.45, 1.0, saturate(beer * powder * 2.6));  // 2.6 = 1/0.385

                    float silver = clamp(0.51 / pow(max(1.49 - cosTheta * 1.4, 1e-4), 1.5), 0.0, 3.0);  // g = 0.7
                    float edge = cloudMask * (1.0 - cloudMask) * 4.0;

                    float3 cloudColor = skyAmbient + sunColor * shade;
                    cloudColor += sunColor * silver * edge * 0.6;
                    cloudColor *= max(dayFactor, 0.03);

                    float alpha = (1.0 - exp(depth * _CloudDensity * -12.0)) * horizonFade;

                    // Occlude the sun disc/glow behind opaque cloud, then composite through the
                    // atmosphere so distant clouds dissolve into haze.
                    texColor -= (L0 * 0.04 + sundiscColor) * alpha;
                    float3 cloudAerial = lerp(texColor, cloudColor, Fex);
                    texColor = lerp(texColor, cloudAerial, alpha);
                }

                // js/lighting.js:163-168 wrapper — calibrate into HDR, then blend to the night sky.
                float3 sky = texColor * _Exposure;
                sky = lerp(sky, _NightColor.rgb, _NightFactor);

                return half4(sky, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
