using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;
using Nyc.Generation;
using Nyc.Gameplay;
using Nyc.Gameplay.Traffic;
using Nyc.Gameplay.Pedestrians;
using Nyc.Rendering;
using Nyc.UI;
using Nyc.Core;

namespace Nyc.EditorTools
{
    /// <summary>
    /// Editor entry points for the procedural Manhattan bake and 1-Click playable scene creation.
    /// Menu: NYC/...
    /// </summary>
    public static class ManhattanBakeMenu
    {
        const string Root = "Assets/Generated";

        [MenuItem("NYC/Build Complete Playable Scene", priority = -10)]
        public static void BuildCompletePlayableScene()
        {
            Debug.Log("[NYC] Starting 1-Click Complete Playable Scene Setup...");

            // 1. Ensure GeoData is imported into StreamingAssets
            NycGeoDataImporter.ImportGeoData(runNodeBuilder: false);

            // 2. Ensure Default Materials exist
            CreateMaterials();

            // 3. Ensure Time of Day Presets exist
            NycPresetCreator.CreatePresets();

            // 4. Create or Locate NYC_World Master Hierarchy
            var rootGo = GameObject.Find("NYC_World");
            if (!rootGo) rootGo = new GameObject("NYC_World");

            // Directional Light (Sun)
            var sunGo = GameObject.Find("Directional Light (Sun)");
            if (!sunGo)
            {
                sunGo = new GameObject("Directional Light (Sun)");
                sunGo.transform.SetParent(rootGo.transform, false);
                var light = sunGo.AddComponent<Light>();
                light.color = new Color(1f, 0.92f, 0.82f);
                light.intensity = 5.8f;
                light.shadows = LightShadows.Soft;
                sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            var lightingSystem = rootGo.GetComponent<LightingSystem>();
            if (!lightingSystem) lightingSystem = rootGo.AddComponent<LightingSystem>();

            // Post-Processing Volume
            var volGo = GameObject.Find("Global_Volume");
            if (!volGo)
            {
                volGo = new GameObject("Global_Volume");
                volGo.transform.SetParent(rootGo.transform, false);
            }
            var vol = volGo.GetComponent<Volume>();
            if (!vol) vol = volGo.AddComponent<Volume>();
            var postFX = volGo.GetComponent<PostProcessingController>();
            if (!postFX) postFX = volGo.AddComponent<PostProcessingController>();
            postFX.volume = vol;

            // Procedural Mesh Builders
            var meshBuilder = FindOrCreateBuilder();
            meshBuilder.transform.SetParent(rootGo.transform, false);

            var landmarkGo = GameObject.Find("Landmarks");
            if (!landmarkGo)
            {
                landmarkGo = new GameObject("Landmarks");
                landmarkGo.transform.SetParent(rootGo.transform, false);
            }
            var landmarkBuilder = landmarkGo.GetComponent<LandmarkMeshBuilder>();
            if (!landmarkBuilder) landmarkBuilder = landmarkGo.AddComponent<LandmarkMeshBuilder>();

            var boroughsGo = GameObject.Find("Boroughs");
            if (!boroughsGo)
            {
                boroughsGo = new GameObject("Boroughs");
                boroughsGo.transform.SetParent(rootGo.transform, false);
            }
            var boroughsBuilder = boroughsGo.GetComponent<BoroughsMeshBuilder>();
            if (!boroughsBuilder) boroughsBuilder = boroughsGo.AddComponent<BoroughsMeshBuilder>();

            // Vegetation & Street Details
            var treeGo = GameObject.Find("Vegetation");
            if (!treeGo)
            {
                treeGo = new GameObject("Vegetation");
                treeGo.transform.SetParent(rootGo.transform, false);
            }
            var treeSys = treeGo.GetComponent<TreeSystem>();
            if (!treeSys) treeSys = treeGo.AddComponent<TreeSystem>();

            var streetDetailsGo = GameObject.Find("StreetDetails");
            if (!streetDetailsGo)
            {
                streetDetailsGo = new GameObject("StreetDetails");
                streetDetailsGo.transform.SetParent(rootGo.transform, false);
            }
            var streetDetailsSys = streetDetailsGo.GetComponent<StreetDetailsSystem>();
            if (!streetDetailsSys) streetDetailsSys = streetDetailsGo.AddComponent<StreetDetailsSystem>();

            // Collision World
            var world = Object.FindFirstObjectByType<CollisionWorld>();
            if (!world)
            {
                var go = new GameObject("CollisionWorld");
                go.transform.SetParent(rootGo.transform, false);
                world = go.AddComponent<CollisionWorld>();
            }

            // Road Network & Traffic
            var net = Object.FindFirstObjectByType<RoadNetwork>();
            if (!net)
            {
                var go = new GameObject("RoadNetwork");
                go.transform.SetParent(rootGo.transform, false);
                net = go.AddComponent<RoadNetwork>();
            }

            var trafficGo = GameObject.Find("Traffic");
            if (!trafficGo)
            {
                trafficGo = new GameObject("Traffic");
                trafficGo.transform.SetParent(rootGo.transform, false);
            }
            var trafficSys = trafficGo.GetComponent<TrafficSystem>();
            if (!trafficSys) trafficSys = trafficGo.AddComponent<TrafficSystem>();
            var trafficRend = trafficGo.GetComponent<TrafficRenderer>();
            if (!trafficRend) trafficRend = trafficGo.AddComponent<TrafficRenderer>();
            var vehicleLights = trafficGo.GetComponent<VehicleLightsRenderer>();
            if (!vehicleLights) vehicleLights = trafficGo.AddComponent<VehicleLightsRenderer>();
            trafficRend.lightsRenderer = vehicleLights;

            var crowdGo = GameObject.Find("Pedestrians");
            if (!crowdGo)
            {
                crowdGo = new GameObject("Pedestrians");
                crowdGo.transform.SetParent(rootGo.transform, false);
            }
            var crowd = crowdGo.GetComponent<PedestrianCrowd>();
            if (!crowd) crowd = crowdGo.AddComponent<PedestrianCrowd>();

            // Player Controllers
            var playerGo = GameObject.Find("PlayerControllers");
            if (!playerGo)
            {
                playerGo = new GameObject("PlayerControllers");
                playerGo.transform.SetParent(rootGo.transform, false);
            }
            var ctrlMgr = playerGo.GetComponent<ThirdPersonControllerManager>();
            if (!ctrlMgr) ctrlMgr = playerGo.AddComponent<ThirdPersonControllerManager>();
            var inputReader = playerGo.GetComponent<PlayerInputReader>();
            if (!inputReader) inputReader = playerGo.AddComponent<PlayerInputReader>();
            ctrlMgr.input = inputReader;
            ctrlMgr.collision = world;

            // 4 Vehicle / Character Entities
            var personGo = playerGo.transform.Find("Person")?.gameObject;
            if (!personGo) { personGo = new GameObject("Person"); personGo.transform.SetParent(playerGo.transform, false); }
            var personCtrl = personGo.GetComponent<PedestrianController>();
            if (!personCtrl) personCtrl = personGo.AddComponent<PedestrianController>();
            ctrlMgr.person = personCtrl;

            var carGo = playerGo.transform.Find("Car")?.gameObject;
            if (!carGo) { carGo = new GameObject("Car"); carGo.transform.SetParent(playerGo.transform, false); }
            var carCtrl = carGo.GetComponent<CarController>();
            if (!carCtrl) carCtrl = carGo.AddComponent<CarController>();
            ctrlMgr.car = carCtrl;

            var bikeGo = playerGo.transform.Find("Bike")?.gameObject;
            if (!bikeGo) { bikeGo = new GameObject("Bike"); bikeGo.transform.SetParent(playerGo.transform, false); }
            var bikeCtrl = bikeGo.GetComponent<BikeController>();
            if (!bikeCtrl) bikeCtrl = bikeGo.AddComponent<BikeController>();
            ctrlMgr.bike = bikeCtrl;

            var heliGo = playerGo.transform.Find("Helicopter")?.gameObject;
            if (!heliGo) { heliGo = new GameObject("Helicopter"); heliGo.transform.SetParent(playerGo.transform, false); }
            var heliCtrl = heliGo.GetComponent<HelicopterController>();
            if (!heliCtrl) heliCtrl = heliGo.AddComponent<HelicopterController>();
            ctrlMgr.helicopter = heliCtrl;

            // Camera System
            var camGo = GameObject.Find("CameraSystem");
            if (!camGo)
            {
                camGo = new GameObject("CameraSystem");
                camGo.transform.SetParent(rootGo.transform, false);
            }
            var camSys = camGo.GetComponent<CameraManager>();
            if (!camSys) camSys = camGo.AddComponent<CameraManager>();
            camSys.controllers = ctrlMgr;
            if (Camera.main) camSys.cam = Camera.main;

            // Scene Culling System
            var cullingGo = GameObject.Find("SceneCulling");
            if (!cullingGo)
            {
                cullingGo = new GameObject("SceneCulling");
                cullingGo.transform.SetParent(rootGo.transform, false);
            }
            var cullingSys = cullingGo.GetComponent<SceneCullingSystem>();
            if (!cullingSys) cullingSys = cullingGo.AddComponent<SceneCullingSystem>();

            // UI Document, HUD & Touch Controls
            var uiGo = GameObject.Find("UI_HUD");
            if (!uiGo)
            {
                uiGo = new GameObject("UI_HUD");
                uiGo.transform.SetParent(rootGo.transform, false);
            }
            var uiDoc = uiGo.GetComponent<UIDocument>();
            if (!uiDoc) uiDoc = uiGo.AddComponent<UIDocument>();

            var hudCtrl = uiGo.GetComponent<HUDController>();
            if (!hudCtrl) hudCtrl = uiGo.AddComponent<HUDController>();
            hudCtrl.controllers = ctrlMgr;
            hudCtrl.cameras = camSys;
            hudCtrl.lighting = lightingSystem;

            var mapSys = uiGo.GetComponent<NycMapSystem>();
            if (!mapSys) mapSys = uiGo.AddComponent<NycMapSystem>();
            mapSys.controllers = ctrlMgr;

            var touchCtrl = uiGo.GetComponent<TouchControlsController>();
            if (!touchCtrl) touchCtrl = uiGo.AddComponent<TouchControlsController>();
            touchCtrl.uiDocument = uiDoc;
            touchCtrl.inputReader = inputReader;
            touchCtrl.controllerManager = ctrlMgr;

            // Main Orchestrator Bootstrapper
            var bootstrapper = rootGo.GetComponent<NycMainBootstrapper>();
            if (!bootstrapper) bootstrapper = rootGo.AddComponent<NycMainBootstrapper>();
            bootstrapper.lightingSystem = lightingSystem;
            bootstrapper.postProcessing = postFX;
            bootstrapper.cityMeshBuilder = meshBuilder;
            bootstrapper.landmarkBuilder = landmarkBuilder;
            bootstrapper.boroughsBuilder = boroughsBuilder;
            bootstrapper.treeSystem = treeSys;
            bootstrapper.streetDetailsSystem = streetDetailsSys;
            bootstrapper.cullingSystem = cullingSys;
            bootstrapper.collisionWorld = world;
            bootstrapper.trafficSystem = trafficSys;
            bootstrapper.trafficRenderer = trafficRend;
            bootstrapper.vehicleLightsRenderer = vehicleLights;
            bootstrapper.pedestrianCrowd = crowd;
            bootstrapper.controllerManager = ctrlMgr;
            bootstrapper.cameraManager = camSys;
            bootstrapper.hudDocument = uiDoc;
            bootstrapper.hudController = hudCtrl;
            bootstrapper.mapSystem = mapSys;
            bootstrapper.touchControls = touchCtrl;

            EditorUtility.SetDirty(rootGo);
            EditorSceneMarkDirty();
            Debug.Log("[NYC] Complete Playable Scene Hierarchy Created Successfully! Press Play to run.");
        }

        [MenuItem("NYC/1. Create Default Materials", priority = 1)]
        public static void CreateMaterials()
        {
            Directory.CreateDirectory(Root + "/Materials");
            var facade = Make("Facade", new Color(0.72f, 0.68f, 0.60f), 0.15f, 0.0f);
            var solid  = Make("Solid",  new Color(0.62f, 0.60f, 0.56f), 0.10f, 0.0f);
            var walk   = Make("Sidewalk", new Color(0.78f, 0.77f, 0.74f), 0.05f, 0.0f);
            var road   = Make("Road", new Color(0.16f, 0.16f, 0.17f), 0.35f, 0.0f);
            var park   = Make("Park", new Color(0.42f, 0.52f, 0.29f), 0.02f, 0.0f);
            var land   = Make("Land", new Color(0.45f, 0.45f, 0.44f), 0.02f, 0.0f);
            var water  = Make("Water", new Color(0.10f, 0.17f, 0.20f), 0.92f, 0.0f);

            var builder = FindOrCreateBuilder();
            builder.facadeMaterial = facade;
            builder.solidMaterial = solid;
            builder.sidewalkMaterial = walk;
            builder.roadMaterial = road;
            builder.parkMaterial = park;
            builder.landMaterial = land;
            builder.waterMaterial = water;
            EditorUtility.SetDirty(builder);
            AssetDatabase.SaveAssets();
            Selection.activeObject = builder;
            Debug.Log("[NYC] Default URP materials created in " + Root + "/Materials");
        }

        [MenuItem("NYC/2. Build Manhattan Mesh", priority = 2)]
        public static void BuildManhattan()
        {
            var builder = FindOrCreateBuilder();
            if (builder.facadeMaterial == null) CreateMaterials();
            builder.Build();
            EditorUtility.SetDirty(builder);
            EditorSceneMarkDirty();
        }

        [MenuItem("NYC/3. Save Generated Meshes As Assets", priority = 3)]
        public static void SaveMeshes()
        {
            var builder = FindOrCreateBuilder();
            Transform root = builder.transform.Find("Manhattan");
            if (!root) { Debug.LogWarning("[NYC] Nothing to save: build the city first."); return; }

            Directory.CreateDirectory(Root + "/Meshes");
            int saved = 0;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf.sharedMesh) continue;
                string path = $"{Root}/Meshes/{mf.sharedMesh.name}.asset";
                if (AssetDatabase.LoadAssetAtPath<Mesh>(path) == null)
                {
                    AssetDatabase.CreateAsset(mf.sharedMesh, path);
                    saved++;
                }
            }
            AssetDatabase.SaveAssets();
            Debug.Log($"[NYC] Saved {saved} meshes to {Root}/Meshes");
        }

