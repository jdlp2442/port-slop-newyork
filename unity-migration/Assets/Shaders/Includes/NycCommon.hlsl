#ifndef NYC_COMMON_INCLUDED
#define NYC_COMMON_INCLUDED

// ---------------------------------------------------------------------------------------------
// Shared math + globals for every NYC shader.
// The globals mirror `cityUniforms` in js/tsl-materials.js and are pushed from
// LightingSystem.cs on every preset change / day-night tick.
// ---------------------------------------------------------------------------------------------

float4 _NycSunColor;        // linear RGB, xyz
float4 _NycSkyTopColor;
float4 _NycSkyHorizonColor;
float4 _NycSunDir;          // unit vector TOWARD the sun (three.js sunDir)
float4 _NycCpBounds;        // Central Park: minX, maxX, minZ, maxZ (source frame, z south)
float4 _NycIslandBounds;    // Manhattan bulkhead bbox: minX, minZ, maxX, maxZ (Unity frame)
float  _NycInteriorGlow;    // 0 = day, ~1.55 = midnight
float  _NycRoadWetness;     // 0 = bone dry, 1 = after a downpour
float  _NycCanyonBounce;
float  _NycTime;            // seconds since load (water / steam animation)

#define NYC_PI  3.14159265359
#define NYC_2PI 6.28318530718

// ---------------------------------------------------------------------------------------------
// Hashes & noise (exact ports of hash2D / noise2D in tsl-materials.js)
// ---------------------------------------------------------------------------------------------

float NycHash2D(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
}

float NycHash1D(float p)
{
    return frac(sin(p * 78.233) * 43758.5453);
}

float NycNoise2D(float2 p)
{
    float2 i = floor(p);
    float2 f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    float a = NycHash2D(i);
    float b = NycHash2D(i + float2(1.0, 0.0));
    float c = NycHash2D(i + float2(0.0, 1.0));
    float d = NycHash2D(i + float2(1.0, 1.0));
    return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
}

// ---------------------------------------------------------------------------------------------
// Filter-width anti-aliased primitives (ports of lineAA / gridLine / filteredPulse)
// ---------------------------------------------------------------------------------------------

float NycLineAA(float coord, float halfWidth)
{
    float aa = max(fwidth(coord), 0.0001);
    float effHw = max(halfWidth, aa * 0.5);
    float cov = smoothstep(effHw + aa, effHw - aa, abs(coord));
    return cov * min(1.0, halfWidth / max(effHw, 1e-5));
}

float NycGridLine(float coord, float period, float halfWidth)
{
    float g = coord / period;
    float d = 0.5 - abs(frac(g) - 0.5);
    float aa = max(fwidth(g), 0.0001);
    float hw = halfWidth / period;
    float effHw = max(hw, aa * 0.5);
    float cov = smoothstep(effHw + aa, effHw - aa, d);
    return cov * min(1.0, hw / max(effHw, 1e-5));
}

float NycPulseIntegral(float t, float lo, float hi)
{
    return floor(t) * (hi - lo) + clamp(frac(t), lo, hi) - lo;
}

// Fraction of a footprint `w` wide around `x` where frac() lies in [a, b]. Settles to the mean
// coverage (b - a) once the period shrinks below a pixel: facade grids never shimmer.
float NycFilteredPulse(float x, float a, float b, float w)
{
    float hw = max(w, 0.0001) * 0.5;
    return (NycPulseIntegral(x + hw, a, b) - NycPulseIntegral(x - hw, a, b)) / (hw * 2.0);
}

// ---------------------------------------------------------------------------------------------
// Mikkelsen surface-gradient bump mapping (ported from bumpNormal in tsl-materials.js).
// Evaluated in world space so no view matrix round-trip is needed.
// ---------------------------------------------------------------------------------------------

float3 NycBumpNormal(float height, float3 positionWS, float3 normalWS)
{
    float3 dpdx = ddx(positionWS);
    float3 dpdy = ddy(positionWS);
    float3 r1 = cross(dpdy, normalWS);
    float3 r2 = cross(normalWS, dpdx);
    float det = dot(dpdx, r1);
    // HLSL derivatives are ddx/ddy (the GLSL names dFdx/dFdy do not compile
    // on Unity's DX12 shader compiler).
    float3 grad = sign(det) * (ddx(height) * r1 + ddy(height) * r2);
    return normalize(abs(det) * normalWS - grad);
}

// ---------------------------------------------------------------------------------------------
// Occlusion-aware urban canyon sky visibility (aoNode in tsl-materials.js).
// ---------------------------------------------------------------------------------------------

float NycCanyonAO(float3 positionWS)
{
    float inParkX = smoothstep(_NycCpBounds.x - 10.0, _NycCpBounds.x + 20.0, positionWS.x)
                  * (1.0 - smoothstep(_NycCpBounds.y - 20.0, _NycCpBounds.y + 10.0, positionWS.x));
    float inParkZ = smoothstep(_NycCpBounds.z - 10.0, _NycCpBounds.z + 30.0, positionWS.z)
                  * (1.0 - smoothstep(_NycCpBounds.w - 30.0, _NycCpBounds.w + 10.0, positionWS.z));
    float inPark = inParkX * inParkZ;

    float heightOpenness = smoothstep(0.0, 68.0, positionWS.y);
    float streetCrevice = smoothstep(0.0, 7.5, positionWS.y) * 0.22 + 0.78;
    float canyon = lerp(0.44, 1.0, heightOpenness) * streetCrevice;
    return lerp(canyon, 1.0, inPark);
}

// ---------------------------------------------------------------------------------------------
// Post-rain puddle field + wetness masks (roadPuddleField / roadWetMasks)
// ---------------------------------------------------------------------------------------------

float NycPuddleField(float wx, float wz)
{
    float nMacro  = NycNoise2D(float2(wx * 0.095, wz * 0.075));
    float nMid    = NycNoise2D(float2(wx * 0.25 + 19.4, wz * 0.22 + 11.8));
    float nDetail = NycNoise2D(float2(wx * 0.68 - 7.3, wz * 0.68 + 29.1));
    // A guaranteed pool at the 5th Ave / 37th St spawn so the player sees the effect immediately.
    float spawnPool = smoothstep(4.2, 1.4, length(float2(wx - 1.6, wz - 122.8))) * 0.10;
    return nMacro * 0.52 + nMid * 0.34 + nDetail * 0.14 + spawnPool;
}

// x = damp stains, y = standing water, z = overall wet film
float3 NycWetMasks(float field, float drainage = 0.0)
{
    float w = _NycRoadWetness;
    float level = lerp(0.86, 0.56, saturate((w - 0.1) / 0.55)) + drainage;
    float damp   = smoothstep(level - 0.09, level - 0.015, field);
    float puddle = smoothstep(level, level + 0.022, field) * smoothstep(0.1, 0.22, w);
    float film   = smoothstep(0.2, 0.8, w);
    return float3(damp, puddle, film);
}

// Water's Fresnel (F0 = 0.02) used as a metalness proxy so puddles reflect through SSR.
float NycWaterFresnel(float3 viewDir)
{
    return pow(1.0 - saturate(viewDir.y), 5.0) * 0.98 + 0.02;
}

// ---------------------------------------------------------------------------------------------
// Vertex helpers
// ---------------------------------------------------------------------------------------------

float3 NycSafeNormalize(float3 v, float3 fallback)
{
    float l2 = dot(v, v);
    return l2 > 1e-12 ? v * rsqrt(l2) : fallback;
}

#endif // NYC_COMMON_INCLUDED
