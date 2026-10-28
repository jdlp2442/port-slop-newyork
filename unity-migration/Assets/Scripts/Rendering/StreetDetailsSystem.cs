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

        [Header("Expanded Street Furniture")]
        public Material benchMaterial;
        public Material dumpsterMaterial;
        public Material stopSignMaterial;
        public Material trafficConeMaterial;
        public Material foodCartBodyMaterial;
        public Material foodCartUmbrellaMaterial;
        public Material subwayGrateMaterial;

        [Header("Steam Smoke Texture")]
        public Texture2D bakedSmokeTexture;

        public bool buildSignals = true;
        public bool buildStreetlights = true;
        public bool buildSteamStacks = true;
        public bool buildHydrantsAndBaskets = true;
        public bool buildSubwayEntrances = true;
        public bool buildBenchesAndDumpsters = true;

        Mesh _signalMesh;
        Mesh _signalRedLensMesh;
        Mesh _signalAmberLensMesh;
        Mesh _signalGreenLensMesh;
        Mesh _streetlightMesh;
        Mesh _steamStackMesh;
        Mesh _barricadeMesh;
        Mesh _smokeQuadMesh;
        Mesh _hydrantMesh;
        Mesh _mailboxMesh;
        Mesh _trashMesh;
        Mesh _subwayMesh;
        Mesh _benchMesh;
        Mesh _dumpsterMesh;
        Mesh _stopSignMesh;
        Mesh _trafficConeMesh;
        Mesh _foodCartMesh;
        Mesh _foodCartUmbrellaMesh;
        Mesh _subwayGrateMesh;

        Material _signalRedLitMat;
        Material _signalAmberLitMat;
        Material _signalGreenLitMat;
        Material _signalDarkMat;

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
        readonly List<Matrix4x4> _benchMatrices = new List<Matrix4x4>(256);
        readonly List<Matrix4x4> _dumpsterMatrices = new List<Matrix4x4>(128);
        readonly List<Matrix4x4> _stopSignMatrices = new List<Matrix4x4>(512);
        readonly List<Matrix4x4> _trafficConeMatrices = new List<Matrix4x4>(256);
        readonly List<Matrix4x4> _foodCartMatrices = new List<Matrix4x4>(128);
        readonly List<Matrix4x4> _subwayGrateMatrices = new List<Matrix4x4>(256);

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

            // Dynamic 3-aspect lenses in sync with CityConfig.SignalPhase
            if (_signalRedLensMesh != null && _signalRedLitMat != null)
            {
                float curTime = Time.time;
                int phase = CityConfig.SignalPhase(curTime, true);
                Material redMat = (phase >= 2) ? _signalRedLitMat : _signalDarkMat;
                Material amberMat = (phase == 1) ? _signalAmberLitMat : _signalDarkMat;
                Material greenMat = (phase == 0) ? _signalGreenLitMat : _signalDarkMat;

                DrawInstancedBatch(_signalRedLensMesh, redMat, _signalMatrices);
                DrawInstancedBatch(_signalAmberLensMesh, amberMat, _signalMatrices);
                DrawInstancedBatch(_signalGreenLensMesh, greenMat, _signalMatrices);
            }

            DrawInstancedBatch(_streetlightMesh, streetlightMaterial, _streetlightMatrices);
            DrawInstancedBatch(_steamStackMesh, steamStackOrangeMaterial, _steamOrangeMatrices);
            DrawInstancedBatch(_steamStackMesh, steamStackWhiteMaterial, _steamWhiteMatrices);
            DrawInstancedBatch(_barricadeMesh, steamStackOrangeMaterial, _barricadeMatrices);
            DrawInstancedBatch(_hydrantMesh, hydrantMaterial, _hydrantMatrices);
            DrawInstancedBatch(_mailboxMesh, mailboxMaterial, _mailboxMatrices);
            DrawInstancedBatch(_trashMesh, litterBasketMaterial, _trashMatrices);
            DrawInstancedBatch(_subwayMesh, subwayEntranceMaterial, _subwayMatrices);
            DrawInstancedBatch(_benchMesh, benchMaterial, _benchMatrices);
            DrawInstancedBatch(_dumpsterMesh, dumpsterMaterial, _dumpsterMatrices);
            DrawInstancedBatch(_stopSignMesh, stopSignMaterial, _stopSignMatrices);
            DrawInstancedBatch(_trafficConeMesh, trafficConeMaterial, _trafficConeMatrices);
            DrawInstancedBatch(_foodCartMesh, foodCartBodyMaterial, _foodCartMatrices);
            DrawInstancedBatch(_foodCartUmbrellaMesh, foodCartUmbrellaMaterial, _foodCartMatrices);
            DrawInstancedBatch(_subwayGrateMesh, subwayGrateMaterial, _subwayGrateMatrices);
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
            if (!CitySpatialIndex.Ready && geo != null) CitySpatialIndex.Build(geo);

            _signalMatrices.Clear();
            _streetlightMatrices.Clear();
            _steamOrangeMatrices.Clear();
            _steamWhiteMatrices.Clear();
            _barricadeMatrices.Clear();
            _hydrantMatrices.Clear();
            _mailboxMatrices.Clear();
            _trashMatrices.Clear();
            _subwayMatrices.Clear();
            _benchMatrices.Clear();
            _dumpsterMatrices.Clear();
            _stopSignMatrices.Clear();
            _trafficConeMatrices.Clear();
            _foodCartMatrices.Clear();
            _subwayGrateMatrices.Clear();
            _steamPuffs.Clear();
            _placedCurbProps.Clear();

            // 1. Cobra-head LED Streetlights along Avenue Curbs (registered first for clearance)
            if (buildStreetlights && geo.Avenues != null)
            {
                foreach (var ave in geo.Avenues)
                {
                    float ax = ave.X;
                    foreach (var s in ave.Segs)
                    {
                        for (float z = s.Z0 + 15f; z < s.Z1 - 15f; z += 42f)
                        {
                            float xLeft = ax - s.W * 0.5f - 1.4f;
                            if (CitySpatialIndex.IsOnSidewalk(xLeft, z) || CitySpatialIndex.IsOnSidewalk(xLeft - 1.0f, z))
                            {
                                TryAddCurbProp(_streetlightMatrices, xLeft, 0.18f, z, 0f, 1f, minClearance: 5.5f);
                            }

                            float xRight = ax + s.W * 0.5f + 1.4f;
                            if (CitySpatialIndex.IsOnSidewalk(xRight, z) || CitySpatialIndex.IsOnSidewalk(xRight + 1.0f, z))
                            {
                                TryAddCurbProp(_streetlightMatrices, xRight, 0.18f, z, 180f, 1f, minClearance: 5.5f);
                            }
                        }
                    }
                }
            }

            // 2. Traffic Signals & Stop Signs at Intersections (Correctly oriented cantilever arms over traffic)
            if (buildSignals && geo.Streets != null && geo.Avenues != null)
            {
                foreach (var ave in geo.Avenues)
                {
                    float ax = ave.X;
                    float aveW = (ave.Segs != null && ave.Segs.Count > 0) ? ave.Segs[0].W : 30f;
                    float cornerOffsetX = aveW * 0.5f + 1.35f;

                    foreach (var st in geo.Streets)
                    {
                        if (st.N < 14 || st.N > 59) continue;
                        float sz = st.Z;
                        float stW = (st.Segs != null && st.Segs.Count > 0) ? st.Segs[0].Width : 18f;
                        float cornerOffsetZ = stW * 0.5f + 1.35f;

                        // Alternate signalized intersections (even streets) to prevent mast clutter
                        if (st.N % 2 == 0)
                        {
                            // East sidewalk corner: cantilever arm reaches west (-X) over roadway -> rot 180 deg
                            float px1 = ax + cornerOffsetX;
                            float pz1 = sz - cornerOffsetZ;
                            if ((CitySpatialIndex.IsOnSidewalk(px1, pz1) || CitySpatialIndex.IsOnSidewalk(px1 + 0.85f, pz1 - 0.85f))
                                && TryRegisterCurbPosition(px1, pz1, 5f))
                            {
                                AddSignalInstance(px1, pz1, 180f);
                            }

                            // West sidewalk corner: cantilever arm reaches east (+X) over roadway -> rot 0 deg
                            float px2 = ax - cornerOffsetX;
                            float pz2 = sz + cornerOffsetZ;
                            if ((CitySpatialIndex.IsOnSidewalk(px2, pz2) || CitySpatialIndex.IsOnSidewalk(px2 - 0.85f, pz2 + 0.85f))
                                && TryRegisterCurbPosition(px2, pz2, 5f))
                            {
                                AddSignalInstance(px2, pz2, 0f);
                            }
                        }
                        else
                        {
                            // Stop signs on alternate cross streets entering avenue
                            float sx1 = ax - cornerOffsetX + 2.5f;
                            float sz1 = sz - cornerOffsetZ - 1.2f;
                            if ((CitySpatialIndex.IsOnSidewalk(sx1, sz1) || CitySpatialIndex.IsOnSidewalk(sx1 - 0.8f, sz1))
                                && TryRegisterCurbPosition(sx1, sz1, 4f))
                            {
                                AddMatrix(_stopSignMatrices, sx1, 0.18f, sz1, 90f, 1f);
                            }
                        }
                    }
                }
            }

            // 3. ConEd Steam Stacks, Safety Cones & Volumetric Plumes
            if (buildSteamStacks)
            {
                var rawSteamLocs = new[]
                {
                    new { x = 16.5f,  z = 96f,   wx = -1.0f, wz = -0.18f }, // 5th Ave & 37th St curb
                    new { x = -17.0f, z = 148f,  wx = 0.95f, wz = -0.22f }, // 5th Ave near 34th (ESB)
                    new { x = 16.5f,  z = 48f,   wx = -1.0f, wz = 0.15f  }, // 5th Ave & 40th (NYPL)
                    new { x = -17.5f, z = -18f,  wx = 0.92f, wz = -0.20f }, // 5th Ave & 43rd
                    new { x = -206f,  z = -32f,  wx = 0.95f, wz = 0.22f  }, // Times Square
                    new { x = 16.5f,  z = 285f,  wx = -0.9f, wz = -0.22f }, // 5th Ave & 26th (Flatiron)
                    new { x = 16.5f,  z = 615f,  wx = -0.9f, wz = 0.18f  }, // Washington Sq
                    new { x = -235f,  z = 5180f, wx = 0.95f, wz = -0.20f }  // One WTC / Financial District
                };

                for (int sIdx = 0; sIdx < rawSteamLocs.Length; sIdx++)
                {
                    var sl = rawSteamLocs[sIdx];
                    Vector2 snapped = SnapToCurbIsland(sl.x, sl.z, 1.2f);
                    float sx = snapped.x;
                    float sz = snapped.y;

                    Vector3 unityPos = CityFrame.ToUnity(sx, 0.0f, sz);
                    AddSteamStackInstance(sx, sz);

                    // Safety cones cleanly placed at outer perimeter (2.4m radius) clear of barricades
                    AddMatrix(_trafficConeMatrices, sx + 0.95f, 0.18f, sz + 2.15f, 15f, 1f);
                    AddMatrix(_trafficConeMatrices, sx - 0.95f, 0.18f, sz + 2.15f, -15f, 1f);
                    AddMatrix(_trafficConeMatrices, sx + 0.95f, 0.18f, sz - 2.15f, 165f, 1f);
                    AddMatrix(_trafficConeMatrices, sx - 0.95f, 0.18f, sz - 2.15f, -165f, 1f);

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

            // 4. Hydrants, Mailboxes, Benches, Dumpsters & Litter Baskets (Mid-block sidewalks verified with spatial exclusion)
            if (buildHydrantsAndBaskets && geo.Avenues != null && geo.Streets != null)
            {
                foreach (var ave in geo.Avenues)
                {
                    float ax = ave.X;
                    float aveW = (ave.Segs != null && ave.Segs.Count > 0) ? ave.Segs[0].W : 30f;

                    for (int row = 0; row < geo.Streets.Count - 1; row++)
                    {
                        var st0 = geo.Streets[row];
                        var st1 = geo.Streets[row + 1];
                        if (st0.N < 14 || st1.N > 59) continue;

                        float zMid = (st0.Z + st1.Z) * 0.5f;

                        // Hydrant near west curb mid-block
                        float hx = ax - aveW * 0.5f - 1.15f;
                        float hz = zMid - 16f;
                        if (CitySpatialIndex.IsOnSidewalk(hx, hz) || CitySpatialIndex.IsOnSidewalk(hx - 1.0f, hz))
                            TryAddCurbProp(_hydrantMatrices, hx, 0.18f, hz, 0f, 1f, minClearance: 4.5f);

                        // Blue USPS Mailbox near east curb mid-block (opening faces sidewalk)
                        float mx = ax + aveW * 0.5f + 1.25f;
                        float mz = zMid - 22f;
                        if (CitySpatialIndex.IsOnSidewalk(mx, mz) || CitySpatialIndex.IsOnSidewalk(mx + 1.0f, mz))
                            TryAddCurbProp(_mailboxMatrices, mx, 0.18f, mz, 180f, 1f, minClearance: 4.5f);

                        // Litter Basket near west curb
                        float tx = ax - aveW * 0.5f - 1.25f;
                        float tz = zMid + 16f;
                        if (CitySpatialIndex.IsOnSidewalk(tx, tz) || CitySpatialIndex.IsOnSidewalk(tx - 1.0f, tz))
                            TryAddCurbProp(_trashMatrices, tx, 0.18f, tz, 0f, 1f, minClearance: 4.5f);

                        // Park Benches: recessed against building facade line, facing the sidewalk / roadway
                        // Offset in Z by 8.5m from mid-block to maintain generous separation from light poles (step 42m)
                        if (row % 2 == 0)
                        {
                            float bz = (row % 4 == 0) ? zMid - 8.5f : zMid + 8.5f;

                            // West sidewalk bench: sits along interior facade, facing east towards avenue
                            float bxLeft = ax - aveW * 0.5f - 3.2f;
                            if (CitySpatialIndex.IsOnSidewalk(bxLeft, bz) && CitySpatialIndex.IsOnSidewalk(bxLeft + 1.2f, bz) && CitySpatialIndex.IsOnSidewalk(bxLeft - 0.7f, bz))
                            {
                                TryAddCurbProp(_benchMatrices, bxLeft, 0.18f, bz, -90f, 1f, minClearance: 6.0f);
                            }

                            // East sidewalk bench: sits along interior facade, facing west towards avenue
                            float bxRight = ax + aveW * 0.5f + 3.2f;
                            if (CitySpatialIndex.IsOnSidewalk(bxRight, bz) && CitySpatialIndex.IsOnSidewalk(bxRight - 1.2f, bz) && CitySpatialIndex.IsOnSidewalk(bxRight + 0.7f, bz))
                            {
                                TryAddCurbProp(_benchMatrices, bxRight, 0.18f, bz, 90f, 1f, minClearance: 6.0f);
                            }
                        }

                        // Commercial Big Dumpster in side-street loading zone every 3 blocks (aligned parallel to curb)
                        if (row % 3 == 0)
                        {
                            float dx = ax - aveW * 0.5f - 12f;
                            float dz = st0.Z - 3.8f;
                            if (CitySpatialIndex.IsOnSidewalk(dx, dz))
                                TryAddCurbProp(_dumpsterMatrices, dx, 0.18f, dz, 90f, 1f, minClearance: 5.5f);
                        }
                    }
                }
            }

            // 5. MTA Subway Entrances (Snapped and verified to curb islands with clearance)
            if (buildSubwayEntrances && geo.Streets != null)
            {
                foreach (var st in geo.Streets)
                {
                    if (st.N % 4 == 0 && st.N >= 14 && st.N <= 59)
                    {
                        float ax = CityConfig.AvenueX("6");
                        Vector2 subPos = SnapToCurbIsland(ax - 17.5f, st.Z + 12f, 1.2f);
                        if (CitySpatialIndex.IsOnSidewalk(subPos.x, subPos.y))
                            TryAddCurbProp(_subwayMatrices, subPos.x, 0.18f, subPos.y, 0f, 1f, minClearance: 7.0f);
                    }
                }
            }

            // 6. NYC Food Carts & Halal/Hot Dog Stands at commercial plazas and Central Park perimeter
            if (geo.Streets != null && geo.Avenues != null)
            {
                foreach (var ave in geo.Avenues)
                {
                    float ax = ave.X;
                    float aveW = (ave.Segs != null && ave.Segs.Count > 0) ? ave.Segs[0].W : 30f;
                    string aName = ave.Label ?? ave.Id ?? "";
                    bool isMajorAve = aName.Contains("5") || aName.Contains("6") || aName.Contains("7") || aName.Contains("Broadway") || aName.Contains("Park");
                    if (!isMajorAve) continue;

                    for (int sIdx = 0; sIdx < geo.Streets.Count; sIdx += 5)
                    {
                        var st = geo.Streets[sIdx];
                        float fcX = ax - aveW * 0.5f - 2.4f;
                        float fcZ = st.Z + 7.5f;
                        if (CitySpatialIndex.IsOnSidewalk(fcX, fcZ))
                            TryAddCurbProp(_foodCartMatrices, fcX, 0.18f, fcZ, 45f, 1f, minClearance: 7.0f);
                    }
                }

                // Central Park South (59th St) iconic food carts
                float cpZ = CityConfig.StreetZ(59);
                for (float cpx = -600f; cpx <= 200f; cpx += 85f)
                {
                    float southSidewalkZ = cpZ + 6.5f;
                    if (CitySpatialIndex.IsOnSidewalk(cpx, southSidewalkZ))
                        TryAddCurbProp(_foodCartMatrices, cpx, 0.18f, southSidewalkZ, 0f, 1f, minClearance: 7.0f);
                }
            }

            // 7. NYC Subway Ventilation Grates embedded into sidewalk pavement
            if (geo.Streets != null && geo.Avenues != null)
            {
                foreach (var ave in geo.Avenues)
                {
                    float ax = ave.X;
                    float aveW = (ave.Segs != null && ave.Segs.Count > 0) ? ave.Segs[0].W : 30f;
                    for (int sIdx = 1; sIdx < geo.Streets.Count - 1; sIdx += 2)
                    {
                        var s0 = geo.Streets[sIdx];
                        var s1 = geo.Streets[sIdx + 1];
                        float zMid = (s0.Z + s1.Z) * 0.5f;

                        // Grate pair along curb line
                        float gxLeft = ax - aveW * 0.5f - 1.4f;
                        if (CitySpatialIndex.IsOnSidewalk(gxLeft, zMid))
                        {
                            TryAddCurbProp(_subwayGrateMatrices, gxLeft, 0.19f, zMid - 2.2f, 90f, 1f, minClearance: 4.0f);
                            TryAddCurbProp(_subwayGrateMatrices, gxLeft, 0.19f, zMid + 2.2f, 90f, 1f, minClearance: 4.0f);
                        }

                        float gxRight = ax + aveW * 0.5f + 1.4f;
                        if (CitySpatialIndex.IsOnSidewalk(gxRight, zMid))
                        {
                            TryAddCurbProp(_subwayGrateMatrices, gxRight, 0.19f, zMid - 2.2f, -90f, 1f, minClearance: 4.0f);
                            TryAddCurbProp(_subwayGrateMatrices, gxRight, 0.19f, zMid + 2.2f, -90f, 1f, minClearance: 4.0f);
                        }
                    }
                }
            }

            Debug.Log($"[StreetDetails] Furniture populated: {_signalMatrices.Count} signals, {_stopSignMatrices.Count} stop signs, {_streetlightMatrices.Count} streetlights, {_hydrantMatrices.Count} hydrants, {_benchMatrices.Count} benches, {_dumpsterMatrices.Count} dumpsters, {_trafficConeMatrices.Count} cones, {_foodCartMatrices.Count} food carts, {_subwayGrateMatrices.Count} grates, {_mailboxMatrices.Count} mailboxes, {_trashMatrices.Count} trash, {_subwayMatrices.Count} subways, {_steamOrangeMatrices.Count} steam stacks.");
        }

        static Vector2 SnapToCurbIsland(float x, float z, float minPad)
        {
            if (CitySpatialIndex.IsOnSidewalk(x, z)) return new Vector2(x, z);
            for (float r = 2f; r <= 24f; r += 2f)
            {
                for (int k = 0; k < 16; k++)
                {
                    float ang = (k / 16f) * Mathf.PI * 2f;
                    float nx = x + Mathf.Cos(ang) * r;
                    float nz = z + Mathf.Sin(ang) * r;
                    if (CitySpatialIndex.IsOnSidewalk(nx, nz)) return new Vector2(nx, nz);
                }
            }
            return new Vector2(x, z);
        }

        readonly List<Vector2> _placedCurbProps = new List<Vector2>(8192);

        bool TryRegisterCurbPosition(float x, float z, float minDistance)
        {
            Vector2 p = new Vector2(x, z);
            float minSq = minDistance * minDistance;
            for (int i = 0; i < _placedCurbProps.Count; i++)
            {
                if ((_placedCurbProps[i] - p).sqrMagnitude < minSq) return false;
            }
            _placedCurbProps.Add(p);
            return true;
        }

        bool TryAddCurbProp(List<Matrix4x4> list, float x, float y, float z, float rotDeg, float scale, float minClearance = 4.5f)
        {
            if (!TryRegisterCurbPosition(x, z, minClearance)) return false;
            AddMatrix(list, x, y, z, rotDeg, scale);
            return true;
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
            _signalRedLensMesh = CreateSignalLensMesh(5.82f);
            _signalAmberLensMesh = CreateSignalLensMesh(5.42f);
            _signalGreenLensMesh = CreateSignalLensMesh(5.02f);
            _streetlightMesh = CreateStreetlightMesh();
            _steamStackMesh = CreateSteamStackMesh();
            _barricadeMesh = CreateBarricadeMesh();
            _smokeQuadMesh = CreateSmokeQuadMesh();
            _hydrantMesh = CreateHydrantMesh();
            _mailboxMesh = CreateMailboxMesh();
            _trashMesh = CreateTrashMesh();
            _subwayMesh = CreateSubwayMesh();
            _benchMesh = CreateBenchMesh();
            _dumpsterMesh = CreateDumpsterMesh();
            _stopSignMesh = CreateStopSignMesh();
            _trafficConeMesh = CreateTrafficConeMesh();
            _foodCartMesh = CreateFoodCartMesh();
            _foodCartUmbrellaMesh = CreateFoodCartUmbrellaMesh();
            _subwayGrateMesh = CreateSubwayGrateMesh();
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
            // Visors / hoods over lenses (angled sunshades)
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(5.2f, 5.94f, -0.28f), new Vector3(0.32f, 0.04f, 0.16f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(5.2f, 5.54f, -0.28f), new Vector3(0.32f, 0.04f, 0.16f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(5.2f, 5.14f, -0.28f), new Vector3(0.32f, 0.04f, 0.16f));

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateSignalLensMesh(float yOffset)
        {
            var m = new Mesh { name = "SignalLens" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Circular aspect lens on traffic face
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(5.2f, yOffset, -0.24f), new Vector3(0.26f, 0.26f, 0.04f));

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

        static Mesh CreateBenchMesh()
        {
#if UNITY_EDITOR
            var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Urban_Props_Pack_Rozity/Prefabs/URP/SM_Park_Bench_01.prefab");
            if (p != null)
            {
                var mf = p.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
            }
#endif
            var m = new Mesh { name = "ParkBench" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.48f, 0), new Vector3(1.8f, 0.08f, 0.52f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.82f, -0.22f), new Vector3(1.8f, 0.45f, 0.08f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-0.75f, 0.24f, 0), new Vector3(0.08f, 0.48f, 0.50f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0.75f, 0.24f, 0), new Vector3(0.08f, 0.48f, 0.50f));
            m.SetVertices(verts); m.SetNormals(norms); SetAllUVs(m, uvs, verts.Count); m.SetTriangles(tris, 0); m.RecalculateBounds();
            return m;
        }

        static Mesh CreateDumpsterMesh()
        {
#if UNITY_EDITOR
            var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Simple Street Props/Prefabs/Green Big Dumpster.prefab");
            if (p != null)
            {
                var mf = p.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
            }
#endif
            var m = new Mesh { name = "BigDumpster" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.65f, 0), new Vector3(1.6f, 1.3f, 0.95f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 1.34f, 0), new Vector3(1.55f, 0.12f, 0.90f));
            m.SetVertices(verts); m.SetNormals(norms); SetAllUVs(m, uvs, verts.Count); m.SetTriangles(tris, 0); m.RecalculateBounds();
            return m;
        }

        static Mesh CreateStopSignMesh()
        {
#if UNITY_EDITOR
            var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Urban_Props_Pack_Rozity/Prefabs/URP/SM_RoadSign_Stop.prefab");
            if (p != null)
            {
                var mf = p.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
            }
#endif
            var m = new Mesh { name = "StopSign" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 1.4f, 0), new Vector3(0.08f, 2.8f, 0.08f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 2.45f, 0.05f), new Vector3(0.75f, 0.75f, 0.04f));
            m.SetVertices(verts); m.SetNormals(norms); SetAllUVs(m, uvs, verts.Count); m.SetTriangles(tris, 0); m.RecalculateBounds();
            return m;
        }

        static Mesh CreateTrafficConeMesh()
        {
#if UNITY_EDITOR
            var p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Urban_Props_Pack_Rozity/Prefabs/URP/SM_TrafficCone_01.prefab");
            if (p != null)
            {
                var mf = p.GetComponentInChildren<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) return mf.sharedMesh;
            }
#endif
            var m = new Mesh { name = "TrafficCone" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.02f, 0), new Vector3(0.48f, 0.04f, 0.48f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.28f, 0), new Vector3(0.24f, 0.52f, 0.24f));
            m.SetVertices(verts); m.SetNormals(norms); SetAllUVs(m, uvs, verts.Count); m.SetTriangles(tris, 0); m.RecalculateBounds();
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

        static Mesh CreateFoodCartMesh()
        {
            var m = new Mesh { name = "FoodCart" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Stainless steel main cart body (1.4m x 0.95m x 0.95m)
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.58f, 0), new Vector3(1.4f, 0.85f, 0.95f));
            // Stainless front service counter shelf
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.98f, -0.55f), new Vector3(1.4f, 0.06f, 0.22f));
            // Condiment squeeze bottles (mustard / ketchup)
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-0.35f, 1.12f, -0.55f), new Vector3(0.09f, 0.22f, 0.09f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-0.20f, 1.12f, -0.55f), new Vector3(0.09f, 0.22f, 0.09f));
            // Pretzel warmer glass display box
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0.28f, 1.18f, -0.15f), new Vector3(0.65f, 0.38f, 0.55f));

            // Side wheels (left and right spoke cart wheels)
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-0.74f, 0.30f, 0), new Vector3(0.08f, 0.60f, 0.60f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0.74f, 0.30f, 0), new Vector3(0.08f, 0.60f, 0.60f));
            // Front small swivel caster
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.12f, -0.42f), new Vector3(0.12f, 0.24f, 0.12f));

            // Chrome umbrella pole mast
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0.48f, 1.70f, 0.35f), new Vector3(0.06f, 1.50f, 0.06f));

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateFoodCartUmbrellaMesh()
        {
            var m = new Mesh { name = "FoodCartUmbrella" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Octagonal umbrella canopy with rich relief
            int umbrellaSides = 8;
            float umbrellaRadius = 1.15f;
            float umbrellaApexY = 2.65f;
            float umbrellaBaseY = 2.30f;
            Vector3 uApex = new Vector3(0.48f, umbrellaApexY, 0.35f);

            for (int i = 0; i < umbrellaSides; i++)
            {
                float a0 = i * Mathf.PI * 2f / umbrellaSides;
                float a1 = (i + 1) * Mathf.PI * 2f / umbrellaSides;
                Vector3 p0 = new Vector3(0.48f + Mathf.Cos(a0) * umbrellaRadius, umbrellaBaseY, 0.35f + Mathf.Sin(a0) * umbrellaRadius);
                Vector3 p1 = new Vector3(0.48f + Mathf.Cos(a1) * umbrellaRadius, umbrellaBaseY, 0.35f + Mathf.Sin(a1) * umbrellaRadius);
                Vector3 normal = Vector3.Cross(p1 - p0, uApex - p0).normalized;

                int bIdx = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(uApex);
                norms.Add(normal); norms.Add(normal); norms.Add(normal);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0.5f, 1));
                tris.Add(bIdx); tris.Add(bIdx + 1); tris.Add(bIdx + 2);

                // Scalloped fringe flap
                Vector3 flap0 = p0 + Vector3.down * 0.14f;
                Vector3 flap1 = p1 + Vector3.down * 0.14f;
                Vector3 fNorm = Vector3.Cross(flap0 - p0, p1 - p0).normalized;
                int fbIdx = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(flap1); verts.Add(flap0);
                norms.Add(fNorm); norms.Add(fNorm); norms.Add(fNorm); norms.Add(fNorm);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                tris.Add(fbIdx); tris.Add(fbIdx + 1); tris.Add(fbIdx + 2);
                tris.Add(fbIdx); tris.Add(fbIdx + 2); tris.Add(fbIdx + 3);
            }

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateSubwayGrateMesh()
        {
            var m = new Mesh { name = "SubwayGrate" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Cast iron grate outer perimeter frame (1.30m x 2.40m x 0.04m)
            float gw = 1.30f, gl = 2.40f, gh = 0.04f, fw = 0.08f;
            // Border lips
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, gh * 0.5f, -gl * 0.5f + fw * 0.5f), new Vector3(gw, gh, fw));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, gh * 0.5f, gl * 0.5f - fw * 0.5f), new Vector3(gw, gh, fw));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-gw * 0.5f + fw * 0.5f, gh * 0.5f, 0), new Vector3(fw, gh, gl - fw * 2f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(gw * 0.5f - fw * 0.5f, gh * 0.5f, 0), new Vector3(fw, gh, gl - fw * 2f));

            // Center longitudinal spine bar
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, gh * 0.5f, 0), new Vector3(0.06f, gh, gl - fw * 2f));

            // Transverse ventilation slats
            for (float sz = -gl * 0.45f; sz <= gl * 0.45f; sz += 0.22f)
            {
                AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, gh * 0.5f, sz), new Vector3(gw - fw * 2f, gh * 0.9f, 0.045f));
            }

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
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

            if (benchMaterial == null)
            {
#if UNITY_EDITOR
                var pBench = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Urban_Props_Pack_Rozity/Prefabs/URP/SM_Park_Bench_01.prefab");
                if (pBench != null)
                {
                    var mr = pBench.GetComponentInChildren<MeshRenderer>();
                    if (mr != null && mr.sharedMaterial != null)
                        benchMaterial = new Material(mr.sharedMaterial) { name = "Bench_URP_Instanced", enableInstancing = true };
                }
#endif
                if (benchMaterial == null) benchMaterial = MakeMat("Bench_Wood", new Color(0.32f, 0.22f, 0.14f), 0.35f, 0.1f);
            }

            if (dumpsterMaterial == null)
            {
#if UNITY_EDITOR
                var pDumpster = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Simple Street Props/Prefabs/Green Big Dumpster.prefab");
                if (pDumpster != null)
                {
                    var mr = pDumpster.GetComponentInChildren<MeshRenderer>();
                    if (mr != null && mr.sharedMaterial != null)
                    {
                        var s = Shader.Find("Universal Render Pipeline/Lit");
                        dumpsterMaterial = new Material(s ? s : mr.sharedMaterial.shader) { name = "Dumpster_URP_Instanced", enableInstancing = true };
                        if (mr.sharedMaterial.mainTexture != null) dumpsterMaterial.mainTexture = mr.sharedMaterial.mainTexture;
                    }
                }
#endif
                if (dumpsterMaterial == null) dumpsterMaterial = MakeMat("Dumpster_Green", new Color(0.18f, 0.32f, 0.20f), 0.40f, 0.2f);
            }

            if (stopSignMaterial == null)
            {
#if UNITY_EDITOR
                var pSign = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Urban_Props_Pack_Rozity/Prefabs/URP/SM_RoadSign_Stop.prefab");
                if (pSign != null)
                {
                    var mr = pSign.GetComponentInChildren<MeshRenderer>();
                    if (mr != null && mr.sharedMaterial != null)
                        stopSignMaterial = new Material(mr.sharedMaterial) { name = "RoadSign_URP_Instanced", enableInstancing = true };
                }
#endif
                if (stopSignMaterial == null) stopSignMaterial = MakeMat("RoadSign_Red", new Color(0.85f, 0.12f, 0.12f), 0.65f, 0.3f);
            }

            if (trafficConeMaterial == null)
            {
#if UNITY_EDITOR
                var pCone = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Urban_Props_Pack_Rozity/Prefabs/URP/SM_TrafficCone_01.prefab");
                if (pCone != null)
                {
                    var mr = pCone.GetComponentInChildren<MeshRenderer>();
                    if (mr != null && mr.sharedMaterial != null)
                        trafficConeMaterial = new Material(mr.sharedMaterial) { name = "Cone_URP_Instanced", enableInstancing = true };
                }
#endif
                if (trafficConeMaterial == null) trafficConeMaterial = MakeMat("Cone_Orange", new Color(1.0f, 0.45f, 0.05f), 0.50f, 0.1f);
            }

            if (foodCartBodyMaterial == null) foodCartBodyMaterial = MakeMat("FoodCart_Stainless", new Color(0.85f, 0.87f, 0.90f), 0.85f, 0.85f);
            if (foodCartUmbrellaMaterial == null) foodCartUmbrellaMaterial = MakeMat("FoodCart_Umbrella", new Color(0.96f, 0.78f, 0.14f), 0.35f, 0.05f);
            if (subwayGrateMaterial == null) subwayGrateMaterial = MakeMat("Subway_Grate_CastIron", new Color(0.20f, 0.22f, 0.25f), 0.35f, 0.75f);

            if (_signalRedLitMat == null) _signalRedLitMat = MakeEmissiveMat("Signal_Red_Lit", new Color(0.95f, 0.08f, 0.08f), new Color(1.0f, 0.05f, 0.02f), 3.5f);
            if (_signalAmberLitMat == null) _signalAmberLitMat = MakeEmissiveMat("Signal_Amber_Lit", new Color(0.98f, 0.65f, 0.05f), new Color(1.0f, 0.70f, 0.04f), 3.0f);
            if (_signalGreenLitMat == null) _signalGreenLitMat = MakeEmissiveMat("Signal_Green_Lit", new Color(0.08f, 0.95f, 0.35f), new Color(0.05f, 1.0f, 0.40f), 3.5f);
            if (_signalDarkMat == null) _signalDarkMat = MakeMat("Signal_Dark_Lens", new Color(0.08f, 0.08f, 0.08f), 0.85f, 0.1f);
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

        static Material MakeEmissiveMat(string name, Color baseColor, Color emissionColor, float emissionIntensity)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            mat.color = baseColor;
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emissionColor * emissionIntensity);
            }
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.90f);
            mat.enableInstancing = true;
            return mat;
        }
    }
}
