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
        public bool useBRG = false;
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
        // MTA shell is pearl white; blue/cyan belt details are emitted by VehiclePaint parts 5/6.
        static readonly Color BusColor = new Color(0.94f, 0.96f, 0.98f);

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
                InitializeBRGPilot();
            }

            _ready = true;
        }

        /// <summary>Rebuilds the single BRG pilot after changing the toggle in the Inspector.</summary>
        public bool ReinitializeBRGPilot()
        {
            if (!_ready) EnsureInit();
            DisposeBRGBackends();
            if (!useBRG || _meshes == null || _materials == null) return false;
            InitializeBRGPilot();
            return _brgBackends != null && _brgBackends.Length > 0 && _brgBackends[0] != null && _brgBackends[0].IsAvailable;
        }

        void InitializeBRGPilot()
        {
            _brgBackends = new NycVehicleBRGBackend[_meshes.Length];
            // Pilot only the first (taxi) batch. Every other batch remains on the proven
            // RenderMeshInstanced path until BRG shader/culling parity is measured.
            var backend = new NycVehicleBRGBackend();
            if (backend.Initialize(_meshes[0], _materials[0], capacity)) _brgBackends[0] = backend;
            else backend.Dispose();
        }

        void DisposeBRGBackends()
        {
            if (_brgBackends == null) return;
            for (int i = 0; i < _brgBackends.Length; i++) _brgBackends[i]?.Dispose();
            _brgBackends = null;
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

                if (useBRG && b == 0 && _brgBackends != null && b < _brgBackends.Length && _brgBackends[b] != null && _brgBackends[b].IsAvailable)
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
            DisposeBRGBackends();

            if (!_ready || _instances == null || _matrices == null) return;
            int count = Mathf.Min(_instances.Length, _matrices.Length);
            for (int i = 0; i < count; i++)
            {
                if (_instances[i].IsCreated) _instances[i].Dispose();
                if (_matrices[i].IsCreated) _matrices[i].Dispose();
            }
        }
    }
}
