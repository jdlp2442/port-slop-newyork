# FASE 4 — Controladores, física, cámara y teletransporte

Puerto de `js/controllers.js` (1 175 líneas) y `js/cameras.js` (352 líneas).
Todo el código en `Assets/Scripts/Gameplay/`.

---

## 4.0 Idea clave: la simulación vive en el marco fuente

`TravelController` guarda:

```csharp
public Vector2 Position;   // (x, z) del marco fuente: x = este de la retícula, z = sur
public float Height;       // Y de Unity
public float Heading;      // radianes fuente: 0 = +Z (sur), PI = norte
public float Speed;        // m/s
```

Solo al final del frame se cruza a Unity:

```csharp
transform.SetPositionAndRotation(
    CityFrame.ToUnity(Position.x, Height, Position.y),
    CityFrame.HeadingToRotation(Heading));   // Euler(0, -heading°, 0)
```

Así `Mathf.Sin(Heading)` / `Mathf.Cos(Heading)` significan exactamente lo mismo que en JS y la
física se puede comparar línea a línea con el original.

---

## 4.1 Peatón — `PedestrianController.cs`

| Parámetro | Valor | Origen |
|---|---|---|
| Velocidad andando | 4.4 m/s | `controllers.js:975` |
| Sprint (boost) | 8.2 m/s | idem |
| Marcha atrás | 0.65 × | idem |
| Giro | 2.6 rad/s | idem |
| Aceleración | `speed += (target − speed)·min(1, 10·dt)` | idem |
| Salto | 5.2 m/s | `:993` |
| Gravedad | 14.5 m/s² | `:995` |
| Radio | 0.48 m | `:989` |

```csharp
Speed += (targetSpeed - Speed) * Mathf.Min(1f, dt * 10f);
Position.x += Mathf.Sin(Heading) * Speed * dt;
Position.y += Mathf.Cos(Heading) * Speed * dt;
ctx.ClampToIsland(ref Position, false, ref Speed);
ctx.Collision.Resolve(ref Position, radius, radius, Height + 1f, ctx.Normals);

float groundY = ctx.Ground(Position.x, Position.y);
if (input.Up > 0.5f && Height <= groundY + 0.02f) VerticalVelocity = jumpVelocity;
VerticalVelocity -= gravity * dt;
Height += VerticalVelocity * dt;
```

El suelo depende del modo: dentro de Central Park devuelve `centralParkGroundY`; para el peatón,
acera 0.22 m si `CitySpatialIndex.IsOnSidewalk`, si no 0.

---

## 4.2 Taxi — `CarController.cs`

| Parámetro | Valor |
|---|---|
| Velocidad máx. | 23.5 m/s (36 con boost) |
| Aceleración | 14 m/s² (21 con boost) |
| Marcha atrás | 0.45 × máx |
| Coast (sin gas) | `speed *= max(0, 1 − 2.8·dt)` |
| Ángulo de dirección | 0.52 rad, respuesta 10/s |
| Giro | `steerAngle · 2.4 · min(1, |v|/8) · sign(v) · dt` |
| Grip lateral | `exp(−6.8·dt)` |
| Yaw por choque | decae `exp(−7.8·dt)` |
| Huella | 0.86 × 2.18 m (semiejes) |

Suspensión de 3 grados de libertad (muelle-amortiguador):

```csharp
float targetPitch = -Throttle * 0.026f;
float targetRoll  = -SteerAngle * (Speed / topSpeed) * 0.092f;

PitchVelocity += ((targetPitch - ChassisPitch) * 68f - PitchVelocity * 11.2f) * dt;
RollVelocity  += ((targetRoll  - ChassisRoll ) * 68f - RollVelocity  * 11.2f) * dt;

ChassisPitch = Mathf.Clamp(ChassisPitch + PitchVelocity * dt, -0.22f, 0.22f);
ChassisRoll  = Mathf.Clamp(ChassisRoll  + RollVelocity  * dt, -0.26f, 0.26f);

HeaveVelocity += (-HeaveY * 85f - HeaveVelocity * 12f) * dt;
HeaveY = Mathf.Clamp(HeaveY + HeaveVelocity * dt, -0.08f, 0.14f);
```

