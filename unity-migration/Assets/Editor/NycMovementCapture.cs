using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Nyc.Core;
using Nyc.Gameplay;

namespace Nyc.EditorTools
{
    /// <summary>
    /// Deterministic temporal capture for the two regressions that a still matrix cannot expose:
    /// disappearing pedestrians and traffic flicker while the player car advances.
    /// </summary>
    public static class NycMovementCapture
    {
        const int Width = 640;
        const int Height = 360;
        const int Frames = 120;
        const float Dt = 1f / 60f;

        [MenuItem("NYC/10. Capture Movement and Aerial Audit", priority = 10)]
        public static void Capture()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[NYC Movement] Enter Play mode before capturing.");
                return;
            }

            var boot = NycMainBootstrapper.Instance;
            var cam = Camera.main;
            if (boot == null || !boot.IsInitialized || cam == null || boot.controllerManager == null ||
                !boot.controllerManager.IsReady || boot.trafficSystem == null)
            {
                Debug.LogError("[NYC Movement] Bootstrapper/controller is not ready; wait for scene initialization before capture.");
                return;
            }

            string root = Path.GetFullPath("D:/escena/Captures/Movement");
            Directory.CreateDirectory(root);
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previousTarget = cam.targetTexture;
            cam.targetTexture = rt;

            float totalDiff = 0f;
            float maxDiff = 0f;
            int diffFrames = 0;
            Color32[] previous = null;

            try
            {
                boot.controllerManager.SetMode(TravelMode.Car);
                boot.controllerManager.TeleportTo("timessq");
                boot.controllerManager.input.InputEnabled = false;
                boot.controllerManager.input.SetManualInput(new ControllerInput { Forward = 1f });
                boot.cameraManager?.SetPreset("chase", true);
                boot.cameraManager?.ResetForTeleport();

                for (int frame = 0; frame < Frames; frame++)
                {
                    boot.controllerManager.Tick(Dt);
                    boot.trafficSystem.Tick(Dt);
                    boot.cameraManager?.Tick(Dt, frame * Dt);
                    cam.Render();

                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply(false, false);
                    var pixels = tex.GetPixels32();
                    if (previous != null)
                    {
                        float diff = 0f;
                        for (int i = 0; i < pixels.Length; i++)
                        {
                            diff += Mathf.Abs(pixels[i].r - previous[i].r)
                                  + Mathf.Abs(pixels[i].g - previous[i].g)
                                  + Mathf.Abs(pixels[i].b - previous[i].b);
                        }
                        diff /= pixels.Length * 3f * 255f;
                        totalDiff += diff;
                        maxDiff = Mathf.Max(maxDiff, diff);
                        if (diff > 0.18f) diffFrames++;
                    }
                    previous = pixels;
                    if ((frame % 10) == 0)
                        File.WriteAllBytes(Path.Combine(root, $"car_{frame:000}.png"), tex.EncodeToPNG());
                }
                boot.controllerManager.input.ClearTouch();
                boot.controllerManager.input.SetManualInput(default);

                // Aerial roof audit at the One WTC teleport, matching the web helicopter view.
                boot.controllerManager.SetMode(TravelMode.Helicopter);
                boot.controllerManager.TeleportActiveTo(-305.7f, 5210f, Mathf.Atan2(-204f + 305.7f, 5255.2f - 5210f));
                if (boot.controllerManager.helicopter != null) boot.controllerManager.helicopter.Height = 150f;
                boot.cameraManager?.SetPreset("helicopter", true);
                boot.cameraManager?.ResetForTeleport();
                boot.cameraManager?.Tick(0f, Frames * Dt);
                cam.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                tex.Apply(false, false);
                File.WriteAllBytes(Path.Combine(root, "helicopter_roofs.png"), tex.EncodeToPNG());

                string metrics = "{\n" +
                    $"  \"frames\": {Frames},\n" +
                    $"  \"meanFrameDiff\": {totalDiff / Mathf.Max(1, Frames - 1):F6},\n" +
                    $"  \"maxFrameDiff\": {maxDiff:F6},\n" +
                    $"  \"highDiffFrames\": {diffFrames}\n" +
                    "}\n";
                File.WriteAllText(Path.Combine(root, "movement-metrics.json"), metrics);
                AssetDatabase.Refresh();
                Debug.Log($"[NYC Movement] Wrote {Frames} frames, aerial roof capture and metrics to {root}");
            }
            finally
            {
                if (boot.controllerManager.input != null)
                {
                    boot.controllerManager.input.ClearTouch();
                    boot.controllerManager.input.SetManualInput(default);
                    boot.controllerManager.input.InputEnabled = true;
                }
                RenderTexture.active = null;
                cam.targetTexture = previousTarget;
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(tex);
            }
        }
    }
}
