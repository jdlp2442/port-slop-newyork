# FASE 5 — Tráfico y multitudes de alto rendimiento

Puerto del núcleo de `js/traffic.js` (2 337 líneas) y de la arquitectura de `js/pedestrians.js`.
Objetivo: cientos de vehículos y peatones animados con **una llamada de dibujo por lote** y el
coste de simulación por debajo de 0,2 ms por frame.

---

## 5.0 Resultado medido

```
lanes 33,601, connections 149,765, roads 3,088 in 145 ms
traffic spawn: 197 agents in 3 ms
traffic 30 s sim: 215 ms total          → 0.12 ms por frame con 232 agentes
traffic stats: active 232, nan 0, stopped 36, avg 9.47 m/s, max 13.40 m/s
pedestrians: 295 walkers over 21 blocks in 2 ms
```

* 232 vehículos (200 coches + 32 buses) simulados a **0,12 ms/frame**.
* 295 peatones animados en **1 draw call** (`DrawMeshInstancedIndirect`).
* 4 draw calls de vehículos (`Graphics.RenderMeshInstanced`).
* 0 NaN, 36 detenidos (semáforos y colas), velocidad media 9,47 m/s (34 km/h).

---

## 5.1 Red de carriles — `RoadNetwork.cs`

El original construye su red desde la geometría ya generada (`road-network.js`). Aquí se parte de
las 3 246 polilíneas de `GEO.roads` y se **cortan en cada cruce** con otra calle:

1. Hash espacial de todos los segmentos (celdas de 48 m).
2. Para cada calle se muestrea cada 4 m; si un punto está a menos de
   `anchoA/2 + anchoB/2 + 2 m` de otra calle **con dirección distinta** (> 23°), se marca un corte.
3. La polilínea se parte en los cortes (dedupe 10 m).
4. Cada trozo genera una lane por carril y sentido:
   * `perDir = lanes` si es de un sentido, `lanes/2` si es de doble sentido;
   * offset lateral `(i + 0.5 − perDir/2) · laneWidth`;
   * velocidad: highway 22,35 m/s · avenida 13,4 · calle 8,94.
5. Conexiones: para cada lane, las que empiezan a menos de 12 m de su final y con ángulo
   `dot > −0.25` (nada de giros de 180°).

Resultado: **33 601 lanes, 149 765 conexiones, 145 ms** (una sola vez).

```csharp
public void Sample(int laneId, float s, out Vector2 pos, out Vector2 dir);
public float Project(int laneId, Vector2 p);   // distancia a lo largo de la lane
```

---

## 5.2 IDM — `TrafficSystem.cs`

Constantes exactas de `traffic.js:46-47`:

| | a | b | T | s0 |
|---|---|---|---|---|
| Coche | 2,0 | 3,0 | 1,2 | 2,0 |
| Bus | 1,1 | 2,5 | 1,5 | 2,5 |

```csharp
float freeAcc = a * (1 - pow(v / v0, 4));
float desired = s0 + max(0, v*T + v*(v - leaderSpeed) / (2*sqrt(a*b)));
float brake   = -a * pow(desired / max(gap, 0.1), 2);
float acc     = clamp(freeAcc + worstBrake, -9, a);
```

Cada agente evalúa, en este orden:

1. **Líder en su carril** (lista enlazada por carril, reconstruida cada frame).
2. **Líder en el carril siguiente** si está a menos de 70 m del final (para no empotrarse al girar).
3. **Semáforo**: `CityConfig.SignalPhase(t, isAvenue)` — verde pasa; ámbar pasa solo si no puede
   frenar cómodamente (`freeAcc + stopAt > -b`); rojo frena a la línea de parada.
4. **El jugador**: se proyecta su posición sobre el carril; si está delante y a menos de
   2,4 m + medio ancho lateral, se trata como líder (los coches te frenan).

Al llegar al final del carril se transfiere al siguiente (`S -= length`) eligiendo entre las
conexiones con peso 3 para recto y 1 para giro.

**Población**: 200 coches + 32 buses. Aparecen a 100–300 m **fuera del cono de visión**
(`dot(forward, dir) < 0.45`), se reciclan a más de 1 200 m y los buses solo circulan por avenidas.

---

## 5.3 Render de vehículos — `TrafficRenderer.cs` + `TrafficJobs.cs`

4 lotes: taxi (amarillo), sedán (plata), SUV (gris oscuro), bus (azul claro).

```csharp
var job = new BuildVehicleMatricesJob { Instances = _instances[b], Matrices = _matrices[b] };
job.Schedule(count, 64).Complete();
Graphics.RenderMeshInstanced(_params[b], _meshes[b], 0, _matrices[b], count);
```

```csharp
[BurstCompile]
public struct BuildVehicleMatricesJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<VehicleInstance> Instances;
    public NativeArray<Matrix4x4> Matrices;

    public void Execute(int index)
    {
        var i = Instances[index];
        Matrices[index] = Matrix4x4.TRS(i.Position, i.Rotation, new Vector3(i.Scale, i.Scale, i.Scale));
    }
}
```

> **Por qué `IJobParallelFor` y no `IJobParallelForTransform`:** no hay GameObjects que
> transformar; las matrices van directas al renderer instanciado. `IJobParallelForTransform`
> obligaría a mantener 232 Transform más un `TransformAccessArray`, que es justo lo que evitamos.

