using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using Nyc.Core;
using Nyc.Rendering;

namespace Nyc.EditorTools
{
    public static class NycCaptureRunner
    {
        public struct ViewLocation
        {
            public string Name;
            public Vector3 Position;
            public Vector3 EulerAngles;
        }

        public struct TimePreset
        {
            public string Name;
            public float Hour;
            public string LookName;
        }

        public static ViewLocation[] GetLocations()
        {
            float ax7 = -614.4f;
            float sz46 = -321.4f;
            float ax6 = -307.2f;
            float sz57 = -1202.8f;

            if (CityConfig.Ready)
            {
                ax7 = CityConfig.AvenueX("7");
                sz46 = CityConfig.StreetZ(46);
                ax6 = CityConfig.AvenueX("6");
                sz57 = CityConfig.StreetZ(57);
            }

            return new[]
            {
                new ViewLocation
                {
                    Name = "midtown",
                    Position = CityFrame.ToUnity(0f, 22f, 0f),
                    EulerAngles = new Vector3(14f, 205f, 0f)
                },
                new ViewLocation
                {
                    Name = "times_square",
                    Position = CityFrame.ToUnity(ax7 + 3.6f, 18f, sz46),
                    EulerAngles = new Vector3(12f, 180f, 0f)
                },
                new ViewLocation
                {
                    Name = "central_park",
                    Position = CityFrame.ToUnity(ax6 + 3.6f, 32f, sz57),
                    EulerAngles = new Vector3(20f, 180f, 0f)
                },
                new ViewLocation
                {
                    Name = "one_wtc",
                    Position = CityFrame.ToUnity(-305.7f, 35f, 5210f),
                    EulerAngles = new Vector3(25f, 40f, 0f)
                }
            };
        }

        public static TimePreset[] GetTimes()
        {
            return new[]
            {
                new TimePreset { Name = "midday", Hour = 12.0f, LookName = "midday" },
                new TimePreset { Name = "golden", Hour = 18.0f, LookName = "golden" },
                new TimePreset { Name = "twilight", Hour = 20.5f, LookName = "twilight" },
                new TimePreset { Name = "midnight", Hour = 0.0f, LookName = "midnight" }
            };
        }

        [MenuItem("NYC/Capture All Comparable Screenshots")]
        public static void CaptureAll()
        {
            string outDir = Path.GetFullPath("D:/escena/Captures/Unity");
            if (!Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

            // Ensure city is generated
            var builder = UnityEngine.Object.FindAnyObjectByType<Nyc.Generation.ManhattanMeshBuilder>();
            if (builder && (builder.transform.Find("Manhattan") == null || builder.transform.Find("Manhattan").childCount == 0))
            {
                Debug.Log("[NYC Capture] Building Manhattan procedural geometry before capture...");
                ManhattanBakeMenu.CreateMaterials();
                builder.Build();
            }

            var lightSys = UnityEngine.Object.FindAnyObjectByType<LightingSystem>();
            var postCtrl = UnityEngine.Object.FindAnyObjectByType<PostProcessingController>();

            Camera cam = Camera.main;
            if (!cam)
            {
                var camGo = GameObject.Find("CaptureCamera");
                if (!camGo) camGo = new GameObject("CaptureCamera");
                cam = camGo.GetComponent<Camera>();
                if (!cam) cam = camGo.AddComponent<Camera>();
            }

            var camData = cam.GetComponent<UniversalAdditionalCameraData>();
            if (!camData) camData = cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            camData.renderPostProcessing = true;

            int width = 1280;
            int height = 720;
            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            var prevRT = cam.targetTexture;
            cam.targetTexture = rt;
            cam.fieldOfView = 60f;

            var locs = GetLocations();
            var times = GetTimes();

            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);

            try
            {
                foreach (var loc in locs)
                {
                    cam.transform.position = loc.Position;
                    cam.transform.rotation = Quaternion.Euler(loc.EulerAngles);

                    foreach (var time in times)
                    {
                        if (lightSys) lightSys.SetHour(time.Hour);
                        if (postCtrl)
                        {
                            postCtrl.EnsureInit();
                            postCtrl.UpdateByHour(time.Hour);
                        }

                        cam.Render();

                        RenderTexture.active = rt;
                        tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                        tex.Apply();
                        RenderTexture.active = null;

                        byte[] bytes = tex.EncodeToPNG();
                        string fileName = $"{loc.Name}_{time.Name}.png";
                        string filePath = Path.Combine(outDir, fileName);
                        File.WriteAllBytes(filePath, bytes);
                        Debug.Log($"[NYC Capture] Saved: {filePath} ({bytes.Length} bytes)");
                    }
                }
            }
            finally
            {
                cam.targetTexture = prevRT;
                RenderTexture.active = null;
                UnityEngine.Object.DestroyImmediate(rt);
                UnityEngine.Object.DestroyImmediate(tex);
            }

            Debug.Log("[NYC Capture] Finished capturing all 16 Unity screenshots.");
        }
    }
}
