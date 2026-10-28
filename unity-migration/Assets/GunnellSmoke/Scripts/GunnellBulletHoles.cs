using System.Collections.Generic;
using UnityEngine;

namespace GunnellSmoke
{
    /// <summary>
    /// Agujeros de bala del humo de Gunnell.
    ///
    /// Cada disparo conserva su semilla, orientación y perfil durante toda su
    /// vida. El disparo inyecta una sola vez un canal despejado e impulso en
    /// GunnellSmokeWake; su densidad vuelve mediante transporte del gas.
    ///
    /// Es estático y no un MonoBehaviour a propósito: la feature es un
    /// ScriptableObject y no tiene Update, pero sí un pase por frame, que es donde
    /// se hace el Tick. Así no hace falta añadir ningún componente a la escena.
    /// </summary>
    public static class GunnellBulletHoles
    {
        public const int MaxHoles = 256;

        /// <summary>
        /// Radio del cono en el extremo cercano (junto al origen del disparo).
        /// Radio visual de la estela despejada, mayor que el calibre físico.
        /// </summary>
        public static float entryRadius = 0.342f;

        /// <summary>Radio en el extremo lejano: el túnel se estrecha con la distancia.</summary>
        public static float exitRadius = 0.27f;

        /// <summary>
        /// Alcance por defecto para inyecciones directas y capturas. Los
        /// disparos del rifle usan el segmento resuelto por el arma.
        /// </summary>
        public static float depth = 15f;

        /// <summary>
        /// Duración del registro del disparo. 0.36 lo conserva ~2.8 s; cada
        /// disparo recibe una variación pequeña y estable de duración.
        /// El cierre lo determina la densidad transportada; no depende de esta vida.
        /// </summary>
        public static float lifetimeSpeed = 0.36f;

        // LAYOUT: 8 bloques `Vector4` = 128 B, sin `Vector3`/`Vector2` sueltos.
        //
        // POR QUÉ TODO EN `Vector4` (bug real, 2026-10-01): HLSL empaqueta un
        // `float4` con alineación 16, así que mezclar `float3`/`float2` mete
        // relleno y los DOS lados dejan de medir lo mismo. Con el struct anterior
        // de campos sueltos, C# calculaba 104 B para el array gestionado
        // (`UnsafeUtility.SizeOf<GpuHole>()`) mientras el buffer se creaba con 96
        // -> `SetData` lanzaba cada frame:
        //     ArgumentException: One of C# data stride (104) and Buffer stride (96)
        //     should be multiple of other.
        // Ese throw, lanzado desde `beginContextRendering`, abortaba el render ->
        // frame NEGRO con cualquier agujero vivo. Con SOLO `Vector4` ambos lados
        // miden 128 B exactos y el stride coincide por construcción.
        //
        // POR QUÉ SE ENVÍA TODO ESTO EN VEZ DE CALCULARLO EN EL SHADER: el bucle
        // de agujeros corre POR MUESTRA de raymarch (cientos de miles de veces
        // por frame). Cada dato precalculado al disparar sale del bucle más
        // caliente del shader:
        //   - `axisA`/`axisB`: base ortonormal (antes `cross`+`normalize` por muestra).
        //   - `bound`: radio transversal conservador para el filtro espacial.
        //   - `shape`: semilla, escala y aspecto -> VARIEDAD entre disparos.
        private struct GpuHole
        {
            // Los `.w` libres se usan para la CARACTERIZACIÓN de cada agujero:
            // no son relleno, son lo que hace que dos disparos no se lean igual.
            public Vector4 originFwd; // xyz = origen,       w = seedB (2ª semilla de ruido)
            public Vector4 forward;   // xyz = dirección,    w = flare (perfil de ensanchado)
            public Vector4 axisA;     // xyz = base ortonormal, w = dirección del mechón, x
            public Vector4 axisB;     // xyz = segunda base, w = dirección del mechón, y
            public Vector4 radius;    // x = entry, y = exit, z = abierto, w = notch (muescas)
            public Vector4 shape;     // x = semilla, y = escala, z = aspecto, w = tatter
            public Vector4 bound;     // xyz = centro del eje, w = radio transversal
            public Vector4 phase;     // x = longitud hasta el obstáculo, y = reservado, z = edad, w = reservado
        }

