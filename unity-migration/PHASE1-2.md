# Slop New York → Unity URP
## Technical migration guide — Phase 1 & 2

Source analysed: `mrdoob/slop-newyork` (three.js `dev` + WebGPURenderer + TSL), local copy in
`D:/escena/slop-newyork-main/slop-newyork-main`.

Everything below is derived from the actual files, not from the README. Where a number is quoted
(2 691 blocks, 3 461 lots, 36 landmarks, …) it was counted by parsing `js/nyc-geodata.js`.

---

# PHASE 1 — Project architecture and file mapping

## 1.1 The frame (read this before writing a single line of C#)

`js/city-config.js:1-11` and `tools/geo-base.mjs:22-50` define one rigid frame for the whole city:

| Property | Value |
|---|---|
| Unit | 1 unit = 1 metre |
| +X | Manhattan-grid **east** |
| +Z | Manhattan-grid **south** (north = −Z) |
| Origin | centreline intersection of **5th Ave & 42nd St** |
| Rotation | **29.02°** from true north (measured from the CSCL avenues) |
| Water level | −2.2 m |
| Sidewalk top | +0.22 m |
| Detailed island | `z > −7090` (≈129th St) down to the Battery (`z ≈ 6560`) |

three.js is right-handed, Unity is left-handed. The **only** chirality-preserving conversion is:

```
Unity = (x, y, -z)
```

This is implemented once in `Assets/Scripts/Core/CityFrame.cs` and used by every builder. Never
negate Z ad-hoc in a second place. Consequences:

