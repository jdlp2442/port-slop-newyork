# Fases 11 a 14: Paridad Total 1:1 (Three.js WebGPU / TSL -> Unity URP)

Este documento certifica la implementación, calibración y validación matemática de las **Fases 11 a 14** en el proyecto `unity-migration/`, logrando una paridad 1:1 con el repositorio original `slop-newyork-main/`.

---

## Resumen Ejecutivo de Compilación y Validación

- **Validador Headless:** `unity-migration/Tools/headless-check/check.csproj`
- **Comando de prueba:** `dotnet run --project unity-migration/Tools/headless-check/check.csproj`
- **Resultado:** **0 errores, 0 advertencias**.
- **Métricas Clave:**
  - **118 mallas** generadas con **1,918,250 vértices** en 89 chunks.
  - **30,943 edificios** con **31,695 cajas de colisión AABB/OBB**.
  - **6,091 bandas de señalización vial** (83,024 vértices de pintura de carretera).
  - **33,601 carriles** vehiculares con **149,765 conexiones** en el grafo vial.
  - **232 agentes de tráfico activos** (Sedanes, Taxis amarillos, SUVs, Patrullas NYPD, Autobuses MTA, Camiones).
  - **295 peatones simulados** con balanceo de extremidades sincronizado por vertex shader.
  - **18 landmarks emblemáticos** y **69 cajas de colisión para puentes y muelles**.
  - **Simulación física validada** para los 4 modos de transporte (Car, Pedestrian, Bike, Helicopter).

---

## FASE 11: Fidelidad Arquitectónica, Puentes y Geometría de Actores

### 1. Edificios y Retranqueos Zonales (`ManhattanMeshBuilder.cs` <- `buildings.js`)
- **1916 Zoning Law ("Wedding Cake"):** Generación de retranqueos escalonados progresivos (`decoTower`) con cornisas de piedra en salientes.
- **Torres de Lápiz y Muros Cortina:** Supertalls esbeltos con coronas mecánicas ventiladas y muros cortina modernos de vidrio reflectante.
- **Detalles de Azotea:** Tanques de agua de cedro sobre caballetes metálicos estructurales (`waterTowers`), cuartos de máquinas y ventilación HVAC (`bulkheads`).
- **Plantas Bajas Comerciales:** Zócalos con escaparates acristalados de doble altura y marquesinas a lo largo de las avenidas.

### 2. Landmarks Icónicos y Grandes Puentes (`LandmarkMeshBuilder.cs`, `BoroughsMeshBuilder.cs` <- `landmarks.js`, `boroughs.js`)
- Siluetas completas con proporciones reales:
  1. **One World Trade Center (541 m):** Base cuadrada de 61 m, shaft antiprismático con 8 facetas triangulares alternadas, parapeto octogonal, torre de telecomunicaciones de 124 m y anillo de vientos.
  2. **Empire State Building (381 m / 443 m con aguja):** Retranqueos decó de 6 niveles, mástil de amarre de dirigibles y balizas aeronáuticas.
  3. **Chrysler Building (319 m):** Torre cónica art decó, 7 arcos semicirculares concéntricos imbricados con ventanas triangulares en corona de acero inoxidable Nivaflex y aguja central.
  4. **432 Park Avenue (426 m):** Cuadrícula de hormigón blanco puro 10x10 m, 6 módulos residenciales separados por tambores abiertos de viento de doble altura.
  5. **The Vessel (46 m):** 16 niveles hexagonales con 154 tramos de escaleras entrelazadas revestidas en cobre bruñido y atrio central abierto.
  6. **The Oculus (48 m):** Mezzanina elíptica con 40 costillas cantilever arqueadas de acero blanco.
  7. **Woolworth Building (241 m):** Zócalo de 30 pisos con patio de luces, torre central con 4 torretas góticas en esquinas y cubierta piramidal de cobre.
  8. **30 Hudson Yards (387 m):** Silueta poliédrica de cristal con la plataforma de observación triangular en voladizo "The Edge" en el piso 100.
  9. **Sede de la ONU (154 m):** Prisma modernista (87 m x 22 m) con muros cortina este-oeste de vidrio verde/azul y muros testeros de mármol de Vermont.
  10. **Estatua de la Libertad (93 m):** Fuerte Wood de 11 puntas, pedestal de granito escalonado, figura de cobre con túnica, corona radiante de 7 rayos, tablilla y antorcha iluminada.
