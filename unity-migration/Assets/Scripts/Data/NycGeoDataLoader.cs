using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

namespace Nyc.Data
{
    /// <summary>
    /// Async loader for platforms where StreamingAssets is not a plain file path
    /// (Android APK, WebGL). On desktop/console NycGeoData.LoadFromStreamingAssets is enough.
    /// </summary>
    public static class NycGeoDataLoader
    {
        public static IEnumerator LoadAsync(Action<NycGeoData> onLoaded, Action<string> onError = null) =>
            LoadAsync("nyc/nyc-geodata.json", onLoaded, onError);

        public static IEnumerator LoadAsync(string relativePath, Action<NycGeoData> onLoaded, Action<string> onError = null)
        {
            if (string.IsNullOrEmpty(relativePath)) relativePath = "nyc/nyc-geodata.json";
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, relativePath);
            if (!System.IO.File.Exists(path))
            {
                string alt1 = System.IO.Path.Combine(Application.streamingAssetsPath, "nyc-geodata.json");
                string alt2 = System.IO.Path.Combine(Application.streamingAssetsPath, "nyc/nyc-geodata.js");
                string alt3 = System.IO.Path.Combine(Application.streamingAssetsPath, "nyc-geodata.js");
                if (System.IO.File.Exists(alt1)) path = alt1;
                else if (System.IO.File.Exists(alt2)) path = alt2;
                else if (System.IO.File.Exists(alt3)) path = alt3;
            }

#if UNITY_ANDROID && !UNITY_EDITOR || UNITY_WEBGL
            using var req = UnityWebRequest.Get(path);
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(req.error);
                yield break;
            }
            NycGeoData data = null;
            string error = null;
            try { data = NycGeoData.Parse(req.downloadHandler.text); }
            catch (Exception e) { error = e.Message; }
            if (data == null) onError?.Invoke(error);
            else onLoaded(data);
#else
            NycGeoData data = null;
            string error = null;
            try { data = NycGeoData.LoadFromFile(path); }
            catch (Exception e) { error = e.Message; }
            yield return null;
            if (data == null) onError?.Invoke(error);
            else onLoaded(data);
#endif
        }
    }
}
