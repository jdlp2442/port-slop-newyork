using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nyc.Rendering.Passes
{
    /// <summary>
    /// URP ScriptableRendererFeature for Screen-Space Contact Shadows & Ambient Occlusion.
    /// Anchors building foundations, curbs, and vehicle tires to the asphalt road surface.
    /// </summary>
    public sealed class NycGroundContactFeature : ScriptableRendererFeature
    {
        [Serializable]
        public sealed class Settings
        {
            public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingTransparents;
            public Shader shader;

            [Header("Contact AO Parameters")]
            [Range(0.0f, 2.0f)] public float intensity = 0.85f;
            [Range(0.1f, 5.0f)] public float sampleRadius = 1.25f;
            [Range(0.01f, 0.5f)] public float depthBias = 0.05f;
            public float maxDistance = 350.0f;
        }

        public Settings settings = new Settings();
        NycGroundContactPass _pass;
        Material _material;

        public override void Create()
        {
            if (settings.shader == null) settings.shader = Shader.Find("Hidden/Nyc/GroundContactAO");
            if (settings.shader != null && _material == null)
            {
                _material = new Material(settings.shader) { name = "GroundContactAOMat" };
            }

            _pass = new NycGroundContactPass(settings, _material);
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

    public sealed class NycGroundContactPass : ScriptableRenderPass
    {
        readonly NycGroundContactFeature.Settings _settings;
        readonly Material _material;
        RenderTargetIdentifier _tempRT;
        const string ProfilerTag = "NycGroundContactAO";
        static readonly int TempRTId = Shader.PropertyToID("_TempContactAORT");
        static readonly int AOParamsId = Shader.PropertyToID("_AOParams");

        public NycGroundContactPass(NycGroundContactFeature.Settings settings, Material material)
        {
            _settings = settings;
            _material = material;
            renderPassEvent = settings.passEvent;
            _tempRT = new RenderTargetIdentifier(TempRTId);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_material == null) return;

            CommandBuffer cmd = CommandBufferPool.Get(ProfilerTag);

            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            cmd.GetTemporaryRT(TempRTId, desc.width, desc.height, 0, FilterMode.Bilinear);

            _material.SetVector(AOParamsId, new Vector4(_settings.intensity, _settings.sampleRadius, _settings.depthBias, _settings.maxDistance));

            RenderTargetIdentifier source = "_CameraColorTexture";
            cmd.Blit(source, _tempRT, _material, 0);
            cmd.Blit(_tempRT, source);

            cmd.ReleaseTemporaryRT(TempRTId);
            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
