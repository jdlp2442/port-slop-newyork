using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GunnellSmoke;

namespace Nyc.Gameplay.Effects
{
    /// <summary>
    /// Represents a destroyed, burning vehicle wreck resulting from a high-speed crash or explosion.
    /// Manages the crushed blackened chassis, flying physical debris (wheels, bumpers, metal plates),
    /// persistent engine fire, and a vertical rising dark volumetric smoke plume (GunnellSmoke).
    /// Dynamically clears and cavitates when a helicopter, drone, or aircraft flies overhead.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NycBurningCarWreck : MonoBehaviour
    {
        [Header("Destruction Visuals")]
        public Material burnedChassisMaterial;
        public Material flameMaterial;
        public int debrisCount = 6;
        public float debrisExplosionForce = 14.0f;

        [Header("Burning Smoke Plume")]
        [Tooltip("Delay in seconds after the initial blast before the burning smoke plume begins erupting.")]
        public float plumeIgnitionDelay = 0.35f;
        public float plumeRadius = 4.2f;
        public float voxelSize = 0.38f;
        public float plumeLifetime = 45.0f;

        [Header("Aircraft Interaction")]
        [Tooltip("Distance threshold for helicopter/drone downwash to part and clear the smoke plume.")]
        public float aircraftInteractionRadius = 26.0f;

        private GunnellSmokeVolume _smokeVolume;
        private ParticleSystem _fireParticles;
        private GameObject _vfxFireSmokeInstance;
        private ParticleSystem[] _fireSmokeParticleSystems;
        private Light _fireLight;
        private AudioSource _fireAudio;
        private readonly List<GameObject> _debrisPieces = new List<GameObject>();
        private float _spawnTime;
        private float _nextWakeTime;
        private bool _isPlumeActive;
        private static int _nextWreckId = 1;
        private int _wreckId;

        void Awake()
        {
            _wreckId = _nextWreckId++;
        }

        /// <summary>
        /// Attaches burning engine fire, volumetric smoke plume, downwash clearance, and debris
        /// directly to an existing vehicle chassis without generating a duplicate dummy chassis.
        /// </summary>
        public static NycBurningCarWreck AttachToVehicle(GameObject vehicleRoot, float impactSpeed = 15f)
        {
            if (vehicleRoot == null) return null;
            var existing = vehicleRoot.GetComponent<NycBurningCarWreck>();
            if (existing != null) return existing;

            var wreck = vehicleRoot.AddComponent<NycBurningCarWreck>();
            wreck._spawnTime = Time.time;
            wreck.SpawnFlyingDebris(impactSpeed);
            wreck.SetupFireAndAudio();
            wreck.StartCoroutine(wreck.IgniteSmokePlumeRoutine());
            return wreck;
        }

        public static NycBurningCarWreck SpawnWreck(Vector3 position, Quaternion rotation, float impactSpeed = 15f)
        {
            var go = new GameObject("BurningCarWreck");
            go.transform.position = position;
            go.transform.rotation = rotation;

            var wreck = go.AddComponent<NycBurningCarWreck>();
            wreck.InitializeWreck(impactSpeed);
            return wreck;
        }

        void InitializeWreck(float impactSpeed)
        {
            _spawnTime = Time.time;
            CreateBurnedChassis();
            SpawnFlyingDebris(impactSpeed);
            SetupFireAndAudio();
            StartCoroutine(IgniteSmokePlumeRoutine());
        }

        void CreateBurnedChassis()
        {
            if (burnedChassisMaterial == null)
            {
                Shader s = Shader.Find("Universal Render Pipeline/Lit");
                burnedChassisMaterial = new Material(s)
                {
                    color = new Color(0.11f, 0.10f, 0.10f),
                    name = "Burned_Chassis_Charred"
                };
                burnedChassisMaterial.SetFloat("_Smoothness", 0.05f);
                burnedChassisMaterial.SetFloat("_Metallic", 0.25f);
            }

            // 1. Crushed Lower Chassis Body
            var bodyGo = new GameObject("Wreck_Body");
            bodyGo.transform.SetParent(transform, false);
            bodyGo.transform.localPosition = new Vector3(0f, 0.38f, 0f);
            bodyGo.transform.localRotation = Quaternion.Euler(7.5f, 0f, 3.0f); // crumpled forward attitude

            var bodyFilter = bodyGo.AddComponent<MeshFilter>();
            bodyFilter.sharedMesh = CreateCrushedBodyMesh();
            var bodyRend = bodyGo.AddComponent<MeshRenderer>();
            bodyRend.sharedMaterial = burnedChassisMaterial;

            // 2. Crushed Cabin Frame
            var cabinGo = new GameObject("Wreck_Cabin");
            cabinGo.transform.SetParent(bodyGo.transform, false);
            cabinGo.transform.localPosition = new Vector3(0f, 0.42f, 0.15f);
            cabinGo.transform.localRotation = Quaternion.Euler(5.0f, 0f, -2.5f);

            var cabinFilter = cabinGo.AddComponent<MeshFilter>();
            cabinFilter.sharedMesh = CreateCrushedCabinMesh();
            var cabinRend = cabinGo.AddComponent<MeshRenderer>();
            cabinRend.sharedMaterial = burnedChassisMaterial;

            // 3. Ground Scorched Decal / Char Mark
            var scorchGo = new GameObject("ScorchMark");
            scorchGo.transform.SetParent(transform, false);
            scorchGo.transform.localPosition = new Vector3(0f, 0.02f, 0f);
            scorchGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var scorchFilter = scorchGo.AddComponent<MeshFilter>();
            scorchFilter.sharedMesh = CreateDiscMesh(2.6f, 16);
            var scorchRend = scorchGo.AddComponent<MeshRenderer>();
            var scorchMat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = new Color(0.04f, 0.04f, 0.04f)
            };
            scorchMat.SetFloat("_Smoothness", 0.02f);
            scorchRend.sharedMaterial = scorchMat;
        }

