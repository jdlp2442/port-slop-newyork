# Fase 10: Controles Táctiles, Orquestador Principal (`main.js`) y Auto-Setup de Escena

En esta última fase se han integrado todos los sistemas procedimentales, físicos, visuales, de interfaz y de control en un único ecosistema listo para su ejecución inmediata y autónoma, portando directamente la arquitectura de inicialización de `slop-newyork-main/js/main.js` y `touch-controls.js`.

---

## 1. Controles Táctiles Virtuales (`TouchControlsController.cs`, `HUD.uxml` y `HUD.uss`)

### Porta de Controles Táctiles (`slop-newyork-main/js/touch-controls.js`)
- **Joystick Virtual Analógico (`#touch-stick`, `#touch-stick-knob`):**
  - Radio máximo de 44 px con zona muerta configurada en 0.15 para evitar microdesplazamientos no deseados.
  - Cálculo analógico continuo de intensidad y vector unitario `(dx, dy)`.
  - Desplazamiento reactivo de la perilla (`style.translate = new Translate(x, y)`) y transmisión desacoplada de los ejes (`left`, `right`, `forward`, `backward`) a `PlayerInputReader.SetTouch()`.
- **Botones de Acción Dinámicos (`#touch-actions`):**
  - **Botón Superior (`touch-btn-up`):** Se adapta según el modo de transporte activo:
    - *Modo Peatón:* "↑ Jump" (Salto vertical).
    - *Modo Helicóptero:* "↑ Ascend" (Ascenso aerodinámico).
    - *Modo Coche / Bici:* Oculto automáticamente.
  - **Botón Inferior (`touch-btn-down`):** "↓ Descend" activo exclusivamente en modo Helicóptero.
  - **Botón de Impulso (`touch-btn-boost`):** "⚡ Sprint" en peatón/ciclista y "⚡ Boost" en coche/helicóptero.
- **Integración UI Toolkit:**
  - Capa de controles añadida en `HUD.uxml` con modo `picking-mode="Position"` sobre los controles y `picking-mode="Ignore"` en la capa base.
  - Reglas de estilo en `HUD.uss` con esquinas redondeadas (`border-radius`), translucidez oscura de fondo y resaltado dorado/ámbar en boost.
  - Activación automática en dispositivos con pantalla táctil (`Input.touchSupported`) o forzado manual para depuración en el Inspector (`forceEnableOnDesktop`).

---

## 2. Orquestador Maestro (`NycMainBootstrapper.cs`)

Porta integral del ciclo de vida y función `initApp()` de `slop-newyork-main/js/main.js` en un MonoBehaviour de ejecución prioritaria (`[DefaultExecutionOrder(-100)]`):

```
                               ┌─────────────────────────────┐
                               │     1. Carga de GeoData     │
                               │  (NycGeoDataLoader.Load)    │
                               └──────────────┬──────────────┘
                                              ▼
                               ┌─────────────────────────────┐
                               │   2. Iluminación y PostFX   │
                               │ (LightingSystem + Volume)   │
                               └──────────────┬──────────────┘
                                              ▼
                               ┌─────────────────────────────┐
                               │    3. Mallas Procedurales   │
                               │(Manhattan, Landmarks, Borough)
                               └──────────────┬──────────────┘
                                              ▼
                               ┌─────────────────────────────┐
                               │  4. Follaje y Calle ConEd   │
                               │(TreeSystem + StreetDetails) │
                               └──────────────┬──────────────┘
                                              ▼
                               ┌─────────────────────────────┐
                               │ 5. Colisiones y Red Vial    │
                               │(CollisionWorld + RoadNetwork)
                               └──────────────┬──────────────┘
                                              ▼
                               ┌─────────────────────────────┐
                               │   6. Tráfico y Peatones     │
                               │(TrafficSystem + LightBeams) │
                               └──────────────┬──────────────┘
                                              ▼
                               ┌─────────────────────────────┐
                               │  7. Controladores de Actor  │
                               │ (ThirdPerson + ActorFactory)│
                               └──────────────┬──────────────┘
                                              ▼
                               ┌─────────────────────────────┐
                               │   8. Culling y HUD/Minimap  │
                               │(SceneCulling + NycMapSystem)│
                               └─────────────────────────────┘
```