        [MenuItem("NYC/4. Clear Generated City", priority = 4)]
        public static void Clear()
        {
            var builder = FindOrCreateBuilder();
            builder.Reset();
            EditorSceneMarkDirty();
        }

        [MenuItem("NYC/7. Build Collision World", priority = 7)]
        public static void BuildCollision()
        {
            var builder = FindOrCreateBuilder();
            if (builder.CollisionBoxes.Count == 0)
            {
                Debug.LogWarning("[NYC] No colliders yet — run NYC/2. Build Manhattan Mesh first.");
                return;
            }

            var world = Object.FindFirstObjectByType<CollisionWorld>();
            if (!world)
            {
                var go = new GameObject("CollisionWorld");
                world = go.AddComponent<CollisionWorld>();
            }

            world.Build(builder.CollisionBoxes);

            if (CityConfig.Ready && CityConfig.Geo?.Manhattan != null)
            {
                var ring = CityConfig.Geo.Manhattan;
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                for (int i = 0; i < ring.Length; i++)
                {
                    var v = ring[i];
                    if (v.x < minX) minX = v.x;
                    if (v.x > maxX) maxX = v.x;
                    if (v.y < minZ) minZ = v.y;
                    if (v.y > maxZ) maxZ = v.y;
                }
                world.SetIslandBounds(new Vector4(minX, maxX, minZ, maxZ));
            }

            EditorUtility.SetDirty(world);
            EditorSceneMarkDirty();
            Debug.Log($"[NYC] Collision world: {world.Count:N0} boxes.");
        }

