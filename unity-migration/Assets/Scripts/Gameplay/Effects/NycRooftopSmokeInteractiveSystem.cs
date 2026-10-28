using System.Collections.Generic;
using UnityEngine;
using Nyc.Core;
using GunnellSmoke;

namespace Nyc.Gameplay.Effects
{
    /// <summary>
    /// Manages interactive rooftop steam/smoke plumes (chimneys, HVAC cooling towers, boiler exhausts)
    /// across Manhattan. When a helicopter flies overhead or passes close, its rotor downwash
    /// dynamically displaces, cavitates, and swirls the volumetric smoke in real time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NycRooftopSmokeInteractiveSystem : MonoBehaviour
    {
        public static NycRooftopSmokeInteractiveSystem Instance { get; private set; }

        [Header("Helicopter Interaction")]
        [Tooltip("Max distance from helicopter to rooftop smoke for downwash interaction.")]
        public float interactionRadius = 36.0f;
        [Tooltip("Rotor downward air velocity force in m/s.")]
        public float rotorDownwashStrength = 14.0f;
        [Tooltip("Downward clearing fraction carved into the volumetric smoke.")]
        [Range(0.1f, 0.9f)] public float clearingFraction = 0.65f;
        [Tooltip("Close-proximity distance threshold for violent wake blast / vortex ring.")]
        public float nearTurbulenceDistance = 12.0f;

        [Header("Plume Configuration")]
        public float plumeRadius = 5.2f;
        public float voxelSize = 0.36f;
        public float maxActiveDistance = 380.0f;
        public int maxActivePlumes = 10;

        [Header("Audio Feedback")]
        public AudioClip downwashGustClip;
        [Range(0f, 1f)] public float audioVolume = 0.6f;

        // Rooftop plume anchors (City coordinates: X, Z) across Midtown, Times Square, and surrounding blocks
        private static readonly Vector2[] AnchorCoords = new Vector2[]
        {
            // Midtown (5th Ave / 42nd St vicinity)
            new Vector2(35.0f, -42.0f),
            new Vector2(-48.0f, 38.0f),
            new Vector2(62.0f, 85.0f),
            new Vector2(-80.0f, -65.0f),
            new Vector2(110.0f, -120.0f),
            new Vector2(-28.0f, 140.0f),
            // Times Square / Theater District (7th Ave / Broadway)
            new Vector2(-155.0f, -190.0f),
            new Vector2(-190.0f, -270.0f),
            new Vector2(-125.0f, -340.0f),
            // Herald Square / 34th St
            new Vector2(20.0f, 340.0f),
            new Vector2(-55.0f, 390.0f),
            new Vector2(85.0f, 440.0f),
            // Grand Central / Lexington
            new Vector2(165.0f, -35.0f),
            new Vector2(210.0f, 45.0f),
            // Central Park South (59th St)
            new Vector2(-15.0f, -820.0f),
            new Vector2(75.0f, -860.0f),
        };

        private struct RooftopPlume
        {
            public Vector3 worldPosition;
            public GunnellSmokeVolume volume;
            public float nextWakeTime;
            public bool isSpawned;
        }

        private readonly List<RooftopPlume> _plumes = new List<RooftopPlume>();
        private ThirdPersonControllerManager _controllerManager;
        private AudioSource _audioSource;
        private float _lastGustTime = -10f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInit()
        {
            if (Instance == null)
            {
                var go = new GameObject("NycRooftopSmokeInteractiveSystem");
                Instance = go.AddComponent<NycRooftopSmokeInteractiveSystem>();
            }
        }

        void Awake()
        {
            if (Instance == null) Instance = this;
            _audioSource = GetComponent<AudioSource>();
            if (_audioSource == null)
            {
                _audioSource = gameObject.AddComponent<AudioSource>();
                _audioSource.spatialBlend = 1.0f;
                _audioSource.minDistance = 6.0f;
                _audioSource.maxDistance = 45.0f;
            }

            if (downwashGustClip == null)
            {
#if UNITY_EDITOR
                downwashGustClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Explosions/Throw.wav");
#endif
            }
        }

        void Start()
        {
            _controllerManager = FindAnyObjectByType<ThirdPersonControllerManager>();
            InitializePlumeLocations();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            CleanupPlumes();
        }

        void InitializePlumeLocations()
        {
            _plumes.Clear();
            var collision = FindAnyObjectByType<CollisionWorld>();

            for (int i = 0; i < AnchorCoords.Length; i++)
            {
                Vector2 coord = AnchorCoords[i];
                float roofY = 0f;
                if (collision != null)
                {
                    roofY = collision.GetRoofHeight(coord.x, coord.y);
                }

                // If roof height not detected by collision query, assign authentic NYC mid-rise roof height
                if (roofY < 12.0f || roofY > 180.0f)
                {
                    roofY = 28.0f + (Mathf.Abs(coord.x * 3.7f + coord.y * 1.9f) % 45.0f);
                }

                // Position on top of chimney cowl / HVAC exhaust vent
                Vector3 worldPos = CityFrame.ToUnity(coord.x, roofY + 1.1f, coord.y);

                _plumes.Add(new RooftopPlume
                {
                    worldPosition = worldPos,
                    volume = null,
                    nextWakeTime = 0f,
                    isSpawned = false
                });
            }
        }

        void Update()
        {
            Camera cam = Camera.main;
            Vector3 focusPos = cam != null ? cam.transform.position : transform.position;

            // 1. Manage Active Plumes within distance threshold
            UpdatePlumeSpawning(focusPos);

            // 2. Interactively calculate Helicopter Downwash displacement
            UpdateHelicopterDownwash();
        }

        void UpdatePlumeSpawning(Vector3 focusPos)
        {
            int activeCount = 0;
            for (int i = 0; i < _plumes.Count; i++)
            {
                RooftopPlume plume = _plumes[i];
                float dist = Vector3.Distance(focusPos, plume.worldPosition);

                if (dist <= maxActiveDistance && activeCount < maxActivePlumes)
                {
                    if (!plume.isSpawned || plume.volume == null)
                    {
                        plume.volume = SpawnPlumeVolume(plume.worldPosition, i);
                        plume.isSpawned = true;
                    }
                    activeCount++;
                }
                else
                {
                    if (plume.isSpawned && plume.volume != null)
                    {
                        Destroy(plume.volume.gameObject);
                        plume.volume = null;
                        plume.isSpawned = false;
                    }
                }
                _plumes[i] = plume;
            }
        }

        GunnellSmokeVolume SpawnPlumeVolume(Vector3 position, int index)
        {
            var go = new GameObject($"RooftopSmokePlume_{index}");
            go.transform.position = position;
            go.transform.SetParent(transform, true);

            var volume = go.AddComponent<GunnellSmokeVolume>();
            volume.voxelSize = voxelSize;
            volume.gridExtent = new Vector3Int(14, 16, 14);
            volume.maxVoxels = 2600;
            volume.radius = plumeRadius;
            volume.growthPerSecond = 3400f;
            volume.expansionDuration = GunnellSmokeVolume.MinimumExpansionDuration;
            volume.emissionDuration = 2.2f;
            volume.edgeParticles = true;
            volume.avoidObstacles = true;
            volume.obstacleMask = SmokeGameplay.EnvironmentMask;
            volume.obstacleRadius = volume.voxelSize * 0.4f;
            volume.infinite = true;
            volume.destroyOnFinish = false;
            volume.autoStart = true;

            return volume;
        }

        void UpdateHelicopterDownwash()
        {
            if (AircraftWakeSource.ActiveAircraft.Count > 0)
            {
                for (int a = 0; a < AircraftWakeSource.ActiveAircraft.Count; a++)
                {
                    var aircraft = AircraftWakeSource.ActiveAircraft[a];
                    if (aircraft == null) continue;
                    ApplyAircraftWakeToPlumes(aircraft.Position, aircraft.Velocity, aircraft.downwashStrength, aircraft.rotorRadius, aircraft.clearingFraction);
                }
                return;
            }

            if (_controllerManager == null)
                _controllerManager = FindAnyObjectByType<ThirdPersonControllerManager>();

            HelicopterController heli = null;
            if (_controllerManager != null)
            {
                heli = _controllerManager.helicopter;
            }

            if (heli == null) return;

            // Get helicopter world-space state
            Vector3 heliPos = CityFrame.ToUnity(heli.Position.x, heli.Height, heli.Position.y);
            float heading = heli.Heading;
            Vector3 heliForward = new Vector3(Mathf.Sin(heading), 0f, -Mathf.Cos(heading));
            Vector3 heliVelocity = heliForward * heli.Speed + Vector3.up * heli.VerticalSpeed;
            ApplyAircraftWakeToPlumes(heliPos, heliVelocity, rotorDownwashStrength, 5.0f, clearingFraction);
        }

        void ApplyAircraftWakeToPlumes(Vector3 craftPos, Vector3 craftVelocity, float downwashForce, float rotorRad, float clearFrac)
        {
            for (int i = 0; i < _plumes.Count; i++)
            {
                RooftopPlume plume = _plumes[i];
                if (!plume.isSpawned || plume.volume == null || plume.volume.Wake == null) continue;

                Vector3 cloudPos = plume.worldPosition;
                Vector3 delta = cloudPos - craftPos;
                float dist = delta.magnitude;

                if (dist > interactionRadius) continue;

                // Vertical relative offset: downwash is strongest when aircraft is hovering or passing above
                float vertRel = craftPos.y - cloudPos.y;
                if (vertRel < -4.0f || vertRel > 45.0f) continue;

                // Rate-limit wake injection to 30 Hz
                if (Time.time < plume.nextWakeTime) continue;
                plume.nextWakeTime = Time.time + (1f / 30f);

                // Downwash air stream: downwards plus radial spread along rooftop
                Vector3 radialDir = new Vector3(delta.x, 0f, delta.z).normalized;
                Vector3 downwashFlow = Vector3.down * downwashForce + radialDir * 4.5f;

                // 1. Continuous aerodynamic wake & cavity displacement
                plume.volume.Wake.AddMotion(
                    source: 8000 + i,
                    kind: 0,
                    a: craftPos,
                    b: cloudPos + Vector3.down * 1.5f,
                    radius: rotorRad * 1.5f,
                    flow: downwashFlow,
                    clearing: clearFrac,
                    duration: 0.12f,
                    seed: Time.time,
                    sweep: craftVelocity * 0.25f
                );

                // 2. High-intensity near rotor blast & vortex ring
                if (dist < nearTurbulenceDistance)
                {
                    plume.volume.Wake.AddExplosion(
                        craftPos + Vector3.down * 2.0f,
                        radius: 8.5f,
                        reach: 12.0f,
                        strength: 15.0f,
                        flow: downwashFlow
                    );

                    PlayGustSound(cloudPos);
                }

                _plumes[i] = plume;
            }
        }

        void PlayGustSound(Vector3 pos)
        {
            if (downwashGustClip == null || Time.time - _lastGustTime < 1.2f) return;
            _lastGustTime = Time.time;

            if (_audioSource != null)
            {
                _audioSource.transform.position = pos;
                _audioSource.PlayOneShot(downwashGustClip, audioVolume);
            }
        }

        void CleanupPlumes()
        {
            for (int i = 0; i < _plumes.Count; i++)
            {
                if (_plumes[i].volume != null)
                {
                    Destroy(_plumes[i].volume.gameObject);
                }
            }
            _plumes.Clear();
        }
    }
}