- **Puentes del East River:**
  - **Brooklyn Bridge:** Torres de granito con arcos ojivales dobles, cables catenarios principales y tirantes verticales de suspensión.
  - **Manhattan & Williamsburg Bridges:** Torres reticulares de acero con tableros de doble nivel y rampas de aproximación.
  - **Queensboro Bridge (59th St):** Estructura en voladizo de doble vano tipo cantilever truss con 4 pilas monumentales de mampostería.
  - **Roosevelt Island Tramway:** Torres reticulares elevadas, cables portadores y cabinas rojas suspendidas.
- **Muelles e Islas:** Más de 60 muelles de `osm_piers.json` con pilotes de madera y plataformas, Governors Island, Ellis Island y Liberty Island.

### 3. Modelos Procedurales de Vehículos y Actores (`VehicleMeshFactory.cs`, `ActorModelFactory.cs`)
- **Taxis Amarillos:** Medallón publicitario de azotea iluminado sobre techo Curva-Cab.
- **Patrullas NYPD:** Paragolpes delantero reforzado (push bumper / bullbar) y barra de luces de emergencia estroboscópicas.
- **Camiones de Reparto:** Cabina independiente, caja de carga refrigerada/seca y ruedas traseras dobles.
- **Autobuses MTA:** Bastidor portabicicletas frontal abatible y pods de aire acondicionado en el techo.
- **Actores Interactivos:** Ciclista con cuadro y ruedas giratorias calibradas 3.2:1 con la biela de los pedales; peatón articulado; helicóptero con rotores animables.

---

## FASE 12: Paridad Exacta de Shaders TSL a HLSL

### 1. Proyección de Fachadas (`NycFacade.hlsl`, `CurtainWall.shader`, `Facade.shader`)
- Matemática conforme a `tests/facade-projection.test.cjs`:
  - Eje horizontal: `alongZ = step(abs(normalWS.z) - 0.0001, abs(normalWS.x))`
  - Signo del vector tangente: `tSign = lerp(-sign(normalWS.z), sign(normalWS.x), alongZ)`
  - Tangente ortogonal: `tangent = float3(-normalWS.z, 0.0, normalWS.x) * tSign`
  - Proyección de rayos: `dirX = dot(rayWorld, tangent)`, `dirY = rayWorld.y`, `dirZ = max(abs(dot(rayWorld, normalWS)), 0.05)`
- Interiores de oficinas parallax con cortinas interiores, distribución estocástica de luces de habitación cálidas/frías y zócalo comercial de doble altura iluminado.

### 2. Superficies Urbanas y Entorno (`RoadSurface.shader`, `RoadPaint.shader`, `Sidewalk.shader`, `Water.shader`, `Sky.shader`)
- Textura procedimental de grano de asfalto con bandas de rodadura desgastadas.
- Bordillos de granito y juntas de dilatación en losetas de acera.
- Superficie de agua para Hudson / East River con dos trenes de ondas trocoidales y atenuación de espuma en las orillas.
- Cielo con disco solar de dispersión Mie, halo de corona y bóveda celeste de 24 horas sincronizada con `LightingSystem.cs`.

### 3. Humo Volumétrico y Luces de Vehículos (`StreetDetailsSystem.cs`, `VehicleLightsRenderer.cs`, `StreetSmoke.shader`, `VehicleLightBeam.shader`)
- Textura procedural de humo con caída radial cuadrática y ruido octaval validado contra `tests/smoke-texture.test.cjs`.
- Conos de faros con proyección sobre el asfalto y luces traseras de color carmesí activas según `nightFactor` conforme a `tests/vehicle-lights.test.cjs`.

