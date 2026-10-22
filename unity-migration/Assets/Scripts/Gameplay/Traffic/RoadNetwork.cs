using System.Collections.Generic;
using UnityEngine;
using Nyc.Data;

namespace Nyc.Gameplay.Traffic
{
    /// <summary>
    /// Drivable lane graph built from GEO.roads.
    ///
    /// The source builds its network from the *built* geometry (road-network.js); here the
    /// centrelines are split at every crossing with another road, which turns the 3 246 merged
    /// polylines into a real intersection graph. Each piece becomes one or two directed lanes
    /// with a lateral offset per travel lane.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RoadNetwork : MonoBehaviour
    {
        public struct Lane
        {
            public int Id;
            public Vector2[] Points;
            public float[] Cum;          // cumulative length at each point
            public float Length;
            public float HalfWidth;
            public float SpeedLimit;
            public bool IsAvenue;
            public bool IsHighway;
            public string Name;
            public int Dir;              // 1 forward, -1 reversed (both are baked as separate lanes)
        }

        public Lane[] Lanes = System.Array.Empty<Lane>();
        public int[][] Next = System.Array.Empty<int[]>();

        [Header("Stats")]
        public int roadCount;
        public int laneCount;
        public int connectionCount;

        const float SampleStep = 4f;
        const float EndpointMatch = 12f;
        const float SplitMerge = 10f;

        // ------------------------------------------------------------------ build

        public void Build(NycGeoData geo)
        {
            var raw = new List<RawRoad>(geo.Roads.Count);
            for (int i = 0; i < geo.Roads.Count; i++)
            {
                RoadRec r = geo.Roads[i];
                if (r.Pts == null || r.Pts.Length < 2 || r.Width < 4f) continue;
                if (r.Pts.Length == 2 && (r.Pts[1] - r.Pts[0]).sqrMagnitude < 100f) continue;

                raw.Add(new RawRoad
                {
                    Name = r.Name,
                    Width = r.Width,
                    Dir = r.Dir,
                    Lanes = Mathf.Max(1, r.Lanes),
                    Highway = r.Highway,
                    Pts = r.Pts
                });
            }
            roadCount = raw.Count;

            // Spatial hash of every segment, used to find crossings.
            var hash = new Dictionary<long, List<int>>(raw.Count * 8);
            for (int i = 0; i < raw.Count; i++)
            {
                var pts = raw[i].Pts;
                for (int k = 0; k + 1 < pts.Length; k++)
                {
                    Vector2 a = pts[k], b = pts[k + 1];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, b.x) / 48f);
                    int x1 = Mathf.FloorToInt(Mathf.Max(a.x, b.x) / 48f);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.y, b.y) / 48f);
                    int z1 = Mathf.FloorToInt(Mathf.Max(a.y, b.y) / 48f);
                    for (int x = x0; x <= x1; x++)
                        for (int z = z0; z <= z1; z++)
                        {
                            long key = Key(x, z);
                            if (!hash.TryGetValue(key, out var list)) { list = new List<int>(4); hash[key] = list; }
                            list.Add(i * 4096 + k);
                        }
                }
            }

            var lanes = new List<Lane>(raw.Count * 4);

            for (int i = 0; i < raw.Count; i++)
            {
                RawRoad road = raw[i];
                List<float> cuts = FindCrossings(i, road, hash, raw);

                var pieces = SplitPolyline(road.Pts, cuts);
                for (int p = 0; p < pieces.Count; p++)
                {
                    Vector2[] piece = pieces[p];
                    if (PolylineLength(piece) < 6f) continue;

                    AddLanes(lanes, piece, road, +1);
                    if (road.Dir == 0) AddLanes(lanes, piece, road, -1);
                }
            }

            Lanes = lanes.ToArray();
            laneCount = Lanes.Length;
            BuildConnections();
        }

        void AddLanes(List<Lane> lanes, Vector2[] pts, RawRoad road, int dir)
        {
            int total = Mathf.Max(1, road.Lanes);
            int perDir = road.Dir == 0 ? Mathf.Max(1, total / 2) : total;
            float laneWidth = road.Width / total;

            Vector2[] ordered = dir > 0 ? pts : Reverse(pts);

            for (int l = 0; l < perDir; l++)
            {
                float lateral = road.Dir == 0
                    ? (l + 0.5f) * laneWidth
                    : (l + 0.5f - perDir * 0.5f) * laneWidth;

                if (road.Dir == 0) lateral = dir > 0 ? -lateral : lateral;

                Vector2[] offsetPts = OffsetPolyline(ordered, lateral);
                lanes.Add(new Lane
                {
                    Id = lanes.Count,
                    Points = offsetPts,
                    Cum = Cumulative(offsetPts),
                    Length = PolylineLength(offsetPts),
                    HalfWidth = Mathf.Max(2.5f, road.Width * 0.5f / perDir),
                    SpeedLimit = SpeedFor(road),
                    IsAvenue = IsAvenue(road.Name),
                    IsHighway = road.Highway,
                    Name = road.Name,
                    Dir = dir
                });
            }
        }

        static float SpeedFor(RawRoad r)
        {
            if (r.Highway) return 22.35f;               // 50 mph
            if (IsAvenue(r.Name)) return 13.4f;         // 30 mph
            return 8.94f;                               // 20 mph
        }

        static bool IsAvenue(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            return name.Contains("AVE") || name.Contains("AVENUE") || name.Contains("BLVD") ||
                   name.Contains("BROADWAY") || name.Contains("PARK ") || name.Contains("DRIVE");
        }

        // ------------------------------------------------------------------ crossings

        struct RawRoad
        {
            public string Name;
            public float Width;
            public int Dir;
            public int Lanes;
            public bool Highway;
            public Vector2[] Pts;
        }

        List<float> FindCrossings(int roadIndex, RawRoad road, Dictionary<long, List<int>> hash, List<RawRoad> all)
        {
            var cuts = new List<float>(8);
            float travelled = 0f;
            Vector2[] pts = road.Pts;

            for (int k = 0; k + 1 < pts.Length; k++)
            {
                Vector2 a = pts[k], b = pts[k + 1];
                float segLen = Vector2.Distance(a, b);
                if (segLen < 0.01f) continue;

                Vector2 dirA = (b - a) / segLen;
                int steps = Mathf.Max(1, Mathf.CeilToInt(segLen / SampleStep));

                for (int s = 0; s < steps; s++)
                {
                    Vector2 p = Vector2.Lerp(a, b, (s + 0.5f) / steps);
                    int cx = Mathf.FloorToInt(p.x / 48f);
                    int cz = Mathf.FloorToInt(p.y / 48f);
                    long key = Key(cx, cz);
                    if (!hash.TryGetValue(key, out var list)) continue;

                    for (int q = 0; q < list.Count; q++)
                    {
                        int otherRoad = list[q] / 4096;
                        if (otherRoad == roadIndex) continue;

                        RawRoad o = all[otherRoad];
                        int segIndex = list[q] % 4096;
                        if (segIndex + 1 >= o.Pts.Length) continue;

                        Vector2 oa = o.Pts[segIndex], ob = o.Pts[segIndex + 1];
                        Vector2 dirB = (ob - oa).normalized;
                        if (Mathf.Abs(Vector2.Dot(dirA, dirB)) > 0.94f) continue;   // parallel

                        float reach = road.Width * 0.5f + o.Width * 0.5f + 2f;
                        if (DistancePointSegment(p, oa, ob) > reach) continue;

                        float t = travelled + segLen * ((s + 0.5f) / steps);
                        if (cuts.Count == 0 || Mathf.Abs(cuts[cuts.Count - 1] - t) > SplitMerge) cuts.Add(t);
                        break;
                    }
                }
                travelled += segLen;
            }
            return cuts;
        }

        static float DistancePointSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float l2 = ab.sqrMagnitude;
            if (l2 < 1e-6f) return Vector2.Distance(p, a);
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
            return Vector2.Distance(p, a + ab * t);
        }

        static List<Vector2[]> SplitPolyline(Vector2[] pts, List<float> cuts)
        {
            var result = new List<Vector2[]>(cuts.Count + 1);
            if (cuts.Count == 0) { result.Add(pts); return result; }

            float travelled = 0f;
            var current = new List<Vector2> { pts[0] };
            int cutIndex = 0;

            for (int k = 0; k + 1 < pts.Length; k++)
            {
                Vector2 a = pts[k], b = pts[k + 1];
                float segLen = Vector2.Distance(a, b);
                if (segLen < 0.01f) continue;

                while (cutIndex < cuts.Count && cuts[cutIndex] <= travelled + segLen)
                {
                    float local = Mathf.Clamp01((cuts[cutIndex] - travelled) / segLen);
                    Vector2 cutPoint = Vector2.Lerp(a, b, local);
                    current.Add(cutPoint);
                    if (current.Count >= 2) result.Add(current.ToArray());
                    current = new List<Vector2> { cutPoint };
                    cutIndex++;
                }

                current.Add(b);
                travelled += segLen;
            }

            if (current.Count >= 2) result.Add(current.ToArray());
            return result;
        }

        // ------------------------------------------------------------------ connections

        void BuildConnections()
        {
            var starts = new Dictionary<long, List<int>>(Lanes.Length);
            for (int i = 0; i < Lanes.Length; i++)
            {
                Vector2 p = Lanes[i].Points[0];
                long key = Key(Mathf.FloorToInt(p.x / 16f), Mathf.FloorToInt(p.y / 16f));
                if (!starts.TryGetValue(key, out var list)) { list = new List<int>(4); starts[key] = list; }
                list.Add(i);
            }

            Next = new int[Lanes.Length][];
            int total = 0;

            for (int i = 0; i < Lanes.Length; i++)
            {
                Lane lane = Lanes[i];
                Vector2 end = lane.Points[lane.Points.Length - 1];
                Vector2 dirEnd = (lane.Points[lane.Points.Length - 1] - lane.Points[lane.Points.Length - 2]).normalized;

                var candidates = new List<int>(6);
                int cx = Mathf.FloorToInt(end.x / 16f);
                int cz = Mathf.FloorToInt(end.y / 16f);

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!starts.TryGetValue(Key(cx + dx, cz + dz), out var list)) continue;
                        for (int k = 0; k < list.Count; k++)
                        {
                            int j = list[k];
                            if (j == i) continue;
                            Lane other = Lanes[j];
                            Vector2 start = other.Points[0];
                            if (Vector2.Distance(end, start) > EndpointMatch) continue;

                            Vector2 dirStart = (other.Points[1] - other.Points[0]).normalized;
                            if (Vector2.Dot(dirEnd, dirStart) < -0.25f) continue;   // no giros de 180
                            candidates.Add(j);
                        }
                    }
                }

                Next[i] = candidates.ToArray();
                total += candidates.Count;
            }
            connectionCount = total;
        }

        // ------------------------------------------------------------------ sampling

        public void Sample(int laneId, float s, out Vector2 pos, out Vector2 dir)
        {
            Lane lane = Lanes[laneId];
            float[] cum = lane.Cum;
            s = Mathf.Clamp(s, 0f, lane.Length);

            int lo = 0, hi = cum.Length - 1;
            while (lo < hi - 1)
            {
                int mid = (lo + hi) >> 1;
                if (cum[mid] <= s) lo = mid; else hi = mid;
            }

            float segLen = Mathf.Max(0.0001f, cum[hi] - cum[lo]);
            float t = Mathf.Clamp01((s - cum[lo]) / segLen);
            pos = Vector2.Lerp(lane.Points[lo], lane.Points[hi], t);
            dir = (lane.Points[hi] - lane.Points[lo]).normalized;
        }

        /// <summary>Nearest distance along a lane to a world XZ point (source frame).</summary>
        public float Project(int laneId, Vector2 p)
        {
            Lane lane = Lanes[laneId];
            float best = 0f, bestDist = float.MaxValue;

            for (int i = 0; i + 1 < lane.Points.Length; i++)
            {
                Vector2 a = lane.Points[i], b = lane.Points[i + 1];
                Vector2 ab = b - a;
                float l2 = ab.sqrMagnitude;
                if (l2 < 1e-6f) continue;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / l2);
                float d = Vector2.Distance(p, a + ab * t);
                if (d < bestDist) { bestDist = d; best = lane.Cum[i] + Mathf.Sqrt(l2) * t; }
            }
            return best;
        }

        // ------------------------------------------------------------------ helpers

        static long Key(int x, int z) => ((long)(x + 8192) << 20) | (uint)(z + 8192);

        static Vector2[] Reverse(Vector2[] pts)
        {
            var r = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++) r[i] = pts[pts.Length - 1 - i];
            return r;
        }

        static Vector2[] OffsetPolyline(Vector2[] pts, float offset)
        {
            if (Mathf.Abs(offset) < 0.001f) return pts;

            var r = new Vector2[pts.Length];
            for (int i = 0; i < pts.Length; i++)
            {
                Vector2 dir;
                if (i == 0) dir = (pts[1] - pts[0]).normalized;
                else if (i == pts.Length - 1) dir = (pts[i] - pts[i - 1]).normalized;
                else dir = (pts[i + 1] - pts[i - 1]).normalized;

                var normal = new Vector2(dir.y, -dir.x);
                r[i] = pts[i] + normal * offset;
            }
            return r;
        }

        static float[] Cumulative(Vector2[] pts)
        {
            var cum = new float[pts.Length];
            for (int i = 1; i < pts.Length; i++) cum[i] = cum[i - 1] + Vector2.Distance(pts[i - 1], pts[i]);
            return cum;
        }

        static float PolylineLength(Vector2[] pts)
        {
            float l = 0f;
            for (int i = 1; i < pts.Length; i++) l += Vector2.Distance(pts[i - 1], pts[i]);
            return l;
        }
    }
}
