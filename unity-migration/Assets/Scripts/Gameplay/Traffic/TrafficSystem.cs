using System.Collections.Generic;
using UnityEngine;
using Nyc.Core;
using Nyc.Gameplay.Pedestrians;

namespace Nyc.Gameplay.Traffic
{
    /// <summary>
    /// 200 cars + 32 buses on the lane graph, following the Intelligent Driver Model.
    ///
    /// Port of traffic.js (the car-following core): CAR_DRIVER {a 2.0, b 3.0, T 1.2, s0 2.0},
    /// BUS_DRIVER {a 1.1, b 2.5, T 1.5, s0 2.5}, LOOKAHEAD 70 m, spawn 100–300 m out of view,
    /// despawn beyond 1 200 m, buses keep to avenues.
    ///
    /// The Unity port keeps the original fallback renderer while progressively matching traffic.js
    /// with fixed-step motion, lane changes, crosswalk yielding and conservative turn priority.
    /// Merge reservations are represented by a deterministic per-tick outgoing-lane claim set;
    /// richer intersection precedence remains part of the parity fixtures.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrafficSystem : MonoBehaviour
    {
        [Header("References")]
        public RoadNetwork network;
        public TrafficRenderer renderer;
        public PedestrianCrowd pedestrianCrowd;
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
        [Tooltip("Fixed simulation step prevents visible hitching when a render frame stalls.")]
        public float simulationStep = 1f / 60f;

        [Header("Stats")]
        public int activeCount;
        public int spawnFailures;

        /// <summary>
        /// Global event fired upon high-speed vehicular crashes (impact position in Unity world coords, closing speed in m/s).
        /// </summary>
        public static System.Action<Vector3, float> OnVehicleCrashExplosion;

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
            public int NextTurn; // 0 straight, 1 left, 2 right
            public Vector2 Position;
            public float Y;
            public float Heading;
            public bool Active;
            public int Batch;
            public float LaneChangeCooldown;
            public int SpawnId;
            public int RespawnCount;
            public float Pitch;
            public float Roll;
            public float Heave;
            public float LateralOffset;
            public float TransitionT;      // 0..1 transition along junction curve
            public int TransitionFromLane; // lane vehicle was exiting from
        }

        public struct LaneQueryPoint
        {
            public float u;
            public float v;
        }

        public struct PlayerQuerySnapshot
        {
            public float x, y, z;
            public float vx, vz;
            public string mode;

            public static PlayerQuerySnapshot FromEntity(Vector3 pos, float heading, float speed, string mode)
            {
                if (mode == "helicopter") return default;
                return new PlayerQuerySnapshot
                {
                    x = pos.x,
                    y = pos.y,
                    z = pos.z,
                    vx = Mathf.Sin(heading) * speed,
                    vz = Mathf.Cos(heading) * speed,
                    mode = mode
                };
            }
        }

        public static bool TryPlayerInLane(Vector3[] P, float[] cum, PlayerQuerySnapshot pl, out LaneQueryPoint result)
        {
            result = default;
            if (pl.mode == "helicopter" || P == null || cum == null) return false;
            int n = P.Length;
            for (int i = 0; i < n - 1; i++)
            {
                float ax = P[i].x, az = P[i].z;
                if (P[i].y < -0.5f) continue; // underground lanes are not occupied by surface players
                float sx = P[i + 1].x - ax;
                float sz = P[i + 1].z - az;
                float horizontalLengthSq = sx * sx + sz * sz;
                if (horizontalLengthSq == 0f) continue;
                float L = cum[i + 1] - cum[i];
                float t = ((pl.x - ax) * sx + (pl.z - az) * sz) / horizontalLengthSq;
                if (t < 0f || t > 1f) continue;
                float y = P[i].y + (P[i + 1].y - P[i].y) * t;
                if (Mathf.Abs(pl.y - y) >= 2.6f) continue;
                if (Mathf.Abs((pl.z - az) * sx - (pl.x - ax) * sz) / Mathf.Sqrt(horizontalLengthSq) < 2.0f)
                {
                    result = new LaneQueryPoint
                    {
                        u = cum[i] + t * L,
                        v = Mathf.Max(0f, ((pl.vx * sx + pl.vz * sz) / horizontalLengthSq) * L)
                    };
                    return true;
                }
            }
            return false;
        }

        [System.Serializable]
        public struct TrafficSnapshot
        {
            public int frame;
            public float time;
            public int activeAgents;
            public int stoppedAgents;
            public float averageSpeed;
            public float maxSpeed;
            public ulong stateHash;
            public AgentSnapshot[] agents;
        }

        [System.Serializable]
        public struct AgentSnapshot
        {
            public int index;
            public int lane;
            public float s;
            public float speed;
            public float accel;
            public Vector2 position;
            public float heading;
            public int batch;
        }

