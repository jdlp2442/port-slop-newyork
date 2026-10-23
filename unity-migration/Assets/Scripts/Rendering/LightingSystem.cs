using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nyc.Rendering
{
    /// <summary>
    /// Drives the whole atmosphere from TimeOfDayPreset assets: sun, ambient, sky material, fog,
    /// the global city uniforms read by every NYC shader, and the URP post-processing overrides.
    /// Port of LightingSystem + createPostProcessing from the three.js project.
    ///
    /// Presets are keyframed by their `hour`; SetHour blends the two neighbours so a full 24 h
    /// cycle is continuous, while 13:30 / 17:45 / 19:15 / 23:00 reproduce the original looks
    /// exactly.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class LightingSystem : MonoBehaviour
    {
        [Header("Presets (any order; blended by `hour`)")]
        [SerializeField] TimeOfDayPreset[] presets = Array.Empty<TimeOfDayPreset>();

        [Header("Scene references")]
        [SerializeField] Light sun;
        [SerializeField] Material skyMaterial;
        [SerializeField] VolumeProfile volumeProfile;
        [SerializeField] ReflectionProbe environmentProbe;

        [Header("Day / night cycle")]
        [SerializeField] bool runCycle;
        [SerializeField, Range(0f, 24f)] float hour = 17.75f;
        [Tooltip("Real minutes per in-game hour. 2 = a full day in 48 real minutes.")]
        [Min(0.01f)] [SerializeField] float realMinutesPerGameHour = 2f;
        [Tooltip("Seconds between ambient probe refreshes while the cycle runs.")]
        [SerializeField] float ambientRefreshInterval = 0.25f;

        [Header("Shadows")]
        [SerializeField] float streetShadowDistance = 300f;
        [SerializeField] float airShadowDistance = 720f;
        [SerializeField] float airborneAltitude = 80f;

        [Header("City bounds (filled from CityConfig when available)")]
        [SerializeField] Vector4 centralParkBounds = new Vector4(-845f, -14f, 1397f, 5491f);
        [SerializeField] Vector4 islandBounds = new Vector4(-2117f, -6512f, 2569f, 7090f);

        public TimeOfDayPreset Active { get; private set; }
        public float Hour => hour;
        public float NightFactor => Active != null ? Active.nightFactor : 0f;
        public static LightingSystem Instance { get; private set; }
        public event Action<TimeOfDayPreset> PresetChanged;

        /// <summary>Editor/bootstrap hook used by the one-click scene builder.</summary>
        public void Configure(TimeOfDayPreset[] presetAssets, Light sunLight, VolumeProfile profile)
        {
            presets = presetAssets ?? Array.Empty<TimeOfDayPreset>();
            sun = sunLight;
            volumeProfile = profile;
            _sorted = (TimeOfDayPreset[])presets.Clone();
            Array.Sort(_sorted, (a, b) => a.hour.CompareTo(b.hour));
            CacheVolume();
            if (_sorted.Length > 0) Apply(Evaluate(hour));
        }

        TimeOfDayPreset _blend;
        TimeOfDayPreset[] _sorted;
        Bloom _bloom;
        ColorAdjustments _grade;
        Tonemapping _tonemap;
        Vignette _vignette;
        UniversalRenderPipelineAsset _urp;
        float _ambientTimer;
        bool _airborne;
        bool _globalsPushed;

        // ------------------------------------------------------------------ lifecycle

        void OnEnable()
        {
            Instance = this;
            _blend = ScriptableObject.CreateInstance<TimeOfDayPreset>();
            _blend.hideFlags = HideFlags.HideAndDontSave;

            if (!skyMaterial)
            {
                var s = Shader.Find("Nyc/Sky");
                if (s) skyMaterial = new Material(s) { name = "SkyProcedural" };
            }
            if (skyMaterial) RenderSettings.skybox = skyMaterial;

            CacheVolume();
            CachePipeline();

            if (presets != null && presets.Length > 0)
            {
                _sorted = (TimeOfDayPreset[])presets.Clone();
                Array.Sort(_sorted, (a, b) => a.hour.CompareTo(b.hour));
                Apply(Evaluate(hour));
            }
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (!_blend) return;
            if (Application.isPlaying) Destroy(_blend); else DestroyImmediate(_blend);
        }

        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            CacheVolume();
            if (presets != null && presets.Length > 0)
            {
                _sorted = (TimeOfDayPreset[])presets.Clone();
                Array.Sort(_sorted, (a, b) => a.hour.CompareTo(b.hour));
                Apply(Evaluate(hour));
            }
        }

        void Update()
        {
            Shader.SetGlobalFloat(NycShaderIDs.Time, Time.time);

            if (runCycle)
            {
                float hoursPerSecond = 1f / Mathf.Max(0.01f, realMinutesPerGameHour * 60f);
                hour = Mathf.Repeat(hour + Time.deltaTime * hoursPerSecond, 24f);
                Apply(Evaluate(hour));

                _ambientTimer += Time.deltaTime;
                if (_ambientTimer >= ambientRefreshInterval)
                {
                    _ambientTimer = 0f;
                    DynamicGI.UpdateEnvironment();
                }
            }

            // The WebGPU materials receive live uniform values every frame.  URP
            // keeps global shader state between cameras, so re-publish the active
            // preset even when the clock is paused or a reflection probe is not
            // being refreshed.
            if (Active != null) PushCityGlobals(Active);

            UpdateShadowDistance();
        }

        // ------------------------------------------------------------------ public API

        public void ApplyPreset(string id)
        {
            if (presets == null) return;
            for (int i = 0; i < presets.Length; i++)
            {
                if (presets[i] != null && presets[i].id == id)
                {
                    hour = presets[i].hour;
                    Apply(presets[i]);
                    return;
                }
            }
            Debug.LogWarning($"[NYC] No time-of-day preset with id '{id}'.");
        }

        public void ApplyPreset(int index)
        {
            if (presets == null || index < 0 || index >= presets.Length) return;
            hour = presets[index].hour;
            Apply(presets[index]);
        }

        /// <summary>Continuous cycle entry point. 0..24, wraps.</summary>
        public void SetHour(float h)
        {
            hour = Mathf.Repeat(h, 24f);
            Apply(Evaluate(hour));
        }

        public void SetCycleRunning(bool running) => runCycle = running;

        // ------------------------------------------------------------------ evaluation

        TimeOfDayPreset Evaluate(float h)
        {
            if (_sorted == null || _sorted.Length == 0) return null;
            if (_sorted.Length == 1) return _sorted[0];
            if (_blend == null)
            {
                _blend = ScriptableObject.CreateInstance<TimeOfDayPreset>();
                _blend.hideFlags = HideFlags.HideAndDontSave;
            }

            int next = 0;
            while (next < _sorted.Length && _sorted[next].hour <= h) next++;

            TimeOfDayPreset a, b;
            if (next == 0) { a = _sorted[_sorted.Length - 1]; b = _sorted[0]; }
            else if (next >= _sorted.Length) { a = _sorted[_sorted.Length - 1]; b = _sorted[0]; }
            else { a = _sorted[next - 1]; b = _sorted[next]; }

            float span = b.hour - a.hour;
            if (span <= 0f) span += 24f;
            float local = h - a.hour;
            if (local < 0f) local += 24f;
            float t = Mathf.Clamp01(local / Mathf.Max(0.0001f, span));

            return TimeOfDayPreset.Lerp(a, b, t, _blend);
        }

        // ------------------------------------------------------------------ apply

        void Apply(TimeOfDayPreset p)
        {
            if (p == null) return;
            Active = p;

            // Sun -------------------------------------------------------------------------
            if (sun)
            {
                Vector3 dir = p.sunDir.sqrMagnitude > 1e-6f ? p.sunDir.normalized : Vector3.up;
                sun.transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
                sun.color = p.sunColor;
                sun.intensity = p.sunIntensity;
                sun.shadows = LightShadows.Soft;
                sun.shadowBias = 0.05f;
                sun.shadowNormalBias = 0.35f;
            }

            // Ambient: a hemisphere bounce, matching THREE.HemisphereLight ------------------
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = p.hemiSky * p.hemiIntensity;
            RenderSettings.ambientEquatorColor = Color.Lerp(p.hemiSky, p.hemiGround, 0.5f) * p.hemiIntensity;
            RenderSettings.ambientGroundColor = p.hemiGround * p.hemiIntensity;
            RenderSettings.ambientIntensity = 1f;

            // Fog (FogExp2) ----------------------------------------------------------------
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = p.fogColor;
            RenderSettings.fogDensity = p.fogDensity;

            RenderSettings.reflectionIntensity = p.envIntensity;

            // Sky material ----------------------------------------------------------------
            if (skyMaterial)
            {
                skyMaterial.SetFloat(NycShaderIDs.Turbidity, p.turbidity);
                skyMaterial.SetFloat(NycShaderIDs.Rayleigh, p.rayleigh);
                skyMaterial.SetFloat(NycShaderIDs.MieCoefficient, p.mieCoefficient);
                skyMaterial.SetFloat(NycShaderIDs.MieG, p.mieDirectionalG);
                skyMaterial.SetVector(NycShaderIDs.SkySunDir, p.sunDir.normalized);
                skyMaterial.SetFloat(NycShaderIDs.SkyExposure, p.skyExposure);
                skyMaterial.SetFloat(NycShaderIDs.NightFactor, p.nightFactor);
                skyMaterial.SetFloat(NycShaderIDs.CloudCoverage, p.cloudCoverage);
                skyMaterial.SetFloat(NycShaderIDs.CloudDensity, p.cloudDensity);
                skyMaterial.SetFloat(NycShaderIDs.CloudScale, 0.00018f);
                skyMaterial.SetFloat(NycShaderIDs.CloudElevation, 0.52f);
                skyMaterial.SetColor(NycShaderIDs.NightColor, new Color(0.043f, 0.082f, 0.157f, 1f));
                RenderSettings.skybox = skyMaterial;
            }

            PushCityGlobals(p);
            ApplyGrade(p);

            if (environmentProbe) environmentProbe.RenderProbe();

            PresetChanged?.Invoke(p);
        }

        void PushCityGlobals(TimeOfDayPreset p)
        {
            Shader.SetGlobalVector(NycShaderIDs.SunColor, p.sunColor.linear);
            Shader.SetGlobalVector(NycShaderIDs.SkyTopColor, p.skyTop.linear);
            Shader.SetGlobalVector(NycShaderIDs.SkyHorizonColor, p.skyHorizon.linear);
            Shader.SetGlobalVector(NycShaderIDs.SunDir, p.sunDir.normalized);
            Shader.SetGlobalFloat(NycShaderIDs.InteriorGlow, p.interiorGlow);
            Shader.SetGlobalFloat(NycShaderIDs.RoadWetness, p.roadWetness);
            Shader.SetGlobalFloat(NycShaderIDs.CanyonBounce, p.canyonBounce);

            if (!_globalsPushed && Core.CityConfig.Ready)
            {
                Vector4 cp = Core.CityConfig.CentralParkBounds; // minX, maxX, minZ, maxZ (source)
                centralParkBounds = new Vector4(cp.x, cp.y, -cp.w, -cp.z);

                Vector4 b = IslandBoundsFromGeo();
                if (b != Vector4.zero) islandBounds = b;
                _globalsPushed = true;
            }

            Shader.SetGlobalVector(NycShaderIDs.CpBounds, centralParkBounds);
            Shader.SetGlobalVector(NycShaderIDs.IslandBounds, islandBounds);
        }

        static Vector4 IslandBoundsFromGeo()
        {
            var geo = Core.CityConfig.Geo;
            if (geo?.Manhattan == null || geo.Manhattan.Length == 0) return Vector4.zero;

            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < geo.Manhattan.Length; i++)
            {
                Vector2 v = geo.Manhattan[i];
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minZ) minZ = v.y;
                if (v.y > maxZ) maxZ = v.y;
            }
            // Source z (south positive) -> Unity z = -z.
            return new Vector4(minX, -maxZ, maxX, -minZ);
        }

        // ------------------------------------------------------------------ grading

        void CacheVolume()
        {
            if (volumeProfile == null) return;
            volumeProfile.TryGet(out _bloom);
            volumeProfile.TryGet(out _grade);
            volumeProfile.TryGet(out _tonemap);
            volumeProfile.TryGet(out _vignette);
        }

        void ApplyGrade(TimeOfDayPreset p)
        {
            if (_tonemap) _tonemap.mode.Override(TonemappingMode.ACES);

            if (_grade)
            {
                _grade.postExposure.Override(Mathf.Log(Mathf.Max(0.01f, p.exposure), 2f));
                _grade.contrast.Override((p.contrast - 1f) * 100f);
                _grade.saturation.Override((p.saturation - 1f) * 100f);
                _grade.colorFilter.Override(new Color(p.whiteBalance.x, p.whiteBalance.y, p.whiteBalance.z, 1f));
            }

            if (_bloom)
            {
                _bloom.intensity.Override(p.bloom);
                _bloom.threshold.Override(p.bloomThreshold);
                _bloom.scatter.Override(0.7f);
                _bloom.tint.Override(Color.white);
            }

            if (_vignette)
            {
                _vignette.intensity.Override(0.12f);
                _vignette.smoothness.Override(0.4f);
            }
        }

        // ------------------------------------------------------------------ shadows

        void CachePipeline()
        {
            _urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        }

        void UpdateShadowDistance()
        {
            if (!_urp || !sun) return;

            Camera cam = Camera.main;
            bool airborne = cam && cam.transform.position.y > airborneAltitude;
            if (airborne == _airborne) return;

            _airborne = airborne;
            _urp.shadowDistance = airborne ? airShadowDistance : streetShadowDistance;
        }

        // ------------------------------------------------------------------ editor helpers

        [ContextMenu("Apply Current Hour")]
        void ApplyCurrentHour() => SetHour(hour);

        [ContextMenu("Golden Hour")]
        void Golden() => ApplyPreset("golden");

        [ContextMenu("Midday")]
        void Midday() => ApplyPreset("midday");

        [ContextMenu("Twilight")]
        void Twilight() => ApplyPreset("twilight");

        [ContextMenu("Midnight")]
        void Midnight() => ApplyPreset("midnight");
    }
}
