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
                if (yHeight > c.MaxY + 1.5f) continue;

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
                if (yHeight > c.MaxY + 1.5f) continue;
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
    }
}
