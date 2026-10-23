using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Nyc.Core;
using Nyc.Data;

namespace Nyc.Generation
{
    /// <summary>
    /// Procedural mesh generator for New York City's surrounding boroughs, harbor islands,
    /// waterfront piers, bridges, the Palisades cliff ridge, and skyline clusters.
    /// Full 1:1 port of js/boroughs.js and waterfront piers from city-grid.js.
    /// </summary>
    public sealed class BoroughsMeshBuilder : MonoBehaviour
    {
        public bool buildLandMasses = true;
        public bool buildPalisadesRidge = true;
        public bool buildSignatureTowers = true;
        public bool buildPiers = true;
        public bool buildBridges = true;
        public bool buildHarborLandmarks = true;

        [Header("Materials")]
        public Material landMaterial;
        public Material bulkheadMaterial;
        public Material palisadesMaterial;
        public Material boroughBuildingMaterial;
        public Material bridgeSteelMaterial;
        public Material bridgeStoneMaterial;
        public Material pierDeckMaterial;
        public Material pierPileMaterial;
        public Material statueCopperMaterial;

        public Transform BoroughsRoot { get; private set; }
        public readonly List<CollisionBox> CollisionBoxes = new List<CollisionBox>(128);

        const float LandBottomY = -3.6f;
        const float WaterLevelY = -2.2f;

        public GameObject Build(NycGeoData geo = null, Transform parent = null)
        {
            if (geo == null) geo = CityConfig.Geo;
            EnsureMaterials();

            var rootGo = new GameObject("BoroughsAndHarbor");
            rootGo.hideFlags = HideFlags.DontSave;
            if (parent != null) rootGo.transform.SetParent(parent, false);
            else rootGo.transform.SetParent(transform, false);
            BoroughsRoot = rootGo.transform;
            CollisionBoxes.Clear();

            if (buildLandMasses) BuildLandMasses(geo, BoroughsRoot);
            if (buildPalisadesRidge) BuildPalisadesRidge(BoroughsRoot);
            if (buildPiers) BuildPiers(geo, BoroughsRoot);
            if (buildSignatureTowers) BuildSignatureTowers(BoroughsRoot);
            if (buildBridges) BuildBridges(geo, BoroughsRoot);
            if (buildHarborLandmarks) BuildHarborLandmarks(BoroughsRoot);

            return rootGo;
        }