        private struct Hole
        {
            public bool active;
            public float t;
            public float speed;
            public Vector3 origin;
            public Vector3 forward;
            // --- CARÁCTER del agujero (sorteado al disparar, fijo en su vida) ---
            // Semillas estables para pequeñas variaciones del gas. El ruido
            // principal pertenece al propio humo.
            public float shapeSeed;  // fase del remolino
            public float seedB;      // orientación y variación de tamaño
            public float shapeScale; // escala del remolino
            public float aspect;     // elipse: de ovalada a redonda
            public float tatter;     // amplitud de la erosión suave
            public float notch;      // amplitud de la variación del remolino
            public float flare;      // cambio de anchura a lo largo del eje
            public Vector3 axisA;
            public Vector3 axisB;
            public bool pendingInjection;
            public float length;
        }

        private static readonly Hole[] holes = new Hole[MaxHoles];
        private static readonly GpuHole[] gpu = new GpuHole[MaxHoles];
        private static readonly GpuHole[] injectionGpu = new GpuHole[MaxHoles];
        private static ComputeBuffer buffer;
        private static ComputeBuffer injectionBuffer;
        public static ComputeBuffer InjectionBuffer => injectionBuffer;
        public static int InjectionCount { get; private set; }
        private static int count;
        private static int cursor;

        public static ComputeBuffer Buffer => buffer;
        public static int Count => count;

        /// <summary>Diagnóstico: disparos registrados desde que arrancó la sesión.</summary>
        public static int AddedTotal { get; private set; }

        /// <summary>Diagnóstico: veces que ha corrido Tick.</summary>
        public static int TickTotal { get; private set; }

        /// <summary>
        /// Stride del struct GPU en bytes: 8 bloques <c>Vector4</c> = 128 B.
        /// Con SOLO <c>Vector4</c> el layout no depende del empaquetado de HLSL ni
        /// del de C#: ambos miden 128 exactos. Es la invariante que hace que
        /// <c>SetData</c> no lance la excepción de stride (104 vs 96 del struct
        /// anterior con <c>Vector3</c>/<c>Vector2</c> sueltos).
        /// </summary>
        public const int GpuStride = 128;

        /// <summary>Stride con el que se CREA el ComputeBuffer. Debe ser 128.</summary>
        private const int GpuBufferStride = 128;

