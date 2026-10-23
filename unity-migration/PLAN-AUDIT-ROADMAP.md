# Auditoría de jugabilidad y roadmap de portabilidad

**Fecha:** 2026-10-22  
**Proyecto:** Unity 6.6.0f1 / URP 17, `unity-migration`

## Resultado de la auditoría

La entrada jugable queda disponible en `Assets/Scenes/NYC_Playable.unity` y está registrada como
escena 0 en `ProjectSettings/EditorBuildSettings.asset`. La escena contiene cámara principal,
bootstrapper, generación procedural, colisiones, red vial, tráfico, peatones, controladores, HUD,
mapa y configuración URP.

La prueba de Play realizada en el Editor completó el arranque con estos resultados:

| Área | Resultado | Evidencia |
|---|---:|---|
| GeoData | ✅ | 2 691 bloques, 3 246 carreteras y 132 calles cargados |
| Geometría | ✅ | 30 955 edificios, 1 962 247 vértices de Manhattan, 90 chunks |
| Colliders | ✅ | 31 742 cajas de edificios más colliders de landmarks y puentes |
| Red vial | ✅ | 33 596 carriles construidos al entrar en Play |
| Tráfico | ✅ | 232 agentes creados; 208 activos tras el arranque |
| Cámara | ✅ | `Main Camera`, FOV 56°, conectada al `CameraManager` |
| Play | ✅ | `NycMainBootstrapper` llega a “Ready for Play!” |
| Headless | ✅ | `dotnet run --project Tools/headless-check/check.csproj` |
| Tests de origen | ✅ | `node --test tests/facade-projection.test.cjs tests/smoke-texture.test.cjs` (7/7) |

## Incidencias corregidas durante la auditoría

1. No había ninguna escena `.unity` ni escena registrada en Build Settings. Se añadió el menú
   `NYC/0. Create and Save Playable Scene`, se creó la escena y se registró automáticamente.
2. El bootstrapper intentaba crear tráfico antes de construir `RoadNetwork`. Ahora construye la red
   después de cargar GeoData, la asigna a `TrafficSystem` y crea los agentes correctamente.
3. Los shaders usaban `dFdx/dFdy` de GLSL. En HLSL/URP se cambiaron a `ddx/ddy` en
   `Assets/Shaders/Includes/NycCommon.hlsl`.
4. El callback Render Graph del bloom usaba un `TextureHandle` fuera de un pass válido en Unity 6.
   El camino incompatible quedó desactivado temporalmente; el bloom de Volume URP sigue disponible.

## Pendientes y riesgos

- El bloom custom debe reescribirse como raster pass explícito con `UseTexture` y acceso dentro de
  `SetRenderFunc`; actualmente el proyecto prioriza que Play sea estable.
- Hay avisos de API obsoleta por `FindFirstObjectByType`; conviene migrar a referencias serializadas
  o `FindAnyObjectByType` cuando se estabilice la escena.
- La verificación actual es de Editor y headless. Falta generar un build Windows/WebGL y medirlo en
  una máquina objetivo.
- La escena usa modelos procedurales (`ActorModelFactory`); todavía falta una pasada visual contra
  los modelos/animaciones del original.
- HUD, minimapa, postprocesado y shaders deben revisarse visualmente a resolución objetivo.

## Roadmap priorizado

### 1. Contrato de ejecución y regresión

- Hacer el bootstrap idempotente al reentrar en Play y al recargar la escena.
- Añadir una validación de escena que falle con un mensaje claro si falta cámara, URP, GeoData o
  `RoadNetwork`.
- Convertir la prueba de arranque en una prueba automatizada: escena, `IsInitialized`, carriles,
  agentes y ausencia de errores de consola.

**Aceptación:** abrir `NYC_Playable`, pulsar Play y obtener `Ready for Play!`, ≥30 000 edificios,
≥30 000 carriles, 232 agentes y cero errores de consola en dos arranques consecutivos.

### 2. Render Graph y compatibilidad URP

- Implementar el bloom custom con un pass de rasterización real para Unity 6.
- Revisar `AtmosphericScatteringFeature` y `NycGroundContactFeature` con el validador Render Graph.
- Fijar variantes de shader y comprobar sombras, depth texture, opaque texture y transparencias.

**Aceptación:** cero errores Render Graph, cámara renderizando correctamente y comparación capturada
en día, hora dorada y noche.

### 3. Paridad visual

- Comparar fachadas, curtain walls, asfalto mojado, pintura vial, aceras, agua, cielo y niebla con el
  original mediante capturas de cámara fija.
- Sustituir materiales aproximados por assets de producción donde la diferencia sea visible.

**Aceptación:** checklist de 11 cámaras y 4 presets horarios sin artefactos de winding, UV o z-fighting.

### 4. Geometría y landmarks

- Validar puentes, muelles, islas, Central Park y landmarks contra sus bounds fuente.
- Añadir LOD/culling por distancia a los 90 chunks y medir memoria de meshes.

**Aceptación:** bounds y conteos dentro de la tolerancia documentada; frame time medido con ciudad completa.

### 5. Gameplay

- Comparar trayectorias IDM, conexiones en cruces, semáforos, colisiones y despawn con la referencia.
- Añadir pruebas de los cuatro modos de viaje, teletransporte seguro, cámara y control táctil.
- Revisar peatones, animación procedural y límites de población en escenas largas.

**Aceptación:** cada modo se puede seleccionar, mover y teletransportar sin atravesar colliders; tráfico y
peatones siguen activos durante 60 s sin crecimiento de memoria.

### 6. HUD y mapa

- Capturar y revisar HUD, etiquetas, telemetría, radar, búsqueda y click-to-teleport.
- Ajustar escalado para 16:9, 21:9, 1080p y 1440p.

**Aceptación:** todos los elementos se enlazan sin `null`, mapa operativo y lectura legible en las cuatro
resoluciones objetivo.

### 7. Builds, rendimiento y entrega

- Crear perfiles Windows y WebGL, probar compresión, stripping y shader variants.
- Medir CPU/GPU, memoria, tiempo de carga y draw calls con ciudad completa y tráfico activo.
- Incorporar la auditoría headless y el smoke test de Play al CI antes de continuar el porte.

**Aceptación:** build reproducible, escena 0 incluida, sin errores de compilación y presupuesto de frame
definido por plataforma.

### 8. Herramientas de ingeniería inversa y transporte

- Usar `Tools/PORT-REFERENCES.md` como contrato para REA, SPIRV-Cross, BRG y NativeWebSocket.
- Instrumentar snapshots de tráfico antes de cambiar el renderer; comparar primero la lógica con
  `traffic.js` y `road-network.js`.
- Probar BRG en un lote aislado y conservar `Graphics.RenderMeshInstanced` como fallback.
- Activar `RemoteSession` solo cuando haya un servidor WebSocket explícito; Play no depende de él.

## Comandos de verificación

```powershell
dotnet run --project Tools/headless-check/check.csproj
node --test tests/facade-projection.test.cjs tests/smoke-texture.test.cjs
```

En Unity: abrir `Assets/Scenes/NYC_Playable.unity`, ejecutar `NYC/0. Create and Save Playable Scene`
solo si se quiere regenerar la escena, y pulsar Play.