- **Sincronización Per-Frame:** Conduce el avance del ciclo día/noche actualizando el volumen URP y la intensidad de los faros de tráfico sin intervención del usuario.

---

## 3. Creador de Escena en 1 Clic (`ManhattanBakeMenu.cs`)

Se ha incorporado la entrada de menú principal:
**`NYC / Build Complete Playable Scene`** (Atajo prioritario en Editor).

Al pulsarse, ejecuta de forma totalmente desatendida:
1. **Verificación de GeoData:** Confirma la presencia de los datos espaciales en `Assets/StreamingAssets/nyc/nyc-geodata.json` (importándolos desde `tools/` si faltasen).
2. **Materiales URP:** Genera los 7 materiales base (`Facade`, `Solid`, `Sidewalk`, `Road`, `Park`, `Land`, `Water`) con shaders ForwardLit.
3. **Presets de Hora del Día:** Crea los 4 activos `TimeOfDayPreset` (`golden`, `midday`, `twilight`, `midnight`).
4. **Construcción de la Jerarquía Completa de Escena (`NYC_World`):**
   - Sol direccional calibrado en color dorado y ángulo rasante con sombras suaves.
   - Perfil de Post-Procesado URP global (ACES, Bloom, Viñeta, Ajustes de Color).
   - Constructores procedurales: `ManhattanMeshBuilder`, `LandmarkMeshBuilder`, `BoroughsMeshBuilder`, `TreeSystem`, `StreetDetailsSystem`.
   - Sistema de colisiones `CollisionWorld` con límites de isla fijados.
   - Red de tráfico `RoadNetwork` con `TrafficSystem`, `TrafficRenderer` instanciado y `VehicleLightsRenderer`.
   - Simulación de peatones `PedestrianCrowd`.
   - Actor del jugador `ThirdPersonControllerManager` con sus 4 personajes/vehículos articulados creados mediante `ActorModelFactory`.
   - Sistema de cámaras `CameraManager` con 11 presets de visualización y cámara en tercera persona sincronizada.
   - Sistema de culling espacial por celdas `SceneCullingSystem`.
   - Documento UI Toolkit `UIDocument` vinculado a `HUD.uxml`, `HUDController`, `NycMapSystem` y `TouchControlsController`.
   - Componente maestro `NycMainBootstrapper` conectando las referencias de todos los subsistemas.
5. Marca la escena activa como modificada (`EditorSceneManager.MarkSceneDirty`). El desarrollador solo debe presionar **Play**.

---

## 4. Validación Final de Compilación y Ejecución

```bash
dotnet build unity-migration/Tools/headless-check/check.csproj
# Compilación correcta. 0 Advertencia(s), 0 Errores.

dotnet run --project unity-migration/Tools/headless-check/check.csproj
# parse: 191 ms
# streets 132, avenues 22, blocks 2691, roads 3246, landmarks 36, parks 116
# district 5th/42: midtown
# district Harlem: harlem
# district FiDi:   fidi
# build: 1239 ms
# meshes 111, verts 1,859,176, chunks 89, buildings 30938, boxes 31258
# bounds X [-60000,26499] Y [-3.60,541.0] Z [-60000,60000]
# NaN verts 0, degenerate tris 83, meshes with bad uv sets 0
# markings: 6091 strips, 83,024 verts, 12 ms
# collision: 31,338 boxes
# car 10s: pos (3.6,626.1) speed 23.50 inside=False
# ped 5s: pos (-11.6,432.2) speed 4.40 y 0.00
# heli 5s: pos (-110.0,477.5) alt 170.3 speed 32.00
# teleport to 5th/42: True -> (0.0,0.0)
# lanes 33,601, connections 149,765, roads 3,088 in 107 ms
# traffic spawn: 197 agents in 3 ms
# traffic stats: active 232, nan 0, stopped 36, avg 9.47 m/s, max 13.40 m/s
# pedestrians: 295 walkers over 21 blocks in 1 ms
# map atlas 512px: 14690x14690 m, land 22.3%, parks 1.6%, roads 20.25%, 12 ms
# Exit Code: 0
```
