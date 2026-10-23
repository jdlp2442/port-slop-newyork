# Prompt de continuidad para el siguiente agente

Actúa como Ingeniero Principal de Unity 6/URP y continúa el port de `mrdoob/slop-newyork` en
`D:/escena/unity-migration`. El objetivo es alcanzar paridad funcional, visual y de rendimiento con
la versión Three.js/WebGPU, manteniendo siempre una escena jugable.

## Estado que debes aceptar como base

- Escena jugable: `Assets/Scenes/NYC_Playable.unity`, registrada como Build Settings escena 0.
- Play verificado: GeoData 2.691 bloques/3.246 carreteras; 30.955 edificios; 33.596 carriles;
  208 vehículos activos; cámara principal; HUD/mapa y ciclo horario inicializados.
- Headless check obligatorio: `dotnet run --project Tools/headless-check/check.csproj`.
- Tests de referencia obligatorios desde `D:/escena/slop-newyork-main`:
  `node --test tests/facade-projection.test.cjs tests/smoke-texture.test.cjs`.
- NativeWebSocket está declarado en `Packages/manifest.json` y `RemoteSession` está en la escena.
  La conexión está desactivada por defecto. El arnés headless compila el stub automáticamente.
- La ruta Render Graph custom del bloom está temporalmente desactivada en Unity 6; el Volume URP
  sigue activo. No reactivarla sin un raster pass válido.

## Orden de trabajo obligatorio

1. Ejecuta primero los dos comandos de verificación y comprueba el estado de Unity. Si fallan,
   corrige la regresión antes de añadir funcionalidades.
2. Implementa un **piloto BRG aislado para un único lote de vehículos**. No sustituyas todavía el
   renderer actual: añade backend seleccionable y fallback. Usa `BatchRendererGroup`, buffers raw,
   `MetadataValue`, DOTS instancing y culling explícito. Valida matrices, bounds, sombras y material
   antes de medir rendimiento.
3. Instrumenta `RoadNetwork` y `TrafficSystem` para exportar snapshots deterministas de carril,
   conexión elegida, semáforo, posición y velocidad. Compara fixtures pequeños contra
   `slop-newyork-main/js/traffic.js` y `js/road-network.js`.
4. Usa REA solo sobre software propio o autorizado y formula preguntas concretas con evidencia.
   No aceptes pseudocódigo como verdad sin contrastarlo con fuente, tests y ejecución.
5. Usa SPIRV-Cross únicamente para inspeccionar recursos/bindings/clip-space de un artefacto SPIR-V;
   las fuentes HLSL y la compilación de Unity son la autoridad final.
6. Añade pruebas visuales de cámara fija para 5th/42nd, Central Park, Battery Park y un puente en
   mediodía, golden hour, twilight y noche.
7. Solo después de validar tráfico y render, aborda peatones BRG, LOD/culling, HUD/mapa, builds
   Windows/WebGL y presupuesto de frame.

## Reglas de calidad

- No borres datos ni cambies el marco de coordenadas `(x, y, -z)`.
- No introduzcas un paquete o API sin comprobar que Unity 6/URP 17 compila.
- Cada cambio debe incluir prueba headless o Play reproducible y una nota de evidencia.
- Si una optimización visual rompe Play, conserva el backend anterior como fallback.
- No declares una fase completa solo porque compile: exige comportamiento observado, métricas y
  ausencia de errores de consola.

## Entrega esperada

Devuelve: archivos modificados, evidencia de pruebas, métricas antes/después, problemas abiertos y
el siguiente paso concreto. Deja `NYC_Playable` abierta y fuera de Play al terminar.
