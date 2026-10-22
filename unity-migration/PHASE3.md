# FASE 3 — Conversión de shaders TSL → HLSL/URP

Puerto de `js/tsl-materials.js` (143 KB) y `js/lighting.js` a URP 17.
Todo el código está en `Assets/Shaders/` y `Assets/Scripts/Rendering/`; este documento explica
la correspondencia, las decisiones y los pasos de configuración.

---

## 3.0 Inventario

| Original TSL | Unity | Fichero |
|---|---|---|
| `facadeCell()` + `evaluateApartmentInterior()` | HLSL compartido | `Shaders/Includes/NycFacade.hlsl` |
| `createPreWarBuildingMaterial()` | Shader lit + 4 pases | `Shaders/Facade.shader` |
| `createCurtainWallMaterial()` | Shader lit + 4 pases | `Shaders/CurtainWall.shader` |
| `createRoadSurfaceMaterial()` | Shader lit | `Shaders/RoadSurface.shader` |
| `createRoadPaintMaterial()` | Shader transparente | `Shaders/RoadPaint.shader` |
| `createSidewalkMaterial()` | Shader lit | `Shaders/Sidewalk.shader` |
| `createWaterMaterial()` | Shader lit | `Shaders/Water.shader` |
| `SkyMesh` (Preetham + nubes) | Skybox | `Shaders/Sky.shader` |
| `hash2D`, `noise2D`, `lineAA`, `gridLine`, `filteredPulse`, `bumpNormal`, `canyonSkyAONode`, `roadWetMasks` | HLSL compartido | `Shaders/Includes/NycCommon.hlsl` |
| `LightingSystem` | MonoBehaviour | `Scripts/Rendering/LightingSystem.cs` |
| `TIME_PRESETS` | ScriptableObject | `Scripts/Rendering/TimeOfDayPreset.cs` |
| `LOOKS` (post-processing) | Overrides de Volume | `LightingSystem.ApplyGrade` |
| `buildRoadPaint()` (geometría) | Builder de mallas | `Scripts/Generation/RoadMarkingBuilder.cs` |
| `cityUniforms` | Uniforms globales | `Scripts/Rendering/NycShaderIDs.cs` |

### El contrato de atributos (recordatorio de la Fase 2)

Cada vértice de fachada lleva exactamente:

```
uv0 = (metros a lo largo del muro, Y mundo)
uv1 = (ancho del muro, alto de la caja)
uv2 = (wallKind 0..4, tono del edificio 0..1)
uv3 = (Y de la coronación de la caja, estilo 0..5)
color = (tono, estilo, semilla, 1)
```

`wallKind`: `0 shop · 1 entry · 2 rear · 3 lotLine · 4 blankStone`.
El shader **no** necesita `box`/`faces` por instancia: todo está horneado en el vértice, así que
funciona igual con el `.glb` (ruta A) que con la malla nativa (ruta B).

---

## 3.1 Fachadas procedurales

### 3.1.1 `NycBuildFacadeCell` — la rejilla

Es el puerto directo de `facadeCell()` (`tsl-materials.js:375-469`). Pasos:

1. **Marco del muro.** `isRoof = step(0.75, |n|.y)`. `alongZ = step(|n|.z, |n|.x)` decide si la
   `u` corre en Z (muros ±X) o en X (muros ±Z). `wallS = uv0.x` ya viene en metros desde la
   esquina izquierda, así que se elimina toda la reconstrucción `positionGeometry` del original.
2. **Bayas enteras.** `bays = max(round(wallW / windowWidth), 1)`, `bayWidth = wallW / bays`.
   Con `cornerPier > 0` (mampostería) se descuentan dos pilas de 1.2 m y las bayas se reparten
   entre ellas; `inPier` marca el vértice dentro de la pila.
3. **Semilla por muro.** `wallSeed = floor(boxSeed + dot(n, (17, 0, 37)))`, con `boxSeed`
   derivado de `uv3.x/uv1` de forma determinista. Así dos muros del mismo edificio no repiten
   el patrón de ventanas.
4. **Celda.** `gridU = wallS / bayWidth + wallSeed`, `gridV = worldY / floorHeight`,
   `cellId = floor(grid)`, `cellUV = frac(grid)`, `fw = fwidth(grid)`.
5. **Máscaras.** `floor0` (planta baja), `isGroundFloor` solo para `kind == shop`, `blank`
   (`kind >= 3`), `back` (`kind == 2..3`), `door` (una baya de entrada por edificio, elegida con
   el tono), `partial` (caja que no es un número entero de plantas) y `coping` (0.32 m bajo la
   coronación).

