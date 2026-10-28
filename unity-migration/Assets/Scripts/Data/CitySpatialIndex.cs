using System.Collections.Generic;
using UnityEngine;
using Nyc.Data;

namespace Nyc.Core
{
    /// <summary>
    /// 64 m uniform grid over Manhattan's curb islands, mirroring BLOCK_GRID / blockAt() in
    /// city-config.js. Used for the walking surface test (sidewalk vs roadway) and by the
    /// minimap. Built once after the geodata is loaded.
    /// </summary>
    public static class CitySpatialIndex
    {
        const float Cell = 64f;

        static readonly Dictionary<long, List<BlockRec>> Grid = new Dictionary<long, List<BlockRec>>(4096);
        static readonly List<BlockRec> Empty = new List<BlockRec>(0);

        public static bool Ready { get; private set; }

        public static void Build(NycGeoData geo)
        {
            Grid.Clear();
            if (geo?.Blocks == null) { Ready = false; return; }

            for (int i = 0; i < geo.Blocks.Count; i++)
            {
                BlockRec b = geo.Blocks[i];
                int ix0 = Mathf.FloorToInt(b.Bounds.x / Cell);
                int ix1 = Mathf.FloorToInt(b.Bounds.y / Cell);
                int iz0 = Mathf.FloorToInt(b.Bounds.z / Cell);
                int iz1 = Mathf.FloorToInt(b.Bounds.w / Cell);

                for (int ix = ix0; ix <= ix1; ix++)
                {
                    for (int iz = iz0; iz <= iz1; iz++)
                    {
                        long key = Key(ix, iz);
                        if (!Grid.TryGetValue(key, out var list))
                        {
                            list = new List<BlockRec>(4);
                            Grid[key] = list;
                        }
                        list.Add(b);
                    }
                }
            }
            Ready = true;
        }

        static long Key(int ix, int iz) => ((long)(ix + 8192) << 20) | (uint)(iz + 8192);

        /// <summary>The curb island under (x, z) in the source frame, or null on the roadway / water.</summary>
        public static BlockRec BlockAt(float x, float z)
        {
            if (!Ready) return null;
            if (!Grid.TryGetValue(Key(Mathf.FloorToInt(x / Cell), Mathf.FloorToInt(z / Cell)), out var list))
                return null;

            for (int i = 0; i < list.Count; i++)
            {
                BlockRec b = list[i];
                if (x < b.Bounds.x || x > b.Bounds.y || z < b.Bounds.z || z > b.Bounds.w) continue;
                if (b.IsRect || PointInPolygon(b.Poly, x, z)) return b;
            }
            return null;
        }

        /// <summary>True on a raised curb island that is not a park (mirrors isPointOnSidewalk).</summary>
        public static bool IsOnSidewalk(float x, float z)
        {
            BlockRec b = BlockAt(x, z);
            return b != null && !b.Median && !(b.Park > 0.6f);
        }

        public static bool PointInPolygon(Vector2[] poly, float x, float z)
        {
            if (poly == null || poly.Length < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > z) != (b.y > z) &&
                    x < (b.x - a.x) * (z - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }
    }
}