        void SpawnFlyingDebris(float impactSpeed)
        {
            float force = Mathf.Clamp(debrisExplosionForce * (impactSpeed / 10f), 10f, 28f);

            for (int i = 0; i < debrisCount; i++)
            {
                var debris = new GameObject($"CarDebris_{i}");
                debris.transform.position = transform.position + Vector3.up * 0.6f + Random.insideUnitSphere * 0.5f;

                var filter = debris.AddComponent<MeshFilter>();
                var rend = debris.AddComponent<MeshRenderer>();
                rend.sharedMaterial = burnedChassisMaterial;

                if (i < 2)
                {
                    // Flying Car Wheels
                    filter.sharedMesh = CreateWheelMesh(0.36f, 0.24f, 12);
                }
                else if (i == 2)
                {
                    // Detached Front Bumper
                    filter.sharedMesh = CreateBoxMesh(1.65f, 0.22f, 0.18f);
                }
                else
                {
                    // Mangled Metal Shards
                    filter.sharedMesh = CreateBoxMesh(Random.Range(0.4f, 0.8f), Random.Range(0.08f, 0.18f), Random.Range(0.3f, 0.7f));
                }

                var col = debris.AddComponent<BoxCollider>();
                col.size = filter.sharedMesh.bounds.size;

                var rb = debris.AddComponent<Rigidbody>();
                rb.mass = i < 2 ? 15f : 8f;
                Vector3 blastDir = (Random.insideUnitSphere + Vector3.up * 1.2f).normalized;
                rb.linearVelocity = blastDir * Random.Range(force * 0.6f, force * 1.2f);
                rb.angularVelocity = Random.insideUnitSphere * Random.Range(18f, 40f);

                _debrisPieces.Add(debris);
                Destroy(debris, 20.0f);
            }
        }

