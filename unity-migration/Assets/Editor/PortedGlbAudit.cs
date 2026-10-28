using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Nyc.EditorTools
{
    /// <summary>Reports what glTFast actually imported from the web export.</summary>
    public static class PortedGlbAudit
    {
        [MenuItem("NYC/11. Audit Imported GLB Assets", priority = 11)]
        public static void Audit()
        {
            string root = "Assets/PortedGLB";
            string[] files = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.EndsWith(".glb", System.StringComparison.OrdinalIgnoreCase))
                .Where(p => p.StartsWith(root + "/", System.StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p)
                .ToArray();
            foreach (string path in files)
            {
                var assets = AssetDatabase.LoadAllAssetsAtPath(path);
                int meshes = assets.Count(a => a is Mesh);
                int materials = assets.Count(a => a is Material);
                int textures = assets.Count(a => a is Texture);
                int anims = assets.Count(a => a is AnimationClip);
                Debug.Log($"[NYC GLB] {Path.GetFileName(path)}: objects={assets.Length}, meshes={meshes}, materials={materials}, textures={textures}, animations={anims}");
            }
            Debug.Log($"[NYC GLB] Audited {files.Length} imported assets under {root}.");
        }
    }
}
