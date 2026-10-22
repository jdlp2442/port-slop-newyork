# FASE 6 — HUD y mapas (UI Toolkit)

Port de `index.html` + `css/style.css` + `js/nyc-map.js` + la parte de UI de `main.js` a
**UI Toolkit** (`uxml` + `uss` + `C#`).

---

## 6.0 Resultado

```
map atlas 512px: 14690x14690 m, land 22.3%, parks 1.6%, roads 20.25%, 14 ms
map projection round-trip at 5th/42: (0.00,0.00)
```

* Atlas cartográfico horneado una sola vez (2048², ~1 s) desde los mismos datos que la ciudad.
* HUD completo con el sistema de diseño del original (glass, taxi yellow, NYC DOT green).
* Radar minimapa + atlas a pantalla completa con búsqueda y click-para-teletransportar.
* Todo el teclado del original: `1-4`, `5-0`, `V`, `H`, `M`, `Esc`.

---

## 6.1 Cartografía — `NycMapBaker.cs`

`nyc-map.js` dibuja cada frame en un canvas 2D. Aquí se **rasteriza una vez** y la UI solo mueve y
escala la textura: el coste por frame es cero.

| Capa | Color |
|---|---|
| Agua | `#0A1118` |
| Tierra | `#1A1E24` |
| Parques | `#183424` |
| Islas de acera | `#282E36` |
| Calles | `#4E5864` |
| Avenidas | `#687482` |
| Autopistas | `#967442` |
| Landmarks | `#C4A056` |

Orden: agua → tierra → parques → bloques → calles → landmarks. Las polilíneas se pintan con
Bresenham de grosor proporcional al ancho real; las avenidas y autopistas salen más gruesas.

**Proyección** (norte arriba, igual que el original):

```csharp
Vector2 WorldToPixel(float x, float z) => new Vector2(
    (x - WorldRect.x) / WorldRect.width  * Size,
    (z - WorldRect.y) / WorldRect.height * Size);   // y = 0 es el norte

Vector2 PixelToWorld(Vector2 p) => new Vector2(
    WorldRect.x + p.x / Size * WorldRect.width,
    WorldRect.y + p.y / Size * WorldRect.height);
```

`Rasterize` es pública para poder inspeccionarla desde el harness (por eso se miden los % de
cobertura); `Bake` añade el volteo vertical (las texturas de Unity van de abajo arriba) y crea la
`Texture2D`.

---

## 6.2 HUD en UI Toolkit

### Ficheros

| Fichero | Contenido |
|---|---|
| `Assets/UI/HUD.uxml` | layout completo (20 secciones, ~110 nodos) |
| `Assets/UI/HUD.uss` | sistema de diseño traducido de `style.css` |
| `Assets/Scripts/UI/HUDController.cs` | bindings, teclado, refresco de telemetría |

### Tokens (de `style.css:5-39`)

```
glass rgba(18,19,22,.62) · border rgba(255,255,255,.09) · r-panel 16 · r-ctl 10
text #F6F5F2 · text-2 .68 · text-3 .44 · taxi #FFC21A · taxi-ink #17140A · sign-green #0B6A46
ok #5EE08F
```

Lo que **no** existe en USS y cómo se resuelve:

| CSS | UI Toolkit |
|---|---|
| `backdrop-filter: blur(22px)` | no soportado → fondo translucido + borde de 1 px |
| gradientes (`linear-gradient`) | no soportados → color plano |
| `box-shadow` | no soportado → `border` + fondo fuerte |
| `transform-origin: left top` | se calcula a mano (ver §6.4) |
| `text-overflow: ellipsis` | no soportado → ancho fijo |
| iconos SVG | se usan glifos de texto (`▲`, `‹`, `›`, `NY`, `M`, `N`) |

### Estructura del UXML

```
hud-root (picking-mode Ignore: los clics pasan al juego)
├── .top-left      brand (NY bullet + título + fps)
├── .top-right     toolbar (4× hora, teleport, map, hide) + teleport-popover
├── .bottom-left   location-place + street-sign + radar
│                 └── radar → minimap-map, minimap-player, radar-north, radar-key
├── .bottom-center controls-hint + mode-switch (indicator + 4 botones)
├── .bottom-right  cluster (gauge + camera-switch + camera-popover)
└── .fullmap       canvas-wrap → fullmap-map, header, results, hint
```

Cada popover y el atlas llevan `picking-mode="Position"` para recibir clics; el resto del HUD
los ignora.

---

## 6.3 `HUDController.cs`

* **Bindings**: `root.Q<Button>("mode-person")`, `root.Q<Label>("speed")`, etc.
* **Modo**: `controllers.SetMode(...)`; si la cámara activa es escénica, vuelve a `chase`
  (igual que `main.js`).
* **Hora**: `lighting.ApplyPreset("golden" | "midday" | "twilight" | "midnight")`.
* **Cámara**: 11 botones + prev/next + `CycleCamera(±1)`; `ResetForTeleport()` al teletransportar.
* **Teletransporte**: 8 botones → `controllers.TeleportTo(id)`.
* **Telemetría** cada 200 ms (como el original): FPS suavizado, mph, altitud (solo visible en
  helicóptero), barra de velocidad escalada al tope de cada modo (18/81/43/116 mph), etiqueta de
  modo y de cámara, rótulo de calle y pista de controles.