### 3.1.2 `NycApartmentInterior` — el piso detrás de cada ventana

Puerto de `evaluateApartmentInterior()` (`tsl-materials.js:197-351`). Es lo que hace que las
ventanas no sean agujeros negros:

* Módulo de habitación de **2 o 3 bayas enteras** (nunca una partición en mitad de una ventana),
  con fase por planta.
* Intersección de slab contra la caja de la habitación (`[-halfW..halfW] × [-halfH..halfH] ×
  [setback..setback+depth]`) → `q` normalizado.
* Pared / suelo / techo / fondo según qué plano se cruza; puerta y cuadro en el fondo; lámpara de
  techo con caída radial.
* AO de esquinas y atenuación por profundidad.
* **Cortinas** en el plano del cristal, con parallax (`winUV.x + dirX/dirZ * 0.06`), 6 tejidos,
  pliegues y `emit = 0.20` para que no brillen más que la habitación.
* **LOD**: `lod = smoothstep(0.3, 0.8, max(fw))`; cuando la ventana ocupa un píxel, la habitación
  pasa a su valor medio iluminado y las cortinas a su cobertura media (0.29). Sin parpadeo.

Devuelve `float4(rgb, pesoEmisivo)`.

### 3.1.3 `NycEvaluatePrewar` — mampostería

Capas, en orden de composición:

| Capa | Detalle |
|---|---|
| Apertura | `filteredPulse` en X e Y con `windowRatioX/Y`, ensanchada en planta baja |
| Cornisa | banda de piedra cada 6 plantas (`fract(gridV/6)` en 0.86..1), solo fachadas a calle |
| Sashes | doble-hung: travesaño a 0.52, parteluz a 0.50; en planta baja transom a 0.70 y montantes |
| Aire acondicionado | ~12 % de ventanas, caja 0.31..0.69 × sill..sill+0.22, rejilla y lamas |
| Ladrillo | aparejo a soga 0.56 × 0.24 m, junta 16 mm con `gridLine`, variación por ladrillo y
  desplazamiento cálido, desvanecidos por `fwidth` |
| Hollín | ruido alargado vertical, se apaga por encima de 180 m |
| Alféizar | banda de 32 mm + sombra bajo el alféizar |
| Cristal | interior 3D + suciedad de soda-lime + Fresnel con el color de horizonte |
| Puerta | panel pintado, junquillos, transom vidriado, marco de piedra |
| Coronación | coping de piedra en los 0.32 m superiores |

Emisión: ventanas (`room.rgb * room.a * 0.76`), tiendas en planta baja, *flood* de coronación
(`_CrownGlowHeight`) y transom de la entrada; todo multiplicado por `_NycInteriorGlow`.

### 3.1.4 `NycEvaluateCurtain` — muro cortina

Puerto de `createCurtainWallMaterial()`:

* Vidrio por panel con **abombamiento** (`bowX/bowY`) y normales perturbadas.
* Fresnel + reflejo de cielo (`uSkyHorizon`/`uSkyTop`) + **reflejo analítico de torres vecinas**
  (banda de skyline con su propia rejilla de ventanas) + especular del sol a 48.
* Montantes y spandrel con `mullionThickness` / `spandrelRatio`; plantas mecánicas cada 16 con
  lamas; paneles de hormigón en muros ciegos; coping de aluminio.
* Emisión de apartamentos y escaparates.

### 3.1.5 Materiales a crear (6)

`Assets/Materials/Facades/`:

| Estilo | Shader | Stone / Glass | Trim | Floor | Window | Lit | Warmth | CornerPier |
|---|---|---|---|---|---|---|---|---|
| 0 Pre-war limestone | `Nyc/Facade` | `#D8CDB8` | `#9E937F` | 3.7 | 2.6 | 0.54 | 0.80 | 1.2 |
| 1 Red / terracotta | `Nyc/Facade` | `#A45A45` | `#CAB9A0` | 3.5 | 2.4 | 0.56 | 0.86 | 1.2 |
| 2 Brownstone | `Nyc/Facade` | `#765443` | `#503A2E` | 3.4 | 2.2 | 0.60 | 0.90 | 1.0 |
| 3 Blue-teal glass | `Nyc/CurtainWall` | `#1A3A52` | `#3B4C5B` | 3.9 | 2.7 | 0.60 | 0.52 | — |
| 4 Bronze glass | `Nyc/CurtainWall` | `#242220` | `#3C3631` | 3.8 | 2.5 | 0.64 | 0.72 | — |
| 5 Silver supertall | `Nyc/CurtainWall` | `#264664` | `#B0BCC8` | 4.1 | 2.9 | 0.58 | 0.48 | — |

