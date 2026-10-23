using System;
using UnityEngine;
using Nyc.Core;

namespace Nyc.Gameplay
{
    /// <summary>
    /// Owns the four travel controllers and the shared world services.
    /// Port of ThirdPersonControllerManager (controllers.js:89-303, 922-971).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThirdPersonControllerManager : MonoBehaviour
    {
        [Header("References")]
        public PlayerInputReader input;
        public CollisionWorld collision;
        public TeleportService teleport;

        [Header("Entities")]
        public PedestrianController person;
        public CarController car;
        public BikeController bike;
        public HelicopterController helicopter;

        [Header("Config")]
        public TravelMode startMode = TravelMode.Car;
        public float sidewalkHeight = 0.22f;
        [Tooltip("Walking surface inside Central Park. The Phase 2 park mesh sits at 0.06 m; " +
                 "the three.js project used a raised 1.24 m terrain.")]
        public float centralParkGroundY = 0.06f;
        [Tooltip("When off, the pedestrian stays where you left it instead of patrolling.")]
        public bool idlePedestrianWalks = false;

        public TravelMode ActiveMode { get; private set; } = TravelMode.Car;
        public TravelController Active => Resolve(ActiveMode);
        public event Action<TravelMode> ModeChanged;
        public event Action Teleported;

        ControllerContext _ctx;
        TravelController[] _all;
        bool _ready;
        float _simulationAccumulator;
        const float SimulationStep = 1f / 60f;

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (!person) person = GetComponentInChildren<PedestrianController>(true);
            if (!car) car = GetComponentInChildren<CarController>(true);
            if (!bike) bike = GetComponentInChildren<BikeController>(true);
            if (!helicopter) helicopter = GetComponentInChildren<HelicopterController>(true);
            if (!input) input = GetComponentInChildren<PlayerInputReader>(true);
            if (!collision) collision = FindAnyObjectByType<CollisionWorld>();
            if (!teleport) teleport = GetComponentInChildren<TeleportService>(true);

            _all = new[] { (TravelController)person, car, bike, helicopter };

            _ctx = new ControllerContext
            {
                Collision = collision,
                Manager = this,
                Ground = GroundForActive
            };

            if (collision && CityConfig.Ready && CityConfig.Geo?.Manhattan != null)
                collision.SetIslandBounds(IslandBoundsFromGeo());

            SpawnAll();
            SetMode(startMode);
            _ready = true;
        }

        void Update()
        {
            if (!_ready) return;
            _simulationAccumulator += Mathf.Min(Time.deltaTime, 0.10f);
            int steps = 0;
            while (_simulationAccumulator >= SimulationStep && steps++ < 4)
            {
                Tick(SimulationStep);
                _simulationAccumulator -= SimulationStep;
            }
            if (steps == 4) _simulationAccumulator = 0f;

            // Render between fixed states so pedestrians and vehicles do not stair-step when
            // the render frame rate is not an exact multiple of the simulation rate.
            float alpha = _simulationAccumulator / SimulationStep;
            if (_all != null)
                for (int i = 0; i < _all.Length; i++) if (_all[i] != null) _all[i].InterpolateTransform(alpha);
        }

        // ------------------------------------------------------------------ tick

        public void Tick(float dt)
        {
            _ctx.Elapsed += dt;

            if (_all != null)
                for (int i = 0; i < _all.Length; i++) if (_all[i] != null) _all[i].BeginSimulationStep();

            if (ActiveMode != TravelMode.Person && idlePedestrianWalks) TickIdlePedestrian(dt);

            TravelController active = Active;
            if (active != null)
            {
                ControllerInput state = input ? input.Current : default;
                active.Tick(dt, state, _ctx);
            }

            for (int i = 0; i < _all.Length; i++)
                if (_all[i] != null) _all[i].SyncTransform();
        }

        void TickIdlePedestrian(float dt)
        {
            if (!person) return;
            const float walkSpeed = 1.48f;
            person.Speed = walkSpeed;
            person.Position.x += Mathf.Sin(person.Heading) * walkSpeed * dt;
            person.Position.y += Mathf.Cos(person.Heading) * walkSpeed * dt;
            if (person.Position.y < -340f) person.Heading = 0f;
            else if (person.Position.y > 340f) person.Heading = Mathf.PI;
        }

        // ------------------------------------------------------------------ world services

        float GroundForActive(float x, float z) => GetGroundHeight(x, z, ActiveMode);

        public float GetGroundHeight(float x, float z, TravelMode mode)
        {
            Vector4 cp = CityConfig.CentralParkBounds;
            if (x > cp.x && x < cp.y && z > cp.z && z < cp.w) return centralParkGroundY;

            if (mode == TravelMode.Person)
                return CitySpatialIndex.IsOnSidewalk(x, z) ? sidewalkHeight : 0f;

            return 0f;
        }

