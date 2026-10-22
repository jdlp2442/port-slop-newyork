using System.Collections.Generic;
using UnityEngine;
using Nyc.Core;
using Nyc.Data;

namespace Nyc.UI
{
    /// <summary>
    /// Bakes a static, north-up cartographic texture of Manhattan from the same geodata the city
    /// is built from: water, land, parks, curb islands, road centrelines and landmarks.
    ///
    /// This replaces nyc-map.js's per-frame canvas 2D drawing with a one-off CPU rasterisation;
    /// the minimap and the atlas then only move/scale the resulting texture.
    /// </summary>
    public static class NycMapBaker
    {
        public struct MapTexture
        {
            public Texture2D Texture;
            public Rect WorldRect;     // x, z, width, depth in the SOURCE frame
            public int Size;

            public float PixelsPerMetre => Size / Mathf.Max(1f, WorldRect.width);

            /// <summary>Source (x, z) -> texture pixel (origin top-left).</summary>
            public Vector2 WorldToPixel(float x, float z) => new Vector2(
                (x - WorldRect.x) / WorldRect.width * Size,
                (z - WorldRect.y) / WorldRect.height * Size);

            public Vector2 PixelToWorld(Vector2 p) => new Vector2(
                WorldRect.x + p.x / Size * WorldRect.width,
                WorldRect.y + p.y / Size * WorldRect.height);
        }

        static readonly Color32 Water     = new Color32(10, 17, 24, 255);
        static readonly Color32 Land      = new Color32(26, 30, 36, 255);
        static readonly Color32 Park      = new Color32(24, 52, 36, 255);
        static readonly Color32 Block     = new Color32(40, 46, 54, 255);
        static readonly Color32 BlockEdge = new Color32(58, 66, 76, 255);
        static readonly Color32 Road      = new Color32(78, 88, 100, 255);
        static readonly Color32 Avenue    = new Color32(104, 116, 130, 255);
        static readonly Color32 Highway   = new Color32(150, 116, 66, 255);
        static readonly Color32 Landmark  = new Color32(196, 160, 86, 255);

        public static MapTexture Bake(NycGeoData geo, int size = 2048)
        {
            Color32[] px = Rasterize(geo, size, out Rect rect);

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "NYC_MapAtlas",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Texture rows run bottom-up; our raster is top-down.
            var flipped = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                int src = y * size;
                int dst = (size - 1 - y) * size;
                System.Array.Copy(px, src, flipped, dst, size);
            }
            tex.SetPixels32(flipped);
            tex.Apply(false, false);

            return new MapTexture { Texture = tex, WorldRect = rect, Size = size };
        }

