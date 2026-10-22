using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Nyc.Core;
using Nyc.Data;
using Nyc.Generation;

namespace Nyc.Rendering
{
    /// <summary>
    /// Procedural tree generation and GPU instanced rendering for Central Park,
    /// neighborhood parks, and Manhattan street sidewalks.
    /// Port of js/trees.js (London plane, Honey locust, American elm).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TreeSystem : MonoBehaviour
    {
        public enum Species
        {
            LondonPlane = 0,
            HoneyLocust = 1,
            AmericanElm = 2
        }

        public struct TreeInstance
        {
            public Matrix4x4 Matrix;
            public Color Color;
            public Species Species;
        }

        [Header("Materials")]
        public Material foliageMaterial;
        public Material trunkMaterial;

        [Header("Tuning")]
        public bool buildStreetTrees = true;
        public bool buildParkTrees = true;
        public bool buildCentralParkTrees = true;
        public int maxInstances = 8000;

        Mesh[] _speciesCrownMeshes;
        Mesh[] _speciesTrunkMeshes;
        readonly List<TreeInstance>[] _batches = new List<TreeInstance>[3];
        MaterialPropertyBlock _propBlock;
        bool _initialized;

        static readonly int InstanceColorId = Shader.PropertyToID("_InstanceColor");

        // Species summer color swatches from trees.js
        static readonly Color[][] SpeciesTints = new[]
        {
            // London plane (summer olive greens)
            new[] { new Color(0.38f, 0.48f, 0.22f), new Color(0.44f, 0.52f, 0.24f), new Color(0.34f, 0.42f, 0.18f) },
            // Honey locust (lacy dappled bright light greens)
            new[] { new Color(0.52f, 0.62f, 0.25f), new Color(0.58f, 0.68f, 0.28f), new Color(0.48f, 0.58f, 0.22f) },
            // American elm (deep stately canopy greens)
            new[] { new Color(0.28f, 0.44f, 0.20f), new Color(0.32f, 0.48f, 0.22f), new Color(0.24f, 0.38f, 0.16f) }
        };

        void Awake()
        {
            EnsureMaterials();
            _propBlock = new MaterialPropertyBlock();
            for (int s = 0; s < 3; s++) _batches[s] = new List<TreeInstance>(2048);
        }

        public void Initialize(NycGeoData geo)
        {
            if (geo == null) geo = CityConfig.Geo;
            if (geo == null) return;

            EnsureMaterials();
            BuildProceduralTreeMeshes();
            CollectTreeSites(geo);
            _initialized = true;
        }

        void LateUpdate()
        {
            if (!_initialized) return;
            RenderTrees();
        }

        public void RenderTrees()
        {
            if (!_initialized || foliageMaterial == null || trunkMaterial == null) return;

            var matrices = new Matrix4x4[1023];
            var colors = new Vector4[1023];

            for (int s = 0; s < 3; s++)
            {
                var list = _batches[s];
                int total = list.Count;
                if (total == 0) continue;

                Mesh crownMesh = _speciesCrownMeshes[s];
                Mesh trunkMesh = _speciesTrunkMeshes[s];

                for (int offset = 0; offset < total; offset += 1023)
                {
                    int count = Mathf.Min(1023, total - offset);
                    for (int i = 0; i < count; i++)
                    {
                        var inst = list[offset + i];
                        matrices[i] = inst.Matrix;
                        colors[i] = inst.Color;
                    }

                    _propBlock.SetVectorArray(InstanceColorId, colors);

                    var renderParamsFoliage = new RenderParams(foliageMaterial)
                    {
                        matProps = _propBlock,
                        shadowCastingMode = ShadowCastingMode.On,
                        receiveShadows = true
                    };
                    Graphics.RenderMeshInstanced(renderParamsFoliage, crownMesh, 0, matrices, count);

                    var renderParamsTrunk = new RenderParams(trunkMaterial)
                    {
                        shadowCastingMode = ShadowCastingMode.On,
                        receiveShadows = true
                    };
                    Graphics.RenderMeshInstanced(renderParamsTrunk, trunkMesh, 0, matrices, count);
                }
            }
        }

        // =========================================================================
        // Tree Distribution / Site Gathering
        // =========================================================================
        void CollectTreeSites(NycGeoData geo)
        {
            for (int s = 0; s < 3; s++) _batches[s].Clear();
            int totalPlanted = 0;

            // 1. Central Park Trees
            if (buildCentralParkTrees && geo.CentralPark != null)
            {
                Vector4 b = geo.CentralPark.Bounds;
                float minX = b.x + 20f, maxX = b.y - 20f;
                float minZ = b.z + 30f, maxZ = b.w - 30f;

                var rng = new Mulberry32(40019);
                for (float z = minZ; z < maxZ; z += 35f)
                {
                    for (float x = minX; x < maxX; x += 35f)
                    {
                        if (totalPlanted >= maxInstances) break;
                        float ox = (rng.NextFloat() - 0.5f) * 20f;
                        float oz = (rng.NextFloat() - 0.5f) * 20f;
                        float wx = x + ox;
                        float wz = z + oz;

                        // Check water keepout
                        if (InsideCentralParkWater(geo.CentralPark, wx, wz)) continue;

                        Species sp = (Species)(rng.NextInt() % 3);
                        float scale = 0.85f + rng.NextFloat() * 0.45f;
                        float yaw = rng.NextFloat() * Mathf.PI * 2f;
                        Color tint = SpeciesTints[(int)sp][rng.NextInt() % 3];

                        AddInstance(wx, 0.0f, wz, scale, yaw, sp, tint);
                        totalPlanted++;
                    }
                }
            }

            // 2. Manhattan Neighborhood Parks (Bryant, Madison, Washington Sq, etc.)
            if (buildParkTrees && geo.Parks != null)
            {
                var rng = new Mulberry32(50021);
                foreach (var p in geo.Parks)
                {
                    if (p.Name == "Central Park" || p.Rings == null) continue;
                    foreach (var ring in p.Rings)
                    {
                        if (ring.Length < 3) continue;
                        Vector4 b = RingBounds(ring);
                        for (float z = b.z + 12f; z < b.w - 12f; z += 18f)
                        {
                            for (float x = b.x + 12f; x < b.y - 12f; x += 18f)
                            {
                                if (totalPlanted >= maxInstances) break;
                                float wx = x + (rng.NextFloat() - 0.5f) * 8f;
                                float wz = z + (rng.NextFloat() - 0.5f) * 8f;
                                if (!PolygonTriangulator.PointInPolygon(ring, new Vector2(wx, wz))) continue;

                                Species sp = (Species)(rng.NextInt() % 3);
                                float scale = 0.75f + rng.NextFloat() * 0.4f;
                                float yaw = rng.NextFloat() * Mathf.PI * 2f;
                                Color tint = SpeciesTints[(int)sp][rng.NextInt() % 3];

                                AddInstance(wx, 0.0f, wz, scale, yaw, sp, tint);
                                totalPlanted++;
                            }
                        }
                    }
                }
            }

            // 3. Street / Sidewalk Trees along Avenues
            if (buildStreetTrees && geo.Avenues != null)
            {
                var rng = new Mulberry32(60033);
                foreach (var ave in geo.Avenues)
                {
                    float x = ave.X;
                    foreach (var s in ave.Segs)
                    {
                        for (float z = s.Z0 + 20f; z < s.Z1 - 20f; z += 42f)
                        {
                            if (totalPlanted >= maxInstances) break;
                            // Left sidewalk
                            AddStreetTree(x - s.W * 0.5f - 2.2f, z, rng);
                            // Right sidewalk
                            AddStreetTree(x + s.W * 0.5f + 2.2f, z, rng);
                            totalPlanted += 2;
                        }
                    }
                }
            }
        }

        void AddStreetTree(float x, float z, Mulberry32 rng)
        {
            Species sp = (Species)(rng.NextInt() % 3);
            float scale = 0.65f + rng.NextFloat() * 0.35f;
            float yaw = rng.NextFloat() * Mathf.PI * 2f;
            Color tint = SpeciesTints[(int)sp][rng.NextInt() % 3];
            AddInstance(x, 0.18f, z, scale, yaw, sp, tint);
        }

        void AddInstance(float x, float y, float z, float scale, float yaw, Species sp, Color tint)
        {
            Vector3 unityPos = CityFrame.ToUnity(x, y, z);
            Quaternion rot = Quaternion.Euler(0, -yaw * Mathf.Rad2Deg, 0);
            Vector3 sc = new Vector3(scale, scale, scale);

            Matrix4x4 mat = Matrix4x4.TRS(unityPos, rot, sc);
            _batches[(int)sp].Add(new TreeInstance
            {
                Matrix = mat,
                Color = tint,
                Species = sp
            });
        }

        static bool InsideCentralParkWater(CentralParkRec cp, float x, float z)
        {
            if (cp.Water == null) return false;
            var pt = new Vector2(x, z);
            for (int i = 0; i < cp.Water.Length; i++)
            {
                var r = cp.Water[i].Rings;
                if (r == null) continue;
                for (int j = 0; j < r.Length; j++)
                {
                    if (PolygonTriangulator.PointInPolygon(r[j], pt)) return true;
                }
            }
            return false;
        }

        static Vector4 RingBounds(Vector2[] ring)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < ring.Length; i++)
            {
                if (ring[i].x < minX) minX = ring[i].x;
                if (ring[i].x > maxX) maxX = ring[i].x;
                if (ring[i].y < minZ) minZ = ring[i].y;
                if (ring[i].y > maxZ) maxZ = ring[i].y;
            }
            return new Vector4(minX, maxX, minZ, maxZ);
        }

        // =========================================================================
        // Procedural Tree Meshes (Crown Canopy + Trunk Tube)
        // =========================================================================
        void BuildProceduralTreeMeshes()
        {
            _speciesCrownMeshes = new Mesh[3];
            _speciesTrunkMeshes = new Mesh[3];

            // 1. London plane: wide spreading flared crown, thick trunk
            _speciesCrownMeshes[0] = GenerateCanopyMesh(radius: 4.8f, height: 6.5f, verticalOffset: 5.5f, lobes: 5, seed: 101);
            _speciesTrunkMeshes[0] = GenerateTrunkMesh(rBase: 0.55f, rTop: 0.28f, height: 5.5f, branches: 4, seed: 101);

            // 2. Honey locust: slender tall trunk, feathery multi-cluster crown
            _speciesCrownMeshes[1] = GenerateCanopyMesh(radius: 3.8f, height: 7.2f, verticalOffset: 6.0f, lobes: 7, seed: 202);
            _speciesTrunkMeshes[1] = GenerateTrunkMesh(rBase: 0.42f, rTop: 0.22f, height: 6.0f, branches: 5, seed: 202);

            // 3. American elm: vase-shaped arching crown, tall stately trunk
            _speciesCrownMeshes[2] = GenerateCanopyMesh(radius: 5.2f, height: 8.5f, verticalOffset: 6.8f, lobes: 6, seed: 303);
            _speciesTrunkMeshes[2] = GenerateTrunkMesh(rBase: 0.58f, rTop: 0.32f, height: 6.8f, branches: 6, seed: 303);
        }

        static Mesh GenerateCanopyMesh(float radius, float height, float verticalOffset, int lobes, int seed)
        {
            var mesh = new Mesh { name = "TreeCanopy_" + seed };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color32>();
            var tris = new List<int>();

            int rings = 12, segments = 16;
            Vector3 center = new Vector3(0, verticalOffset + height * 0.5f, 0);

            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                float phi = v * Mathf.PI;
                for (int seg = 0; seg <= segments; seg++)
                {
                    float u = seg / (float)segments;
                    float theta = u * Mathf.PI * 2f;

                    // Organic multi-lobe deformation from trees.js
                    float lobeNoise = 1.0f + 0.16f * Mathf.Sin(theta * lobes) + 0.10f * Mathf.Cos(phi * 3f);
                    float rx = Mathf.Sin(phi) * Mathf.Cos(theta) * radius * lobeNoise;
                    float rz = Mathf.Sin(phi) * Mathf.Sin(theta) * radius * lobeNoise;
                    float ry = Mathf.Cos(phi) * height * 0.5f;

                    Vector3 pos = center + new Vector3(rx, ry, rz);
                    Vector3 normal = (pos - center).normalized;

                    // Bake sway weight into Red channel (top of crown sways more), ambient occlusion into Green
                    float swayWeight = Mathf.Clamp01((pos.y - verticalOffset) / height);
                    float occlusion = Mathf.Lerp(0.6f, 1.0f, v);
                    byte swayByte = (byte)(swayWeight * 255);
                    byte occByte = (byte)(occlusion * 255);

                    verts.Add(pos);
                    norms.Add(normal);
                    uvs.Add(new Vector2(u, v));
                    colors.Add(new Color32(swayByte, occByte, 0, 255));
                }
            }

            for (int r = 0; r < rings; r++)
            {
                for (int seg = 0; seg < segments; seg++)
                {
                    int i0 = r * (segments + 1) + seg;
                    int i1 = i0 + segments + 1;

                    tris.Add(i0); tris.Add(i1); tris.Add(i0 + 1);
                    tris.Add(i0 + 1); tris.Add(i1); tris.Add(i1 + 1);
                }
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh GenerateTrunkMesh(float rBase, float rTop, float height, int branches, int seed)
        {
            var mesh = new Mesh { name = "TreeTrunk_" + seed };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var colors = new List<Color32>();
            var tris = new List<int>();

            int radial = 8, heightSegs = 6;
            for (int h = 0; h <= heightSegs; h++)
            {
                float t = h / (float)heightSegs;
                float y = t * height;
                float r = Mathf.Lerp(rBase, rTop, t);

                for (int i = 0; i <= radial; i++)
                {
                    float angle = (i / (float)radial) * Mathf.PI * 2f;
                    float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);

                    Vector3 pos = new Vector3(cs * r, y, sn * r);
                    Vector3 n = new Vector3(cs, 0, sn);

                    // Trunk base doesn't sway, upper trunk sways slightly
                    byte sway = (byte)(t * 64);
                    verts.Add(pos);
                    norms.Add(n);
                    uvs.Add(new Vector2(i / (float)radial, t * 2f));
                    colors.Add(new Color32(sway, 255, 0, 255));
                }
            }

            for (int h = 0; h < heightSegs; h++)
            {
                for (int i = 0; i < radial; i++)
                {
                    int i0 = h * (radial + 1) + i;
                    int i1 = i0 + radial + 1;

                    tris.Add(i0); tris.Add(i1); tris.Add(i0 + 1);
                    tris.Add(i0 + 1); tris.Add(i1); tris.Add(i1 + 1);
                }
            }

            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        void EnsureMaterials()
        {
            if (foliageMaterial == null)
            {
                Shader shader = Shader.Find("Nyc/TreeFoliage");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
                foliageMaterial = new Material(shader) { name = "Material_TreeFoliage" };
                foliageMaterial.enableInstancing = true;
            }

            if (trunkMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                trunkMaterial = new Material(shader) { name = "Material_TreeBark" };
                trunkMaterial.color = new Color(0.28f, 0.24f, 0.20f);
                if (trunkMaterial.HasProperty("_Smoothness")) trunkMaterial.SetFloat("_Smoothness", 0.15f);
                trunkMaterial.enableInstancing = true;
            }
        }
    }
}
