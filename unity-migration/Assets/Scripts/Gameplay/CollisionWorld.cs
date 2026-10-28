using System.Collections.Generic;
using UnityEngine;
using Nyc.Core;

namespace Nyc.Gameplay
{
    /// <summary>
    /// Static horizontal collision for the whole island.
    /// Port of ThirdPersonControllerManager.nearColliders() + resolveHorizontalCollision()
    /// (controllers.js:729-900): a 48 m uniform grid, AABB / OBB push-out, and the "dropped
    /// inside a building" re-query.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CollisionWorld : MonoBehaviour
    {
        public const float Cell = 48f;

        [Header("Island bounds (source frame, filled from CityConfig)")]
        public Vector4 Bounds = new Vector4(-2117f, 2569f, -7090f, 6512f);

        readonly Dictionary<long, List<int>> _cells = new Dictionary<long, List<int>>(8192);
        readonly List<int> _query = new List<int>(64);
        CollisionBox[] _boxes = System.Array.Empty<CollisionBox>();
        int[] _stamp = System.Array.Empty<int>();
        int _tick;

        public int Count => _boxes.Length;

        static long Key(int ix, int iz) => ((long)(ix + 8192) << 20) | (uint)(iz + 8192);

        // ------------------------------------------------------------------ build

        public void Build(IReadOnlyList<CollisionBox> boxes)
        {
            _boxes = new CollisionBox[boxes.Count];
            _stamp = new int[boxes.Count];
            _cells.Clear();

            for (int i = 0; i < boxes.Count; i++)
            {
                CollisionBox b = boxes[i];
                _boxes[i] = b;

                int ix0 = Mathf.FloorToInt(b.MinX / Cell);
                int ix1 = Mathf.FloorToInt(b.MaxX / Cell);
                int iz0 = Mathf.FloorToInt(b.MinZ / Cell);
                int iz1 = Mathf.FloorToInt(b.MaxZ / Cell);
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    for (int iz = iz0; iz <= iz1; iz++)
                    {
                        long k = Key(ix, iz);
                        if (!_cells.TryGetValue(k, out var list))
                        {
                            list = new List<int>(8);
                            _cells[k] = list;
                        }
                        list.Add(i);
                    }
                }
            }
        }

        public void SetIslandBounds(Vector4 sourceBounds) => Bounds = sourceBounds;

        // ------------------------------------------------------------------ query

        /// <summary>Indices of the boxes whose cells overlap the square of half-size r around p.</summary>
        public List<int> Query(Vector2 p, float r)
        {
            _query.Clear();
            if (_boxes.Length == 0) return _query;

            int stamp = ++_tick;
            int ix0 = Mathf.FloorToInt((p.x - r) / Cell);
            int ix1 = Mathf.FloorToInt((p.x + r) / Cell);
            int iz0 = Mathf.FloorToInt((p.y - r) / Cell);
            int iz1 = Mathf.FloorToInt((p.y + r) / Cell);

            for (int ix = ix0; ix <= ix1; ix++)
            {
                for (int iz = iz0; iz <= iz1; iz++)
                {
                    if (!_cells.TryGetValue(Key(ix, iz), out var list)) continue;
                    for (int k = 0; k < list.Count; k++)
                    {
                        int idx = list[k];
                        if (_stamp[idx] == stamp) continue;
                        _stamp[idx] = stamp;
                        _query.Add(idx);
                    }
                }
            }
            return _query;
        }

