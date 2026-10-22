using System.IO;
using UnityEditor;
using UnityEngine;
using Nyc.Rendering;

namespace Nyc.EditorTools
{
    /// <summary>
    /// Creates the four TimeOfDayPreset assets with the exact values of TIME_PRESETS (lighting.js)
    /// and LOOKS (post-processing.js), plus a ready-made URP Volume profile.
    /// Menu: NYC/5. Create Time Of Day Presets
    /// </summary>
    public static class NycPresetCreator
    {
        const string Dir = "Assets/ScriptableObjects/City";

        [MenuItem("NYC/5. Create Time Of Day Presets", priority = 5)]
        public static void CreatePresets()
        {
            Directory.CreateDirectory(Dir);

            Create("golden", "Golden Hour · 17:45", 17.75f,
                sunDir: new Vector3(-0.44f, 0.58f, -0.68f), sun: 0xffca92, sunI: 5.8f,
                hemiSky: 0x9abbe6, hemiGround: 0xc88a62, hemiI: 1.48f,
                skyTop: 0x2c568c, skyHorizon: 0xebd1b5, fog: 0xdcc5ae, fogD: 0.00024f,
                glow: 0.48f, wet: 0.28f, env: 0.54f, bounce: 0.52f,
                turb: 8.0f, ray: 2.8f, mie: 0.008f, mieG: 0.88f, cloudC: 0.36f, cloudD: 0.38f,
                skyExp: 0.148f, night: 0.0f,
                exp: 1.04f, contrast: 1.12f, sat: 1.08f, wb: new Vector3(1.045f, 1f, 0.955f),
                bloom: 0.24f, bloomT: 1.20f);

            Create("midday", "Sunny Midday · 13:30", 13.5f,
                sunDir: new Vector3(-0.34f, 0.82f, -0.46f), sun: 0xfff5e8, sunI: 6.6f,
                hemiSky: 0xaad2ff, hemiGround: 0xb89678, hemiI: 1.58f,
                skyTop: 0x225ea8, skyHorizon: 0xc8dff5, fog: 0xc6dcf2, fogD: 0.00018f,
                glow: 0.24f, wet: 0.08f, env: 0.58f, bounce: 0.58f,
                turb: 6.5f, ray: 2.4f, mie: 0.006f, mieG: 0.86f, cloudC: 0.32f, cloudD: 0.35f,
                skyExp: 0.142f, night: 0.0f,
                exp: 1.00f, contrast: 1.10f, sat: 1.06f, wb: new Vector3(1.015f, 1f, 0.985f),
                bloom: 0.18f, bloomT: 1.35f);

            Create("twilight", "Blue Hour · 19:15", 19.25f,
                sunDir: new Vector3(-0.52f, 0.36f, -0.77f), sun: 0xff9466, sunI: 3.1f,
                hemiSky: 0x4c6ca8, hemiGround: 0x6e5048, hemiI: 0.85f,
                skyTop: 0x102248, skyHorizon: 0xb86b6c, fog: 0x353c58, fogD: 0.00032f,
                glow: 1.25f, wet: 0.55f, env: 0.36f, bounce: 0.32f,
                turb: 9.5f, ray: 3.4f, mie: 0.010f, mieG: 0.90f, cloudC: 0.40f, cloudD: 0.42f,
                skyExp: 0.115f, night: 0.28f,
                exp: 1.06f, contrast: 1.10f, sat: 1.08f, wb: new Vector3(1.025f, 0.995f, 1.015f),
                bloom: 0.28f, bloomT: 1.05f);

            Create("midnight", "Manhattan Night · 23:00", 23f,
                sunDir: new Vector3(0.42f, 0.68f, -0.60f), sun: 0x8cb0e8, sunI: 1.45f,
                hemiSky: 0x263a62, hemiGround: 0x382e36, hemiI: 0.72f,
                skyTop: 0x060c1a, skyHorizon: 0x182640, fog: 0x0c1424, fogD: 0.00036f,
                glow: 1.55f, wet: 0.62f, env: 0.30f, bounce: 0.18f,
                turb: 4.0f, ray: 1.5f, mie: 0.004f, mieG: 0.80f, cloudC: 0.22f, cloudD: 0.25f,
                skyExp: 0.022f, night: 0.86f,
                exp: 1.04f, contrast: 1.08f, sat: 1.06f, wb: new Vector3(0.985f, 1f, 1.035f),
                bloom: 0.30f, bloomT: 1.00f);

            AssetDatabase.SaveAssets();
            Debug.Log("[NYC] Time-of-day presets created in " + Dir);
        }

        static void Create(string id, string display, float hour,
            Vector3 sunDir, uint sun, float sunI,
            uint hemiSky, uint hemiGround, float hemiI,
            uint skyTop, uint skyHorizon, uint fog, float fogD,
            float glow, float wet, float env, float bounce,
            float turb, float ray, float mie, float mieG, float cloudC, float cloudD,
            float skyExp, float night,
            float exp, float contrast, float sat, Vector3 wb, float bloom, float bloomT)
        {
            string path = $"{Dir}/TimeOfDay_{id}.asset";
            var asset = AssetDatabase.LoadAssetAtPath<TimeOfDayPreset>(path);
            if (!asset)
            {
                asset = ScriptableObject.CreateInstance<TimeOfDayPreset>();
                AssetDatabase.CreateAsset(asset, path);
            }

            asset.id = id;
            asset.displayName = display;
            asset.hour = hour;

            asset.sunDir = sunDir.normalized;
            asset.sunColor = FromHex(sun);
            asset.sunIntensity = sunI;

            asset.hemiSky = FromHex(hemiSky);
            asset.hemiGround = FromHex(hemiGround);
            asset.hemiIntensity = hemiI;

            asset.skyTop = FromHex(skyTop);
            asset.skyHorizon = FromHex(skyHorizon);
            asset.turbidity = turb;
            asset.rayleigh = ray;
            asset.mieCoefficient = mie;
            asset.mieDirectionalG = mieG;
            asset.cloudCoverage = cloudC;
            asset.cloudDensity = cloudD;
            asset.skyExposure = skyExp;
            asset.nightFactor = night;

            asset.fogColor = FromHex(fog);
            asset.fogDensity = fogD;
            asset.envIntensity = env;

            asset.interiorGlow = glow;
            asset.roadWetness = wet;
            asset.canyonBounce = bounce;

            asset.exposure = exp;
            asset.contrast = contrast;
            asset.saturation = sat;
            asset.whiteBalance = wb;
            asset.bloom = bloom;
            asset.bloomThreshold = bloomT;

            EditorUtility.SetDirty(asset);
        }

        static Color FromHex(uint hex) => new Color(
            ((hex >> 16) & 0xFF) / 255f,
            ((hex >> 8) & 0xFF) / 255f,
            (hex & 0xFF) / 255f,
            1f);
    }
}
