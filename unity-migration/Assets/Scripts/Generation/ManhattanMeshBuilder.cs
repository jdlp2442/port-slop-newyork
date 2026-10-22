using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Nyc.Core;
using Nyc.Data;

namespace Nyc.Generation
{
    /// <summary>
    /// Builds Manhattan's static render geometry directly from js/nyc-geodata.js, with no
    /// intermediate DCC step. Output is a small, fixed set of meshes:
    ///
    ///   Manhattan/Blocks_&lt;x&gt;_&lt;z&gt;   sub 0 = facade boxes (procedural facade shader)
    ///                                    sub 1 = cornices, bulkheads, water towers
    ///   Manhattan/Ground                sub 0 = land, 1 = sidewalks, 2 = parks, 3 = water
    ///   Manhattan/Roads                 road ribbons
    ///
    /// Facade vertex layout (consumed by Facade.shader, Phase 3):
    ///   uv0 = (metres along the wall from its left corner, world Y)
    ///   uv1 = (wall width in metres, box height in metres)
    ///   uv2 = (wall kind 0..4, building tone 0..1)
    ///   uv3 = (box top Y, facade style 0..5)
    ///   color = (tone, tone, tone, 1)
    ///
    /// Wall kinds match js/tsl-materials.js WALL_KINDS:
    ///   0 shop (storefront ground floor), 1 entry (residential front),
    ///   2 rear (yard / court), 3 lot line (windowless party wall), 4 blank stone.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ManhattanMeshBuilder : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Relative to StreamingAssets. The repository file can be dropped in untouched.")]
        public string geoRelativePath = "nyc/nyc-geodata.js";

        [Header("Scope")]
        public bool buildLand = true;
        public bool buildSidewalks = true;
        public bool buildBuildings = true;
        public bool buildRoads = true;
        public bool buildParks = true;
        public bool buildProps = true;
        public bool buildLandmarks = true;
        public bool buildBoroughs = true;

        [Header("Tuning")]
        [Tooltip("Buildings are bucketed into square tiles of this size for frustum culling.")]
        public float chunkSize = 750f;
        public int seed = 10019;
        [Range(0f, 1.5f)] public float towerChanceScale = 1f;
        [Tooltip("Hard cap on rooftop water towers (each is ~40 triangles).")]
        public int maxWaterTowers = 3000;

        [Header("Materials (URP)")]
        public Material facadeMaterial;
        public Material solidMaterial;
        public Material sidewalkMaterial;
        public Material roadMaterial;
        public Material parkMaterial;
        public Material landMaterial;
        public Material waterMaterial;

        [Header("Stats")]
        public int buildingCount;
        public int boxCount;
        public int vertexCount;
        public int chunkCount;
        public float buildSeconds;

        /// <summary>Static colliders emitted with the massing (consumed by CollisionWorld).</summary>
        public readonly List<CollisionBox> CollisionBoxes = new List<CollisionBox>(40000);

        NycGeoData geo;
        Transform root;
        readonly Dictionary<Vector2Int, MeshBuf> chunks = new Dictionary<Vector2Int, MeshBuf>();
        MeshBuf ground, roads;
        RectXZ[] landmarkReserves;
        int waterTowers;
        Mulberry32 rng;
        bool playing => Application.isPlaying;

        // ==================================================================== public API

        [ContextMenu("Build Manhattan")]
        public void Build()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (!CityConfig.Ready)
            {
                geo = NycGeoData.LoadFromStreamingAssets(geoRelativePath);
                CityConfig.Initialize(geo);
            }
            else geo = CityConfig.Geo;

            Reset();

            landmarkReserves = BuildLandmarkReserves();
            ground = new MeshBuf(4);
            roads  = new MeshBuf(1);

            if (buildLand)      BuildLandMesh();
            if (buildSidewalks) BuildSidewalkMeshes();
            if (buildParks)     BuildParkMeshes();
            if (buildBuildings) BuildBuildingMeshes();
            if (buildRoads)     BuildRoadMeshes();
            BuildWaterPlane();

            FlushGround();
            FlushRoads();
            FlushChunks();

            if (buildLandmarks)
            {
                var lmBuilder = gameObject.GetComponent<LandmarkMeshBuilder>();
                if (lmBuilder == null) lmBuilder = gameObject.AddComponent<LandmarkMeshBuilder>();
                lmBuilder.Build(geo, root);
                CollisionBoxes.AddRange(lmBuilder.CollisionBoxes);
            }

            if (buildBoroughs)
            {
                var bBuilder = gameObject.GetComponent<BoroughsMeshBuilder>();
                if (bBuilder == null) bBuilder = gameObject.AddComponent<BoroughsMeshBuilder>();
                bBuilder.Build(geo, root);
                CollisionBoxes.AddRange(bBuilder.CollisionBoxes);
            }

            vertexCount = 0;
            foreach (var kv in chunks) vertexCount += kv.Value.P.Count;
            if (ground != null) vertexCount += ground.P.Count;
            if (roads != null) vertexCount += roads.P.Count;
            chunkCount = chunks.Count;

