using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Nyc.Data;
using Debug = UnityEngine.Debug;

namespace Nyc.EditorTools
{
    /// <summary>
    /// Editor importer for NYC GeoData.
    /// Runs tools/build-geodata.mjs or ingests slop-newyork-main/js/nyc-geodata.js into
    /// Assets/StreamingAssets/nyc/nyc-geodata.json and nyc-geodata.js.
    /// </summary>
    public static class NycGeoDataImporter
    {
        public const string DefaultStreamingFolder = "Assets/StreamingAssets/nyc";
        public const string JsonFileName = "nyc-geodata.json";
        public const string JsFileName = "nyc-geodata.js";

        [MenuItem("NYC/0. Import GeoData from Tools", priority = 0)]
        public static void ImportGeoDataMenu()
        {
            ImportGeoData(runNodeBuilder: true);
        }

        public static bool ImportGeoData(bool runNodeBuilder = true)
        {
            string workspaceRoot = FindWorkspaceRoot();
            string toolsDir = Path.Combine(workspaceRoot, "slop-newyork-main", "tools");
            string builderScript = Path.Combine(toolsDir, "build-geodata.mjs");
            string sourceJs = Path.Combine(workspaceRoot, "slop-newyork-main", "js", "nyc-geodata.js");

            string destDir = Path.Combine(Application.dataPath, "StreamingAssets", "nyc");
            Directory.CreateDirectory(destDir);
            string destJson = Path.Combine(destDir, JsonFileName);
            string destJs = Path.Combine(destDir, JsFileName);
            string rootJson = Path.Combine(Application.dataPath, "StreamingAssets", JsonFileName);

            if (runNodeBuilder && File.Exists(builderScript))
            {
                try
                {
                    Debug.Log("[NycGeoDataImporter] Running build-geodata.mjs with Node.js...");
                    var psi = new ProcessStartInfo
                    {
                        FileName = "node",
                        Arguments = $"--max-old-space-size=4096 \"{builderScript}\" \"{sourceJs}\"",
                        WorkingDirectory = Path.GetDirectoryName(builderScript),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };
                    using var proc = Process.Start(psi);
                    if (proc != null)
                    {
                        proc.WaitForExit(60000);
                        if (proc.ExitCode == 0)
                        {
                            Debug.Log("[NycGeoDataImporter] Node build completed successfully.");
                        }
                        else
                        {
                            string err = proc.StandardError.ReadToEnd();
                            Debug.LogWarning($"[NycGeoDataImporter] Node builder exit code {proc.ExitCode}: {err}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[NycGeoDataImporter] Could not run Node.js directly: {ex.Message}. Falling back to precompiled JS.");
                }
            }

            if (!File.Exists(sourceJs))
            {
                // Try alternate candidate locations
                string[] candidates = new[]
                {
                    Path.Combine(workspaceRoot, "slop-newyork-main", "slop-newyork-main", "js", "nyc-geodata.js"),
                    Path.Combine(workspaceRoot, "js", "nyc-geodata.js")
                };
                foreach (var c in candidates)
                {
                    if (File.Exists(c)) { sourceJs = c; break; }
                }
            }

            if (!File.Exists(sourceJs))
            {
                Debug.LogError($"[NycGeoDataImporter] Source geodata file not found at: {sourceJs}");
                return false;
            }

            string rawText = File.ReadAllText(sourceJs);
            // Copy raw JS file
            File.WriteAllText(destJs, rawText);

            // Extract pure JSON payload
            string json = rawText;
            if (rawText.TrimStart()[0] != '{')
            {
                int marker = rawText.IndexOf("window.NYC.GEO", StringComparison.Ordinal);
                if (marker >= 0)
                {
                    int eq = rawText.IndexOf('=', marker);
                    int start = rawText.IndexOf('{', eq);
                    int end = rawText.LastIndexOf("};", StringComparison.Ordinal);
                    if (start >= 0 && end > start) json = rawText.Substring(start, end - start + 1);
                }
            }
            File.WriteAllText(destJson, json);
            File.WriteAllText(rootJson, json);

            // Verify with NycGeoData parser
            try
            {
                var data = NycGeoData.LoadFromFile(destJson);
                Debug.Log($"[NycGeoDataImporter] Successfully loaded and verified GeoData: " +
                          $"{data.Streets.Count} streets, {data.Avenues.Count} avenues, " +
                          $"{data.Blocks.Count} blocks, {data.Roads.Count} roads, " +
                          $"{data.Landmarks.Count} landmarks, {data.Parks.Count} parks, " +
                          $"{data.Piers.Count} piers, {data.Plazas.Count} plazas.");
                AssetDatabase.Refresh();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[NycGeoDataImporter] Failed to parse imported geodata: {ex.Message}");
                return false;
            }
        }

        static string FindWorkspaceRoot()
        {
            string dir = Directory.GetCurrentDirectory();
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "slop-newyork-main")))
                    return dir;
                string parent = Directory.GetParent(dir)?.FullName;
                if (parent == dir) break;
                dir = parent;
            }
            return "D:/escena";
        }
    }
}