Los coches se generan por código (`VehicleMeshFactory`): cuerpo, cabina, 4 ruedas, faros y pilotos,
con `uv1.x` como id de pieza (0 carrocería, 1 cristal, 2 faro, 3 piloto, 4 neumático).
`Nyc/VehiclePaint` colorea por pieza y hace **emisivos los faros y pilotos** según
`_NycInteriorGlow` (de día apagados, de noche encendidos).

**Faros reales**: un `Light` por coche es inviable. El original usa luces agrupadas con un pool.
Aquí la emisión va en la geometría y, si se quieren haces, hay que añadir un pool de 8–12
`SpotLight` para los vehículos más cercanos (ver §5.8).

---

## 5.4 Peatones — `PedestrianCrowd.cs` + `Nyc/Pedestrian`

Un único `DrawMeshInstancedIndirect` para toda la multitud.

* **Dónde andan**: por el perímetro real de cada *curb island* (`GEO.blocks[].poly`), desplazados
  1,9 m hacia el centro (la acera). Es la forma más simple de tener gente caminando por aceras
  reales sin navmesh.
* **Cuántos**: `clamp(perímetro / 30, 2, 18)` por bloque, solo bloques a menos de 320 m del
  jugador → ~295 peatones en Midtown.
* **Cuándo se recalcula**: cuando la cámara se mueve 45 m (2 ms).
* **Animación**: cada vértice lleva `uv1 = (pieza, 0..1 desde el pivote)`. El vertex shader
  balancea brazos y piernas con `sin(fase + desfase)`; la fase avanza con la velocidad.

```hlsl
struct PedInstance { float4 posHeading; float4 params; float4 tint; };  // 48 B
StructuredBuffer<PedInstance> _Instances;

float pivotY = part > 3.5 ? 0.88 : (part > 1.5 ? 1.44 : 0.0);
float swing  = sin(phase + offset) * amplitude * saturate(speed / 1.4) * t;
```

```csharp
_instanceBuffer.SetData(_data, 0, 0, walkerCount);
material.SetBuffer("_Instances", _instanceBuffer);
Graphics.DrawMeshInstancedIndirect(_mesh, 0, material, _bounds, _argsBuffer);
```

Iluminación: lambert + `SampleSH` (sin PBR completo) — a distancia de calle es indistinguible y
cuesta una fracción.

---

## 5.5 Montaje en escena

1. `NYC → 8. Build Traffic Network` (crea `RoadNetwork`, `Traffic`, `Pedestrians` y los conecta).
2. Comprobar en el inspector: `Traffic.activeCount ≈ 232`, `PedestrianCrowd.walkerCount ≈ 300`.
3. Asignar materiales:
   * `TrafficRenderer.taxiMaterial/sedanMaterial/suvMaterial/busMaterial` → `Nyc/VehiclePaint`
     con `_BaseColor` distinto (amarillo taxi `#FFBE14`, plata, gris, azul MTA).
   * `PedestrianCrowd.material` → `Nyc/Pedestrian`.
4. El shader de peatones necesita **Shader Model 4.5** (StructuredBuffer); el de vehículos
   funciona con 3.5.

---

## 5.6 Verificación

```
lanes 33,601, connections 149,765, roads 3,088 in 145 ms
traffic spawn: 197 agents in 3 ms
traffic 30 s sim: 215 ms total
traffic stats: active 232, nan 0, stopped 36, avg 9.47 m/s, max 13.40 m/s
pedestrians: 295 walkers over 21 blocks in 2 ms
```

Comprobaciones:

* `max 13.40 m/s` = límite de avenida → ningún coche se dispara.
* `avg 9.47` con 36 parados → el IDM frena y arranca.
* `nan 0` → la transferencia entre carriles no rompe la posición.
* 232 activos tras 30 s → el reciclado mantiene la población.

---

## 5.7 Simplificaciones (y cómo completarlas)

| Falta | Por qué | Cómo añadirlo |
|---|---|---|
| Cambio de carril | requiere las listas de ocupación por carril de `traffic.js` | añadir `LaneChange` con `MOBIL` (gap + aceleración) sobre `_laneHead` |
| Ceder a peatones en pasos de cebra | necesita el grafo de aceras y las colas de `curb` | emitir `Crosswalk` en los nodos y frenar con `stopAt` si hay un walker dentro |
| Merges / claims en intersección | el original usa `claim(v)` y `precedence()` | añadir un `IntersectionClaim` por nodo con prioridad por orden de llegada |
| Faros volumétricos | pool de luces agrupadas | 12 `SpotLight` reasignados a los coches más cercanos + `DecalProjector` para el charco de luz |
| Peatones con esqueleto real | falta el rig de `pedestrians.js` | hornear una **VAT** del rig y sustituir el swing del vertex shader por muestreo de la textura |
| Cruces de peatones | idem | waypoints en los extremos de cada bloque + espera en el bordillo |
| Bus con paradas | no hay datos de paradas | puntos cada ~250 m en avenidas, `stopAt` + temporizador |

---

## 5.8 Presupuesto

| Sistema | Coste |
|---|---|
| Red de carriles (una vez) | 145 ms |
| Simulación 232 agentes | 0,12 ms/frame |
| Matrices (Burst) | ~0,02 ms/frame |
| Draw calls vehículos | 4 |
| Draw call peatones | 1 |
| Rebuild de multitud | 2 ms cada 45 m |

Con esto el frame queda dominado por la geometría de la ciudad, no por el tráfico.