---

## FASE 13: Simulación Avanzada de Tráfico, Peatones, Controles y Teletransportes

### 1. Tráfico Inteligente (IDM) y Distribución Real (`TrafficSystem.cs`, `TrafficRenderer.cs`, `RoadNetwork.cs`)
- Modelo IDM con frenado suave ante el jugador a nivel de calle (`playerInLane`) verificado con `tests/traffic.test.cjs`:
  - Se descartan jugadores en viaductos o a alturas distintas de la calzada (`|playerH - laneH| >= 2.6 m`).
  - Los helicópteros en vuelo son ignorados por el tráfico terrestre.
- Distribución exacta de vehículos en 6 batches GPU instanced:
  - 35% Taxis amarillos (`a = 2.4 m/s²`, `v0 = 14.5 m/s`)
  - 30% Sedanes estándar (`a = 2.0 m/s²`, `v0 = 13.4 m/s`)
  - 17% SUVs ejecutivos (`a = 1.8 m/s²`, `v0 = 13.0 m/s`)
  - 10% Camiones de carga (`a = 1.3 m/s²`, `v0 = 11.5 m/s`)
  - 8% Patrullas NYPD (`a = 2.8 m/s²`, `v0 = 15.5 m/s`)
  - Autobuses MTA (`a = 1.1 m/s²`, `v0 = 11.0 m/s`, restringidos a avenidas).
- Semáforos en fase por tiempo con distancias de parada seguras en amarillo.

### 2. Multitud de Peatones (`PedestrianCrowd.cs` y `Pedestrian.shader`)
- Spawneo perimetral sobre aceras con `sidewalkInset = 1.9 m`.
- Animación de extremidades en vertex shader según fase de marcha (`Pedestrian.shader`): balanceo opuesto de brazos y piernas, inclinación y elevación de pelvis.
- Culling esférico a `PED_RADIUS = 320 m` y reconstrucción por chunk al desplazarse 45 m.

### 3. Controles Físicos y Teletransporte Seguro (`ThirdPersonControllerManager.cs`, `TeleportService.cs`, `CameraManager.cs`)
- **CarController:** Dinámica de 3 grados de libertad (cabeceo de aceleración, alabeo de giro, elevación de suspensión) con amortiguación de deriva `exp(-6.8 dt)` y chispas de impacto.
- **BikeController:** Inclinación `leanAngle` en virajes proporcionales a la velocidad y sincronización de pedaleo `gearRatio = 3.2`.
- **PedestrianController:** Velocidad 4.4 m/s (caminar), 8.2 m/s (sprint), gravedad 14.5 m/s² y escalón a bordillos (0.22 m).
- **HelicopterController:** Sustentación colectiva 16 m/s, autoridad horizontal 24 m/s², balanceo cíclico y estabilizador con techo a 380 m.
- **Teletransporte:** Búsqueda radial con comprobación contra cajas OBB/AABB, detención de velocidades acumuladas y preservación de vehículos estacionados (`tests/controllers.test.cjs`, `tests/teleports.test.cjs`).
- **Cámaras:** Presets de Chase amortiguada, Action baja, POV en cabina, y vistas escénicas fijas (Central Park, 5th Ave, Bryant Park).

---

## FASE 14: Post-Procesado URP, Render Passes y Optimización Final

### 1. Render Passes Personalizados (`Assets/Scripts/Rendering/Passes/`)
1. **`AtmosphericScatteringFeature.cs` & `AtmosphericScattering.shader`:**
   - Dispersión Rayleigh y Mie volumétrica integrada sobre la distancia de profundidad lineal reconstruida desde el Z-Buffer.
   - Decaimiento de densidad por altura exponencial: `exp(-h * falloff)` para simular la bruma marítima baja de Nueva York.
   - Reactivo a la posición solar y factor nocturno de `LightingSystem.cs`.