Choque (por cada contacto que devuelve `CollisionWorld.Resolve`):

```csharp
float vn = -(totalVx * n.x + totalVz * n.y);
if (vn <= 0.5f) return;

float headOn  = Mathf.Abs(sinH * n.x + cosH * n.y);
float sideDot = cosH * n.x - sinH * n.y;

Vx += n.x * vn * 0.42f;
Vz += n.y * vn * 0.42f;

if (headOn > 0.72f) Speed = -Speed * 0.25f;               // de frente: rebote
else { Speed *= 1f - 0.62f * headOn;                       // roce: pierde velocidad
       AngularVelocity += sideDot * sign * Mathf.Min(3.2f, vn * 0.22f); }

PitchVelocity += along * Mathf.Min(14f, vn) * 0.16f;
RollVelocity  -= sideDot * Mathf.Min(14f, vn) * 0.22f;
HeaveVelocity += Mathf.Min(1.4f, vn * 0.11f);

if (vn > 2.2f) CollisionSpark?.Invoke(point, n, vn);       // chispas (Fase 5)
```

La huella se proyecta según el heading, igual que el original:

```csharp
float extX = |cosH| * 0.86f + |sinH| * 2.18f;
float extZ = |sinH| * 0.86f + |cosH| * 2.18f;
```

---

## 4.3 Bicicleta — `BikeController.cs`

| Parámetro | Valor |
|---|---|
| Velocidad | 12.8 m/s (19 boost) |
| Aceleración | 9 m/s² (13.5 boost) |
| Marcha atrás | 0.6 ×, tope −4.5 m/s |
| Giro | 2.15 rad/s |
| Inclinación | 0.34 rad, respuesta 9/s |
| Rueda | r = 0.35 m |
| Transmisión | 3.2 : 1 (pedalier : rueda) |

```csharp
float targetLean = -turnInput * Mathf.Min(1f, Mathf.Abs(Speed) / 9f) * 0.34f;
LeanAngle += (targetLean - LeanAngle) * Mathf.Min(1f, dt * 9f);

float wheelDelta = (Speed / 0.35f) * dt;
if (throttle > 0f && wheelDelta > 0f) PedalPhase += wheelDelta / 3.2f;
```

`LeanAngle`, `WheelSpin` y `PedalPhase` están pensados para que el prefab los lea y anime el
modelo (leanPivot, ruedas, bielas).

---

## 4.4 Helicóptero — `HelicopterController.cs`

| Parámetro | Valor |
|---|---|
| Velocidad horizontal | 32 m/s (52 boost) |
| Velocidad vertical | 16 m/s |
| Aceleración | 24 m/s² horiz., 22 m/s² vert. |
| Amortiguación | 1.8/s horiz., 3.5/s vert. |
| Guiñada | 1.45 rad/s |
| Cabeceo / alabeo | 0.24 / 0.28 rad, respuesta 5/s |
| Techo | 380 m, suelo 0.25 m |
| Radio de colisión | 4.5 m |
| Balanceo | `sin(t·2.6)·0.18` |

```csharp
Speed += fwdInput * 24f * dt;                 // clamp a ±(max·0.6, max)
VerticalSpeed += vertInput * 22f * dt;        // clamp ±16
Heading += yawInput * 1.45f * dt;

Position.x += Mathf.Sin(Heading) * Speed * dt;
Position.y += Mathf.Cos(Heading) * Speed * dt;
Height = Mathf.Clamp(Height + VerticalSpeed * dt, 0.25f, 380f);

PitchAngle += ((Speed / maxHoriz) * 0.24f - PitchAngle) * Mathf.Min(1f, dt * 5f);
RollAngle  += (-yawInput * 0.28f - RollAngle) * Mathf.Min(1f, dt * 5f);
Bob = Mathf.Sin(ctx.Elapsed * 2.6f) * 0.18f;
```

El helicóptero es el único que puede salir del island bbox (±6 km) para sobrevolar el puerto.

---

## 4.5 Colisiones — `CollisionWorld.cs`

