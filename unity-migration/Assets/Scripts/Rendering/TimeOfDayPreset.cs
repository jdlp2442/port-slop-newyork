using UnityEngine;

namespace Nyc.Rendering
{
    /// <summary>
    /// One atmosphere look, copied field-for-field from TIME_PRESETS in js/lighting.js plus the
    /// matching LOOKS grade from js/post-processing.js.
    /// </summary>
    [CreateAssetMenu(menuName = "NYC/Time Of Day Preset", fileName = "TimeOfDay_")]
    public sealed class TimeOfDayPreset : ScriptableObject
    {
        [Header("Identity")]
        public string id = "golden";
        public string displayName = "Golden Hour · 17:45";
        [Tooltip("Clock hour this preset represents, used by the continuous day/night cycle.")]
        [Range(0f, 24f)] public float hour = 17.75f;

        [Header("Sun")]
        public Vector3 sunDir = new Vector3(-0.44f, 0.58f, -0.68f);
        public Color sunColor = new Color(1f, 0.792f, 0.573f);
        [Min(0f)] public float sunIntensity = 5.8f;

        [Header("Ambient")]
        public Color hemiSky = new Color(0.604f, 0.733f, 0.902f);
        public Color hemiGround = new Color(0.784f, 0.541f, 0.384f);
        [Min(0f)] public float hemiIntensity = 1.48f;

        [Header("Sky")]
        public Color skyTop = new Color(0.173f, 0.337f, 0.549f);
        public Color skyHorizon = new Color(0.922f, 0.820f, 0.710f);
        public float turbidity = 8f;
        public float rayleigh = 2.8f;
        public float mieCoefficient = 0.008f;
        public float mieDirectionalG = 0.88f;
        public float cloudCoverage = 0.36f;
        public float cloudDensity = 0.38f;
        public float skyExposure = 0.148f;
        [Range(0f, 1f)] public float nightFactor = 0f;

        [Header("Atmosphere")]
        public Color fogColor = new Color(0.863f, 0.773f, 0.682f);
        [Tooltip("FogExp2 density, per metre (three.js scene.fog.density).")]
        public float fogDensity = 0.00024f;
        [Range(0f, 2f)] public float envIntensity = 0.54f;

        [Header("City")]
        [Range(0f, 3f)] public float interiorGlow = 0.48f;
        [Range(0f, 1f)] public float roadWetness = 0.28f;
        [Range(0f, 1f)] public float canyonBounce = 0.52f;

        [Header("Grade")]
        public float exposure = 1.04f;
        public float contrast = 1.12f;
        public float saturation = 1.08f;
        public Vector3 whiteBalance = new Vector3(1.045f, 1.0f, 0.955f);
        public float bloom = 0.24f;
        public float bloomThreshold = 1.20f;

        /// <summary>Blend two presets; used by LightingSystem.SetHour.</summary>
        public static TimeOfDayPreset Lerp(TimeOfDayPreset a, TimeOfDayPreset b, float t, TimeOfDayPreset into)
        {
            into.id = t < 0.5f ? a.id : b.id;
            into.displayName = t < 0.5f ? a.displayName : b.displayName;
            into.hour = Mathf.Lerp(a.hour, b.hour, t);

            into.sunDir = Vector3.Slerp(a.sunDir.normalized, b.sunDir.normalized, t);
            into.sunColor = Color.Lerp(a.sunColor, b.sunColor, t);
            into.sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, t);

            into.hemiSky = Color.Lerp(a.hemiSky, b.hemiSky, t);
            into.hemiGround = Color.Lerp(a.hemiGround, b.hemiGround, t);
            into.hemiIntensity = Mathf.Lerp(a.hemiIntensity, b.hemiIntensity, t);

            into.skyTop = Color.Lerp(a.skyTop, b.skyTop, t);
            into.skyHorizon = Color.Lerp(a.skyHorizon, b.skyHorizon, t);
            into.turbidity = Mathf.Lerp(a.turbidity, b.turbidity, t);
            into.rayleigh = Mathf.Lerp(a.rayleigh, b.rayleigh, t);
            into.mieCoefficient = Mathf.Lerp(a.mieCoefficient, b.mieCoefficient, t);
            into.mieDirectionalG = Mathf.Lerp(a.mieDirectionalG, b.mieDirectionalG, t);
            into.cloudCoverage = Mathf.Lerp(a.cloudCoverage, b.cloudCoverage, t);
            into.cloudDensity = Mathf.Lerp(a.cloudDensity, b.cloudDensity, t);
            into.skyExposure = Mathf.Lerp(a.skyExposure, b.skyExposure, t);
            into.nightFactor = Mathf.Lerp(a.nightFactor, b.nightFactor, t);

            into.fogColor = Color.Lerp(a.fogColor, b.fogColor, t);
            into.fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t);
            into.envIntensity = Mathf.Lerp(a.envIntensity, b.envIntensity, t);

            into.interiorGlow = Mathf.Lerp(a.interiorGlow, b.interiorGlow, t);
            into.roadWetness = Mathf.Lerp(a.roadWetness, b.roadWetness, t);
            into.canyonBounce = Mathf.Lerp(a.canyonBounce, b.canyonBounce, t);

            into.exposure = Mathf.Lerp(a.exposure, b.exposure, t);
            into.contrast = Mathf.Lerp(a.contrast, b.contrast, t);
            into.saturation = Mathf.Lerp(a.saturation, b.saturation, t);
            into.whiteBalance = Vector3.Lerp(a.whiteBalance, b.whiteBalance, t);
            into.bloom = Mathf.Lerp(a.bloom, b.bloom, t);
            into.bloomThreshold = Mathf.Lerp(a.bloomThreshold, b.bloomThreshold, t);
            return into;
        }
    }
}