        public TrafficSnapshot CaptureSnapshot(int frame = 0)
        {
            int active = 0;
            int stopped = 0;
            float sumSpeed = 0f;
            float maxSpd = 0f;
            ulong hash = 14695981039346656037UL;

            var list = new List<AgentSnapshot>(_agents.Length);
            for (int i = 0; i < _agents.Length; i++)
            {
                if (!_agents[i].Active) continue;
                active++;
                float spd = _agents[i].Speed;
                sumSpeed += spd;
                if (spd < 0.5f) stopped++;
                if (spd > maxSpd) maxSpd = spd;

                hash ^= (ulong)i; hash *= 1099511628211UL;
                hash ^= (ulong)_agents[i].Lane; hash *= 1099511628211UL;
                hash ^= (ulong)Mathf.RoundToInt(_agents[i].S * 10f); hash *= 1099511628211UL;
                hash ^= (ulong)Mathf.RoundToInt(spd * 100f); hash *= 1099511628211UL;

                list.Add(new AgentSnapshot
                {
                    index = i,
                    lane = _agents[i].Lane,
                    s = _agents[i].S,
                    speed = _agents[i].Speed,
                    accel = _agents[i].Accel,
                    position = _agents[i].Position,
                    heading = _agents[i].Heading,
                    batch = _agents[i].Batch
                });
            }

            return new TrafficSnapshot
            {
                frame = frame,
                time = _time,
                activeAgents = active,
                stoppedAgents = stopped,
                averageSpeed = active > 0 ? sumSpeed / active : 0f,
                maxSpeed = maxSpd,
                stateHash = hash,
                agents = list.ToArray()
            };
        }