* Rejilla uniforme de **48 m** (`Cell`), igual que `nearColliders` del original.
* `Build(IReadOnlyList<CollisionBox>)` una sola vez tras el bake (31 258 cajas).
* `Resolve(ref Vector2 pos, extX, extZ, yHeight, List<Vector2> normals)`:
  * descarta cajas cuyo `MaxY + 1.5 < yHeight` (puedes pasar por encima);
  * AABB: expulsa por el eje de **mínima penetración**;
  * OBB (bloques con marco): transforma al espacio local, expulsa y devuelve la normal rotada;
  * devuelve **todas** las normales de contacto para que el coche aplique varios impulsos.
* `Overlaps(...)` para el teletransporte (test de huella completa).

`ManhattanMeshBuilder` emite los colliders en `EmitBox` para todo lo que supere 2 m:

```csharp
if (f.IsIdentity) CollisionBoxes.Add(new CollisionBox { MinX=…, MaxX=…, MinZ=…, MaxZ=…, MaxY=y1 });
else              CollisionBoxes.Add(new CollisionBox { …, HasObb = true, Cx, Cz, Hx, Hz, Cs, Sn });
```

---

## 4.6 Teletransporte seguro — `TeleportService.cs`

Puerto de `findTeleportPosition` (`controllers.js:231-303`). No basta con probar el centro: se
prueba **toda la huella** y se rechaza si:

1. la huella se solapa con un edificio (`CollisionWorld.Overlaps`), o
2. el modelo de otro jugador cae en la llegada **o en el corredor de la cámara de persecución**.

Si el punto pedido no vale, busca en anillos de 4 m hasta 256 m, 32 direcciones por anillo.
Devuelve `false` si no hay sitio (el jugador se queda donde estaba, como el original).

Los 8 destinos están en `CityConfig.Teleports` con los valores exactos de `TELEPORT_LOCATIONS`:
Empire State, Times Square, Chrysler, Grand Central, Bryant Park, Flatiron, Central Park, One WTC.

```csharp
manager.TeleportTo("owtc");                       // por id
manager.TeleportActiveTo(x, z, headingRad);       // por coordenadas
```

---

## 4.7 Cámara — `CameraManager.cs`

11 presets con los mismos FOV y categorías:

| id | Nombre | Follow | FOV |
|---|---|---|---|
| `chase` | 3rd-Person Chase | ✅ | 56 |
| `action` | Low-Angle Action | ✅ | 60 |
| `pov` | First-Person / Cockpit | ✅ | 68 |
| `centralPark` | Central Park Oasis | — | 52 |
| `fifthAve` | 5th Ave & Empire State | — | 48 |
| `timesSquare` | Times Square Crossroads | — | 58 |
| `chryslerGCT` | 42nd St · Chrysler & GCT | — | 50 |
| `observatory` | Empire State 86th Deck | — | 52 |
| `bryantPark` | Bryant Park & NYPL | — | 52 |
| `waterfront` | East River & Skyline | — | 46 |
| `drone` | Auto-Director Aerial | — | 50 |

### Persecución (chase)

```csharp
float dist   = entity.ChaseDistance * zoom * (1 + min(0.25, smoothedSpeed * 0.01));
float height = entity.ChaseHeight * zoom + sin(orbitPitch) * dist * 0.85;
float horiz  = cos(orbitPitch) * dist + speedLag;     // speedLag = smoothedSpeed * 0.1

desiredPos  = entity - (sin(totalYaw), cos(totalYaw)) * horiz;
desiredPos.y = max(0.65, entity.y + height);
desiredLook = entity + forward * 5.2 + (targetOffset.y + lookUpBias);
```

`lookUpBias`: coche 0.52, peatón 1.55, helicóptero 0.20.

### Suavizado en el marco de la entidad

Es lo que evita que la cámara "tire" hacia atrás al acelerar. Se guardan offsets **relativos a la
entidad** y se interpolan con `k = 1 − exp(−dt·8.5)`; la altura se interpola en espacio mundo
(para aceras, saltos y ascensos del helicóptero):

```csharp
_followOffset     = Lerp(_followOffset,     desiredPos  - entityPos, k);
_followLookOffset = Lerp(_followLookOffset, desiredLook - entityPos, k);
_currentPos  = new Vector3(entityPos.x, _followBaseY, entityPos.z) + _followOffset;
_currentLook = new Vector3(entityPos.x, _followBaseY, entityPos.z) + _followLookOffset;
```