        // =========================================================================
        // 1. LAND MASSES & BULKHEADS (NJ, Brooklyn, Queens, Bronx, SI, Islands)
        // =========================================================================
        void BuildLandMasses(NycGeoData geo, Transform parent)
        {
            var landGo = new GameObject("BoroughLandMasses");
            landGo.transform.SetParent(parent, false);

            var topVerts = new List<Vector3>(16384);
            var topNorms = new List<Vector3>(16384);
            var topUvs   = new List<Vector2>(16384);
            var topColors = new List<Color32>(16384);
            var topTris  = new List<int>(32768);

            var wallVerts = new List<Vector3>(8192);
            var wallNorms = new List<Vector3>(8192);
            var wallUvs   = new List<Vector2>(8192);
            var wallColors = new List<Color32>(8192);
            var wallTris  = new List<int>(16384);

            // Collect all land polygons from geo
            var lands = new List<(string name, Vector2[] pts, float topY)>();

            if (geo.Boroughs.TryGetValue("newJersey", out var njRings) && njRings.Length > 0)
                lands.Add(("NJ", njRings[0], 0.0f));

            if (geo.Boroughs.TryGetValue("brooklyn", out var bkRings))
                foreach (var r in bkRings) if (r.Length >= 3) lands.Add(("Brooklyn", r, 0.0f));

            if (geo.Boroughs.TryGetValue("queens", out var qnRings))
                foreach (var r in qnRings) if (r.Length >= 3) lands.Add(("Queens", r, 0.0f));

            if (geo.Boroughs.TryGetValue("bronx", out var bxRings))
                foreach (var r in bxRings) if (r.Length >= 3) lands.Add(("Bronx", r, 0.0f));

            if (geo.Boroughs.TryGetValue("statenIsland", out var siRings))
                foreach (var r in siRings) if (r.Length >= 3) lands.Add(("StatenIsland", r, 0.0f));

            if (geo.UpperManhattan != null && geo.UpperManhattan.Length >= 3)
                lands.Add(("UpperManhattan", geo.UpperManhattan, -0.02f));

            // Harbor Islands
            if (geo.Islands.TryGetValue("roosevelt", out var roos)) lands.Add(("RooseveltIsland", roos, 0.3f));
            if (geo.Islands.TryGetValue("randalls", out var rand)) lands.Add(("RandallsIsland", rand, 0.35f));
            if (geo.Islands.TryGetValue("governors", out var gov)) lands.Add(("GovernorsIsland", gov, 0.45f));
            if (geo.Islands.TryGetValue("liberty", out var lib)) lands.Add(("LibertyIsland", lib, 0.6f));
            if (geo.Islands.TryGetValue("ellis", out var ell)) lands.Add(("EllisIsland", ell, 0.6f));
            if (geo.Islands.TryGetValue("millRock", out var mill)) lands.Add(("MillRock", mill, 0.3f));

            foreach (var (name, pts, topY) in lands)
            {
                if (pts == null || pts.Length < 3) continue;

                // The generated New Jersey outline uses ±60000 sentinel points to
                // close the map at the horizon. They are rasterization markers, not
                // world coordinates; triangulating them creates a 120 km rectangle.
                var trimmedPts = TrimHorizonSentinels(pts);
                if (trimmedPts.Length < 3) continue;

                // Triangulate top surface
                var verts2D = new List<Vector2>();
                var tris = new List<int>();
                PolygonTriangulator.Triangulate(trimmedPts, null, verts2D, tris);

                int baseTop = topVerts.Count;
                for (int i = 0; i < verts2D.Count; i++)
                {
                    Vector2 p = verts2D[i];
                    topVerts.Add(CityFrame.ToUnity(p.x, topY, p.y));
                    topNorms.Add(Vector3.up);
                    topUvs.Add(new Vector2(p.x * 0.01f, p.y * 0.01f));
                    topColors.Add(Color.white);
                }
                for (int i = 0; i < tris.Count; i += 3)
                {
                    topTris.Add(baseTop + tris[i]);
                    topTris.Add(baseTop + tris[i + 1]);
                    topTris.Add(baseTop + tris[i + 2]);
                }

                // Vertical Bulkhead Quads along shoreline
                for (int i = 0; i < pts.Length; i++)
                {
                    Vector2 a = pts[i];
                    Vector2 b = pts[(i + 1) % pts.Length];
                    float dx = b.x - a.x;
                    float dz = b.y - a.y;
                    float len = Mathf.Sqrt(dx * dx + dz * dz);
                    if (len < 0.1f || len > 2500f) continue; // skip artificial closure lines to horizon

                    Vector3 aTop = CityFrame.ToUnity(a.x, topY, a.y);
                    Vector3 aBot = CityFrame.ToUnity(a.x, LandBottomY, a.y);
                    Vector3 bTop = CityFrame.ToUnity(b.x, topY, b.y);
                    Vector3 bBot = CityFrame.ToUnity(b.x, LandBottomY, b.y);

                    Vector3 n = Vector3.Cross(bBot - aBot, aTop - aBot).normalized;

                    int wBase = wallVerts.Count;
                    wallVerts.Add(aTop); wallNorms.Add(n); wallUvs.Add(new Vector2(0, 1)); wallColors.Add(Color.white);
                    wallVerts.Add(aBot); wallNorms.Add(n); wallUvs.Add(new Vector2(0, 0)); wallColors.Add(Color.white);
                    wallVerts.Add(bBot); wallNorms.Add(n); wallUvs.Add(new Vector2(1, 0)); wallColors.Add(Color.white);
                    wallVerts.Add(bTop); wallNorms.Add(n); wallUvs.Add(new Vector2(1, 1)); wallColors.Add(Color.white);

                    wallTris.Add(wBase); wallTris.Add(wBase + 1); wallTris.Add(wBase + 2);
                    wallTris.Add(wBase); wallTris.Add(wBase + 2); wallTris.Add(wBase + 3);
                }
            }

            // Create Land Top GameObject
            if (topVerts.Count > 0)
            {
                var topMesh = new Mesh { name = "BoroughsLandTop" };
                if (topVerts.Count > 65000) topMesh.indexFormat = IndexFormat.UInt32;
                topMesh.SetVertices(topVerts);
                topMesh.SetNormals(topNorms);
                SetAllUVs(topMesh, topUvs, topVerts.Count);
                topMesh.SetColors(topColors);
                topMesh.SetTriangles(topTris, 0);
                topMesh.RecalculateBounds();

                var mf = landGo.AddComponent<MeshFilter>();
                mf.sharedMesh = topMesh;
                var mr = landGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = landMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }

            // Create Bulkhead Wall GameObject
            if (wallVerts.Count > 0)
            {
                var wallGo = new GameObject("BoroughBulkheads");
                wallGo.transform.SetParent(landGo.transform, false);

                var wallMesh = new Mesh { name = "BoroughBulkheads" };
                if (wallVerts.Count > 65000) wallMesh.indexFormat = IndexFormat.UInt32;
                wallMesh.SetVertices(wallVerts);
                wallMesh.SetNormals(wallNorms);
                SetAllUVs(wallMesh, wallUvs, wallVerts.Count);
                wallMesh.SetColors(wallColors);
                wallMesh.SetTriangles(wallTris, 0);
                wallMesh.RecalculateBounds();

                var mf = wallGo.AddComponent<MeshFilter>();
                mf.sharedMesh = wallMesh;
                var mr = wallGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = bulkheadMaterial;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
        }

        static Vector2[] TrimHorizonSentinels(Vector2[] points)
        {
            int valid = 0;
            for (int i = 0; i < points.Length; i++)
                if (Mathf.Abs(points[i].x) < 50000f && Mathf.Abs(points[i].y) < 50000f) valid++;

            if (valid == points.Length) return points;
            var trimmed = new Vector2[valid];
            int w = 0;
            for (int i = 0; i < points.Length; i++)
                if (Mathf.Abs(points[i].x) < 50000f && Mathf.Abs(points[i].y) < 50000f)
                    trimmed[w++] = points[i];
            return trimmed;
        }

        // =========================================================================
        // 2. PALISADES RIDGE CLIFFS (New Jersey diabase cliffs)
        // =========================================================================
        void BuildPalisadesRidge(Transform parent)
        {
            var ridgeGo = new GameObject("PalisadesRidge");
            ridgeGo.transform.SetParent(parent, false);

            // Sample rows along the Hudson River from Fort Lee down to Jersey City Heights
            var rows = new List<float>();
            for (float z = -20000; z < -14000; z += 200f) rows.Add(z);
            for (float z = -14000; z < 4000; z += 50f) rows.Add(z);
            for (float z = 4000; z <= 12000; z += 150f) rows.Add(z);

            float[] faceFractions = new[] { 0.15f, 0.35f, 0.55f, 0.70f, 0.85f, 1.0f };
            float[] backFractions = new[] { 0.1f, 0.25f, 0.5f, 0.75f, 1.0f };
            int numCols = 2 + faceFractions.Length + backFractions.Length;

            var verts = new List<Vector3>(rows.Count * numCols);
            var norms = new List<Vector3>(rows.Count * numCols);
            var uvs   = new List<Vector2>(rows.Count * numCols);
            var tris  = new List<int>((rows.Count - 1) * (numCols - 1) * 6);

            for (int r = 0; r < rows.Count; r++)
            {
                float z = rows[r];
                GetRidgeParams(z, out float cliffX, out float cliffH, out float cliffW, out float backW);

                var dList = new List<float> { -12f, 0f };
                foreach (float f in faceFractions) dList.Add(f * cliffW);
                foreach (float b in backFractions) dList.Add(cliffW + b * backW);

                for (int c = 0; c < dList.Count; c++)
                {
                    float d = dList[c];
                    float x = cliffX - d;
                    float y = d <= 0 ? (d <= -12 ? -1.5f : -1.5f * (-d / 12f))
                                     : GetRidgeProfile(d, cliffH, cliffW, backW);

                    verts.Add(CityFrame.ToUnity(x, y, z));
                    norms.Add(Vector3.up); // recalculated below
                    uvs.Add(new Vector2(d * 0.02f, z * 0.01f));
                }
            }

            for (int r = 0; r < rows.Count - 1; r++)
            {
                for (int c = 0; c < numCols - 1; c++)
                {
                    int i0 = r * numCols + c;
                    int i1 = i0 + 1;
                    int i2 = (r + 1) * numCols + c;
                    int i3 = i2 + 1;

                    tris.Add(i0); tris.Add(i2); tris.Add(i1);
                    tris.Add(i1); tris.Add(i2); tris.Add(i3);
                }
            }

            if (verts.Count > 0)
            {
                var mesh = new Mesh { name = "PalisadesRidge" };
                if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(verts);
                SetAllUVs(mesh, uvs, verts.Count);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var mf = ridgeGo.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = ridgeGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = palisadesMaterial;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
        }

        static void GetRidgeParams(float z, out float crestX, out float height, out float cliffWidth, out float backReach)
        {
            // Port of RIDGE_KEYS interpolation
            if (z < -14000f)
            {
                crestX = -3200f; height = 130f; cliffWidth = 110f; backReach = 1800f;
            }
            else if (z < -8000f)
            {
                float t = (z + 14000f) / 6000f;
                crestX = Mathf.Lerp(-3200f, -3400f, t);
                height = Mathf.Lerp(130f, 80f, t);
                cliffWidth = Mathf.Lerp(110f, 90f, t);
                backReach = 1600f;
            }
            else if (z < 0f) // Weehawken
            {
                float t = (z + 8000f) / 8000f;
                crestX = Mathf.Lerp(-3400f, -3800f, t);
                height = Mathf.Lerp(80f, 55f, t);
                cliffWidth = Mathf.Lerp(90f, 80f, t);
                backReach = 1400f;
            }
            else if (z < 6000f) // Jersey City Heights
            {
                float t = z / 6000f;
                crestX = Mathf.Lerp(-3800f, -4700f, t);
                height = Mathf.Lerp(55f, 38f, t);
                cliffWidth = Mathf.Lerp(80f, 180f, t);
                backReach = 1200f;
            }
            else // Bayonne
            {
                float t = Mathf.Clamp01((z - 6000f) / 6000f);
                crestX = Mathf.Lerp(-4700f, -5100f, t);
                height = Mathf.Lerp(38f, 10f, t);
                cliffWidth = Mathf.Lerp(180f, 260f, t);
                backReach = 800f;
            }
        }

        static float Smooth01(float t)
        {
            float x = Mathf.Clamp01(t);
            return x * x * (3f - 2f * x);
        }

        static float GetRidgeProfile(float d, float h, float cw, float back)
        {
            if (d < cw)
            {
                float t = d / cw;
                float talus = 0.4f * Smooth01(t / 0.62f);
                float face = 0.56f * Smooth01((t - 0.55f) / 0.4f);
                return h * (talus + face);
            }
            float b = d - cw;
            if (b >= back) return -1.5f;
            float crest = 0.96f + 0.04f * Smooth01(b / 30f);
            float fall = 1.0f - Smooth01(b / back);
            return h * crest * fall;
        }

        // =========================================================================
        // 3. WATERFRONT PIERS (Piers from osm_piers.json / geo.Piers)
        // =========================================================================
        void BuildPiers(NycGeoData geo, Transform parent)
        {
            if (geo.Piers == null || geo.Piers.Count == 0) return;

            var piersGo = new GameObject("WaterfrontPiers");
            piersGo.transform.SetParent(parent, false);

            var deckVerts = new List<Vector3>(8192);
            var deckNorms = new List<Vector3>(8192);
            var deckUvs   = new List<Vector2>(8192);
            var deckTris  = new List<int>(16384);

            var pileVerts = new List<Vector3>(8192);
            var pileNorms = new List<Vector3>(8192);
            var pileUvs   = new List<Vector2>(8192);
            var pileTris  = new List<int>(16384);

            foreach (var pier in geo.Piers)
            {
                if (pier.Len < 10f || pier.Wid < 4f) continue;

                float cx = pier.Cx, cz = pier.Cz;
                float hl = pier.Len * 0.5f, hw = pier.Wid * 0.5f;
                float ang = pier.Ang;
                float cs = Mathf.Cos(ang), sn = Mathf.Sin(ang);

                // 4 corners of the pier deck in world space (y = 0.6m)
                Vector2 c0 = RotatePoint(-hl, -hw, cs, sn, cx, cz);
                Vector2 c1 = RotatePoint(hl, -hw, cs, sn, cx, cz);
                Vector2 c2 = RotatePoint(hl, hw, cs, sn, cx, cz);
                Vector2 c3 = RotatePoint(-hl, hw, cs, sn, cx, cz);

                // Top deck polygon
                int dBase = deckVerts.Count;
                Vector3 p0 = CityFrame.ToUnity(c0.x, 0.6f, c0.y);
                Vector3 p1 = CityFrame.ToUnity(c1.x, 0.6f, c1.y);
                Vector3 p2 = CityFrame.ToUnity(c2.x, 0.6f, c2.y);
                Vector3 p3 = CityFrame.ToUnity(c3.x, 0.6f, c3.y);

                deckVerts.Add(p0); deckNorms.Add(Vector3.up); deckUvs.Add(new Vector2(0, 0));
                deckVerts.Add(p1); deckNorms.Add(Vector3.up); deckUvs.Add(new Vector2(1, 0));
                deckVerts.Add(p2); deckNorms.Add(Vector3.up); deckUvs.Add(new Vector2(1, 1));
                deckVerts.Add(p3); deckNorms.Add(Vector3.up); deckUvs.Add(new Vector2(0, 1));

                deckTris.Add(dBase); deckTris.Add(dBase + 1); deckTris.Add(dBase + 2);
                deckTris.Add(dBase); deckTris.Add(dBase + 2); deckTris.Add(dBase + 3);

                // Fascia edges down to water
                AddPierFascia(deckVerts, deckNorms, deckUvs, deckTris, p0, p1);
                AddPierFascia(deckVerts, deckNorms, deckUvs, deckTris, p1, p2);
                AddPierFascia(deckVerts, deckNorms, deckUvs, deckTris, p2, p3);

                // Timber pilings below the deck
                float stepL = 12f;
                for (float dl = -hl + 4f; dl <= hl - 4f; dl += stepL)
                {
                    for (float dw = -hw + 2f; dw <= hw - 2f; dw += 6f)
                    {
                        Vector2 pos = RotatePoint(dl, dw, cs, sn, cx, cz);
                        AddPiling(pileVerts, pileNorms, pileUvs, pileTris, pos.x, pos.y);
                    }
                }

                // Add collision box for the pier
                float minX = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x));
                float maxX = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x));
                float minZ = Mathf.Min(Mathf.Min(c0.y, c1.y), Mathf.Min(c2.y, c3.y));
                float maxZ = Mathf.Max(Mathf.Max(c0.y, c1.y), Mathf.Max(c2.y, c3.y));
                CollisionBoxes.Add(new CollisionBox
                {
                    MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ, MaxY = 1.2f
                });
            }