        /// <summary>Port of the island / shoreline clamp at the top of resolveHorizontalCollision.</summary>
        public void ClampToIsland(ref Vector2 pos, bool isHelicopter, ref float speed)
        {
            Vector4 b = collision ? collision.Bounds : new Vector4(-2117f, 2569f, -7090f, 6512f);

            if (isHelicopter)
            {
                pos.x = Mathf.Clamp(pos.x, b.x - 6000f, b.y + 6000f);
                pos.y = Mathf.Clamp(pos.y, b.z - 6000f, b.w + 6000f);
                return;
            }

            pos.y = Mathf.Clamp(pos.y, b.z + 30f, b.w);
            pos.x = Mathf.Clamp(pos.x, b.x, b.y);

            if (!CityConfig.Ready) return;
            CityConfig.IslandBoundsAtZ(pos.y, out float west, out float east);
            float minX = west + 8f;
            float maxX = east - 8f;
            if (minX > maxX) return;

            if (pos.x < minX)
            {
                pos.x = minX;
                if (speed != 0f) speed *= 0.4f;
            }
            else if (pos.x > maxX)
            {
                pos.x = maxX;
                if (speed != 0f) speed *= 0.4f;
            }
        }

        static Vector4 IslandBoundsFromGeo()
        {
            var ring = CityConfig.Geo.Manhattan;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < ring.Length; i++)
            {
                Vector2 v = ring[i];
                if (v.x < minX) minX = v.x;
                if (v.x > maxX) maxX = v.x;
                if (v.y < minZ) minZ = v.y;
                if (v.y > maxZ) maxZ = v.y;
            }
            return new Vector4(minX, maxX, minZ, maxZ);
        }

        // ------------------------------------------------------------------ modes

        public TravelController Resolve(TravelMode mode)
        {
            switch (mode)
            {
                case TravelMode.Person: return person;
                case TravelMode.Car: return car;
                case TravelMode.Cyclist: return bike;
                default: return helicopter;
            }
        }

        public void SetMode(TravelMode mode)
        {
            if (Resolve(mode) == null) return;
            ActiveMode = mode;
            if (input) input.ClearTouch();
            Active?.SyncTransform();
            ModeChanged?.Invoke(mode);
        }

        public void SetMode(string mode)
        {
            if (Enum.TryParse(mode, true, out TravelMode m)) SetMode(m);
        }

        // ------------------------------------------------------------------ spawn / teleport

        /// <summary>Spawn points from controllers.js: 5th Ave & 37th St.</summary>
        public void SpawnAll()
        {
            float z = CityConfig.Ready ? CityConfig.StreetZ(37) : 0f;

            if (person)
            {
                var p = new Vector2(-11.6f, z + 12f);
                person.Teleport(p, 0f, GetGroundHeight(p.x, p.y, TravelMode.Person));
            }
            if (car)
            {
                var p = new Vector2(3.6f, z + 12f);
                car.Teleport(p, 0f, 0f);
            }
            if (bike)
            {
                var p = new Vector2(5.2f, z - 8f);
                bike.Teleport(p, 0f, 0f);
            }
            if (helicopter)
            {
                var p = new Vector2(-110f, z - 60f);
                helicopter.Teleport(p, 0f, 96f);
            }

            ActorModelFactory.AttachActorModels(this);
        }

        public Vector2 TeleportActiveTo(float x, float z, float heading)
        {
            TravelController ent = Active;
            if (ent == null) return Vector2.zero;

            if (float.IsNaN(heading) || float.IsInfinity(heading)) heading = ent.Heading;

            float altitude = ActiveMode == TravelMode.Helicopter
                ? Mathf.Clamp(ent.Height, 92f, 380f)
                : 0f;

            Vector2 destination = ent.Position;
            bool found = teleport && teleport.TryFindSafe(new Vector2(x, z), heading, ActiveMode, ent, out destination);

            if (found)
            {
                ent.Position = destination;
                ent.Heading = heading;
                ent.Height = ActiveMode == TravelMode.Helicopter
                    ? altitude
                    : GetGroundHeight(destination.x, destination.y, ActiveMode);
            }

            ent.ResetMotion();
            ent.SyncTransform();
            if (input) input.ClearTouch();

            Teleported?.Invoke();
            return ent.Position;
        }

        public Vector2 TeleportTo(string locationId)
        {
            if (!CityConfig.Teleports.TryGetValue(locationId, out var t)) return Active != null ? Active.Position : Vector2.zero;
            return TeleportActiveTo(t.X, t.Z, t.HeadingRad);
        }
    }
}