        /// <summary>
        /// Returns the highest roof surface at (x, z) within radius, or 0 if on the ground/street.
        /// When currentAltitude is provided, only surfaces at or below the entity (with up to 2.5m headroom)
        /// are considered so tall walls beside the mover do not catapult them into the sky.
        /// </summary>
        public float GetRoofHeight(float x, float z, float currentAltitude = float.MaxValue, float radius = 0f)
        {
            if (_boxes.Length == 0) return 0f;
            float reach = radius + 2f;
            var near = Query(new Vector2(x, z), reach);
            float maxRoof = 0f;

            for (int i = 0; i < near.Count; i++)
            {
                CollisionBox c = _boxes[near[i]];
                if (c.MaxY <= maxRoof) continue;
                if (currentAltitude < float.MaxValue && c.MaxY > currentAltitude + 2.5f) continue;

                if (c.HasObb)
                {
                    float dx = x - c.Cx;
                    float dz = z - c.Cz;
                    float lx = dx * c.Cs - dz * c.Sn;
                    float lz = dx * c.Sn + dz * c.Cs;
                    if (Mathf.Abs(lx) <= c.Hx + radius && Mathf.Abs(lz) <= c.Hz + radius)
                    {
                        if (c.MaxY > maxRoof) maxRoof = c.MaxY;
                    }
                }
                else
                {
                    if (x + radius >= c.MinX && x - radius <= c.MaxX &&
                        z + radius >= c.MinZ && z - radius <= c.MaxZ)
                    {
                        if (c.MaxY > maxRoof) maxRoof = c.MaxY;
                    }
                }
            }
            return maxRoof;
        }

        // ------------------------------------------------------------------ resolve

        /// <summary>
        /// Pushes <paramref name="pos"/> out of every overlapping box and appends the contact
        /// normals to <paramref name="normals"/>. Returns the number of contacts.
        /// extX / extZ are the half-extents of the mover in world axes (the car passes its
        /// heading-projected footprint, everything else a radius).
        /// </summary>
        public int Resolve(ref Vector2 pos, float extX, float extZ, float yHeight, List<Vector2> normals)
        {
            if (_boxes.Length == 0) return 0;
            normals.Clear();

            float reach = Mathf.Max(extX, extZ) + 6f;
            var near = Query(pos, reach);
            int n = 0;

            for (int i = 0; i < near.Count; i++)
            {
                CollisionBox c = _boxes[near[i]];
                if (yHeight >= c.MaxY - 0.25f || yHeight < c.MinY - 0.8f) continue;

                if (pos.x + extX <= c.MinX || pos.x - extX >= c.MaxX ||
                    pos.y + extZ <= c.MinZ || pos.y - extZ >= c.MaxZ) continue;

                Vector2 normal;

                if (c.HasObb)
                {
                    float dx = pos.x - c.Cx;
                    float dz = pos.y - c.Cz;
                    float lx = dx * c.Cs - dz * c.Sn;
                    float lz = dx * c.Sn + dz * c.Cs;

                    if (lx + extX <= -c.Hx || lx - extX >= c.Hx ||
                        lz + extZ <= -c.Hz || lz - extZ >= c.Hz) continue;

                    float oL = lx + extX + c.Hx;
                    float oR = c.Hx - (lx - extX);
                    float oT = lz + extZ + c.Hz;
                    float oB = c.Hz - (lz - extZ);
                    float m = Mathf.Min(Mathf.Min(oL, oR), Mathf.Min(oT, oB));

                    float qx = lx, qz = lz, mx = 0f, mz = 0f;
                    if (m == oL) { qx = -c.Hx - extX; mx = -1f; }
                    else if (m == oR) { qx = c.Hx + extX; mx = 1f; }
                    else if (m == oT) { qz = -c.Hz - extZ; mz = -1f; }
                    else { qz = c.Hz + extZ; mz = 1f; }

                    pos.x = c.Cx + qx * c.Cs + qz * c.Sn;
                    pos.y = c.Cz - qx * c.Sn + qz * c.Cs;
                    normal = new Vector2(mx * c.Cs + mz * c.Sn, -mx * c.Sn + mz * c.Cs);
                }
                else
                {
                    float oL = (pos.x + extX) - c.MinX;
                    float oR = c.MaxX - (pos.x - extX);
                    float oT = (pos.y + extZ) - c.MinZ;
                    float oB = c.MaxZ - (pos.y - extZ);
                    float m = Mathf.Min(Mathf.Min(oL, oR), Mathf.Min(oT, oB));

                    if (m == oL) { pos.x = c.MinX - extX; normal = new Vector2(-1f, 0f); }
                    else if (m == oR) { pos.x = c.MaxX + extX; normal = new Vector2(1f, 0f); }
                    else if (m == oT) { pos.y = c.MinZ - extZ; normal = new Vector2(0f, -1f); }
                    else { pos.y = c.MaxZ + extZ; normal = new Vector2(0f, 1f); }
                }

                normals.Add(normal);
                n++;

                // A push that carried the mover far from where the candidates were gathered:
                // re-query around the new position (rare, only when dropped inside a building).
                if (Mathf.Abs(pos.x) > 1e6f || Mathf.Abs(pos.y) > 1e6f) break;
            }
            return n;
        }