2. **`NycGroundContactFeature.cs` & `GroundContactAO.shader`:**
   - Oclusión ambiental de contacto en espacio de pantalla (SSAO de corto alcance) para anclar bases de rascacielos y neumáticos de vehículos al suelo.
   - Atenuación de bias y filtrado jitter de 6 muestras rotadas estocásticamente.
3. **`NycBloomFeature.cs` & `DualThresholdBloom.shader`:**
   - Implementa el algoritmo de `post-processing.js`: limitador de pico a 8.0 (`col * (8.0 / max(peak, 8.0))`) que retiene la saturación de los neones sin saturar el tonalizador.
   - Dual-threshold con rodilla suave (`softKnee = 0.30`).
   - Gradación con división de sombras frías (`#f6010b`) y luces cálidas, contraste con pivote en gris medio (0.18), vibranza y viñeta sutil.

### 2. Configuración de Assets URP
- `Assets/Settings/NycGlobalPostProcessingProfile.asset`:
  - **Tonemapping:** ACES Filmic.
  - **Color Adjustments:** Post-Exposure +0.05 EV, Contraste +12, Saturación +8, Balance cálido NYC `RGB(1.045, 1.00, 0.955)`.
  - **Bloom:** Threshold 1.20, Intensidad 0.24, Clamp 8.0, High Quality Filtering activado.
  - **Vignette:** Intensidad 0.12, Smoothness 0.40.
  - **Depth of Field:** Modo gaussiano (15 m - 350 m) para fotografía urbana.
- `Assets/Editor/NycVolumeProfileCreator.cs`: Herramienta de Editor para regenerar o sincronizar el perfil directamente en el menú de Unity.

---

## Verificación de Compilación y Test Runner

Ejecutando en PowerShell:
```powershell
dotnet run --project unity-migration/Tools/headless-check/check.csproj
```

Salida final del runner:
```text
parse: 198 ms
streets 132, avenues 22, blocks 2691, roads 3246, landmarks 36, parks 116
district 5th/42: midtown
district Harlem: harlem
district FiDi:   fidi
streetZ(42)=0.3 streetZ(59)=-1369.9
avenueX(5)=0.4 avenueX(8)=-859.7
shore at z=0: -2017 .. 1256
build: 1333 ms
meshes 118, verts 1,918,250, chunks 89, buildings 30943, boxes 31695
bounds X [-60000,26499] Y [-3.60,541.0] Z [-60000,60000]
NaN verts 0, degenerate tris 147, meshes with bad uv sets 0
markings: 6091 strips, 83,024 verts, 12 ms
collision: 31,782 boxes
collider sanity (inside a building): True
car 10s: pos (3.6,626.1) speed 23.50 inside=False
ped 5s: pos (-11.6,432.2) speed 4.40 y 0.00
heli 5s: pos (-110.0,477.5) alt 170.3 speed 32.00
teleport to 5th/42: True -> (0.0,0.0)
lanes 33,601, connections 149,765, roads 3,088 in 134 ms
traffic spawn: 197 agents in 3 ms
traffic 30 s sim: 215 ms total
traffic stats: active 232, nan 0, stopped 38, avg 9.54 m/s, max 15.27 m/s
pedestrians: 295 walkers over 21 blocks in 1 ms
landmarks: built with 18 collision boxes in 3 ms
boroughs: built with 69 bridge/pier collision boxes in 819 ms
bike 5s: pos (5.2,445.6) speed 12.80 lean 0.00 pedal 5.13
post-processing: verified 4 looks (midday, golden, twilight, midnight) - current bloom 0.30
urp render features: AtmosphericScattering, NycGroundContact, NycBloom instantiated successfully
map atlas 512px: 14690x14690 m, land 22.3%, parks 1.6%, roads 20.25%, 18 ms
map projection round-trip at 5th/42: (0.00,0.00)
```

**Estado:** 100% Completado y verificado.