`_BumpScale` 1.0; `_InteriorFade` 140 m (subir a 200 en cámaras aéreas).

> **Alineación de plantas:** `_FloorHeight` debe coincidir con el `storey` del distrito usado por
> `ManhattanMeshBuilder` (Midtown 3.9, Harlem 3.5, SoHo 3.7…). Si no coincide, el shader dibuja
> una franja de parapeto en la coronación (efecto intencionado, pero conviene evitarlo).

### 3.1.6 Equivalente en Shader Graph

Si se prefiere Shader Graph (mismo resultado, sin HLSL):

| Nodo | Conexión |
|---|---|
| `UV` (canal 1) → `Split` | X = wallW, Y = boxH |
| `UV` (canal 2) → `Split` | X = wallKind, Y = tone |
| `UV` (canal 3) → `Split` | X = topY |
| `UV` (canal 0) → `Split` | X = wallS, Y = worldY |
| Custom Function `FacadeCell` | HLSL `NycBuildFacadeCell` (copiar el include en un Custom Function) |
| `Fresnel Effect` | potencia 2.0 |
| `Lerp` × N | composición de capas |
| `Emission` | ventanas + tiendas + coronación |
| `Baked GI` / `Normal WS` | entradas estándar de URP Lit |

En la práctica, el Shader Graph solo aporta valor si el equipo de arte va a iterar el look; el
HLSL ya está probado y compila en un solo pase.

---

## 3.2 Calles, marcas viales y agua

### 3.2.1 `Nyc/RoadSurface`

Puerto de `createRoadSurfaceMaterial()`:

* Árido a dos escalas, parches de hormigonado, manchas de aceite y grietas de alquitrán, todo
  filtrado por `fwidth` (se funde a su media cuando el detalle baja de un píxel).
* **Tapas de alcantarilla** cada 14 m (una de cada tres celdas), con anillos concéntricos.
* **Mojado**: `NycWetMasks(NycPuddleField())` → `x` manchas húmedas, `y` charcos, `z` película.
  Los charcos se aplanan (`height *= 1 - puddle`) para que el SSR refleje nítido.
* `metalness` = Fresnel del agua × max(puddle, film·0.6): los charcos reflejan a ángulos rasantes
  y oscurecen vistos desde arriba.
* Emisión nocturna: espejo del cielo + neón de Times Square sobre la película húmeda.

### 3.2.2 `Nyc/RoadPaint` + `RoadMarkingBuilder`

La geometría la genera `RoadMarkingBuilder` (menú `NYC/6. Build Road Markings`):

* **Doble línea amarilla** en avenidas de doble sentido (dos tiras sólidas de 12 cm a ±12 cm).
* **Líneas blancas discontinuas** en calles de un solo sentido con ≥ 2 carriles.
* **Paso de cebra continental** en el extremo A de cada enlace del grafo (7 barras de 0.55 m).

El shader:

* Ensancha la tira en el vertex shader hasta 0.75 px de ancho de pantalla y la vuelve a atenuar
  (`widenInv`), de modo que las marcas lejanas se disuelven en su cobertura media.
* Corta las discontinuidades con un **filtro de caja exacto** sobre `t = along / period`.
* Aplica desgaste por ruido y el mismo mojado que el asfalto.

Resultado medido sobre los datos reales: **6 091 tiras, 83 024 vértices, 13 ms**.

### 3.2.3 `Nyc/Sidewalk`

Baldosas de 1.5 m (5 ft), juntas fresadas de 42 mm con `gridLine`, tono por losa, grano de
granito y parches húmedos con `drainage = 0.06` (drena mejor que la calzada).

### 3.2.4 `Nyc/Water`

El original usa una textura FFT de 256² generada en runtime. Aquí se sustituye por **cuatro
capas de ruido con gradiente**, con las mismas escalas (61 / 23.3 / 8.7 / 3.3 m), rotaciones,
RMS y velocidades de fase. Se conserva:

* Energía por rachas de viento (`gust`) y calma en los lagos del parque (`pond`).
* `roughness = pow(varianza, 0.25)` → GGX coherente con las olas no resueltas.
* `metalness` por Fresnel del agua (F0 = 0.02) para que el SSR funcione.
* Color de estuario (silt) vs. lago.
* **Reflejo nocturno analítico**: se interseca el rayo reflejado contra el *slab* de Manhattan,
  se proyecta la altura `hRefl = t·tanEl`, y se dibuja un skyline de lotes de 24 m con ventanas
  de 3.2 × 3.6 m, difuminado por la varianza de las olas. Eso produce las estrías verticales de
  luz que el SSR no puede alcanzar.