        void SetupFireAndAudio()
        {
            // Crackling fire flicker light
            var lightGo = new GameObject("EngineFireLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.85f, -1.2f); // engine hood
            _fireLight = lightGo.AddComponent<Light>();
            _fireLight.type = LightType.Point;
            _fireLight.color = new Color(1.0f, 0.55f, 0.15f);
            _fireLight.range = 8.5f;
            _fireLight.intensity = 2.4f;

            // Load high-fidelity URP Big Smoke & Fire VFX prefab
            GameObject fireSmokePrefab = null;
#if UNITY_EDITOR
            fireSmokePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Prefabs/BurningCarFireSmoke.prefab");
            if (fireSmokePrefab == null)
            {
                fireSmokePrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Vefects/Free Fire VFX URP/Particles/VFX_Fire_01_Big_Smoke.prefab");
            }
#endif
            if (fireSmokePrefab == null)
            {
                fireSmokePrefab = Resources.Load<GameObject>("Prefabs/BurningCarFireSmoke");
            }

            if (fireSmokePrefab != null)
            {
                _vfxFireSmokeInstance = Instantiate(fireSmokePrefab, transform, false);
                _vfxFireSmokeInstance.name = "EngineFireSmoke_VFX";
                _vfxFireSmokeInstance.transform.localPosition = new Vector3(0f, 0.75f, -1.15f);
                _vfxFireSmokeInstance.transform.localRotation = Quaternion.identity;
                _vfxFireSmokeInstance.transform.localScale = Vector3.one * 1.15f;
                _fireSmokeParticleSystems = _vfxFireSmokeInstance.GetComponentsInChildren<ParticleSystem>(true);
            }
            else
            {
                // Fallback procedural particles if prefab is not loaded
                var fireGo = new GameObject("EngineFireParticles");
                fireGo.transform.SetParent(transform, false);
                fireGo.transform.localPosition = new Vector3(0f, 0.75f, -1.2f);
                _fireParticles = fireGo.AddComponent<ParticleSystem>();

                var main = _fireParticles.main;
                main.startLifetime = 1.2f;
                main.startSpeed = 2.5f;
                main.startSize = 0.9f;
                main.startColor = new Color(1.0f, 0.45f, 0.08f, 0.85f);
                main.maxParticles = 80;
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emission = _fireParticles.emission;
                emission.rateOverTime = 35f;

                var shape = _fireParticles.shape;
                shape.shapeType = ParticleSystemShapeType.Cone;
                shape.angle = 18f;
                shape.radius = 0.45f;

                var colorOverLifetime = _fireParticles.colorOverLifetime;
                colorOverLifetime.enabled = true;
                Gradient grad = new Gradient();
                grad.SetKeys(
                    new[] { new GradientColorKey(new Color(1f, 0.9f, 0.3f), 0f), new GradientColorKey(new Color(0.9f, 0.25f, 0.02f), 0.5f), new GradientColorKey(new Color(0.15f, 0.15f, 0.15f), 1f) },
                    new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) }
                );
                colorOverLifetime.color = grad;

                var rend = fireGo.GetComponent<ParticleSystemRenderer>();
                rend.material = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                rend.material.SetColor("_BaseColor", new Color(1.2f, 0.5f, 0.1f));
            }

