using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Nyc.Rendering.Passes;

namespace Nyc.EditorTools
{
    /// <summary>
    /// Creates the URP asset/renderer pair required by the NYC post stack and
    /// keeps the three renderer features registered exactly once.  The web
    /// project has one final render graph; this is its persistent URP
    /// equivalent, so a fresh checkout never depends on hand-editing a
    /// Renderer Data asset.
    /// </summary>
    public static class NycRenderPipelineSetup
    {
        public const string PipelinePath = "Assets/Settings/NYC_UniversalRenderPipelineAsset.asset";
        public const string RendererPath = "Assets/Settings/NYC_UniversalRendererData.asset";

        public static UniversalRenderPipelineAsset EnsureConfigured()
        {
            EnsureSettingsFolder();

            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererPath);
            if (rendererData == null)
            {
                rendererData = ScriptableObject.CreateInstance<UniversalRendererData>();
                rendererData.name = "NYC_UniversalRendererData";
                AssetDatabase.CreateAsset(rendererData, RendererPath);
            }

            EnsureFeature<AtmosphericScatteringFeature>(rendererData,
                "Hidden/Nyc/AtmosphericScattering");
            EnsureFeature<NycGroundContactFeature>(rendererData,
                "Hidden/Nyc/GroundContactAO");
            EnsureFeature<NycBloomFeature>(rendererData,
                "Hidden/Nyc/DualThresholdBloom");
            rendererData.SetDirty();
            EditorUtility.SetDirty(rendererData);

            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelinePath);
            if (pipeline == null)
            {
                pipeline = UniversalRenderPipelineAsset.Create(rendererData);
                pipeline.name = "NYC_UniversalRenderPipelineAsset";
                AssetDatabase.CreateAsset(pipeline, PipelinePath);
            }

            // `Create(rendererData)` serializes the renderer list and default
            // index.  URP 17 exposes the list as a read-only span, so an
            // existing asset is deliberately preserved rather than assigning
            // through a non-existent setter.
            pipeline.supportsHDR = true;
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;

            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return pipeline;
        }

        static void EnsureFeature<T>(UniversalRendererData data, string shaderName)
            where T : ScriptableRendererFeature
        {
            var feature = data.rendererFeatures.OfType<T>().FirstOrDefault();
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<T>();
                feature.name = typeof(T).Name;
                data.rendererFeatures.Add(feature);
                AssetDatabase.AddObjectToAsset(feature, data);
            }

            if (feature is AtmosphericScatteringFeature atmospheric)
                atmospheric.settings.shader = Shader.Find(shaderName);
            else if (feature is NycGroundContactFeature contact)
                contact.settings.shader = Shader.Find(shaderName);
            else if (feature is NycBloomFeature bloom)
                bloom.settings.shader = Shader.Find(shaderName);
            feature.Create();
            EditorUtility.SetDirty(feature);
        }

        static void EnsureSettingsFolder()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Settings"))
                AssetDatabase.CreateFolder("Assets", "Settings");
        }
    }
}