            if (deckVerts.Count > 0)
            {
                var dMesh = new Mesh { name = "PierDecks" };
                if (deckVerts.Count > 65000) dMesh.indexFormat = IndexFormat.UInt32;
                dMesh.SetVertices(deckVerts);
                dMesh.SetNormals(deckNorms);
                SetAllUVs(dMesh, deckUvs, deckVerts.Count);
                dMesh.SetTriangles(deckTris, 0);
                dMesh.RecalculateBounds();

                var mf = piersGo.AddComponent<MeshFilter>();
                mf.sharedMesh = dMesh;
                var mr = piersGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = pierDeckMaterial;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }

            if (pileVerts.Count > 0)
            {
                var pGo = new GameObject("PierPilings");
                pGo.transform.SetParent(piersGo.transform, false);

                var pMesh = new Mesh { name = "PierPilings" };
                if (pileVerts.Count > 65000) pMesh.indexFormat = IndexFormat.UInt32;
                pMesh.SetVertices(pileVerts);
                pMesh.SetNormals(pileNorms);
                SetAllUVs(pMesh, pileUvs, pileVerts.Count);
                pMesh.SetTriangles(pileTris, 0);
                pMesh.RecalculateBounds();

                var mf = pGo.AddComponent<MeshFilter>();
                mf.sharedMesh = pMesh;
                var mr = pGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = pierPileMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }
        }

        static Vector2 RotatePoint(float lx, float lz, float cs, float sn, float cx, float cz)
        {
            return new Vector2(cx + lx * cs - lz * sn, cz + lx * sn + lz * cs);
        }

        static void AddPierFascia(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                                  Vector3 aTop, Vector3 bTop)
        {
            Vector3 aBot = new Vector3(aTop.x, WaterLevelY, aTop.z);
            Vector3 bBot = new Vector3(bTop.x, WaterLevelY, bTop.z);
            Vector3 n = Vector3.Cross(bBot - aBot, aTop - aBot).normalized;

            int b = verts.Count;
            verts.Add(aTop); norms.Add(n); uvs.Add(new Vector2(0, 1));
            verts.Add(aBot); norms.Add(n); uvs.Add(new Vector2(0, 0));
            verts.Add(bBot); norms.Add(n); uvs.Add(new Vector2(1, 0));
            verts.Add(bTop); norms.Add(n); uvs.Add(new Vector2(1, 1));

            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
        }

        static void AddPiling(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                              float wx, float wz)
        {
            float r = 0.45f;
            Vector3 pTop = CityFrame.ToUnity(wx, 0.4f, wz);
            Vector3 pBot = CityFrame.ToUnity(wx, LandBottomY, wz);

            int b = verts.Count;
            // 4-sided timber pile
            Vector3[] cTop = new[]
            {
                pTop + new Vector3(-r, 0, -r), pTop + new Vector3(r, 0, -r),
                pTop + new Vector3(r, 0, r), pTop + new Vector3(-r, 0, r)
            };
            Vector3[] cBot = new[]
            {
                pBot + new Vector3(-r, 0, -r), pBot + new Vector3(r, 0, -r),
                pBot + new Vector3(r, 0, r), pBot + new Vector3(-r, 0, r)
            };

            for (int i = 0; i < 4; i++)
            {
                int next = (i + 1) % 4;
                int cur = verts.Count;
                Vector3 n = Vector3.Cross(cBot[next] - cBot[i], cTop[i] - cBot[i]).normalized;
                verts.Add(cTop[i]); norms.Add(n); uvs.Add(new Vector2(0, 1));
                verts.Add(cBot[i]); norms.Add(n); uvs.Add(new Vector2(0, 0));
                verts.Add(cBot[next]); norms.Add(n); uvs.Add(new Vector2(1, 0));
                verts.Add(cTop[next]); norms.Add(n); uvs.Add(new Vector2(1, 1));

                tris.Add(cur); tris.Add(cur + 1); tris.Add(cur + 2);
                tris.Add(cur); tris.Add(cur + 2); tris.Add(cur + 3);
            }
        }

        // =========================================================================
        // 4. SIGNATURE BOROUGH TOWERS & SKYLINE HIGH-RISES
        // =========================================================================
        void BuildSignatureTowers(Transform parent)
        {
            var towersGo = new GameObject("BoroughSignatureTowers");
            towersGo.transform.SetParent(parent, false);

            var verts = new List<Vector3>(8192);
            var norms = new List<Vector3>(8192);
            var uvs   = new List<Vector2>(8192);
            var tris  = new List<int>(16384);

            // Landmark towers around Manhattan (converted from lon/lat in boroughs.js)
            var towers = new[]
            {
                // Jersey City
                new { name = "99 Hudson", lon = -74.0347f, lat = 40.7150f, w = 30f, d = 26f, h = 270f },
                new { name = "Goldman Sachs", lon = -74.0337f, lat = 40.7144f, w = 40f, d = 30f, h = 238f },
                new { name = "Urby JC", lon = -74.0348f, lat = 40.7182f, w = 28f, d = 24f, h = 213f },
                new { name = "Trump Plaza JC", lon = -74.0370f, lat = 40.7198f, w = 28f, d = 22f, h = 163f },
                new { name = "Newport Tower", lon = -74.0338f, lat = 40.7268f, w = 40f, d = 32f, h = 161f },
                new { name = "Journal Square", lon = -74.0632f, lat = 40.7322f, w = 32f, d = 26f, h = 215f },
                // Long Island City
                new { name = "Skyline Tower", lon = -73.9446f, lat = 40.7480f, w = 30f, d = 30f, h = 237f },
                new { name = "One Court Square", lon = -73.9430f, lat = 40.7471f, w = 42f, d = 36f, h = 205f },
                new { name = "Queens Plaza Park", lon = -73.9392f, lat = 40.7509f, w = 30f, d = 26f, h = 228f },
                // Downtown Brooklyn
                new { name = "The Brooklyn Tower", lon = -73.9836f, lat = 40.6904f, w = 34f, d = 34f, h = 325f },
                new { name = "Brooklyn Point", lon = -73.9822f, lat = 40.6913f, w = 28f, d = 28f, h = 219f },
                new { name = "The Hub", lon = -73.9805f, lat = 40.6871f, w = 30f, d = 28f, h = 230f },
                new { name = "Williamsburgh Savings Bank", lon = -73.9775f, lat = 40.6855f, w = 26f, d = 26f, h = 156f },
                // Fort Lee & Palisades
                new { name = "Fort Lee North", lon = -73.9712f, lat = 40.8512f, w = 26f, d = 44f, h = 140f },
                new { name = "Fort Lee South", lon = -73.9722f, lat = 40.8500f, w = 26f, d = 44f, h = 140f },
                new { name = "Galaxy Tower 1", lon = -74.0042f, lat = 40.7906f, w = 34f, d = 34f, h = 125f },
                new { name = "Galaxy Tower 2", lon = -74.0036f, lat = 40.7914f, w = 34f, d = 34f, h = 125f },
                new { name = "Galaxy Tower 3", lon = -74.0028f, lat = 40.7921f, w = 34f, d = 34f, h = 125f }
            };

            foreach (var tow in towers)
            {
                Vector2 pos = LonLatToWorld(tow.lon, tow.lat);
                AddBoroughTower(verts, norms, uvs, tris, pos.x, pos.y, tow.w, tow.d, tow.h);
            }

            if (verts.Count > 0)
            {
                var mesh = new Mesh { name = "BoroughSignatureTowers" };
                if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                SetAllUVs(mesh, uvs, verts.Count);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();

                var mf = towersGo.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = towersGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = boroughBuildingMaterial;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
        }

        static Vector2 LonLatToWorld(float lon, float lat)
        {
            float dl = lon + 73.98185f;
            float dp = lat - 40.75318f;
            return new Vector2(-54.351f + 73845.537f * dl - 53872.251f * dp,
                               69.424f - 40967.61f * dl - 97106.601f * dp);
        }

        static void AddBoroughTower(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                                    float cx, float cz, float w, float d, float h)
        {
            float hx = w * 0.5f, hz = d * 0.5f;
            Vector3 center = CityFrame.ToUnity(cx, h * 0.5f, cz);
            Vector3 size = new Vector3(w, h, d);

            Vector3[] c = new[]
            {
                center + new Vector3(-hx, -h * 0.5f, -hz),
                center + new Vector3(hx, -h * 0.5f, -hz),
                center + new Vector3(hx, -h * 0.5f, hz),
                center + new Vector3(-hx, -h * 0.5f, hz),
                center + new Vector3(-hx, h * 0.5f, -hz),
                center + new Vector3(hx, h * 0.5f, -hz),
                center + new Vector3(hx, h * 0.5f, hz),
                center + new Vector3(-hx, h * 0.5f, hz)
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
                    uv0To(uvs, v);
                }
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
        }

        static void uv0To(List<Vector2> uvs, int v)
        {
            uvs.Add(new Vector2((v == 1 || v == 2) ? 1f : 0f, (v >= 2) ? 1f : 0f));
        }

        // =========================================================================
        // 5. BRIDGES (East River Bridges & Outer Bridges)
        // =========================================================================
        void BuildBridges(NycGeoData geo, Transform parent)
        {
            var bridgesGo = new GameObject("IconicBridges");
            bridgesGo.transform.SetParent(parent, false);

            var steelVerts = new List<Vector3>(8192);
            var steelNorms = new List<Vector3>(8192);
            var steelUvs   = new List<Vector2>(8192);
            var steelTris  = new List<int>(16384);

            var stoneVerts = new List<Vector3>(4096);
            var stoneNorms = new List<Vector3>(4096);
            var stoneUvs   = new List<Vector2>(4096);
            var stoneTris  = new List<int>(8192);

            // Brooklyn Bridge (Gothic Stone Towers & Suspended Spans)
            BuildSuspensionBridge(stoneVerts, stoneNorms, stoneUvs, stoneTris,
                                  steelVerts, steelNorms, steelUvs, steelTris,
                                  start: new Vector2(408f, 4976f), end: new Vector2(1637f, 5322f),
                                  towerH: 84f, deckW: 26f, stoneTowers: true);

            // Manhattan Bridge (Blue Steel Towers)
            BuildSuspensionBridge(steelVerts, steelNorms, steelUvs, steelTris,
                                  steelVerts, steelNorms, steelUvs, steelTris,
                                  start: new Vector2(903f, 4217f), end: new Vector2(1979f, 5042f),
                                  towerH: 102f, deckW: 36f, stoneTowers: false);

            // Williamsburg Bridge (Lattice Truss)
            BuildSuspensionBridge(steelVerts, steelNorms, steelUvs, steelTris,
                                  steelVerts, steelNorms, steelUvs, steelTris,
                                  start: new Vector2(1565f, 3666f), end: new Vector2(3092f, 3487f),
                                  towerH: 94f, deckW: 36f, stoneTowers: false);

            // Queensboro Bridge (Cantilever Truss spanning over Roosevelt Island)
            BuildCantileverBridge(steelVerts, steelNorms, steelUvs, steelTris,
                                  stoneVerts, stoneNorms, stoneUvs, stoneTris,
                                  start: new Vector2(911f, -1425f), end: new Vector2(2125f, -1401f),
                                  midRoosevelt: new Vector2(1480f, -1413f));

            // Distant skyline bridges use the same suspension vocabulary as
            // the source: two towers, a deck, catenary segments and hangers.
            // Their real anchor points come from GEO.points, so this remains
            // data-driven when the OSM extract is regenerated.
            if (geo?.Points != null &&
                geo.Points.TryGetValue("gwbManhattanTower", out var gwbStart) &&
                geo.Points.TryGetValue("gwbNJTower", out var gwbEnd))
            {
                BuildSuspensionBridge(steelVerts, steelNorms, steelUvs, steelTris,
                                      steelVerts, steelNorms, steelUvs, steelTris,
                                      gwbStart, gwbEnd, towerH: 184f, deckW: 36f, stoneTowers: false);
            }
            if (geo?.Points != null &&
                geo.Points.TryGetValue("verrazzanoStatenTower", out var vzStart) &&
                geo.Points.TryGetValue("verrazzanoBrooklynTower", out var vzEnd))
            {
                BuildSuspensionBridge(steelVerts, steelNorms, steelUvs, steelTris,
                                      steelVerts, steelNorms, steelUvs, steelTris,
                                      vzStart, vzEnd, towerH: 211f, deckW: 31f, stoneTowers: false);
            }

            // The render meshes are intentionally lightweight, but their
            // approach slabs and tower caissons still belong to the shared
            // collision world.  Keep the bridge boxes in the source frame and
            // use the same oriented-box convention as buildings.js.
            AddBridgeColliders(new Vector2(408f, 4976f), new Vector2(1637f, 5322f), 26f, 4.2f,
                               towerFractions: new[] { 0.33f, 0.67f });
            AddBridgeColliders(new Vector2(903f, 4217f), new Vector2(1979f, 5042f), 36f, 4.2f,
                               towerFractions: new[] { 0.33f, 0.67f });
            AddBridgeColliders(new Vector2(1565f, 3666f), new Vector2(3092f, 3487f), 36f, 4.2f,
                               towerFractions: new[] { 0.33f, 0.67f });
            AddBridgeColliders(new Vector2(911f, -1425f), new Vector2(2125f, -1401f), 27f, 4.2f,
                               towerFractions: new[] { 0.16f, 0.34f, 0.51f, 0.84f });

            // Outer bridge anchors are present in GEO.points even though they
            // are skyline geometry. Register their Manhattan approach boxes so
            // teleports and helicopter/vehicle collision queries see the same
            // obstruction as the source scene.
            if (geo?.Points != null &&
                geo.Points.TryGetValue("gwbManhattanTower", out var gwbA) &&
                geo.Points.TryGetValue("gwbNJTower", out var gwbB))
                AddBridgeColliders(gwbA, gwbB, 36f, 4.2f, new[] { 0.0f, 1.0f });
            if (geo?.Points != null &&
                geo.Points.TryGetValue("verrazzanoStatenTower", out var vzA) &&
                geo.Points.TryGetValue("verrazzanoBrooklynTower", out var vzB))
                AddBridgeColliders(vzA, vzB, 31f, 4.2f, new[] { 0.0f, 1.0f });

            if (steelVerts.Count > 0)
            {
                var sGo = new GameObject("BridgeSteel");
                sGo.transform.SetParent(bridgesGo.transform, false);
                var sMesh = new Mesh { name = "BridgeSteel" };
                if (steelVerts.Count > 65000) sMesh.indexFormat = IndexFormat.UInt32;
                sMesh.SetVertices(steelVerts);
                sMesh.SetNormals(steelNorms);
                SetAllUVs(sMesh, steelUvs, steelVerts.Count);
                sMesh.SetTriangles(steelTris, 0);
                sMesh.RecalculateBounds();

                var mf = sGo.AddComponent<MeshFilter>();
                mf.sharedMesh = sMesh;
                var mr = sGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = bridgeSteelMaterial;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }

            if (stoneVerts.Count > 0)
            {
                var stGo = new GameObject("BridgeMasonry");
                stGo.transform.SetParent(bridgesGo.transform, false);
                var stMesh = new Mesh { name = "BridgeMasonry" };
                if (stoneVerts.Count > 65000) stMesh.indexFormat = IndexFormat.UInt32;
                stMesh.SetVertices(stoneVerts);
                stMesh.SetNormals(stoneNorms);
                SetAllUVs(stMesh, stoneUvs, stoneVerts.Count);
                stMesh.SetTriangles(stoneTris, 0);
                stMesh.RecalculateBounds();

                var mf = stGo.AddComponent<MeshFilter>();
                mf.sharedMesh = stMesh;
                var mr = stGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = bridgeStoneMaterial;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
        }

        void AddBridgeColliders(Vector2 start, Vector2 end, float width, float maxY, float[] towerFractions)
        {
            Vector2 delta = end - start;
            float length = delta.magnitude;
            if (length < 1f) return;
            Vector2 dir = delta / length;

            // Only the two shore approaches are low enough to stop a player on
            // the street below.  The suspended middle span remains traversable.
            const float approach = 110f;
            AddOrientedBridgeBox(start + dir * (approach * 0.5f), approach, width, maxY, dir);
            AddOrientedBridgeBox(end - dir * (approach * 0.5f), approach, width, maxY, dir);

            if (towerFractions == null) return;
            for (int i = 0; i < towerFractions.Length; i++)
            {
                float t = Mathf.Clamp01(towerFractions[i]);
                AddOrientedBridgeBox(start + delta * t, 20f, width + 10f, maxY + 110f, dir);
            }
        }

        void AddOrientedBridgeBox(Vector2 center, float along, float across, float maxY, Vector2 dir)
        {
            float cs = dir.x;
            float sn = -dir.y;
            float ex = Mathf.Abs(cs) * along * 0.5f + Mathf.Abs(sn) * across * 0.5f;
            float ez = Mathf.Abs(sn) * along * 0.5f + Mathf.Abs(cs) * across * 0.5f;
            CollisionBoxes.Add(new CollisionBox
            {
                MinX = center.x - ex,
                MaxX = center.x + ex,
                MinZ = center.y - ez,
                MaxZ = center.y + ez,
                MaxY = maxY,
                HasObb = true,
                Cx = center.x,
                Cz = center.y,
                Hx = along * 0.5f,
                Hz = across * 0.5f,
                Cs = cs,
                Sn = sn
            });
        }

        static void BuildSuspensionBridge(List<Vector3> towerV, List<Vector3> towerN, List<Vector2> towerU, List<int> towerT,
                                          List<Vector3> deckV, List<Vector3> deckN, List<Vector2> deckU, List<int> deckT,
                                          Vector2 start, Vector2 end, float towerH, float deckW, bool stoneTowers)
        {
            Vector2 dir = (end - start).normalized;
            Vector2 nrm = new Vector2(-dir.y, dir.x);
            float totalLen = Vector2.Distance(start, end);

            // Two main suspension towers at 1/3 and 2/3 of span
            float t1Dist = totalLen * 0.33f;
            float t2Dist = totalLen * 0.67f;
            Vector2 t1 = start + dir * t1Dist;
            Vector2 t2 = start + dir * t2Dist;

            float deckH = 40f;

            // 1. Massive Shore Anchorages
            AddBoxSimple(towerV, towerN, towerU, towerT, start.x, start.y, 16f, deckW + 12f, 36f, 32f);
            AddBoxSimple(towerV, towerN, towerU, towerT, end.x, end.y, 16f, deckW + 12f, 36f, 32f);

            // 2. Tower Pillars & Gothic / Steel Lattice Portals
            foreach (var tp in new[] { t1, t2 })
            {
                float legOffset = deckW * 0.45f;
                Vector2 l1 = tp + nrm * legOffset;
                Vector2 l2 = tp - nrm * legOffset;

                // Foundation Caissons below water
                AddBoxSimple(towerV, towerN, towerU, towerT, tp.x, tp.y, 2f, deckW + 16f, 22f, 10f);

                // Two soaring vertical legs / columns
                AddBoxSimple(towerV, towerN, towerU, towerT, l1.x, l1.y, towerH * 0.5f, 6.5f, 9.5f, towerH);
                AddBoxSimple(towerV, towerN, towerU, towerT, l2.x, l2.y, towerH * 0.5f, 6.5f, 9.5f, towerH);

                // Lower roadway cross-girder & portals
                AddBoxSimple(towerV, towerN, towerU, towerT, tp.x, tp.y, deckH - 2f, deckW + 4f, 8f, 4.5f);
                // Intermediate portal strut
                AddBoxSimple(towerV, towerN, towerU, towerT, tp.x, tp.y, Mathf.Lerp(deckH, towerH, 0.55f), deckW + 4f, 7f, 4f);
                // Top cross saddle girder
                AddBoxSimple(towerV, towerN, towerU, towerT, tp.x, tp.y, towerH - 2f, deckW + 8f, 10f, 5f);

                if (stoneTowers)
                {
                    // Brooklyn Bridge central Gothic divider pier creating twin pointed archways
                    AddBoxSimple(towerV, towerN, towerU, towerT, tp.x, tp.y, (deckH + towerH) * 0.5f, 4.5f, 8.5f, towerH - deckH);
                }
            }

            // 3. Roadway Deck & Stiffening Trusses
            float segLen = 20f;
            int numSegs = Mathf.CeilToInt(totalLen / segLen);
            for (int i = 0; i < numSegs; i++)
            {
                float tDist = (i + 0.5f) * segLen;
                float tFrac = tDist / totalLen;
                Vector2 p = start + dir * tDist;

                // Roadway floor slab
                AddBoxSimple(deckV, deckN, deckU, deckT, p.x, p.y, deckH, deckW, segLen, 2.5f);

                // Left and right stiffening Warren trusses
                Vector2 sL = p + nrm * (deckW * 0.5f - 0.4f);
                Vector2 sR = p - nrm * (deckW * 0.5f - 0.4f);
                AddBoxSimple(deckV, deckN, deckU, deckT, sL.x, sL.y, deckH + 2.5f, 0.8f, segLen, 5f);
                AddBoxSimple(deckV, deckN, deckU, deckT, sR.x, sR.y, deckH + 2.5f, 0.8f, segLen, 5f);
            }

            // 4. Parabolic Catenary Suspension Cables & Vertical Suspenders
            int cableSegs = Mathf.CeilToInt(totalLen / 12f);
            float saddleY = towerH - 2f;
            float cableR = 0.65f;

            for (int i = 0; i <= cableSegs; i++)
            {
                float dist = (i / (float)cableSegs) * totalLen;
                float cableY;

                if (dist < t1Dist)
                {
                    // Side span from start anchorage (y=18) up to tower 1
                    float u = dist / t1Dist;
                    cableY = Mathf.Lerp(18f, saddleY, Mathf.Pow(u, 1.4f));
                }
                else if (dist > t2Dist)
                {
                    // Side span from tower 2 down to end anchorage (y=18)
                    float u = (dist - t2Dist) / (totalLen - t2Dist);
                    cableY = Mathf.Lerp(saddleY, 18f, Mathf.Pow(u, 0.7f));
                }
                else
                {
                    // Main central span catenary dip
                    float u = (dist - t1Dist) / (t2Dist - t1Dist);
                    float dip = 4f * u * (1f - u); // 0 at towers, 1 at center
                    cableY = saddleY - dip * (saddleY - (deckH + 2.8f));
                }

                Vector2 centerPt = start + dir * dist;

                // 2 pairs of cables (outer left and outer right)
                foreach (float sideSgn in new[] { -1f, 1f })
                {
                    Vector2 cPos = centerPt + nrm * (sideSgn * (deckW * 0.5f + 0.5f));
                    AddBoxSimple(deckV, deckN, deckU, deckT, cPos.x, cPos.y, cableY, cableR * 2f, 12.5f, cableR * 2f);

                    // Vertical suspender wire down to deck level
                    if (cableY > deckH + 3f && dist > 20f && dist < totalLen - 20f)
                    {
                        float dropH = cableY - deckH;
                        AddBoxSimple(deckV, deckN, deckU, deckT, cPos.x, cPos.y, deckH + dropH * 0.5f, 0.22f, 0.22f, dropH);
                    }
                }
            }
        }

        static void BuildCantileverBridge(List<Vector3> steelV, List<Vector3> steelN, List<Vector2> steelU, List<int> steelT,
                                          List<Vector3> stoneV, List<Vector3> stoneN, List<Vector2> stoneU, List<int> stoneT,
                                          Vector2 start, Vector2 end, Vector2 midRoosevelt)
        {
            Vector2 dir = (end - start).normalized;
            Vector2 nrm = new Vector2(-dir.y, dir.x);
            float totalLen = Vector2.Distance(start, end);

            // Queensboro Bridge has 4 massive granite masonry piers and cantilever spires:
            // 1. Manhattan shore (York Ave)
            // 2. Roosevelt Island West shore
            // 3. Roosevelt Island East shore
            // 4. Queens shore (Long Island City)
            Vector2 p1 = start + dir * (totalLen * 0.16f);
            Vector2 p2 = midRoosevelt - dir * 75f;
            Vector2 p3 = midRoosevelt + dir * 75f;
            Vector2 p4 = end - dir * (totalLen * 0.16f);

            var piers = new[] { p1, p2, p3, p4 };
            foreach (var p in piers)
            {
                // Massive rusticated granite anchor pier
                AddBoxSimple(stoneV, stoneN, stoneU, stoneT, p.x, p.y, 16f, 32f, 18f, 32f);

                // Twin vertical cantilever steel truss towers rising to 95m
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector2 col = p + nrm * (s * 13f);
                    AddBoxSimple(steelV, steelN, steelU, steelT, col.x, col.y, 62f, 4.8f, 5.2f, 68f);
                    // Ornate Gothic spire finial atop tower posts
                    AddBoxSimple(steelV, steelN, steelU, steelT, col.x, col.y, 98f, 2.2f, 2.2f, 8f);
                }
                // Tower cross-bracing and portal arch
                AddBoxSimple(steelV, steelN, steelU, steelT, p.x, p.y, 88f, 28f, 4.5f, 4f);
            }

            // Continuous two-level cantilever deck
            float segLen = 25f;
            int numSegs = Mathf.CeilToInt(totalLen / segLen);
            for (int i = 0; i < numSegs; i++)
            {
                float tFrac = (i + 0.5f) / numSegs;
                Vector2 p = start + dir * (totalLen * tFrac);

                // Lower road deck
                AddBoxSimple(steelV, steelN, steelU, steelT, p.x, p.y, 40f, 28f, segLen, 3f);
                // Upper deck
                AddBoxSimple(steelV, steelN, steelU, steelT, p.x, p.y, 48f, 24f, segLen, 2.5f);
                // Side Warren trusses
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector2 tw = p + nrm * (s * 13.5f);
                    AddBoxSimple(steelV, steelN, steelU, steelT, tw.x, tw.y, 44f, 0.9f, segLen, 9f);
                }
            }

            // Roosevelt Island Tramway (running parallel to Queensboro Bridge on its north flank)
            Vector2 tramStart = start + nrm * 26f;
            Vector2 tramEnd = midRoosevelt + nrm * 26f;
            Vector2 tramDir = (tramEnd - tramStart).normalized;
            float tramLen = Vector2.Distance(tramStart, tramEnd);

            // Manhattan station & Roosevelt Island station
            AddBoxSimple(steelV, steelN, steelU, steelT, tramStart.x, tramStart.y, 14f, 14f, 22f, 24f);
            AddBoxSimple(steelV, steelN, steelU, steelT, tramEnd.x, tramEnd.y, 12f, 14f, 22f, 20f);

            // Steel lattice tram towers
            Vector2 tramT1 = tramStart + tramDir * (tramLen * 0.25f);
            Vector2 tramT2 = tramStart + tramDir * (tramLen * 0.70f);
            foreach (var tt in new[] { tramT1, tramT2 })
            {
                AddBoxSimple(steelV, steelN, steelU, steelT, tt.x, tt.y, 45f, 4.5f, 4.5f, 85f);
                AddBoxSimple(steelV, steelN, steelU, steelT, tt.x, tt.y, 86f, 14f, 3.5f, 3.5f);
            }

            // Track ropes
            for (int s = -1; s <= 1; s += 2)
            {
                Vector2 rPos = (tramStart + tramEnd) * 0.5f + nrm * (s * 3.5f);
                AddBoxSimple(steelV, steelN, steelU, steelT, rPos.x, rPos.y, 72f, 0.25f, tramLen, 0.25f);
            }

            // Two iconic bright red tram cabins shuttling across
            Vector2 cabin1Pos = tramStart + tramDir * (tramLen * 0.38f) + nrm * 3.5f;
            Vector2 cabin2Pos = tramStart + tramDir * (tramLen * 0.65f) - nrm * 3.5f;
            foreach (var cp in new[] { cabin1Pos, cabin2Pos })
            {
                // Red cabin body (6.5m long, 3.2m wide, 3.8m high)
                AddBoxSimple(steelV, steelN, steelU, steelT, cp.x, cp.y, 66f, 3.2f, 6.5f, 3.8f);
                // Suspension hanger arm connecting to track rope
                AddBoxSimple(steelV, steelN, steelU, steelT, cp.x, cp.y, 70f, 0.35f, 0.35f, 4.5f);
            }
        }

        static void AddBoxSimple(List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> tris,
                                 float cx, float cz, float cy, float sx, float sz, float sy)
        {
            float hx = sx * 0.5f, hz = sz * 0.5f, hy = sy * 0.5f;
            Vector3 center = CityFrame.ToUnity(cx, cy, cz);

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
                    uv0To(uvs, v);
                }
                tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 3);
            }
        }

        // =========================================================================
        // 6. HARBOR LANDMARKS (Statue of Liberty & Governors Island)
        // =========================================================================
        void BuildHarborLandmarks(Transform parent)
        {
            var harborGo = new GameObject("HarborLandmarks");
            harborGo.transform.SetParent(parent, false);

            var verts = new List<Vector3>(4096);
            var norms = new List<Vector3>(4096);
            var uvs   = new List<Vector2>(4096);
            var tris  = new List<int>(8192);

            // Statue of Liberty (Liberty Island)
            Vector2 solPos = LonLatToWorld(-74.0445f, 40.68925f);
            // 11-pointed star fort base (approximated by stepped octagon)
            AddBoxSimple(verts, norms, uvs, tris, solPos.x, solPos.y, 6f, 60f, 60f, 12f);
            // Classical stone pedestal (47m tall)
            AddBoxSimple(verts, norms, uvs, tris, solPos.x, solPos.y, 25f, 22f, 22f, 28f);
            AddBoxSimple(verts, norms, uvs, tris, solPos.x, solPos.y, 42f, 16f, 16f, 8f);
            // Copper Statue (46m tall)
            AddBoxSimple(verts, norms, uvs, tris, solPos.x, solPos.y, 62f, 9f, 9f, 32f);
            // Torch Arm & Spire
            AddBoxSimple(verts, norms, uvs, tris, solPos.x + 3f, solPos.y, 84f, 2.5f, 2.5f, 16f);

            // Governors Island - Castle Williams circular red sandstone fortress
            Vector2 cwPos = LonLatToWorld(-74.0195f, 40.6922f);
            AddBoxSimple(verts, norms, uvs, tris, cwPos.x, cwPos.y, 7f, 48f, 48f, 14f);

            if (verts.Count > 0)
            {
                var mesh = new Mesh { name = "HarborLandmarks" };
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                SetAllUVs(mesh, uvs, verts.Count);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateBounds();

                var mf = harborGo.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                var mr = harborGo.AddComponent<MeshRenderer>();
                mr.sharedMaterial = statueCopperMaterial;
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
        }

        void EnsureMaterials()
        {
            if (landMaterial == null) landMaterial = MakeMaterial("Borough_Land", new Color(0.42f, 0.44f, 0.40f), 0.05f, 0.0f);
            if (bulkheadMaterial == null) bulkheadMaterial = MakeMaterial("Borough_Bulkhead", new Color(0.50f, 0.48f, 0.45f), 0.15f, 0.0f);
            if (palisadesMaterial == null) palisadesMaterial = MakeMaterial("Borough_Palisades", new Color(0.38f, 0.42f, 0.35f), 0.10f, 0.0f);
            if (boroughBuildingMaterial == null) boroughBuildingMaterial = MakeMaterial("Borough_Building", new Color(0.68f, 0.66f, 0.62f), 0.20f, 0.0f);
            if (bridgeSteelMaterial == null) bridgeSteelMaterial = MakeMaterial("Bridge_Steel", new Color(0.35f, 0.45f, 0.55f), 0.50f, 0.60f);
            if (bridgeStoneMaterial == null) bridgeStoneMaterial = MakeMaterial("Bridge_Stone", new Color(0.60f, 0.58f, 0.54f), 0.15f, 0.0f);
            if (pierDeckMaterial == null) pierDeckMaterial = MakeMaterial("Pier_Deck", new Color(0.58f, 0.56f, 0.52f), 0.10f, 0.0f);
            if (pierPileMaterial == null) pierPileMaterial = MakeMaterial("Pier_Piling", new Color(0.24f, 0.22f, 0.20f), 0.05f, 0.0f);
            if (statueCopperMaterial == null) statueCopperMaterial = MakeMaterial("Statue_Copper", new Color(0.36f, 0.62f, 0.54f), 0.35f, 0.20f);
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

        static Material MakeMaterial(string name, Color color, float smoothness, float metallic)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            return mat;
        }
    }
}
