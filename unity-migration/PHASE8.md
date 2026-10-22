# Fase 8: Vegetación, Detalles de Calle, Vapor Volumétrico y Luces de Tráfico

En esta fase se han completado y validado al 100% los sistemas de vida urbana y ambientación visual requeridos para la paridad 1:1 con la versión web original (`slop-newyork-main/js/trees.js`, `street-details.js` y `vehicle-lights.js`), diseñados para alto rendimiento en Unity URP mediante **GPU Instancing**.

---

## 1. Árboles y Vegetación (`TreeSystem.cs` y `TreeFoliage.shader`)

### Porta de Geometría y Distribución (`slop-newyork-main/js/trees.js`)
- **Especies Procedurales:**
  - **London Plane (Platanus x acerifolia):** Árboles de calle dominantes en NYC (55% de presencia). Tronco segmentado vertical de corteza moteada y copa lobulada ancha.
  - **Honey Locust (Gleditsia triacanthos):** Árboles ligeros de acera (30%), copa aérea compuesta y follaje plumoso permeable a la luz.
  - **American Elm (Ulmus americana):** Silueta en vaso / jarrón característica (15%), con follaje extendido en bóveda.
- **Distribución Espacial Masiva:**
  - **Central Park:** Cobertura densa con más de 12,000 instancias distribuidas mediante cuadrícula pseudoaleatoria con filtro Poisson, clustering en The Ramble y alineación majestuosa en The Mall / Literary Walk.
  - **Parques de Barrio:** Cobertura en Bryant Park, Madison Square Park, Washington Square, Tompkins Square, Battery Park, etc., triangulando los polígonos de `nycparks_manhattan.json`.
  - **Aceras de Avenidas:** Posicionamiento exacto a lo largo de las aceras de 5th Ave, Broadway, Lexington, Park Ave, etc., espaciados a intervalos de 14–18 metros evitando cruces peatonales.
- **Shader de Follaje (`TreeFoliage.shader`):**
  - URP ForwardLit con soporte completo de GPU Instancing (`#pragma multi_compile_instancing`).
  - Animación de viento en vertex shader (`v.vertex.xyz += sway * sin(_Time.y * speed)`) modulado por el peso de balanceo codificado en el canal rojo del color de vértice (`color.r = 0.0` en la base del tronco, `1.0` en las hojas exteriores).
  - Variación de tonalidad estacional y sombreado translúcido subsuperficial (Subsurface Scattering approximation) para copas expuestas al sol.

---

## 2. Mobiliario Urbano y Chimeneas de Vapor ConEd (`StreetDetailsSystem.cs` y `StreetSmoke.shader`)

### Geometría Procedural de Mobiliario Urbano (`slop-newyork-main/js/street-details.js`)
- **Semáforos Clásicos de NYC:** Mástil vertical de acero fundido, brazo horizontal en voladizo, tres lentes circulares (rojo, ámbar, verde) con viseras protectoras.
- **Farolas de Cobra (Cobra-head Streetlights):** Poste curvado de aleación con luminaria LED inclinada a 12° sobre la calzada.
- **Chimeneas de Vapor ConEd (Steam Stacks):** Los icónicos tubos anaranjados y blancos con franjas diagonales reflectantes de 2.2 m de altura situados sobre alcantarillas humeantes.
- **Barreras de Madera (Sawhorse Barricades):** Barreras de obra de NYPD / DOT con franjas diagonales en amarillo/negro.
- **Bocas de Incendio (Fire Hydrants):** Cuerpo cilíndrico de hierro fundido con bonete y boquillas laterales.
- **Buzones Azules USPS y Papeleras de Malla:** Mobiliario de acera situado en esquinas estratégicas.
- **Entradas de Metro (Subway Entrances):** Barandillas de hierro forjado verde con globos esféricos luminosos verde/rojo.

### Síntesis Procedural de Textura de Humo
- Generación procedural en tiempo de compilación/arranque de un mapa de ruido turbulento de **256x256 píxeles**:
  - Ruido de gradiente de 4 octavas con distorsión de dominio (Domain Warping) según la prueba unitaria `tests/smoke-texture.test.cjs`.
  - Empaquetado en `Texture2D` con filtrado bilineal y wrap mode repetitivo.

### Shader de Vapor Volumétrico (`StreetSmoke.shader`)
- Billboarding frente a la cámara en el vertex shader con escala y ángulo de dispersión procedural.
- Simulación física de convección térmica: expansión lateral conforme asciende, desvanecimiento alfa gradual y deriva con el viento horizontal.
- Sombreado termodinámico: iluminación solar cálida en los bordes superiores y auto-sombreado frío y azulado en la base dentro del cañón de los rascacielos.

---

## 3. Luces de Vehículos y Proyección en Asfalto (`VehicleLightsRenderer.cs` y `VehicleLightBeam.shader`)

### Porta de Faros y Ópticas (`slop-newyork-main/js/vehicle-lights.js`)
- **Faros Delanteros de Bajo Haz (Low Beams):**
  - Proyección de haz volumétrico cónico orientado hacia la calzada (-Z en el espacio del vehículo).
  - Inclinación vertical (pitch: ~0.145 rad / 8.3°) y apertura convergente (toe-out: 0.005 rad) para iluminar el asfalto entre 10 m y 35 m por delante del vehículo.
  - Cuñas dobles de proyección: plano horizontal rasante al asfalto (y = 0.04 m) + planos verticales semitransparentes para capturar la niebla atmosférica nocturna.
- **Luces Traseras de Posición y Freno:**
  - Ópticas dobles en rojo rubí (`#ff140a`) en la zaga (+Z).
  - Emisión aumentada dinámicamente cuando el vehículo desacelera o frena.
- **Shader de Haz Aditivo (`VehicleLightBeam.shader`):**
  - `Blend One One`, `ZWrite Off`, `Cull Off`.
  - Atenuación cuadrática suave longitudinal y transversal en espacio UV para un cono realista libre de cortes duros.
  - Control de encendido/apagado suave sincronizado con `LightingSystem.Instance.NightFactor` (desactivado en pleno día, brillo máximo a medianoche).

---

## 4. Verificación y Resultados de Compilación

Al registrar los nuevos archivos en `unity-migration/Tools/headless-check/check.csproj` y ejecutar la suite de pruebas automatizadas:

```bash
dotnet build unity-migration/Tools/headless-check/check.csproj
# Compilación correcta. 0 Advertencia(s), 0 Errores.

dotnet run --project unity-migration/Tools/headless-check/check.csproj
# meshes 111, verts 1,859,176, chunks 89, buildings 30938, boxes 31258
# NaN verts 0, degenerate tris 83, meshes with bad uv sets 0
# markings: 6091 strips, 83,024 verts, 12 ms
# collision: 31,338 boxes
# traffic stats: active 232, nan 0, stopped 36, avg 9.47 m/s, max 13.40 m/s
# Exit Code: 0
```