### Órbita y zoom

* Arrastrar (ratón o un dedo): `yaw −= dx·0.0065`, `pitch = clamp(pitch + dy·0.005, −0.32, 0.68)`.
* Rueda: `zoom = clamp(zoom + scroll·0.001, 0.45, 2.4)`.
* Si sueltas y la entidad se mueve, el yaw vuelve a 0 con `1 − exp(−dt·2.2)` tras 4 s.

---

## 4.8 Input — `PlayerInputReader.cs`

| Acción | Teclas |
|---|---|
| Avanzar | W / ↑ |
| Retroceder | S / ↓ |
| Izquierda | A / ← |
| Derecha | D / → |
| Subir / saltar | Espacio / E |
| Bajar | Q / C |
| Boost | Shift izq./der. |

Teclado y touch se combinan con `Mathf.Max` (el touch puede ser analógico 0..1), igual que
`keys[action] = keyboard || touch`. `SetTouch("forward", 0.6f)` alimenta el joystick de la Fase 6.

Compila con y sin Input System (`#if ENABLE_INPUT_SYSTEM`).

---

## 4.9 Montaje en escena

```
Systems (GameObject)
├── LightingSystem
├── PlayerInputReader
├── CollisionWorld            ← NYC/7 lo crea y rellena
├── TeleportService           (collision + manager)
├── ThirdPersonControllerManager
│   ├── Person      (PedestrianController + prefab visual)
│   ├── Car         (CarController + taxi)
│   ├── Bike        (BikeController + bici + ciclista)
│   └── Helicopter  (HelicopterController + heli)
└── CameraRig
    └── Main Camera (CameraManager → cam, controllers)
```

1. `ManhattanMeshBuilder` en la escena, ejecutar `NYC/2` y `NYC/7`.
2. Crear los 4 prefabs (o placeholders: cubo 1×1×1 con el script). **Mirando a −Z.**
3. Asignar en el manager `person/car/bike/helicopter`, `collision`, `input`, `teleport`.
4. Asignar en `CameraManager` la cámara y el manager.
5. Play: WASD conduce, 1–4 cambia de modo (llamar `SetMode` desde el HUD de la Fase 6),
   arrastrar orbita, rueda hace zoom.

Los puntos de spawn son los del original: 5th Ave & 37th St (peatón −11.6, coche 3.6, bici 5.2,
helicóptero −110 a 96 m).

---

## 4.10 Verificación

```
Compilación correcta. 0 advertencias, 0 errores      (24 scripts)
collision: 31,258 boxes
collider sanity (inside a building): True
car 10s: pos (3.6,626.1) speed 23.50 inside=False
ped 5s: pos (-11.6,432.2) speed 4.40 y 0.00
heli 5s: pos (-110.0,477.5) alt 170.3 speed 32.00
teleport to 5th/42: True -> (0.0,0.0)
```

Interpretación:

* El coche acelera a 23.5 m/s y recorre ~214 m en 10 s sin quedarse dentro de ningún edificio.
* El peatón anda 4.4 m/s y se queda a y = 0 (calzada, porque el spawn está en la 5th Ave).
* El helicóptero sube de 96 a 170 m y avanza a 32 m/s.
* El teletransporte a 5th/42 encuentra (0,0) libre.

---

## 4.11 Desviaciones conscientes

| Tema | Original | Aquí |
|---|---|---|
| Peatón inactivo | pasea solo por la 5th Ave | quieto por defecto (`idlePedestrianWalks = false`) |
| Bryant Park | altura de césped desde `window.NYC.bryantParkLawn` | `bryantParkLawn` serializado en `CameraManager`; el suelo usa acera |
| Animación | rig con IK de pies (`pedestrians.js`) | pendiente Fase 5 (VAT o compute skinning) |
| Chispas de choque | partículas en `lastCollisionSpark` | evento `CollisionSpark` listo para conectar |
| Cinemachine | — | cámara directa (el suavizado relativo no se reproduce con un transposer) |