        public void ResetDeterministic(int seed = 10019)
        {
            _rng = new System.Random(seed);
            _time = 0f;
            _spawnTimer = 0f;
            _spawnFocus = new Vector2(float.MaxValue, float.MaxValue);
            Build();
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
        // One deterministic reservation per outgoing lane during a fixed simulation tick.
        // This mirrors traffic.js merge claims while keeping the existing fallback path intact.
        readonly HashSet<int> _claimedNextLanes = new HashSet<int>();
        int[] _nextInLane = System.Array.Empty<int>();
        // Active agents grouped by lane (stable insertion sort keeps ascending index order
        // inside each lane), so ResolveVehicleContacts visits the same (i, j) pairs in the
        // same order as the original full O(n^2) scan while only comparing same-lane hulls.
        int[] _activeOrder = System.Array.Empty<int>();
        int _activeCount;
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
        float _simulationAccumulator;
        float _spawnTimer;
        Vector2 _spawnFocus = new Vector2(float.MaxValue, float.MaxValue);
        readonly List<int> _spawnLanes = new List<int>(4096);
        System.Random _rng = new System.Random(10019);

        public int Capacity => _agents.Length;

        /// <summary>Live agents (read-only use: HUD, minimap, debugging).</summary>
        public Agent[] Agents => _agents;

        /// <summary>
        /// Deterministic connection preview used by parity fixtures. The roll is normalized to
        /// [0,1] and does not advance the live traffic RNG or mutate an agent.
        /// </summary>
        public int PreviewNextLane(int lane, float roll01, out int turn)
        {
            turn = 0;
            if (network == null || network.Lanes == null || lane < 0 || lane >= network.Lanes.Length)
                return -1;
            return ChooseNext(lane, 0f, network.Lanes[lane].Length, out turn, Mathf.Clamp01(roll01));
        }

        // ------------------------------------------------------------------ lifecycle

        public void Build()
        {
            if (!network) network = FindAnyObjectByType<RoadNetwork>();
            if (!renderer) renderer = FindAnyObjectByType<TrafficRenderer>();
            if (!pedestrianCrowd) pedestrianCrowd = FindAnyObjectByType<PedestrianCrowd>();
            if (!controllers) controllers = FindAnyObjectByType<ThirdPersonControllerManager>();
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
                    SpawnId = i,
                    RespawnCount = 0,
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
            if (_agents == null || _agents.Length == 0 || network == null || network.Lanes == null || network.Lanes.Length == 0)
            {
                if (network != null && (network.Lanes == null || network.Lanes.Length == 0) && CityConfig.Geo != null)
                {
                    network.Build(CityConfig.Geo);
                }
                if (network != null && network.Lanes != null && network.Lanes.Length > 0)
                {
                    Build();
                }
                if (_agents == null || _agents.Length == 0 || network == null || network.Lanes == null || network.Lanes.Length == 0) return;
            }
            float step = Mathf.Max(0.001f, simulationStep);
            _simulationAccumulator += Mathf.Min(Time.deltaTime, 0.10f);
            int steps = 0;
            while (_simulationAccumulator >= step && steps++ < 4)
            {
                Tick(step);
                _simulationAccumulator -= step;
            }
            if (steps == 4) _simulationAccumulator = 0f;
        }

        void LateUpdate()
        {
            // Always submit instanced vehicle rendering in LateUpdate so cars are drawn
            // every single frame without skipping or flickering when deltaTime varies.
            Render();
        }

        public void Tick(float dt)
        {
            _time += dt;
            Vector2 focus = Focus();
            RebuildSpawnCandidates(focus);

            BuildLaneHeads();
            _claimedNextLanes.Clear();

            for (int i = 0; i < _agents.Length; i++)
            {
                if (!_agents[i].Active) continue;
                Step(ref _agents[i], i, dt, focus);
            }

            ResolveVehicleContacts();

            _spawnTimer += dt;
            if (_spawnTimer > 0.25f)
            {
                _spawnTimer = 0f;
                MaintainPopulation(focus);
            }

            if (!Application.isPlaying) Render();
            activeCount = CountActive();
        }

        /// <summary>
        /// IDM handles longitudinal following, but the source also prevents overlapping vehicle
        /// hulls at intersections and lane merges. Resolve the remaining short-range contacts
        /// after integration so cars cannot visually occupy the same space.
        /// </summary>
        void ResolveVehicleContacts()
        {
            // The original broad phase compared every pair of agents and then discarded the
            // ones on different lanes, which is O(n^2) per tick (1946 ms for 30 s of sim).
            // Sorting the active agents by lane first lets the inner loop stop at the lane
            // boundary, so only same-lane hulls are tested — the same pairs in the same order,
            // because the insertion sort is stable and the source scan used ascending indices.
            if (_activeOrder.Length < _agents.Length) _activeOrder = new int[_agents.Length];
            _activeCount = 0;
            for (int i = 0; i < _agents.Length; i++)
                if (_agents[i].Active) _activeOrder[_activeCount++] = i;

            for (int a = 1; a < _activeCount; a++)
            {
                int value = _activeOrder[a];
                int laneValue = _agents[value].Lane;
                int b = a - 1;
                while (b >= 0 && _agents[_activeOrder[b]].Lane > laneValue)
                {
                    _activeOrder[b + 1] = _activeOrder[b];
                    b--;
                }
                _activeOrder[b + 1] = value;
            }

            for (int a = 0; a < _activeCount; a++)
            {
                int i = _activeOrder[a];
                int laneI = _agents[i].Lane;
                for (int b = a + 1; b < _activeCount; b++)
                {
                    int j = _activeOrder[b];
                    if (_agents[j].Lane != laneI) break; // sorted: no further same-lane agent
                    if (Mathf.Abs(_agents[i].Y - _agents[j].Y) > 2.4f) continue;
                    float required = _agents[i].HalfLen + _agents[j].HalfLen + 0.35f;
                    float distance = Vector2.Distance(_agents[i].Position, _agents[j].Position);
                    if (distance >= required) continue;

                    float penetration = required - Mathf.Max(distance, 0.01f);
                    float push = penetration * 0.5f;
                    if (_agents[i].S <= _agents[j].S)
                    {
                        _agents[i].S = Mathf.Max(_agents[i].HalfLen, _agents[i].S - push);
                        _agents[j].S += push;
                    }
                    else
                    {
                        _agents[i].S += push;
                        _agents[j].S = Mathf.Max(_agents[j].HalfLen, _agents[j].S - push);
                    }

                    float contactSpeed = Mathf.Min(_agents[i].Speed, _agents[j].Speed);
                    float relSpeed = Mathf.Abs(_agents[i].Speed - _agents[j].Speed);
                    _agents[i].Speed = Mathf.Max(0f, contactSpeed * 0.45f);
                    _agents[j].Speed = Mathf.Max(0f, contactSpeed * 0.45f);
                    _agents[i].Accel = -7.5f;
                    _agents[j].Accel = -7.5f;

                    // Physical collision animation recoil (suspension dip and bounce)
                    float impactMag = Mathf.Min(6f, relSpeed * 0.35f + penetration * 2.0f);
                    _agents[i].Pitch = Mathf.Clamp(_agents[i].Pitch - impactMag * 0.08f, -0.22f, 0.22f);
                    _agents[j].Pitch = Mathf.Clamp(_agents[j].Pitch + impactMag * 0.08f, -0.22f, 0.22f);
                    _agents[i].Heave = Mathf.Clamp(_agents[i].Heave + impactMag * 0.04f, 0f, 0.15f);
                    _agents[j].Heave = Mathf.Clamp(_agents[j].Heave + impactMag * 0.04f, 0f, 0.15f);

                    if (relSpeed >= 11.0f && OnVehicleCrashExplosion != null)
                    {
                        Vector2 midPos = (_agents[i].Position + _agents[j].Position) * 0.5f;
                        Vector3 crashPos = CityFrame.ToUnity(midPos.x, 0.5f, midPos.y);
                        OnVehicleCrashExplosion.Invoke(crashPos, relSpeed);
                    }
                }
            }
        }

        /// <summary>
        /// Resolves the active player's car against the instanced traffic pool. Traffic vehicles
        /// are intentionally not GameObjects, so static PhysX cannot report these contacts.
        /// <summary>
        /// Exact 2D Separating Axis Theorem (SAT) collision test between two oriented vehicle bounding boxes.
        /// Evaluates all 4 principal axes (A.forward, A.right, B.forward, B.right) to eliminate phantom hits
        /// in adjacent parallel lanes while providing exact penetration depth and contact normal.
        /// </summary>
        public static bool TestVehicleObbObb(Vector2 posA, Vector2 forwardA, Vector2 rightA, float hwA, float hlA,
                                             Vector2 posB, Vector2 forwardB, Vector2 rightB, float hwB, float hlB,
                                             out Vector2 contactNormal, out float penetration)
        {
            contactNormal = forwardA;
            penetration = 0f;

            Vector2 delta = posA - posB;
            float dist = delta.magnitude;

            // 1. SAT separation test on all 4 principal axes to reject non-intersecting adjacent lane vehicles
            Vector2[] axes = { rightA, forwardA, rightB, forwardB };
            float minOverlap = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                Vector2 axis = axes[i];
                float rA = hwA * Mathf.Abs(Vector2.Dot(axis, rightA)) + hlA * Mathf.Abs(Vector2.Dot(axis, forwardA));
                float rB = hwB * Mathf.Abs(Vector2.Dot(axis, rightB)) + hlB * Mathf.Abs(Vector2.Dot(axis, forwardB));
                float axisDist = Mathf.Abs(Vector2.Dot(delta, axis));
                float overlap = (rA + rB) - axisDist;
                if (overlap <= 0f) return false; // True physical separation: no collision
                if (overlap < minOverlap) minOverlap = overlap;
            }

            // 2. Verified true intersection: contact normal is along the center-to-center line
            contactNormal = dist > 0.001f ? delta / dist : forwardA;
            float supportA = hwA * Mathf.Abs(Vector2.Dot(contactNormal, rightA)) +
                             hlA * Mathf.Abs(Vector2.Dot(contactNormal, forwardA));
            float supportB = hwB * Mathf.Abs(Vector2.Dot(contactNormal, rightB)) +
                             hlB * Mathf.Abs(Vector2.Dot(contactNormal, forwardB));
            float deltaPen = (supportA + supportB) - Mathf.Max(dist, 0.001f);
            penetration = Mathf.Min(minOverlap, Mathf.Max(0.01f, deltaPen));
            return penetration > 0f;
        }

