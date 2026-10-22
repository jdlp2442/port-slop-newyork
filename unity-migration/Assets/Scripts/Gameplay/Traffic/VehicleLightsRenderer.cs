using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Nyc.Gameplay.Traffic
{
    /// <summary>
    /// Renders vehicle headlights (volumetric/asphalt projected light beams) and taillights
    /// for active traffic and player vehicles during dusk, night, and dawn.
    /// Ported and enhanced from slop-newyork-main/js/vehicle-lights.js.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleLightsRenderer : MonoBehaviour
    {
        [Header("Materials")]
        public Material beamMaterial;
        public Material tailMaterial;

        [Header("Calibration (Matching slop HEADLIGHTS spec)")]
        [Tooltip("Color of headlights (warm xenon/halogen)")]
        public Color headlightColor = new Color(1.0f, 0.97f, 0.92f, 0.65f);
        [Tooltip("Color of taillights")]
        public Color taillightColor = new Color(1.0f, 0.08f, 0.04f, 0.85f);
        [Tooltip("Beam forward range in meters")]
        public float beamDistance = 30.0f;
        [Tooltip("Beam spread width at max distance")]
        public float beamSpreadWidth = 4.5f;

        [Header("State")]
        [Range(0f, 1f)]
        public float nightIntensity = 1.0f;

        Mesh _carBeamMesh;
        Mesh _busBeamMesh;
        Mesh _tailGlowMesh;
        RenderParams _beamParams;
        RenderParams _tailParams;
        MaterialPropertyBlock _mpb;

        // Buffers for instanced matrices (up to 1024 instances per batch)
        const int MaxBatch = 1024;
        Matrix4x4[] _beamMatrices;
        Matrix4x4[] _tailMatrices;

        bool _initialized;

        void Awake() => EnsureInit();

        public void EnsureInit()
        {
            if (_initialized) return;

            if (!beamMaterial)
            {
                Shader shader = Shader.Find("Nyc/VehicleLightBeam");
                if (!shader) shader = Shader.Find("Universal Render Pipeline/Unlit");
                beamMaterial = new Material(shader) { name = "VehicleHeadlightBeams" };
            }

            if (!tailMaterial)
            {
                Shader shader = Shader.Find("Nyc/VehicleLightBeam");
                if (!shader) shader = Shader.Find("Universal Render Pipeline/Unlit");
                tailMaterial = new Material(shader) { name = "VehicleTaillights" };
            }

            _carBeamMesh = CreateBeamMesh(halfWidth: 0.58f, frontZ: -2.2f, range: beamDistance, spread: beamSpreadWidth);
            _busBeamMesh = CreateBeamMesh(halfWidth: 0.85f, frontZ: -5.8f, range: beamDistance * 1.2f, spread: beamSpreadWidth * 1.3f);
            _tailGlowMesh = CreateTailGlowMesh(halfWidth: 0.58f, rearZ: 2.2f);

            _beamParams = new RenderParams(beamMaterial)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 30000f),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };

            _tailParams = new RenderParams(tailMaterial)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 30000f),
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };

            _beamMatrices = new Matrix4x4[MaxBatch];
            _tailMatrices = new Matrix4x4[MaxBatch];
            _mpb = new MaterialPropertyBlock();

            _initialized = true;
        }

        /// <summary>
        /// Creates a combined dual-beam ground projection & atmospheric wedge mesh for both left/right headlights.
        /// </summary>
        static Mesh CreateBeamMesh(float halfWidth, float frontZ, float range, float spread)
        {
            var mesh = new Mesh { name = "VehicleHeadlightBeamMesh" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Pitch angle down towards asphalt (~0.14 rad ~ 8 degrees)
            float pitchDrop = 0.62f; // Drops from headlight height (~0.66m) down to ~0.04m above street

            foreach (float side in new[] { -1f, 1f })
            {
                float x0 = side * halfWidth;
                float y0 = 0.65f;
                float z0 = frontZ;

                // Toe out slightly
                float x1 = side * (halfWidth + spread * 0.5f);
                float y1 = Mathf.Max(0.04f, y0 - pitchDrop);
                float z1 = frontZ - range;

                // Ground illumination trapezoid (skimming asphalt)
                int start = verts.Count;
                verts.Add(new Vector3(x0 - 0.2f, y0, z0)); // 0: inner lamp
                verts.Add(new Vector3(x0 + 0.2f, y0, z0)); // 1: outer lamp
                verts.Add(new Vector3(x1 - spread * 0.4f, y1, z1)); // 2: inner end
                verts.Add(new Vector3(x1 + spread * 0.4f, y1, z1)); // 3: outer end

                for (int i = 0; i < 4; i++) norms.Add(Vector3.up);

                uvs.Add(new Vector2(0.2f, 0.0f));
                uvs.Add(new Vector2(0.8f, 0.0f));
                uvs.Add(new Vector2(0.0f, 1.0f));
                uvs.Add(new Vector2(1.0f, 1.0f));

                tris.Add(start + 0); tris.Add(start + 2); tris.Add(start + 1);
                tris.Add(start + 1); tris.Add(start + 2); tris.Add(start + 3);

                // Volumetric vertical fan wedge (visible from side & top angles)
                int vstart = verts.Count;
                verts.Add(new Vector3(x0, y0, z0)); // 0: lamp apex
                verts.Add(new Vector3(x1, y0 + 0.2f, z1)); // 1: top edge
                verts.Add(new Vector3(x1, y1, z1)); // 2: bottom edge

                for (int i = 0; i < 3; i++) norms.Add(Vector3.Cross(verts[vstart + 1] - verts[vstart], verts[vstart + 2] - verts[vstart]).normalized);

                uvs.Add(new Vector2(0.5f, 0.0f));
                uvs.Add(new Vector2(0.5f, 1.0f));
                uvs.Add(new Vector2(0.5f, 1.0f));

                tris.Add(vstart + 0); tris.Add(vstart + 1); tris.Add(vstart + 2);
                tris.Add(vstart + 0); tris.Add(vstart + 2); tris.Add(vstart + 1); // Double sided
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            SetAllUVs(mesh, uvs, verts.Count);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Creates a pair of glowing red billboards/discs for the taillights at the rear of the vehicle.
        /// </summary>
        static Mesh CreateTailGlowMesh(float halfWidth, float rearZ)
        {
            var mesh = new Mesh { name = "VehicleTailGlowMesh" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            float sz = 0.22f;
            foreach (float side in new[] { -1f, 1f })
            {
                float cx = side * halfWidth;
                float cy = 0.68f;
                float cz = rearZ + 0.02f;

                int start = verts.Count;
                verts.Add(new Vector3(cx - sz, cy - sz * 0.6f, cz));
                verts.Add(new Vector3(cx + sz, cy - sz * 0.6f, cz));
                verts.Add(new Vector3(cx - sz, cy + sz * 0.6f, cz));
                verts.Add(new Vector3(cx + sz, cy + sz * 0.6f, cz));

                for (int i = 0; i < 4; i++) norms.Add(Vector3.forward);

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(0f, 1f));
                uvs.Add(new Vector2(1f, 1f));

                tris.Add(start + 0); tris.Add(start + 2); tris.Add(start + 1);
                tris.Add(start + 1); tris.Add(start + 2); tris.Add(start + 3);
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            SetAllUVs(mesh, uvs, verts.Count);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static void SetAllUVs(Mesh mesh, List<Vector2> uv0, int count)
        {
            var dummy = new List<Vector2>(count);
            for (int i = 0; i < count; i++) dummy.Add(Vector2.zero);

            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, dummy);
            mesh.SetUVs(2, dummy);
            mesh.SetUVs(3, dummy);
        }

        /// <summary>
        /// Renders headlight beams and taillights for all vehicle batches.
        /// </summary>
        public void RenderVehicleLights(List<VehicleInstance>[] batches, float currentNightFactor)
        {
            if (currentNightFactor < 0.02f) return; // Daylight: vehicle beams off
            EnsureInit();

            nightIntensity = currentNightFactor;

            beamMaterial.SetFloat("_NightIntensity", nightIntensity);
            tailMaterial.SetFloat("_NightIntensity", nightIntensity);

            for (int b = 0; b < batches.Length && b < 4; b++)
            {
                int count = Mathf.Min(batches[b].Count, MaxBatch);
                if (count == 0) continue;

                Mesh beamMesh = (b == 3) ? _busBeamMesh : _carBeamMesh;

                for (int i = 0; i < count; i++)
                {
                    VehicleInstance inst = batches[b][i];
                    Vector3 scale = new Vector3(inst.Scale, inst.Scale, inst.Scale);
                    Matrix4x4 mat = Matrix4x4.TRS(inst.Position, inst.Rotation, scale);
                    _beamMatrices[i] = mat;
                    _tailMatrices[i] = mat;
                }

                _mpb.SetVector("_LightParams", new Vector4(headlightColor.r, headlightColor.g, headlightColor.b, 1f));
                Graphics.RenderMeshInstanced(_beamParams, beamMesh, 0, _beamMatrices, count);

                _mpb.SetVector("_LightParams", new Vector4(taillightColor.r, taillightColor.g, taillightColor.b, 1f));
                Graphics.RenderMeshInstanced(_tailParams, _tailGlowMesh, 0, _tailMatrices, count);
            }
        }
    }
}
