# Roadmap de paridad 1:1 con `slop-newyork`

La paridad se medirá contra el comportamiento observable del proyecto web, no contra una lista de
assets aislados. Cada fase tiene una salida verificable y bloquea la siguiente cuando falla.

## Fase 0 — contrato y referencia

**Objetivo:** fijar coordenadas, datos, cámaras, horas y semillas.

**Trabajo:** congelar `js/nyc-geodata.js`, registrar las 11 cámaras, 4 presets horarios, puntos de
teletransporte, escalas y tolerancias; conservar el marco Unity `(x, y, -z)`.

**Salida:** `Tools/parity/parity-manifest.json`, hashes de GeoData y snapshots de red/tráfico.

**Aceptación:** dos ejecuciones producen los mismos hashes con el mismo input.

## Fase 1 — estabilidad temporal y jugabilidad

**Objetivo:** que avanzar, frenar, girar y volar se perciba igual a 30, 60 y 120 Hz.

**Trabajo:** simulación fija, interpolación visual, teletransporte estable, colisiones de ciudad,
contactos de tráfico, límites de altura y cámara sin saltos.

**Completado en esta iteración:** pasos fijos para jugador, peatones y tráfico; separación de coches
que se solapan en el mismo carril; smoke test de Play sin errores.

**Aceptación:** 60 s sin NaN, sin penetración persistente entre coches, sin saltos de posición mayores
que la tolerancia y cuatro modos de viaje operativos.

## Fase 2 — paridad de simulación

**Objetivo:** que las decisiones de tráfico, peatones y controladores sean equivalentes.

**Trabajo:** comparar `traffic.js`, `road-network.js`, `controllers.js` y `pedestrians.js` con
fixtures deterministas; añadir señales, prioridad en cruces, cambios de carril, crosswalk yielding,
merge claims, despawn y spawn exactamente con las mismas semillas.

**Aceptación:** snapshots de posición/velocidad/carril dentro de tolerancia durante 60 s y mismas
decisiones de conexión en los fixtures.

## Fase 3 — paridad de actores

**Objetivo:** igualar silueta, proporciones, orientación, animación y materiales.

**Trabajo:** portar `actor-models.js` y las rutinas de peatones; separar malla, materiales y rig;
igualar taxi, sedán, SUV, bus, policía, camión, bicicleta y helicóptero; validar pivotes de ruedas,
rotor, cristales, faros, luces traseras y actitud.

**Aceptación:** comparación de silueta y pose en cámara fija; ningún actor usa una forma de prueba
cuando existe una forma equivalente en la referencia.

## Fase 4 — props, árboles y distribución

**Objetivo:** que cada prop aparezca en la misma clase de lugar, escala y orientación.

**Trabajo:** portar `trees.js`, `street-details.js`, farolas, señales, mobiliario, humo, muelles y
vegetación; usar la misma semilla y reglas de distribución; sustituir cajas genéricas por las mallas
procedurales equivalentes.

**Aceptación:** conteo por categoría, bounds y distribución espacial dentro de tolerancia; capturas de
Central Park, avenidas y barrios comparables.

## Fase 5 — materiales y render

**Objetivo:** igualar apariencia bajo los cuatro presets.

**Trabajo:** comparar TSL/WGSL con HLSL/URP; revisar fachadas, curtain walls, asfalto, aceras, agua,
cielo, niebla, ventanas, luces y sombras; validar Render Graph y variantes shader.

**Aceptación:** capturas Unity/Web en 16 combinaciones, error visual documentado y cero errores de
shader o Render Graph.

## Fase 6 — geometría, landmarks y streaming

**Objetivo:** igualar la ciudad visible y sus límites de rendimiento.

**Trabajo:** landmarks, puentes, muelles, islas, Central Park, LOD, culling por chunks, memoria,
occlusion y carga incremental.

**Aceptación:** bounds, conteos y posiciones dentro de tolerancia; frame time y memoria bajo el
presupuesto de la plataforma.

## Fase 7 — HUD, mapa y herramientas remotas

**Objetivo:** igualar interacción y diagnóstico.

**Trabajo:** HUD, minimapa, etiquetas, búsqueda, click-to-teleport, controles táctiles, telemetría
NativeWebSocket y capturas automatizadas.

**Aceptación:** recorrido completo de UI sin `null`, cuatro resoluciones, WebGL y Windows.

## Fase 8 — builds y cierre

**Objetivo:** entregar un port reproducible.

**Trabajo:** CI, build Windows/WebGL, compresión, stripping, shader warmup, pruebas de carga y
comparación final.

**Aceptación:** build reproducible, escena 0 incluida, consola limpia, métricas publicadas y ninguna
fase anterior con estado provisional.

## Estado inicial y siguiente bloque

Fases 0 y 1 están instrumentadas: escena jugable, snapshots deterministas, fixed-step, contactos de tráfico, BRG piloto y matriz de capturas Unity. La Fase 2 está parcialmente implementada con yield conservador en curb/crosswalk, consultas de ocupación de peatones, cambios de carril anticipados con cache de adyacencias paralelas y prioridad de giro con cesión frente a tráfico contrario. Se añadió reserva determinista de la conexión de salida por tick (`merge claims`) para evitar entradas simultáneas. La validación actual produce snapshots deterministas (`net DE01D860F5065F00`, `traffic C883092F46DAFE60`), 33.601 carriles, 149.765 conexiones, 232 agentes activos y 0 NaN. El spawn/despawn ya usa streams por vehículo (`SpawnId` + `RespawnCount`) y no depende del orden global de actualización. Se añadió `Tools/parity/traffic-fixtures.json` y su validación headless: 6 casos de ocupación de carril, alturas, helicóptero, borde de lane y merge claim con seed 10019. Se añadió `PreviewNextLane` y la validación de 99.474 casos lane/roll deterministas sobre las 33.601 lanes. El siguiente bloque concreto es comparar esos snapshots con decisiones exportadas de `js/traffic.js`; Además se añadió interpolación visual entre estados fijos en `TravelController`, eliminando el escalonado de peatones y vehículos cuando el render no coincide con 60 Hz. La fase visual ya incluye interpolación del jugador y de la multitud instanciada, con corrección del wrap perimetral de peatones. La siguiente mejora de actores añadió la librea MTA del bus (carrocería blanca, banda azul y cinta cian) como partes de mesh y shader, sin aumentar draw calls. El próximo bloque es aplicar la misma comparación a taxi/NYPD/helicóptero y validar capturas por ubicación.