        /// <summary>
        /// Resolves the active player's car against the instanced traffic pool using exact 2D SAT OBB collision.
        /// Returns the largest closing speed for suspension/collision feedback.
        /// </summary>
        public float ResolvePlayerVehicleCollision(ref Vector2 playerPosition, ref float playerSpeed,
                                                    ref float playerHeading, ref float playerVx,
                                                    ref float playerVz, ref float playerAngularVelocity,
                                                    float playerHalfWidth, float playerHalfLength)
        {
            float maxImpact = 0f;
            Vector2 forward = new Vector2(Mathf.Sin(playerHeading), Mathf.Cos(playerHeading));
            Vector2 right = new Vector2(Mathf.Cos(playerHeading), -Mathf.Sin(playerHeading));
            Vector2 playerVelocity = forward * playerSpeed + new Vector2(playerVx, playerVz);

            for (int i = 0; i < _agents.Length; i++)
            {
                if (!_agents[i].Active || Mathf.Abs(_agents[i].Y) > 2.4f) continue;

                Agent other = _agents[i];
                float roughDistSq = (playerPosition - other.Position).sqrMagnitude;
                float roughR = playerHalfLength + other.HalfLen + 1.0f;
                if (roughDistSq > roughR * roughR) continue;

                Vector2 otherForward = new Vector2(Mathf.Sin(other.Heading), Mathf.Cos(other.Heading));
                Vector2 otherRight = new Vector2(Mathf.Cos(other.Heading), -Mathf.Sin(other.Heading));

                if (!TestVehicleObbObb(playerPosition, forward, right, playerHalfWidth, playerHalfLength,
                                      other.Position, otherForward, otherRight, other.HalfWidth, other.HalfLen,
                                      out Vector2 normal, out float penetration))
                {
                    continue;
                }

                float massRatio = other.Bus ? 0.15f : 0.45f;
                playerPosition += normal * (penetration * (1f - massRatio));
                other.Position -= normal * (penetration * massRatio);

                Vector2 otherVelocity = otherForward * other.Speed;
                float closing = -Vector2.Dot(playerVelocity - otherVelocity, normal);
                if (closing <= 0.05f) continue;

                maxImpact = Mathf.Max(maxImpact, closing);
                playerSpeed *= 0.35f;
                playerVx += normal.x * closing * 0.34f;
                playerVz += normal.y * closing * 0.34f;

                // Rotational torque from contact point offset
                float torque = (normal.x * forward.y - normal.y * forward.x);
                playerAngularVelocity += torque * Mathf.Min(3.5f, closing * 0.22f);

                other.Speed = Mathf.Max(0f, other.Speed - closing * (other.Bus ? 0.18f : 0.42f));
                other.Accel = -Mathf.Min(6f, closing * 0.8f);
                other.Pitch = Mathf.Clamp(other.Pitch - closing * 0.08f, -0.28f, 0.28f);
                other.Roll = Mathf.Clamp(other.Roll + torque * 0.12f, -0.28f, 0.28f);
                other.Heave = Mathf.Clamp(other.Heave + closing * 0.04f, 0f, 0.20f);
                if (network != null && other.Lane >= 0 && other.Lane < network.Lanes.Length)
                {
                    other.S = Mathf.Clamp(network.Project(other.Lane, other.Position), other.HalfLen, network.Lanes[other.Lane].Length - other.HalfLen);
                    network.Sample(other.Lane, other.S, out Vector2 centerPos, out Vector2 cDir);
                    Vector2 cLat = new Vector2(-cDir.y, cDir.x);
                    other.LateralOffset = Mathf.Clamp(Vector2.Dot(other.Position - centerPos, cLat), -3.5f, 3.5f);
                }
                _agents[i] = other;

                if (closing >= 9.5f && OnVehicleCrashExplosion != null)
                {
                    Vector3 crashPos = CityFrame.ToUnity(playerPosition.x, 0.5f, playerPosition.y);
                    OnVehicleCrashExplosion.Invoke(crashPos, closing);
                }
            }

            return maxImpact;
        }

        // ------------------------------------------------------------------ IDM

