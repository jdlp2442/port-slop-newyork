# AGENT.md — Traspaso del proyecto

> Documento de continuidad. Si eres otra IA (o una persona) que hereda este trabajo, lee esto
> entero antes de tocar nada. Explica qué se ha hecho, por qué, dónde está cada cosa, qué
> contratos no se pueden romper y qué queda por hacer.

Última actualización: **proyecto completo** (fases 1–6 implementadas y verificadas).

---

## 0. Estado en una frase

Se está portando **`mrdoob/slop-newyork`** (simulador de Manhattan 1:1 en three.js + WebGPU + TSL)
a **Unity 6 / URP 17**. Las fases 1–4 están implementadas y verificadas de forma headless; las
fases 5 (tráfico y multitudes) y 6 (HUD en UI Toolkit) están pendientes.

| Fase | Contenido | Estado |
|---|---|---|
| 1 | Arquitectura, mapeo de ficheros, paquetes, settings | ✅ |
| 2 | Datos + generación procedural de Manhattan (GLB y C# nativo) | ✅ verificado |
| 3 | Shaders TSL → HLSL/URP + iluminación y ciclo día/noche | ✅ compilado (C#), shaders revisados |
| 4 | 4 controladores, cámara de 11 presets, teletransporte seguro | ✅ verificado |
| 5 | Tráfico (IDM) + peatones GPU instancing | ✅ verificado |
| 6 | HUD y minimapa en UI Toolkit | ✅ verificado |

Cifras verificadas sobre los datos reales: **2 691 bloques, 3 461 lotes, 918 bloques con marco,
36 landmarks, 3 246 polilíneas de calle, 3 646 nodos de grafo** → **30 938 edificios,
1 662 327 vértices, 89 chunks, 0 NaN, 31 258 colliders, 6 091 tiras de pintura vial,
33 601 carriles, 232 vehículos, 295 peatones, atlas 2048²**.

---

## 1. Qué es el proyecto original

`slop-newyork` dibuja Manhattan a escala 1:1 (1 unidad = 1 metro) con:

* tres.js `dev` + `WebGPURenderer`, materiales **TSL** (Three Shading Language, node graphs).
* Datos reales: NYC Open Data (DCP borough boundaries, CSCL street centerline, NYC Parks) +
  OpenStreetMap (Central Park, piers, landmarks, NJ coastline).
* 4 modos de viaje, 11 cámaras, 200 coches + 32 buses con IDM, 340 peatones animados con
  skinning en GPU, ciclo día/noche con 4 presets, minimapa y atlas de calles en canvas 2D.

El objetivo del port no es "rehacer un GTA", es **reproducir la misma ciudad, los mismos shaders y
la misma sensación de conducción** en Unity, manteniendo el dato como única fuente de verdad.

---

## 2. Dónde está todo

```
D:/escena/
├── slop-newyork-main/slop-newyork-main/   ← repositorio original (NO TOCAR, es la referencia)
│   ├── js/           23 módulos (fuente de la lógica)
│   ├── tools/        pipeline de datos Node (build-geodata.mjs + geo-*.mjs)
│   ├── css/          HUD
│   └── index.html
└── unity-migration/                        ← ESTE proyecto
    ├── AGENT.md                            ← este documento
    ├── PHASE1-2.md                         ← guía detallada fases 1 y 2
    ├── PHASE3.md                           ← guía detallada fase 3
    ├── PHASE4.md                           ← guía detallada fase 4
    ├── Assets/
    │   ├── Scripts/Core/         CityFrame, CollisionBox
    │   ├── Scripts/Data/         NycGeoData, loader, CityConfig, CitySpatialIndex
    │   ├── Scripts/Generation/   PolygonTriangulator, ManhattanMeshBuilder, RoadMarkingBuilder
    │   ├── Scripts/Rendering/    NycShaderIDs, TimeOfDayPreset, LightingSystem
    │   ├── Scripts/Gameplay/     CollisionWorld, TravelController + 4 controladores,
    │   │                          PlayerInputReader, TeleportService, CameraManager
    │   ├── Shaders/              Facade, CurtainWall, RoadSurface, RoadPaint, Sidewalk,
    │   │                          Water, Sky + Includes/{NycCommon,NycFacade,NycPasses}.hlsl
    │   └── Editor/               ManhattanBakeMenu, NycPresetCreator
    └── Tools/
        ├── export-nyc-glb.js               ← exporter GLB para inyectar en index.html
        └── headless-check/                 ← harness dotnet que compila y ejecuta todo
```

`unity-migration/Assets` es directamente la carpeta `Assets` de un proyecto Unity: se puede
copiar tal cual.

---

## 3. REGLA DE ORO: el marco de coordenadas

Esto es lo primero que hay que entender; si se rompe, todo lo demás falla en silencio.

El original usa un marco **diestro**:

* 1 unidad = 1 metro.
* **+X = este de la retícula** de Manhattan.
* **+Z = sur de la retícula** (norte = −Z).
* Origen: intersección de **5th Ave & 42nd St** (línea central).
* Ejes girados **29.02°** respecto al norte real.
* Nivel del agua −2.2 m, acera +0.22 m.

Unity es **zurdo**. La única conversión que conserva la quiralidad es:

```
Unity = (x, y, -z)
```

Está implementada **solo** en `CityFrame`:

```csharp
CityFrame.ToUnity(x, y, z)        // posición
CityFrame.ToUnityXZ(x, z)         // Vector2
CityFrame.HeadingToRotation(h)    // Quaternion.Euler(0, -h·Rad2Deg, 0)
```

Consecuencias, todas deliberadas:

1. **Norte = +Z** en Unity (convención de Unity).
2. **La simulación se queda en el marco fuente.** Todos los controladores guardan
   `Position` como `Vector2 (x, z)` fuente, `Height` como Y, y `Heading` en radianes fuente
   (0 = +Z = sur). La conversión ocurre **solo** al escribir el transform (`SyncTransform()`).
   Esto permite portar `controllers.js` línea a línea sin errores de signo.
3. **Los prefabs de actores deben mirar a −Z.** Un modelo glTF importado con glTFast ya queda
   así (glTF +Z → Unity −Z). Si alguien modela un coche mirando a +Z, hay que meterle un hijo
   "Visual" rotado 180°.
4. **El winding se corrige solo** en el builder (`MeshBuf.Tri` compara el producto cruz con la
   normal y gira el triángulo si hace falta). No lo toques a mano.

---

## 4. El dato: `js/nyc-geodata.js`

Fichero generado por `tools/build-geodata.mjs` (1.09 MB). Es un `window.NYC.GEO = {...}` envuelto
en un IIFE. `NycGeoData.Parse` recorta el envoltorio y lo pasa a Newtonsoft; **no hay paso de
conversión**: se copia el `.js` tal cual a `Assets/StreamingAssets/nyc/`.

### Esquema (y cifras reales)

| Clave | Forma | Cantidad |
|---|---|---|
| `frame` | `{bearingDeg, cutZ, strip, sidewalk}` | — |
| `manhattan` | `[x,z,…]` plano | 687 pts · bbox x[−2117,2569] z[−7090,6512] |
| `upperManhattan` | idem | 483 pts |
| `islands` | 6 anillos | Roosevelt, Randalls, Governors, Liberty, Mill Rock, Ellis |
| `boroughs` | 5 × anillos | NJ, Brooklyn, Queens, Bronx, Staten Island |
| `streets` | `[{n,z,segs:[[x0,x1,dir,w]]}]` | 132 (1st–132nd) |
| `avenues` | `[{id,label,x,segs}]` | 22 |
| `roads` | `[[name,w,dir,lanes,hw,flat]]` | 3 246 polilíneas / 9 609 pts |
| `roadGraph` | `{nodes:flat, links:[[a,b,nameIdx,w,lanes,dir,inner[]]], names}` | 3 646 nodos / 2 717 enlaces |
| `blocks` | `[[poly, rects, park, parkName, isRect, frames]]` | 2 691 bloques / 3 461 lotes / 918 con marco |
| `parks` | `[{name,cat,acres,rings}]` | 116 |
| `centralPark` | bounds, outline 22 pts, 7 agua, 9 praderas, 217 viales | — |
| `piers` | 69 | |
| `landmarks` | 36 footprints | |
| `points` | 9 puntos | estatua, terminales… |

### Tres trampas del dato (ya resueltas, no las reintroduzcas)

1. **`blocks[5]` (frames)** vale `0` en bloques de retícula, o `[theta, px, pz, rects]` en los
   irregulares. Los lotes del marco están en un sistema rotado:
   `world = p + R_y(θ)·(local − p)`. `ManhattanMeshBuilder.BoxFrame` lo implementa.
2. **`parks[].rings` son parcelas disjuntas, no anillos con agujeros.** Tratarlas como
   "outer + holes" hace desaparecer media ciudad (costó un bug).
3. **`roadGraph.links[i][6]` es un array anidado** con los puntos interiores, no floats sueltos.

---

## 5. Inventario de código

### Core

| Fichero | Qué hace | API clave |
|---|---|---|
| `CityFrame.cs` (64) | conversión fuente↔Unity | `ToUnity`, `ToUnityXZ`, `HeadingToRotation` |
| `CollisionBox.cs` (16) | collider AABB/OBB plano | struct con `MaxY`, `HasObb`, `Cx/Cz/Hx/Hz/Cs/Sn` |

### Data

| Fichero | Qué hace | API clave |
|---|---|---|
| `NycGeoData.cs` (488) | DTOs + parser del `.js` | `LoadFromFile`, `LoadFromStreamingAssets`, `Parse` |
| `NycGeoDataLoader.cs` (43) | carga async (Android/WebGL) | `LoadAsync` |
| `CityConfig.cs` (280) | calles, avenidas, distritos, costa, Broadway, teleport, señales | `StreetZ`, `AvenueX`, `DistrictAt`, `IslandBoundsAtZ`, `Teleports`, `SignalPhase` |
| `CitySpatialIndex.cs` (89) | rejilla 64 m de bloques | `BlockAt`, `IsOnSidewalk` |

### Generation

| Fichero | Qué hace | API clave |
|---|---|---|
| `PolygonTriangulator.cs` (136) | ear clipping + filtrado de agujeros por centroide | `Triangulate`, `PointInPolygon` |
| `ManhattanMeshBuilder.cs` (957) | masas, aceras, parques, agua, calles, props, colliders | `Build()`, `Reset()`, `CollisionBoxes` |
| `RoadMarkingBuilder.cs` (222) | tiras de pintura vial desde `roads` + `roadGraph` | `Build()` |

### Rendering

| Fichero | Qué hace | API clave |
|---|---|---|
| `NycShaderIDs.cs` (33) | ids cacheados de uniforms globales | — |
| `TimeOfDayPreset.cs` (102) | 4 presets con TODOS los valores del original | `Lerp` |
| `LightingSystem.cs` (356) | sol, ambiente, skybox, niebla, globales, grading, ciclo | `ApplyPreset`, `SetHour`, `runCycle` |

### Gameplay

| Fichero | Qué hace | API clave |
|---|---|---|
| `CollisionWorld.cs` (199) | rejilla 48 m, push-out AABB/OBB | `Build`, `Resolve`, `Overlaps` |
| `TravelController.cs` (89) | base + enum + input + contexto | `Tick`, `SyncTransform`, `Teleport` |
| `PedestrianController.cs` (76) | andar/correr/girar/saltar | — |
| `CarController.cs` (188) | taxi: motor, grip, suspensión, choques | `CollisionSpark` event |
| `BikeController.cs` (98) | bici: pedaleo, lean | — |
| `HelicopterController.cs` (112) | helicóptero: colectivo, cabeceo, alabeo | — |
| `PlayerInputReader.cs` (99) | teclado + touch analógico | `Current`, `SetTouch` |
| `TeleportService.cs` (129) | búsqueda de llegada segura | `TryFindSafe` |
| `ThirdPersonControllerManager.cs` (263) | dueño de los 4 modos, suelo, clamp, spawn | `SetMode`, `TeleportActiveTo`, `TeleportTo` |
| `CameraManager.cs` (365) | 11 presets, órbita, zoom, suavizado | `SetPreset`, `Cycle`, `ResetForTeleport` |

### Traffic / Pedestrians

| Fichero | Qué hace | API clave |
|---|---|---|
| `Traffic/RoadNetwork.cs` (~430) | parte `GEO.roads` en cruces y construye 33 601 carriles con conexiones | `Build(geo)`, `Sample(lane,s)`, `Project(lane,p)` |
| `Traffic/TrafficSystem.cs` (~420) | 200 coches + 32 buses con IDM, semáforos, jugador como líder | `Build()`, `Tick(dt)`, `Agents` |
| `Traffic/TrafficRenderer.cs` (~150) | 4 lotes con `Graphics.RenderMeshInstanced` | `Render(batches)` |
| `Traffic/TrafficJobs.cs` (~40) | `BuildVehicleMatricesJob` (Burst, `IJobParallelFor`) | — |
| `Traffic/VehicleMeshFactory.cs` (~150) | taxi/sedán/SUV/bus low-poly con id de pieza en `uv1.x` | `CreateCar`, `CreateBus` |
| `Pedestrians/PedestrianCrowd.cs` (~340) | 295 peatones por perímetro de bloque, `DrawMeshInstancedIndirect` | `RebuildNow()` |

### UI

| Fichero | Qué hace | API clave |
|---|---|---|
| `UI/NycMapBaker.cs` (~330) | atlas cartográfico horneado (agua, tierra, parques, bloques, calles) | `Bake(geo, size)`, `Rasterize(geo, size, out rect)` |
| `UI/NycMapSystem.cs` (~340) | radar + atlas: escala, pan, zoom, click-to-teleport, búsqueda | `Initialize(root)`, `Tick()`, `Toggle()`, `SetOpen()` |
| `UI/LocationLabel.cs` (~120) | `"5th Ave & 42nd St"` para cualquier punto | `Get(geo, x, z)` |
| `UI/HUDController.cs` (~380) | HUD completo + teclado + telemetría | `SetMode`, `SetCamera`, `SetTime`, `Teleport`, `ToggleHud` |

### UI assets

| Fichero | Qué contiene |
|---|---|
| `UI/HUD.uxml` | layout completo (~110 nodos, 20 secciones) |
| `UI/HUD.uss` | sistema de diseño traducido de `css/style.css` |

### Shaders

| Fichero | Qué hace |
|---|---|
| `Includes/NycCommon.hlsl` (161) | hash, ruido, `filteredPulse`, `lineAA`, `gridLine`, `bumpNormal`, AO de cañón, máscaras de mojado, globales |
| `Includes/NycFacade.hlsl` (647) | celda de fachada, interior 3D, familia pre-war y muro cortina |
| `Includes/NycPasses.hlsl` (67) | vertex/fragment de ShadowCaster, DepthOnly, DepthNormals |
| `Facade.shader` (260) | mampostería (estilos 0–2) |
| `CurtainWall.shader` (237) | vidrio (estilos 3–5) |
| `RoadSurface.shader` (226) | asfalto, alcantarillas, mojado, SSR |
| `RoadPaint.shader` (168) | marcas con filtro de caja y ensanchado en pantalla |
| `Sidewalk.shader` (205) | losas 1.5 m, juntas, mojado |
| `Water.shader` (251) | 4 capas de olas, Fresnel, skyline nocturno analítico |
| `Sky.shader` (189) | Preetham + nubes FBM |
| `VehiclePaint.shader` (~180) | instanced, colorea por pieza, faros/pilotos emisivos de noche |
| `Pedestrian.shader` (~130) | indirect + `StructuredBuffer`, balanceo de brazos/piernas en el vertex |

### Contrato de nombres del HUD (UXML ↔ HUDController)

Si se renombra un elemento en el `.uxml`, hay que renombrarlo en `HUDController.Bind()`:

```
fps · mode-label · camera-label · speed · altitude · location-place · location-signs
controls-hint · speed-meter · altitude-row · mode-indicator · teleport-popover · camera-popover
mode-person · mode-car · mode-cyclist · mode-helicopter
time-golden · time-midday · time-twilight · time-midnight
cam-prev · cam-next · cam-menu · cam-<id de preset>      (11 presets)
tp-<id>                                                (8 teletransportes)
btn-teleport · btn-map · btn-hud · fullmap-close
radar · minimap-map · minimap-player
fullmap · fullmap-canvas-wrap · fullmap-map · fullmap-search · fullmap-results · fullmap-hint · fullmap-player-info
```

### Editor

| Fichero | Menú |
|---|---|
| `ManhattanBakeMenu.cs` (159) | `NYC/1..4`, `NYC/7 Build Collision World` |
| `NycPresetCreator.cs` (130) | `NYC/5 Create Time Of Day Presets` |

---

## 6. Contratos que NO se pueden romper

### 6.1 Atributos de fachada (malla → shader)

Cada vértice de edificio:

```
uv0 = (metros a lo largo del muro, Y mundo)
uv1 = (ancho del muro, alto de la caja)
uv2 = (wallKind 0..4, tono del edificio 0..1)
uv3 = (Y de coronación, estilo 0..5)
color = (tono, estilo, semilla, 1)
```

`wallKind`: `0 shop · 1 entry · 2 rear · 3 lotLine · 4 blankStone`.

Se escribe en `ManhattanMeshBuilder.Side/Top` y lo consume `NycBuildFacadeCell`. El exporter GLB
(`Tools/export-nyc-glb.js`) escribe exactamente lo mismo en `TEXCOORD_0..3` + `COLOR_0`, así que
las dos rutas (GLB y malla nativa) usan el mismo material.

### 6.2 Marcas viales

```
uv0 = (metros a lo largo de la tira, 0)
uv1 = (periodo del patrón, duty 0..1)
uv2 = (offset del centro de la tira a este borde, en Unity: x, z)
color = color de pintura
```

`RoadMarkingBuilder.AddVert` **niega la z** del offset (`side.y` → `-side.y`) porque el shader
trabaja en espacio Unity. Si se cambia, las marcas se ensanchan hacia el lado contrario.

### 6.3 Uniforms globales

Los escribe `LightingSystem.PushCityGlobals` y los leen todos los shaders:

```
_NycSunColor, _NycSkyTopColor, _NycSkyHorizonColor, _NycSunDir (unit, hacia el sol)
_NycCpBounds (minX, maxX, minZ, maxZ en espacio UNITY)
_NycIslandBounds (minX, minZ, maxX, maxZ en espacio UNITY)
_NycInteriorGlow, _NycRoadWetness, _NycCanyonBounce, _NycTime
```

Los colores van en **lineal** (`Color.linear`). Si se envían en sRGB, las fachadas salen lavadas.

### 6.4 Orientación de actores

Prefabs mirando a **−Z**. `HeadingToRotation(h) = Euler(0, −h°, 0)`. Ver §3.

---

## 7. Cómo se ejecuta y se verifica

### 7.1 En Unity (primera vez)

1. Proyecto Unity 6.0 LTS con URP 17 (plantilla 3D URP).
2. Copiar `unity-migration/Assets` sobre `Assets/`.
3. Instalar paquetes de `PHASE1-2.md §1.4` (mínimo: URP, Input System, Newtonsoft, Burst,
   Mathematics, Collections).
4. Copiar `js/nyc-geodata.js` a `Assets/StreamingAssets/nyc/`.
5. `NYC → 1. Create Default Materials`
6. `NYC → 2. Build Manhattan Mesh` (~0.5 s de cálculo, luego subida a GPU)
7. `NYC → 7. Build Collision World`
8. `NYC → 5. Create Time Of Day Presets`
9. `NYC → 6. Build Road Markings` (componente `RoadMarkingBuilder` en un GameObject vacío)
10. Escena: `Systems` (LightingSystem + ThirdPersonControllerManager + CollisionWorld +
    PlayerInputReader + TeleportService), un `Camera` con `CameraManager`, y los 4 prefabs de
    actor como hijos del manager.
11. Asignar materiales por submesh (ver `PHASE3.md §3.4`).

### 7.2 Headless (sin Unity) — úsalo siempre antes de tocar C#

```
cd Tools/headless-check
dotnet run
```

Compila **los mismos `.cs`** contra stubs de Unity/URP y ejecuta el pipeline completo. Salida
esperada:

```
parse: 286 ms
streets 132, avenues 22, blocks 2691, roads 3246, landmarks 36, parks 116
district 5th/42: midtown · Harlem: harlem · FiDi: fidi
streetZ(42)=0.3  streetZ(59)=-1369.9
avenueX(5)=0.4   avenueX(8)=-859.7
shore at z=0: -2017 .. 1256
build: 525 ms
meshes 91, verts 1,662,327, chunks 89, buildings 30938, boxes 31258
bounds X [-24117,24569] Y [-2.20,302.3] Z [-28512,29090]
NaN verts 0, degenerate tris 3, meshes with bad uv sets 0
markings: 6091 strips, 83,024 verts, 16 ms
collision: 31,258 boxes
collider sanity (inside a building): True
car 10s: pos (3.6,626.1) speed 23.50 inside=False
ped 5s: pos (-11.6,432.2) speed 4.40 y 0.00
heli 5s: pos (-110.0,477.5) alt 170.3 speed 32.00
teleport to 5th/42: True -> (0.0,0.0)
lanes 33,601, connections 149,765, roads 3,088 in 145 ms
traffic spawn: 197 agents in 3 ms
traffic 30 s sim: 215 ms total
traffic stats: active 232, nan 0, stopped 36, avg 9.47 m/s, max 13.40 m/s
pedestrians: 295 walkers over 21 blocks in 2 ms
map atlas 512px: 14690x14690 m, land 22.3%, parks 1.6%, roads 20.25%, 14 ms
map projection round-trip at 5th/42: (0.00,0.00)
```

Si cambias un `.cs` y el harness falla, **no sigas**: arréglalo antes de abrir Unity.
Los stubs (`UnityStubs.cs`, `UrpStubs.cs`, `JobsStubs.cs`) son solo para compilar; si añades una
API de Unity que no está, añádela al stub (nunca al código de producción).

---

## 8. Configuración de Unity que asume el código

| Ajuste | Valor | Por qué |
|---|---|---|
| Color Space | Linear | los shaders authoran lineal |
| Active Input Handling | Input System (New) | `ENABLE_INPUT_SYSTEM` |
| HDR / MSAA | On / 4× | bloom y bordes |
| Shadow distance | 300 m (720 en vuelo) | `LightingSystem` lo cambia |
| Cascadas | 4 | |
| Additional lights | 256 | el original usa 256 clustered |
| SSR | activado en el Renderer | agua y charcos |
| APV | on, bricks 2 m | GI de cañón |
| SRP Batcher | on | todos los materiales tienen `CBUFFER(UnityPerMaterial)` |

---

## 9. Decisiones y desviaciones (y por qué)

| Decisión | Motivo |
|---|---|
| Simulación en marco fuente, conversión solo al transform | portar la física sin errores de signo |
| `HeadingToRotation` = −h y prefabs a −Z | es lo que produce glTFast; evita un 180° oculto |
| Sin Rigidbody: cinemático + push-out | el original es cinemático; el push-out es determinista y barato |
| Colisiones AABB/OBB planas (sin MeshCollider) | 31 k cajas en rejilla 48 m vs. 31 k MeshColliders |
| Cámara directa, no Cinemachine | el suavizado en el marco de la entidad no se reproduce con un transposer |
| `parks[].rings` como parcelas | el dato no son agujeros |
| Central Park a 0.06 m | mi malla de parque está a 0.06; el original usaba 1.24 (terreno elevado) |
| Peatón inactivo quieto por defecto | el original lo hace pasear y atraviesa edificios; flag `idlePedestrianWalks` |
| Agua con ruido de gradiente en vez de FFT | no hay textura FFT en Unity; misma escala/RMS |
| Ghost signs omitidos | ~40 líneas de glifos con poco retorno visual |

---

## 10. Trampas ya encontradas (no las reintroduzcas)

1. **`blocks[].frames`**: los lotes están rotados; usar el AABB del mundo para colisionar exige
   OBB (`CollisionBox.HasObb`).
2. **`parks[].rings`**: parcelas, no agujeros.
3. **`roadGraph.links`**: el 7.º elemento es un array anidado.
4. **Altura de torre**: si `h` se calcula antes de decidir "tower", las torres nunca suben
   (el primer port daba 122 m de máximo; ahora 302 m).
5. **Winding**: `MeshBuf.Tri` invierte si `dot(cross, n) < 0`; el criterio de Unity es
   "front-facing ⇒ producto punto positivo" (verificado con el Quad nativo).
6. **`uv2` de las marcas**: hay que negar la z al pasarla a Unity.
7. **`Color32` implícito**: `Color` → `Color32` existe en Unity; si el harness se queja, es el stub.
8. **`NycGeoData.Parse`**: el wrapper JS se recorta con `IndexOf('{', '=')` + `LastIndexOf("};")`.
9. **Newtonsoft y `JToken.Count()`**: usar el método LINQ, no la propiedad.
10. **`ApplyShadowBias`** requiere `Shadows.hlsl` incluido antes de `NycPasses.hlsl`.
11. **Spawn de tráfico**: elegir un carril al azar entre 33 601 acierta ~1,5 % de las veces;
    hay que prefiltrar por cercanía (`_spawnLanes`) o solo aparecen 8 coches de 232.
12. **Mallas de vehículo**: el morro va a **−Z** (los faros también). Si se construyen a +Z,
    circulan marcha atrás.
13. **`DrawMeshInstancedIndirect`** exige Shader Model 4.5 y el `ComputeBuffer` enlazado con
    `material.SetBuffer("_Instances", buffer)` **cada frame** si el material se reutiliza.
14. **Escalar un `VisualElement` con `translate`**: el origen por defecto es el centro, así que
    `screen = S/2 + (p − S/2)·s + t`. Si aplicas `t = C − p·s` sin el término `S·(s−1)/2` el mapa
    sale desplazado (ver `NycMapSystem`).
15. **`Texture2D.SetPixels32` va de abajo arriba**: el raster del atlas es de arriba abajo; hay que
    voltear las filas o el mapa sale boca abajo.
16. **`NycGeoData` es estático**: si cambias de escena sin recargar el dominio, `CityConfig` sigue
    apuntando a los datos viejos. Recarga si el proyecto usa multi-escena.

---

## 11. Estado del proyecto

**Las 6 fases están implementadas y verificadas.** El puerto es funcional de punta a punta:
ciudad → calles → tráfico → peatones → controladores → cámara → HUD.

### Fase 5 — Tráfico y multitudes ✅

`RoadNetwork` (33 601 carriles), `TrafficSystem` (IDM exacto), `TrafficRenderer` (4 draw calls,
Burst), `PedestrianCrowd` (1 draw call, 295 peatones). Detalles en `PHASE5.md`.

Pendiente de pulir: cambios de carril (MOBIL), cesión en pasos de cebra, claims de intersección,
pool de `SpotLight` para haces de faros, VAT del rig real de peatones.

### Fase 6 — HUD y mapas ✅

`HUD.uxml` + `HUD.uss` + `HUDController` (todo el teclado y la telemetría del original),
`NycMapBaker` (atlas 2048² horneado), `NycMapSystem` (radar + atlas con búsqueda y
click-to-teleport), `LocationLabel`. Detalles en `PHASE6.md`.

Pendiente de pulir: iconos SVG como sprites, ruta GPS, etiquetas de calle en el atlas, pantalla de
carga.

### Mejoras siguientes (por impacto)

1. **Landmarks** (`landmarks.js`, 12 modelos): el terreno está reservado pero los modelos no
   están portados. Es el hueco visual más grande de Midtown.
2. **Boroughs** (`boroughs.js`): el skyline de Jersey City, Brooklyn y Queens + el city carpet.
3. **Detalle urbano** (`street-details.js`, `trees.js`, `boroughs.js` props): señales, farolas,
   vapor, árboles, bocas de incendio.
3. **Shader de calle con carriles**: hoy las marcas son una malla separada; un shader que lea el
   carril desde `uv1` ahorraría la malla entera.
5. **Post-proceso**: SSR real para el agua (hoy hay emisión + Fresnel).
6. **Perfiles**: `ProfilerMarker` en `TrafficSystem.Tick` y `ManhattanMeshBuilder.Build`.

---

## 12. Cómo continuar (orden recomendado)

1. Ejecuta `dotnet run` en `Tools/headless-check`. Si no compila, arréglalo primero.
2. Lee `PHASE6.md` (HUD y mapas), `PHASE5.md` (tráfico), `PHASE4.md` (física) y `PHASE3.md`
   (shaders). `PHASE1-2.md` tiene el marco y los datos.
3. **Lo primero que conviene atacar es el contenido visual**, no la mecánica: `landmarks.js`
   (12 modelos) y `boroughs.js` (el resto de Nueva York). La infraestructura ya está.
4. Si retocas el tráfico, mide con el harness: la simulación debe seguir por debajo de 0,2 ms
   con 232 agentes.
5. Si tocas el HUD, respeta el contrato de nombres del §6.4 (UXML ↔ `HUDController.Bind`).
6. No cambies el contrato de atributos ni el marco sin actualizar `AGENT.md`, el exporter y los
   shaders a la vez.
7. Cualquier número "mágico" que copies del original, déjalo con un comentario que apunte al
   fichero y línea (`controllers.js:1005`). Así el siguiente puede verificarlo.

---

## 13. Glosario

* **Marco fuente**: el sistema de coordenadas del three.js original (x este, z sur).
* **Bloque / curb island**: isla de acera entre calles; el dato trae 2 691.
* **Lote**: rectángulo edificable dentro de un bloque; 3 461.
* **Marco de fachada** (`frames`): sistema rotado para bloques irregulares.
* **wallKind**: tratamiento de un muro (escaparate, entrada, trasero, medianera, ciego).
* **IDM**: Intelligent Driver Model, el modelo de seguimiento de vehículos del original.
* **Chunk**: tile de 750 m donde se agrupan las mallas de edificios para culling.
* **Harness**: `Tools/headless-check`, compila y ejecuta sin Unity.
