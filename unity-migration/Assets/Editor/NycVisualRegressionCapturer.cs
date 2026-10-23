using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Nyc.Core;

namespace Nyc.EditorTools
{
    /// <summary>Captures the same fixed camera matrix used by the web/Unity parity checklist.</summary>
    public static class NycVisualRegressionCapturer
    {
        static readonly string[] Locations = { "empire", "centralpark", "owtc", "timessq" };
        static readonly float[] Hours = { 12f, 18.5f, 21f, 0f };

        [MenuItem("NYC/9. Capture Visual Regression Matrix", priority = 9)]
        public static void CaptureMatrix()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[NYC Capture] Enter Play mode before capturing the visual matrix.");
                return;
            }

            var boot = NycMainBootstrapper.Instance;
            var camera = Camera.main;
            if (boot == null || camera == null || boot.controllerManager == null || boot.lightingSystem == null)
            {
                Debug.LogError("[NYC Capture] Missing bootstrapper, camera, controller or lighting system.");
                return;
            }

            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/visual-regression/unity"));
            Directory.CreateDirectory(root);
            int captured = 0;
            int width = Mathf.Max(320, camera.pixelWidth);
            int height = Mathf.Max(180, camera.pixelHeight);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var previous = camera.targetTexture;
            camera.targetTexture = target;

            try
            {
                foreach (string location in Locations)
                foreach (float hour in Hours)
                {
                    boot.controllerManager.TeleportTo(location);
                    boot.cameraManager?.ResetForTeleport();
                    boot.lightingSystem.SetHour(hour);
                    boot.cameraManager?.Tick(0f, 0f);
                    camera.Render();
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    texture.Apply(false, false);
                    string name = $"{location}_{hour:00.0}.png".Replace('.', '_');
                    File.WriteAllBytes(Path.Combine(root, name), texture.EncodeToPNG());
                    captured++;
                }
            }
            finally
            {
                RenderTexture.active = null;
                camera.targetTexture = previous;
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.Refresh();
            Debug.Log($"[NYC Capture] Wrote {captured} Unity frames to {root}");
        }
    }
}