        /// <summary>True if the footprint at pos overlaps any box (used by the teleport search).</summary>
        public bool Overlaps(Vector2 pos, float extX, float extZ, float yHeight)
        {
            if (_boxes.Length == 0) return false;
            float reach = Mathf.Max(extX, extZ) + 6f;
            var near = Query(pos, reach);

            for (int i = 0; i < near.Count; i++)
            {
                CollisionBox c = _boxes[near[i]];
                if (yHeight >= c.MaxY - 0.25f || yHeight < c.MinY - 0.8f) continue;
                if (pos.x + extX <= c.MinX || pos.x - extX >= c.MaxX ||
                    pos.y + extZ <= c.MinZ || pos.y - extZ >= c.MaxZ) continue;

                if (!c.HasObb) return true;

                float dx = pos.x - c.Cx;
                float dz = pos.y - c.Cz;
                float lx = dx * c.Cs - dz * c.Sn;
                float lz = dx * c.Sn + dz * c.Cs;
                if (Mathf.Abs(lx) < c.Hx + extX && Mathf.Abs(lz) < c.Hz + extZ) return true;
            }
            return false;
        }

        /// <summary>
        /// Fast 2D line test against building colliders. Returns true if a building blocks line of sight.
        /// </summary>
        public bool HasBuildingBetween(Vector2 a, Vector2 b, float minHeight = 6f)
        {
            if (_boxes.Length == 0) return false;
            float dist = Vector2.Distance(a, b);
            if (dist < 2f) return false;

            Vector2 dir = (b - a) / dist;
            float step = 24f; // sample cells along line
            int steps = Mathf.Max(1, Mathf.CeilToInt(dist / step));
            int stamp = ++_tick;

            for (int s = 1; s < steps; s++)
            {
                Vector2 sample = a + dir * (s * step);
                int ix = Mathf.FloorToInt(sample.x / Cell);
                int iz = Mathf.FloorToInt(sample.y / Cell);
                if (!_cells.TryGetValue(Key(ix, iz), out var list)) continue;

                for (int k = 0; k < list.Count; k++)
                {
                    int idx = list[k];
                    if (_stamp[idx] == stamp) continue;
                    _stamp[idx] = stamp;
                    CollisionBox c = _boxes[idx];
                    if (c.MaxY < minHeight) continue;

                    // 2D segment vs AABB intersection
                    if (SegmentIntersectsAabb(a, b, c.MinX, c.MaxX, c.MinZ, c.MaxZ))
                        return true;
                }
            }
            return false;
        }

        static bool SegmentIntersectsAabb(Vector2 p0, Vector2 p1, float minX, float maxX, float minZ, float maxZ)
        {
            float dx = p1.x - p0.x;
            float dz = p1.y - p0.y;
            float t0 = 0f, t1 = 1f;

            if (Mathf.Abs(dx) < 0.0001f)
            {
                if (p0.x < minX || p0.x > maxX) return false;
            }
            else
            {
                float invDx = 1f / dx;
                float tx1 = (minX - p0.x) * invDx;
                float tx2 = (maxX - p0.x) * invDx;
                t0 = Mathf.Max(t0, Mathf.Min(tx1, tx2));
                t1 = Mathf.Min(t1, Mathf.Max(tx1, tx2));
                if (t0 > t1) return false;
            }

            if (Mathf.Abs(dz) < 0.0001f)
            {
                if (p0.y < minZ || p0.y > maxZ) return false;
            }
            else
            {
                float invDz = 1f / dz;
                float tz1 = (minZ - p0.y) * invDz;
                float tz2 = (maxZ - p0.y) * invDz;
                t0 = Mathf.Max(t0, Mathf.Min(tz1, tz2));
                t1 = Mathf.Min(t1, Mathf.Max(tz1, tz2));
                if (t0 > t1) return false;
            }

            return true;
        }
    }
}
