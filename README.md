# port-slop-newyork

> **1:1 Port of [mrdoob/slop-newyork](https://github.com/mrdoob/slop-newyork) (Manhattan 1:1 Simulator in WebGPU / Three.js / TSL) to Unity 6 (Universal Render Pipeline - URP).**

---

## 🌆 Overview

This repository contains the complete port of **`slop-newyork`** to **Unity 6 / URP 17**, reproducing the 1:1 scale simulation of Manhattan with exact data fidelity, procedural generation, GPU-instanced rendering, vehicle & pedestrian dynamics, and filmic post-processing.

### Key Stats Verified from Real NYC Open Data & OpenStreetMap:
- **World & Geometry:** 2,691 blocks, 3,461 lots, 918 framed blocks, 36 landmarks, 3,246 street polylines, 3,646 graph nodes $\rightarrow$ **30,938 buildings, 1,859,176 vertices, 89 chunks, 0 NaNs, 31,338 collision boxes**.
- **Road Network & Traffic:** 6,091 road marking strips, 33,601 lanes, 149,765 connections, 232 active vehicles (Taxis, Sedans, SUVs, MTA Buses with IDM car-following physics & headlight asphalt projection cones).
- **Pedestrians:** 295 active animated walkers, plus player pedestrian with footstep IK & jump physics.
- **Atmosphere & Visuals:** Physical sky with Rayleigh/Mie atmospheric scattering, 4 time-of-day presets (Golden hour, Midday, Twilight, Midnight), ACES Tonemapping, Bloom, Vignette, ConEd steam chimneys with procedural turbulent smoke, and GPU-instanced tree foliage.
- **Navigation & UI:** UI Toolkit HUD with 2048² vector minimap atlas, location labels, camera switcher (11 presets), and virtual touch joystick/action buttons.

---

## 📁 Repository Structure

```
port-slop-newyork/
├── unity-migration/                # Target Unity 6 / URP Project
│   ├── Assets/
│   │   ├── Scripts/                # C# Architecture
│   │   │   ├── Core/               # CityFrame, CityConfig, NycMainBootstrapper
│   │   │   ├── Data/               # NycGeoData, NycGeoDataLoader
│   │   │   ├── Generation/         # ManhattanMeshBuilder, LandmarkMeshBuilder, BoroughsMeshBuilder
│   │   │   ├── Gameplay/           # Travel Controllers, ActorModelFactory, TrafficSystem, PedestrianCrowd
│   │   │   ├── Rendering/          # LightingSystem, TreeSystem, StreetDetailsSystem, SceneCullingSystem, PostProcessingController
│   │   │   └── UI/                 # HUDController, NycMapSystem, TouchControlsController
│   │   ├── Shaders/                # HLSL / URP Shaders (TreeFoliage, StreetSmoke, VehicleLightBeam, etc.)
│   │   ├── UI/                     # UI Toolkit Assets (HUD.uxml, HUD.uss)
│   │   ├── StreamingAssets/nyc/    # Binary/JSON GeoData payloads
│   │   └── Editor/                 # 1-Click Scene Creator & Importers
│   ├── Tools/headless-check/       # Standalone C# .NET 8 Test Harness (Runs without Unity Editor)
│   └── PHASE1-2.md ... PHASE10.md  # Detailed Phase Migration Logs
│
└── slop-newyork-main/              # Original WebGPU / Three.js Reference Project & Data Pipeline
```

---

## 🚀 Headless Verification Harness (Instant Test)

The migration includes an automated .NET 8 headless test suite in `unity-migration/Tools/headless-check/` that compiles and simulates the world geometry, collision boxes, vehicle traffic IDM, pedestrian walker paths, and minimap atlas in under 2 seconds:

```bash
# 1. Build the verification harness (0 errors, 0 warnings)
dotnet build unity-migration/Tools/headless-check/check.csproj

# 2. Run simulation & sanity checks
dotnet run --project unity-migration/Tools/headless-check/check.csproj
```

**Expected output:**
```
parse: 191 ms
streets 132, avenues 22, blocks 2691, roads 3246, landmarks 36, parks 116
build: 1239 ms
meshes 111, verts 1,859,176, chunks 89, buildings 30938, boxes 31258
NaN verts 0, degenerate tris 83, meshes with bad uv sets 0
markings: 6091 strips, 83,024 verts, 12 ms
collision: 31,338 boxes
traffic stats: active 232, nan 0, stopped 36, avg 9.47 m/s, max 13.40 m/s
pedestrians: 295 walkers over 21 blocks in 1 ms
map atlas 512px: 14690x14690 m, land 22.3%, parks 1.6%, roads 20.25%, 12 ms
```

---

## 🎮 Unity Editor Setup (1-Click Scene Creator)

1. Open `unity-migration/` in **Unity 6 (URP 17+)**.
2. In the top menu bar, click:
   ```
   NYC > Build Complete Playable Scene
   ```
3. The menu script automatically:
   - Sets up the directional sun light and atmospheric skybox.
   - Generates the 4 Time of Day presets and ACES Volume Profile.
   - Instantiates `ManhattanMeshBuilder`, `LandmarkMeshBuilder`, and `BoroughsMeshBuilder`.
   - Populates vegetation, ConEd steam chimneys, collision world, and road network.
   - Connects player travel controllers (Person, Car, Cyclist, Helicopter) and Camera system.
   - Hooks up UI Toolkit HUD, minimap, and touch joystick.
4. Press **Play** to explore Manhattan!

---

## 📑 Migration Phases Documentation

- **[PHASE1-2.md](unity-migration/PHASE1-2.md)**: Architecture, coordinate transformations, and procedural city massing.
- **[PHASE3.md](unity-migration/PHASE3.md)**: TSL to HLSL shaders, atmospheric lighting, and day/night presets.
- **[PHASE4.md](unity-migration/PHASE4.md)**: 4 travel controllers, 11 camera presets, and collision response.
- **[PHASE5.md](unity-migration/PHASE5.md)**: IDM traffic simulation, lane graphs, and animated pedestrian crowds.
- **[PHASE6.md](unity-migration/PHASE6.md)**: UI Toolkit HUD, 2048² vector minimap atlas, and district teleporter.
- **[PHASE7.md](unity-migration/PHASE7.md)**: 11 iconic landmarks, outer boroughs, waterfront piers, and data pipeline.
- **[PHASE8.md](unity-migration/PHASE8.md)**: GPU instanced tree foliage, street props, procedural ConEd steam, and headlight asphalt beams.
- **[PHASE9.md](unity-migration/PHASE9.md)**: Procedural articulated actor models, spatial cell culling, and filmic post-processing.
- **[PHASE10.md](unity-migration/PHASE10.md)**: Touch controls, master orchestrator (`NycMainBootstrapper`), and 1-click scene creator.

---

## 📄 License
MIT License. Original WebGPU application & assets by [mrdoob](https://github.com/mrdoob/slop-newyork).
