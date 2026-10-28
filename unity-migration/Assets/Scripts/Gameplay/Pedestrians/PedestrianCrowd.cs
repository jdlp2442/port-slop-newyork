using System.Collections.Generic;
using UnityEngine;
using Nyc.Core;
using Nyc.Data;

namespace Nyc.Gameplay.Pedestrians
{
    /// <summary>
    /// Sidewalk crowd rendered with one DrawMeshInstancedIndirect call.
    ///
    /// Walkers follow the perimeter of the real curb islands (GEO.blocks polygons), offset 1.9 m
    /// inward onto the sidewalk, which reproduces the original's "people walking around the block"
    /// without a navmesh. Per-person gait phase / speed / scale / tint live in a StructuredBuffer
    /// read by Nyc/Pedestrian, which swings the six body parts in the vertex shader.
    ///
    /// The set is rebuilt when the camera moves 45 m, so only blocks around the viewer are
    /// simulated (the original does the same with PED_RADIUS = 320 m).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PedestrianCrowd : MonoBehaviour
    {
        [Header("Rendering")]
        public Material material;
        public int maxWalkers = 1200;
        // Hysteresis keeps the same nearby walkers alive while a vehicle moves through the city;
        // the web crowd does not rebuild the visible pool every few metres.
        public float radius = 480f;
        public float rebuildDistance = 160f;
        public float sidewalkInset = 1.9f;

        [Header("References")]
        public Camera viewCamera;
        public ThirdPersonControllerManager controllers;

        [Header("Stats")]
        public int walkerCount;
        public int blockCount;

        struct Walker
        {
            public int Block;
            public float S;
            public float PreviousS;
            public float Dir;
            public float Speed;
            public float Phase;
            public float PreviousPhase;
            public float Scale;
            public float Seed;
            public Vector4 Tint;
        }

        struct PedInstance
        {
            public Vector4 PosHeading;
            public Vector4 Params;
            public Vector4 Tint;
        }

        static readonly Vector4[] Palette =
        {
            new Vector4(0.92f, 0.72f, 0.12f, 1f), // NYC Yellow
            new Vector4(0.78f, 0.18f, 0.16f, 1f), // Crimson red
            new Vector4(0.18f, 0.42f, 0.76f, 1f), // Royal blue
            new Vector4(0.24f, 0.52f, 0.35f, 1f), // Emerald green
            new Vector4(0.85f, 0.55f, 0.22f, 1f), // Tan/camel coat
            new Vector4(0.88f, 0.88f, 0.86f, 1f), // Light puffer
            new Vector4(0.35f, 0.28f, 0.45f, 1f), // Purple/plum
            new Vector4(0.72f, 0.68f, 0.58f, 1f), // Trench coat
            new Vector4(0.20f, 0.28f, 0.38f, 1f), // Navy jacket
            new Vector4(0.55f, 0.52f, 0.48f, 1f), // Heather grey
            new Vector4(0.16f, 0.18f, 0.20f, 1f), // Charcoal coat
            new Vector4(0.82f, 0.38f, 0.24f, 1f), // Terracotta
            new Vector4(0.28f, 0.62f, 0.68f, 1f), // Teal parka
            new Vector4(0.64f, 0.22f, 0.38f, 1f), // Berry
            new Vector4(0.48f, 0.58f, 0.32f, 1f), // Olive jacket
            new Vector4(0.32f, 0.34f, 0.38f, 1f)  // Slate
        };

        Mesh _mesh;
        Walker[] _walkers;
        PedInstance[] _data;
        ComputeBuffer _instanceBuffer;
        ComputeBuffer _argsBuffer;
        readonly int[] _args = new int[5];
        Bounds _bounds;
        Vector2 _lastBuild = new Vector2(float.MaxValue, float.MaxValue);
        bool _ready;
        float _simulationAccumulator;
        float _renderAlpha;
        const float SimulationStep = 1f / 60f;
        System.Random _rng = new System.Random(20261022);

        void Awake() => EnsureInit();

        void EnsureInit()
        {
            if (_ready) return;
            if (!material) material = new Material(Shader.Find("Nyc/Pedestrian"));
            if (!viewCamera) viewCamera = Camera.main;
            if (!controllers) controllers = FindAnyObjectByType<ThirdPersonControllerManager>();

            _mesh = BuildWalkerMesh();
            _walkers = new Walker[maxWalkers];
            _data = new PedInstance[maxWalkers];

            _instanceBuffer = new ComputeBuffer(maxWalkers, 48, ComputeBufferType.Structured);
            _argsBuffer = new ComputeBuffer(1, 5 * sizeof(int), ComputeBufferType.IndirectArguments);

            _bounds = new Bounds(Vector3.zero, Vector3.one * 2000f);
            _ready = true;
        }

        float GetEffectiveRadius(out float rebuildDist)
        {
            bool isHelicopter = controllers != null && controllers.ActiveMode == TravelMode.Helicopter;
            float camAltitude = viewCamera != null ? viewCamera.transform.position.y : 0f;
            if (isHelicopter || camAltitude > 35f)
            {
                float t = Mathf.Clamp01((camAltitude - 35f) / 120f);
                rebuildDist = Mathf.Lerp(rebuildDistance, 280f, t);
                return Mathf.Lerp(radius, 780f, t);
            }
            rebuildDist = rebuildDistance;
            return radius;
        }

        void Update()
        {
            if (!_ready) EnsureInit();
            if (!_ready || CityConfig.Geo == null) return;

            Vector2 focus = Focus();
            float effRadius = GetEffectiveRadius(out float effRebuildDist);
            if (Vector2.Distance(focus, _lastBuild) > effRebuildDist || walkerCount == 0) Rebuild(focus, effRadius);

            _simulationAccumulator += Mathf.Min(Time.deltaTime, 0.10f);
            int steps = 0;
            while (_simulationAccumulator >= SimulationStep && steps++ < 4)
            {
                Tick(SimulationStep, focus);
                _simulationAccumulator -= SimulationStep;
            }
            if (steps == 4) _simulationAccumulator = 0f;
            _renderAlpha = _simulationAccumulator / SimulationStep;
            Draw();
        }

        /// <summary>Rebuild the walker set around the current focus (tooling / teleport).</summary>
        public void RebuildNow()
        {
            EnsureInit();
            if (CityConfig.Geo != null) Rebuild(Focus(), GetEffectiveRadius(out _));
        }

        /// <summary>Traffic query used to keep vehicles from entering a busy curb/crossing.</summary>
        public bool HasWalkerNear(Vector2 sourcePoint, float radius)
        {
            if (_walkers == null || CityConfig.Geo == null) return false;
            float radiusSq = radius * radius;
            for (int i = 0; i < walkerCount; i++)
            {
                Walker w = _walkers[i];
                BlockRec block = CityConfig.Geo.Blocks[w.Block];
                SampleBlock(block, w.S, sidewalkInset, out Vector2 pos, out _);
                if ((pos - sourcePoint).sqrMagnitude <= radiusSq) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ simulation

        void Tick(float dt, Vector2 focus)
        {
            var geo = CityConfig.Geo;
            if (geo == null || geo.Blocks == null) return;
            EnsureBlockPerimeters();

            for (int i = 0; i < walkerCount; i++)
            {
                Walker w = _walkers[i];
                BlockRec block = geo.Blocks[w.Block];
                float perimeter = GetBlockPerimeter(w.Block);
                if (perimeter < 1f) continue;

                w.PreviousS = w.S;
                w.PreviousPhase = w.Phase;
                w.S += w.Dir * w.Speed * dt;
                if (w.S > perimeter) w.S -= perimeter;
                else if (w.S < 0f) w.S += perimeter;

                w.Phase += dt * (2.4f + w.Speed * 2.2f);
                _walkers[i] = w;

                SampleBlock(block, w.S, sidewalkInset, out Vector2 pos, out Vector2 tangent);
                if (w.Dir < 0f) tangent = -tangent;

                float heading = Mathf.Atan2(tangent.x, tangent.y);   // source heading
                _data[i] = new PedInstance
                {
                    PosHeading = new Vector4(pos.x, CityFrame.SidewalkHeight, -pos.y, -heading),
                    Params = new Vector4(w.Phase, w.Speed, w.Scale, w.Seed),
                    Tint = w.Tint
                };
            }
        }

        void Draw()
        {
            if (walkerCount == 0) return;

            // Build interpolated instance transforms at render rate. Without this, the fixed
            // 60 Hz crowd update becomes visibly stepped on displays running at 75/90/120 Hz.
            for (int i = 0; i < walkerCount; i++)
            {
                Walker w = _walkers[i];
                BlockRec block = CityConfig.Geo.Blocks[w.Block];
                float perimeter = GetBlockPerimeter(w.Block);
                float previousS = w.PreviousS;
                if (perimeter > 0f && Mathf.Abs(w.S - previousS) > perimeter * 0.5f)
                    previousS += w.S > previousS ? perimeter : -perimeter;
                float s = Mathf.Lerp(previousS, w.S, _renderAlpha);
                if (perimeter > 0f) { s %= perimeter; if (s < 0f) s += perimeter; }
                float phase = Mathf.Lerp(w.PreviousPhase, w.Phase, _renderAlpha);
                SampleBlock(block, s, sidewalkInset, out Vector2 pos, out Vector2 tangent);
                if (w.Dir < 0f) tangent = -tangent;
                float heading = Mathf.Atan2(tangent.x, tangent.y);
                _data[i].PosHeading = new Vector4(pos.x, CityFrame.SidewalkHeight, -pos.y, -heading);
                _data[i].Params.x = phase;
            }

            _instanceBuffer.SetData(_data, 0, 0, walkerCount);
            material.SetBuffer("_Instances", _instanceBuffer);

            _args[0] = (int)_mesh.GetIndexCount(0);
            _args[1] = walkerCount;
            _args[2] = 0;
            _args[3] = 0;
            _args[4] = 0;
            _argsBuffer.SetData(_args);
            Vector3 centre = viewCamera ? viewCamera.transform.position : Vector3.zero;
            _bounds.center = centre;
            float effR = GetEffectiveRadius(out _);
            _bounds.size = new Vector3(effR * 2.5f, 600f, effR * 2.5f);

            Graphics.DrawMeshInstancedIndirect(_mesh, 0, material, _bounds, _argsBuffer);
        }

        // ------------------------------------------------------------------ population

        void Rebuild(Vector2 focus, float curRadius)
        {
            _lastBuild = focus;
            var geo = CityConfig.Geo;
            if (geo == null || geo.Blocks.Count == 0) return;

            if (walkerCount == 0)
            {
                // Initial deterministic spawn around the spawn location
                _rng = new System.Random(20261022);
                walkerCount = 0;
                blockCount = 0;

                for (int i = 0; i < geo.Blocks.Count && walkerCount < maxWalkers; i++)
                {
                    BlockRec block = geo.Blocks[i];
                    if (block.Poly == null || block.Poly.Length < 3) continue;
                    if (Vector2.Distance(block.Center, focus) > curRadius) continue;

                    float perimeter = Perimeter(block.Poly);
                    if (perimeter < 25f) continue;

                    blockCount++;
                    int n = Mathf.Clamp(Mathf.RoundToInt(perimeter / 30f), 2, 18);

                    for (int k = 0; k < n && walkerCount < maxWalkers; k++)
                    {
                        float initS = (float)_rng.NextDouble() * perimeter;
                        float initPhase = (float)_rng.NextDouble() * Mathf.PI * 2f;
                        _walkers[walkerCount++] = new Walker
                        {
                            Block = i,
                            S = initS,
                            PreviousS = initS,
                            Dir = _rng.Next(0, 2) == 0 ? -1f : 1f,
                            Speed = 1.05f + (float)_rng.NextDouble() * 0.65f,
                            Phase = initPhase,
                            PreviousPhase = initPhase,
                            Scale = 0.92f + (float)_rng.NextDouble() * 0.16f,
                            Seed = (float)_rng.NextDouble(),
                            Tint = Palette[_rng.Next(0, Palette.Length)]
                        };
                    }
                }
                return;
            }

            // Continuous streaming: Keep existing nearby walkers walking without resetting.
            // Project active focus forward when player is in motion so sidewalks ahead are always populated.
            bool isHelicopter = controllers != null && controllers.ActiveMode == TravelMode.Helicopter;
            float camAltitude = viewCamera != null ? viewCamera.transform.position.y : 0f;
            Vector2 activeFocus = focus;
            if (controllers != null && controllers.Active != null && controllers.Active.Speed > 2.0f)
            {
                Vector2 moveDir = new Vector2(Mathf.Sin(controllers.Active.Heading), Mathf.Cos(controllers.Active.Heading));
                float maxLead = isHelicopter ? 260f : 120f;
                activeFocus += moveDir * Mathf.Min(maxLead, controllers.Active.Speed * (isHelicopter ? 4.5f : 3.0f));
            }

            var candidateBlocks = new List<int>();
            for (int i = 0; i < geo.Blocks.Count; i++)
            {
                BlockRec b = geo.Blocks[i];
                if (b.Poly == null || b.Poly.Length < 3) continue;
                float dist = Vector2.Distance(b.Center, activeFocus);
                if (dist < curRadius * 0.94f && dist > 30f) candidateBlocks.Add(i);
            }

            if (candidateBlocks.Count == 0) return;

            float maxDistSq = (curRadius * 1.25f) * (curRadius * 1.25f);
            for (int w = 0; w < walkerCount; w++)
            {
                BlockRec currentBlock = geo.Blocks[_walkers[w].Block];
                if ((currentBlock.Center - focus).sqrMagnitude > maxDistSq)
                {
                    int targetBlockIdx = -1;
                    for (int attempt = 0; attempt < 16; attempt++)
                    {
                        int cand = candidateBlocks[_rng.Next(0, candidateBlocks.Count)];
                        BlockRec b = geo.Blocks[cand];
                        float dist = Vector2.Distance(b.Center, focus);
                        bool allowDistantRespawn = (isHelicopter || camAltitude > 35f) && dist > 360f;

                        if (!allowDistantRespawn && viewCamera != null)
                        {
                            // Test both center and bounding corners: never spawn on a block that touches camera view
                            Vector3 bWorld = CityFrame.ToUnity(b.Center.x, 0f, b.Center.y);
                            Vector3 vp = viewCamera.WorldToViewportPoint(bWorld);
                            if (vp.z > 0f && vp.x >= -0.2f && vp.x <= 1.2f && vp.y >= -0.2f && vp.y <= 1.2f)
                                continue;

                            Vector3 c0 = CityFrame.ToUnity(b.Bounds.x, 0f, b.Bounds.z);
                            Vector3 vp0 = viewCamera.WorldToViewportPoint(c0);
                            if (vp0.z > 0f && vp0.x >= -0.2f && vp0.x <= 1.2f && vp0.y >= -0.2f && vp0.y <= 1.2f)
                                continue;

                            Vector3 c1 = CityFrame.ToUnity(b.Bounds.y, 0f, b.Bounds.w);
                            Vector3 vp1 = viewCamera.WorldToViewportPoint(c1);
                            if (vp1.z > 0f && vp1.x >= -0.2f && vp1.x <= 1.2f && vp1.y >= -0.2f && vp1.y <= 1.2f)
                                continue;
                        }
                        targetBlockIdx = cand;
                        break;
                    }
                    if (targetBlockIdx < 0) continue;

                    BlockRec targetBlock = geo.Blocks[targetBlockIdx];
                    float targetPerim = Perimeter(targetBlock.Poly);
                    float newS = (float)_rng.NextDouble() * targetPerim;
                    float newPhase = (float)_rng.NextDouble() * Mathf.PI * 2f;
                    _walkers[w].Block = targetBlockIdx;
                    _walkers[w].S = newS;
                    _walkers[w].PreviousS = newS;
                    _walkers[w].Phase = newPhase;
                    _walkers[w].PreviousPhase = newPhase;
                }
            }
        }

        float[] _blockPerimeters;

        void EnsureBlockPerimeters()
        {
            var geo = CityConfig.Geo;
            if (geo == null || geo.Blocks == null) return;
            if (_blockPerimeters != null && _blockPerimeters.Length == geo.Blocks.Count) return;

            _blockPerimeters = new float[geo.Blocks.Count];
            for (int b = 0; b < geo.Blocks.Count; b++)
            {
                _blockPerimeters[b] = Perimeter(geo.Blocks[b].Poly);
            }
        }

        float GetBlockPerimeter(int blockIndex)
        {
            if (_blockPerimeters == null || blockIndex < 0 || blockIndex >= _blockPerimeters.Length)
            {
                EnsureBlockPerimeters();
                if (_blockPerimeters != null && blockIndex >= 0 && blockIndex < _blockPerimeters.Length)
                    return _blockPerimeters[blockIndex];
                if (CityConfig.Geo != null && CityConfig.Geo.Blocks != null && blockIndex >= 0 && blockIndex < CityConfig.Geo.Blocks.Count)
                    return Perimeter(CityConfig.Geo.Blocks[blockIndex].Poly);
                return 0f;
            }
            return _blockPerimeters[blockIndex];
        }

        static float Perimeter(Vector2[] poly)
        {
            float p = 0f;
            for (int i = 0; i < poly.Length; i++)
                p += Vector2.Distance(poly[i], poly[(i + 1) % poly.Length]);
            return p;
        }

        static void SampleBlock(BlockRec block, float s, float inset, out Vector2 pos, out Vector2 tangent)
        {
            Vector2[] poly = block.Poly;
            float acc = 0f;
            pos = poly[0];
            tangent = Vector2.up;

            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i];
                Vector2 b = poly[(i + 1) % poly.Length];
                float len = Vector2.Distance(a, b);
                if (len < 0.001f) continue;

                if (s <= acc + len)
                {
                    float t = (s - acc) / len;
                    pos = Vector2.Lerp(a, b, t);
                    tangent = (b - a) / len;
                    // Correct inward normal perpendicular to curb edge so pedestrians never veer into buildings
                    Vector2 normal = new Vector2(-tangent.y, tangent.x);
                    if (Vector2.Dot(normal, block.Center - pos) < 0f) normal = -normal;
                    pos += normal * inset;
                    return;
                }
                acc += len;
            }
        }

        Vector2 Focus()
        {
            if (controllers && controllers.Active != null) return controllers.Active.Position;
            if (viewCamera)
            {
                Vector3 p = viewCamera.transform.position;
                return new Vector2(p.x, -p.z);
            }
            return Vector2.zero;
        }

        void OnDestroy()
        {
            _instanceBuffer?.Release();
            _argsBuffer?.Release();
        }

        // ------------------------------------------------------------------ mesh

        static Mesh BuildWalkerMesh()
        {
            var b = new MeshBuilder();
            // Part 0: Upper Torso / broad chest & athletic shoulders (distinct silhouette from both street & aerial cameras)
            b.Limb(new Vector3(0f, 1.34f, 0f), new Vector3(0.48f, 0.28f, 0.24f), 0, 0f);
            // Part 0: Lower Torso / tapered waist
            b.Limb(new Vector3(0f, 1.07f, 0f), new Vector3(0.36f, 0.26f, 0.20f), 0, 0f);

            // Part 1: Neck connecting body and head
            b.Limb(new Vector3(0f, 1.49f, 0f), new Vector3(0.13f, 0.10f, 0.13f), 1, 0f);
            // Part 1: Cranium / face
            b.Limb(new Vector3(0f, 1.62f, 0f), new Vector3(0.20f, 0.18f, 0.21f), 1, 0f);
            // Part 1: Distinct top hair cap / crown (clearly recognizable human head circle from high drone/flight view)
            b.Limb(new Vector3(0f, 1.71f, -0.01f), new Vector3(0.22f, 0.08f, 0.23f), 1, 0f);

            // Part 2: Left Arm & Hand (swings with shoulder offset)
            b.Limb(new Vector3(-0.27f, 1.14f, 0f), new Vector3(0.11f, 0.58f, 0.11f), 2, 1.44f);
            // Part 3: Right Arm & Hand
            b.Limb(new Vector3(0.27f, 1.14f, 0f), new Vector3(0.11f, 0.58f, 0.11f), 3, 1.44f);

            // Part 4: Left Leg & Foot (clear separation between legs)
            b.Limb(new Vector3(-0.11f, 0.48f, 0f), new Vector3(0.14f, 0.80f, 0.16f), 4, 0.88f);
            b.Limb(new Vector3(-0.11f, 0.05f, 0.04f), new Vector3(0.13f, 0.10f, 0.24f), 4, 0.88f);

            // Part 5: Right Leg & Foot
            b.Limb(new Vector3(0.11f, 0.48f, 0f), new Vector3(0.14f, 0.80f, 0.16f), 5, 0.88f);
            b.Limb(new Vector3(0.11f, 0.05f, 0.04f), new Vector3(0.13f, 0.10f, 0.24f), 5, 0.88f);

            return b.ToMesh("PedestrianWalker");
        }

        sealed class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>(128);
            readonly List<Vector3> _n = new List<Vector3>(128);
            readonly List<Vector2> _uv0 = new List<Vector2>(128);
            readonly List<Vector2> _uv1 = new List<Vector2>(128);
            readonly List<int> _t = new List<int>(256);

            public void Limb(Vector3 centre, Vector3 size, int part, float pivotY)
            {
                Vector3 h = size * 0.5f;
                Vector3 min = centre - h, max = centre + h;

                Quad(new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
                     new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z), Vector3.left, part, pivotY, size.y);
                Quad(new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z),
                     new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), Vector3.right, part, pivotY, size.y);
                Quad(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                     new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z), Vector3.back, part, pivotY, size.y);
                Quad(new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
                     new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), Vector3.forward, part, pivotY, size.y);
                Quad(new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
                     new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z), Vector3.up, part, pivotY, size.y);
            }

            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, int part, float pivotY, float height)
            {
                int i0 = Add(p0, n, part, pivotY, height);
                int i1 = Add(p1, n, part, pivotY, height);
                int i2 = Add(p2, n, part, pivotY, height);
                int i3 = Add(p3, n, part, pivotY, height);

                Vector3 cross = Vector3.Cross(_v[i1] - _v[i0], _v[i2] - _v[i0]);
                if (Vector3.Dot(cross, n) >= 0f)
                {
                    _t.Add(i0); _t.Add(i1); _t.Add(i2);
                    _t.Add(i0); _t.Add(i2); _t.Add(i3);
                }
                else
                {
                    _t.Add(i0); _t.Add(i2); _t.Add(i1);
                    _t.Add(i0); _t.Add(i3); _t.Add(i2);
                }
            }

            int Add(Vector3 p, Vector3 n, int part, float pivotY, float height)
            {
                _v.Add(p);
                _n.Add(n);
                _uv0.Add(Vector2.zero);
                float t = pivotY > 0f ? Mathf.Clamp01((pivotY - p.y) / Mathf.Max(0.01f, height)) : 0f;
                _uv1.Add(new Vector2(part, t));
                return _v.Count - 1;
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv0);
                mesh.SetUVs(1, _uv1);
                mesh.SetTriangles(_t, 0, false);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
