using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Nyc.Core;
using Nyc.Data;

namespace Nyc.Generation
{
    /// <summary>
    /// Builds the road-marking mesh consumed by Nyc/RoadPaint.
    ///
    /// Every strip is a continuous ribbon; the shader cuts the dashes with a box filter so they
    /// anti-alias correctly at any distance. Vertex contract:
    ///   uv0 = (metres along the strip, 0)
    ///   uv1 = (pattern period, duty 0..1)
    ///   uv2 = (offset from the strip centre to this edge, in metres: x, z)
    ///   color = paint colour
    ///
    /// Contents: double-yellow centre lines on two-way avenues, dashed white lane dividers on
    /// multi-lane one-way streets, and continental crosswalk bars at every road-graph link end.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class RoadMarkingBuilder : MonoBehaviour
    {
        [Header("Paint")]
        public Material paintMaterial;
        public Color whitePaint = new Color(0.86f, 0.86f, 0.84f);
        public Color yellowPaint = new Color(0.85f, 0.68f, 0.18f);
        public Color bikePaint = new Color(0.18f, 0.45f, 0.30f);

        [Header("Geometry")]
        public float lineWidth = 0.14f;
        public float dashPeriod = 9f;
        [Range(0.05f, 1f)] public float dashDuty = 0.35f;
        public float surfaceY = 0.045f;
        public float crosswalkBarWidth = 0.55f;
        public float crosswalkBarLength = 2.6f;
        public float crosswalkGap = 1.2f;
        public bool buildCentreLines = true;
        public bool buildLaneLines = true;
        public bool buildCrosswalks = true;

        [Header("Stats")]
        public int stripCount;
        public int vertexCount;

        readonly List<Vector3> _pos = new List<Vector3>(1 << 16);
        readonly List<Vector3> _nrm = new List<Vector3>(1 << 16);
        readonly List<Vector2> _uv0 = new List<Vector2>(1 << 16);
        readonly List<Vector2> _uv1 = new List<Vector2>(1 << 16);
        readonly List<Vector2> _uv2 = new List<Vector2>(1 << 16);
        readonly List<Color32> _col = new List<Color32>(1 << 16);
        readonly List<int> _tris = new List<int>(1 << 17);

        [ContextMenu("Build Road Markings")]
        public void Build()
        {
            if (!CityConfig.Ready)
            {
                Debug.LogWarning("[NYC] CityConfig is not initialised; run the Manhattan bake first.");
                return;
            }

            _pos.Clear(); _nrm.Clear(); _uv0.Clear(); _uv1.Clear(); _uv2.Clear(); _col.Clear(); _tris.Clear();
            stripCount = 0;

            var geo = CityConfig.Geo;
            BuildRoadLines(geo);
            if (buildCrosswalks) BuildCrosswalks(geo);

            var mesh = new Mesh { name = "NYC_RoadMarkings" };
            if (_pos.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_pos);
            mesh.SetNormals(_nrm);
            mesh.SetUVs(0, _uv0);
            mesh.SetUVs(1, _uv1);
            mesh.SetUVs(2, _uv2);
            mesh.SetColors(_col);
            mesh.SetTriangles(_tris, 0, false);
            mesh.RecalculateBounds();

            var mf = GetComponent<MeshFilter>();
            mf.sharedMesh = mesh;

            var mr = GetComponent<MeshRenderer>();
            if (paintMaterial) mr.sharedMaterial = paintMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;

            vertexCount = _pos.Count;
            Debug.Log($"[NYC] Road markings: {stripCount} strips, {vertexCount:N0} verts.");
        }

        // ------------------------------------------------------------------ lane lines

        void BuildRoadLines(NycGeoData geo)
        {
            for (int i = 0; i < geo.Roads.Count; i++)
            {
                RoadRec r = geo.Roads[i];
                if (r.Pts == null || r.Pts.Length < 2 || r.Width < 5f) continue;

                float half = r.Width * 0.5f;

                if (r.Dir == 0 && buildCentreLines)
                {
                    // Double yellow: two solid 12 cm lines 24 cm apart.
                    EmitStrip(r.Pts, -0.12f, 0.12f, 1000f, 1f, yellowPaint);
                    EmitStrip(r.Pts, +0.12f, 0.12f, 1000f, 1f, yellowPaint);
                }

                if (buildLaneLines && r.Lanes >= 2)
                {
                    float laneW = r.Width / r.Lanes;
                    for (int lane = 1; lane < r.Lanes; lane++)
                    {
                        float offset = -half + lane * laneW;
                        if (r.Dir == 0 && Mathf.Abs(offset) < 0.5f) continue; // centre already yellow
                        EmitStrip(r.Pts, offset, lineWidth, dashPeriod, dashDuty, whitePaint);
                    }
                }
            }
        }

        /// <summary>Ribbon of `width` metres, offset `lateral` metres right of the centreline.</summary>
        void EmitStrip(Vector2[] pts, float lateral, float width, float period, float duty, Color color)
        {
            float travelled = 0f;
            for (int i = 0; i + 1 < pts.Length; i++)
            {
                Vector2 a = pts[i], b = pts[i + 1];
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 0.5f) continue;
                d /= len;

                var n = new Vector2(d.y, -d.x);
                Vector2 ca = a + n * lateral;
                Vector2 cb = b + n * lateral;
                Vector2 half = n * (width * 0.5f);

                AddQuad(ca - half, ca + half, cb + half, cb - half,
                        -half, half, travelled, travelled + len, period, duty, color);
                travelled += len;
            }
            stripCount++;
        }

