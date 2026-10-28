# Importación de arte desde el proyecto web (props, vehículos, iconos)

**Fecha:** 2026-10-23
**Objetivo:** sustituir la geometría procedural por la geometría **del propio proyecto web**,
exportada a GLB, para conseguir paridad de siluetas sin reescribir los generadores.

## Pipeline creado (en el repositorio web)

| Fichero | Función |
|---|---|
| `tools/export-actors-glb.js` | Exportador que corre **dentro de la página** y usa las funciones originales (`buildCarGeometry`, `buildStreetDetails`, `buildTreeGeometries`, `buildPersonGeometry`) para generar los GLB. Añade `TEXCOORD_1 = (partId, 0)` y `COLOR_0 = color de pieza`, que es exactamente lo que leen `Nyc/VehiclePaint` y `Nyc/Pedestrian`. |
| `tools/run-export.mjs` | Runner headless (Chrome por CDP, WebGPU real): abre la app, espera `window.__NYC_APP.ready`, importa el exportador y escribe los GLB en disco. Uso: `node tools/run-export.mjs vehicles|props|trees`. |

Los materiales de `street-details.js` recibieron nombres semánticos (`prop_signal_pole`,
`prop_hydrant`, …) para poder identificar cada prototipo en la exportación; es un cambio aditivo
que no altera geometría ni posiciones.

## Arte importado (51 GLB en `Assets/Resources/Art/`)

| Categoría | Cantidad | Detalle |
|---|---|---|
| Vehículos | 7 | taxi, sedan, SUV, NYPD (con lightbar), bus MTA (cuerpo + letrero + ruedas), helicóptero (cuerpo + 2 rotores), peatón |
| Props | 35 | semáforos (poste/cabeza), farolas, globos peatonales y MTA, hidrantes, buzones, árboles de calle (15 variantes), carritos, tapas de árbol |
| Árboles | 9 | 3 especies × 3 LOD (completo, medio, tronco) |
| Iconos HUD | 22 SVG | extraídos de los `<symbol>` de `index.html`, en `Assets/UI/Icons/` |

## Conexión en Unity

- `Assets/Scripts/Art/ImportedArtLibrary.cs`: carga y cachea los GLB desde `Resources/Art/`,
  con `GetMesh(name, flipZ)`. El **flip de 180° en Y** es obligatorio: el web construye los
  vehículos con el morro en **+Z** (`BODY_SPECS.zFront > 0`) y este pipeline usa **−Z** como
  frente; sin él todos los coches circularían marcha atrás.
- `VehicleMeshFactory.CreateCar/CreatePoliceCar/CreateBus/CreateTruck`: devuelven la malla
  importada si existe, con **fallback** a la generación procedural (así el harness headless y un
  checkout sin exportar siguen funcionando).
- `StreetDetailsSystem.CreateHydrantMesh/CreateMailboxMesh`: usan los GLB del web.
- El coche del jugador mantiene su construcción procedural: incluye extras que el GLB no tiene
  (placas NY, medallones T&LC, faros con emisión) y ya usaba cuerpo *lofted*.

## Resultado medido

| Métrica | Antes | Después |
|---|---:|---:|
| `total diff %` medio (16 pares) | 55,6 | **48,5** |
| error medio (midtown_midday) | 101,4 | **36,4** |
| escena diff % medio | 93,6 | **91,7** |

Las diferencias que quedan son sobre todo de **iluminación/exposición por preset** (luma U/W
desigual, p. ej. twilight 22/89) y de **contenido ausente** (tráfico visible lejos del spawn,
peatones, HUD), no de siluetas de vehículos.

## Pendiente

1. Rasterizar los 22 SVG del HUD a PNG 64² — el script `tools/export-hud-icons.mjs` está escrito
   y validado sintácticamente, pero el sandbox de este entorno mata el proceso cuando lanza
   Chrome; ejecutarlo en una shell sin sandbox.
2. Conectar el resto de props (semáforos, farolas, vapor) y los árboles GLB a `TreeSystem`.
3. Banda de transporte: mover los `vehicle_*.glb` a `Addressables` cuando se active BRG.

## Cómo reproducir

```powershell
cd D:\escena\slop-newyork-main
node tools/run-export.mjs vehicles
node tools/run-export.mjs props
node tools/run-export.mjs trees
```
