using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nyc.Rendering.Passes
{
    /// <summary>
    /// URP ScriptableRendererFeature for Dual-Threshold Bloom with Peak Limiting.
    /// Recreates the WebGPU post-processing pipeline from slop-newyork-main/js/post-processing.js:
    /// preserves neon saturation, prevents blowout, and isolates Times Square billboards and vehicle lights.
    /// </summary>
    public sealed class NycBloomFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class Settings
        {
            public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            public Shader shader;

            [Header("Dual-Threshold Parameters")]
            [Range(0.1f, 5.0f)] public float threshold = 1.20f;
            [Range(0.01f, 1.0f)] public float softKnee = 0.30f;
            public float peakLimit = 8.0f;
            [Range(0.0f, 2.0f)] public float intensity = 0.24f;

            [Header("Color Balance & Grading")]
            public Color colorBalance = new Color(1.045f, 1.00f, 0.955f, 1.0f);
            [Range(0.5f, 2.0f)] public float exposure = 1.04f;
            [Range(0.5f, 2.0f)] public float contrast = 1.12f;
            [Range(0.5f, 2.0f)] public float saturation = 1.08f;
            [Range(0.0f, 1.0f)] public float vignette = 0.12f;
        }

        public Settings settings = new Settings();
        NycBloomPass _pass;
        Material _material;

        public override void Create()
        {
            if (settings.shader == null) settings.shader = Shader.Find("Hidden/Nyc/DualThresholdBloom");
            if (settings.shader != null && _material == null)
            {
                _material = new Material(settings.shader) { name = "DualThresholdBloomMat" };
            }

            _pass = new NycBloomPass(settings, _material);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null || _pass == null) return;
            if (renderingData.cameraData.isSceneViewCamera) return;

            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            if (_material != null)
            {
                #if UNITY_EDITOR
                if (!Application.isPlaying) DestroyImmediate(_material);
                else Destroy(_material);
                #else
                Destroy(_material);
                #endif
            }
        }
    }

    public sealed class NycBloomPass : ScriptableRenderPass
    {
        readonly NycBloomFeature.Settings _settings;
        readonly Material _material;
        const string ProfilerTag = "NycDualThresholdBloom";

        static readonly int BloomPrefilterRT = Shader.PropertyToID("_BloomPrefilterRT");
        static readonly int BloomBlurHRT = Shader.PropertyToID("_BloomBlurHRT");
        static readonly int BloomBlurVRT = Shader.PropertyToID("_BloomBlurVRT");
        static readonly int BloomCompositeRT = Shader.PropertyToID("_BloomCompositeRT");

        static readonly int ThresholdParamsId = Shader.PropertyToID("_ThresholdParams");
        static readonly int GradingParamsId = Shader.PropertyToID("_GradingParams");
        static readonly int ColorBalanceId = Shader.PropertyToID("_ColorBalance");
        static readonly int BlurOffsetId = Shader.PropertyToID("_BlurOffset");
        static readonly int BloomTexId = Shader.PropertyToID("_BloomTex");

        public NycBloomPass(NycBloomFeature.Settings settings, Material material)
        {
            _settings = settings;
            _material = material;
            renderPassEvent = settings.passEvent;
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null) return;

            CommandBuffer cmd = CommandBufferPool.Get(ProfilerTag);

            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            int downW = Mathf.Max(desc.width / 2, 1);
            int downH = Mathf.Max(desc.height / 2, 1);

            cmd.GetTemporaryRT(BloomPrefilterRT, downW, downH, 0, FilterMode.Bilinear);
            cmd.GetTemporaryRT(BloomBlurHRT, downW, downH, 0, FilterMode.Bilinear);
            cmd.GetTemporaryRT(BloomBlurVRT, downW, downH, 0, FilterMode.Bilinear);
            cmd.GetTemporaryRT(BloomCompositeRT, desc.width, desc.height, 0, FilterMode.Bilinear);

            // Update parameters from look if available
            float threshold = _settings.threshold;
            float intensity = _settings.intensity;
            Color balance = _settings.colorBalance;
            float exposure = _settings.exposure;
            float contrast = _settings.contrast;
            float saturation = _settings.saturation;

            var pp = FindFirstObjectByType<PostProcessingController>();
            if (pp != null)
            {
                threshold = pp.currentLook.bloomThreshold;
                intensity = pp.currentLook.bloom;
                balance = pp.currentLook.balance;
                exposure = pp.currentLook.exposure;
                contrast = pp.currentLook.contrast;
                saturation = pp.currentLook.saturation;
            }

            _material.SetVector(ThresholdParamsId, new Vector4(threshold, _settings.softKnee, _settings.peakLimit, intensity));
            _material.SetVector(GradingParamsId, new Vector4(exposure, contrast, saturation, _settings.vignette));
            _material.SetVector(ColorBalanceId, balance);

            RenderTargetIdentifier source = "_CameraColorTexture";

            // 1. Prefilter Pass (extract bright highlights with peak limit 8.0)
            cmd.Blit(source, BloomPrefilterRT, _material, 0);

            // 2. Horizontal Blur
            _material.SetVector(BlurOffsetId, new Vector4(1.0f / downW, 0f, 0f, 0f));
            cmd.Blit(BloomPrefilterRT, BloomBlurHRT, _material, 1);

            // 3. Vertical Blur
            _material.SetVector(BlurOffsetId, new Vector4(0f, 1.0f / downH, 0f, 0f));
            cmd.Blit(BloomBlurHRT, BloomBlurVRT, _material, 1);

            // 4. Composite & Grading
            cmd.SetGlobalTexture(BloomTexId, BloomBlurVRT);
            cmd.Blit(source, BloomCompositeRT, _material, 2);
            cmd.Blit(BloomCompositeRT, source);

            cmd.ReleaseTemporaryRT(BloomPrefilterRT);
            cmd.ReleaseTemporaryRT(BloomBlurHRT);
            cmd.ReleaseTemporaryRT(BloomBlurVRT);
            cmd.ReleaseTemporaryRT(BloomCompositeRT);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        static T FindFirstObjectByType<T>() where T : UnityEngine.Object
        {
            #if UNITY_2023_1_OR_NEWER
            return UnityEngine.Object.FindFirstObjectByType<T>();
            #else
            return UnityEngine.Object.FindObjectOfType<T>();
            #endif
        }
    }
}