        [MenuItem("NYC/8. Build Traffic Network", priority = 8)]
        public static void BuildTraffic()
        {
            if (!CityConfig.Ready)
            {
                var geo = Nyc.Data.NycGeoData.LoadFromStreamingAssets("nyc/nyc-geodata.js");
                CityConfig.Initialize(geo);
            }

            var net = Object.FindFirstObjectByType<RoadNetwork>();
            if (!net)
            {
                var go = new GameObject("RoadNetwork");
                net = go.AddComponent<RoadNetwork>();
            }
            net.Build(CityConfig.Geo);

            var system = Object.FindFirstObjectByType<TrafficSystem>();
            if (!system)
            {
                var go = new GameObject("Traffic");
                system = go.AddComponent<TrafficSystem>();
            }

            var renderer = system.GetComponent<TrafficRenderer>();
            if (!renderer) renderer = system.gameObject.AddComponent<TrafficRenderer>();

            system.network = net;
            system.renderer = renderer;
            system.controllers = Object.FindFirstObjectByType<ThirdPersonControllerManager>();
            system.viewCamera = Camera.main;
            system.Build();

            var crowd = Object.FindFirstObjectByType<PedestrianCrowd>();
            if (!crowd)
            {
                var go = new GameObject("Pedestrians");
                crowd = go.AddComponent<PedestrianCrowd>();
            }
            crowd.controllers = system.controllers;
            crowd.viewCamera = Camera.main;
            crowd.RebuildNow();

            EditorUtility.SetDirty(net);
            EditorUtility.SetDirty(system);
            EditorSceneMarkDirty();

            Debug.Log($"[NYC] Road network: {net.laneCount:N0} lanes, {net.connectionCount:N0} connections. " +
                      $"Traffic: {system.activeCount} vehicles. Pedestrians: {crowd.walkerCount}.");
        }

        static ManhattanMeshBuilder FindOrCreateBuilder()
        {
            var found = Object.FindFirstObjectByType<ManhattanMeshBuilder>();
            if (found) return found;
            var go = new GameObject("ManhattanBuilder");
            Undo.RegisterCreatedObjectUndo(go, "Create ManhattanBuilder");
            return go.AddComponent<ManhattanMeshBuilder>();
        }

        static Material Make(string name, Color color, float smoothness, float metallic)
        {
            string path = $"{Root}/Materials/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) return existing;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) { Debug.LogError("[NYC] URP is not the active render pipeline."); return null; }

            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            m.enableInstancing = false;
            AssetDatabase.CreateAsset(m, path);
            return m;
        }

        static void EditorSceneMarkDirty()
        {
#if UNITY_EDITOR
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
                UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
#endif
        }
    }
}