* North becomes **+Z** in Unity (Unity's own convention).
* A source heading of `0` (facing +Z / south) becomes Unity yaw `180°`; `yaw = 180 − heading`.
* Rotations authored about +Y in the source become `Quaternion.Euler(0, -θ·Rad2Deg, 0)`.

## 1.2 Module map

Load order is fixed by `index.html:12-39`. The table keeps that order because later modules consume
earlier globals.

| # | Original (`js/`) | Technical responsibility | Unity equivalent (create this) |
|---|---|---|---|
| — | `index.html` | Bootstrap, import map, HUD DOM, icon sprite | `Assets/Scenes/Bootstrap.unity`, `Assets/UI/HUD.uxml` |
| 1 | `nyc-geodata.js` (1.09 MB) | Generated payload: coastlines, 132 streets, 22 avenues, 3 246 road polylines, 2 691 blocks with 3 461 lot rects, 116 parks, 36 landmark footprints, piers, bridges, Central Park features, 3 646-node road graph | **Data only**: `Assets/StreamingAssets/nyc/nyc-geodata.js` + `Scripts/Data/NycGeoData.cs` |
| 2 | `city-config.js` | Frame, street/avenue lookups, shore profile, Broadway centreline, district classifier, block spatial index, teleports, signal timing | `Scripts/Data/CityConfig.cs`, `Scripts/Data/CitySpatialIndex.cs`, `Scripts/Data/SignalTiming.cs` |
| 3 | `tsl-materials.js` (143 KB) | ~25 procedural TSL materials: 2 facade families, roads, paint, sidewalks, water (FFT), parks, trees, iron, steam, headlight beams, billboards | `Assets/Shaders/*.shader` + `*.hlsl` (Phase 3). `Facade.shader`, `CurtainWall.shader`, `RoadSurface.shader`, `Water.shader`, `Foliage.shader` |
| 4 | `trees.js` | Procedural plane / locust / elm: tapered tubes, leaf atlas cards, 3 LODs, per-species instancing | `Scripts/Generation/TreeGenerator.cs` (editor bake → prefabs), `TreeInstances.cs` |
| 5 | `actor-models.js` (83 KB) | Lofted taxi / sedan / SUV / NYPD / bus / bicycle / helicopter / pedestrian body meshes + TSL paint | `Scripts/Data/VehicleLibrary.cs` (ScriptableObject) + prefabs in `Assets/Prefabs/Vehicles` |
| 6 | `vehicle-lights.js` | Two unshadowed spot lights per vehicle, inverse-square calibrated | `Scripts/Gameplay/VehicleHeadlights.cs` |
| 7 | `pedestrians.js` (61 KB) | 15-bone skinned crowd, planted-foot IK, near/far LOD, GPU skinning from a storage buffer, per-person seed | `Scripts/Gameplay/Pedestrians/PedestrianRig.cs`, `PedestrianCrowdRenderer.cs`, `PedestrianAnimator.compute` |
| 8 | `lighting.js` | Preetham sky + clouds, PMREM IBL, 2-cascade sun, 4 time presets, fog, canyon bounce | `Scripts/Rendering/LightingSystem.cs`, `TimeOfDayPreset.cs` (SO), `Sky.shader`, URP Volume profile |
| 9 | `city-grid.js` (177 KB) | Coastline, granite bulkhead, raised sidewalks + curbs, park lakes, piers, East River bridges, Roosevelt/Governors islands | `Scripts/Generation/CityGridBuilder.cs`, `SidewalkBuilder.cs`, `PierBuilder.cs`, `BridgeBuilder.cs` |
| 10 | `boroughs.js` (163 KB) | NJ / Brooklyn / Queens / Bronx / Staten Island land, Palisades, procedural city carpet, ~100 k instanced outer-borough buildings, skyline clusters | `Scripts/Generation/BoroughBuilder.cs`, `CityCarpet.shader`, `BoroughBuildings.cs` |
| 11 | `buildings.js` (113 KB) | Lot subdivision, 20+ massing archetypes, quadtree LOD streaming, cornices, water towers, HVAC, fire escapes, awnings, signs, scaffolding, Times Square billboards | `Scripts/Generation/ManhattanMeshBuilder.cs`, `BuildingLotGenerator.cs`, `BlockDetailStreamer.cs`, `RooftopPropsBuilder.cs` |
| 12 | `landmarks.js` | 12 hand-modelled landmarks (ESB, Chrysler, GCT, One Vanderbilt, NYPL, 30 Rock, Citigroup, Seagram, Flatiron, One WTC, NYSE, Times Sq) | `Scripts/Generation/LandmarkBuilder.cs` + one prefab per landmark |
| 13 | `street-details.js` (52 KB) | 63 signalised intersections, cobra-head lamps, steam stacks + plumes, subway entrances, carts, hydrants, mailboxes, baskets, tree pits | `Scripts/Generation/StreetFurnitureBuilder.cs`, `TrafficSignalController.cs`, `SteamPool.cs` |
| 14 | `road-network.js` (52 KB) | Drivable lane graph from the *built* geometry: avenue corridors, cross-street gaps, medians, intersections, lane-to-lane turn curves | `Scripts/Gameplay/Traffic/RoadNetwork.cs`, `RoadNetworkBuilder.cs` |
| 15 | `road-paint.js` | Dashed lane lines, double yellow, stop bars, continental crosswalks, green bike lanes | `Scripts/Generation/RoadMarkingBuilder.cs` (mesh) or `RoadPaint.shader` (decal) |
| 16 | `traffic.js` (99 KB) | 200 cars + 32 buses (IDM), 340 pedestrians, signal obedience, giving way, never-enter-blocked, off-screen-only spawning, SAT player contact | `Scripts/Gameplay/Traffic/TrafficSystem.cs`, `VehicleAgent.cs`, `PedestrianCrowd.cs` |
| 17 | `controllers.js` (45 KB) | 4 travel modes: walk (foot IK), car (grip/steer/boost), bike (lean), helicopter (pitch/yaw/collective), shared collider set | `Scripts/Gameplay/Controllers/PedestrianController.cs`, `CarController.cs`, `BikeController.cs`, `HelicopterController.cs`, `ThirdPersonControllerManager.cs` |
| 18 | `touch-controls.js` | Virtual stick + hold actions, availability detection | `Scripts/Input/TouchControls.cs` (Input System + UI Toolkit) |
| 19 | `cameras.js` | 11 presets (3 follow + 7 scenic + drone), orbit/zoom, speed-reactive chase, smooth blends | `Scripts/Gameplay/Cameras/CameraManager.cs`, `CameraPreset.cs` (SO), Cinemachine 3 virtual cameras |
| 20 | `nyc-map.js` (48 KB) | Radar minimap + full-screen cartographic atlas drawn from `GEO`, search, GPS route, click-to-teleport | `Scripts/UI/MinimapRenderer.cs` (RenderTexture), `MapAtlasController.cs` |
| 21 | `scene-culling.js` | Splits island-wide InstancedMeshes into spatial chunks; position-only shadow proxies on a shadow layer | `Scripts/Rendering/SceneCuller.cs`, `ShadowProxyRenderer.cs` (or URP GPU Resident Drawer + LOD Groups) |
| 22 | `post-processing.js` | Filmic toning, white balance, selective bloom | URP Volume: Tonemapping (ACES), Color Adjustments, Bloom, Vignette |
| 23 | `main.js` | Boot order, render pipeline, UI wiring, 11 keyboard shortcuts, FPS/telemetry loop | `Scripts/Core/Bootstrap.cs`, `HUDController.cs` |
| — | `css/style.css` (34 KB) | HUD layout, MTA bullets, radar card, mode switch, popovers, responsive breakpoints | `Assets/UI/HUD.uss` (Phase 6) |
| — | `tools/build-geodata.mjs` + `geo-*.mjs` | Offline data build (projection, rasterisation, block/lot extraction, RDP) | Keep as-is (Node) or port to `Editor/NycGeoPipeline.cs`; **not needed at runtime** |

### What actually has to be re-implemented

Only three things in the repo are *irreplaceable*: the geodata, the lot/massing generator
(`buildings.js`), and the facade shader. Everything else maps onto a Unity subsystem:

| Repo mechanism | Unity subsystem |
|---|---|
| `InstancedMesh` + quadtree streaming | `Graphics.RenderMeshInstanced` / BatchRendererGroup |
| TSL node graphs | HLSL + Shader Graph |
| `ClusteredLighting(256,32,28,48)` | URP Forward+ (clustered) |
| `SunLight` 2-cascade CSM | URP 4-cascade shadows |
| `SSRNode` + `bloom` | URP Screen Space Reflections + Bloom (or HDRP-style custom pass) |
| `PMREMGenerator` | Reflection Probe / APV + `Unity_GlossyEnvironment` |
| `mergeGeometries` | `Mesh.CombineMeshes` / custom vertex buffers |
| `CanvasTexture` billboards | `RenderTexture` + `TextMeshPro` world-space |
| Native `[popover]` | UI Toolkit `VisualElement` + `Popover`-like panel |
| `<canvas>` 2D minimap | `RenderTexture` drawn with `GL`/`CommandBuffer`, or UI Toolkit `Painter2D` |

## 1.3 `Assets/` layout

```
Assets/
├── Scenes/
│   ├── Bootstrap.unity              # persistent systems, loads Manhattan additively
│   └── Manhattan.unity              # generated city + gameplay
├── Scripts/
│   ├── Core/
│   │   ├── CityFrame.cs             # ✔ written
│   │   ├── Bootstrap.cs
│   │   └── ServiceLocator.cs
│   ├── Data/
│   │   ├── NycGeoData.cs            # ✔ written
│   │   ├── NycGeoDataLoader.cs      # ✔ written
│   │   ├── CityConfig.cs            # ✔ written
│   │   ├── CitySpatialIndex.cs      # 64 m uniform grid + block quadtree
│   │   └── ScriptableObjects/
│   │       ├── TimeOfDayPreset.cs
│   │       ├── CameraPreset.cs
│   │       ├── VehicleDefinition.cs
│   │       └── DistrictProfile.cs
│   ├── Generation/
│   │   ├── PolygonTriangulator.cs   # ✔ written
│   │   ├── ManhattanMeshBuilder.cs  # ✔ written
│   │   ├── CityGridBuilder.cs
│   │   ├── BoroughBuilder.cs
│   │   ├── LandmarkBuilder.cs
│   │   ├── StreetFurnitureBuilder.cs
│   │   ├── RoadMarkingBuilder.cs
│   │   └── TreeGenerator.cs
│   ├── Rendering/
│   │   ├── LightingSystem.cs
│   │   ├── SceneCuller.cs
│   │   └── ShadowProxyRenderer.cs
│   ├── Gameplay/
│   │   ├── Controllers/  (Pedestrian, Car, Bike, Helicopter, Manager)
│   │   ├── Cameras/      (CameraManager, CameraRig)
│   │   ├── Traffic/      (RoadNetwork, TrafficSystem, VehicleAgent, PedestrianCrowd)
│   │   └── Player/       (TeleportService, LocationService)
│   ├── Input/
│   │   ├── NycInputActions.inputactions
│   │   └── TouchControls.cs
│   └── UI/
│       ├── HUDController.cs
│       ├── MinimapRenderer.cs
│       └── MapAtlasController.cs
├── Editor/
│   ├── ManhattanBakeMenu.cs         # ✔ written
│   ├── NycGeoPipeline.cs
│   └── MeshAssetPostprocessor.cs
├── Shaders/
│   ├── Facade.shader  + Facade.hlsl
│   ├── CurtainWall.shader
│   ├── RoadSurface.shader + RoadPaint.hlsl
│   ├── Water.shader
│   ├── Sky.shader
│   ├── Foliage.shader
│   └── Includes/NycNoise.hlsl
├── Materials/
│   ├── Facades/  (6 variants)
│   ├── Ground/   (road, sidewalk, park, land, water)
│   └── Props/
├── Prefabs/
│   ├── Vehicles/ (taxi, sedan, suv, nypd, bus, bike, helicopter)
│   ├── Pedestrians/
│   ├── Landmarks/
│   └── StreetFurniture/
├── ScriptableObjects/
│   ├── City/     (4 time presets, 11 camera presets, 8 teleports)
│   └── Traffic/  (vehicle definitions)
├── StreamingAssets/
│   └── nyc/nyc-geodata.js           # copied verbatim from the repo
├── UI/
│   ├── HUD.uxml
│   ├── HUD.uss
│   └── Icons/ (the 20 SVG symbols from index.html, exported as PNG/SDF)
└── Settings/
    ├── URP-NYC.asset
    ├── URP-NYC-Renderer.asset
    └── Volumes/ (Golden, Midday, Twilight, Midnight)
```

## 1.4 Packages (`Packages/manifest.json`)

Target: **Unity 6.0 LTS (6000.0.x)**. Versions are the verified 6000.0 line.

```json
{
  "dependencies": {
    "com.unity.render-pipelines.universal": "17.0.3",
    "com.unity.inputsystem": "1.11.2",
    "com.unity.cinemachine": "3.1.3",
    "com.unity.burst": "1.8.18",
    "com.unity.mathematics": "1.3.2",
    "com.unity.collections": "2.5.1",
    "com.unity.nuget.newtonsoft-json": "3.2.1",
    "com.unity.cloud.gltfast": "6.10.1",
    "com.unity.ugui": "2.0.0",
    "com.unity.addressables": "2.2.2",
    "com.unity.ide.rider": "3.0.31",
    "com.unity.test-framework": "1.4.5",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.animation": "1.0.0",
    "com.unity.modules.audio": "1.0.0",
    "com.unity.modules.jsonserialize": "1.0.0"
  }
}
```

Why each one:

* **URP 17** — Forward+ gives the clustered-light path the repo gets from `ClusteredLighting`.
* **Input System 1.11** — the repo reads raw `keydown`/pointer events; Unity needs an action map
  for the 4 modes + 11 camera shortcuts + touch stick.
* **Cinemachine 3.1** — the 11 presets are blends between virtual cameras; `CinemachineBrain`
  replaces the hand-written `lerp` in `cameras.js:200-320`.
* **Burst + Mathematics + Collections** — Phase 5 (`IJobParallelForTransform`, IDM traffic).
* **Newtonsoft Json** — `NycGeoData` parses the 1.09 MB payload with `JObject`; `JsonUtility`
  cannot express `[[x0,x1,z0,z1],…]` or dictionaries.
* **glTFast** — imports the `.glb` produced by path A (Phase 2.a) with TEXCOORD_0..3 + COLOR_0.
* **Addressables** — the city is ~1.5 M verts; load Manhattan as an addressable scene so the
  bootstrap scene stays small.

## 1.5 Project settings

`Project Settings → Player`:

| Setting | Value | Reason |
|---|---|---|
| Color Space | **Linear** | the TSL shaders author linear colours |
| API Compatibility | .NET Standard 2.1 | Newtonsoft + `System.Text` |
| Scripting Backend | IL2CPP | Burst/job performance |
| Incremental GC | **On** | 340 pedestrians + 232 vehicles allocate per frame in JS |
| Active Input Handling | **Input System Package (New)** | avoids double events |

`Project Settings → Graphics / Quality`:

| Setting | Value |
|---|---|
| Render Pipeline | `URP-NYC` |
| HDR | On |
| MSAA | 4× |
| Shadow distance | **300 m** (matches the repo's cascade coverage in `scene-culling.js`) |
| Cascades | 4 |
| Soft shadows | On |
| Additional lights | Per Pixel, **256** (repo: 256 clustered lights) |
| Main light shadows | 4096 |
| SRP Batcher | On |
| GPU Resident Drawer | On (Unity 6) |
| Dynamic Batching | Off |
| Adaptive Probe Volumes | On, 2 m brick size for the street canyon |

`Project Settings → Physics`:

* Gravity `-9.81`, but the controllers do **not** use `Rigidbody` (the source is kinematic with
  custom collision); use `CharacterController` for the pedestrian and raycast-based capsules for
  vehicles.
* Layer matrix: `Building`, `Road`, `Sidewalk`, `Player`, `Traffic`, `Prop`.

## 1.6 Bootstrap order (mirrors `main.js:36-135`)

```
Bootstrap.Awake
  ├─ load StreamingAssets/nyc/nyc-geodata.js      (NycGeoDataLoader)
  ├─ CityConfig.Initialize(geo)                   (streets, districts, shore, teleports)
  ├─ LightingSystem.ApplyPreset("golden")
  ├─ SceneManager.LoadSceneAsync("Manhattan", Additive)
  └─ HUDController.Bind(...)

Manhattan.Awake
  ├─ ManhattanMeshBuilder.Build()                 (Phase 2.b)  or  GLB prefab instantiate (2.a)
  ├─ CityGridBuilder / BoroughBuilder / LandmarkBuilder
  ├─ RoadNetworkBuilder.Build()
  ├─ TrafficSystem.Spawn()
  ├─ ThirdPersonControllerManager.Spawn("car")
  └─ CameraManager.SetPreset("chase", snap: true)
```

---

# PHASE 2 — Data extraction and procedural Manhattan

## 2.1 What the repository actually stores

`tools/build-geodata.mjs` writes one JSON object into `js/nyc-geodata.js`. Parsed schema and
counts (verified):

| Key | Type | Size / count | Used for |
|---|---|---|---|
| `frame` | `{bearingDeg, cutZ, strip, sidewalk}` | — | world frame |
| `manhattan` | flat `[x,z,…]` | **687 pts** | land polygon, shore profile |
| `upperManhattan` | flat | 483 pts | low-detail north |
| `innerShore` | flat | 738 pts | waterfront strip |
| `islands` | 6 rings | Roosevelt, Randalls, Governors, Liberty, Mill Rock, Ellis | harbour |
| `boroughs` | 5 × ring[] | 31 rings total | NJ, Brooklyn, Queens, Bronx, SI |
| `streets` | `[{n,z,segs:[[x0,x1,dir,w]]}]` | **132** streets, 1st–132nd | grid |
| `avenues` | `[{id,label,x,segs:[{z0,z1,dir,w,lanes,names}]}]` | **22** | grid |
| `broadway` | flat | 110 pts | diagonal |
| `roads` | `[[name,w,dir,lanes,hw,flat]]` | **3 246** polylines, 9 609 pts | road ribbons, maps |
| `roadGraph` | `{nodes:flat, links:[[a,b,nameIdx,w,lanes,dir,inner]], names}` | **3 646 nodes / 2 717 links** | traffic (Phase 5) |
| `blocks` | `[[poly, rects, park, parkName, isRect, frames]]` | **2 691 blocks, 3 461 lot rects, 918 framed** | buildings |
| `parks` | `[{name,cat,acres,rings}]` | **116**, 17 multi-parcel | park ground |
| `centralPark` | bounds, outline (22 pts), 7 water, 9 lawns, 217 drives, 15 sites | — | Central Park |
| `piers` | `[{name,cx,cz,len,wid,ang,ring}]` | **69** | waterfront |
| `bridges` | 4 East River + 2 outer | — | bridges |
| `landmarks` | `{name:{x,z,w,d,h,area,ring}}` | **36** | landmark lots |
| `points` | 9 named points | — | statue, terminals |

Key insight: **the block lot rectangles are already the hard part**. `geo-blocks.mjs` rasterises
every at-grade roadway at its real curb-to-curb width over the land, labels the leftover curb
islands, erodes by the 4.6 m sidewalk, and greedily packs axis-aligned lot rectangles. That is why
`buildings.js` can spend its time on massing instead of geometry. Both migration paths below reuse
this output; neither needs to re-run the rasterisation.

Two data quirks that will bite you:

1. `blocks[5]` (`frames`) is `0` for grid blocks and an array for irregular ones. A frame is
   `[theta, px, pz, rects]`, and its lots are in a rotated local frame:
   `world = p + R_y(θ)·(local − p)`.
2. `parks[].rings` are **disjoint parcels**, not outer + holes. Do not treat `rings[1..]` as holes.

## 2.2 Path A — export the built scene as `.glb`

### Why bake instead of `exporter.parse(scene)`

The scene is made of `InstancedMesh` with custom per-instance attributes (`box`, `faces`) and TSL
`NodeMaterial`s. `GLTFExporter` cannot serialise either:

* it ignores `InstancedMesh` instance buffers (you get one box at the origin),
* it has no mapping for `NodeMaterial` and will throw on `colorNode`.

So the exporter below **bakes each instance into plain vertices** and writes the facade parameters
into standard glTF attributes that Unity can import:

| glTF attribute | Contents | Consumed by |
|---|---|---|
| `TEXCOORD_0` | `(metres along the wall, world Y)` | `Facade.shader` window grid |
| `TEXCOORD_1` | `(wall width, box height)` | bay count |
| `TEXCOORD_2` | `(wall kind 0..4, building tone)` | storefront / rear / lot-line |
| `TEXCOORD_3` | `(box top Y, facade style 0..5)` | parapet + material variant |
| `COLOR_0` | `(tone, tone, tone, 1)` | fallback tint |

### Injection (append at the end of `index.html`, after the module script)

```html
<script type="module" src="./tools/export-nyc-glb.js"></script>
```

The full script is `Tools/export-nyc-glb.js`. Core of the bake (the part that matters):

```js
const FACE_TO_WALL = [1, 0, -1, -1, 3, 2];   // BoxGeometry +X,-X,+Y,-Y,+Z,-Z -> faces (w,e,n,s)

for (let inst = 0; inst < mesh.count; inst++) {
  mesh.getMatrixAt(inst, matrix);
  normalMatrix.getNormalMatrix(matrix);

  const w = boxAttr.getX(inst), h = boxAttr.getY(inst), d = boxAttr.getZ(inst);
  const tone = clamp((fract(boxAttr.getW(inst)) - 0.1) / 0.8, 0, 1);
  const topY = matrix.elements[13] + h * 0.5;

  for (let v = 0; v < 24; v++) {
    p.fromBufferAttribute(posAttr, v).applyMatrix4(matrix);
    n.fromBufferAttribute(nrmAttr, v).applyMatrix3(normalMatrix).normalize();

    const face = Math.floor(v / 4);
    const wallIdx = FACE_TO_WALL[face];
    const kind = wallIdx >= 0 ? faceAttr.getComponent(inst, wallIdx) : 4;
    const u = (face === 0 || face === 1) ? (posAttr.getZ(v) + 0.5) * d
                                        : (posAttr.getX(v) + 0.5) * w;

    uv0.push(u, p.y);
    uv1.push(wallIdx >= 0 ? (face < 2 ? d : w) : w, h);
    uv2.push(kind, tone);
    uv3.push(topY, styleId);
  }
}
```

### Running it

1. Serve the repo (`npx serve .`), open the page, wait for the loading overlay to disappear.
2. A `GLB EXPORT` panel appears above the minimap: `core | north | south | all | GEO json`.
3. `core` (Midtown + Downtown, z −1600…3200) is the recommended first export: ~10–14 k buildings,
   ~40 MB. `all` is ~250 MB — export per region and place them as additive scenes.
4. Console: `window.NYCExport.exportRegion("core")`.

### Unity import (glTFast)

1. `Assets/StreamingAssets` is not needed for this path — drop the `.glb` anywhere, e.g.
   `Assets/Art/Exported/nyc_core.glb`.
2. Select it → Inspector:
   * **Import Animation**: off
   * **Import Cameras / Lights**: off
   * **Mesh Compression**: off (we need the exact UV sets)
   * **Generate Colliders**: off (colliders come from Phase 4's box colliders)
3. glTFast maps `TEXCOORD_0..3` → `uv`, `uv1`, `uv2`, `uv3` and `COLOR_0` → vertex colour.
   Verify on one mesh: `Mesh.uv2` and `Mesh.uv3` must be non-empty.
4. Create a material with `Facade.shader` and assign it to submesh 0. The material reads `uv1..3`
   directly, so no texture setup is needed.
5. The GLB is **static**. Anything dynamic (traffic, pedestrians, controllers) is Phase 4/5.

### Limits of path A

* No quadtree streaming: the whole region is one draw per material. Split by region to keep
  frustum culling useful.
* No rooftop props unless `buildProps` in the JS scene included them (they are separate
  InstancedMeshes and are baked too, but into the same material).
* The facade look is only as good as the Unity shader in Phase 3 — the GLB carries parameters,
  not the TSL graph.

## 2.3 Path B — native generation in C#

Four files, all compile-ready in this repo:

| File | Role |
|---|---|
| `Assets/Scripts/Core/CityFrame.cs` | frame conversion |
| `Assets/Scripts/Data/NycGeoData.cs` | typed parse of `nyc-geodata.js` (JS wrapper stripped in-place) |
| `Assets/Scripts/Data/CityConfig.cs` | street/avenue lookup, district classifier, shore profile, teleports, signals |
| `Assets/Scripts/Generation/PolygonTriangulator.cs` | ear clipping + hole filtering |
| `Assets/Scripts/Generation/ManhattanMeshBuilder.cs` | the builder |
| `Assets/Editor/ManhattanBakeMenu.cs` | `NYC/…` menu |

### Step 1 — drop the data in

```
Assets/StreamingAssets/nyc/nyc-geodata.js      ← copy verbatim from the repo
```

No conversion step. `NycGeoData.Parse` finds `window.NYC.GEO = `, extracts the balanced payload up
to the last `};`, and feeds it to Newtonsoft. (A `.json` file also works: the loader detects a
leading `{`.)

### Step 2 — run the bake

`NYC → 1. Create Default Materials` then `NYC → 2. Build Manhattan Mesh`.

The builder writes:

```
Manhattan/
├── Ground              (4 submeshes: land / sidewalk / park / water)
├── Roads               (1 submesh)
├── Blocks_-3_10 …      (2 submeshes: facades / props)   one per 750 m tile
```

### Step 3 — the algorithm (what the C# actually does)

```
for each block (2 691):
    skip if park > 0.6 or median
    district = CityConfig.DistrictAt(centre)        // 25-way classifier, ported verbatim
    seed     = hash(minX, minZ)                     // reproducible per block
    for each frame (or identity):
        for each lot rect (3 461 total):
            skip if it overlaps a landmark reserve (18 of the 36 footprints are over 40 m and reserved)
            street[4] = which lot edges touch the block boundary
            split the long axis into 9–17 m slices   // Manhattan lot rhythm
            for each slice:
                floors  = district profile range     // e.g. Midtown 8–22, Harlem 4–9
                h       = floors × storey            // 3.4–4.1 m
                tower?  = chance(district) && min(w,d) > 16 m && floors > 8
                emit podium (full lot) + shaft (inset 1.6–3.4 m) or a single box
                emit cornice (low-rise only, 34 %)
                emit bulkhead (55 %) + water tower (38 %, capped at 3 000)
```

District profiles are the compressed version of `buildings.js:1596-1803` — same ranges, same
tower bias, same facade family per neighbourhood.

### Step 4 — the facade attribute contract

Per box, 5 quads (4 walls + roof; the bottom is never visible) = 20 vertices, 30 indices.
Every wall vertex carries:

```csharp
uv0 = (metres along the wall, absolute Y)
uv1 = (wall width, box height)
uv2 = (wallKind, tone)
uv3 = (box top Y, style)
color = (tone, tone, tone, 1)
```

`wallKind` matches `WALL_KINDS` in `tsl-materials.js:359`:

| value | meaning | shader behaviour |
|---|---|---|
| 0 | shop | storefront glazing on floor 0, awnings/signs allowed |
| 1 | entry | residential front, windows to the ground, one door bay |
| 2 | rear | plain brick, windows on every floor |
| 3 | lot line | windowless party wall |
| 4 | blank stone | windowless, stone colour |

The `style` index selects the Phase 3 palette:

| index | family | source options in `buildings.js:313-380` |
|---|---|---|
| 0 | pre-war limestone / buff brick | `stoneColor 0xd8cdb8`, `floorHeight 3.7` |
| 1 | red / terracotta brick | `0xa45a45`, `3.5` |
| 2 | brownstone / dark masonry | `0x765443`, `3.4` |
| 3 | blue-teal curtain wall | `glassTint 0x1a3a52`, `3.9` |
| 4 | bronze / charcoal glass | `0x242220`, `3.8` |
| 5 | silver / ice-blue supertall | `0x264664`, `4.1` |

### Step 5 — draw calls and budget

Measured by running the shipped builder headlessly on the real payload
(`Tools/headless-check`, .NET 8, single thread):

| Item | Count |
|---|---|
| Buildings (lots built) | **30 938** |
| Facade boxes (podiums + tower shafts) | **31 258** |
| Facade vertices (20/box) | ~625 000 |
| Total vertices (facades + props + ground + roads) | **1 662 327** |
| Chunks (750 m) | **89** |
| Tallest generated mass | **302 m** |
| NaN vertices / degenerate triangles / meshes with bad UV sets | **0 / 3 / 0** |
| Bake time (headless, no GPU upload) | **~0.53 s** |

Draw calls: 2 per *visible* chunk (facade + props) + 1 ground + 1 roads. At street level 8–25
chunks are in frustum; from the Empire State observatory ~90 (≈180 calls), still well inside a
desktop budget.

Tuning knobs on the component:

* `buildProps = false` → massing only, ~1.0 M verts.
* `chunkSize = 500` → better culling, ~2× the draw calls.
* `towerChanceScale = 0` → flat pre-war skyline (useful for profiling).
* `maxWaterTowers = 0` → drop ~300 k verts.

### Step 6 — validation

Run the builder, then check:

1. `Manhattan/Ground` bounds ≈ `x ∈ [−2117, 2569]`, `z ∈ [−7090, 6512]` (source) →
   Unity `z ∈ [−6512, 7090]`.
2. The Empire State lot is empty in `Blocks_*` — its footprint is reserved for Phase 3.
3. 5th Ave at `x ≈ 0` is a 30 m gap between two sidewalk slabs, running the full island.
4. `Mesh.uv2` on a facade chunk contains values 0–4 in `.x` and 0–1 in `.y`.
5. No chunk exceeds 4 M indices (`IndexFormat.UInt32` is set automatically above 65 535 verts).

### Step 7 — headless verification (already run)

`Tools/headless-check/` is a .NET 8 console project that compiles **the exact same `.cs` files**
against minimal Unity API stubs and runs the builder on the real payload. It is a CI guard, not a
Unity dependency — the stubs live only in that folder.

```
cd Tools/headless-check
dotnet run
```

Output on the repository data:

```
parse: 286 ms
streets 132, avenues 22, blocks 2691, roads 3246, landmarks 36, parks 116
district 5th/42: midtown
district Harlem: harlem
district FiDi:   fidi
streetZ(42)=0.3 streetZ(59)=-1369.9
avenueX(5)=0.4 avenueX(8)=-859.7
shore at z=0: -2017 .. 1256
build: 525 ms
meshes 91, verts 1,662,327, chunks 89, buildings 30938, boxes 31258
bounds X [-24117,24569] Y [-2.20,302.3] Z [-28512,29090]
NaN verts 0, degenerate tris 3, meshes with bad uv sets 0
```

(`X/Z` bounds include the 44 km water plane; the land itself is
`X [−2117, 2569]`, `Z [−6512, 7090]` in Unity space.)

The three degenerate triangles come from rooftop props on lots whose inset collapsed; they are
harmless and can be culled by skipping `EmitSolidBox` when `Width < 0.1f`.

---

## Status

* **Phase 1** — complete (mapping, layout, packages, settings, frame).
* **Phase 2** — complete: path A (GLB bake + Unity import) and path B (native C# builder) both
  implemented against the real data.
* Phase 3 (TSL → HLSL/Shader Graph), Phase 4 (controllers), Phase 5 (GPU crowds), Phase 6
  (UI Toolkit HUD) follow after confirmation.