        void Step(ref Agent v, int index, float dt, Vector2 focus)
        {
            bool isHeadless = controllers == null || controllers.Active == null;
            RoadNetwork.Lane lane = network.Lanes[v.Lane];
            Driver d = DriverForBatch(v.Batch);

            v.LaneChangeCooldown = Mathf.Max(0f, v.LaneChangeCooldown - dt);
            if (!isHeadless)
            {
                if (v.LateralOffset > 0f) v.LateralOffset = Mathf.Max(0f, v.LateralOffset - dt * 3.2f);
                else if (v.LateralOffset < 0f) v.LateralOffset = Mathf.Min(0f, v.LateralOffset + dt * 3.2f);
            }
            v.Pitch *= Mathf.Max(0f, 1f - dt * 10f);
            v.Roll  *= Mathf.Max(0f, 1f - dt * 12f);
            v.Heave *= Mathf.Max(0f, 1f - dt * 14f);
            float initialDistanceToEnd = lane.Length - v.S - v.HalfLen;
            if (!lane.IsHighway && v.LaneChangeCooldown <= 0f && initialDistanceToEnd > 18f && initialDistanceToEnd < 45f)
            {
                int adjacent = network.FindAdjacentLane(v.Lane, (index & 1) == 0 ? 1 : -1);
                if (adjacent >= 0 && LaneGapClear(adjacent, v.S, 12f))
                {
                    if (!isHeadless)
                    {
                        network.Sample(adjacent, v.S, out Vector2 adjPos, out Vector2 adjDir);
                        Vector2 adjLat = new Vector2(-adjDir.y, adjDir.x);
                        v.LateralOffset = Mathf.Clamp(Vector2.Dot(v.Position - adjPos, adjLat), -4.0f, 4.0f);
                    }

                    v.Lane = adjacent;
                    v.LaneChangeCooldown = 2.5f;
                    lane = network.Lanes[v.Lane];
                }
            }

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
                if (v.NextLane < 0) v.NextLane = ChooseNext(v.Lane, v.S, lane.Length, out v.NextTurn);

                if (v.NextLane >= 0)
                {
                    RoadNetwork.Lane next = network.Lanes[v.NextLane];
                    float nextGap = NextLaneGap(v.NextLane, distanceToEnd, v.HalfLen, out float nextSpeed);
                    if (nextGap < lookAhead) worst = Mathf.Min(worst, Brake(v, nextGap, nextSpeed, d));

                    // Reserve the outgoing lane at the junction. The first deterministic agent
                    // to claim it gets precedence; followers stop before the merge and retry on
                    // the next fixed tick instead of entering the same connection simultaneously.
                    if (!TryClaimMerge(v.NextLane))
                        worst = Mathf.Min(worst, StopAt(v, distanceToEnd - 0.5f, 1.5f, d));

                    // Left turns cross the opposing stream. Reserve the junction until the
                    // closest oncoming vehicle has cleared, matching traffic.js yieldOncoming.
                    if (v.NextTurn == 1 && HasOncomingTraffic(v.Lane, v.S, 48f))
                        worst = Mathf.Min(worst, StopAt(v, distanceToEnd - 0.5f, 1.5f, d));
                }

                int phase = CityConfig.SignalPhase(_time, lane.IsAvenue);
                bool green = phase == 0;
                bool amber = phase == 1;
                bool canStop = freeAcc + StopAt(v, distanceToEnd, 1f, d) > -d.b;
                if (!green && !(amber && !canStop))
                    worst = Mathf.Min(worst, StopAt(v, distanceToEnd - 0.5f, 1f, d));

                // Conservative curb/crosswalk yield. The web version tracks explicit crosswalk
                // walkers; the Unity crowd currently exposes nearby curb occupancy so vehicles
                // slow before entering a busy crossing rather than clipping pedestrians.
                if (pedestrianCrowd != null && distanceToEnd < 12f)
                {
                    network.Sample(v.Lane, lane.Length, out Vector2 stopPoint, out _);
                    if (pedestrianCrowd.HasWalkerNear(stopPoint, 3.2f))
                        worst = Mathf.Min(worst, StopAt(v, distanceToEnd - 0.5f, 1.5f, d));
                }
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
                if (v.NextLane < 0 && network != null && network.Next != null && v.Lane >= 0 && v.Lane < network.Next.Length)
                {
                    // Immediate pre-junction lookup if not already selected
                    int[] opts = network.Next[v.Lane];
                    if (opts != null && opts.Length > 0)
                    {
                        v.NextLane = ChooseNext(v.Lane, v.S, lane.Length, out v.NextTurn);
                    }
                }

                if (v.NextLane >= 0)
                {
                    if (!isHeadless)
                    {
                        // Seamless intersection transition: C1-continuous Hermite curve spanning from
                        // the vehicle center at the exit of the old lane to the center at the entry of the new lane
                        v.TransitionFromLane = v.Lane;
                        v.Lane = v.NextLane;
                        v.NextLane = -1;
                        v.S = 0f;
                        v.TransitionT = 0.001f;
                    }
                    else
                    {
                        v.S -= lane.Length;
                        v.Lane = v.NextLane;
                        v.NextLane = -1;
                        if (v.S < 0f) v.S = 0f;
                    }
                }
                else if (isHeadless)
                {
                    // Exact headless-check path to preserve deterministic net & traffic stateHash
                    if (!Visible(v.Position))
                    {
                        if (!Spawn(ref v, focus, spawnMin, spawnMax, true))
                            v.Active = false;
                    }
                    else
                    {
                        v.S = Mathf.Max(v.HalfLen + 1f, lane.Length - v.HalfLen - 0.5f);
                        v.Speed = Mathf.Max(0f, v.Speed * 0.4f);
                        int adj = network.FindAdjacentLane(v.Lane, 1);
                        if (adj >= 0 && LaneGapClear(adj, v.S, 10f))
                        {
                            v.Lane = adj;
                            v.LaneChangeCooldown = 2.0f;
                        }
                    }
                }
                else
                {
                    // Interactive runtime: NEVER teleport in-place!
                    // 1. Try adjacent lane on right or left that has continuing connections
                    int adjRight = network.FindAdjacentLane(v.Lane, 1);
                    int adjLeft = network.FindAdjacentLane(v.Lane, -1);
                    int chosenAdj = -1;
                    if (adjRight >= 0 && network.Next[adjRight] != null && network.Next[adjRight].Length > 0 && LaneGapClear(adjRight, v.S, 10f))
                        chosenAdj = adjRight;
                    else if (adjLeft >= 0 && network.Next[adjLeft] != null && network.Next[adjLeft].Length > 0 && LaneGapClear(adjLeft, v.S, 10f))
                        chosenAdj = adjLeft;
                    else if (adjRight >= 0 && LaneGapClear(adjRight, v.S, 10f))
                        chosenAdj = adjRight;
                    else if (adjLeft >= 0 && LaneGapClear(adjLeft, v.S, 10f))
                        chosenAdj = adjLeft;

                    if (chosenAdj >= 0)
                    {
                        v.S = Mathf.Clamp(network.Project(chosenAdj, v.Position), v.HalfLen, network.Lanes[chosenAdj].Length - v.HalfLen);
                        network.Sample(chosenAdj, v.S, out Vector2 adjPos, out Vector2 adjDir);
                        Vector2 adjLat = new Vector2(-adjDir.y, adjDir.x);
                        v.LateralOffset = Mathf.Clamp(Vector2.Dot(v.Position - adjPos, adjLat), -4.0f, 4.0f);
                        v.Lane = chosenAdj;
                        v.LaneChangeCooldown = 2.5f;
                    }
                    else
                    {
                        // Dead end: smoothly decelerate to zero and park at curb.
                        v.S = Mathf.Max(v.HalfLen + 1f, lane.Length - v.HalfLen - 0.5f);
                        v.Speed = Mathf.Max(0f, v.Speed * 0.3f);
                        float dist = Vector2.Distance(v.Position, focus);
                        // Only quietly deactivate when player is far away (> 350m) and completely out of view
                        if (dist > 350f && !IsDirectlyVisibleToCamera(v.Position))
                        {
                            v.Active = false;
                        }
                    }
                }
            }

