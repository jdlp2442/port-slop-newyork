using System.Collections.Generic;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using Nyc.Rendering;

namespace Nyc.Gameplay.Traffic
{
    /// <summary>
    /// Instanced vehicle rendering: 4 batches (taxi / sedan / SUV / bus), one
    /// Graphics.RenderMeshInstanced call each, matrices built by a Burst job.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrafficRenderer : MonoBehaviour
    {
        [Header("Materials (Nyc/VehiclePaint)")]
        public Material taxiMaterial;
        public Material sedanMaterial;
        public Material suvMaterial;
        public Material busMaterial;

        [Header("Capacity")]
        public int capacity = 512;

        [Header("Experimental BatchRendererGroup (Unity 6)")]
        [Tooltip("When enabled, vehicle batches use BatchRendererGroup with custom culling; falls back to RenderMeshInstanced if unavailable.")]
        public bool useBRG = true;
        public bool useBRGForBatch0
        {
            get => useBRG;
            set => useBRG = value;
        }

        [Header("Vehicle Lights")]
        public VehicleLightsRenderer lightsRenderer;

        NycVehicleBRGBackend[] _brgBackends;
        Mesh[] _meshes;
        Material[] _materials;
        NativeArray<VehicleInstance>[] _instances;
        NativeArray<Matrix4x4>[] _matrices;
        RenderParams[] _params;
        bool _ready;

        static readonly Color TaxiColor = new Color(1f, 0.745f, 0.078f);
        static readonly Color SedanColor = new Color(0.62f, 0.64f, 0.66f);
        static readonly Color SuvColor = new Color(0.16f, 0.17f, 0.19f);
        static readonly Color BusColor = new Color(0.72f, 0.76f, 0.80f);

        void Awake() => EnsureInit();

        public void EnsureInit()
        {
            if (_ready) return;

            _meshes = new[]
            {
                VehicleMeshFactory.CreateCar(false, true),  // 0: Taxi
                VehicleMeshFactory.CreateCar(false, false), // 1: Sedan
                VehicleMeshFactory.CreateCar(true, false),  // 2: SUV
                VehicleMeshFactory.CreateBus(),             // 3: Bus
                VehicleMeshFactory.CreatePoliceCar(),       // 4: Police
                VehicleMeshFactory.CreateTruck()            // 5: Truck
            };

            _materials = new[]
            {
                taxiMaterial ? taxiMaterial : MakeMaterial("Taxi", TaxiColor, 0.66f, 0.30f),
                sedanMaterial ? sedanMaterial : MakeMaterial("Sedan", SedanColor, 0.70f, 0.45f),
                suvMaterial ? suvMaterial : MakeMaterial("SUV", SuvColor, 0.55f, 0.25f),
                busMaterial ? busMaterial : MakeMaterial("Bus", BusColor, 0.42f, 0.05f),
                MakeMaterial("Police", new Color(0.96f, 0.98f, 1.0f), 0.70f, 0.35f),
                MakeMaterial("Truck", new Color(0.85f, 0.85f, 0.82f), 0.35f, 0.10f)
            };

            int batchCount = _meshes.Length;
            _instances = new NativeArray<VehicleInstance>[batchCount];
            _matrices = new NativeArray<Matrix4x4>[batchCount];
            _params = new RenderParams[batchCount];

            for (int i = 0; i < batchCount; i++)
            {
                _instances[i] = new NativeArray<VehicleInstance>(capacity, Allocator.Persistent);
                _matrices[i] = new NativeArray<Matrix4x4>(capacity, Allocator.Persistent);
                _params[i] = new RenderParams(_materials[i])
                {
                    worldBounds = new Bounds(Vector3.zero, Vector3.one * 30000f),
                    shadowCastingMode = ShadowCastingMode.On,
                    receiveShadows = true
                };
            }

            if (useBRG && _meshes.Length > 0 && _materials.Length > 0)
            {
                _brgBackends = new NycVehicleBRGBackend[_meshes.Length];
                for (int i = 0; i < _meshes.Length; i++)
                {
                    var backend = new NycVehicleBRGBackend();
                    if (backend.Initialize(_meshes[i], _materials[i], capacity))
                    {
                        _brgBackends[i] = backend;
                    }
                    else
                    {
                        backend.Dispose();
                        _brgBackends[i] = null;
                    }
                }
            }

            _ready = true;
        }

        static Material MakeMaterial(string name, Color color, float smoothness, float metallic)
        {
            Shader shader = Shader.Find("Nyc/VehiclePaint");
            if (!shader) shader = Shader.Find("Universal Render Pipeline/Lit");

            var m = new Material(shader) { name = "Vehicle_" + name };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            // RenderMeshInstanced requires this flag on every material in the batch.
            m.enableInstancing = true;
            return m;
        }

        public void Render(List<VehicleInstance>[] batches)
        {
            EnsureInit();

            for (int b = 0; b < batches.Length && b < _meshes.Length; b++)
            {
                int count = Mathf.Min(batches[b].Count, capacity);
                if (count == 0) continue;

                for (int i = 0; i < count; i++) _instances[b][i] = batches[b][i];

                var job = new BuildVehicleMatricesJob
                {
                    Instances = _instances[b],
                    Matrices = _matrices[b]
                };
                job.Schedule(count, 64).Complete();

                if (useBRG && _brgBackends != null && b < _brgBackends.Length && _brgBackends[b] != null && _brgBackends[b].IsAvailable)
                {
                    _brgBackends[b].UpdateInstances(_matrices[b], count);
                }
                else
                {
                    Graphics.RenderMeshInstanced(_params[b], _meshes[b], 0, _matrices[b], count);
                }
            }

            if (lightsRenderer != null)
            {
                float night = LightingSystem.Instance != null ? LightingSystem.Instance.NightFactor : 0f;
                lightsRenderer.RenderVehicleLights(batches, night);
            }
        }

        void OnDestroy()
        {
            if (_brgBackends != null)
            {
                for (int i = 0; i < _brgBackends.Length; i++)
                {
                    _brgBackends[i]?.Dispose();
                    _brgBackends[i] = null;
                }
                _brgBackends = null;
            }

            if (!_ready) return;
            for (int i = 0; i < _instances.Length; i++)
            {
                if (_instances[i].IsCreated) _instances[i].Dispose();
                if (_matrices[i].IsCreated) _matrices[i].Dispose();
            }
        }
    }
}
