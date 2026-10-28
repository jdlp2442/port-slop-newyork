using System.Collections.Generic;
using UnityEngine;
using Nyc.Core;
using Nyc.Gameplay.Traffic;
using GunnellSmoke;

namespace Nyc.Gameplay.Effects
{
    /// <summary>
    /// Manages high-impact vehicle crash explosions and spawns interactive volumetric CS2 smoke clouds.
    /// Combines particle shockwaves, embers, debris, 3D spatial audio, and volumetric fluid simulation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NycVehicleExplosionManager : MonoBehaviour
    {
        public static NycVehicleExplosionManager Instance { get; private set; }

        [Header("Explosion VFX Prefabs")]
        public GameObject[] explosionPrefabs;

        [Header("Audio")]
        public AudioClip[] explosionClips;
        [Range(0f, 1f)] public float soundVolume = 0.85f;

        [Header("Volumetric Smoke")]
        public bool spawnVolumetricSmoke = true;
        public float smokeSustainDuration = 14f;
        public float smokeReleaseDuration = 0.6f;

        [Header("Limits & Cooldown")]
        public float minImpactSpeed = 9.5f;
        public float explosionCooldown = 0.45f;
        public int maxSimultaneousClouds = 8;

        float _lastExplosionTime = -10f;
        Vector3 _lastExplosionPos;
        readonly List<GunnellSmokeVolume> _activeClouds = new List<GunnellSmokeVolume>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInit()
        {
            if (Instance == null)
            {
                var go = new GameObject("NycVehicleExplosionManager");
                Instance = go.AddComponent<NycVehicleExplosionManager>();
            }
        }

        void Awake()
        {
            if (Instance == null) Instance = this;
            LoadResourcesIfNeeded();
        }

        void OnEnable()
        {
            TrafficSystem.OnVehicleCrashExplosion += HandleVehicleCrash;
        }

        void OnDisable()
        {
            TrafficSystem.OnVehicleCrashExplosion -= HandleVehicleCrash;
        }

        private void HandleVehicleCrash(Vector3 worldPosition, float impactSpeed)
        {
            TriggerCrashExplosion(worldPosition, impactSpeed, true);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void LoadResourcesIfNeeded()
        {
            if (explosionPrefabs == null || explosionPrefabs.Length == 0)
            {
                var list = new List<GameObject>();
                for (int i = 1; i <= 4; i++)
                {
                    var p = Resources.Load<GameObject>($"Prefabs/Explosions/Explosion FREE {i} Variant");
                    if (!p) p = Resources.Load<GameObject>($"Explosions/Explosion FREE {i} Variant");
#if UNITY_EDITOR
                    if (!p)
                    {
                        p = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                            $"Assets/Mirza Beig/Cinematic Explosions FREE/Prefabs/Explosions/Explosion FREE {i} Variant.prefab");
                    }
#endif
                    if (p) list.Add(p);
                }
                if (list.Count > 0) explosionPrefabs = list.ToArray();
            }

            if (explosionClips == null || explosionClips.Length == 0)
            {
                var list = new List<AudioClip>();
                for (int i = 1; i <= 3; i++)
                {
                    var c = Resources.Load<AudioClip>($"Audio/Explosions/Explosion{i}");
#if UNITY_EDITOR
                    if (!c)
                    {
                        c = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(
                            $"Assets/Audio/Explosions/Explosion{i}.wav");
                    }
#endif
                    if (c) list.Add(c);
                }
                if (list.Count > 0) explosionClips = list.ToArray();
            }
        }

        /// <summary>
        /// Public static trigger for vehicle crashes and collisions.
        /// </summary>
        public static void TriggerCrashExplosion(Vector3 worldPosition, float impactSpeed, bool spawnStandaloneWreck = true)
        {
            if (Instance == null)
            {
                var go = new GameObject("NycVehicleExplosionManager");
                Instance = go.AddComponent<NycVehicleExplosionManager>();
            }

            Instance.ExecuteExplosion(worldPosition, impactSpeed, spawnStandaloneWreck);
        }

        void ExecuteExplosion(Vector3 pos, float impactSpeed, bool spawnStandaloneWreck = true)
        {
            if (impactSpeed < minImpactSpeed) return;
            if (Time.time - _lastExplosionTime < explosionCooldown && Vector3.Distance(pos, _lastExplosionPos) < 12f)
                return;

            _lastExplosionTime = Time.time;
            _lastExplosionPos = pos;

            // 1. Spawn Particle System Explosion
            if (explosionPrefabs != null && explosionPrefabs.Length > 0)
            {
                GameObject prefab = explosionPrefabs[Random.Range(0, explosionPrefabs.Length)];
                if (prefab != null)
                {
                    GameObject vfx = Instantiate(prefab, pos + Vector3.up * 0.4f, Quaternion.identity);
                    vfx.name = "VehicleCrash_Explosion";
                    Destroy(vfx, 6.0f);
                }
            }

            // 2. Play Spatialized 3D Explosion Audio
            if (explosionClips != null && explosionClips.Length > 0)
            {
                AudioClip clip = explosionClips[Random.Range(0, explosionClips.Length)];
                if (clip != null)
                {
                    AudioSource.PlayClipAtPoint(clip, pos, soundVolume);
                }
            }

            // 3. Volumetric Shockwave Carving Cavity
            SmokeGameplay.AddExplosion(pos, radius: 8.5f, strength: 14f, carveRadius: 4.8f);

            // 4. Destroy Car & Spawn Burning Vehicle Wreck with Rising Smoke Plume (Delayed Plume Ignition)
            if (spawnVolumetricSmoke && spawnStandaloneWreck)
            {
                NycBurningCarWreck.SpawnWreck(pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f), impactSpeed);
            }
        }

        void CleanCloudList()
        {
            for (int i = _activeClouds.Count - 1; i >= 0; i--)
            {
                if (_activeClouds[i] == null) _activeClouds.RemoveAt(i);
            }
        }
    }
}
