using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Nyc.EditorTools
{
    /// <summary>One-shot UPM installer for the GLB import path. Run from CI/editor only.</summary>
    public static class PackageInstaller
    {
        static readonly string[] PackagesToAdd =
        {
            "https://github.com/Unity-Technologies/com.unity.cloud.gltfast.git?path=/Packages/com.unity.cloud.gltfast#main"
        };

        static AddAndRemoveRequest _request;
        static double _deadline;

        public static void InstallGltfFast()
        {
            Debug.Log("[NYC PackageInstaller] Adding Unity glTFast from the official repository.");
            _request = Client.AddAndRemove(PackagesToAdd, new string[0]);
            _deadline = EditorApplication.timeSinceStartup + 600.0;
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (_request == null) return;
            if (!_request.IsCompleted)
            {
                if (EditorApplication.timeSinceStartup > _deadline)
                {
                    EditorApplication.update -= Poll;
                    Debug.LogError("[NYC PackageInstaller] Timed out waiting for UPM.");
                    EditorApplication.Exit(2);
                }
                return;
            }

            EditorApplication.update -= Poll;
            if (_request.Status == StatusCode.Success)
            {
                var resolved = _request.Result.Select(p => p.name + "@" + p.version);
                Debug.Log("[NYC PackageInstaller] Resolved: " + string.Join(", ", resolved));
                EditorApplication.Exit(0);
            }
            else
            {
                Debug.LogError("[NYC PackageInstaller] Failed: " + _request.Error?.message);
                EditorApplication.Exit(1);
            }
        }
    }
}
