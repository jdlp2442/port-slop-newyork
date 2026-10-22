using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nyc.Generation
{
    /// <summary>
    /// Ear-clipping triangulator for Manhattan's rings.
    ///
    /// Holes are handled by clipping the outer ring and dropping every triangle whose centroid
    /// falls inside a hole. That is O(n * h) but exact for non-overlapping holes and, unlike
    /// bridge insertion, it cannot fail on rings with many holes (Central Park's ponds).
    /// The output indices point into a vertex list equal to the (CCW) outer ring.
    /// </summary>
    public static class PolygonTriangulator
    {
        const float Eps = 1e-6f;

        public static void Triangulate(IList<Vector2> outer, IList<IList<Vector2>> holes,
                                       List<Vector2> outVerts, List<int> outTris)
        {
            outVerts.Clear();
            outTris.Clear();
            if (outer == null || outer.Count < 3) return;

            var poly = new List<Vector2>(outer.Count);
            for (int i = 0; i < outer.Count; i++) poly.Add(outer[i]);
            if (SignedArea(poly) < 0f) poly.Reverse();

            outVerts.AddRange(poly);
            EarClip(poly, outTris);

            if (holes == null || holes.Count == 0 || outTris.Count == 0) return;

            var kept = new List<int>(outTris.Count);
            for (int i = 0; i + 2 < outTris.Count; i += 3)
            {
                Vector2 c = (poly[outTris[i]] + poly[outTris[i + 1]] + poly[outTris[i + 2]]) / 3f;
                bool inHole = false;
                for (int h = 0; h < holes.Count && !inHole; h++)
                {
                    var hole = holes[h];
                    if (hole != null && hole.Count >= 3 && PointInPolygon(hole, c)) inHole = true;
                }
                if (inHole) continue;
                kept.Add(outTris[i]); kept.Add(outTris[i + 1]); kept.Add(outTris[i + 2]);
            }
            outTris.Clear();
            outTris.AddRange(kept);
        }

        public static float SignedArea(IList<Vector2> p)
        {
            float a = 0f;
            for (int i = 0, j = p.Count - 1; i < p.Count; j = i++)
                a += p[j].x * p[i].y - p[i].x * p[j].y;
            return a * 0.5f;
        }

        public static bool PointInPolygon(IList<Vector2> poly, Vector2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) &&
                    p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
            }
            return inside;
        }

        // ------------------------------------------------------------------ ear clipping

        static void EarClip(List<Vector2> poly, List<int> tris)
        {
            int n = poly.Count;
            if (n < 3) return;

            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);

            int guard = 0, maxGuard = n * n + 64;
            while (idx.Count > 3 && guard++ < maxGuard)
            {
                bool clipped = false;
                for (int k = 0; k < idx.Count; k++)
                {
                    int ia = idx[(k - 1 + idx.Count) % idx.Count];
                    int ib = idx[k];
                    int ic = idx[(k + 1) % idx.Count];
                    Vector2 a = poly[ia], b = poly[ib], c = poly[ic];

                    if (Cross(b - a, c - b) <= Eps) continue;   // reflex or collinear

                    bool contains = false;
                    for (int j = 0; j < idx.Count; j++)
                    {
                        int ij = idx[j];
                        if (ij == ia || ij == ib || ij == ic) continue;
                        if (StrictlyInside(poly[ij], a, b, c)) { contains = true; break; }
                    }
                    if (contains) continue;

                    tris.Add(ia); tris.Add(ib); tris.Add(ic);
                    idx.RemoveAt(k);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }

            if (idx.Count == 3)
            {
                tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]);
            }
            else if (idx.Count > 3)
            {
                // Degenerate remainder (self-touching ring): fan it rather than dropping area.
                for (int i = 1; i + 1 < idx.Count; i++)
                {
                    tris.Add(idx[0]); tris.Add(idx[i]); tris.Add(idx[i + 1]);
                }
            }
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool StrictlyInside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross(b - a, p - a);
            float d2 = Cross(c - b, p - b);
            float d3 = Cross(a - c, p - c);
            return d1 > Eps && d2 > Eps && d3 > Eps;
        }
    }
}