* **Teclado**: `1-4` modos, `5-0` cámaras, `V`/`Shift+V` ciclar, `H` ocultar HUD, `M` mapa, `Esc`
  cerrar mapa.
* **Píldora taxi**: se posiciona sobre el botón activo con `layout.x` / `layout.width`.

```csharp
void PlaceModeIndicator()
{
    Button active = _modeButtons[i];              // el que tenga la clase "active"
    Rect layout = active.layout;
    if (layout.width <= 1f) return;               // layout aún no resuelto
    _modeIndicator.style.left = layout.x;
    _modeIndicator.style.width = layout.width;
}
```

---

## 6.4 Mapas — `NycMapSystem.cs`

Un único `NycMapSystem` alimenta el radar y el atlas.

### Escalar y centrar sin `transform-origin`

Con el origen por defecto (centro), un punto local `p` cae en pantalla en:

```
screen = S/2 + (p − S/2)·s + t
```

De ahí la traslación que deja al jugador en el centro del radar (`C` = centro del contenedor):

```csharp
float s = (h / (2f * minimapRadius)) / _map.PixelsPerMetre;
_tx = w * 0.5f - pixel.x * s + S * (s - 1f) * 0.5f;
_ty = h * 0.5f - pixel.y * s + S * (s - 1f) * 0.5f;

_mapEl.style.scale = new Scale(new Vector3(s, s, 1f));
_mapEl.style.translate = new Translate(_tx, _ty);
_player.style.rotate = new Rotate(new Angle(-heading * Rad2Deg, AngleUnit.Degree));
```

La inversa (para el click) es la misma fórmula despejada:

```csharp
Vector2 ScreenToPixel(Vector2 local) => new Vector2(
    (local.x - _tx - S * 0.5f) / _scale + S * 0.5f,
    (local.y - _ty - S * 0.5f) / _scale + S * 0.5f);
```

### Interacción del atlas

* **Arrastrar** → pan (`PointerDown/Move/Up`).
* **Rueda** → zoom alrededor del cursor conservando el punto del mundo que hay debajo.
* **Click sin arrastrar** → `TeleportActiveTo(world)` + `cameras.ResetForTeleport()` + cerrar.
* **Buscador** → filtra calles, avenidas, landmarks y teletransportes; al elegir uno centra el mapa.

---

## 6.5 `LocationLabel.cs`

Port ligero de `getLocationLabel()`:

1. Landmark a menos de 80 m → su nombre.
2. Dentro de Central Park → `"Central Park · near 65th St"`.
3. Si no, la avenida más cercana (cuyos segmentos cubren esa z) + la calle más cercana (cuyos
   segmentos cubren esa x) → `"5th Ave & 42nd St"`.
4. Fuera de la isla → `"New York Harbor"`.

Incluye el sufijo ordinal (`1st`, `2nd`, `3rd`, `11th`, `12nd`…).

---

## 6.6 Montaje en Unity

1. **PanelSettings**: `Assets → Create → UI Toolkit → Panel Settings`. Asignar la fuente (Inter si
   está disponible) y el `Scale Mode` = `Scale With Screen Size` (1920×1080, match height).
2. Crear un GameObject `HUD` con `UIDocument`; asignarle el PanelSettings y `Source Asset` =
   `HUD.uxml`. Añadir `HUDController`.
3. Arrastrar referencias: `controllers`, `cameras`, `lighting`, `mapSystem`.
4. `NycMapSystem` se inicializa solo (`Initialize(root)`); la primera vez hornea el atlas
   (~1 s, conviene con pantalla de carga).
5. Opcional: un `TextAsset` con las fuentes y los iconos como sprites para sustituir los glifos.

---

## 6.7 Verificación

```
map atlas 512px: 14690x14690 m, land 22.3%, parks 1.6%, roads 20.25%, 14 ms
map projection round-trip at 5th/42: (0.00,0.00)
```

* 22,3 % de tierra en el cuadrado que contiene la isla: coherente con la forma de Manhattan.
* 20,25 % de píxeles de calle: la retícula está completa.
* El round-trip mundo→píxel→mundo es exacto, así que el click-para-teletransportar acierta.
* Los 4 ficheros C# de UI compilan contra los stubs de UI Toolkit.

---

## 6.8 Diferencias con el original y cómo completarlas

| Original | Aquí | Cómo completarlo |
|---|---|---|
| Iconos SVG de 20 trazos | glifos de texto | exportar los SVG a PNG/SDF y usar `VisualElement` con `background-image` |
| `backdrop-filter` | fondo translucido | pase de blur a pantalla completa (Renderer Feature) o `UnityGLTF` + custom panel |
| Píldora deslizante animada | salto instantáneo | `experimental.animation` o lerp en `Update` |
| Mapa con carreteras rotuladas | carreteras sin etiquetas | dibujar el nombre con `TextCore` sobre el atlas, o un `Label` por calle cercana |
| Minimapa rotando con el heading | norte fijo | el original también usa norte fijo (comentario en `nyc-map.js`) |
| Ruta GPS al teletransportar | no implementada | polyline con `Painter2D` sobre el atlas |
| Pantalla de carga | no implementada | `VisualElement` con la barra del original mientras se hornea el atlas |