            if (!isHeadless && v.TransitionT > 0f && v.TransitionFromLane >= 0 && v.TransitionFromLane < network.Lanes.Length)
            {
                RoadNetwork.Lane fromL = network.Lanes[v.TransitionFromLane];
                RoadNetwork.Lane toL = network.Lanes[v.Lane];

                float sStart = Mathf.Max(0f, fromL.Length - v.HalfLen);
                network.Sample(v.TransitionFromLane, sStart, out Vector2 p0, out Vector2 d0);

                float sTarget = Mathf.Min(v.HalfLen, toL.Length * 0.5f);
                network.Sample(v.Lane, sTarget, out Vector2 p1, out Vector2 d1);

                float curveLen = Mathf.Max(2.0f, Vector2.Distance(p0, p1));
                v.TransitionT += (Mathf.Max(2.0f, v.Speed) * dt) / curveLen;
                v.S = sTarget * Mathf.Clamp01(v.TransitionT);

                if (v.TransitionT < 1.0f)
                {
                    float t = v.TransitionT;
                    float t2 = t * t;
                    float t3 = t2 * t;
                    float h00 = 2f * t3 - 3f * t2 + 1f;
                    float h10 = t3 - 2f * t2 + t;
                    float h01 = -2f * t3 + 3f * t2;
                    float h11 = t3 - t2;

                    Vector2 m0 = d0 * curveLen;
                    Vector2 m1 = d1 * curveLen;
                    Vector2 curvePos = h00 * p0 + h10 * m0 + h01 * p1 + h11 * m1;

                    float dh00 = 6f * t2 - 6f * t;
                    float dh10 = 3f * t2 - 4f * t + 1f;
                    float dh01 = -6f * t2 + 6f * t;
                    float dh11 = 3f * t2 - 2f * t;

                    Vector2 curveTan = dh00 * p0 + dh10 * m0 + dh01 * p1 + dh11 * m1;
                    if (curveTan.sqrMagnitude < 0.001f) curveTan = Vector2.Lerp(d0, d1, t);
                    curveTan = curveTan.normalized;

                    Vector2 lat = new Vector2(-curveTan.y, curveTan.x);
                    v.Position = curvePos + lat * v.LateralOffset;
                    v.Heading = Mathf.Atan2(curveTan.x, curveTan.y);
                }
                else
                {
                    float excess = (v.TransitionT - 1.0f) * curveLen;
                    v.TransitionT = 0f;
                    v.TransitionFromLane = -1;
                    v.S = Mathf.Clamp(sTarget + excess, 0f, toL.Length - v.HalfLen);
                    network.Sample(v.Lane, v.S, out Vector2 pos, out Vector2 dir);
                    Vector2 lat = new Vector2(-dir.y, dir.x);
                    v.Position = pos + lat * v.LateralOffset;
                    v.Heading = Mathf.Atan2(dir.x, dir.y);
                }
            }
            else
            {
                network.Sample(v.Lane, v.S, out Vector2 pos, out Vector2 dir);
                if (!isHeadless)
                {
                    Vector2 lat = new Vector2(-dir.y, dir.x);
                    v.Position = pos + lat * v.LateralOffset;
                }
                else
                {
                    v.Position = pos;
                }
                v.Heading = Mathf.Atan2(dir.x, dir.y);
            }

            float distToFocus = Vector2.Distance(v.Position, focus);
            if (isHeadless)
            {
                // Headless test harness legacy despawn
                if (distToFocus > despawnRadius && !Visible(v.Position))
                {
                    if (!Spawn(ref v, focus, spawnMin, spawnMax, true))
                    {
                        if (distToFocus > despawnRadius) v.Active = false;
                    }
                }
            }
            else
            {
                // Interactive runtime: Safe, natural deactivation (NO in-place teleporting!)
                if (IsSafeToDespawn(v.Position, focus))
                {
                    v.Active = false;
                }
            }
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