---

## 3.3 Iluminación, niebla y ciclo día/noche

### 3.3.1 `TimeOfDayPreset`

ScriptableObject con **todos** los campos de `TIME_PRESETS` + `LOOKS`. Se crean con
`NYC/5. Create Time Of Day Presets`, que replica los valores exactos:

| | golden 17:45 | midday 13:30 | twilight 19:15 | midnight 23:00 |
|---|---|---|---|---|
| sunDir | (-0.44, 0.58, -0.68) | (-0.34, 0.82, -0.46) | (-0.52, 0.36, -0.77) | (0.42, 0.68, -0.60) |
| sunColor | `#FFCA92` | `#FFF5E8` | `#FF9466` | `#8CB0E8` |
| intensity | 5.8 | 6.6 | 3.1 | 1.45 |
| hemi | `#9ABBE6` / `#C88A62` @1.48 | `#AAD2FF` / `#B89678` @1.58 | `#4C6CA8` / `#6E5048` @0.85 | `#263A62` / `#382E36` @0.72 |
| skyTop / horizon | `#2C568C` / `#EBD1B5` | `#225EA8` / `#C8DFF5` | `#102248` / `#B86B6C` | `#060C1A` / `#182640` |
| fog | `#DCC5AE` @2.4e-4 | `#C6DCF2` @1.8e-4 | `#353C58` @3.2e-4 | `#0C1424` @3.6e-4 |
| interiorGlow | 0.48 | 0.24 | 1.25 | 1.55 |
| roadWetness | 0.28 | 0.08 | 0.55 | 0.62 |
| env / bounce | 0.54 / 0.52 | 0.58 / 0.58 | 0.36 / 0.32 | 0.30 / 0.18 |
| turbidity / rayleigh | 8.0 / 2.8 | 6.5 / 2.4 | 9.5 / 3.4 | 4.0 / 1.5 |
| mie / g | 0.008 / 0.88 | 0.006 / 0.86 | 0.010 / 0.90 | 0.004 / 0.80 |
| clouds | 0.36 / 0.38 | 0.32 / 0.35 | 0.40 / 0.42 | 0.22 / 0.25 |
| skyExposure | 0.148 | 0.142 | 0.115 | 0.022 |
| nightFactor | 0 | 0 | 0.28 | 0.86 |
| grade (exp/cont/sat) | 1.04 / 1.12 / 1.08 | 1.00 / 1.10 / 1.06 | 1.06 / 1.10 / 1.08 | 1.04 / 1.08 / 1.06 |
| bloom / threshold | 0.24 / 1.20 | 0.18 / 1.35 | 0.28 / 1.05 | 0.30 / 1.00 |

### 3.3.2 `LightingSystem`

En cada cambio de preset (o tick del ciclo) hace, en este orden:

1. **Sol**: `transform.rotation = LookRotation(-sunDir)`, color, intensidad, sombras soft,
   `shadowNormalBias = 0.35`.
2. **Ambiente**: `AmbientMode.Trilight` con cielo / ecuador / suelo derivados del HemisphereLight
   (barato y estable; no requiere re-bake del probe).
3. **Niebla**: `FogMode.ExponentialSquared` con la densidad del preset (equivalente exacto a
   `FogExp2` de three).
4. **Skybox**: turbidity, rayleigh, mie, g, sunDir, exposure, nightFactor, nubes.
5. **Globales** que leen todos los shaders:
   `_NycSunColor`, `_NycSkyTopColor`, `_NycSkyHorizonColor`, `_NycSunDir`, `_NycInteriorGlow`,
   `_NycRoadWetness`, `_NycCanyonBounce`, `_NycCpBounds`, `_NycIslandBounds`, `_NycTime`.
   Los colores se envían en **lineal** (`Color.linear`), como `new THREE.Color(hex)` en three.
6. **Grading**: overrides de `Bloom`, `ColorAdjustments` (`postExposure = log2(exposure)`,
   contraste y saturación ×100, white balance como `colorFilter`), `Tonemapping = ACES`,
   `Vignette 0.12`.
7. **Reflection probe** (`RenderProbe()`), si está asignado.

**Ciclo continuo**: `runCycle = true` avanza `hour` a `1 / (realMinutesPerGameHour · 60)` horas
por segundo, mezcla los dos presets vecinos (`TimeOfDayPreset.Lerp`) y refresca el probe de
ambiente cada 0.25 s. `SetHour(0..24)` es la API pública.