        // ------------------------------------------------------------------ crosswalks

        void BuildCrosswalks(NycGeoData geo)
        {
            if (geo.RoadGraph == null) return;

            var nodes = geo.RoadGraph.Nodes;
            for (int i = 0; i < geo.RoadGraph.Links.Count; i++)
            {
                RoadLink link = geo.RoadGraph.Links[i];
                if (link.Width < 6f) continue;
                if (link.A < 0 || link.A >= nodes.Length || link.B < 0 || link.B >= nodes.Length) continue;

                Vector2 a = nodes[link.A];
                Vector2 b = nodes[link.B];
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len < 6f) continue;
                d /= len;

                var n = new Vector2(d.y, -d.x);
                float half = link.Width * 0.5f;
                Vector2 centre = a + d * (half + 2.2f);

                int bars = Mathf.Max(4, Mathf.FloorToInt((link.Width - 1f) / (crosswalkBarWidth + crosswalkGap)));
                float span = bars * (crosswalkBarWidth + crosswalkGap) - crosswalkGap;
                float start = -span * 0.5f;

                for (int k = 0; k < bars; k++)
                {
                    float o = start + k * (crosswalkBarWidth + crosswalkGap);
                    Vector2 barCentre = centre + n * o;
                    Vector2 along = d * (crosswalkBarLength * 0.5f);
                    Vector2 across = n * (crosswalkBarWidth * 0.5f);

                    AddQuad(barCentre - along - across, barCentre - along + across,
                            barCentre + along + across, barCentre + along - across,
                            -across, across, 0f, crosswalkBarLength, 1000f, 1f, whitePaint);
                }
                stripCount++;
            }
        }

        // ------------------------------------------------------------------ primitives

        void AddQuad(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3,
                     Vector2 side0, Vector2 side1,
                     float along0, float along1, float period, float duty, Color color)
        {
            int baseIndex = _pos.Count;
            Color32 c = color;

            AddVert(p0, side0, along0, period, duty, c);
            AddVert(p1, side1, along0, period, duty, c);
            AddVert(p2, side1, along1, period, duty, c);
            AddVert(p3, side0, along1, period, duty, c);

            // Unity front face: clockwise seen from +Y.
            _tris.Add(baseIndex); _tris.Add(baseIndex + 2); _tris.Add(baseIndex + 1);
            _tris.Add(baseIndex); _tris.Add(baseIndex + 3); _tris.Add(baseIndex + 2);
        }

        void AddVert(Vector2 p, Vector2 side, float along, float period, float duty, Color32 c)
        {
            _pos.Add(CityFrame.ToUnity(p.x, surfaceY, p.y));
            _nrm.Add(Vector3.up);
            _uv0.Add(new Vector2(along, 0f));
            _uv1.Add(new Vector2(period, duty));
            // side is authored in the source frame; Unity's z is negated.
            _uv2.Add(new Vector2(side.x, -side.y));
            _col.Add(c);
        }
    }
}
