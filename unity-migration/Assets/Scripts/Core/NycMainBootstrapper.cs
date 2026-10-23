using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Nyc.Data;
using Nyc.Generation;
using Nyc.Gameplay;
using Nyc.Gameplay.Traffic;
using Nyc.Gameplay.Pedestrians;
using Nyc.Rendering;
using Nyc.UI;

namespace Nyc.Core
{
    /// <summary>
    /// Master orchestrator for the NYC 3D Simulation in Unity (URP).
    /// Line-by-line equivalent of slop-newyork-main/js/main.js initApp() & animation loop:
    /// Coordinates initialization, system wiring, execution order, and per-frame update cycles:
    /// GeoData -> Mesh Builders -> Spatial Index & Colliders -> Lighting & Post-Processing ->
    /// Traffic & Pedestrians -> Controllers & Cameras -> Culling & UI / Minimap.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    public sealed class NycMainBootstrapper : MonoBehaviour
    {
        [Header("Configuration")]
        public bool autoStartOnAwake = true;
        public TravelMode initialTravelMode = TravelMode.Car;
        public float startHour = 17.75f; // Golden hour (matching web version)

        [Header("Core Systems")]
        public LightingSystem lightingSystem;
        public PostProcessingController postProcessing;
        public ManhattanMeshBuilder cityMeshBuilder;
        public LandmarkMeshBuilder landmarkBuilder;
        public BoroughsMeshBuilder boroughsBuilder;
        public TreeSystem treeSystem;
        public StreetDetailsSystem streetDetailsSystem;
        public SceneCullingSystem cullingSystem;

        [Header("Collision & Gameplay")]
        public CollisionWorld collisionWorld;
        public RoadNetwork roadNetwork;
        public TrafficSystem trafficSystem;
        public TrafficRenderer trafficRenderer;
        public VehicleLightsRenderer vehicleLightsRenderer;
        public PedestrianCrowd pedestrianCrowd;
        public ThirdPersonControllerManager controllerManager;
        public CameraManager cameraManager;

        [Header("User Interface")]
        public UIDocument hudDocument;
        public HUDController hudController;
        public NycMapSystem mapSystem;
        public TouchControlsController touchControls;

        public static NycMainBootstrapper Instance { get; private set; }
        public bool IsInitialized { get; private set; }

        public event Action OnCityReady;

        void Awake()
        {
            Instance = this;
            if (autoStartOnAwake)
            {
                StartCoroutine(BootSequence());
            }
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// Asynchronous boot sequence matching main.js initApp().
        /// </summary>
        public IEnumerator BootSequence()
        {
            if (IsInitialized) yield break;

            Debug.Log("[NYC Bootstrapper] Starting NYC World Initialization Sequence...");

            // 1. Load GeoData (JSON / StreamingAssets)
            yield return StartCoroutine(NycGeoDataLoader.LoadAsync(
                data => OnGeoDataLoaded(data),
                err => Debug.LogWarning($"[NYC Bootstrapper] GeoData load warning: {err}")
            ));

            if (!CityConfig.Ready)
            {
                Debug.LogWarning("[NYC Bootstrapper] GeoData not loaded via StreamingAssets, using cached CityConfig fallback.");
            }

            // 2. Initialize Lighting & Post-Processing Stack
            InitLightingAndPostFX();

            // 3. Build Procedural City Geometry & Landmarks
            InitProceduralMeshes();

            // 4. Initialize Collision World & Spatial Grid
            InitCollisionAndSpatialIndex();

            // 5. Initialize Traffic & Pedestrian Simulation
            InitTrafficAndPedestrians();

            // 6. Initialize Player Controllers, Visual Rigs & Camera
            InitControllersAndCamera();

            // 7. Initialize Scene Culling
            InitSceneCulling();

            // 8. Initialize HUD, Map & Touch Controls
            InitUIAndMinimap();

            IsInitialized = true;
            Debug.Log("[NYC Bootstrapper] NYC World Bootstrapping Complete. Ready for Play!");
            OnCityReady?.Invoke();
        }

        void OnGeoDataLoaded(NycGeoData data)
        {
            if (data != null)
            {
                CityConfig.Initialize(data);
                Debug.Log($"[NYC Bootstrapper] GeoData loaded: {data.Blocks?.Count ?? 0} blocks, {data.Roads?.Count ?? 0} roads, {data.Streets?.Count ?? 0} streets.");
            }
        }

        void InitLightingAndPostFX()
        {
            if (!lightingSystem) lightingSystem = FindAnyObjectByType<LightingSystem>();
            if (!postProcessing) postProcessing = FindAnyObjectByType<PostProcessingController>();

            if (lightingSystem)
            {
                lightingSystem.SetHour(startHour);
            }

            if (postProcessing)
            {
                postProcessing.EnsureInit();
                postProcessing.UpdateByHour(startHour);
            }
        }

        void InitProceduralMeshes()
        {
            if (!cityMeshBuilder) cityMeshBuilder = FindAnyObjectByType<ManhattanMeshBuilder>();
            if (!landmarkBuilder) landmarkBuilder = FindAnyObjectByType<LandmarkMeshBuilder>();
            if (!boroughsBuilder) boroughsBuilder = FindAnyObjectByType<BoroughsMeshBuilder>();
            if (!treeSystem) treeSystem = FindAnyObjectByType<TreeSystem>();
            if (!streetDetailsSystem) streetDetailsSystem = FindAnyObjectByType<StreetDetailsSystem>();

            if (cityMeshBuilder) cityMeshBuilder.Build();
            if (landmarkBuilder) landmarkBuilder.Build();
            if (boroughsBuilder) boroughsBuilder.Build();
            if (treeSystem && CityConfig.Geo != null) treeSystem.Initialize(CityConfig.Geo);
            if (streetDetailsSystem && CityConfig.Geo != null) streetDetailsSystem.Initialize(CityConfig.Geo);
        }

        void InitCollisionAndSpatialIndex()
        {
            if (!collisionWorld) collisionWorld = FindAnyObjectByType<CollisionWorld>();

            if (collisionWorld)
            {
                var boxes = new List<CollisionBox>();

                if (cityMeshBuilder != null) boxes.AddRange(cityMeshBuilder.CollisionBoxes);
                if (landmarkBuilder != null) boxes.AddRange(landmarkBuilder.CollisionBoxes);
                if (boroughsBuilder != null) boxes.AddRange(boroughsBuilder.CollisionBoxes);

                collisionWorld.Build(boxes);

                if (CityConfig.Ready && CityConfig.Geo?.Manhattan != null)
                {
                    collisionWorld.SetIslandBounds(CityConfig.ManhattanBounds);
                }
            }
        }

        void InitTrafficAndPedestrians()
        {
            if (!roadNetwork) roadNetwork = FindAnyObjectByType<RoadNetwork>();
            if (!trafficSystem) trafficSystem = FindAnyObjectByType<TrafficSystem>();
            if (!trafficRenderer) trafficRenderer = FindAnyObjectByType<TrafficRenderer>();
            if (!vehicleLightsRenderer) vehicleLightsRenderer = FindAnyObjectByType<VehicleLightsRenderer>();
            if (!pedestrianCrowd) pedestrianCrowd = FindAnyObjectByType<PedestrianCrowd>();

            if (vehicleLightsRenderer) vehicleLightsRenderer.EnsureInit();

            if (trafficRenderer && vehicleLightsRenderer)
            {
                trafficRenderer.lightsRenderer = vehicleLightsRenderer;
            }

            if (trafficSystem)
                trafficSystem.pedestrianCrowd = pedestrianCrowd;

            if (trafficSystem)
            {
                // TrafficSystem expects a populated lane graph. The editor bake creates the
                // RoadNetwork component, but the graph must still be built after GeoData loads
                // when entering Play from the saved scene.
                if (roadNetwork != null && roadNetwork.Lanes.Length == 0 && CityConfig.Geo != null)
                    roadNetwork.Build(CityConfig.Geo);
                trafficSystem.network = roadNetwork;
                trafficSystem.Build();
            }

            if (pedestrianCrowd)
            {
                pedestrianCrowd.RebuildNow();
            }
        }

        void InitControllersAndCamera()
        {
            if (!controllerManager) controllerManager = FindAnyObjectByType<ThirdPersonControllerManager>();
            if (!cameraManager) cameraManager = FindAnyObjectByType<CameraManager>();

            if (controllerManager)
            {
                controllerManager.startMode = initialTravelMode;
                controllerManager.SpawnAll();
                controllerManager.SetMode(initialTravelMode);
                ActorModelFactory.AttachActorModels(controllerManager);
            }

            if (cameraManager && controllerManager)
            {
                cameraManager.controllers = controllerManager;
            }
        }

        void InitSceneCulling()
        {
            if (!cullingSystem) cullingSystem = FindAnyObjectByType<SceneCullingSystem>();

            if (cullingSystem)
            {
                Transform cityRoot = cityMeshBuilder ? cityMeshBuilder.transform : null;
                Transform detailsRoot = streetDetailsSystem ? streetDetailsSystem.transform : null;
                cullingSystem.RegisterCityRoots(cityRoot, detailsRoot);
            }
        }

        void InitUIAndMinimap()
        {
            if (!hudDocument) hudDocument = FindAnyObjectByType<UIDocument>();
            if (!hudController) hudController = FindAnyObjectByType<HUDController>();
            if (!mapSystem) mapSystem = FindAnyObjectByType<NycMapSystem>();
            if (!touchControls) touchControls = FindAnyObjectByType<TouchControlsController>();

            if (hudController && controllerManager)
            {
                hudController.controllers = controllerManager;
            }

            if (touchControls)
            {
                touchControls.controllerManager = controllerManager;
                touchControls.SetMode(initialTravelMode);
            }

            if (mapSystem && hudDocument)
            {
                mapSystem.Initialize(hudDocument.rootVisualElement);
            }
        }

        void Update()
        {
            if (!IsInitialized) return;

            // Day / night cycle progression
            if (lightingSystem != null && postProcessing != null)
            {
                postProcessing.UpdateByHour(lightingSystem.Hour);
            }
        }
    }
}
