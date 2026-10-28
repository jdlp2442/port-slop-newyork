using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Nyc.Core;

namespace Nyc.EditorTools
{
    /// <summary>
    /// Captures the same fixed camera matrix used by the web/Unity parity checklist.
    ///
    /// Parity contract with the web capturer (tools/capture-web-cdp.mjs + js/main.js):
    ///   loc=midtown      -> teleportTo(0, 0, Math.PI)           (the grid ORIGIN, not the Empire State)
    ///   loc=times_square  -> TELEPORT_LOCATIONS.timessq
    ///   loc=central_park  -> TELEPORT_LOCATIONS.centralpark
    ///   loc=one_wtc       -> TELEPORT_LOCATIONS.owtc
    ///   time=midday|golden|twilight|midnight -> lightingSystem.applyPreset(time)
    ///   camera=chase      -> selectCamera('chase', true)
    /// File names are {loc}_{time}.png so Tools/parity/compare-captures.mjs pairs them directly.
    /// </summary>
    public static class NycVisualRegressionCapturer
    {
        const int CaptureWidth = 1280;   // matches the web CDP viewport exactly
        const int CaptureHeight = 720;

        static readonly string[] Locations = { "midtown", "times_square", "central_park", "one_wtc" };
        static readonly string[] Times = { "midday", "golden", "twilight", "midnight" };

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

            string capturesUnity = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Captures/Unity"));
            string root = Directory.Exists(Path.GetDirectoryName(capturesUnity))
                ? capturesUnity
                : Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/visual-regression/unity"));
            Directory.CreateDirectory(root);
            int captured = 0;
            var texture = new Texture2D(CaptureWidth, CaptureHeight, TextureFormat.RGB24, false);
            var target = new RenderTexture(CaptureWidth, CaptureHeight, 24, RenderTextureFormat.ARGB32);
            var previous = camera.targetTexture;
            camera.targetTexture = target;

            try
            {
                foreach (string location in Locations)
                foreach (string time in Times)
                {
                    // Same destination the web uses for each query param (js/main.js:615-628).
                    switch (location)
                    {
                        case "midtown": boot.controllerManager.TeleportActiveTo(0f, 0f, Mathf.PI); break;
                        case "times_square": boot.controllerManager.TeleportTo("timessq"); break;
                        case "central_park": boot.controllerManager.TeleportTo("centralpark"); break;
                        default: boot.controllerManager.TeleportTo("owtc"); break;
                    }

                    boot.cameraManager?.SetPreset("chase", true);
                    boot.cameraManager?.ResetForTeleport();
                    boot.lightingSystem.ApplyPreset(time);   // raises PresetChanged -> post-processing follows
                    boot.cameraManager?.Tick(0f, 0f);
                    // Teleporting inside an editor command does not wait for LateUpdate. Refresh
                    // the same distance/frustum culling pass before the off-screen render, or the
                    // capture can contain the previous location's visible chunks.
                    boot.cullingSystem?.UpdateCulling(camera);
                    camera.Render();
                    RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, CaptureWidth, CaptureHeight), 0, 0);
                    texture.Apply(false, false);
                    File.WriteAllBytes(Path.Combine(root, $"{location}_{time}.png"), texture.EncodeToPNG());
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