        /// <summary>Rasterises the atlas into a top-down RGBA buffer (exposed for tooling/tests).</summary>
        public static Color32[] Rasterize(NycGeoData geo, int size, out Rect rect)
        {
            var ring = geo.Manhattan;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < ring.Length; i++)
            {
                Vector2 v = ring[i];
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minZ) minZ = v.y;
                if (v.y > maxZ) maxZ = v.y;
            }

            float width = maxX - minX;
            float depth = maxZ - minZ;
            float pad = Mathf.Max(width, depth) * 0.04f;
            minX -= pad; maxX += pad; minZ -= pad; maxZ += pad;
            width = maxX - minX;
            depth = maxZ - minZ;

            float span = Mathf.Max(width, depth);
            float cx = (minX + maxX) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;
            rect = new Rect(cx - span * 0.5f, cz - span * 0.5f, span, span);

            var px = new Color32[size * size];
            for (int i = 0; i < px.Length; i++) px[i] = Water;

            FillPolygon(px, size, ring, rect, Land);

            // Parks first, then blocks on top (parks inside blocks are rare but possible).
            for (int i = 0; i < geo.Parks.Count; i++)
            {
                var rings = geo.Parks[i].Rings;
                for (int r = 0; r < rings.Length; r++)
                    FillPolygon(px, size, rings[r], rect, Park);
            }

            for (int i = 0; i < geo.Blocks.Count; i++)
            {
                BlockRec b = geo.Blocks[i];
                if (b.Poly == null || b.Poly.Length < 3) continue;
                if (b.Park > 0.6f && !b.Median) continue;
                FillPolygon(px, size, b.Poly, rect, Block);
            }

            // Road centrelines, avenues wider than streets.
            for (int i = 0; i < geo.Roads.Count; i++)
            {
                RoadRec r = geo.Roads[i];
                if (r.Pts == null || r.Pts.Length < 2) continue;
                bool avenue = r.Width >= 14f || r.Highway;
                float w = Mathf.Clamp(r.Width * 0.35f, 1f, 5f);
                Color32 c = r.Highway ? Highway : avenue ? Avenue : Road;
                for (int k = 0; k + 1 < r.Pts.Length; k++)
                    DrawLine(px, size, r.Pts[k], r.Pts[k + 1], rect, w, c);
            }

            // Landmarks as small warm markers.
            foreach (var kv in geo.Landmarks)
            {
                LandmarkRec l = kv.Value;
                if (l.W < 10f || l.D < 10f) continue;
                DrawDisc(px, size, new Vector2(l.X, l.Z), 2.2f, rect, Landmark);
            }

            return px;
        }

        // ------------------------------------------------------------------ raster

        static void FillPolygon(Color32[] px, int size, Vector2[] poly, Rect rect, Color32 color)
        {
            if (poly == null || poly.Length < 3) return;

            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < poly.Length; i++)
            {
                minX = Mathf.Min(minX, poly[i].x); maxX = Mathf.Max(maxX, poly[i].x);
                minZ = Mathf.Min(minZ, poly[i].y); maxZ = Mathf.Max(maxZ, poly[i].y);
            }

            int y0 = Mathf.Clamp(Mathf.FloorToInt((minZ - rect.y) / rect.height * size), 0, size - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt((maxZ - rect.y) / rect.height * size), 0, size - 1);

            var xs = new List<float>(16);

            for (int y = y0; y <= y1; y++)
            {
                float z = rect.y + (y + 0.5f) / size * rect.height;
                xs.Clear();

                for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                {
                    Vector2 a = poly[i], b = poly[j];
                    if ((a.y > z) == (b.y > z)) continue;
                    float t = (z - a.y) / (b.y - a.y);
                    xs.Add(a.x + (b.x - a.x) * t);
                }
                if (xs.Count < 2) continue;
                xs.Sort();

                for (int k = 0; k + 1 < xs.Count; k += 2)
                {
                    int xa = Mathf.Clamp(Mathf.RoundToInt((xs[k] - rect.x) / rect.width * size), 0, size - 1);
                    int xb = Mathf.Clamp(Mathf.RoundToInt((xs[k + 1] - rect.x) / rect.width * size), 0, size - 1);
                    int row = y * size;
                    for (int x = xa; x <= xb; x++) px[row + x] = color;
                }
            }
        }

        static void DrawLine(Color32[] px, int size, Vector2 a, Vector2 b, Rect rect, float width, Color32 color)
        {
            Vector2 pa = new Vector2((a.x - rect.x) / rect.width * size, (a.y - rect.y) / rect.height * size);
            Vector2 pb = new Vector2((b.x - rect.x) / rect.width * size, (b.y - rect.y) / rect.height * size);

            float len = Vector2.Distance(pa, pb);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len));
            int r = Mathf.Max(0, Mathf.RoundToInt(width * 0.5f));

            for (int i = 0; i <= steps; i++)
            {
                Vector2 p = Vector2.Lerp(pa, pb, i / (float)steps);
                int cx = Mathf.RoundToInt(p.x);
                int cy = Mathf.RoundToInt(p.y);

                for (int dy = -r; dy <= r; dy++)
                {
                    int y = cy + dy;
                    if (y < 0 || y >= size) continue;
                    int row = y * size;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int x = cx + dx;
                        if (x < 0 || x >= size) continue;
                        px[row + x] = color;
                    }
                }
            }
        }

        static void DrawDisc(Color32[] px, int size, Vector2 world, float metres, Rect rect, Color32 color)
        {
            Vector2 p = new Vector2((world.x - rect.x) / rect.width * size, (world.y - rect.y) / rect.height * size);
            int r = Mathf.Max(1, Mathf.RoundToInt(metres / rect.width * size));

            for (int dy = -r; dy <= r; dy++)
            {
                int y = Mathf.RoundToInt(p.y) + dy;
                if (y < 0 || y >= size) continue;
                for (int dx = -r; dx <= r; dx++)
                {
                    if (dx * dx + dy * dy > r * r) continue;
                    int x = Mathf.RoundToInt(p.x) + dx;
                    if (x < 0 || x >= size) continue;
                    px[y * size + x] = color;
                }
            }
        }
    }
}
