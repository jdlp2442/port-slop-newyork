# Investigación de port y ruta de paridad

## Decisiones verificadas

| Fuente | Hallazgo aplicable | Integración en este port |
|---|---|---|
| `Unity-Technologies/brg-shooter` | Ejemplo oficial de `BatchRendererGroup` con Burst/Job System y objetivo de rendimiento móvil. | Mantener `NycVehicleBRGBackend` como piloto de un lote; ampliar solo después de validar culling, sombras y material. |
| `AlexMerzlikin/Unity-BatchRendererGroup-Boids` | Muestra el contrato de shader URP con DOTS instancing y advierte que un NaN de simulación rompe el flock. | Mantener validación de NaN, offsets raw, matrices packed y fallback `RenderMeshInstanced`. |
| `endel/NativeWebSocket` | En Unity 2.x los eventos se despachan por `SynchronizationContext`; `DispatchMessageQueue` solo hace falta sin ese contexto. | `NycRemoteSession` no añade polling manual; se conserva `Application.runInBackground` y protocolo opt-in. |
| `KhronosGroup/SPIRV-Cross` | La reflexión permite inspeccionar recursos, set/binding y convenciones de clip-space; `fixup_clipspace` no debe sustituir la matriz de proyección sin evidencia. | Usar SPIR-V-Cross para auditoría de bindings y profundidad, nunca para traducir shaders automáticamente. |
| `morluto/rea` | Flujo de reverse engineering basado en evidencia y fuentes oficiales; el repositorio indica usar sus canales oficiales. | Usar REA únicamente para observación autorizada del comportamiento del proyecto web y guardar fixtures reproducibles. |

## Ruta para portar el proyecto entero

1. **Contrato de datos:** congelar `nyc-geodata.js`, ubicaciones, horas, cámara y marco `(x,y,-z)`.
2. **Simulación:** comparar decisiones por conexión, spawn/despawn, merge claims, señales, peatones y colisiones con fixtures de `traffic.js`, `road-network.js`, `pedestrians.js` y `controllers.js`.
3. **Actores:** igualar `actor-models.js` y `pedestrians.js` por categoría, escala, pivote, materiales, decals, luces y animación.
4. **Props y distribución:** portar reglas y semillas de `trees.js`, `street-details.js`, landmarks y boroughs; medir conteo, bounds y transformaciones.
5. **Render:** comparar bindings, clip-space, profundidad, sombras, bloom, niebla, ventanas, asfalto y luces bajo 16 capturas.
6. **Rendimiento:** migrar lotes a BRG solo si el fallback y las capturas permanecen equivalentes; medir CPU, GPU, memoria y draw calls.
7. **Interacción y red:** conservar NativeWebSocket opt-in para telemetría, HUD y herramientas; no convertirlo en autoridad de simulación.
8. **Cierre:** Play smoke, headless, snapshots, consola limpia y builds Windows/WebGL reproducibles.

## Estado actual

- Escena jugable registrada como escena 0.
- Fixed-step e interpolación visual implementados para jugador, tráfico y peatones.
- Merge claims y spawn/despawn deterministas implementados.
- 99.474 fixtures lane/roll deterministas.
- BRG experimental para un lote con fallback activo.
- Taxi controlable con placas, medalliones T&LC y hubs de aleación.
- Matriz Unity de 16 capturas generada.
- Captura Web bloqueada en inicialización WebGPU en Chrome headless; no se considera evidencia de paridad hasta disponer de backend WebGPU operativo.

## Siguiente trabajo verificable

1. Generar snapshots de decisión del Web con una ejecución instrumentada y compararlos con `PreviewNextLane`.
2. Capturar el taxi Web en cámara chase/lateral/frontal cuando WebGPU esté operativo.
3. Ajustar escala y cámara del taxi con error medido, no por percepción subjetiva.
4. Pasar el mismo proceso a peatones, NYPD, buses, árboles, props y helicóptero.