        /// <summary>
        /// Diagnóstico: volcado legible de los agujeros que están AHORA en el
        /// buffer GPU, tal como los ve el shader. Sirve para verificar el layout
        /// (stride, ejes, esfera envolvente) sin adivinar.
        /// </summary>
        public static string DumpGpu()
        {
            if (count <= 0) return "sin agujeros activos";
            var sb = new System.Text.StringBuilder();
            sb.Append("stride=").Append(GpuStride).Append(" count=").Append(count).Append('\n');
            for (int i = 0; i < count; i++)
            {
                sb.Append(i)
                  .Append(" org=").Append(((Vector3)gpu[i].originFwd).ToString("F2"))
                  .Append(" fwd=").Append(((Vector3)gpu[i].forward).ToString("F2"))
                  .Append(" rad=(").Append(gpu[i].radius.x.ToString("F3")).Append(',')
                  .Append(gpu[i].radius.y.ToString("F3")).Append(',')
                  .Append(gpu[i].radius.w.ToString("F2")).Append(')')
                  .Append(" boundC=").Append(((Vector3)gpu[i].bound).ToString("F2"))
                  .Append(" boundR=").Append(gpu[i].bound.w.ToString("F2"))
                  .Append(" shape=(").Append(gpu[i].shape.x.ToString("F2")).Append(',')
                  .Append(gpu[i].shape.y.ToString("F2")).Append(',')
                  .Append(gpu[i].shape.z.ToString("F2")).Append(')')
                  // Carácter: seedB/flare/tatter/notch. Debe variar MUCHO entre
                  // filas; si sale casi constante, el shader no podrá variar.
                  .Append(" char=(sB=").Append(gpu[i].originFwd.w.ToString("F2"))
                  .Append(" fl=").Append(gpu[i].forward.w.ToString("F2"))
                  .Append(" tt=").Append(gpu[i].shape.w.ToString("F2"))
                  .Append(" nt=").Append(gpu[i].radius.w.ToString("F2")).Append(')')
                  .Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Reserva el buffer y se suscribe al disparo. Idempotente.</summary>
        public static void Ensure()
        {
            if (buffer == null) buffer = new ComputeBuffer(MaxHoles, GpuBufferStride);
            if (injectionBuffer == null) injectionBuffer = new ComputeBuffer(MaxHoles, GpuBufferStride);


        }

        public static void Release()
        {
            injectionBuffer?.Release();
            injectionBuffer = null;
            InjectionCount = 0;
            if (buffer != null)
            {
                buffer.Release();
                buffer = null;
            }
            for (int i = 0; i < MaxHoles; i++) holes[i].active = false;
            count = 0;
            cursor = 0;
        }

        /// <summary>
        /// Registra un disparo. `origin` suele ser la boca del arma y `forward` la
        /// dirección de la vista; el cono se extiende `depth` metros hacia delante.
        /// </summary>
        /// <summary>Dispara un agujero con CARÁCTER sorteado al azar.</summary>
        public static void Add(Vector3 origin, Vector3 forward)
        {
            AddInternal(origin, forward,
                Random.Range(0f, 1f),   // shapeSeed
                Random.Range(0f, 1f),   // seedB
                Random.Range(0.55f, 1.70f),  // shapeScale (densidad del desgarro)
                Random.Range(0.90f, 1.10f),  // aspect: variación moderada
                Random.Range(0.12f, 0.60f),  // tatter
                Random.Range(0f, 1f),   // notch
                Random.Range(-0.25f, 0.25f)); // flare
        }

        /// <summary>Inyecta el trayecto resuelto por el arma, con su alcance y máscara.</summary>
        public static void AddSegment(Vector3 origin, Vector3 end)
        {
            Vector3 delta = end - origin;
            float length = delta.magnitude;
            if (length <= 0.0001f) return;
            AddInternal(origin, delta / length,
                Random.Range(0f,1f), Random.Range(0f,1f), Random.Range(0.55f,1.70f),
                Random.Range(0.90f,1.10f), Random.Range(0.12f,0.60f),
                Random.Range(0f,1f), Random.Range(-0.25f,0.25f), length);
        }

        /// <summary>
        /// Diagnóstico/verificación: dispara con un CARÁCTER EXPLÍCITO en vez de
        /// sorteado. Sirve para capturar la MISMA posición con formas opuestas y
        /// comprobar que el shader cambia de silueta (no sólo de tamaño).
        /// </summary>
        public static void AddForced(Vector3 origin, Vector3 forward,
            float shapeSeed, float seedB, float shapeScale, float aspect,
            float tatter, float notch, float flare)
        {
            AddInternal(origin, forward, shapeSeed, seedB, shapeScale, aspect,
                        tatter, notch, flare);
        }

        private static void AddInternal(Vector3 origin, Vector3 forward,
            float shapeSeed, float seedB, float shapeScale, float aspect,
            float tatter, float notch, float flare, float resolvedLength = -1f)
        {
            if (forward.sqrMagnitude < 1e-8f) return;
            Ensure();
            AddedTotal++;

            // Recorded shots are a ring; their transported density outlives
            // the record. Evicting a record never removes an existing wake.
            int i = cursor;
            cursor = (cursor + 1) % MaxHoles;

            holes[i].active = true;
            holes[i].t = 0f;
            holes[i].origin = origin;
            // La deformación pertenece al gas; el eje sigue el disparo.
            holes[i].forward = forward.normalized;
            holes[i].speed = lifetimeSpeed / Random.Range(0.90f, 1.10f);
            holes[i].pendingInjection = true;
            holes[i].length = resolvedLength >= 0f ? resolvedLength : depth;
            if (resolvedLength < 0f && Physics.Raycast(origin, holes[i].forward, out RaycastHit hit, depth, ~0,
                QueryTriggerInteraction.Ignore)) holes[i].length = hit.distance;

            // --- Carácter de FORMA por disparo ----------------------------
            // Es lo que hace que dos balas no se vean calcadas. Antes el
            // shader usaba offsets de ruido y un `aspect` CONSTANTES, así que
            // el desgarro era idéntico para todos los agujeros y sólo
            // cambiaba de sitio/tamaño (el usuario lo describió como "solo
            // cambia el tamaño del hueco"). Aquí se sortea el CARÁCTER, no
            // sólo el tamaño. Ver la documentación de cada campo en `Hole`.
            holes[i].shapeSeed = Mathf.Repeat(shapeSeed, 1f);
            holes[i].seedB = Mathf.Clamp01(seedB);
            holes[i].shapeScale = Mathf.Clamp(shapeScale, 0.55f, 1.70f);
            holes[i].aspect = Mathf.Clamp(aspect, 0.90f, 1.10f);
            holes[i].tatter = Mathf.Clamp01(tatter);
            holes[i].notch = Mathf.Clamp01(notch);
            holes[i].flare = Mathf.Clamp(flare, -0.25f, 0.25f);
            // Rotate the whole cross-section, rather than stretching every
            // shot along the same world-up axis. Cache the basis once.
            Vector3 fwd = holes[i].forward;
            Vector3 reference = Mathf.Abs(fwd.y) < 0.92f ? Vector3.up : Vector3.right;
            Vector3 baseA = Vector3.Cross(fwd, reference).normalized;
            Vector3 baseB = Vector3.Cross(fwd, baseA);
            float roll = holes[i].seedB * Mathf.PI * 2f;
            holes[i].axisA = baseA * Mathf.Cos(roll) + baseB * Mathf.Sin(roll);
            holes[i].axisB = Vector3.Cross(fwd, holes[i].axisA);

        }

        /// <summary>Avanza las vidas y sube los agujeros vivos al buffer.</summary>
        public static void Tick(float dt)
        {
            TickTotal++;
            count = 0;
            InjectionCount = 0;
            if (buffer == null) return;

            for (int i = 0; i < MaxHoles; i++)
            {
                if (!holes[i].active) continue;

                // El ritmo se sortea al crear cada hueco. El paso aleatorio por frame
                // alargaba algunos túneles muchos segundos.
                holes[i].t += dt * holes[i].speed;
                if (holes[i].t > 1f)
                {
                    holes[i].active = false;
                    continue;
                }

                float entryOpen = 1f, exitOpen = 1f;

                // `Vector4` con la `w` ocupada por el CARÁCTER del agujero: no es
                // relleno. Se escribe el bloque entero para que el layout sea el
                // mismo en ambos lados (128 B exactos).
                gpu[count].originFwd = new Vector4(
                    holes[i].origin.x, holes[i].origin.y, holes[i].origin.z,
                    holes[i].seedB);
                gpu[count].forward = new Vector4(
                    holes[i].forward.x, holes[i].forward.y, holes[i].forward.z,
                    holes[i].flare);

                // Base ortonormal del túnel, calculada UNA vez aquí en vez de
                // `cross` + `normalize` + branch por muestra en el shader.
                Vector3 fwd = holes[i].forward;
                Vector3 axisA = holes[i].axisA;
                Vector3 axisB = holes[i].axisB;
                // Orientación estable del impulso; el solver transporta el gas después.
                float age = holes[i].t / Mathf.Max(holes[i].speed, 0.0001f);
                float wispAngle = holes[i].shapeSeed * Mathf.PI * 2f;
                gpu[count].axisA = new Vector4(axisA.x, axisA.y, axisA.z, Mathf.Cos(wispAngle));
                gpu[count].axisB = new Vector4(axisB.x, axisB.y, axisB.z, Mathf.Sin(wispAngle));

                // Diferencias pequeñas de anchura por disparo.
                float size = 0.94f + Mathf.Clamp01(holes[i].seedB) * 0.12f;
                float rEntry = RadiusFromOpen(entryOpen, entryRadius * size);
                float rExit = RadiusFromOpen(exitOpen, exitRadius * size);
                gpu[count].radius = new Vector4(
                    rEntry, rExit,
                    entryOpen >= 0.999f && exitOpen >= 0.999f ? 1f : 0f,
                    holes[i].notch);
                gpu[count].phase = new Vector4(holes[i].length, 0f, age, 1f);

                // Radio de la cápsula que contiene el túnel y sus pliegues.
                // La inyección recorre sólo esta región conservadora, que incluye
                // el canal despejado y el gas desplazado a su alrededor.
                float maxR = Mathf.Max(rEntry, rExit, 0f);
                if (rEntry <= 0f && rExit <= 0f)
                {
                    // Sin abertura: el filtro espacial lo descarta.
                    gpu[count].bound = new Vector4(
                        holes[i].origin.x, holes[i].origin.y, holes[i].origin.z, 0f);
                }
                else
                {
                    Vector3 farEnd = holes[i].origin + fwd * holes[i].length;
                    Vector3 boundC = (holes[i].origin + farEnd) * 0.5f;
                    // Perfil máximo + pared suave + desviación del eje.
                    float aspect = holes[i].aspect;
                    float profile = 1.12f + 0.02f * holes[i].tatter;
                    float boundR = maxR * 1.07f * ((profile + 0.22f)
                        * Mathf.Max(aspect, 1f / aspect) + 0.06f) + 0.025f;
                    gpu[count].bound = new Vector4(boundC.x, boundC.y, boundC.z, boundR);
                }

                gpu[count].shape = new Vector4(
                    holes[i].shapeSeed, holes[i].shapeScale, holes[i].aspect,
                    holes[i].tatter);
                if (holes[i].pendingInjection)
                {
                    injectionGpu[InjectionCount++] = gpu[count];
                    holes[i].pendingInjection = false;
                }
                count++;
            }

            // NOTA: esta subida es una operación CPU→GPU inmediata. `Tick` NO debe
            // llamarse mientras el RenderGraph graba los pases: `SetData` durante
            // la grabación desincroniza el grafo y el frame sale NEGRO en cuanto
            // hay un agujero vivo. La feature lo invoca desde
            // `RenderPipelineManager.beginContextRendering`, antes de grabar.
            if (count > 0) buffer.SetData(gpu, 0, 0, count);
            if (InjectionCount > 0) injectionBuffer.SetData(injectionGpu, 0, 0, InjectionCount);
        }

        /// <summary>Quick CPU rejection: allocate a wake only for clouds touched by a new shot.</summary>
        public static bool InjectionIntersects(Bounds bounds)
        {
            bounds.Expand(entryRadius * 3f);
            for (int i = 0; i < InjectionCount; i++)
            {
                var h = injectionGpu[i];
                if (bounds.IntersectRay(new Ray((Vector3)h.originFwd, (Vector3)h.forward), out float distance)
                    && distance <= h.phase.x + entryRadius) return true;
            }
            return false;
        }

        public static Bounds InjectionBounds(int index)
        {
            var shot = injectionGpu[index];
            Vector3 start = shot.originFwd;
            Vector3 end = start + (Vector3)shot.forward * shot.phase.x;
            var bounds = new Bounds(start, Vector3.zero);
            bounds.Encapsulate(end);
            bounds.Expand(shot.bound.w * 3f);
            return bounds;
        }

        private static float RadiusFromOpen(float open, float radius) => radius * Mathf.Clamp01(open);
    }
}
