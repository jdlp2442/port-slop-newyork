using System.IO;
using UnityEditor;
using UnityEngine;
using Nyc.Core;
using Nyc.Gameplay;

namespace Nyc.EditorTools
{
    /// <summary>
    /// Deterministic drone trailer capture. The sequence deliberately uses several camera shots:
    /// chase/action show the aircraft, then POV restores the dual-weapon hub and linked reticles.
    /// Frames are written at 30 fps so Unity Recorder/ffmpeg can encode them without timing drift.
    /// </summary>
    public static class NycDroneTrailerCapture
    {
        const int Width = 960;
        const int Height = 540;
        const int Fps = 30;
        const int Frames = 360;
        const float Dt = 1f / Fps;

        [MenuItem("NYC/12. Capture Drone Trailer", priority = 12)]
        public static void Capture()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("[NYC Drone Trailer] Enter Play mode before capturing.");
                return;
            }

            var boot = NycMainBootstrapper.Instance;
            var cam = Camera.main;
            if (boot == null || !boot.IsInitialized || cam == null || boot.controllerManager == null ||
                !boot.controllerManager.IsReady || boot.cameraManager == null)
            {
                Debug.LogError("[NYC Drone Trailer] Bootstrapper/controller/camera is not ready.");
                return;
            }

            string root = Path.GetFullPath("D:/escena/Captures/DroneTrailer");
            Directory.CreateDirectory(root);
            var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            var previousTarget = cam.targetTexture;
            cam.targetTexture = rt;

            var controller = boot.controllerManager;
            var weapons = controller.helicopter ? controller.helicopter.GetComponent<DroneWeaponSystem>() : null;
            try
            {
                controller.SetMode(TravelMode.Helicopter);
                controller.TeleportActiveTo(-305.7f, 5210f,
                    Mathf.Atan2(-204f + 305.7f, 5255.2f - 5210f));
                if (controller.helicopter) controller.helicopter.Height = 150f;
                controller.input.InputEnabled = false;
                controller.input.SetManualInput(default);

                string[] shots = { "chase", "action", "pov" };
                int[] shotStarts = { 0, 60, 120 };
                int captureIndex = 0;

                for (int frame = 0; frame < Frames; frame++)
                {
                    if (frame == shotStarts[0]) boot.cameraManager.SetPreset(shots[0], true);
                    if (frame == shotStarts[1]) boot.cameraManager.SetPreset(shots[1], true);
                    if (frame == shotStarts[2]) boot.cameraManager.SetPreset(shots[2], true);

                    float t = frame * Dt;
                    float yaw = Mathf.Sin(t * 0.72f) * 0.65f;
                    float forward = frame < 80 ? 0.25f : 0.72f;
                    float turn = frame < 60 ? 0.12f : Mathf.Sin(t * 0.55f) * 0.22f;
                    float vertical = frame < 80 ? 0.16f : Mathf.Sin(t * 0.8f) * 0.12f;
                    controller.input.SetManualInput(new ControllerInput
                    {
                        Forward = forward,
                        Left = turn > 0f ? turn : 0f,
                        Right = turn < 0f ? -turn : 0f,
                        Up = vertical > 0f ? vertical : 0f,
                        Down = vertical < 0f ? -vertical : 0f,
                        Boost = frame > 80 && frame < 220 ? 1f : 0f
                    });

                    controller.Tick(Dt);
                    boot.trafficSystem?.Tick(Dt);

                    if (weapons != null)
                    {
                        Vector2 leftAim = new Vector2(-0.30f + Mathf.Sin(t * 1.2f) * 0.30f,
                            Mathf.Sin(t * 0.9f) * 0.22f);
                        Vector2 rightAim = new Vector2(0.30f + Mathf.Sin(t * 1.2f + 0.14f) * 0.30f,
                            Mathf.Sin(t * 0.9f + 0.18f) * 0.22f);
                        bool firing = frame >= 190 && frame <= 245;
                        weapons.SetDemoAim(leftAim, rightAim, firing, firing);
                    }

                    boot.cameraManager.Tick(Dt, t);
                    cam.Render();
                    RenderTexture.active = rt;
                    tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    tex.Apply(false, false);
                    File.WriteAllBytes(Path.Combine(root, $"frame_{captureIndex:0000}.png"), tex.EncodeToPNG());
                    captureIndex++;
                }

                File.WriteAllText(Path.Combine(root, "trailer-manifest.json"),
                    "{\n" +
                    $"  \"fps\": {Fps},\n" +
                    $"  \"frames\": {Frames},\n" +
                    "  \"shots\": [\n" +
                    "    { \"name\": \"chase\", \"startFrame\": 0, \"endFrame\": 59, \"hub\": false },\n" +
                    "    { \"name\": \"action\", \"startFrame\": 60, \"endFrame\": 119, \"hub\": false },\n" +
                    "    { \"name\": \"drone_pov_dual_weapon\", \"startFrame\": 120, \"endFrame\": 359, \"hub\": true }\n" +
                    "  ]\n" +
                    "}\n");
                AssetDatabase.Refresh();
                Debug.Log($"[NYC Drone Trailer] Wrote {Frames} frames at {Fps} fps to {root}");
            }
            finally
            {
                if (weapons != null) weapons.ClearDemoAim();
                if (controller.input != null)
                {
                    controller.input.SetManualInput(default);
                    controller.input.InputEnabled = true;
                }
                RenderTexture.active = null;
                cam.targetTexture = previousTarget;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(tex);
            }
        }
    }
}
