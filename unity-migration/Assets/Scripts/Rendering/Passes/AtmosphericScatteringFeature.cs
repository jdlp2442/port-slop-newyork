using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
#if UNITY_6000_0_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
#endif

namespace Nyc.Rendering.Passes
{
    /// <summary>
    /// URP ScriptableRendererFeature for low-altitude volumetric fog and atmospheric scattering.
    /// Simulates Rayleigh/Mie scattering along long avenues and over the Hudson/East River.
    /// Compatible with both Unity 6 RenderGraph and legacy/headless pipelines.
    /// </summary>
    public sealed class AtmosphericScatteringFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class Settings
        {
            public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            public Shader shader;

            [Header("Fog Parameters")]
            [Range(0.0001f, 0.05f)] public float fogDensity = 0.0016f;
            [Range(0.001f, 0.05f)]  public float heightFalloff = 0.008f;
            public float startDistance = 20.0f;
            public float maxDistance = 3000.0f;

            [Header("Atmospheric Haze")]
            public Color hazeColor = new Color(0.72f, 0.82f, 0.95f, 1.0f);
            [Range(0.1f, 3.0f)] public float inscatteringBoost = 1.35f;
        }

        public Settings settings = new Settings();
        AtmosphericScatteringPass _pass;
        Material _material;

        public override void Create()
        {
            if (settings.shader == null) settings.shader = Shader.Find("Hidden/Nyc/AtmosphericScattering");
            if (settings.shader != null && _material == null)
            {
                _material = new Material(settings.shader) { name = "AtmosphericScatteringMat" };
            }

            _pass = new AtmosphericScatteringPass(settings, _material);
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

    public sealed class AtmosphericScatteringPass : ScriptableRenderPass
    {
        readonly AtmosphericScatteringFeature.Settings _settings;
        readonly Material _material;
        RenderTargetIdentifier _tempRT;
        const string ProfilerTag = "NycAtmosphericScattering";
        static readonly int TempRTId = Shader.PropertyToID("_TempAtmosphericRT");

        public AtmosphericScatteringPass(AtmosphericScatteringFeature.Settings settings, Material material)
        {
            _settings = settings;
            _material = material;
            renderPassEvent = settings.passEvent;
            _tempRT = new RenderTargetIdentifier(TempRTId);
            ConfigureInput(ScriptableRenderPassInput.Depth);
        }

        void UpdateMaterial()
        {
            Vector3 sunDir = new Vector3(0.45f, 0.75f, 0.45f).normalized;
            Color sunColor = new Color(1.0f, 0.96f, 0.88f);
            float sunIntensity = 1.0f;
            float nightFactor = 0f;
            float fogDensity = _settings.fogDensity;
            Color haze = _settings.hazeColor;

            if (LightingSystem.Instance != null && LightingSystem.Instance.Active != null)
            {
                var active = LightingSystem.Instance.Active;
                Vector3 uDir = Core.CityFrame.DirToUnity(active.sunDir);
                sunDir = uDir.sqrMagnitude > 1e-6f ? uDir.normalized : Vector3.up;
                sunColor = active.sunColor;
                sunIntensity = active.sunIntensity;
                nightFactor = active.nightFactor;
                if (active.fogDensity > 0.00001f) fogDensity = active.fogDensity;
                haze = active.fogColor;
            }

            _material.SetVector(NycShaderIDs.FogParams, new Vector4(fogDensity, 900.0f, _settings.startDistance, _settings.maxDistance));
            _material.SetVector(NycShaderIDs.PassSunDir, new Vector4(sunDir.x, sunDir.y, sunDir.z, sunIntensity));
            _material.SetVector(NycShaderIDs.PassSunColor, new Vector4(sunColor.r, sunColor.g, sunColor.b, nightFactor));
            _material.SetVector(NycShaderIDs.HazeColor, new Vector4(haze.r, haze.g, haze.b, _settings.inscatteringBoost));
        }

#if UNITY_6000_0_OR_NEWER
        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null) return;

            UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            if (cameraData.isSceneViewCamera || !resourcesData.cameraColor.IsValid()) return;

            UpdateMaterial();

            TextureDesc desc = renderGraph.GetTextureDesc(resourcesData.cameraColor);
            desc.name = "_AtmosphericScatteringTemp";
            desc.clearBuffer = false;
            TextureHandle tempTex = renderGraph.CreateTexture(desc);

            var blitParams = new RenderGraphUtils.BlitMaterialParameters(resourcesData.cameraColor, tempTex, _material, 0);
            renderGraph.AddBlitPass(blitParams, ProfilerTag);

            renderGraph.AddBlitPass(tempTex, resourcesData.cameraColor, Vector2.one, Vector2.zero, passName: "AtmosphericScatteringRestore");
        }
#else
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null) return;

            CommandBuffer cmd = CommandBufferPool.Get(ProfilerTag);

            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            cmd.GetTemporaryRT(TempRTId, desc.width, desc.height, 0, FilterMode.Bilinear);

            UpdateMaterial();

            RenderTargetIdentifier source = "_CameraColorTexture";
            cmd.Blit(source, _tempRT, _material, 0);
            cmd.Blit(_tempRT, source);

            cmd.ReleaseTemporaryRT(TempRTId);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
#endif
    }
}
