#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Nyc.Editor
{
    public static class NycVolumeProfileCreator
    {
        public const string AssetPath = "Assets/Settings/NycGlobalPostProcessingProfile.asset";

        [MenuItem("NYC/Create or Update Post-Processing Volume Profile")]
        public static void CreateProfile()
        {
            var profile = EnsureProfileAsset();
            Nyc.EditorTools.NycRenderPipelineSetup.EnsureConfigured();
            var volume = Object.FindAnyObjectByType<Volume>();
            if (volume != null)
            {
                volume.isGlobal = true;
                volume.sharedProfile = profile;
                EditorUtility.SetDirty(volume);
            }
            Debug.Log($"[NYC] VolumeProfile created/updated successfully at {AssetPath}");
        }

        public static VolumeProfile EnsureProfileAsset()
        {
            const string folderPath = "Assets/Settings";
            if (!AssetDatabase.IsValidFolder(folderPath))
            {
                AssetDatabase.CreateFolder("Assets", "Settings");
            }

            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(AssetPath);

            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, AssetPath);
            }

            // 1. Tonemapping ACES
            if (!profile.TryGet(out Tonemapping tonemapping))
                tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.ACES);

            // 2. Color Adjustments
            if (!profile.TryGet(out ColorAdjustments colorAdjustments))
                colorAdjustments = profile.Add<ColorAdjustments>(true);
            colorAdjustments.postExposure.Override(0.05f);
            colorAdjustments.contrast.Override(12.0f);
            colorAdjustments.saturation.Override(8.0f);
            colorAdjustments.colorFilter.Override(new Color(1.045f, 1.00f, 0.955f, 1.0f));

            // 3. Bloom
            if (!profile.TryGet(out Bloom bloom))
                bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.20f);
            bloom.intensity.Override(0.24f);
            bloom.scatter.Override(0.40f);
            bloom.clamp.Override(8.0f);
            bloom.highQualityFiltering.Override(true);

            // 4. Vignette
            if (!profile.TryGet(out Vignette vignette))
                vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.12f);
            vignette.smoothness.Override(0.40f);
            vignette.rounded.Override(false);

            // 5. Depth of Field
            if (!profile.TryGet(out DepthOfField dof))
                dof = profile.Add<DepthOfField>(true);
            dof.mode.Override(DepthOfFieldMode.Gaussian);
            dof.gaussianStart.Override(15.0f);
            dof.gaussianEnd.Override(350.0f);
            dof.gaussianMaxRadius.Override(1.2f);
            dof.highQualitySampling.Override(true);

            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return profile;
        }
    }
}
#endif
