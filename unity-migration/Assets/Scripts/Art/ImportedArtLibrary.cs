using System.Collections.Generic;
using UnityEngine;

namespace Nyc.Art
{
    /// <summary>
    /// Runtime access to the .glb meshes exported from the web project by
    /// slop-newyork-main/tools/export-actors-glb.js (geometry built by the ORIGINAL code, so the
    /// silhouettes are identical to the WebGPU version).
    ///
    /// Files live in Assets/Resources/Art/&lt;name&gt;.glb. Imported glTF primitives carry:
    ///   TEXCOORD_0 = original uv · TEXCOORD_1 = (partId, 0) · COLOR_0 = part albedo
    /// which is exactly what Nyc/VehiclePaint and Nyc/Pedestrian expect.
    ///
    /// Every getter falls back to null so callers keep their procedural geometry when a file is
    /// missing (headless harness, fresh checkout without the export step).
    /// </summary>
    public static class ImportedArtLibrary
    {
        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();
        static readonly Dictionary<string, Mesh> FlippedCache = new Dictionary<string, Mesh>();
        static bool _scanned;

        public static void ClearCache()
        {
            Cache.Clear();
            FlippedCache.Clear();
        }

        /// <summary>Loads (and caches) the combined mesh of an imported glb, or null.</summary>
        public static Mesh GetMesh(string name) => GetMesh(name, false);

        /// <param name="flipZ">
        /// Rotates the mesh 180° about Y. The web project builds vehicles with the nose at +Z
        /// (actor-models.js BODY_SPECS.zFront &gt; 0) while this pipeline uses -Z as forward, so
        /// vehicle art must be flipped or every car drives backwards.
        /// </param>
        public static Mesh GetMesh(string name, bool flipZ)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var cache = flipZ ? FlippedCache : Cache;
            if (cache.TryGetValue(name, out Mesh cached)) return cached;

            Mesh mesh = LoadRaw(name);
            if (mesh != null)
            {
                if (flipZ) mesh = FlipZ(mesh);
                cache[name] = mesh;
            }
            return mesh;
        }

        static Mesh LoadRaw(string name)
        {
            var prefab = Resources.Load<GameObject>("Art/" + name);
            if (prefab == null) return null;
            Mesh mesh = CombinePrefabMeshes(prefab);
            if (mesh == null) Debug.LogWarning($"[ImportedArt] '{name}' has no MeshFilter.");
            return mesh;
        }

        /// <summary>Returns a copy rotated 180° about Y (positions and normals).</summary>
        static Mesh FlipZ(Mesh source)
        {
            var dst = Object.Instantiate(source);
            dst.name = source.name + "_flipZ";

            var pos = source.vertices;
            var nrm = source.normals;
            var outPos = new Vector3[pos.Length];
            var outNrm = nrm != null && nrm.Length == pos.Length ? new Vector3[pos.Length] : null;
            for (int i = 0; i < pos.Length; i++)
            {
                outPos[i] = new Vector3(-pos[i].x, pos[i].y, -pos[i].z);
                if (outNrm != null) outNrm[i] = new Vector3(-nrm[i].x, nrm[i].y, -nrm[i].z);
            }
            dst.vertices = outPos;
            if (outNrm != null) dst.normals = outNrm;
            dst.RecalculateBounds();
            return dst;
        }

        public static bool Has(string name) => GetMesh(name) != null;

        /// <summary>Vehicle keys understood by <see cref="VehicleMeshFactory"/>.</summary>
        public static readonly string[] VehicleNames =
        {
            "vehicle_taxi", "vehicle_sedan", "vehicle_suv", "vehicle_nypd",
            "vehicle_bus", "vehicle_helicopter", "vehicle_pedestrian"
        };

        public static bool HasAnyVehicle
        {
            get
            {
                foreach (string n in VehicleNames) if (Has(n)) return true;
                return false;
            }
        }

        /// <summary>
        /// Merges every MeshFilter under the prefab into one mesh, keeping the first UV set and
        /// copying TEXCOORD_1 (partId) and COLOR_0 through. Used when the importer keeps the
        /// glTF node hierarchy instead of a single primitive.
        /// </summary>
        static Mesh CombinePrefabMeshes(GameObject prefab)
        {
            var filters = prefab.GetComponentsInChildren<MeshFilter>(true);
            if (filters == null || filters.Length == 0) return null;

            if (filters.Length == 1 && filters[0].sharedMesh != null)
            {
                Mesh single = Object.Instantiate(filters[0].sharedMesh);
                single.name = prefab.name + "_mesh";
                var sc = single.bounds.center;
                var se = single.bounds.extents;
                if (float.IsNaN(sc.x) || float.IsNaN(sc.y) || float.IsNaN(sc.z) ||
                    float.IsNaN(se.x) || float.IsNaN(se.y) || float.IsNaN(se.z))
                {
                    Debug.LogWarning($"[ImportedArt] Mesh '{prefab.name}' contains NaN bounds; skipping.");
                    return null;
                }
                return single;
            }

            var rootInv = prefab.transform.worldToLocalMatrix;
            var combines = new List<CombineInstance>();
            foreach (var f in filters)
            {
                Mesh src = f.sharedMesh;
                if (src == null || src.vertexCount == 0) continue;
                var sc = src.bounds.center;
                var se = src.bounds.extents;
                if (float.IsNaN(sc.x) || float.IsNaN(sc.y) || float.IsNaN(sc.z) ||
                    float.IsNaN(se.x) || float.IsNaN(se.y) || float.IsNaN(se.z) ||
                    se.sqrMagnitude < 0.000001f ||
                    src.bounds.min.x > src.bounds.max.x ||
                    src.bounds.min.y > src.bounds.max.y ||
                    src.bounds.min.z > src.bounds.max.z)
                {
                    continue; // Skip any primitive with invalid/degenerate bounds
                }

                for (int sub = 0; sub < src.subMeshCount; sub++)
                {
                    if (src.GetIndexCount(sub) == 0) continue;
                    combines.Add(new CombineInstance
                    {
                        mesh = src,
                        subMeshIndex = sub,
                        transform = rootInv * f.transform.localToWorldMatrix
                    });
                }
            }
            if (combines.Count == 0) return null;

            var combined = new Mesh { name = prefab.name + "_combined" };
            combined.CombineMeshes(combines.ToArray(), true, true);
            combined.RecalculateBounds();

            var c = combined.bounds.center;
            var e = combined.bounds.extents;
            if (float.IsNaN(c.x) || float.IsNaN(c.y) || float.IsNaN(c.z) ||
                float.IsNaN(e.x) || float.IsNaN(e.y) || float.IsNaN(e.z))
            {
                Debug.LogWarning($"[ImportedArt] Combined mesh '{prefab.name}' has invalid bounds (NaNs); skipping.");
                return null;
            }

            return combined;
        }
    }
}
