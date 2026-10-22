# Fase 9: Modelos Procedurales de Actores, Culling Espacial y Post-Procesado URP

En esta fase se han implementado e integrado los tres sistemas fundamentales de simulación de personajes/vehículos, optimización geométrica y pipeline de renderizado cinematográfico correspondientes a `slop-newyork-main/js/actor-models.js`, `scene-culling.js` y `post-processing.js`.

---

## 1. Modelos Procedurales de Actores y Rigs (`ActorModelFactory.cs`)

Porta completa y autónoma de la geometría 3D y cinemática de `actor-models.js` y `pedestrians.js`. Los 4 modos de transporte cuentan con sus jerarquías visuales generadas proceduralmente en código y animadores en tiempo de ejecución:

### Peatón Articulado (`PedestrianVisual` & `PedestrianController`)
- **Jerarquía:** Raíz móvil, torso con respiración y oscilación vertical, cabeza con cuello, brazos izquierdo y derecho (antebrazo y mano), piernas izquierda y derecha (muslo y espinilla).
- **Animación Cinemática:**
  - Ciclo de marcha y carrera con zancadas dinámicas proporcionales a `Speed` (oscilación sinusoidal hasta 38° en piernas y 35° en brazos en contrafase).
  - Flexión de rodillas y balanceo del torso en el eje Y (`Mathf.Abs(Mathf.Sin(phase * 2)) * 0.04m`).
  - Detección de salto / caída (`!IsGrounded`): transición instantánea a pose de suspensión aérea con brazos elevados y piernas retrasadas.

### Ciclista Urbano (`BikeVisual` & `BikeController`)
- **Bicicleta CitiBike:**
  - Cuadro de diamante tubular (tubo del sillín, tubo diagonal, tubo superior, vainas y tirantes).
  - Bielas y pedales giratorios sincronizados con la cadencia de pedaleo `PedalPhase`.
  - Ruedas delantera y trasera con llanta y neumático rodando continuamente en función de `WheelSpin`.
  - Horquilla delantera orientable y manillar.
- **Ciclista Sentado:** Torso inclinado a 18° sobre el tubo superior, brazos apoyados en manillar y casco aerodinámico.
- **Inclinación en Curva (Lean):** Inclinación lateral coordinada del conjunto bicicleta + ciclista (`LeanAngle` hasta 0.34 rad / 19.5°).

### Automóvil Deportivo / Taxi de NYC (`CarVisual` & `CarController`)
- **Chasis y Carrocería:** Silueta lofted aerodinámica dividida en capó (-Z), habitáculo acristalado, maletero (+Z), retrovisores y cartel luminoso superior de taxi NYC.
- **Dinámica de Suspensión de 3 Grados de Libertad:**
  - Cabeceo longitudinal (`ChassisPitch`): hundimiento del morro en frenada brusca y elevación en aceleración fuerte.
  - Balanceo transversal (`ChassisRoll`): inclinación centrífuga en giros rápidos.
  - Amortiguación vertical (`HeaveY`): respuesta física a irregularidades del asfalto.
- **Ruedas Independientes:** Dirección delantera activa con `SteerAngle` (hasta 0.52 rad) y rotación angular de los 4 neumáticos con `WheelSpin`.

### Helicóptero de Visitas Turísticas (`HelicopterVisual` & `HelicopterController`)
- **Fuselaje Aerodinámico:** Cabina redondeada con gran cúpula transparente de visión panorámica, patines de aterrizaje metálicos con montantes reforzados, viga de cola extendida y estabilizador vertical.
- **Rotores Animados a Alta Velocidad:**
  - Rotor principal superior de 4 palas con perfil aerodinámico girando a 1440°/s en el eje Y.
  - Rotor de cola antipar girando a 2880°/s en el eje X.
- **Actitud de Vuelo:** Inclinación del fuselaje en cabeceo (`PitchAngle`), alabeo (`RollAngle`) y oscilación sinusoidal natural en sustentación (`Bob`).

---

## 2. Partición por Celdas y Culling Espacial (`SceneCullingSystem.cs`)

Porta fiel de `slop-newyork-main/js/scene-culling.js`:
- **Partición Espacial en Rejilla:** Rejilla 2D en plano XZ dividida en celdas de tamaño configurable (`cellSize = 400m`, equivalente a `PROXY_CELL = 500m`).
- **Agrupación Jerárquica de Instancias:** Asignación automática de bloques de edificios, puentes, detalles de calle y elementos de vegetación a su celda espacial (`CullingChunk`).
- **Doble Culling de Rendimiento:**
  - **Distance Culling:** Cálculo de distancia euclidiana al centro de la cámara; los objetos pequeños de calle se ocultan a 350 m y los bloques lejanos a más de 2200 m.
  - **Frustum Culling:** Test de intersección de planos de frustum (`GeometryUtility.TestPlanesAABB`) contra el AABB de 400x600x400 m de cada celda.
  - Activación/desactivación dinámica de renderizadores para liberar tiempo de CPU y llamadas de dibujo (draw calls) en URP.

---

## 3. Post-Procesado URP de Grado Fílmico (`PostProcessingController.cs`)

Porta fiel de `slop-newyork-main/js/post-processing.js`:
- **Tonemapping ACES:** Mapeo tonal cinematográfico ACES Filmic Tone Mapping configurado globalmente en el perfil de volumen.
- **Cuatro Looks de Iluminación (`LOOKS`):**
  - **Midday (12:00h):** Exposure 1.00, Contrast 1.10, Saturation 1.06, Color Balance `(1.015, 1.00, 0.985)`, Bloom 0.18, Threshold 1.35.
  - **Golden Hour (17:30h):** Exposure 1.04, Contrast 1.12, Saturation 1.08, Color Balance `(1.045, 1.00, 0.955)`, Bloom 0.24, Threshold 1.20.
  - **Twilight (19:30h):** Exposure 1.06, Contrast 1.10, Saturation 1.08, Color Balance `(1.025, 0.995, 1.015)`, Bloom 0.28, Threshold 1.05.
  - **Midnight (00:00h):** Exposure 1.04, Contrast 1.08, Saturation 1.06, Color Balance `(0.985, 1.00, 1.035)`, Bloom 0.30, Threshold 1.00.
- **Transiciones Suaves:** Interpolación continua entre presets conforme avanza la hora del día (`LightingSystem.Hour`).
- **Viñeta Óptica:** Atenuación sutil en esquinas (`intensity = 0.12`, `smoothness = 0.40`).

---

## 4. Verificación y Resultados de Compilación

```bash
dotnet build unity-migration/Tools/headless-check/check.csproj
# Compilación correcta. 0 Advertencia(s), 0 Errores.

dotnet run --project unity-migration/Tools/headless-check/check.csproj
# meshes 111, verts 1,859,176, chunks 89, buildings 30938, boxes 31258
# NaN verts 0, degenerate tris 83, meshes with bad uv sets 0
# markings: 6091 strips, 83,024 verts, 14 ms
# collision: 31,338 boxes
# car 10s: pos (3.6,626.1) speed 23.50 inside=False
# ped 5s: pos (-11.6,432.2) speed 4.40 y 0.00
# heli 5s: pos (-110.0,477.5) alt 170.3 speed 32.00
# traffic stats: active 232, nan 0, stopped 36, avg 9.47 m/s, max 13.40 m/s
# Exit Code: 0
```
