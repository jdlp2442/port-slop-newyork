using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nyc.Rendering
{
    /// <summary>
    /// Spatial chunk cell representation for dynamic culling.
    /// Port of cell grouping logic from slop-newyork-main/js/scene-culling.js.
    /// </summary>
    public sealed class CullingChunk
    {
        public Vector3 Center;
        public float Radius;
        public Bounds Bounds;
        public float CullDistance;
        public List<Renderer> Renderers = new List<Renderer>();
        public List<GameObject> GameObjects = new List<GameObject>();
        public bool IsVisible = true;

        public void SetVisible(bool visible)
        {
            if (IsVisible == visible) return;
            IsVisible = visible;

            for (int i = 0; i < Renderers.Count; i++)
            {
                if (Renderers[i] != null) Renderers[i].enabled = visible;
            }

            for (int i = 0; i < GameObjects.Count; i++)
            {
                if (GameObjects[i] != null) GameObjects[i].SetActive(visible);
            }
        }
    }

    /// <summary>
    /// Island-wide spatial chunking and view-frustum / distance culling system.
    /// Breaks large static city meshes and small props into spatial cells, culling them
    /// against the camera frustum and maximum visibility thresholds.
    /// Port of slop-newyork-main/js/scene-culling.js and city-grid.js.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneCullingSystem : MonoBehaviour
    {
        [Header("Camera Reference")]
        public Camera viewCamera;

        [Header("Culling Configuration")]
        [Tooltip("Size of spatial cells in meters (matches PROXY_CELL = 500m in scene-culling.js)")]
        public float cellSize = 400.0f;
        [Tooltip("Minimum cull distance for small street details/props (m)")]
        public float minPropCullDistance = 350.0f;
        [Tooltip("Maximum cull distance for large building blocks (m)")]
        public float maxBuildingCullDistance = 2200.0f;
        [Tooltip("Enable view frustum plane intersection testing")]
        public bool enableFrustumCulling = true;
        [Tooltip("Enable distance-to-camera falloff culling")]
        public bool enableDistanceCulling = true;

        [Header("Runtime Statistics")]
        [SerializeField] int totalChunks;
        [SerializeField] int visibleChunks;
        [SerializeField] int culledChunks;
        [SerializeField] int totalRenderers;

        public static SceneCullingSystem Instance { get; private set; }

        readonly Dictionary<long, CullingChunk> _grid = new Dictionary<long, CullingChunk>();
        readonly List<CullingChunk> _allChunks = new List<CullingChunk>();
        Plane[] _frustumPlanes;
        Vector3 _lastCameraPos;
        bool _initialized;

        void Awake()
        {
            Instance = this;
            if (!viewCamera) viewCamera = Camera.main;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            if (!viewCamera)
            {
                viewCamera = Camera.main;
                if (!viewCamera) return;
            }

            UpdateCulling(viewCamera);
        }

        long CellKey(float x, float z)
        {
            int cx = Mathf.FloorToInt(x / cellSize);
            int cz = Mathf.FloorToInt(z / cellSize);
            return ((long)cx << 32) | (uint)cz;
        }

        CullingChunk GetOrCreateChunk(float x, float z, float cullDist)
        {
            long key = CellKey(x, z);
            if (!_grid.TryGetValue(key, out CullingChunk chunk))
            {
                int cx = Mathf.FloorToInt(x / cellSize);
                int cz = Mathf.FloorToInt(z / cellSize);
                Vector3 center = new Vector3((cx + 0.5f) * cellSize, 0f, (cz + 0.5f) * cellSize);
                float halfCell = cellSize * 0.5f;

                chunk = new CullingChunk
                {
                    Center = center,
                    Radius = Mathf.Sqrt(halfCell * halfCell * 2f),
                    Bounds = new Bounds(center, new Vector3(cellSize, 600f, cellSize)),
                    CullDistance = cullDist
                };

                _grid[key] = chunk;
                _allChunks.Add(chunk);
                totalChunks = _allChunks.Count;
            }

            return chunk;
        }

        /// <summary>
        /// Registers a single renderer into the spatial culling grid.
        /// </summary>
        public void RegisterRenderer(Renderer r, float cullDist = -1f)
        {
            if (r == null) return;
            if (cullDist < 0f) cullDist = maxBuildingCullDistance;

            Vector3 pos = r.transform.position;
            CullingChunk chunk = GetOrCreateChunk(pos.x, pos.z, cullDist);
            chunk.Renderers.Add(r);
            totalRenderers++;
        }

        /// <summary>
        /// Registers a GameObject into a specific spatial cell.
        /// </summary>
        public void RegisterGameObject(GameObject go, float cullDist = -1f)
        {
            if (go == null) return;
            if (cullDist < 0f) cullDist = maxBuildingCullDistance;

            Vector3 pos = go.transform.position;
            CullingChunk chunk = GetOrCreateChunk(pos.x, pos.z, cullDist);
            chunk.GameObjects.Add(go);
        }

        /// <summary>
        /// Scans and partitions all child renderers under the given city root transforms.
        /// </summary>
        public void RegisterCityRoots(Transform buildingRoot, Transform detailsRoot = null)
        {
            if (buildingRoot != null)
            {
                var renderers = buildingRoot.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    RegisterRenderer(renderers[i], maxBuildingCullDistance);
                }
            }

            if (detailsRoot != null)
            {
                var renderers = detailsRoot.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    RegisterRenderer(renderers[i], minPropCullDistance);
                }
            }
        }

        /// <summary>
        /// Evaluates frustum and distance culling against the current camera.
        /// </summary>
        public void UpdateCulling(Camera cam)
        {
            if (cam == null || _allChunks.Count == 0) return;

            Vector3 camPos = cam.transform.position;
            if (enableFrustumCulling)
            {
                _frustumPlanes = GeometryUtility.CalculateFrustumPlanes(cam);
            }

            int visibleCount = 0;
            int culledCount = 0;

            for (int i = 0; i < _allChunks.Count; i++)
            {
                CullingChunk chunk = _allChunks[i];

                bool visible = true;

                // 1. Distance check
                if (enableDistanceCulling)
                {
                    float dx = chunk.Center.x - camPos.x;
                    float dz = chunk.Center.z - camPos.z;
                    float distSq = dx * dx + dz * dz;
                    float maxDist = chunk.CullDistance + chunk.Radius;

                    if (distSq > maxDist * maxDist)
                    {
                        visible = false;
                    }
                }

                // 2. Frustum plane intersection check
                if (visible && enableFrustumCulling && _frustumPlanes != null && _frustumPlanes.Length > 0)
                {
                    if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, chunk.Bounds))
                    {
                        visible = false;
                    }
                }

                chunk.SetVisible(visible);

                if (visible) visibleCount++;
                else culledCount++;
            }

            visibleChunks = visibleCount;
            culledChunks = culledCount;
        }
    }
}