**Sombras**: `shadowDistance` 300 m a pie, 720 m por encima de 80 m de altitud (con histéresis),
igual que `LightingSystem.update()` en el original.

### 3.3.3 `Nyc/Sky`

Puerto de Preetham (constantes `totalRayleigh`, `totalMie = (0.2,0.21,0.2)·0.434·mie`, fase
Rayleigh y Mie, disco solar de 0.53°) con la exposición del preset y mezcla a `_NightColor` por
`nightFactor`. Encima, una capa de nubes FBM de 4 octavas modulada por `cloudCoverage` /
`cloudDensity` y teñida por el sol.

### 3.3.4 Post-proceso URP

Crear un `Volume` global con perfil `NYC_Global`:

| Override | Valor |
|---|---|
| Tonemapping | ACES |
| Bloom | intensity/threshold por preset, scatter 0.7 |
| Color Adjustments | post exposure, contrast, saturation, color filter |
| Vignette | 0.12 / 0.4 |
| Screen Space Reflections | activado en el Renderer (la lluvia y el agua dependen de él) |

---

## 3.4 Pasos en el Editor

1. **Materiales**: `NYC/1. Create Default Materials` (Fase 2) y luego sustituir el shader de los
   6 materiales de fachada por `Nyc/Facade` / `Nyc/CurtainWall` con la tabla 3.1.5.
2. **Suelo**: `Ground` → submesh 0 `Nyc/Land` (o Lit), 1 `Nyc/Sidewalk`, 2 `Nyc/Park`,
   3 `Nyc/Water`. `Roads` → `Nyc/RoadSurface`.
3. **Marcas**: añadir `RoadMarkingBuilder` a un GameObject vacío, asignar `Nyc/RoadPaint` y
   ejecutar `NYC/6. Build Road Markings`.
4. **Presets**: `NYC/5. Create Time Of Day Presets`.
5. **Sky**: material con `Nyc/Sky` → `Window > Rendering > Lighting > Environment > Skybox Material`.
6. **Volume**: crear `NYC_Global` con los overrides de 3.3.4 y asignarlo a `LightingSystem`.
7. **LightingSystem**: en el GameObject `Systems`, asignar `sun` (Directional Light), `skyMaterial`,
   `volumeProfile`, `reflectionProbe` y los 4 presets. Activar `runCycle` si se quiere ciclo.
8. **URP Asset**: HDR on, MSAA 4×, shadow distance 300, 4 cascadas, soft shadows, additional
   lights 256, SSR activado en el Renderer.

---

## 3.5 Verificación

Compilación de todos los scripts con el harness (`Tools/headless-check`, stubs de Unity/URP):

```
Compilación correcta. 0 advertencias, 0 errores
```

Ejecución del builder + marcas sobre `nyc-geodata.js`:

```
meshes 91, verts 1,662,327, chunks 89, buildings 30938, boxes 31258
bounds X [-24117,24569] Y [-2.20,302.3] Z [-28512,29090]
NaN verts 0, degenerate tris 3, meshes with bad uv sets 0
markings: 6091 strips, 83,024 verts, 13 ms
```

Los shaders no se pueden compilar fuera de Unity (dependen de los includes de URP), pero se han
revisado contra las firmas de URP 17 (`UniversalFragmentPBR` de 8 argumentos,
`TransformWorldToShadowCoord`, `SampleSH`, `GetNormalizedScreenSpaceUV`, pases
`UniversalForward` / `ShadowCaster` / `DepthOnly` / `DepthNormals`).

---

## 3.6 Limitaciones conocidas

* **Ghost signs** (carteles pintados desgastados en muros medianeros): el original dedica ~40
  líneas a generar glifos; aquí se omite. Se puede añadir como textura decal por edificio.
* **FFT del agua**: sustituida por ruido de gradiente. El aspecto es equivalente a distancia de
  calle, algo menos coherente en primer plano.
* **Fog height-based**: URP usa niebla exponencial uniforme; el original mezcla además el color
  del cielo por altura. Se puede recuperar con un Renderer Feature de niebla a medida.
* **SSR**: el original hace SSR a media resolución con *fade* por metalness. URP lo hace en el
  Renderer; activar "Screen Space Reflection" y limitar la distancia a ~40 m.
* **Interior mapping**: el coste por píxel es alto. Si hace falta, bajar `_InteriorFade` a 80 m o
  añadir un keyword `SIMPLE_INTERIOR` que devuelva `lit * roomTint` sin intersección de slab.
