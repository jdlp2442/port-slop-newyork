using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Nyc.Core;
using Nyc.Data;

namespace Nyc.Rendering
{
    /// <summary>
    /// Populates Manhattan with authentic street details:
    /// - Cantilevered NYC yellow traffic signals with 3-aspect lenses
    /// - NYC DOT cobra-head LED streetlights
    /// - Con Edison orange/white steam stacks + animated billowing steam plumes
    /// - MTA subway entrances with glowing green globes
    /// - Fire hydrants, USPS mailboxes, litter baskets
    /// Port of js/street-details.js and tests/smoke-texture.test.cjs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StreetDetailsSystem : MonoBehaviour
    {
        [Header("Materials")]
        public Material trafficSignalPoleMaterial;
        public Material trafficSignalLensMaterial;
        public Material streetlightMaterial;
        public Material streetlightGlowMaterial;
        public Material steamStackOrangeMaterial;
        public Material steamStackWhiteMaterial;
        public Material steamParticleMaterial;
        public Material hydrantMaterial;
        public Material mailboxMaterial;
        public Material litterBasketMaterial;
        public Material subwayEntranceMaterial;
        public Material greenGlobeMaterial;

        [Header("Steam Smoke Texture")]
        public Texture2D bakedSmokeTexture;

        public bool buildSignals = true;
        public bool buildStreetlights = true;
        public bool buildSteamStacks = true;
        public bool buildHydrantsAndBaskets = true;
        public bool buildSubwayEntrances = true;

        Mesh _signalMesh;
        Mesh _streetlightMesh;
        Mesh _steamStackMesh;
        Mesh _barricadeMesh;
        Mesh _smokeQuadMesh;
        Mesh _hydrantMesh;
        Mesh _mailboxMesh;
        Mesh _trashMesh;
        Mesh _subwayMesh;

        // Instanced batches
        readonly List<Matrix4x4> _signalMatrices = new List<Matrix4x4>(256);
        readonly List<Matrix4x4> _streetlightMatrices = new List<Matrix4x4>(1024);
        readonly List<Matrix4x4> _steamOrangeMatrices = new List<Matrix4x4>(64);
        readonly List<Matrix4x4> _steamWhiteMatrices = new List<Matrix4x4>(64);
        readonly List<Matrix4x4> _barricadeMatrices = new List<Matrix4x4>(128);
        readonly List<Matrix4x4> _hydrantMatrices = new List<Matrix4x4>(512);
        readonly List<Matrix4x4> _mailboxMatrices = new List<Matrix4x4>(256);
        readonly List<Matrix4x4> _trashMatrices = new List<Matrix4x4>(512);
        readonly List<Matrix4x4> _subwayMatrices = new List<Matrix4x4>(128);

        // Steam particle buffer
        struct SteamPlume
        {
            public Vector4 Origin; // xyz, w = phase
            public Vector4 Wind;   // xyz, w = scaleMul
            public Vector4 Params; // x = spinSpeed, y = curlSeed
        }
        readonly List<SteamPlume> _steamPuffs = new List<SteamPlume>(512);
        MaterialPropertyBlock _smokePropBlock;

        bool _initialized;

        void Awake()
        {
            EnsureMaterials();
            _smokePropBlock = new MaterialPropertyBlock();
        }

        public void Initialize(NycGeoData geo)
        {
            if (geo == null) geo = CityConfig.Geo;
            if (geo == null) return;

            EnsureMaterials();
            BuildFurnitureMeshes();
            PopulateStreetFurniture(geo);
            _initialized = true;
        }

        void LateUpdate()
        {
            if (!_initialized) return;
            RenderStreetFurniture();
            RenderSteamPuffs();
        }

        // =========================================================================
        // Rendering
        // =========================================================================
        void RenderStreetFurniture()
        {
            DrawInstancedBatch(_signalMesh, trafficSignalPoleMaterial, _signalMatrices);
            DrawInstancedBatch(_streetlightMesh, streetlightMaterial, _streetlightMatrices);
            DrawInstancedBatch(_steamStackMesh, steamStackOrangeMaterial, _steamOrangeMatrices);
            DrawInstancedBatch(_steamStackMesh, steamStackWhiteMaterial, _steamWhiteMatrices);
            DrawInstancedBatch(_barricadeMesh, steamStackOrangeMaterial, _barricadeMatrices);
            DrawInstancedBatch(_hydrantMesh, hydrantMaterial, _hydrantMatrices);
            DrawInstancedBatch(_mailboxMesh, mailboxMaterial, _mailboxMatrices);
            DrawInstancedBatch(_trashMesh, litterBasketMaterial, _trashMatrices);
            DrawInstancedBatch(_subwayMesh, subwayEntranceMaterial, _subwayMatrices);
        }

        void RenderSteamPuffs()
        {
            if (_smokeQuadMesh == null || steamParticleMaterial == null || _steamPuffs.Count == 0) return;

            int total = _steamPuffs.Count;
            var matrices = new Matrix4x4[1023];
            var origins = new Vector4[1023];
            var winds = new Vector4[1023];
            var sparams = new Vector4[1023];

            for (int offset = 0; offset < total; offset += 1023)
            {
                int count = Mathf.Min(1023, total - offset);
                for (int i = 0; i < count; i++)
                {
                    var p = _steamPuffs[offset + i];
                    matrices[i] = Matrix4x4.identity;
                    origins[i] = p.Origin;
                    winds[i] = p.Wind;
                    sparams[i] = p.Params;
                }

                _smokePropBlock.SetVectorArray("_SmokeOrigin", origins);
                _smokePropBlock.SetVectorArray("_SmokeWind", winds);
                _smokePropBlock.SetVectorArray("_SmokeParams", sparams);

                var rParams = new RenderParams(steamParticleMaterial)
                {
                    matProps = _smokePropBlock,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false
                };
                Graphics.RenderMeshInstanced(rParams, _smokeQuadMesh, 0, matrices, count);
            }
        }

        static void DrawInstancedBatch(Mesh mesh, Material mat, List<Matrix4x4> matrices)
        {
            if (mesh == null || mat == null || matrices == null || matrices.Count == 0) return;
            var buffer = new Matrix4x4[1023];
            int total = matrices.Count;

            for (int offset = 0; offset < total; offset += 1023)
            {
                int count = Mathf.Min(1023, total - offset);
                for (int i = 0; i < count; i++) buffer[i] = matrices[offset + i];

                var rParams = new RenderParams(mat)
                {
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true
                };
                Graphics.RenderMeshInstanced(rParams, mesh, 0, buffer, count);
            }
        }

        // =========================================================================
        // Placement & Spatial Layout
        // =========================================================================
        void PopulateStreetFurniture(NycGeoData geo)
        {
            _signalMatrices.Clear();
            _streetlightMatrices.Clear();
            _steamOrangeMatrices.Clear();
            _steamWhiteMatrices.Clear();
            _barricadeMatrices.Clear();
            _hydrantMatrices.Clear();
            _mailboxMatrices.Clear();
            _trashMatrices.Clear();
            _subwayMatrices.Clear();
            _steamPuffs.Clear();

            // 1. Traffic Signals at Intersections
            if (buildSignals && geo.Streets != null && geo.Avenues != null)
            {
                foreach (var ave in geo.Avenues)
                {
                    float ax = ave.X;
                    foreach (var st in geo.Streets)
                    {
                        if (st.N < 14 || st.N > 59) continue;
                        float sz = st.Z;
                        // 4 corners of the intersection
                        float hw = 12f;
                        AddSignalInstance(ax + hw, sz - 8f, 0f);
                        AddSignalInstance(ax - hw, sz + 8f, 180f);
                    }
                }
            }

            // 2. Cobra-head LED Streetlights along Avenues
            if (buildStreetlights && geo.Avenues != null)
            {
                foreach (var ave in geo.Avenues)
                {
                    float ax = ave.X;
                    foreach (var s in ave.Segs)
                    {
                        for (float z = s.Z0 + 15f; z < s.Z1 - 15f; z += 48f)
                        {
                            AddStreetlightInstance(ax - s.W * 0.5f - 1.5f, z, 90f);
                            AddStreetlightInstance(ax + s.W * 0.5f + 1.5f, z, -90f);
                        }
                    }
                }
            }

            // 3. ConEd Steam Stacks & Volumetric Plumes
            if (buildSteamStacks)
            {
                var steamLocs = new[]
                {
                    new { x = 6.4f,   z = 96f,   wx = -1.0f, wz = -0.18f }, // 5th Ave & 37th St
                    new { x = -7.5f,  z = 148f,  wx = 0.95f, wz = -0.22f }, // 5th Ave near 34th (ESB)
                    new { x = 6.4f,   z = 48f,   wx = -1.0f, wz = 0.15f  }, // 5th Ave & 40th (NYPL)
                    new { x = -8.0f,  z = -18f,  wx = 0.92f, wz = -0.20f }, // 5th Ave & 43rd
                    new { x = -206f,  z = -32f,  wx = 0.95f, wz = 0.22f  }, // Times Square
                    new { x = 6.4f,   z = 285f,  wx = -0.9f, wz = -0.22f }, // 5th Ave & 26th (Flatiron)
                    new { x = 6.4f,   z = 615f,  wx = -0.9f, wz = 0.18f  }, // Washington Sq
                    new { x = -250f,  z = 5200f, wx = 0.95f, wz = -0.20f }  // One WTC / Financial District
                };

                for (int sIdx = 0; sIdx < steamLocs.Length; sIdx++)
                {
                    var sl = steamLocs[sIdx];
                    Vector3 unityPos = CityFrame.ToUnity(sl.x, 0.0f, sl.z);
                    AddSteamStackInstance(sl.x, sl.z);

                    // 36 Billowing Camera-Facing Volumetric Smoke Sprites per Stack
                    int puffsPerStack = 36;
                    for (int p = 0; p < puffsPerStack; p++)
                    {
                        float phaseOffset = (p + ((sIdx * 0.37f) % 1.0f)) / puffsPerStack;
                        float spinDir = p % 2 == 0 ? 1.0f : -1.0f;
                        float spinSpeed = spinDir * (0.28f + ((p * 0.19f + sIdx * 0.13f) % 0.45f));
                        float curlSeed = (p * 1.73f + sIdx * 2.41f) % (Mathf.PI * 2f);
                        float scaleMul = 0.86f + ((p * 0.31f + sIdx * 0.17f) % 0.36f);

                        _steamPuffs.Add(new SteamPlume
                        {
                            Origin = new Vector4(unityPos.x, 2.75f, unityPos.z, phaseOffset),
                            Wind = new Vector4(sl.wx * 4.5f, 7.4f, -sl.wz * 4.5f, scaleMul),
                            Params = new Vector4(spinSpeed, curlSeed, 0, 0)
                        });
                    }
                }
            }

            // 4. Hydrants, Mailboxes & Litter Baskets
            if (buildHydrantsAndBaskets && geo.Avenues != null)
            {
                foreach (var ave in geo.Avenues)
                {
                    float ax = ave.X;
                    foreach (var s in ave.Segs)
                    {
                        for (float z = s.Z0 + 30f; z < s.Z1 - 30f; z += 90f)
                        {
                            // Hydrant near curb
                            AddMatrix(_hydrantMatrices, ax - s.W * 0.5f - 1.2f, 0.18f, z, 0f, 1f);
                            // Blue Mailbox
                            AddMatrix(_mailboxMatrices, ax + s.W * 0.5f + 1.8f, 0.18f, z + 15f, 90f, 1f);
                            // Litter Basket
                            AddMatrix(_trashMatrices, ax - s.W * 0.5f - 1.8f, 0.18f, z + 25f, 0f, 1f);
                        }
                    }
                }
            }

            // 5. MTA Subway Entrances
            if (buildSubwayEntrances && geo.Streets != null)
            {
                foreach (var st in geo.Streets)
                {
                    if (st.N % 4 == 0 && st.N >= 14 && st.N <= 59)
                    {
                        float ax = CityConfig.AvenueX("6");
                        AddMatrix(_subwayMatrices, ax - 16f, 0.18f, st.Z + 8f, 0f, 1f);
                    }
                }
            }
        }

        void AddSignalInstance(float x, float z, float rotDeg) => AddMatrix(_signalMatrices, x, 0.18f, z, rotDeg, 1f);
        void AddStreetlightInstance(float x, float z, float rotDeg) => AddMatrix(_streetlightMatrices, x, 0.18f, z, rotDeg, 1f);

        void AddSteamStackInstance(float x, float z)
        {
            // Orange and white alternating bands (2.85m tall stack)
            for (int b = 0; b < 5; b++)
            {
                float by = 0.28f + b * 0.56f;
                float rScale = 0.36f - b * 0.025f;
                if (b % 2 == 0) AddMatrix(_steamOrangeMatrices, x, by, z, 0f, rScale);
                else AddMatrix(_steamWhiteMatrices, x, by, z, 0f, rScale);
            }

            // Sawhorse barricades around stack
            AddMatrix(_barricadeMatrices, x + 1.4f, 0.45f, z, 0f, 1f);
            AddMatrix(_barricadeMatrices, x - 1.4f, 0.45f, z, 0f, 1f);
        }

        static void AddMatrix(List<Matrix4x4> list, float x, float y, float z, float rotDeg, float scale)
        {
            Vector3 unityPos = CityFrame.ToUnity(x, y, z);
            Quaternion rot = Quaternion.Euler(0, -rotDeg, 0);
            Vector3 sc = new Vector3(scale, scale, scale);
            list.Add(Matrix4x4.TRS(unityPos, rot, sc));
        }

        // =========================================================================
        // Procedural Furniture Geometries
        // =========================================================================
        void BuildFurnitureMeshes()
        {
            _signalMesh = CreateSignalMesh();
            _streetlightMesh = CreateStreetlightMesh();
            _steamStackMesh = CreateSteamStackMesh();
            _barricadeMesh = CreateBarricadeMesh();
            _smokeQuadMesh = CreateSmokeQuadMesh();
            _hydrantMesh = CreateHydrantMesh();
            _mailboxMesh = CreateMailboxMesh();
            _trashMesh = CreateTrashMesh();
            _subwayMesh = CreateSubwayMesh();
        }

        static Mesh CreateSignalMesh()
        {
            var m = new Mesh { name = "TrafficSignal" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Vertical yellow mast pole (height 6.5m)
            AddBoxSimple(verts, norms, uvs, tris, Vector3.up * 3.25f, new Vector3(0.24f, 6.5f, 0.24f));
            // Cantilever horizontal arm reaching 6.2m over traffic lane
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(3.1f, 6.2f, 0), new Vector3(6.2f, 0.22f, 0.22f));
            // 3-Aspect signal housing on arm
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(5.2f, 5.42f, 0), new Vector3(0.45f, 1.4f, 0.45f));

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateStreetlightMesh()
        {
            var m = new Mesh { name = "StreetlightCobra" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Vertical galvanized steel pole (8.5m high)
            AddBoxSimple(verts, norms, uvs, tris, Vector3.up * 4.25f, new Vector3(0.26f, 8.5f, 0.26f));
            // Curved outreach arm
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(1.3f, 8.4f, 0), new Vector3(2.6f, 0.18f, 0.18f));
            // Cobra luminaire head
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(2.6f, 8.35f, 0), new Vector3(0.85f, 0.22f, 0.38f));

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateSteamStackMesh()
        {
            var m = new Mesh { name = "SteamStackBand" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            int segs = 12;
            float r = 0.35f, h = 0.56f;
            for (int i = 0; i <= segs; i++)
            {
                float a = (i / (float)segs) * Mathf.PI * 2f;
                float cs = Mathf.Cos(a), sn = Mathf.Sin(a);
                verts.Add(new Vector3(cs * r, 0, sn * r)); norms.Add(new Vector3(cs, 0, sn)); uvs.Add(new Vector2(i / (float)segs, 0));
                verts.Add(new Vector3(cs * r, h, sn * r)); norms.Add(new Vector3(cs, 0, sn)); uvs.Add(new Vector2(i / (float)segs, 1));
            }
            for (int i = 0; i < segs; i++)
            {
                int i0 = i * 2, i1 = i * 2 + 1, i2 = (i + 1) * 2, i3 = (i + 1) * 2 + 1;
                tris.Add(i0); tris.Add(i1); tris.Add(i2);
                tris.Add(i2); tris.Add(i1); tris.Add(i3);
            }

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateBarricadeMesh()
        {
            var m = new Mesh { name = "SawhorseBarricade" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Horizontal cross plank
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.85f, 0), new Vector3(1.8f, 0.22f, 0.08f));
            // A-frame legs
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-0.75f, 0.45f, 0), new Vector3(0.12f, 0.9f, 0.6f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0.75f, 0.45f, 0), new Vector3(0.12f, 0.9f, 0.6f));

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateSmokeQuadMesh()
        {
            var m = new Mesh { name = "SmokeParticleQuad" };
            var verts = new[]
            {
                new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0)
            };
            var uvs = new[]
            {
                new Vector2(0, 0), new Vector2(1, 0),
                new Vector2(1, 1), new Vector2(0, 1)
            };
            var norms = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            var tris = new[] { 0, 1, 2, 0, 2, 3 };

            m.SetVertices(new List<Vector3>(verts));
            m.SetNormals(new List<Vector3>(norms));
            SetAllUVs(m, new List<Vector2>(uvs), 4);
            m.SetTriangles(new List<int>(tris), 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateHydrantMesh()
        {
            var m = new Mesh { name = "FireHydrant" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Base body
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.4f, 0), new Vector3(0.32f, 0.8f, 0.32f));
            // Cap
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.85f, 0), new Vector3(0.24f, 0.15f, 0.24f));
            // Nozzles
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.5f, 0.18f), new Vector3(0.16f, 0.16f, 0.18f));

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateMailboxMesh()
        {
            var m = new Mesh { name = "Mailbox" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Post legs
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.25f, 0), new Vector3(0.42f, 0.5f, 0.42f));
            // Main collection box
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.85f, 0), new Vector3(0.6f, 0.75f, 0.55f));
            // Rounded chute lid
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 1.28f, 0), new Vector3(0.56f, 0.16f, 0.52f));

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateTrashMesh()
        {
            var m = new Mesh { name = "LitterBasket" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.45f, 0), new Vector3(0.48f, 0.9f, 0.48f));

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateSubwayMesh()
        {
            var m = new Mesh { name = "SubwayEntrance" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Perimeter wrought iron balustrade
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-1.6f, 0.6f, 0), new Vector3(0.12f, 1.2f, 4.2f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(1.6f, 0.6f, 0), new Vector3(0.12f, 1.2f, 4.2f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.6f, 2.1f), new Vector3(3.2f, 1.2f, 0.12f));

            // Twin green globes on posts at entrance
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-1.5f, 1.5f, -2.0f), new Vector3(0.12f, 0.8f, 0.12f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(1.5f, 1.5f, -2.0f), new Vector3(0.12f, 0.8f, 0.12f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-1.5f, 2.05f, -2.0f), new Vector3(0.38f, 0.38f, 0.38f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(1.5f, 2.05f, -2.0f), new Vector3(0.38f, 0.38f, 0.38f));

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static void AddBoxSimple(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                                 Vector3 center, Vector3 size)
        {
            float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;
            Vector3[] c = new[]
            {
                center + new Vector3(-hx, -hy, -hz), center + new Vector3(hx, -hy, -hz),
                center + new Vector3(hx, -hy, hz), center + new Vector3(-hx, -hy, hz),
                center + new Vector3(-hx, hy, -hz), center + new Vector3(hx, hy, -hz),
                center + new Vector3(hx, hy, hz), center + new Vector3(-hx, hy, hz)
            };

            int[][] faces = new[]
            {
                new[] { 0, 1, 5, 4 }, new[] { 2, 3, 7, 6 },
                new[] { 3, 0, 4, 7 }, new[] { 1, 2, 6, 5 },
                new[] { 4, 5, 6, 7 }
            };
            Vector3[] fn = new[]
            {
                Vector3.back, Vector3.forward, Vector3.left, Vector3.right, Vector3.up
            };

            for (int f = 0; f < 5; f++)
            {
                int b = verts.Count;
                for (int v = 0; v < 4; v++)
                {
                    verts.Add(c[faces[f][v]]);
                    norms.Add(fn[f]);
                    uvs.Add(new Vector2((v == 1 || v == 2) ? 1f : 0f, (v >= 2) ? 1f : 0f));
                }
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
        }

        static void SetAllUVs(Mesh mesh, List<Vector2> uv0, int count)
        {
            var dummy = new List<Vector2>(count);
            for (int i = 0; i < count; i++) dummy.Add(Vector2.zero);
            mesh.SetUVs(0, uv0 != null && uv0.Count == count ? uv0 : dummy);
            mesh.SetUVs(1, dummy);
            mesh.SetUVs(2, dummy);
            mesh.SetUVs(3, dummy);
        }

        // =========================================================================
        // Procedural 256x256 Smoke/Steam Texture Synthesizer (from tests/smoke-texture.test.cjs)
        // =========================================================================
        public static Texture2D GenerateSmokeTexture()
        {
            int size = 256;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "ProceduralStreetSmoke",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            var colors = new Color32[size * size];

            // Value noise + fbm helpers
            float Hash(int ix, int iy)
            {
                float s = Mathf.Sin(ix * 127.1f + iy * 311.7f + 19.19f) * 43758.5453f;
                return s - Mathf.Floor(s);
            }

            float VNoise(float x, float y)
            {
                int ix = Mathf.FloorToInt(x);
                int iy = Mathf.FloorToInt(y);
                float fx = x - ix;
                float fy = y - iy;
                float ux = fx * fx * (3f - 2f * fx);
                float uy = fy * fy * (3f - 2f * fy);
                float a = Hash(ix, iy);
                float b = Hash(ix + 1, iy);
                float c = Hash(ix, iy + 1);
                float d = Hash(ix + 1, iy + 1);
                return a + (b - a) * ux + (c - a) * uy + (a - b - c + d) * ux * uy;
            }

            float Fbm(float x, float y)
            {
                float v = 0f;
                float amp = 0.52f;
                float freq = 1f;
                for (int o = 0; o < 5; o++)
                {
                    v += VNoise(x * freq, y * freq) * amp;
                    freq *= 2.05f;
                    amp *= 0.5f;
                }
                return v;
            }

            for (int py = 0; py < size; py++)
            {
                float ny = (py / (float)(size - 1)) * 2f - 1f;
                for (int px = 0; px < size; px++)
                {
                    float nx = (px / (float)(size - 1)) * 2f - 1f;
                    float r = Mathf.Sqrt(nx * nx + ny * ny);
                    int idx = py * size + px;

                    if (r >= 0.98f)
                    {
                        colors[idx] = new Color32(255, 255, 255, 0);
                        continue;
                    }

                    float u = nx * 2.3f + 4.1f;
                    float v = ny * 2.3f + 7.3f;
                    float wx = Fbm(u, v) - 0.5f;
                    float wy = Fbm(u + 5.2f, v + 1.3f) - 0.5f;
                    float cloud = Fbm(u + wx * 2.2f, v + wy * 2.2f);
                    float fineWisp = Fbm((u + wx) * 2.4f + 11.7f, (v + wy) * 2.4f + 3.9f);

                    float angle = Mathf.Atan2(ny, nx);
                    float lobeRadius = 0.78f + 0.11f * Mathf.Sin(angle * 3f + wx * 4f) + 0.07f * Mathf.Cos(angle * 5f - wy * 4f);
                    float normR = r / Mathf.Max(0.25f, lobeRadius);
                    float radialFalloff = Mathf.Pow(Mathf.Max(0f, 1f - normR * normR), 1.95f);

                    float density = radialFalloff * Mathf.Clamp01((cloud * 0.72f + fineWisp * 0.38f - 0.14f) * 1.45f);

                    float cloudUp = Fbm(u + wx * 2.2f, v - 0.18f + wy * 2.2f);
                    float cloudLeft = Fbm(u - 0.18f + wx * 2.2f, v + wy * 2.2f);
                    float dUp = (cloud - cloudUp) * 1.8f - ny * 0.45f;
                    float dLeft = (cloud - cloudLeft) * 1.8f - nx * 0.35f;

                    float sunLobe = Mathf.Clamp01(0.58f + dUp * 0.42f + dLeft * 0.28f);
                    float hemiGrad = Mathf.Clamp01((1f - ny) * 0.5f * 0.75f + cloud * 0.25f);

                    byte bR = (byte)(Mathf.Clamp01(sunLobe) * 255);
                    byte bG = (byte)(Mathf.Clamp01(hemiGrad) * 255);
                    byte bB = (byte)(Mathf.Clamp01(radialFalloff) * 255);
                    byte bA = (byte)(Mathf.Clamp01(density) * 255);

                    colors[idx] = new Color32(bR, bG, bB, bA);
                }
            }

            tex.SetPixels32(colors);
            tex.Apply();
            return tex;
        }

        void EnsureMaterials()
        {
            if (bakedSmokeTexture == null) bakedSmokeTexture = GenerateSmokeTexture();

            if (steamParticleMaterial == null)
            {
                Shader s = Shader.Find("Nyc/StreetSmoke");
                if (s == null) s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                steamParticleMaterial = new Material(s) { name = "StreetSmoke_Material" };
                if (steamParticleMaterial.HasProperty("_MainTex"))
                    steamParticleMaterial.SetTexture("_MainTex", bakedSmokeTexture);
                steamParticleMaterial.enableInstancing = true;
            }

            if (trafficSignalPoleMaterial == null) trafficSignalPoleMaterial = MakeMat("Signal_Yellow", new Color(0.95f, 0.78f, 0.12f), 0.45f, 0.1f);
            if (streetlightMaterial == null) streetlightMaterial = MakeMat("Streetlight_Galvanized", new Color(0.65f, 0.68f, 0.70f), 0.55f, 0.75f);
            if (steamStackOrangeMaterial == null) steamStackOrangeMaterial = MakeMat("Steam_Orange", new Color(1.0f, 0.35f, 0.08f), 0.40f, 0.0f);
            if (steamStackWhiteMaterial == null) steamStackWhiteMaterial = MakeMat("Steam_White", new Color(0.95f, 0.95f, 0.95f), 0.40f, 0.0f);
            if (hydrantMaterial == null) hydrantMaterial = MakeMat("Hydrant_Silver", new Color(0.80f, 0.82f, 0.85f), 0.60f, 0.60f);
            if (mailboxMaterial == null) mailboxMaterial = MakeMat("Mailbox_Blue", new Color(0.08f, 0.28f, 0.65f), 0.55f, 0.20f);
            if (litterBasketMaterial == null) litterBasketMaterial = MakeMat("Trash_Green", new Color(0.12f, 0.38f, 0.20f), 0.35f, 0.10f);
            if (subwayEntranceMaterial == null) subwayEntranceMaterial = MakeMat("Subway_Iron", new Color(0.12f, 0.14f, 0.15f), 0.50f, 0.80f);
        }

        static Material MakeMat(string name, Color color, float smoothness, float metallic)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            mat.enableInstancing = true;
            return mat;
        }
    }
}
