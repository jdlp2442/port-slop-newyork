using System.Collections.Generic;
using UnityEngine;
using Nyc.Core;

namespace Nyc.Gameplay.Traffic
{
    /// <summary>
    /// 200 cars + 32 buses on the lane graph, following the Intelligent Driver Model.
    ///
    /// Port of traffic.js (the car-following core): CAR_DRIVER {a 2.0, b 3.0, T 1.2, s0 2.0},
    /// BUS_DRIVER {a 1.1, b 2.5, T 1.5, s0 2.5}, LOOKAHEAD 70 m, spawn 100–300 m out of view,
    /// despawn beyond 1 200 m, buses keep to avenues.
    ///
    /// Simplified versus the original: no lane changes, no crosswalk yielding, no merge claims.
    /// Those need the per-lane occupancy sets of traffic.js and are listed in PHASE5.md.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrafficSystem : MonoBehaviour
    {
        [Header("References")]
        public RoadNetwork network;
        public TrafficRenderer renderer;
        public ThirdPersonControllerManager controllers;
        public Camera viewCamera;

        [Header("Counts")]
        public int cars = 200;
        public int buses = 32;

        [Header("Behaviour")]
        public float lookAhead = 70f;
        public float spawnMin = 100f;
        public float spawnMax = 300f;
        public float despawnRadius = 1200f;
        public float nearRadius = 350f;
        public float maxAccel = 9f;

        [Header("Stats")]
        public int activeCount;
        public int spawnFailures;

        public struct Agent
        {
            public int Lane;
            public float S;
            public float Speed;
            public float Accel;
            public float HalfLen;
            public float HalfWidth;
            public bool Bus;
            public float V0;
            public int NextLane;
            public Vector2 Position;
            public float Y;
            public float Heading;
            public bool Active;
            public int Batch;
        }

        struct Driver
        {
            public float a, b, T, s0, aLat;
        }

        static readonly Driver CarDriver = new Driver { a = 2.0f, b = 3.0f, T = 1.2f, s0 = 2.0f, aLat = 3.5f };
        static readonly Driver TaxiDriver = new Driver { a = 2.4f, b = 3.2f, T = 1.1f, s0 = 1.8f, aLat = 3.8f };
        static readonly Driver PoliceDriver = new Driver { a = 2.8f, b = 3.5f, T = 1.0f, s0 = 1.6f, aLat = 4.0f };
        static readonly Driver TruckDriver = new Driver { a = 1.3f, b = 2.6f, T = 1.6f, s0 = 2.8f, aLat = 2.2f };
        static readonly Driver BusDriver = new Driver { a = 1.1f, b = 2.5f, T = 1.5f, s0 = 2.5f, aLat = 2.0f };

        static Driver DriverForBatch(int batch)
        {
            switch (batch)
            {
                case 0: return TaxiDriver;
                case 3: return BusDriver;
                case 4: return PoliceDriver;
                case 5: return TruckDriver;
                default: return CarDriver;
            }
        }

        Agent[] _agents = System.Array.Empty<Agent>();
        readonly Dictionary<int, int> _laneHead = new Dictionary<int, int>(512);
        int[] _nextInLane = System.Array.Empty<int>();
        readonly List<VehicleInstance>[] _batches =
        {
            new List<VehicleInstance>(128), // 0: Taxi
            new List<VehicleInstance>(128), // 1: Sedan
            new List<VehicleInstance>(128), // 2: SUV
            new List<VehicleInstance>(64),  // 3: Bus
            new List<VehicleInstance>(64),  // 4: Police
            new List<VehicleInstance>(64)   // 5: Truck
        };

        float _time;
        float _spawnTimer;
        Vector2 _spawnFocus = new Vector2(float.MaxValue, float.MaxValue);
        readonly List<int> _spawnLanes = new List<int>(4096);
        System.Random _rng = new System.Random(10019);

        public int Capacity => _agents.Length;

        /// <summary>Live agents (read-only use: HUD, minimap, debugging).</summary>
        public Agent[] Agents => _agents;

        // ------------------------------------------------------------------ lifecycle

        public void Build()
        {
            if (!network) network = FindFirstObjectByType<RoadNetwork>();
            if (!renderer) renderer = FindFirstObjectByType<TrafficRenderer>();
            if (!controllers) controllers = FindFirstObjectByType<ThirdPersonControllerManager>();
            if (!viewCamera) viewCamera = Camera.main;

            if (network == null || network.Lanes.Length == 0)
            {
                Debug.LogWarning("[NYC] RoadNetwork is empty — run NYC/8. Build Traffic Network first.");
                return;
            }

            int total = Mathf.Max(1, cars + buses);
            _agents = new Agent[total];
            _nextInLane = new int[total];

            for (int i = 0; i < total; i++)
            {
                bool bus = i >= cars;
                int batch = 1;
                float halfLen = 2.25f;
                float halfWidth = 0.92f;
                float v0 = 13.4f;

                if (bus)
                {
                    batch = 3;
                    halfLen = 5.8f;
                    halfWidth = 1.28f;
                    v0 = 11.0f;
                }
                else
                {
                    int roll = _rng.Next(0, 100);
                    if (roll < 35) // 35% Yellow Taxis
                    {
                        batch = 0;
                        halfLen = 2.25f;
                        halfWidth = 0.92f;
                        v0 = 14.5f;
                    }
                    else if (roll < 65) // 30% Sedans
                    {
                        batch = 1;
                        halfLen = 2.25f;
                        halfWidth = 0.92f;
                        v0 = 13.4f;
                    }
                    else if (roll < 82) // 17% SUVs
                    {
                        batch = 2;
                        halfLen = 2.45f;
                        halfWidth = 0.98f;
                        v0 = 13.0f;
                    }
                    else if (roll < 92) // 10% Delivery Box Trucks
                    {
                        batch = 5;
                        halfLen = 3.65f;
                        halfWidth = 1.15f;
                        v0 = 11.5f;
                    }
                    else // 8% NYPD Patrol Cars
                    {
                        batch = 4;
                        halfLen = 2.28f;
                        halfWidth = 0.93f;
                        v0 = 15.5f;
                    }
                }

                _agents[i] = new Agent
                {
                    Bus = bus,
                    Batch = batch,
                    HalfLen = halfLen,
                    HalfWidth = halfWidth,
                    V0 = v0,
                    NextLane = -1,
                    Active = false
                };
            }

            Vector2 focus = Focus();
            RebuildSpawnCandidates(focus);
            for (int i = 0; i < total; i++) Spawn(ref _agents[i], focus, 0f, nearRadius, false);
            activeCount = CountActive();
        }

        void Update()
        {
            if (_agents.Length == 0 || network == null) return;
            Tick(Time.deltaTime);
        }

        public void Tick(float dt)
        {
            _time += dt;
            Vector2 focus = Focus();
            RebuildSpawnCandidates(focus);

            BuildLaneHeads();

            for (int i = 0; i < _agents.Length; i++)
            {
                if (!_agents[i].Active) continue;
                Step(ref _agents[i], i, dt, focus);
            }

            _spawnTimer += dt;
            if (_spawnTimer > 0.25f)
            {
                _spawnTimer = 0f;
                MaintainPopulation(focus);
            }

            Render();
            activeCount = CountActive();
        }

        // ------------------------------------------------------------------ IDM

        void Step(ref Agent v, int index, float dt, Vector2 focus)
        {
            RoadNetwork.Lane lane = network.Lanes[v.Lane];
            Driver d = DriverForBatch(v.Batch);

            float freeAcc = d.a * (1f - Mathf.Pow(v.Speed / Mathf.Max(0.1f, v.V0), 4f));
            float worst = 0f;

            // Leader in the same lane.
            float gap = float.MaxValue;
            float leaderSpeed = 0f;
            int head = _laneHead.TryGetValue(v.Lane, out int h) ? h : -1;
            for (int j = head; j >= 0; j = _nextInLane[j])
            {
                if (j == index) continue;
                float ds = _agents[j].S - v.S;
                if (ds <= 0f) continue;
                float g = ds - _agents[j].HalfLen - v.HalfLen;
                if (g < gap) { gap = g; leaderSpeed = _agents[j].Speed; }
            }
            if (gap < lookAhead) worst = Mathf.Min(worst, Brake(v, gap, leaderSpeed, d));

            // Stop line and signal at the end of the lane.
            float distanceToEnd = lane.Length - v.S - v.HalfLen;
            if (distanceToEnd < lookAhead)
            {
                if (v.NextLane < 0) v.NextLane = ChooseNext(v.Lane, v.S, lane.Length);

                if (v.NextLane >= 0)
                {
                    RoadNetwork.Lane next = network.Lanes[v.NextLane];
                    float nextGap = NextLaneGap(v.NextLane, distanceToEnd, v.HalfLen, out float nextSpeed);
                    if (nextGap < lookAhead) worst = Mathf.Min(worst, Brake(v, nextGap, nextSpeed, d));
                }

                int phase = CityConfig.SignalPhase(_time, lane.IsAvenue);
                bool green = phase == 0;
                bool amber = phase == 1;
                bool canStop = freeAcc + StopAt(v, distanceToEnd, 1f, d) > -d.b;
                if (!green && !(amber && !canStop))
                    worst = Mathf.Min(worst, StopAt(v, distanceToEnd - 0.5f, 1f, d));
            }

            // The player counts as a leader (excluding helicopters and mismatched vertical levels e.g. viaducts)
            if (controllers && controllers.Active != null && controllers.ActiveMode != TravelMode.Helicopter)
            {
                float playerH = controllers.Active.Height;
                if (Mathf.Abs(playerH - v.Y) < 2.2f)
                {
                    Vector2 player = controllers.Active.Position;
                    if (Vector2.Distance(player, v.Position) < 60f)
                    {
                        float ps = network.Project(v.Lane, player);
                        float ds = ps - v.S;
                        if (ds > 0f && ds < lookAhead)
                        {
                            network.Sample(v.Lane, ps, out Vector2 lanePoint, out _);
                            if (Vector2.Distance(lanePoint, player) < 2.4f + v.HalfWidth)
                                worst = Mathf.Min(worst, Brake(v, ds - v.HalfLen - 0.6f, 0f, d));
                        }
                    }
                }
            }

            float acc = Mathf.Clamp(freeAcc + worst, -9f, d.a);
            v.Accel = acc;
            v.Speed = Mathf.Max(0f, v.Speed + acc * dt);
            v.Speed = Mathf.Min(v.Speed, v.V0 * 1.25f);
            v.S += v.Speed * dt;

            if (v.S > lane.Length - v.HalfLen)
            {
                if (v.NextLane >= 0)
                {
                    v.S -= lane.Length;
                    v.Lane = v.NextLane;
                    v.NextLane = -1;
                    if (v.S < 0f) v.S = 0f;
                }
                else
                {
                    v.S = Mathf.Max(v.HalfLen + 1f, lane.Length - v.HalfLen - 0.5f);
                    v.Speed *= 0.5f;
                }
            }

            network.Sample(v.Lane, v.S, out Vector2 pos, out Vector2 dir);
            v.Position = pos;
            v.Heading = Mathf.Atan2(dir.x, dir.y);

            if (Vector2.Distance(pos, focus) > despawnRadius) Spawn(ref v, focus, spawnMin, spawnMax, true);
        }

        float NextLaneGap(int lane, float distanceToEnd, float halfLen, out float leaderSpeed)
        {
            leaderSpeed = 0f;
            int head = _laneHead.TryGetValue(lane, out int h) ? h : -1;
            float best = float.MaxValue;

            for (int j = head; j >= 0; j = _nextInLane[j])
            {
                Agent o = _agents[j];
                if (o.S > 45f) continue;
                float g = distanceToEnd + o.S - o.HalfLen - halfLen;
                if (g < best) { best = g; leaderSpeed = o.Speed; }
            }
            return best;
        }

        float Brake(Agent v, float gap, float leaderSpeed, Driver d)
        {
            float desired = d.s0 + Mathf.Max(0f,
                v.Speed * d.T + (v.Speed * (v.Speed - leaderSpeed)) / (2f * Mathf.Sqrt(d.a * d.b)));
            return -d.a * Mathf.Pow(desired / Mathf.Max(gap, 0.1f), 2f);
        }

        float StopAt(Agent v, float distance, float s0, Driver d)
        {
            float desired = s0;
            return -d.a * Mathf.Pow(desired / Mathf.Max(distance, 0.1f), 2f);
        }

        int ChooseNext(int lane, float s, float length)
        {
            int[] options = network.Next[lane];
            if (options == null || options.Length == 0) return -1;

            network.Sample(lane, length - 1f, out _, out Vector2 dirEnd);

            float total = 0f;
            var weights = new float[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                network.Sample(options[i], 1f, out _, out Vector2 dirNext);
                bool straight = Vector2.Dot(dirEnd, dirNext) > 0.7f;
                weights[i] = straight ? 3f : 1f;
                total += weights[i];
            }

            float r = (float)_rng.NextDouble() * total;
            for (int i = 0; i < options.Length; i++)
            {
                r -= weights[i];
                if (r <= 0f) return options[i];
            }
            return options[options.Length - 1];
        }

        // ------------------------------------------------------------------ population

        void BuildLaneHeads()
        {
            _laneHead.Clear();
            for (int i = 0; i < _agents.Length; i++)
            {
                if (!_agents[i].Active) continue;
                int lane = _agents[i].Lane;
                _nextInLane[i] = _laneHead.TryGetValue(lane, out int head) ? head : -1;
                _laneHead[lane] = i;
            }
        }

        void MaintainPopulation(Vector2 focus)
        {
            int active = 0;
            for (int i = 0; i < _agents.Length; i++) if (_agents[i].Active) active++;
            if (active >= _agents.Length) return;

            int budget = Mathf.Min(4, _agents.Length - active);
            for (int b = 0; b < budget; b++)
            {
                for (int i = 0; i < _agents.Length; i++)
                {
                    if (_agents[i].Active) continue;
                    if (!Spawn(ref _agents[i], focus, spawnMin, spawnMax, true)) spawnFailures++;
                    break;
                }
            }
        }

        void RebuildSpawnCandidates(Vector2 focus)
        {
            if (Vector2.Distance(focus, _spawnFocus) < 200f && _spawnLanes.Count > 0) return;
            _spawnFocus = focus;
            _spawnLanes.Clear();

            float reach = nearRadius * 1.25f;
            for (int i = 0; i < network.Lanes.Length; i++)
            {
                Vector2 a = network.Lanes[i].Points[0];
                Vector2 b = network.Lanes[i].Points[network.Lanes[i].Points.Length - 1];
                if (Vector2.Distance(a, focus) < reach || Vector2.Distance(b, focus) < reach)
                    _spawnLanes.Add(i);
            }
        }

        bool Spawn(ref Agent v, Vector2 focus, float rMin, float rMax, bool hidden)
        {
            if (_spawnLanes.Count == 0) return false;

            for (int attempt = 0; attempt < 16; attempt++)
            {
                int laneId = _spawnLanes[_rng.Next(0, _spawnLanes.Count)];
                RoadNetwork.Lane lane = network.Lanes[laneId];

                if (v.Bus && !lane.IsAvenue) continue;
                if (lane.Length < 2f * v.HalfLen + 10f) continue;

                float s = v.HalfLen + 5f + (float)_rng.NextDouble() * (lane.Length - 2f * v.HalfLen - 10f);
                network.Sample(laneId, s, out Vector2 pos, out Vector2 dir);

                float d = Vector2.Distance(pos, focus);
                if (d < rMin || d > rMax) continue;
                if (hidden && Visible(pos)) continue;
                if (controllers && controllers.Active != null &&
                    Vector2.Distance(pos, controllers.Active.Position) < 25f) continue;

                // Keep a gap to whoever is already there (works during the initial spawn too).
                bool occupied = false;
                for (int j = 0; j < _agents.Length; j++)
                {
                    if (!_agents[j].Active || _agents[j].Lane != laneId) continue;
                    if (Mathf.Abs(_agents[j].S - s) < v.HalfLen + _agents[j].HalfLen + 8f) { occupied = true; break; }
                }
                if (occupied) continue;

                v.Lane = laneId;
                v.S = s;
                v.Speed = Mathf.Min(v.V0 * 0.6f, lane.SpeedLimit * 0.6f);
                v.NextLane = -1;
                v.Active = true;
                v.Position = pos;
                v.Heading = Mathf.Atan2(dir.x, dir.y);
                return true;
            }
            return false;
        }

        bool Visible(Vector2 p)
        {
            if (!viewCamera) return false;
            Vector3 world = CityFrame.ToUnity(p.x, 1.5f, p.y);
            Vector3 delta = world - viewCamera.transform.position;
            float dist = delta.magnitude;
            if (dist < 1f) return true;
            return Vector3.Dot(viewCamera.transform.forward, delta / dist) > 0.45f && dist < 420f;
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

        int CountActive()
        {
            int n = 0;
            for (int i = 0; i < _agents.Length; i++) if (_agents[i].Active) n++;
            return n;
        }

        // ------------------------------------------------------------------ render

        void Render()
        {
            for (int b = 0; b < _batches.Length; b++) _batches[b].Clear();

            for (int i = 0; i < _agents.Length; i++)
            {
                Agent a = _agents[i];
                if (!a.Active) continue;

                int batch = Mathf.Clamp(a.Batch, 0, _batches.Length - 1);
                _batches[batch].Add(new VehicleInstance
                {
                    Position = CityFrame.ToUnity(a.Position.x, a.Y, a.Position.y),
                    Rotation = CityFrame.HeadingToRotation(a.Heading),
                    Scale = 1f
                });
            }

            if (renderer) renderer.Render(_batches);
        }
    }
}