            // Crackle 3D Audio
            _fireAudio = gameObject.AddComponent<AudioSource>();
            _fireAudio.spatialBlend = 1.0f;
            _fireAudio.minDistance = 3.0f;
            _fireAudio.maxDistance = 35.0f;
            _fireAudio.loop = true;
            _fireAudio.volume = 0.55f;
#if UNITY_EDITOR
            _fireAudio.clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/Explosions/Impact.wav");
#endif
            if (_fireAudio.clip != null) _fireAudio.Play();
        }

        IEnumerator IgniteSmokePlumeRoutine()
        {
            // Delay smoke plume ignition so the initial explosion fireball is seen first
            yield return new WaitForSeconds(plumeIgnitionDelay);

            Vector3 plumePos = transform.position + Vector3.up * 0.9f + transform.forward * -0.9f;
            _smokeVolume = SpawnBurningCarPlumeVolume(plumePos);
            _isPlumeActive = true;
        }

        GunnellSmokeVolume SpawnBurningCarPlumeVolume(Vector3 position)
        {
            var go = new GameObject("BurningCar_SmokePlume");
            go.transform.position = position;
            go.transform.SetParent(transform, true);
            // CRITICAL: Deactivate GameObject before AddComponent so OnEnable() doesn't fire
            // with default grenade dimensions before custom tall vertical plume extent is assigned.
            go.SetActive(false);

            var volume = go.AddComponent<GunnellSmokeVolume>();
            // Tall column grid specifically shaped for an ascending vertical plume (~28m high)
            volume.voxelSize = voxelSize;
            volume.gridExtent = new Vector3Int(14, 38, 14);
            volume.maxVoxels = 3600;
            volume.radius = plumeRadius;
            volume.growthPerSecond = 3800f;
            volume.expansionDuration = GunnellSmokeVolume.MinimumExpansionDuration;
            volume.emissionDuration = 2.4f;
            volume.edgeParticles = true;
            volume.avoidObstacles = true;
            volume.obstacleMask = SmokeGameplay.EnvironmentMask;
            volume.obstacleRadius = volume.voxelSize * 0.4f;
            volume.infinite = true;
            volume.destroyOnFinish = false;
            volume.autoStart = true;

            go.SetActive(true); // NOW OnEnable() allocates the tall vertical domain!
            return volume;
        }

        void Update()
        {
            // 1. Flicker engine fire light
            if (_fireLight != null)
            {
                _fireLight.intensity = 1.8f + Mathf.PerlinNoise(Time.time * 14f, 0f) * 1.2f;
            }

            // 2. Process Interactive Aircraft (Helicopter / Drone) Downwash Clearance
            if (_isPlumeActive && _smokeVolume != null && _smokeVolume.Wake != null)
            {
                UpdateAircraftDownwashClearance();
            }

            // 3. Lifetime management (45 seconds of intense burning wreck)
            if (Time.time - _spawnTime > plumeLifetime)
            {
                FadeOutWreck();
            }
        }

        void UpdateAircraftDownwashClearance()
        {
            if (AircraftWakeSource.ActiveAircraft.Count == 0) return;

            Vector3 plumePos = _smokeVolume.transform.position;

            for (int i = 0; i < AircraftWakeSource.ActiveAircraft.Count; i++)
            {
                AircraftWakeSource aircraft = AircraftWakeSource.ActiveAircraft[i];
                if (aircraft == null) continue;

                Vector3 aircraftPos = aircraft.Position;
                Vector3 delta = plumePos - aircraftPos;
                float dist = delta.magnitude;

                if (dist > aircraftInteractionRadius) continue;

                // Vertical relative offset: clearing is active from slightly below to high above plume
                float vertRel = aircraftPos.y - plumePos.y;
                if (vertRel < -4.0f || vertRel > 45.0f) continue;

                // Rate-limit wake injection to 30 Hz
                if (Time.time < _nextWakeTime) continue;
                _nextWakeTime = Time.time + (1f / 30f);

                // Downwash stream: downward air thrust + radial push + aircraft flight velocity sweep
                Vector3 radialDir = new Vector3(delta.x, 0f, delta.z).normalized;
                Vector3 downwashFlow = Vector3.down * aircraft.downwashStrength + radialDir * 5.5f;

                // 1. Aerodynamic wake carve: clears out a corridor along the aircraft's path through the smoke plume
                _smokeVolume.Wake.AddMotion(
                    source: 9500 + _wreckId,
                    kind: 0,
                    a: aircraftPos,
                    b: plumePos + Vector3.down * 1.5f,
                    radius: aircraft.rotorRadius * 1.6f,
                    flow: downwashFlow,
                    clearing: aircraft.clearingFraction,
                    duration: 0.14f,
                    seed: Time.time,
                    sweep: aircraft.Velocity * 0.25f
                );

                // 2. High-speed close pass or direct fly-through: blast cavitation and vortex rings
                if (dist < 10.5f)
                {
                    _smokeVolume.Wake.AddExplosion(
                        aircraftPos + Vector3.down * 1.5f,
                        radius: 8.5f,
                        reach: 12.0f,
                        strength: 16.0f,
                        flow: downwashFlow
                    );
                }

                // 3. Blow and deflect particle system embers/smoke
                if (_fireSmokeParticleSystems != null)
                {
                    float blowDist = Mathf.Clamp01(1f - dist / aircraftInteractionRadius);
                    for (int p = 0; p < _fireSmokeParticleSystems.Length; p++)
                    {
                        var ps = _fireSmokeParticleSystems[p];
                        if (ps == null) continue;
                        var vel = ps.velocityOverLifetime;
                        if (vel.enabled)
                        {
                            vel.x = downwashFlow.x * blowDist * 0.4f;
                            vel.z = downwashFlow.z * blowDist * 0.4f;
                        }
                    }
                }
            }
        }

        void FadeOutWreck()
        {
            if (_smokeVolume != null)
            {
                _smokeVolume.infinite = false;
                _smokeVolume.sustainDuration = 2f;
                _smokeVolume.fadeDuration = 6f;
            }
            if (_vfxFireSmokeInstance != null)
            {
                var pss = _vfxFireSmokeInstance.GetComponentsInChildren<ParticleSystem>();
                for (int i = 0; i < pss.Length; i++) pss[i].Stop();
            }
            if (_fireParticles != null) _fireParticles.Stop();
            if (_fireLight != null) _fireLight.enabled = false;
            if (_fireAudio != null) _fireAudio.Stop();

            Destroy(gameObject, 12f);
        }

        // =========================================================================================
        // PROCEDURAL MESH HELPERS (Crushed Chassis, Cabin, Wheels, Debris)
        // =========================================================================================

        static Mesh CreateCrushedBodyMesh()
        {
            var m = new Mesh { name = "CrushedCarBody" };
            float hw = 0.90f, hl = 2.25f;
            // 18 vertices with crumpled hood accordion, smashed nose, flared wheel wells
            Vector3[] v = new Vector3[]
            {
                // Bottom chassis base (Z: rear +2.25 to front -1.75 crushed)
                new Vector3(-hw,         0.22f,  hl),        // 0 rear bot L
                new Vector3( hw,         0.22f,  hl),        // 1 rear bot R
                new Vector3( hw * 1.05f, 0.22f, -0.2f),      // 2 mid bot R
                new Vector3(-hw * 1.05f, 0.22f, -0.2f),      // 3 mid bot L
                new Vector3(-hw * 0.82f, 0.12f, -1.75f),     // 4 front bot L (smashed in from -2.25 to -1.75)
                new Vector3( hw * 0.82f, 0.12f, -1.75f),     // 5 front bot R (smashed in)
                
                // Beltline / waist
                new Vector3(-hw * 0.95f, 0.68f,  hl),        // 6 rear waist L
                new Vector3( hw * 0.95f, 0.68f,  hl),        // 7 rear waist R
                new Vector3( hw,         0.68f, -0.2f),      // 8 mid waist R
                new Vector3(-hw,         0.68f, -0.2f),      // 9 mid waist L
                new Vector3(-hw * 0.88f, 0.58f, -1.65f),     // 10 front waist L
                new Vector3( hw * 0.88f, 0.58f, -1.65f),     // 11 front waist R

                // Hood & Trunk deck
                new Vector3( 0f,         0.70f,  hl * 0.9f), // 12 trunk center
                new Vector3(-hw * 0.85f, 0.72f, -0.85f),     // 13 cowl L
                new Vector3( hw * 0.85f, 0.72f, -0.85f),     // 14 cowl R
                new Vector3( 0f,         1.05f, -1.25f),     // 15 BUCKLED HOOD PEAK (bent up into an accordion peak!)
                new Vector3(-hw * 0.75f, 0.55f, -1.65f),     // 16 front nose L
                new Vector3( hw * 0.75f, 0.55f, -1.65f),     // 17 front nose R
            };

            int[] tris = new int[]
            {
                // Floor
                0,1,2, 0,2,3, 3,2,5, 3,5,4,
                // Rear end
                0,6,7, 0,7,1, 6,12,7,
                // Rear sides
                1,7,8, 1,8,2, 0,3,9, 0,9,6,
                // Front sides (crumpled)
                2,8,11, 2,11,5, 3,4,10, 3,10,9,
                // Front bumper/nose
                4,5,11, 4,11,10, 10,11,17, 10,17,16,
                // Buckled Hood (peak at vertex 15)
                9,13,15, 9,15,8, 8,15,14,
                13,16,15, 14,15,17, 16,17,15,
                // Trunk deck
                6,9,12, 7,12,8, 8,12,14, 9,13,12
            };

            m.vertices = v;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateCrushedCabinMesh()
        {
            var m = new Mesh { name = "CrushedCabin" };
            float hw = 0.72f, frontZ = -0.65f, rearZ = 1.15f;
            // Smashed roof with bent A-pillars and collapsed windshield
            Vector3[] v = new Vector3[]
            {
                new Vector3(-hw,         0f,    rearZ),       // 0 rear base L
                new Vector3( hw,         0f,    rearZ),       // 1 rear base R
                new Vector3( hw * 0.95f, 0f,    frontZ),      // 2 front base R
                new Vector3(-hw * 0.95f, 0f,    frontZ),      // 3 front base L
                new Vector3(-hw * 0.85f, 0.62f, rearZ - 0.2f),// 4 rear roof L
                new Vector3( hw * 0.85f, 0.62f, rearZ - 0.2f),// 5 rear roof R
                new Vector3( hw * 0.68f, 0.38f, frontZ + 0.32f), // 6 front roof R (collapsed down)
                new Vector3(-hw * 0.68f, 0.44f, frontZ + 0.32f), // 7 front roof L (collapsed down)
            };
            int[] tris = new int[]
            {
                // Roof (caved in)
                4,6,5, 4,7,6,
                // Front Windshield (punched in)
                3,7,6, 3,6,2,
                // Rear window
                0,1,5, 0,5,4,
                // Sides
                0,4,7, 0,7,3,
                // Right side
                1,2,6, 1,6,5
            };
            m.vertices = v;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateBoxMesh(float w, float h, float d)
        {
            var m = new Mesh { name = "BoxDebris" };
            float hw = w * 0.5f, hh = h * 0.5f, hd = d * 0.5f;
            Vector3[] v = new Vector3[]
            {
                new Vector3(-hw,-hh,-hd), new Vector3(hw,-hh,-hd), new Vector3(hw,-hh,hd), new Vector3(-hw,-hh,hd),
                new Vector3(-hw, hh,-hd), new Vector3(hw, hh,-hd), new Vector3(hw, hh,hd), new Vector3(-hw, hh,hd)
            };
            int[] tris = new int[]
            {
                0,1,2, 0,2,3, 4,6,5, 4,7,6,
                0,4,5, 0,5,1, 2,6,7, 2,7,3,
                0,3,7, 0,7,4, 1,5,6, 1,6,2
            };
            m.vertices = v;
            m.triangles = tris;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateWheelMesh(float radius, float width, int segments)
        {
            var m = new Mesh { name = "WheelDebris" };
            var verts = new List<Vector3>();
            var tris = new List<int>();
            float halfW = width * 0.5f;

            for (int i = 0; i < segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                float y = Mathf.Sin(a) * radius;
                float z = Mathf.Cos(a) * radius;
                verts.Add(new Vector3(-halfW, y, z));
                verts.Add(new Vector3(halfW, y, z));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int i0 = i * 2, i1 = i * 2 + 1;
                int n0 = next * 2, n1 = next * 2 + 1;
                tris.Add(i0); tris.Add(n0); tris.Add(i1);
                tris.Add(i1); tris.Add(n0); tris.Add(n1);
            }

            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateDiscMesh(float radius, int segments)
        {
            var m = new Mesh { name = "DiscDecal" };
            var verts = new List<Vector3> { Vector3.zero };
            var tris = new List<int>();

            for (int i = 0; i <= segments; i++)
            {
                float a = (i / (float)segments) * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f));
            }
            for (int i = 1; i <= segments; i++)
            {
                tris.Add(0); tris.Add(i); tris.Add(i + 1);
            }
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            return m;
        }
    }
}
