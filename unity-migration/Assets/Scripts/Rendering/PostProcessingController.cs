using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nyc.Rendering
{
    /// <summary>
    /// Look parameters matching LOOKS dictionary in slop-newyork-main/js/post-processing.js.
    /// </summary>
    [Serializable]
    public struct PostProcessingLook
    {
        public string name;
        public float hour;
        public float exposure;
        public float contrast;
        public float saturation;
        public Color balance;
        public float bloom;
        public float bloomThreshold;

        public static PostProcessingLook Golden => new PostProcessingLook
        {
            name = "golden",
            hour = 17.5f,
            exposure = 1.04f,
            contrast = 1.12f,
            saturation = 1.08f,
            balance = new Color(1.045f, 1.00f, 0.955f, 1f),
            bloom = 0.24f,
            bloomThreshold = 1.20f
        };

        public static PostProcessingLook Midday => new PostProcessingLook
        {
            name = "midday",
            hour = 12.0f,
            exposure = 1.00f,
            contrast = 1.10f,
            saturation = 1.06f,
            balance = new Color(1.015f, 1.00f, 0.985f, 1f),
            bloom = 0.18f,
            bloomThreshold = 1.35f
        };

        public static PostProcessingLook Twilight => new PostProcessingLook
        {
            name = "twilight",
            hour = 19.5f,
            exposure = 1.06f,
            contrast = 1.10f,
            saturation = 1.08f,
            balance = new Color(1.025f, 0.995f, 1.015f, 1f),
            bloom = 0.28f,
            bloomThreshold = 1.05f
        };

        public static PostProcessingLook Midnight => new PostProcessingLook
        {
            name = "midnight",
            hour = 0.0f,
            exposure = 1.04f,
            contrast = 1.08f,
            saturation = 1.06f,
            balance = new Color(0.985f, 1.00f, 1.035f, 1f),
            bloom = 0.30f,
            bloomThreshold = 1.00f
        };

        public static PostProcessingLook Lerp(PostProcessingLook a, PostProcessingLook b, float t)
        {
            return new PostProcessingLook
            {
                name = t < 0.5f ? a.name : b.name,
                hour = Mathf.Lerp(a.hour, b.hour, t),
                exposure = Mathf.Lerp(a.exposure, b.exposure, t),
                contrast = Mathf.Lerp(a.contrast, b.contrast, t),
                saturation = Mathf.Lerp(a.saturation, b.saturation, t),
                balance = Color.Lerp(a.balance, b.balance, t),
                bloom = Mathf.Lerp(a.bloom, b.bloom, t),
                bloomThreshold = Mathf.Lerp(a.bloomThreshold, b.bloomThreshold, t)
            };
        }
    }

    /// <summary>
    /// Universal Render Pipeline (URP) Post-Processing Controller.
    /// Manages ACES Tonemapping, Bloom, Vignette, and Color Adjustments
    /// synchronized with the day/night cycle and LightingSystem presets.
    /// Port of slop-newyork-main/js/post-processing.js.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Volume))]
    public sealed class PostProcessingController : MonoBehaviour
    {
        [Header("Volume Reference")]
        public Volume volume;

        [Header("Presets Configuration")]
        public PostProcessingLook currentLook;

        [Header("Vignette Settings")]
        [Range(0f, 1f)] public float vignetteIntensity = 0.12f;
        [Range(0f, 1f)] public float vignetteSmoothness = 0.40f;

        Bloom _bloom;
        ColorAdjustments _colorAdjustments;
        Tonemapping _tonemapping;
        Vignette _vignette;
        bool _initialized;

        void Awake() => EnsureInit();

        void OnEnable()
        {
            EnsureInit();
            if (LightingSystem.Instance != null)
            {
                LightingSystem.Instance.PresetChanged += OnLightingPresetChanged;
            }
        }

        void OnDisable()
        {
            if (LightingSystem.Instance != null)
            {
                LightingSystem.Instance.PresetChanged -= OnLightingPresetChanged;
            }
        }

        public void EnsureInit()
        {
            if (_initialized) return;

            if (!volume) volume = GetComponent<Volume>();
            if (volume)
            {
                VolumeProfile profile = volume.sharedProfile;
                if (!profile)
                {
                    profile = ScriptableObject.CreateInstance<VolumeProfile>();
                    profile.name = "NycPostProcessingProfile";
                    volume.sharedProfile = profile;
                }

                if (!profile.TryGet(out _bloom))
                    _bloom = profile.Add<Bloom>(true);

                if (!profile.TryGet(out _colorAdjustments))
                    _colorAdjustments = profile.Add<ColorAdjustments>(true);

                if (!profile.TryGet(out _tonemapping))
                    _tonemapping = profile.Add<Tonemapping>(true);

                if (!profile.TryGet(out _vignette))
                    _vignette = profile.Add<Vignette>(true);
            }

            _initialized = true;
            currentLook = PostProcessingLook.Golden;
            ApplyLook(currentLook);
        }

        void OnLightingPresetChanged(TimeOfDayPreset preset)
        {
            if (preset == null) return;
            UpdateByHour(preset.hour);
        }

        /// <summary>
        /// Selects or blends post-processing looks based on 24-hour time of day.
        /// </summary>
        public void UpdateByHour(float hour)
        {
            EnsureInit();

            // 4 main keyframes: Midnight (0h), Midday (12h), Golden (17.5h), Twilight (19.5h)
            PostProcessingLook look;
            if (hour < 6.0f)
            {
                look = PostProcessingLook.Midnight;
            }
            else if (hour < 12.0f)
            {
                float t = Mathf.InverseLerp(6.0f, 12.0f, hour);
                look = PostProcessingLook.Lerp(PostProcessingLook.Midnight, PostProcessingLook.Midday, t);
            }
            else if (hour < 17.5f)
            {
                float t = Mathf.InverseLerp(12.0f, 17.5f, hour);
                look = PostProcessingLook.Lerp(PostProcessingLook.Midday, PostProcessingLook.Golden, t);
            }
            else if (hour < 20.5f)
            {
                float t = Mathf.InverseLerp(17.5f, 20.5f, hour);
                look = PostProcessingLook.Lerp(PostProcessingLook.Golden, PostProcessingLook.Twilight, t);
            }
            else
            {
                float t = Mathf.InverseLerp(20.5f, 24.0f, hour);
                look = PostProcessingLook.Lerp(PostProcessingLook.Twilight, PostProcessingLook.Midnight, t);
            }

            currentLook = look;
            ApplyLook(look);
        }

        /// <summary>
        /// Applies the look settings directly into URP Volume components.
        /// </summary>
        public void ApplyLook(PostProcessingLook look)
        {
            EnsureInit();

            // 1. ACES Tonemapping
            if (_tonemapping != null)
            {
                _tonemapping.mode.Override(TonemappingMode.ACES);
            }

            // 2. Color Adjustments
            if (_colorAdjustments != null)
            {
                // In post-processing.js: exposure is applied before tone mapping.
                // URP postExposure is in EV (log2):
                _colorAdjustments.postExposure.Override(Mathf.Log(Mathf.Max(0.01f, look.exposure), 2f));
                _colorAdjustments.contrast.Override((look.contrast - 1f) * 100f);
                _colorAdjustments.saturation.Override((look.saturation - 1f) * 100f);
                _colorAdjustments.colorFilter.Override(look.balance);
            }

            // 3. Bloom
            if (_bloom != null)
            {
                _bloom.intensity.Override(look.bloom);
                _bloom.threshold.Override(look.bloomThreshold);
                _bloom.scatter.Override(0.40f);
                _bloom.tint.Override(Color.white);
            }

            // 4. Subtle Lens Vignette
            if (_vignette != null)
            {
                _vignette.intensity.Override(vignetteIntensity);
                _vignette.smoothness.Override(vignetteSmoothness);
                _vignette.rounded.Override(false);
            }
        }
    }
}