            buildSeconds = (float)sw.Elapsed.TotalSeconds;
            Debug.Log($"[Manhattan] {buildingCount} buildings, {boxCount} boxes, {vertexCount:N0} verts, " +
                      $"{chunkCount} chunks, {buildSeconds:F1}s");
        }

        [ContextMenu("Clear")]
        public void Reset()
        {
            if (root)
            {
                if (playing) Destroy(root.gameObject); else DestroyImmediate(root.gameObject);
            }
            root = new GameObject("Manhattan").transform;
            root.SetParent(transform, false);
            chunks.Clear();
            CollisionBoxes.Clear();
            buildingCount = boxCount = waterTowers = 0;
        }

        // ==================================================================== land

        void BuildLandMesh()
        {
            EmitPolygon(ground, 0, geo.Manhattan, null, 0f, new Color32(120, 120, 118, 255), 0.05f);
            if (geo.UpperManhattan != null && geo.UpperManhattan.Length > 2)
                EmitPolygon(ground, 0, geo.UpperManhattan, null, 0f, new Color32(120, 120, 118, 255), 0.05f);
        }

        void BuildWaterPlane()
        {
            Vector4 b = IslandBounds();
            float pad = 22000f;
            float y = CityFrame.WaterLevel;
            Vector3 p0 = CityFrame.ToUnity(b.x - pad, y, b.z - pad);
            Vector3 p1 = CityFrame.ToUnity(b.y + pad, y, b.z - pad);
            Vector3 p2 = CityFrame.ToUnity(b.y + pad, y, b.w + pad);
            Vector3 p3 = CityFrame.ToUnity(b.x - pad, y, b.w + pad);
            const float s = 0.001f; // 1 km per UV unit: the water shader tiles in world space
            var a0 = new Vector2((b.x - pad) * s, (b.z - pad) * s);
            var a1 = new Vector2((b.y + pad) * s, (b.z - pad) * s);
            var a2 = new Vector2((b.y + pad) * s, (b.w + pad) * s);
            var a3 = new Vector2((b.x - pad) * s, (b.w + pad) * s);
            Quad(ground, 3, p0, p1, p2, p3, Vector3.up, a0, a1, a2, a3,
                 Vector2.zero, Vector2.zero, Vector2.zero, new Color32(255, 255, 255, 255));
        }

        Vector4 IslandBounds()
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < geo.Manhattan.Length; i++)
            {
                Vector2 p = geo.Manhattan[i];
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minZ) minZ = p.y;
                if (p.y > maxZ) maxZ = p.y;
            }
            return new Vector4(minX, maxX, minZ, maxZ);
        }

        // ==================================================================== sidewalks

        void BuildSidewalkMeshes()
        {
            for (int i = 0; i < geo.Blocks.Count; i++)
            {
                BlockRec blk = geo.Blocks[i];
                if (blk.Poly == null || blk.Poly.Length < 3) continue;
                EmitPolygon(ground, 1, blk.Poly, null, CityFrame.SidewalkHeight, new Color32(196, 192, 184, 255), 0.25f);
                EmitCurbSides(blk.Poly, CityFrame.SidewalkHeight);
            }
        }

        void EmitCurbSides(Vector2[] poly, float height)
        {
            Vector2 c = PolygonCentroid(poly);
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i];
                float dx = b.x - a.x, dz = b.y - a.y;
                float len = Mathf.Sqrt(dx * dx + dz * dz);
                if (len < 0.05f) continue;

                var outward = new Vector2(dz / len, -dx / len);
                Vector2 mid = (a + b) * 0.5f;
                if (Vector2.Dot(outward, mid - c) < 0f) outward = -outward;

                Vector3 p0 = CityFrame.ToUnity(a.x, 0f, a.y);
                Vector3 p1 = CityFrame.ToUnity(b.x, 0f, b.y);
                Vector3 p2 = CityFrame.ToUnity(b.x, height, b.y);
                Vector3 p3 = CityFrame.ToUnity(a.x, height, a.y);
                Vector3 n = new Vector3(outward.x, 0f, -outward.y);

                Quad(ground, 1, p0, p1, p2, p3, n,
                     Vector2.zero, new Vector2(len, 0f), new Vector2(len, height), new Vector2(0f, height),
                     Vector2.zero, Vector2.zero, Vector2.zero, new Color32(150, 147, 141, 255));
            }
        }

        // ==================================================================== parks

        void BuildParkMeshes()
        {
            // GEO.parks[].rings are *disjoint parts* of one property (NYC Parks properties can be
            // several parcels), not outer + holes. Each ring is filled on its own.
            for (int i = 0; i < geo.Parks.Count; i++)
            {
                ParkRec p = geo.Parks[i];
                if (p.Rings == null) continue;
                for (int r = 0; r < p.Rings.Length; r++)
                {
                    if (p.Rings[r] == null || p.Rings[r].Length < 3) continue;
                    EmitPolygon(ground, 2, p.Rings[r], null, CityFrame.ParkSurfaceY,
                                new Color32(104, 132, 74, 255), 0.12f);
                }
            }

            if (geo.CentralPark == null) return;

            // Lawns are brighter, water is a separate submesh drawn just above the grass.
            var cp = geo.CentralPark;
            if (cp.Lawns != null)
                foreach (var lawn in cp.Lawns)
                    if (lawn.Rings != null && lawn.Rings.Length > 0 && lawn.Rings[0].Length > 2)
                        EmitPolygon(ground, 2, lawn.Rings[0], null, CityFrame.ParkSurfaceY + 0.005f,
                                    new Color32(118, 150, 82, 255), 0.12f);

            if (cp.Water != null)
                foreach (var w in cp.Water)
                    if (w.Rings != null && w.Rings.Length > 0 && w.Rings[0].Length > 2)
                        EmitPolygon(ground, 3, w.Rings[0], null, CityFrame.ParkSurfaceY + 0.01f,
                                    new Color32(46, 74, 86, 255), 0.05f);
        }

        // ==================================================================== roads

        void BuildRoadMeshes()
        {
            for (int i = 0; i < geo.Roads.Count; i++)
            {
                RoadRec r = geo.Roads[i];
                if (r.Pts == null || r.Pts.Length < 2) continue;
                float half = Mathf.Max(2.5f, (r.Width > 0f ? r.Width : 9f) * 0.5f);
                EmitRibbon(r.Pts, half, CityFrame.RoadSurfaceY + (i % 7) * 0.0015f);
            }

            // Central Park drives (closed to traffic but visible as asphalt).
            if (geo.CentralPark?.Drives != null)
                foreach (var d in geo.CentralPark.Drives)
                    if (d.Pts != null && d.Pts.Length > 1)
                        EmitRibbon(d.Pts, Mathf.Max(2.5f, d.Width * 0.5f), CityFrame.RoadSurfaceY + 0.02f);
        }

        void EmitRibbon(Vector2[] pts, float halfWidth, float y)
        {
            float travelled = 0f;
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 0.5f) continue;
                d /= len;
                var n = new Vector2(d.y, -d.x) * halfWidth;

                Vector3 p0 = CityFrame.ToUnity(a.x + n.x, y, a.y + n.y);
                Vector3 p1 = CityFrame.ToUnity(b.x + n.x, y, b.y + n.y);
                Vector3 p2 = CityFrame.ToUnity(b.x - n.x, y, b.y - n.y);
                Vector3 p3 = CityFrame.ToUnity(a.x - n.x, y, a.y - n.y);

                Quad(roads, 0, p0, p1, p2, p3, Vector3.up,
                     new Vector2(0f, travelled), new Vector2(0f, travelled + len),
                     new Vector2(halfWidth * 2f, travelled + len), new Vector2(halfWidth * 2f, travelled),
                     Vector2.zero, Vector2.zero, Vector2.zero, new Color32(58, 58, 60, 255));
                travelled += len;
            }
        }

        // ==================================================================== buildings

        void BuildBuildingMeshes()
        {
            for (int bi = 0; bi < geo.Blocks.Count; bi++)
            {
                BlockRec blk = geo.Blocks[bi];
                if (blk.Park > 0.6f || blk.Median) continue;
                if (blk.Lots == null || blk.Lots.Length == 0) continue;

                string district = CityConfig.DistrictAt(blk.Center.x, blk.Center.y);
                rng = new Mulberry32(BlockSeed(blk.Bounds.x, blk.Bounds.z));

                if (blk.Frames != null && blk.Frames.Length > 0)
                {
                    for (int fi = 0; fi < blk.Frames.Length; fi++)
                    {
                        BlockFrame fr = blk.Frames[fi];
                        var frame = new BoxFrame(fr.Theta, fr.Px, fr.Pz);
                        for (int ri = 0; ri < fr.Rects.Length; ri++)
                            PopulateLot(frame, blk, fr.Rects[ri], district, true);
                    }
                }
                else
                {
                    var frame = BoxFrame.Identity;
                    for (int li = 0; li < blk.Lots.Length; li++)
                        PopulateLot(frame, blk, blk.Lots[li], district, false);
                }
            }
        }

        static uint BlockSeed(float minX, float minZ)
        {
            unchecked
            {
                return (uint)((Mathf.RoundToInt(minX * 4f) * 73856093) ^
                              (Mathf.RoundToInt(minZ * 4f) * 19349663) ^ 10019);
            }
        }

        void PopulateLot(BoxFrame frame, BlockRec blk, RectXZ lot, string district, bool inFrame)
        {
            if (lot.Width < 6f || lot.Depth < 6f) return;

            // Reserve the landmark footprints: they are modelled separately (Phase 3).
            Vector2 wc = frame.WorldXZ(lot.Center);
            if (InsideLandmark(wc)) return;

            bool[] street = inFrame
                ? new[] { true, true, true, true }
                : new[]
                {
                    lot.X0 - blk.Bounds.x < 0.7f,
                    blk.Bounds.y - lot.X1 < 0.7f,
                    lot.Z0 - blk.Bounds.z < 0.7f,
                    blk.Bounds.w - lot.Z1 < 0.7f
                };

            var profile = DistrictProfile.For(district);

            // Towers rise from the avenue end of a lot; the rest of the lot keeps the row rhythm.
            bool canTower = Mathf.Min(lot.Width, lot.Depth) > 22f;
            if (canTower && rng.NextFloat() < profile.TowerChance * towerChanceScale)
            {
                RectXZ towerRect, rest;
                if (lot.Width >= lot.Depth)
                {
                    float w = Mathf.Min(lot.Width * 0.6f, rng.Range(26f, 44f));
                    towerRect = new RectXZ(lot.X0, lot.X0 + w, lot.Z0, lot.Z1);
                    rest = new RectXZ(lot.X0 + w, lot.X1, lot.Z0, lot.Z1);
                }
                else
                {
                    float d = Mathf.Min(lot.Depth * 0.6f, rng.Range(26f, 44f));
                    towerRect = new RectXZ(lot.X0, lot.X1, lot.Z0, lot.Z0 + d);
                    rest = new RectXZ(lot.X0, lot.X1, lot.Z0 + d, lot.Z1);
                }
                EmitTower(frame, towerRect, street, profile);
                SplitRows(frame, rest, street, profile);
            }
            else
            {
                SplitRows(frame, lot, street, profile);
            }
        }

        void SplitRows(BoxFrame frame, RectXZ lot, bool[] street, DistrictProfile p)
        {
            if (lot.Width < 6f || lot.Depth < 6f) return;

            bool splitX = lot.Width >= lot.Depth;
            float longSide = splitX ? lot.Width : lot.Depth;
            int count = Mathf.Max(1, Mathf.RoundToInt(longSide / rng.Range(9f, 17f)));
            float slice = longSide / count;

            for (int k = 0; k < count; k++)
            {
                RectXZ sub = splitX
                    ? new RectXZ(lot.X0 + k * slice, lot.X0 + (k + 1) * slice, lot.Z0, lot.Z1)
                    : new RectXZ(lot.X0, lot.X1, lot.Z0 + k * slice, lot.Z0 + (k + 1) * slice);

                EmitRow(frame, sub, street, p);
            }
        }

        void EmitRow(BoxFrame frame, RectXZ lot, bool[] street, DistrictProfile p)
        {
            int floors = rng.Range(p.FloorsMin, p.FloorsMax + 1);
            float h = floors * p.Storey;
            float tone = rng.Range(0.15f, 0.9f);
            bool shops = street[0] || street[1];
            Color32 col = new Color32((byte)(tone * 255f), (byte)(p.Style / 5f * 255f), 0, 255);

            EmitBox(frame, lot, 0f, h, street, shops, tone, p.Style, col, true);
            EmitCornice(frame, lot, h, tone);
            if (buildProps) EmitRoofProps(frame, lot, h, tone);
            buildingCount++;
        }

        void EmitTower(BoxFrame frame, RectXZ lot, bool[] street, DistrictProfile p)
        {
            int floors = rng.Range(p.FloorsMin, p.FloorsMax + 1);
            int towerFloors = rng.Range(p.TowerFloorsMin, p.TowerFloorsMax + 1);
            // buildings.js `slender`: a shaft may not rise more than ~9x its narrow side.
            int cap = Mathf.Max(floors + 4, Mathf.FloorToInt(Mathf.Min(lot.Width, lot.Depth) * 9f / p.Storey));
            towerFloors = Mathf.Min(towerFloors, cap);

            float totalH = towerFloors * p.Storey;
            float tone = rng.Range(0.15f, 0.9f);
            bool shops = street[0] || street[1];
            Color32 col = new Color32((byte)(tone * 255f), (byte)(p.Style / 5f * 255f), 0, 255);

            // Choose tower typology: 1916 Zoning Deco Wedding Cake, International Style Slab, or Slender Supertall
            float roll = rng.NextFloat();
            if (p.Style <= 2 || roll < 0.45f)
            {
                // 1916-zoning "wedding cake": 3 or 4 setback tiers with cornices on every ledge
                var tiers = new List<RectXZ> { lot };
                while (tiers.Count < 4)
                {
                    RectXZ prev = tiers[tiers.Count - 1];
                    float insetDist = rng.Range(2.0f, 3.4f);
                    RectXZ next = prev.Inset(insetDist);
                    if (next.Width < Mathf.Max(10f, lot.Width * 0.45f) || next.Depth < Mathf.Max(9f, lot.Depth * 0.45f))
                        break;
                    tiers.Add(next);
                }

                float curY = 0f;
                for (int i = 0; i < tiers.Count; i++)
                {
                    bool isLast = i == tiers.Count - 1;
                    float remainingH = totalH - curY;
                    float tierH = isLast ? remainingH : Mathf.Max(2f * p.Storey, remainingH * (i == 0 ? rng.Range(0.35f, 0.50f) : rng.Range(0.30f, 0.45f)));

                    RectXZ tRect = tiers[i];
                    EmitBox(frame, tRect, curY, curY + tierH, street, (i == 0) && shops, tone, p.Style, col, true);
                    EmitCornice(frame, tRect, curY + tierH, tone);

                    if (isLast && buildProps)
                    {
                        EmitRoofProps(frame, tRect, curY + tierH, tone);
                    }
                    curY += tierH;
                }
            }
            else if (p.Style == 5 && lot.Width >= 22f && lot.Depth >= 22f)
            {
                // Billionaires' Row: Slender supertall on podium with mechanical crown
                float podiumH = rng.Range(3, 6) * p.Storey;
                EmitBox(frame, lot, 0f, podiumH, street, shops, tone, p.Style, col, true);
                EmitCornice(frame, lot, podiumH, tone);

                RectXZ shaft = lot.Inset(rng.Range(2.2f, 3.8f));
                float crownH = rng.Range(4, 7) * p.Storey;
                float shaftH = totalH - podiumH - crownH;

                EmitBox(frame, shaft, podiumH, podiumH + shaftH, street, false, tone, p.Style, col, true);
                EmitCornice(frame, shaft, podiumH + shaftH, tone);

                RectXZ crown = shaft.Inset(rng.Range(1.8f, 2.6f));
                if (crown.Width > 8f && crown.Depth > 8f)
                {
                    EmitBox(frame, crown, podiumH + shaftH, totalH, street, false, tone, p.Style, col, true);
                    EmitCornice(frame, crown, totalH, tone);
                    if (buildProps) EmitRoofProps(frame, crown, totalH, tone);
                }
            }
            else
            {
                // Modern Glass Slab Tower with Plaza / Podium
                float podium = totalH * rng.Range(0.28f, 0.42f);
                EmitBox(frame, lot, 0f, podium, street, shops, tone, p.Style, col, true);
                EmitCornice(frame, lot, podium, tone);

                float inset = rng.Range(2.2f, 4.2f);
                RectXZ shaft = lot.Inset(inset);
                if (shaft.Width > 8f && shaft.Depth > 8f)
                {
                    EmitBox(frame, shaft, podium, totalH, street, false, tone, p.Style, col, true);
                    EmitCornice(frame, shaft, totalH, tone);
                    if (buildProps) EmitRoofProps(frame, shaft, totalH, tone);
                }
            }

            buildingCount++;
        }

        void EmitBox(BoxFrame f, RectXZ r, float y0, float y1, bool[] street, bool shops,
                     float tone, int style, Color32 col, bool facade)
        {
            if (y1 - y0 < 0.5f || r.Width < 0.5f || r.Depth < 0.5f) return;

            int sub = facade ? 0 : 1;
            float h = y1 - y0;
            var uv3 = new Vector2(y1, style);
            MeshBuf b = ChunkFor(f.WorldXZ(r.Center));

            Side(b, sub, f, r, y0, y1, 0, street[0] ? (shops ? WallKind.Shop : WallKind.Entry) : WallKind.Rear, tone, uv3, col);
            Side(b, sub, f, r, y0, y1, 1, street[1] ? (shops ? WallKind.Shop : WallKind.Entry) : WallKind.Rear, tone, uv3, col);
            Side(b, sub, f, r, y0, y1, 2, street[2] ? WallKind.Entry : WallKind.Rear, tone, uv3, col);
            Side(b, sub, f, r, y0, y1, 3, street[3] ? WallKind.Entry : WallKind.Rear, tone, uv3, col);
            Top(b, sub, f, r, y1, tone, uv3, col);

            // Static collider for anything tall enough to block a player (2 m).
            if (y1 > 2.0f)
            {
                if (f.IsIdentity)
                {
                    CollisionBoxes.Add(new CollisionBox
                    {
                        MinX = r.X0, MaxX = r.X1, MinZ = r.Z0, MaxZ = r.Z1, MaxY = y1
                    });
                }
                else
                {
                    Vector2 c0 = f.WorldXZ(new Vector2(r.X0, r.Z0));
                    Vector2 c1 = f.WorldXZ(new Vector2(r.X1, r.Z0));
                    Vector2 c2 = f.WorldXZ(new Vector2(r.X1, r.Z1));
                    Vector2 c3 = f.WorldXZ(new Vector2(r.X0, r.Z1));
                    float minX = Mathf.Min(Mathf.Min(c0.x, c1.x), Mathf.Min(c2.x, c3.x));
                    float maxX = Mathf.Max(Mathf.Max(c0.x, c1.x), Mathf.Max(c2.x, c3.x));
                    float minZ = Mathf.Min(Mathf.Min(c0.y, c1.y), Mathf.Min(c2.y, c3.y));
                    float maxZ = Mathf.Max(Mathf.Max(c0.y, c1.y), Mathf.Max(c2.y, c3.y));
                    Vector2 ctr = f.WorldXZ(r.Center);

                    CollisionBoxes.Add(new CollisionBox
                    {
                        MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ, MaxY = y1,
                        HasObb = true, Cx = ctr.x, Cz = ctr.y,
                        Hx = r.Width * 0.5f, Hz = r.Depth * 0.5f,
                        Cs = f.Cs, Sn = f.Sn
                    });
                }
            }

            boxCount++;
        }

        void Side(MeshBuf b, int sub, BoxFrame f, RectXZ r, float y0, float y1, int side,
                  int kind, float tone, Vector2 uv3, Color32 col)
        {
            float W = side < 2 ? r.Depth : r.Width;
            var uv1 = new Vector2(W, y1 - y0);
            var uv2 = new Vector2(kind, tone);

            Vector3 p0, p1, p2, p3, n;
            switch (side)
            {
                case 0: // -X
                    p0 = f.P(r.X0, y0, r.Z0); p1 = f.P(r.X0, y0, r.Z1);
                    p2 = f.P(r.X0, y1, r.Z1); p3 = f.P(r.X0, y1, r.Z0);
                    n = f.N(-1f, 0f, 0f); break;
                case 1: // +X
                    p0 = f.P(r.X1, y0, r.Z1); p1 = f.P(r.X1, y0, r.Z0);
                    p2 = f.P(r.X1, y1, r.Z0); p3 = f.P(r.X1, y1, r.Z1);
                    n = f.N(1f, 0f, 0f); break;
                case 2: // -Z (source north)
                    p0 = f.P(r.X0, y0, r.Z0); p1 = f.P(r.X1, y0, r.Z0);
                    p2 = f.P(r.X1, y1, r.Z0); p3 = f.P(r.X0, y1, r.Z0);
                    n = f.N(0f, 0f, -1f); break;
                default: // +Z
                    p0 = f.P(r.X1, y0, r.Z1); p1 = f.P(r.X0, y0, r.Z1);
                    p2 = f.P(r.X0, y1, r.Z1); p3 = f.P(r.X1, y1, r.Z1);
                    n = f.N(0f, 0f, 1f); break;
            }

            Quad(b, sub, p0, p1, p2, p3, n,
                 new Vector2(0f, y0), new Vector2(W, y0), new Vector2(W, y1), new Vector2(0f, y1),
                 uv1, uv2, uv3, col);
        }

        void Top(MeshBuf b, int sub, BoxFrame f, RectXZ r, float y, float tone, Vector2 uv3, Color32 col)
        {
            Vector3 p0 = f.P(r.X0, y, r.Z0), p1 = f.P(r.X0, y, r.Z1);
            Vector3 p2 = f.P(r.X1, y, r.Z1), p3 = f.P(r.X1, y, r.Z0);
            Vector3 n = f.N(0f, 1f, 0f);
            Quad(b, sub, p0, p1, p2, p3, n,
                 new Vector2(r.X0, r.Z0), new Vector2(r.X0, r.Z1), new Vector2(r.X1, r.Z1), new Vector2(r.X1, r.Z0),
                 new Vector2(r.Width, 0f), new Vector2(WallKind.BlankStone, tone), uv3, col);
        }

        void EmitCornice(BoxFrame f, RectXZ r, float y, float tone)
        {
            if (!buildProps) return;
            // Tall towers get their coping from the facade shader (cheaper and crisper); only a
            // third of the low-rise stock gets a real projecting stone cornice.
            if (y > 45f || rng.NextFloat() > 0.34f) return;
            var c = r.Inset(-0.6f); // 0.6 m overhang
            EmitSolidBox(f, new RectXZ(c.X0, c.X1, c.Z0, c.Z1), y - 0.9f, y, new Color32(200, 188, 166, 255));
        }

        void EmitRoofProps(BoxFrame f, RectXZ r, float y, float tone)
        {
            // Mechanical bulkhead
            if (rng.NextFloat() < 0.55f && r.Width > 8f && r.Depth > 8f)
            {
                float w = Mathf.Min(r.Width - 2f, rng.Range(2.8f, 5.5f));
                float d = Mathf.Min(r.Depth - 2f, rng.Range(2.8f, 5.5f));
                float x = rng.Range(r.X0 + 1f, r.X1 - 1f - w);
                float z = rng.Range(r.Z0 + 1f, r.Z1 - 1f - d);
                EmitSolidBox(f, new RectXZ(x, x + w, z, z + d), y, y + rng.Range(2.4f, 3.4f),
                             new Color32(124, 130, 138, 255));
            }

            // Cedar water tower
            if (waterTowers < maxWaterTowers && r.Width > 7f && r.Depth > 7f && y > 18f && rng.NextFloat() < 0.38f)
            {
                float cx = rng.Range(r.X0 + 2.2f, r.X1 - 2.2f);
                float cz = rng.Range(r.Z0 + 2.2f, r.Z1 - 2.2f);
                EmitWaterTower(f, cx, cz, y);
                waterTowers++;
            }
        }

        void EmitWaterTower(BoxFrame f, float cx, float cz, float y)
        {
            MeshBuf b = ChunkFor(f.WorldXZ(new Vector2(cx, cz)));
            const int sides = 8;
            const float rad = 1.7f;
            float y0 = y + 2.6f, y1 = y0 + 3.6f;
            Color32 wood = new Color32(126, 90, 62, 255);

            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = f.P(cx + Mathf.Cos(a0) * rad, y0, cz + Mathf.Sin(a0) * rad);
                Vector3 p1 = f.P(cx + Mathf.Cos(a1) * rad, y0, cz + Mathf.Sin(a1) * rad);
                Vector3 p2 = f.P(cx + Mathf.Cos(a1) * rad, y1, cz + Mathf.Sin(a1) * rad);
                Vector3 p3 = f.P(cx + Mathf.Cos(a0) * rad, y1, cz + Mathf.Sin(a0) * rad);
                float am = (a0 + a1) * 0.5f;
                Vector3 n = f.N(Mathf.Cos(am), 0f, Mathf.Sin(am));
                Quad(b, 1, p0, p1, p2, p3, n, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero,
                     Vector2.zero, Vector2.zero, Vector2.zero, wood);
            }

            // Conical roof
            Vector3 apex = f.P(cx, y1 + 1.5f, cz);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides;
                float a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 p0 = f.P(cx + Mathf.Cos(a0) * rad * 1.12f, y1, cz + Mathf.Sin(a0) * rad * 1.12f);
                Vector3 p1 = f.P(cx + Mathf.Cos(a1) * rad * 1.12f, y1, cz + Mathf.Sin(a1) * rad * 1.12f);
                float am = (a0 + a1) * 0.5f;
                Vector3 n = f.N(Mathf.Cos(am) * 0.7f, 0.7f, Mathf.Sin(am) * 0.7f);
                int i0 = b.Add(p0, n, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color32(60, 64, 68, 255));
                int i1 = b.Add(p1, n, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color32(60, 64, 68, 255));
                int i2 = b.Add(apex, n, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, new Color32(60, 64, 68, 255));
                b.Tri(1, i0, i1, i2, n);
            }

            // Four steel legs (a single thin box each, cheap)
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
                float lx = cx + Mathf.Cos(a) * 1.15f;
                float lz = cz + Mathf.Sin(a) * 1.15f;
                EmitSolidBox(f, new RectXZ(lx - 0.12f, lx + 0.12f, lz - 0.12f, lz + 0.12f), y, y0,
                             new Color32(40, 44, 48, 255));
            }
        }

        void EmitSolidBox(BoxFrame f, RectXZ r, float y0, float y1, Color32 col)
        {
            if (y1 - y0 < 0.05f) return;
            MeshBuf b = ChunkFor(f.WorldXZ(r.Center));
            Vector3 n;

            n = f.N(-1f, 0f, 0f);
            Quad(b, 1, f.P(r.X0, y0, r.Z0), f.P(r.X0, y0, r.Z1), f.P(r.X0, y1, r.Z1), f.P(r.X0, y1, r.Z0), n,
                 Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, col);
            n = f.N(1f, 0f, 0f);
            Quad(b, 1, f.P(r.X1, y0, r.Z1), f.P(r.X1, y0, r.Z0), f.P(r.X1, y1, r.Z0), f.P(r.X1, y1, r.Z1), n,
                 Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, col);
            n = f.N(0f, 0f, -1f);
            Quad(b, 1, f.P(r.X0, y0, r.Z0), f.P(r.X1, y0, r.Z0), f.P(r.X1, y1, r.Z0), f.P(r.X0, y1, r.Z0), n,
                 Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, col);
            n = f.N(0f, 0f, 1f);
            Quad(b, 1, f.P(r.X1, y0, r.Z1), f.P(r.X0, y0, r.Z1), f.P(r.X0, y1, r.Z1), f.P(r.X1, y1, r.Z1), n,
                 Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, col);
            n = f.N(0f, 1f, 0f);
            Quad(b, 1, f.P(r.X0, y1, r.Z0), f.P(r.X0, y1, r.Z1), f.P(r.X1, y1, r.Z1), f.P(r.X1, y1, r.Z0), n,
                 Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero, col);
        }

        // ==================================================================== landmarks

        RectXZ[] BuildLandmarkReserves()
        {
            var list = new List<RectXZ>(geo.Landmarks.Count);
            foreach (var kv in geo.Landmarks)
            {
                LandmarkRec l = kv.Value;
                if (l.H < 40f || l.W < 8f || l.D < 8f) continue;
                list.Add(new RectXZ(l.X - l.W * 0.5f - 3f, l.X + l.W * 0.5f + 3f,
                                    l.Z - l.D * 0.5f - 3f, l.Z + l.D * 0.5f + 3f));
            }
            return list.ToArray();
        }

        bool InsideLandmark(Vector2 world)
        {
            for (int i = 0; i < landmarkReserves.Length; i++)
                if (world.x > landmarkReserves[i].X0 && world.x < landmarkReserves[i].X1 &&
                    world.y > landmarkReserves[i].Z0 && world.y < landmarkReserves[i].Z1) return true;
            return false;
        }

        // ==================================================================== mesh plumbing

        MeshBuf ChunkFor(Vector2 world)
        {
            var key = new Vector2Int(
                Mathf.FloorToInt((world.x + 3000f) / chunkSize),
                Mathf.FloorToInt((world.y + 8000f) / chunkSize));
            if (!chunks.TryGetValue(key, out MeshBuf b))
            {
                b = new MeshBuf(2);
                chunks.Add(key, b);
            }
            return b;
        }

        void EmitPolygon(MeshBuf b, int sub, Vector2[] outer, IList<IList<Vector2>> holes, float y,
                         Color32 col, float uvScale)
        {
            var verts = new List<Vector2>();
            var tris = new List<int>();
            PolygonTriangulator.Triangulate(outer, holes, verts, tris);
            if (tris.Count == 0) return;

            int baseIdx = b.P.Count;
            for (int i = 0; i < verts.Count; i++)
            {
                Vector2 v = verts[i];
                b.Add(CityFrame.ToUnity(v.x, y, v.y), Vector3.up,
                      new Vector2(v.x * uvScale, v.y * uvScale),
                      Vector2.zero, Vector2.zero, Vector2.zero, col);
            }
            for (int i = 0; i + 2 < tris.Count; i += 3)
                b.Tri(sub, baseIdx + tris[i], baseIdx + tris[i + 1], baseIdx + tris[i + 2], Vector3.up);
        }

        void Quad(MeshBuf b, int sub, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n,
                  Vector2 a0, Vector2 a1, Vector2 a2, Vector2 a3,
                  Vector2 uv1, Vector2 uv2, Vector2 uv3, Color32 col)
        {
            int i0 = b.Add(p0, n, a0, uv1, uv2, uv3, col);
            int i1 = b.Add(p1, n, a1, uv1, uv2, uv3, col);
            int i2 = b.Add(p2, n, a2, uv1, uv2, uv3, col);
            int i3 = b.Add(p3, n, a3, uv1, uv2, uv3, col);
            b.Tri(sub, i0, i1, i2, n);
            b.Tri(sub, i0, i2, i3, n);
        }

        void FlushChunks()
        {
            foreach (var kv in chunks)
            {
                MeshBuf b = kv.Value;
                if (b.P.Count == 0) continue;
                var go = new GameObject($"Blocks_{kv.Key.x}_{kv.Key.y}");
                go.transform.SetParent(root, false);
                var mesh = ToMesh(b, go.name, 2);
                var mr = go.AddComponent<MeshRenderer>();
                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;
                mr.sharedMaterials = new[] { facadeMaterial != null ? facadeMaterial : DefaultFacade(), solidMaterial != null ? solidMaterial : DefaultSolid() };
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;
            }
        }

        void FlushGround()
        {
            if (ground.P.Count == 0) return;
            var go = new GameObject("Ground");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = ToMesh(ground, "ManhattanGround", 4);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[]
            {
                landMaterial != null ? landMaterial : DefaultSolid(),
                sidewalkMaterial != null ? sidewalkMaterial : DefaultSolid(),
                parkMaterial != null ? parkMaterial : DefaultSolid(),
                waterMaterial != null ? waterMaterial : DefaultSolid()
            };
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        void FlushRoads()
        {
            if (roads.P.Count == 0) return;
            var go = new GameObject("Roads");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = ToMesh(roads, "ManhattanRoads", 1);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = new[] { roadMaterial != null ? roadMaterial : DefaultSolid() };
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;
        }

        static Mesh ToMesh(MeshBuf b, string name, int subCount)
        {
            var m = new Mesh { name = name };
            if (b.P.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(b.P);
            m.SetNormals(b.N);
            m.SetUVs(0, b.U0);
            m.SetUVs(1, b.U1);
            m.SetUVs(2, b.U2);
            m.SetUVs(3, b.U3);
            m.SetColors(b.C);
            m.subMeshCount = subCount;
            for (int i = 0; i < subCount; i++) m.SetTriangles(b.Subs[i], i, false);
            m.RecalculateBounds();
            return m;
        }

        Material DefaultFacade()
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "FacadeFallback" };
            m.SetColor("_BaseColor", new Color(0.72f, 0.68f, 0.6f));
            return m;
        }

        Material DefaultSolid()
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "SolidFallback" };
            m.SetColor("_BaseColor", new Color(0.55f, 0.55f, 0.55f));
            return m;
        }

        static Vector2 PolygonCentroid(Vector2[] p)
        {
            Vector2 c = Vector2.zero;
            for (int i = 0; i < p.Length; i++) c += p[i];
            return c / p.Length;
        }

        // ==================================================================== helpers

        static class WallKind
        {
            public const int Shop = 0, Entry = 1, Rear = 2, LotLine = 3, BlankStone = 4;
        }

        readonly struct BoxFrame
        {
            public readonly bool IsIdentity;
            readonly float px, pz, cs, sn;

            public float Cs => cs;
            public float Sn => sn;

            public BoxFrame(float theta, float fx, float fz)
            {
                IsIdentity = Mathf.Abs(theta) < 1e-6f && fx == 0f && fz == 0f;
                px = fx; pz = fz;
                cs = Mathf.Cos(theta);
                sn = Mathf.Sin(theta);
            }

            public static BoxFrame Identity => new BoxFrame(0f, 0f, 0f);

            public Vector3 P(float x, float y, float z)
            {
                if (IsIdentity) return CityFrame.ToUnity(x, y, z);
                float wx = px + (x - px) * cs + (z - pz) * sn;
                float wz = pz - (x - px) * sn + (z - pz) * cs;
                return CityFrame.ToUnity(wx, y, wz);
            }

            public Vector3 N(float nx, float ny, float nz)
            {
                if (IsIdentity) return new Vector3(nx, ny, -nz);
                return new Vector3(nx * cs + nz * sn, ny, nx * sn - nz * cs);
            }

            public Vector2 WorldXZ(Vector2 local)
            {
                if (IsIdentity) return local;
                return new Vector2(px + (local.x - px) * cs + (local.y - pz) * sn,
                                   pz - (local.x - px) * sn + (local.y - pz) * cs);
            }
        }

        sealed class MeshBuf
        {
            public readonly List<Vector3> P = new List<Vector3>(4096);
            public readonly List<Vector3> N = new List<Vector3>(4096);
            public readonly List<Vector2> U0 = new List<Vector2>(4096);
            public readonly List<Vector2> U1 = new List<Vector2>(4096);
            public readonly List<Vector2> U2 = new List<Vector2>(4096);
            public readonly List<Vector2> U3 = new List<Vector2>(4096);
            public readonly List<Color32> C = new List<Color32>(4096);
            public readonly List<int>[] Subs;

            public MeshBuf(int subCount)
            {
                Subs = new List<int>[subCount];
                for (int i = 0; i < subCount; i++) Subs[i] = new List<int>(8192);
            }

            public int Add(Vector3 p, Vector3 n, Vector2 u0, Vector2 u1, Vector2 u2, Vector2 u3, Color32 c)
            {
                P.Add(p); N.Add(n); U0.Add(u0); U1.Add(u1); U2.Add(u2); U3.Add(u3); C.Add(c);
                return P.Count - 1;
            }

            /// <summary>
            /// Emits a triangle with Unity front-face winding: a face is front-facing from the
            /// side its vertex normals point to when dot(cross(b-a, c-a), n) &gt; 0.
            /// </summary>
            public void Tri(int sub, int a, int b, int c, Vector3 n)
            {
                Vector3 cr = Vector3.Cross(P[b] - P[a], P[c] - P[a]);
                if (Vector3.Dot(cr, n) < 0f) { int t = b; b = c; c = t; }
                var s = Subs[sub];
                s.Add(a); s.Add(b); s.Add(c);
            }
        }

        struct DistrictProfile
        {
            public int FloorsMin, FloorsMax;         // street-wall / fill height
            public int TowerFloorsMin, TowerFloorsMax;
            public float TowerChance, Storey;
            public int Style;

            public static DistrictProfile For(string district)
            {
                switch (district)
                {
                    case "midtown":            return P(8, 16, 24, 44, 0.38f, 3.9f, 3);
                    case "garment":            return P(8, 14, 16, 28, 0.25f, 3.8f, 1);
                    case "hudson_yards":       return P(10, 18, 34, 54, 0.45f, 4.0f, 5);
                    case "billionaires_row":   return P(10, 22, 40, 72, 0.50f, 4.1f, 5);
                    case "fidi":               return P(9, 18, 22, 46, 0.40f, 3.9f, 4);
                    case "civic_center":       return P(8, 16, 18, 30, 0.25f, 3.8f, 0);
                    case "battery_park_city":  return P(8, 16, 18, 38, 0.30f, 3.9f, 3);
                    case "hells_kitchen":      return P(6, 12, 16, 26, 0.18f, 3.6f, 2);
                    case "murray_hill":        return P(8, 14, 18, 30, 0.20f, 3.7f, 0);
                    case "turtle_bay":         return P(8, 14, 18, 30, 0.25f, 3.8f, 0);
                    case "uws":
                    case "ues":                return P(8, 14, 16, 26, 0.12f, 3.7f, 0);
                    case "harlem":
                    case "east_harlem":
                    case "morningside":        return P(4, 9, 12, 18, 0.05f, 3.5f, 2);
                    case "village":
                    case "west_village":
                    case "east_village":       return P(5, 10, 12, 20, 0.06f, 3.5f, 2);
                    case "soho":
                    case "tribeca":
                    case "hudson_square":      return P(5, 8, 10, 16, 0.04f, 3.7f, 1);
                    case "chelsea":
                    case "west_chelsea":       return P(6, 12, 14, 24, 0.12f, 3.7f, 1);
                    case "flatiron":
                    case "nomad":              return P(8, 14, 16, 28, 0.20f, 3.8f, 1);
                    case "gramercy":           return P(7, 13, 14, 24, 0.12f, 3.7f, 2);
                    case "little_italy":
                    case "chinatown":
                    case "lower_east_side":    return P(5, 10, 12, 20, 0.05f, 3.5f, 2);
                    case "stuy_town":          return P(12, 14, 14, 16, 0.02f, 3.4f, 1);
                    default:                   return P(8, 16, 20, 36, 0.30f, 3.8f, 3);
                }
            }

            static DistrictProfile P(int a, int b, int ta, int tb, float t, float s, int style) =>
                new DistrictProfile
                {
                    FloorsMin = a, FloorsMax = b,
                    TowerFloorsMin = ta, TowerFloorsMax = tb,
                    TowerChance = t, Storey = s, Style = style
                };
        }

        struct Mulberry32
        {
            uint state;
            public Mulberry32(uint s) { state = s; }

            public float NextFloat()
            {
                unchecked
                {
                    state += 0x6D2B79F5u;
                    uint t = state;
                    t = (t ^ (t >> 15)) * (t | 1u);
                    t ^= t + (t ^ (t >> 7)) * (t | 61u);
                    return (t ^ (t >> 14)) / 4294967296f;
                }
            }

            public float Range(float a, float b) => a + (b - a) * NextFloat();
            public int Range(int a, int b) => a + (int)(NextFloat() * (b - a + 1));
        }
    }
}
