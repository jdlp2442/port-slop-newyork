# Referencias para terminar el port

Este documento convierte los repositorios entregados en tareas del port, sin copiar código de
terceros dentro de `Assets/`. Las licencias y avisos de cada dependencia deben conservarse si se
incorpora código fuente.

## Qué aporta cada referencia

- **SPIRV-Cross**: herramienta de reflexión y traducción de SPIR-V a lenguajes de shader. Sirve
  para comparar recursos, bindings y convenciones de clip-space entre shaders WebGPU/SPIR-V y HLSL
  de URP. No sustituye la compilación de Unity ni debe usarse para traducir a ciegas un shader.
- **Unity brg-shooter**: patrón oficial para `BatchRendererGroup` más Burst/Job System. Es la
  referencia para reemplazar los lotes de vehículos y peatones cuando el coste de
  `Graphics.RenderMeshInstanced` sea el cuello de botella.
- **Unity-BatchRendererGroup-Boids**: ejemplo mínimo de shader URP con DOTS instancing y del
  contrato de buffer de instancia. Es útil para validar el layout de matrices, colores y
  `MetadataValue` antes de tocar la ciudad completa.
- **NativeWebSocket**: transporte UPM multiplataforma (`#upm-2`) para WebGL, Windows, Android e iOS.
  El proyecto ya lo declara en `Packages/manifest.json` y `NycRemoteSession` lo usa como puente
  opcional de telemetría/control. La conexión está desactivada por defecto; el arnés headless usa
  automáticamente un stub para no depender de paquetes Unity.
- **rea**: flujo de ingeniería inversa con evidencia para JavaScript, .NET, WASM, binarios y
  comportamiento de runtime. Se usa para responder una pregunta concreta sobre el original y
  conservar evidencia antes de implementar el equivalente en Unity.

## Estado implementado en este ciclo

`NycRemoteSession` acepta mensajes JSON pequeños:

```json
{"type":"teleport","x":0,"z":0}
{"type":"mode","mode":"Car"}
{"type":"hour","hour":19.25}
```

Cuando `sendTelemetry` está activo, envía `state` con posición del jugador, modo, hora y vehículos
activos. No es autoridad de red ni multiplayer; es una interfaz de inspección que permite comparar
el estado de una ejecución Unity con el original.

## Flujo REA recomendado

1. Trabajar solo sobre una copia o un binario que se tenga autorización para analizar.
2. Formular una pregunta verificable: por ejemplo, “¿cómo decide `traffic.js` el siguiente carril
   en un cruce?”
3. Analizar los módulos mínimos (`js/traffic.js`, `js/road-network.js`, `js/main.js`) y guardar la
   evidencia y los desconocidos.
4. Implementar el equivalente en C# y añadir un caso headless que compare entradas/salidas.
5. No convertir pseudocódigo en verdad: contrastar cada conclusión con el código fuente, tests y
   una ejecución observable.

La versión publicada de REA exige Node.js 22.19+; este entorno tiene Node 22.13.0, por lo que la
ejecución local emitió una advertencia de engine y el analizador no pudo completar su comprobación
de integridad del directorio. Se deja como tarea de entorno actualizar Node antes de automatizar
el análisis.

## Siguiente implementación técnica

1. Instrumentar `TrafficSystem` y `RoadNetwork` para exportar snapshots de carril, elección de
   conexión y señales.
2. Comparar esos snapshots con `traffic.js` usando fixtures pequeños y tolerancias numéricas.
3. Crear un backend BRG experimental para un solo lote de vehículos; mantener el backend actual
   como fallback hasta validar shaders DOTS instancing, sombras y picking.
4. Repetir el proceso para peatones y props. Solo después migrar la ciudad completa a BRG.
5. Usar SPIRV-Cross únicamente para inspeccionar recursos/bindings de shaders cuando exista un
   artefacto SPIR-V; las fuentes HLSL de Unity siguen siendo la fuente compilable.