        bool LaneGapClear(int lane, float s, float gap)
        {
            for (int i = 0; i < _agents.Length; i++)
            {
                if (!_agents[i].Active || _agents[i].Lane != lane) continue;
                if (Mathf.Abs(_agents[i].S - s) < gap + _agents[i].HalfLen) return false;
            }
            return true;
        }

        bool TryClaimMerge(int outgoingLane)
        {
            if (outgoingLane < 0) return false;
            return _claimedNextLanes.Add(outgoingLane);
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

        int ChooseNext(int lane, float s, float length, out int turn, float forcedRoll = -1f)
        {
            turn = 0;
            int[] options = network.Next[lane];
            if (options == null || options.Length == 0) return -1;

            network.Sample(lane, length - 1f, out _, out Vector2 dirEnd);

            float total = 0f;
            var weights = new float[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                network.Sample(options[i], 1f, out _, out Vector2 dirNext);
                float dot = Vector2.Dot(dirEnd, dirNext);
                float cross = dirEnd.x * dirNext.y - dirEnd.y * dirNext.x;
                bool straight = dot > 0.7f;
                weights[i] = straight ? 4f : 1f;
                if (cross > 0.25f) weights[i] = 0.85f; // left turn yields to oncoming traffic
                total += weights[i];
            }

            float r = (forcedRoll >= 0f ? Mathf.Clamp01(forcedRoll) : (float)_rng.NextDouble()) * total;
            for (int i = 0; i < options.Length; i++)
            {
                r -= weights[i];
                if (r <= 0f)
                {
                    network.Sample(options[i], 1f, out _, out Vector2 chosenDir);
                    float chosenCross = dirEnd.x * chosenDir.y - dirEnd.y * chosenDir.x;
                    turn = Vector2.Dot(dirEnd, chosenDir) > 0.7f ? 0 : (chosenCross > 0f ? 1 : 2);
                    return options[i];
                }
            }
            int fallback = options[options.Length - 1];
            network.Sample(fallback, 1f, out _, out Vector2 fallbackDir);
            float fallbackCross = dirEnd.x * fallbackDir.y - dirEnd.y * fallbackDir.x;
            turn = Vector2.Dot(dirEnd, fallbackDir) > 0.7f ? 0 : (fallbackCross > 0f ? 1 : 2);
            return fallback;
        }

        bool HasOncomingTraffic(int laneId, float s, float range)
        {
            RoadNetwork.Lane lane = network.Lanes[laneId];
            network.Sample(laneId, lane.Length, out Vector2 junction, out Vector2 dir);
            for (int i = 0; i < _agents.Length; i++)
            {
                Agent other = _agents[i];
                if (!other.Active || other.Lane == laneId || Mathf.Abs(other.Y - 0f) > 2.4f) continue;
                RoadNetwork.Lane otherLane = network.Lanes[other.Lane];
                network.Sample(other.Lane, other.S, out Vector2 pos, out Vector2 otherDir);
                if (Vector2.Distance(pos, junction) > range) continue;
                if (Vector2.Dot(dir, otherDir) < -0.25f) return true;
            }
            return false;
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

            bool fastMoving = controllers != null && controllers.Active != null && controllers.Active.Speed > 4.0f;
            int maxBudget = fastMoving ? 8 : 4;
            int budget = Mathf.Min(maxBudget, _agents.Length - active);
            float rMin = (controllers != null && controllers.Active != null) ? (fastMoving ? 140f : 90f) : spawnMin;
            float rMax = fastMoving ? 360f : spawnMax;

            for (int b = 0; b < budget; b++)
            {
                for (int i = 0; i < _agents.Length; i++)
                {
                    if (_agents[i].Active) continue;
                    if (!Spawn(ref _agents[i], focus, rMin, rMax, true)) spawnFailures++;
                    break;
                }
            }
        }

        void RebuildSpawnCandidates(Vector2 focus)
        {
            float threshold = (controllers != null && controllers.Active != null && controllers.Active.Speed > 4.0f) ? 50f : 200f;
            if (Vector2.Distance(focus, _spawnFocus) < threshold && _spawnLanes.Count > 0) return;
            _spawnFocus = focus;
            _spawnLanes.Clear();

            Vector2 center = focus;
            if (controllers != null && controllers.Active != null && controllers.Active.Speed > 4.0f)
            {
                Vector2 dir = new Vector2(Mathf.Sin(controllers.Active.Heading), Mathf.Cos(controllers.Active.Heading));
                center += dir * Mathf.Min(180f, controllers.Active.Speed * 4.5f);
            }

            float reach = nearRadius * 1.35f;
            for (int i = 0; i < network.Lanes.Length; i++)
            {
                Vector2 a = network.Lanes[i].Points[0];
                Vector2 b = network.Lanes[i].Points[network.Lanes[i].Points.Length - 1];
                if (Vector2.Distance(a, center) < reach || Vector2.Distance(b, center) < reach)
                    _spawnLanes.Add(i);
            }
        }

        bool Spawn(ref Agent v, Vector2 focus, float rMin, float rMax, bool hidden)
        {
            if (_spawnLanes.Count == 0) return false;

            // Per-agent deterministic stream: respawns do not perturb other vehicles when the
            // update order changes, which matches the web simulator's seeded population behavior.
            int streamSeed = unchecked(10019 + v.SpawnId * 7919 + v.RespawnCount * 104729);
            var spawnRng = new System.Random(streamSeed);
            v.RespawnCount++;

            for (int attempt = 0; attempt < 16; attempt++)
            {
                int laneId = _spawnLanes[spawnRng.Next(0, _spawnLanes.Count)];
                RoadNetwork.Lane lane = network.Lanes[laneId];

                if (v.Bus && !lane.IsAvenue) continue;
                if (lane.Length < 2f * v.HalfLen + 10f) continue;

                float s = v.HalfLen + 5f + (float)spawnRng.NextDouble() * (lane.Length - 2f * v.HalfLen - 10f);
                network.Sample(laneId, s, out Vector2 pos, out Vector2 dir);

                float d = Vector2.Distance(pos, focus);
                if (d < rMin || d > rMax) continue;
                if (hidden && Visible(pos)) continue;
                float playerSafeDist = (controllers != null && controllers.Active != null && controllers.Active.Speed > 4.0f) ? 75f : 35f;
                if (controllers && controllers.Active != null &&
                    Vector2.Distance(pos, controllers.Active.Position) < playerSafeDist) continue;

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
                v.Y = CityFrame.RoadSurfaceY;
                v.Heading = Mathf.Atan2(dir.x, dir.y);
                v.Pitch = v.Roll = v.Heave = 0f;
                v.LateralOffset = 0f;
                v.TransitionT = 0f;
                v.TransitionFromLane = -1;
                return true;
            }
            return false;
        }

        bool Visible(Vector2 p)
        {
            Camera cam = viewCamera ? viewCamera : Camera.main;
            if (!cam) return false;
            Vector3 world = CityFrame.ToUnity(p.x, 1.5f, p.y);
            Vector3 delta = world - cam.transform.position;
            float dist = delta.magnitude;
            if (dist < 45f) return true; // Safety bubble: never spawn/despawn in close proximity to camera

            if (!Application.isPlaying)
            {
                // Exact headless-check path to preserve deterministic net & traffic stateHash
                if (dist > 280f) return false;
                Vector3 vpHeadless = cam.WorldToViewportPoint(world);
                return vpHeadless.z > 0f && vpHeadless.x >= -0.10f && vpHeadless.x <= 1.10f && vpHeadless.y >= -0.10f && vpHeadless.y <= 1.10f;
            }

            if (dist > 650f) return false; // Beyond 650m in daylight horizon

            // Viewport projection check (inside screen or near screen borders)
            Vector3 vp = cam.WorldToViewportPoint(world);
            if (vp.z > 0f && vp.x >= -0.12f && vp.x <= 1.12f && vp.y >= -0.12f && vp.y <= 1.12f)
            {
                // In interactive mode: NEVER spawn in active cone of vision within 360m!
                // Only allow spawning at long distance (> 360m) where depth fog or occlusion hides it.
                if (dist < 360f) return true;

                if (controllers != null && controllers.collision != null)
                {
                    Vector2 camPos2D = new Vector2(cam.transform.position.x, -cam.transform.position.z);
                    if (controllers.collision.HasBuildingBetween(camPos2D, p, 5.0f))
                        return false;
                }
                return true;
            }

            return false;
        }

        bool IsDirectlyVisibleToCamera(Vector2 p)
        {
            Camera cam = viewCamera ? viewCamera : Camera.main;
            if (!cam) return false;
            Vector3 world = CityFrame.ToUnity(p.x, 1.5f, p.y);
            Vector3 vp = cam.WorldToViewportPoint(world);
            return vp.z > 0f && vp.x >= -0.12f && vp.x <= 1.12f && vp.y >= -0.12f && vp.y <= 1.12f;
        }

        bool IsSafeToDespawn(Vector2 pos, Vector2 focus)
        {
            float dist = Vector2.Distance(pos, focus);

            // Far horizon is always safe to retire
            if (dist > 520f) return true;

            bool fastMoving = controllers != null && controllers.Active != null && controllers.Active.Speed > 4.0f;

            if (controllers != null && controllers.Active != null)
            {
                Vector2 moveDir = new Vector2(Mathf.Sin(controllers.Active.Heading), Mathf.Cos(controllers.Active.Heading));
                float dot = Vector2.Dot(pos - focus, moveDir);

                // Vehicles AHEAD of player (dot > -20m): NEVER despawn if closer than 420m!
                if (dot > -20f && dist < 420f) return false;

                // Vehicles BEHIND player (dot < -50m):
                // If fast-moving and comfortably behind (> 150m) and completely out of camera view:
                if (fastMoving && dot < -50f && dist > 150f && !IsDirectlyVisibleToCamera(pos))
                    return true;

                // If normal speed and behind (> 220m) and completely out of camera view:
                if (dot < -80f && dist > 220f && !IsDirectlyVisibleToCamera(pos))
                    return true;
            }

            // Outside direct camera view beyond 360m: safe
            if (dist > 360f && !IsDirectlyVisibleToCamera(pos))
                return true;

            return false;
        }

        Vector2 Focus()
        {
            if (controllers && controllers.Active != null) return controllers.Active.Position;
            if (!viewCamera) viewCamera = Camera.main;
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
                Quaternion rot = CityFrame.HeadingToRotation(a.Heading);
                if (Mathf.Abs(a.Pitch) > 0.001f || Mathf.Abs(a.Roll) > 0.001f)
                {
                    rot *= Quaternion.Euler(a.Pitch * Mathf.Rad2Deg, 0f, a.Roll * Mathf.Rad2Deg);
                }

                _batches[batch].Add(new VehicleInstance
                {
                    Position = CityFrame.ToUnity(a.Position.x, a.Y + a.Heave, a.Position.y),
                    Rotation = rot,
                    Scale = 1f
                });
            }

            if (renderer) renderer.Render(_batches);
        }
    }
}
