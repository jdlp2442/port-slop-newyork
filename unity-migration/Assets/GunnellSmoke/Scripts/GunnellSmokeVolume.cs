using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GunnellSmoke
{
    /// <summary>Obstacle-aware arrival field with continuous, filtered smoke growth.</summary>
    [ExecuteAlways]
    [AddComponentMenu("Gunnell Smoke/Smoke Volume")]
    public class GunnellSmokeVolume : MonoBehaviour
    {
        public const float MinimumExpansionDuration = 3.6f;
        [Header("Rejilla")]
        [Tooltip("Tamaño de la celda de colisión en metros. La superficie visible se reconstruye suavemente.")]
        [Range(0.1f, 1f)] public float voxelSize = 0.35f;
        public Vector3Int gridExtent = new Vector3Int(24, 12, 24);
        [HideInInspector] public int maxVoxels = 6000;
        [Range(1f, 20f)] public float radius = 5.5f;

        [Header("Crecimiento")]
        [Tooltip("Velocidad de expansión. 250 unidades equivalen a una celda por segundo.")]
        [Range(60f, 20000f)] public float growthPerSecond = 2500f;
        [Tooltip("Duración de la descarga. El mínimo evita que escenas antiguas aceleren el nuevo nacimiento del humo.")]
        [Range(MinimumExpansionDuration, 8f)] public float expansionDuration = MinimumExpansionDuration;
        [Range(0.2f, 3f)] public float emissionDuration = 1.15f;
        public bool autoStart = true;

        [Header("Hebras de humo")]
        public bool edgeParticles = true;
        [Range(32, 256)] public int particleBudget = 128;
        [HideInInspector] public Transform emitter;

        [Header("Obstáculos")]
        [Tooltip("La expansión rodea los colliders sólidos. Ignora triggers, cuerpos dinámicos y el jugador.")]
        public bool avoidObstacles = true;
        public LayerMask obstacleMask = ~0;
        [Range(0.02f, 0.5f)] public float obstacleRadius = 0.14f;

        [Header("Ciclo de vida")]
        public float sustainDuration = 18f;
        [Range(0.5f, 10f)] public float fadeDuration = 4f;
        public bool infinite;
        public bool destroyOnFinish = true;

        // Retained for existing serialized scenes; rendering now consumes [0,1] density.
        [HideInInspector] public float fillDepthScale = 16f;

        private float[] arrivalDistance;
        private float[] occupancy;
        private float[] releaseDelays;
        private float[] scratch;
        private float[] filtered;
        private Vector2[] fieldPixels;
        private byte[] solidCache;
        private float[] contactMask;
        private readonly Dictionary<MeshCollider, ContactMesh> contactMeshes = new Dictionary<MeshCollider, ContactMesh>();
        private byte[] verticalLinks;
        private uint[] checkedLinks, openLinks;
        private int sourceLayer;
        private float sourceProbeHeight;
        private float supportHeight;
        private float lowestGroundHeight;
        private MeshCollider conduitCollider;
        private Vector3Int gridRes;
        private Vector3 gridMinLocal;
        private Vector3Int seedCell;
        private Texture3D densityTexture;
        private RTHandle densityHandle;
        private GunnellSmokeGrowth gpuGrowth;
        private Vector3Int activeMin, activeMax;
        private GunnellSmokeWake wake;
        private GunnellSmokeParticles particles;
        internal GunnellSmokeParticles Particles => particles != null ? particles
            : (particles = GetComponent<GunnellSmokeParticles>());
        public GunnellSmokeWake Wake => wake ?? (wake = new GunnellSmokeWake(this));
        public RTHandle WakeDensityHandle => wake != null ? wake.DensityHandle : null;
        private float growthDistance;
        private float propagationBudget;
        private float maxArrivalDistance;
        private float growthAge;
        private float nextDensityUpdate;
        private float growthDuration;
        private bool growing;
        private bool finished;
        private int liveCount;
        public const int MaxBlasts = 2;
        private readonly Vector4[] blastSpheres = new Vector4[MaxBlasts];
        private readonly Vector4[] blastStates = new Vector4[MaxBlasts];
        private readonly Vector4[] blastFlows = new Vector4[MaxBlasts];
        private int blastCursor;
        private const float BlastLifetime = 7.2f;

        public RTHandle DensityHandle => densityHandle;
        public Texture CollisionTexture => gpuGrowth != null ? gpuGrowth.Texture : densityTexture;
        public Vector3 ActiveMinWorld => transform.TransformPoint(gridMinLocal + (Vector3)activeMin * voxelSize);
        public Vector3 ActiveMaxWorld => transform.TransformPoint(gridMinLocal + (Vector3)activeMax * voxelSize);
        public float GrowthProgress => Mathf.Clamp01(growthAge / Mathf.Max(0.1f, growthDuration));
        public Vector3Int GridResolution => gridRes;
        public int LiveVoxelCount => liveCount;
        public float ShapeRadius => Mathf.Min(propagationBudget, Mathf.Max(voxelSize * 0.6f, growthDistance));
        public float Age => growthAge;
        public bool HasProceduralExplosionFlow => wake != null && wake.HasExplosionResponse;
        public float ReleaseEnergy => Mathf.SmoothStep(0f, 1f, growthAge / 0.26f)
            * (1f - Mathf.SmoothStep(0f, 1f, (GrowthProgress - 0.25f) / 0.75f));
        public bool IsFinished => finished;
        public float Dissipation => infinite ? 0f : Mathf.Clamp01(
            (growthAge - growthDuration - Mathf.Max(0f, sustainDuration)) / Mathf.Max(0.5f, fadeDuration));
        public float VerticalScale => Mathf.Lerp(0.62f, 1.15f,
            Mathf.SmoothStep(0f, 1f, (GrowthProgress - 0.08f) / 0.84f));
        public float DensityFade => finished ? 0f : Mathf.SmoothStep(0f, 1f, growthAge / 0.38f)
            * Mathf.SmoothStep(0f, 1f, (1f - Dissipation) / 0.12f);
        public bool HasDensity => !finished && densityHandle != null && liveCount > 0 && DensityFade > 0f;
        public Vector3 SmokeOrigin => transform.TransformPoint(CellCenter(seedCell));
        public Vector3 RenderOrigin => SmokeOrigin;
        public Vector3 EmissionOrigin => transform.position + Vector3.up * 0.08f;
        public Vector3 GridMinWorld => transform.TransformPoint(gridMinLocal);
        public Vector3 GridMaxWorld => transform.TransformPoint(gridMinLocal + (Vector3)gridRes * voxelSize);

        private static readonly List<GunnellSmokeVolume> registry = new List<GunnellSmokeVolume>(4);
        public static IReadOnlyList<GunnellSmokeVolume> Volumes => registry;
        private static readonly Collider[] obstacleHits = new Collider[64];
        private static readonly RaycastHit[] groundHits = new RaycastHit[16];
        private static readonly Collider[] containerHits = new Collider[256];
        private static readonly Vector3[] cardinalDirections = { Vector3.left, Vector3.right,
            Vector3.up, Vector3.down, Vector3.forward, Vector3.back };

        private bool FitContainedGrid(int cellBudget)
        {
            if (!avoidObstacles) return false;
            int count = Physics.OverlapSphereNonAlloc(transform.position, radius * 0.6f,
                containerHits, obstacleMask, QueryTriggerInteraction.Ignore);
            MeshCollider container = null;
            float bestVolume = float.PositiveInfinity;
            Vector3[] directions = cardinalDirections;
            for (int i = 0; i < count; i++)
            {
                var mesh = containerHits[i] as MeshCollider;
                if (mesh == null || mesh.convex || mesh.attachedRigidbody != null) continue;
                Bounds bounds = mesh.bounds;
                if (bounds.SqrDistance(transform.position) > radius * radius * 0.36f) continue;
                float size = bounds.size.x * bounds.size.y * bounds.size.z;
                if (size >= bestVolume || bounds.size.magnitude > radius * 4f) continue;
                int walls = 0;
                // Recognise a hollow passage from the source or just inside
                // its mouth. A release beside an opening needs the same domain
                // as a release inside it, otherwise the far end is cropped.
                for (int probe = 0; probe < 13 && walls < 4; probe++)
                {
                    Vector3 origin = bounds.ClosestPoint(transform.position);
                    if (probe > 0) origin += directions[(probe - 1) % 6] * voxelSize * (probe <= 6 ? 1f : 2f);
                    if (!bounds.Contains(origin)) continue;
                    int enclosingWalls = 0;
                    foreach (Vector3 direction in directions)
                    {
                        int hits = Physics.RaycastNonAlloc(origin, direction, groundHits,
                            radius * 0.6f, obstacleMask, QueryTriggerInteraction.Ignore);
                        for (int h = 0; h < hits; h++)
                        {
                            Collider hit = groundHits[h].collider;
                            if (hit == mesh || hit.transform.IsChildOf(mesh.transform)) { enclosingWalls++; break; }
                        }
                    }
                    walls = Mathf.Max(walls, enclosingWalls);
                }
                if (walls < 4) continue;
                container = mesh;
                bestVolume = size;
            }
            if (container == null) return false;
            conduitCollider = container;
            // A conduit is a collision boundary, not the boundary of the gas.
            // Keep the ordinary release domain around the source, including
            // the floor below the mouths. This leaves room for smoke to spill
            // out without becoming a rectangular slab at the fitted grid faces.
            int below = DownwardCells();
            Vector3 releaseMin = transform.position + new Vector3(
                -(gridRes.x + 1) * voxelSize * 0.5f, -(below + 0.5f) * voxelSize,
                -(gridRes.z + 1) * voxelSize * 0.5f);
            if (!float.IsNaN(lowestGroundHeight)) releaseMin.y = lowestGroundHeight - voxelSize;
            Bounds releaseBounds = new Bounds(releaseMin + (Vector3)gridRes * voxelSize * 0.5f,
                (Vector3)gridRes * voxelSize);
            Bounds fit = container.bounds;
            // Include a full free-space plume beyond every mouth. A small
            // collider-sized pad gives the escaping pressure a flat box cap.
            fit.Expand((radius + voxelSize * 4f) * 2f);
            fit.Encapsulate(releaseBounds);
            do
            {
                // Align a cell face with the actual floor, even when fitting
                // the conduit or increasing the cell size to respect the cap.
                // Otherwise the fitted box can leave a partial empty layer
                // between the falling plume and a thin ground mesh.
                if (!float.IsNaN(lowestGroundHeight))
                {
                    Vector3 minimum = fit.min;
                    minimum.y = lowestGroundHeight - voxelSize;
                    fit.SetMinMax(minimum, fit.max);
                }
                Vector3 span = fit.size;
                gridRes = new Vector3Int(Mathf.Max(4, Mathf.CeilToInt(span.x / voxelSize)),
                    Mathf.Max(4, Mathf.CeilToInt(span.y / voxelSize)),
                    Mathf.Max(4, Mathf.CeilToInt(span.z / voxelSize)));
                // Reserve a bounded exterior band while retaining the fine
                // conduit collision cells. The GPU wake has its own fixed cap.
                if (gridRes.x * gridRes.y * gridRes.z <= cellBudget * 3) break;
                voxelSize *= 1.04f;
            } while (true);
            gridMinLocal = transform.InverseTransformPoint(fit.min);
            Vector3 source = -gridMinLocal / voxelSize;
            seedCell = new Vector3Int(Mathf.FloorToInt(source.x), Mathf.FloorToInt(source.y), Mathf.FloorToInt(source.z));
            propagationBudget = radius;
            return true;
        }

        private void OnEnable()
        {
            if (!registry.Contains(this)) registry.Add(this);
            SmokeGameplay.ExplosionTriggered -= ReactToExplosion;
            SmokeGameplay.ExplosionTriggered += ReactToExplosion;
            if (Application.isPlaying && autoStart) BeginGrowth();
        }
        private void OnDisable()
        {
            registry.Remove(this);
            SmokeGameplay.ExplosionTriggered -= ReactToExplosion;
            Particles?.Clear();
            Release();
        }
        private void OnDestroy() => Release();

        private void Release()
        {
            wake?.Dispose();
            wake = null;
            if (gpuGrowth != null) { gpuGrowth.Dispose(); gpuGrowth = null; }
            else densityHandle?.Release();
            densityHandle = null;
            CoreUtils.Destroy(densityTexture);
            densityTexture = null;
            arrivalDistance = occupancy = scratch = filtered = releaseDelays = null;
            fieldPixels = null;
            solidCache = null;
            contactMask = null;
            contactMeshes.Clear();
            verticalLinks = null;
            checkedLinks = openLinks = null;
        }

        private int IndexOf(Vector3Int c) => c.x + c.y * gridRes.x + c.z * gridRes.x * gridRes.y;
        private Vector3 CellCenter(Vector3Int c) => gridMinLocal + ((Vector3)c + Vector3.one * 0.5f) * voxelSize;
        private bool InGrid(Vector3Int c) => c.x >= 0 && c.y >= 0 && c.z >= 0 &&
            c.x < gridRes.x && c.y < gridRes.y && c.z < gridRes.z;

        private bool InsideEnvelope(Vector3Int c)
        {
            // Only reserve a reconstruction border. Reach is decided by travel
            // through free space, rather than by an origin-centred sphere.
            return c.x > 0 && c.z > 0 && c.x < gridRes.x - 1 && c.z < gridRes.z - 1;
        }

        private bool IsSolid(Vector3Int c)
        {
            if (!avoidObstacles) return false;
            int index = IndexOf(c);
            if (solidCache[index] != 0) return solidCache[index] == 2;
            // Include shared faces so a thin glass wall or floor exactly on a
            // lattice face still marks both adjacent cells for link checking.
            Vector3 probe = Vector3.one * (voxelSize * 0.501f);
            if (c.y == sourceLayer) probe.y = sourceProbeHeight;
            Vector3 center = transform.TransformPoint(CellCenter(c));
            int count = Physics.OverlapBoxNonAlloc(center,
                probe, obstacleHits,
                transform.rotation, obstacleMask, QueryTriggerInteraction.Ignore);
            bool solid = count == obstacleHits.Length;
            float mask = solid ? 0f : 1f;
            for (int i = 0; i < count; i++)
            {
                Collider hit = obstacleHits[i];
                if (hit == null || hit.attachedRigidbody != null ||
                    hit.GetComponentInParent<CharacterController>() != null ||
                    hit.GetComponentInParent<GunnellSmokeVolume>() != null) continue;
                solid = true;
                // Reconstruct the contact independently of cell connectivity.
                // Unreadable or very detailed meshes retain a conservative mask.
                float distance = -voxelSize;
                if (hit is BoxCollider box)
                {
                    Vector3 scale = box.transform.lossyScale;
                    Vector3 half = Vector3.Scale(box.size * 0.5f,
                        new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                    Vector3 local = Quaternion.Inverse(box.transform.rotation) *
                        (center - box.transform.TransformPoint(box.center));
                    Vector3 delta = new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z)) - half;
                    distance = Vector3.Max(delta, Vector3.zero).magnitude +
                        Mathf.Min(Mathf.Max(delta.x, Mathf.Max(delta.y, delta.z)), 0f);
                }
                else if (hit is SphereCollider sphere)
                {
                    Vector3 scale = sphere.transform.lossyScale;
                    float size = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                    if (size > 0f)
                        distance = Vector3.Distance(center, sphere.transform.TransformPoint(sphere.center)) - size;
                }
                else if (hit is CapsuleCollider capsule)
                {
                    Vector3 scale = capsule.transform.lossyScale;
                    scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    int axis = capsule.direction;
                    float rad = capsule.radius * Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
                    Vector3 local = Quaternion.Inverse(capsule.transform.rotation) *
                        (center - capsule.transform.TransformPoint(capsule.center));
                    float half = Mathf.Max(0f, capsule.height * scale[axis] * 0.5f - rad);
                    local[axis] -= Mathf.Clamp(local[axis], -half, half);
                    if (rad > 0f) distance = local.magnitude - rad;
                }
                else if (hit is MeshCollider mesh)
                {
                    distance = MeshContactDistance(mesh, center);
                }
                mask = Mathf.Min(mask, Mathf.Clamp01(0.5f + distance / voxelSize));
            }
            // A cell touching a collider can still have a free centre. Keep
            // that partial cell reachable, with the surface mask independent
            // of connectivity. Its links are checked against real colliders.
            bool blocked = solid && (count == obstacleHits.Length || mask <= 0.5f + 0.01f / voxelSize);
            solidCache[index] = blocked ? (byte)2 : (solid ? (byte)3 : (byte)1);
            contactMask[index] = mask;
            return blocked;
        }

        private struct ContactTriangle
        {
            public Vector3 a, b, c, normal;
            public bool Contains(Vector3 p)
            {
                return Vector3.Dot(Vector3.Cross(b - a, p - a), normal) >= -0.000001f &&
                    Vector3.Dot(Vector3.Cross(c - b, p - b), normal) >= -0.000001f &&
                    Vector3.Dot(Vector3.Cross(a - c, p - c), normal) >= -0.000001f;
            }
            private static Vector3 EdgePoint(Vector3 p, Vector3 a, Vector3 b)
            {
                Vector3 edge = b - a;
                return a + edge * Mathf.Clamp01(Vector3.Dot(p - a, edge) / Mathf.Max(edge.sqrMagnitude, 0.0000001f));
            }
            public float DistanceSquared(Vector3 p)
            {
                Vector3 projected = p - normal * Vector3.Dot(p - a, normal);
                if (Contains(projected)) return (p - projected).sqrMagnitude;
                return Mathf.Min((p - EdgePoint(p, a, b)).sqrMagnitude,
                    Mathf.Min((p - EdgePoint(p, b, c)).sqrMagnitude, (p - EdgePoint(p, c, a)).sqrMagnitude));
            }
        }

        private sealed class ContactMesh
        {
            public ContactTriangle[] triangles;
            public bool closed;
            public Vector2[] outline;
            public Vector3 origin, right, up, forward;
            public float halfHeight;
            public float ExtrudedDistance(Vector3 point)
            {
                Vector3 offset = point - origin;
                Vector2 p = new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, forward));
                float nearest = float.PositiveInfinity;
                bool inside = false;
                for (int i = 0; i < outline.Length; i += 2)
                {
                    Vector2 a = outline[i], b = outline[i + 1], edge = b - a;
                    Vector2 closest = a + edge * Mathf.Clamp01(Vector2.Dot(p - a, edge) / edge.sqrMagnitude);
                    nearest = Mathf.Min(nearest, (p - closest).sqrMagnitude);
                    if ((a.y > p.y) != (b.y > p.y) &&
                        p.x < a.x + (p.y - a.y) * (b.x - a.x) / (b.y - a.y)) inside = !inside;
                }
                float side = Mathf.Sqrt(nearest) * (inside ? -1f : 1f);
                float cap = Mathf.Abs(Vector3.Dot(offset, up)) - halfHeight;
                return new Vector2(Mathf.Max(side, 0f), Mathf.Max(cap, 0f)).magnitude +
                    Mathf.Min(Mathf.Max(side, cap), 0f);
            }
        }

        private float MeshContactDistance(MeshCollider collider, Vector3 point)
        {
            if (!contactMeshes.TryGetValue(collider, out ContactMesh shape))
            {
                shape = BuildContactMesh(collider);
                contactMeshes.Add(collider, shape);
            }
            if (shape == null) return -voxelSize;
            if (shape.outline != null) return shape.ExtrudedDistance(point);
            float nearest = float.PositiveInfinity;
            int crossings = 0;
            // An oblique ray avoids the regular cap diagonals and polygon edges.
            Vector3 direction = new Vector3(0.87341f, 0.34723f, 0.33917f).normalized;
            foreach (var triangle in shape.triangles)
            {
                nearest = Mathf.Min(nearest, triangle.DistanceSquared(point));
                if (!shape.closed) continue;
                float denominator = Vector3.Dot(direction, triangle.normal);
                if (Mathf.Abs(denominator) < 0.000001f) continue;
                float t = Vector3.Dot(triangle.a - point, triangle.normal) / denominator;
                if (t > 0.000001f && triangle.Contains(point + direction * t)) crossings++;
            }
            return Mathf.Sqrt(nearest) * (!shape.closed || (crossings & 1) == 0 ? 1f : -1f);
        }

        private ContactMesh BuildContactMesh(MeshCollider collider)
        {
            Mesh mesh = collider.sharedMesh;
            if (mesh == null || !mesh.isReadable)
                return null;
            int[] indices = mesh.triangles;
            if (indices.Length == 0 || indices.Length > 2048 * 3) return null;
            Vector3[] vertices = mesh.vertices;
            var weld = new Dictionary<Vector3Int, int>();
            int[] ids = new int[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 p = vertices[i] * 100000f;
                var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
                if (!weld.TryGetValue(key, out int id)) { id = weld.Count; weld.Add(key, id); }
                ids[i] = id;
            }
            var edges = new Dictionary<ulong, int>();
            var result = new List<ContactTriangle>();
            for (int i = 0; i < indices.Length; i += 3)
            {
                int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                if (ids[a] == ids[b] || ids[b] == ids[c] || ids[c] == ids[a]) continue;
                Vector3 pa = collider.transform.TransformPoint(vertices[a]);
                Vector3 pb = collider.transform.TransformPoint(vertices[b]);
                Vector3 pc = collider.transform.TransformPoint(vertices[c]);
                Vector3 normal = Vector3.Cross(pb - pa, pc - pa);
                if (normal.sqrMagnitude < 0.00000001f) return null;
                result.Add(new ContactTriangle { a = pa, b = pb, c = pc, normal = normal.normalized });
                for (int k = 0; k < 3; k++)
                {
                    int u = ids[indices[i + k]], v = ids[indices[i + (k + 1) % 3]];
                    ulong key = ((ulong)(uint)Mathf.Min(u, v) << 32) | (uint)Mathf.Max(u, v);
                    edges.TryGetValue(key, out int count); edges[key] = count + 1;
                }
            }
            if (result.Count == 0) return null;
            bool closed = true;
            foreach (int count in edges.Values) if (count != 2) { closed = false; break; }
            // Open surfaces use unsigned contact distance. Their cross-face
            // links are still blocked by real collider raycasts; free space
            // next to a zero-thickness plane must not become an empty cell.
            var shape = new ContactMesh { triangles = result.ToArray(), closed = closed };
            // Stars and cylinders in the demo are closed vertical extrusions.
            // Their exact footprint needs only one segment loop per cell, instead
            // of a nearest-triangle search plus a 3D inside/outside ray.
            Bounds bounds = mesh.bounds;
            bool extruded = closed;
            foreach (Vector3 p in vertices)
                if (Mathf.Abs(p.y - bounds.min.y) > 0.0001f && Mathf.Abs(p.y - bounds.max.y) > 0.0001f)
                { extruded = false; break; }
            if (extruded)
            {
                shape.origin = collider.transform.TransformPoint(bounds.center);
                shape.right = collider.transform.rotation * Vector3.right;
                shape.up = collider.transform.rotation * Vector3.up;
                shape.forward = collider.transform.rotation * Vector3.forward;
                shape.halfHeight = bounds.extents.y * Mathf.Abs(collider.transform.lossyScale.y);
                var outline = new List<Vector2>();
                var degree = new Dictionary<int, int>();
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                    int bottomCount = (Mathf.Abs(vertices[a].y - bounds.min.y) < 0.0001f ? 1 : 0) +
                        (Mathf.Abs(vertices[b].y - bounds.min.y) < 0.0001f ? 1 : 0) +
                        (Mathf.Abs(vertices[c].y - bounds.min.y) < 0.0001f ? 1 : 0);
                    if (bottomCount != 2) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int u = indices[i + k], v = indices[i + (k + 1) % 3];
                        if (Mathf.Abs(vertices[u].y - bounds.min.y) >= 0.0001f ||
                            Mathf.Abs(vertices[v].y - bounds.min.y) >= 0.0001f) continue;
                        if (ids[u] == ids[v]) continue;
                        for (int endpoint = 0; endpoint < 2; endpoint++)
                        {
                            int vertex = endpoint == 0 ? u : v;
                            Vector3 p = collider.transform.TransformPoint(vertices[vertex]) - shape.origin;
                            outline.Add(new Vector2(Vector3.Dot(p, shape.right), Vector3.Dot(p, shape.forward)));
                            degree.TryGetValue(ids[vertex], out int count); degree[ids[vertex]] = count + 1;
                        }
                    }
                }
                foreach (int count in degree.Values) if (count != 2) extruded = false;
                if (extruded && outline.Count >= 6) shape.outline = outline.ToArray();
            }
            return shape;
        }

        private int DownwardCells()
        {
            supportHeight = float.NaN;
            lowestGroundHeight = float.NaN;
            float drop = avoidObstacles ? 0f : radius / 1.15f;
            bool foundGround = false;
            if (avoidObstacles)
            {
                // Sample the surroundings too: the surface directly beneath a
                // grenade on a box is its top, while smoke spills over its edge.
                for (int sample = 0; sample < 17; sample++)
                {
                    float angle = (sample - 1) % 8 * (Mathf.PI * 0.25f);
                    float ring = sample == 0 ? 0f : radius * (sample <= 8 ? 0.55f : 0.90f);
                    Vector3 origin = transform.position + new Vector3(Mathf.Cos(angle) * ring, voxelSize, Mathf.Sin(angle) * ring);
                    int count = Physics.RaycastNonAlloc(origin, Vector3.down, groundHits,
                        Mathf.Max(radius * 2.2f, gridRes.y * voxelSize) + voxelSize,
                        obstacleMask, QueryTriggerInteraction.Ignore);
                    float nearest = float.PositiveInfinity;
                    for (int i = 0; i < count; i++)
                    {
                        Collider hit = groundHits[i].collider;
                        if (hit == null || hit.attachedRigidbody != null ||
                            hit.GetComponentInParent<CharacterController>() != null ||
                            hit.GetComponentInParent<GunnellSmokeVolume>() != null) continue;
                        nearest = Mathf.Min(nearest, groundHits[i].distance);
                    }
                    if (float.IsPositiveInfinity(nearest))
                    {
                        // An open edge above a drop needs space below even when
                        // the bottom lies beyond this cloud's reachable bounds.
                        drop = Mathf.Max(drop, radius / 0.85f);
                        continue;
                    }
                    foundGround = true;
                    if (sample == 0) supportHeight = origin.y - nearest;
                    float ground = origin.y - nearest;
                    lowestGroundHeight = float.IsNaN(lowestGroundHeight) ? ground : Mathf.Min(lowestGroundHeight, ground);
                    drop = Mathf.Max(drop, nearest - voxelSize);
                }
                if (!foundGround) drop = radius / 1.15f;
            }
            // Include a solid reconstruction cell below the actual lower floor.
            // Reserving upper space must not silently crop a descending plume.
            int below = Mathf.CeilToInt(Mathf.Max(0f, drop) / voxelSize) + 1;
            int above = Mathf.CeilToInt(radius / (1.35f * voxelSize)) + 2;
            gridRes.y = Mathf.Max(gridRes.y, below + above);
            return below;
        }

        private void AlignVerticalContacts(bool narrowX, bool narrowZ)
        {
            if (!avoidObstacles) return;
            // Put a cell face on the nearest parallel wall, including the side
            // of a raised platform. Otherwise OverlapBox reserves up to an
            // entire empty cell outside the wall. The source stays in place.
            for (int axis = 0; axis < 3; axis += 2)
            {
                if ((axis == 0 && narrowX) || (axis == 2 && narrowZ)) continue;
                Vector3 localAxis = axis == 0 ? Vector3.right : Vector3.forward;
                Vector3 worldAxis = transform.TransformDirection(localAxis);
                float nearest = radius;
                float plane = 0f;
                bool found = false;
                for (int level = 0; level < 5; level++)
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 origin = transform.position - transform.up * (radius * level * 0.2f);
                    int count = Physics.RaycastNonAlloc(origin, worldAxis * side,
                        groundHits, radius, obstacleMask, QueryTriggerInteraction.Ignore);
                    for (int i = 0; i < count; i++)
                    {
                        Collider hit = groundHits[i].collider;
                        if (hit == null || hit.attachedRigidbody != null ||
                            hit.GetComponentInParent<CharacterController>() != null ||
                            hit.GetComponentInParent<GunnellSmokeVolume>() != null ||
                            Mathf.Abs(Vector3.Dot(groundHits[i].normal, worldAxis)) < 0.999f ||
                            groundHits[i].distance >= nearest) continue;
                        nearest = groundHits[i].distance;
                        plane = transform.InverseTransformPoint(groundHits[i].point)[axis];
                        found = true;
                    }
                }
                if (found)
                    gridMinLocal[axis] = plane + Mathf.Round((gridMinLocal[axis] - plane) / voxelSize) * voxelSize;
            }
        }

        public void BeginGrowth()
        {
            Release();
            System.Array.Clear(blastSpheres, 0, MaxBlasts);
            System.Array.Clear(blastStates, 0, MaxBlasts);
            blastCursor = 0;
            voxelSize = Mathf.Max(0.1f, voxelSize);
            gridRes = new Vector3Int(Mathf.Max(4, gridExtent.x * 2),
                Mathf.Max(4, gridExtent.y * 2), Mathf.Max(4, gridExtent.z * 2));
            // The collision domain must include the lobe reconstruction band
            // and an empty border outside the visible cloud on both sides.
            int horizontalCells = Mathf.CeilToInt(radius / voxelSize) * 2 + 8;
            gridRes.x = Mathf.Max(gridRes.x, horizontalCells);
            gridRes.z = Mathf.Max(gridRes.z, horizontalCells);
            if (avoidObstacles) Physics.SyncTransforms();
            propagationBudget = radius;
            int cellBudget = gridRes.x * gridRes.y * gridRes.z;
            conduitCollider = null;
            bool contained = FitContainedGrid(cellBudget);
            sourceProbeHeight = voxelSize * 0.499f;
            // Measure the real clearance at the emission point before choosing
            // a cell. A pocket below a shelf must keep its seed below that shelf.
            float ceiling = StaticDistance(transform.position, Vector3.up, voxelSize * 1.5f);
            float floor = StaticDistance(transform.position, Vector3.down, voxelSize * 1.5f);
            bool narrow = ceiling + floor < voxelSize * 1.5f;
            if (narrow) sourceProbeHeight = Mathf.Max(0.005f, Mathf.Min(ceiling, floor) * 0.45f);
            if (!contained)
            {
                int below = DownwardCells();
                gridMinLocal = new Vector3(-(gridRes.x + 1) * voxelSize * 0.5f,
                    -(below + 0.5f) * voxelSize, -(gridRes.z + 1) * voxelSize * 0.5f);
                if (!narrow && !float.IsNaN(lowestGroundHeight))
                {
                    // Contact is reconstructed from cells on both sides of
                    // the lower floor, even when the source rests on a shelf.
                    gridMinLocal.y = lowestGroundHeight - voxelSize - transform.position.y;
                }
                // Keep a source-centred sample in subcell slots. Snapping the
                // grid to either wall can otherwise move every centre outside
                // the opening even though the source is in free space.
                float xClearance = StaticDistance(transform.position, Vector3.left, voxelSize * 1.5f) +
                    StaticDistance(transform.position, Vector3.right, voxelSize * 1.5f);
                float zClearance = StaticDistance(transform.position, Vector3.back, voxelSize * 1.5f) +
                    StaticDistance(transform.position, Vector3.forward, voxelSize * 1.5f);
                AlignVerticalContacts(xClearance < voxelSize * 1.5f, zClearance < voxelSize * 1.5f);
                seedCell = new Vector3Int(Mathf.FloorToInt(-gridMinLocal.x / voxelSize),
                    Mathf.FloorToInt(-gridMinLocal.y / voxelSize), Mathf.FloorToInt(-gridMinLocal.z / voxelSize));
            }
            int total = gridRes.x * gridRes.y * gridRes.z;
            arrivalDistance = new float[total]; occupancy = new float[total];
            releaseDelays = new float[total]; scratch = new float[total]; filtered = new float[total];
            fieldPixels = null; solidCache = new byte[total]; contactMask = new float[total];
            verticalLinks = new byte[total]; checkedLinks = new uint[total]; openLinks = new uint[total];
            sourceLayer = seedCell.y;

            // A grenade resting against a wall must seed an adjacent free cell.
            Vector3Int requestedSeed = seedCell;
            float nearest = float.PositiveInfinity;
            for (int z = -2; z <= 2; z++)
            for (int y = -1; y <= 2; y++)
            for (int x = -2; x <= 2; x++)
            {
                var c = requestedSeed + new Vector3Int(x, y, z);
                float distance = x * x + y * y + z * z;
                Vector3 candidate = transform.TransformPoint(CellCenter(c));
                Vector3 segment = candidate - transform.position;
                if (InGrid(c) && distance < nearest && !IsSolid(c) &&
                    StaticDistance(transform.position, segment.normalized, segment.magnitude) >= segment.magnitude)
                { seedCell = c; nearest = distance; }
            }

            BuildArrivalField();
            BuildReleaseDelays();
            // Two channels in one filtered GPU read: travel potential and solid
            // clipping remain independent, so contacts do not erase the gas.
            if (GunnellSmokeGrowth.Supported)
            {
                gpuGrowth = new GunnellSmokeGrowth(gridRes,arrivalDistance,releaseDelays,solidCache,contactMask);
                densityHandle = gpuGrowth.Handle;
            }
            else
            {
                fieldPixels = new Vector2[total];
                densityTexture = new Texture3D(gridRes.x, gridRes.y, gridRes.z, TextureFormat.RGFloat, false)
                {
                    name = "Gunnell Smoke Density",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                densityHandle = RTHandles.Alloc(densityTexture);
            }
            growthDistance = voxelSize * 0.6f;
            growthAge = 0f;
            nextDensityUpdate = 0f;
            float travelBudget = propagationBudget;
            float speed = Mathf.Max(0.25f, growthPerSecond / 250f * voxelSize);
            growthDuration = Mathf.Max(MinimumExpansionDuration, Mathf.Max(expansionDuration, travelBudget / speed));
            finished = false;
            growing = true;
            UploadDensity();
            if (Application.isPlaying && edgeParticles)
            {
                if(particles==null)particles = GetComponent<GunnellSmokeParticles>();
                if (particles == null) particles = gameObject.AddComponent<GunnellSmokeParticles>();
                particles.Initialize(this);
            }
            else Particles?.Clear();
        }

        private struct ArrivalNode
        {
            public Vector3Int cell;
            public float distance;
            public ArrivalNode(Vector3Int c, float d) { cell = c; distance = d; }
        }
        private static void Push(List<ArrivalNode> heap, ArrivalNode node)
        {
            int index = heap.Count;
            heap.Add(node);
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (heap[parent].distance <= node.distance) break;
                heap[index] = heap[parent];
                index = parent;
            }
            heap[index] = node;
        }
        private static ArrivalNode Pop(List<ArrivalNode> heap)
        {
            ArrivalNode first = heap[0];
            ArrivalNode last = heap[heap.Count - 1];
            heap.RemoveAt(heap.Count - 1);
            if (heap.Count == 0) return first;
            int index = 0;
            while (index * 2 + 1 < heap.Count)
            {
                int child = index * 2 + 1;
                if (child + 1 < heap.Count && heap[child + 1].distance < heap[child].distance) child++;
                if (heap[child].distance >= last.distance) break;
                heap[index] = heap[child];
                index = child;
            }
            heap[index] = last;
            return first;
        }

        private void BuildArrivalField()
        {
            for (int i = 0; i < arrivalDistance.Length; i++) arrivalDistance[i] = float.PositiveInfinity;
            maxArrivalDistance = 0f;
            if (IsSolid(seedCell)) return;
            var heap = new List<ArrivalNode>(1024);
            arrivalDistance[IndexOf(seedCell)] = 0f;
            Push(heap, new ArrivalNode(seedCell, 0f));
            while (heap.Count > 0)
            {
                ArrivalNode node = Pop(heap);
                if (node.distance > arrivalDistance[IndexOf(node.cell)]) continue;
                maxArrivalDistance = Mathf.Max(maxArrivalDistance, node.distance);
                // A low ceiling flattens the gas but must not multiply its
                // lateral reach. Only a passage enclosed across two axes gets
                // extra reach, along its remaining open axis. A tight bend
                // can enclose all three cardinal axes, so retain a moderate
                // travel allowance there without accelerating a broad slab.
                bool enclosedX = WallNearby(node.cell, Vector3Int.left) && WallNearby(node.cell, Vector3Int.right);
                bool enclosedY = WallNearby(node.cell, Vector3Int.up) && WallNearby(node.cell, Vector3Int.down);
                bool enclosedZ = WallNearby(node.cell, new Vector3Int(0, 0, -1)) && WallNearby(node.cell, new Vector3Int(0, 0, 1));
                int enclosure = (enclosedX ? 1 : 0) + (enclosedY ? 1 : 0) + (enclosedZ ? 1 : 0);
                Vector3 position = transform.TransformPoint(CellCenter(node.cell));
                bool curvedPassage = enclosure != 2 && enclosure > 0 && InsideConduit(position);
                float sheetCost = 1f;
                if (enclosure == 1 && !curvedPassage)
                {
                    // Broad, stationary variations break a uniform release
                    // front into folds without adding extra travel budget.
                    sheetCost += 0.10f * (0.5f + 0.5f * Mathf.Sin(position.x * 0.73f + position.z * 0.41f)
                        * Mathf.Sin(position.z * 0.67f - position.y * 0.54f));
                }
                float ordinaryCost = curvedPassage ? 0.40f : (enclosure == 1 ? sheetCost : 1f);
                float costX = enclosure == 2 && !enclosedX ? 0.38f : ordinaryCost;
                float costY = enclosure == 2 && !enclosedY ? 0.38f : ordinaryCost;
                float costZ = enclosure == 2 && !enclosedZ ? 0.38f : ordinaryCost;
                for (int z = -1; z <= 1; z++)
                for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    int axes = x * x + y * y + z * z;
                    if (axes == 0) continue;
                    Vector3Int c = node.cell + new Vector3Int(x, y, z);
                    if (!InGrid(c) || !InsideEnvelope(c)) continue;
                    // Gas spreads along floors and passages faster than it rises.
                    float verticalCost = y > 0 ? (curvedPassage || (enclosure == 2 && !enclosedY) ? 1f : 1.35f) : 0.50f;
                    float distance = node.distance + voxelSize *
                        Mathf.Sqrt(x * x * costX * costX + z * z * costZ * costZ +
                            y * y * costY * costY * verticalCost * verticalCost);
                    if (distance > propagationBudget + voxelSize * 2f) continue;
                    int index = IndexOf(c);
                    if (distance >= arrivalDistance[index] || IsSolid(c)) continue;
                    if (!CellLinkOpen(node.cell, c)) continue;
                    if (y != 0 && sourceProbeHeight < voxelSize * 0.49f &&
                        (node.cell.y == sourceLayer || c.y == sourceLayer) && !VerticalOpen(node.cell, y)) continue;
                    // Diagonal expansion cannot cut through a wall corner.
                    // Partial cells already have a verified straight link. Allow
                    // a diagonal through an angled slot even when its off-path
                    // axis neighbours are solid; ordinary cells keep the guard.
                    if (axes > 1 && solidCache[IndexOf(node.cell)] != 3 && solidCache[index] != 3 &&
                        ((x != 0 && IsSolid(node.cell + new Vector3Int(x, 0, 0))) ||
                        (y != 0 && IsSolid(node.cell + new Vector3Int(0, y, 0))) ||
                        (z != 0 && IsSolid(node.cell + new Vector3Int(0, 0, z))))) continue;
                    arrivalDistance[index] = distance;
                    Push(heap, new ArrivalNode(c, distance));
                }
            }
        }

        private bool InsideConduit(Vector3 point)
        {
            if (conduitCollider == null || !conduitCollider.bounds.Contains(point)) return false;
            // A bend need not enclose two cardinal pairs at short range.
            // Confirm its hollow interior against the actual shell so a low
            // shelf or the empty area inside an elbow's bounding box cannot
            // receive the same pressure allowance as the bore.
            int walls = 0;
            foreach (Vector3 direction in cardinalDirections)
            {
                if (conduitCollider.Raycast(new Ray(point, direction), out _, radius * 0.6f)) walls++;
                if (walls >= 4) return true;
            }
            return false;
        }

        private bool WallNearby(Vector3Int cell, Vector3Int direction)
        {
            Vector3Int previous = cell;
            for (int step = 1; step <= 4; step++)
            {
                Vector3Int probe = cell + direction * step;
                if (!InGrid(probe)) return false;
                if (IsSolid(probe)) return true;
                // Thin curved glass can lie entirely between cell centres.
                // Use the cached segment collision, not just solid centres,
                // to detect the pressure confinement without widening walls.
                if (!CellLinkOpen(previous, probe)) return true;
                previous = probe;
            }
            return false;
        }

        private float StaticDistance(Vector3 origin, Vector3 direction, float length)
        {
            if (!avoidObstacles || length <= 0f) return length;
            int count = Physics.RaycastNonAlloc(origin, direction, groundHits, length,
                obstacleMask, QueryTriggerInteraction.Ignore);
            float nearest = length;
            for (int i = 0; i < count; i++)
            {
                Collider hit = groundHits[i].collider;
                if (hit == null || hit.attachedRigidbody != null ||
                    hit.GetComponentInParent<CharacterController>() != null ||
                    hit.GetComponentInParent<GunnellSmokeVolume>() != null) continue;
                nearest = Mathf.Min(nearest, groundHits[i].distance);
            }
            return nearest;
        }

        private bool CellLinkOpen(Vector3Int from, Vector3Int to)
        {
            int a = IndexOf(from), b = IndexOf(to);
            if (solidCache[a] != 3 && solidCache[b] != 3) return true;
            Vector3Int delta = to - from;
            int direction = delta.x + 1 + (delta.y + 1) * 3 + (delta.z + 1) * 9;
            uint bit = 1u << direction;
            if ((checkedLinks[a] & bit) != 0) return (openLinks[a] & bit) != 0;
            Vector3 start = transform.TransformPoint(CellCenter(from));
            Vector3 segment = transform.TransformPoint(CellCenter(to)) - start;
            bool open = StaticDistance(start, segment.normalized, segment.magnitude) >= segment.magnitude - 0.001f;
            uint reverse = 1u << (26 - direction);
            checkedLinks[a] |= bit; checkedLinks[b] |= reverse;
            if (open) { openLinks[a] |= bit; openLinks[b] |= reverse; }
            return open;
        }

        private bool VerticalOpen(Vector3Int cell, int direction)
        {
            Vector3Int lower = direction > 0 ? cell : cell + Vector3Int.down;
            int index = IndexOf(lower);
            if (verticalLinks[index] == 0)
            {
                bool open = StaticDistance(transform.TransformPoint(CellCenter(lower)),
                    transform.up, voxelSize) >= voxelSize;
                verticalLinks[index] = open ? (byte)1 : (byte)2;
            }
            return verticalLinks[index] == 1;
        }

        private void Update()
        {
            if (!Application.isPlaying || finished || arrivalDistance == null) return;
            AdvanceGrowth(Time.deltaTime);
        }
        private void AdvanceGrowth(float dt)
        {
            if (finished) return;
            growthAge += Mathf.Max(0f, dt);
            if (growing)
            {
                float progress = Mathf.Clamp01(growthAge / Mathf.Max(0.1f, growthDuration));
                // A small pressure puff starts the release; most of the gas
                // arrives progressively. Height and rolling motion share this clock.
                float remainder = 1f - progress;
                float pressure = 1f - remainder * remainder * remainder;
                float discharge = Mathf.SmoothStep(0f, 1f, progress);
                float eased = pressure * 0.12f + discharge * 0.88f;
                growthDistance = Mathf.Lerp(voxelSize * 0.6f, propagationBudget, eased);
                growing = progress < 1f;
                // The density texture and its CPU occupancy bounds update at
                // 30 Hz; growth clocks and visual noise remain continuous.
                if(!growing||growthAge>=nextDensityUpdate)
                {UploadDensity();nextDensityUpdate=growthAge+1f/30f;}
            }
            if (infinite || Dissipation < 1f) return;
            finished = true;
            // Retain the collision mask until the final wisps have expired.
            // Las últimas hebras terminan su propia curva de opacidad después del volumen.
            if (destroyOnFinish) Destroy(gameObject, edgeParticles ? 2.2f : 0f);
        }

        private void ReactToExplosion(Vector3 point, float reach, float strength, float carveRadius)
        {
            if (!HasDensity || reach <= 0f || strength <= 0f) return;
            var bounds = new Bounds((GridMinWorld + GridMaxWorld) * 0.5f, GridMaxWorld - GridMinWorld);
            if ((bounds.ClosestPoint(point) - point).sqrMagnitude > reach * reach) return;
            int slot = blastCursor++ % MaxBlasts;
            float holeRadius = carveRadius > 0f ? carveRadius : reach * 0.55f;
            blastSpheres[slot] = new Vector4(point.x, point.y, point.z, Mathf.Max(0.2f, holeRadius));
            blastStates[slot] = new Vector4(Time.timeSinceLevelLoad, strength, reach, 1f);
            blastFlows[slot] = BlastRefillDirection(point);
            Wake.AddExplosion(point, holeRadius, reach, strength, blastFlows[slot]);
            Particles?.BurstFromExplosion(point, reach, strength);
        }

        public int CopyBlasts(Vector4[] spheres, Vector4[] states, int offset = 0, Vector4[] flows = null)
        {
            int activeCount = 0;
            for (int i = 0; i < MaxBlasts; i++)
            {
                bool active = blastStates[i].w > 0f && Time.timeSinceLevelLoad - blastStates[i].x < BlastLifetime;
                spheres[offset + i] = active ? blastSpheres[i] : Vector4.zero;
                states[offset + i] = active ? blastStates[i] : Vector4.zero;
                if (flows != null) flows[offset + i] = active ? blastFlows[i] : Vector4.zero;
                if (active) activeCount++;
            }
            return activeCount;
        }

        public Vector3 ExplosionVelocity(Vector3 point)
        {
            Vector3 velocity = Vector3.zero;
            for (int i = 0; i < MaxBlasts; i++)
            {
                if (blastStates[i].w <= 0f) continue;
                float age = Time.timeSinceLevelLoad - blastStates[i].x;
                if (age < 0f || age > BlastLifetime) continue;
                Vector3 delta = point - (Vector3)blastSpheres[i];
                float distance = delta.magnitude;
                float speed = Mathf.Max(9f, blastSpheres[i].w / 0.5f);
                float front = Mathf.Exp(-Mathf.Abs(distance - age * speed) * 1.4f)
                    * Mathf.Exp(-age * 0.9f);
                float attenuation = Mathf.Clamp01(1f - distance / Mathf.Max(0.1f, blastStates[i].z));
                velocity += delta / Mathf.Max(0.1f, distance) * blastStates[i].y * front * attenuation;
                float refill = Mathf.Sin(Mathf.PI * Mathf.Clamp01((age - 0.85f) / 6.35f));
                velocity += ((Vector3)blastFlows[i] * 0.8f-delta/Mathf.Max(0.2f,distance)*0.62f)
                    * (refill * attenuation);
            }
            return velocity;
        }

        private static Vector3 BlastRefillDirection(Vector3 center)
        {
            float seed = Vector3.Dot(center, new Vector3(0.23f, 0.31f, 0.17f));
            return new Vector3(Mathf.Cos(seed), 0.22f, Mathf.Sin(seed)).normalized;
        }

        // Matches the volumetric clear/refill envelope. Sparse edge particles
        // cannot remain floating in an otherwise evacuated explosion cavity.
        public float ExplosionRemaining(Vector3 point)
        {
            float remaining = 1f;
            for (int i = 0; i < MaxBlasts; i++)
            {
                if (blastStates[i].w <= 0f) continue;
                float age = Time.timeSinceLevelLoad - blastStates[i].x;
                if (age < 0f || age >= BlastLifetime) continue;
                Vector3 center = (Vector3)blastSpheres[i], delta = point - center;
                float radius = blastSpheres[i].w * Mathf.SmoothStep(0f, 1f, age / 0.5f);
                float cleared = 1f - Mathf.SmoothStep(0f, 1f,
                    (delta.magnitude - radius * 0.78f) / (radius * 0.22f + 0.35f));
                float fold = Mathf.Sin(Vector3.Dot(delta, new Vector3(0.72f, 0.24f, -0.65f)) * 1.25f - age * 0.9f)
                    * Mathf.Sin(Vector3.Dot(delta, new Vector3(0.3f, 0.85f, 0.4f)) * 0.85f - age * 0.67f);
                float coordinate = Vector3.Dot(delta, (Vector3)blastFlows[i]) / Mathf.Max(blastSpheres[i].w, 0.2f);
                float front = Mathf.Lerp(-1.35f, 1.35f, Mathf.SmoothStep(0f, 1f, (age - 1.1f) / 5.1f)) + fold * 0.22f;
                float fill = Mathf.SmoothStep(0f, 1f, (front - coordinate + 0.22f) / 0.44f);
                fill = Mathf.Lerp(fill, 1f, Mathf.SmoothStep(0f, 1f, (age - 6.3f) / 0.7f));
                remaining *= 1f - cleared * Mathf.SmoothStep(0f, 1f, age / 0.06f) * (1f - fill);
            }
            return remaining;
        }

        public bool TryGetWispPosition(Vector3 direction, float distance, out Vector3 position)
        {
            position = RenderOrigin;
            if (arrivalDistance == null || scratch == null || finished) return false;
            bool found = false;
            // Buscar el borde conectado en la dirección elegida, incluido el contacto con paredes.
            for (int step = 1; step <= 28; step++)
            {
                Vector3 point = RenderOrigin + direction * (distance * step / 28f);
                Vector3 local = transform.InverseTransformPoint(point) - gridMinLocal;
                var cell = new Vector3Int(Mathf.FloorToInt(local.x / voxelSize),
                    Mathf.FloorToInt(local.y / voxelSize), Mathf.FloorToInt(local.z / voxelSize));
                if (!InGrid(cell)) break;
                int index = IndexOf(cell);
                if (solidCache[index] == 2 || float.IsPositiveInfinity(arrivalDistance[index])) break;
                Vector3 delta = point - RenderOrigin;
                delta.y *= delta.y > 0f ? 1.35f : 0.85f;
                float potential = gpuGrowth != null && growing ? FilteredGrowthPotential(cell,2) : scratch[index];
                float travelDistance = Mathf.Max(0f, 1.25f - potential);
                float surfaceDistance = Mathf.Lerp(Mathf.Min(delta.magnitude / Mathf.Max(ShapeRadius, voxelSize),
                    travelDistance), travelDistance, 0.85f);
                if (surfaceDistance > Mathf.Lerp(0.96f, 0.64f, Dissipation)) break;
                position = point;
                found = true;
            }
            return found;
        }

        // Rare CPU surface probes during an HE burst can reconstruct just the
        // requested 27-cell neighbourhood; no full grid upload or GPU readback.
        private float FilteredGrowthPotential(Vector3Int cell, int axis)
        {
            int index = IndexOf(cell);
            if (axis < 0) return occupancy[index];
            Vector3Int offset = axis == 0 ? Vector3Int.right : axis == 1 ? Vector3Int.up : new Vector3Int(0,0,1);
            float weight = solidCache[index] == 2 ? 0f : 2f;
            float sum = weight > 0 ? FilteredGrowthPotential(cell,axis-1)*weight : 0;
            Vector3Int previous = cell-offset, next = cell+offset;
            if (InGrid(previous) && solidCache[IndexOf(previous)] != 2)
            { sum += FilteredGrowthPotential(previous,axis-1); weight += 1f; }
            if (InGrid(next) && solidCache[IndexOf(next)] != 2)
            { sum += FilteredGrowthPotential(next,axis-1); weight += 1f; }
            return weight > 0 ? sum/weight : 0;
        }

        private void BuildReleaseDelays()
        {
            float seedPhase = Vector3.Dot(RenderOrigin, new Vector3(0.31f, 0.17f, 0.23f));
            // Cache once per detonation; avoid evaluating the procedural fronts
            // with trigonometry for every collision cell on every growth frame.
            for (int i = 0; i < releaseDelays.Length; i++)
            {
                int layer = (i / gridRes.x) % gridRes.y;
                float height = Mathf.Max(0, layer - seedCell.y) * voxelSize;
                int x = i % gridRes.x, z = i / (gridRes.x * gridRes.y);
                float px = (x - seedCell.x) * voxelSize;
                float pz = (z - seedCell.z) * voxelSize;
                // Uneven broad fronts delay neighbouring rolls by different
                // amounts. Positive delays preserve all obstacle connectivity.
                float sector = 0.5f + 0.5f * Mathf.Sin(px * 0.85f + pz * 0.57f + seedPhase)
                    * Mathf.Sin(pz * 0.72f - height * 0.44f + seedPhase * 0.73f);
                releaseDelays[i] = height * 0.85f + 0.48f * sector * Mathf.Min(1f, arrivalDistance[i]);
            }
        }

        private void UploadDensity()
        {
            liveCount = 0;
            // Delay upper rolls while the base spreads. Positive travel delays
            // preserve connectivity and vanish at full expansion.
            float releaseTail = (1f - GrowthProgress) * (1f - GrowthProgress);
            int minX=gridRes.x,minY=gridRes.y,minZ=gridRes.z,maxX=0,maxY=0,maxZ=0;
            int cellX=0,cellY=0,cellZ=0;
            float inverseDistance=1f/Mathf.Max(voxelSize,growthDistance);
            for (int i = 0; i < occupancy.Length; i++)
            {
                // A positive exterior band permits continuous animated lobes.
                float travel = arrivalDistance[i] + releaseDelays[i] * releaseTail;
                occupancy[i] = Mathf.Max(0f, 1.25f - travel * inverseDistance);
                if (occupancy[i] > 0.001f)
                {
                    liveCount++;
                    if(cellX<minX)minX=cellX;if(cellX>maxX)maxX=cellX;
                    if(cellY<minY)minY=cellY;if(cellY>maxY)maxY=cellY;
                    if(cellZ<minZ)minZ=cellZ;if(cellZ>maxZ)maxZ=cellZ;
                }
                if(++cellX == gridRes.x) { cellX=0;if(++cellY == gridRes.y){cellY=0;cellZ++;} }
            }
            // Three cells cover filtering, wall extension, and interpolation.
            activeMin = new Vector3Int(Mathf.Max(0,minX-3),Mathf.Max(0,minY-3),Mathf.Max(0,minZ-3));
            activeMax = new Vector3Int(Mathf.Min(gridRes.x,maxX+4),Mathf.Min(gridRes.y,maxY+4),Mathf.Min(gridRes.z,maxZ+4));
            if (gpuGrowth != null)
            {
                gpuGrowth.Update(Mathf.Max(voxelSize,growthDistance),releaseTail);
                // CPU surface probes are used for edge wisps after expansion.
                if (growing) return;
            }
            // Separable reconstruction removes cell faces without extra GPU voxel reads.
            BlurAxis(occupancy, scratch, 0);
            BlurAxis(scratch, filtered, 1);
            BlurAxis(filtered, scratch, 2);
            if (gpuGrowth != null) return;
            for (int z = 0; z < gridRes.z; z++)
            for (int y = 0; y < gridRes.y; y++)
            for (int x = 0; x < gridRes.x; x++)
            {
                int i = x + y * gridRes.x + z * gridRes.x * gridRes.y;
                float potential = scratch[i];
                if (solidCache[i] == 2)
                {
                    // Extend only the travel channel across one solid texel so
                    // trilinear filtering does not erase gas beside the wall.
                    // The independent clipping channel follows the collider.
                    float sum = 0f;
                    int count = 0;
                    if (x > 0) AddContactPotential(i - 1, ref sum, ref count);
                    if (x + 1 < gridRes.x) AddContactPotential(i + 1, ref sum, ref count);
                    if (y > 0) AddContactPotential(i - gridRes.x, ref sum, ref count);
                    if (y + 1 < gridRes.y) AddContactPotential(i + gridRes.x, ref sum, ref count);
                    int stride = gridRes.x * gridRes.y;
                    if (z > 0) AddContactPotential(i - stride, ref sum, ref count);
                    if (z + 1 < gridRes.z) AddContactPotential(i + stride, ref sum, ref count);
                    potential = count > 0 ? sum / count : 0f;
                }
                float mask = solidCache[i] == 2 ? contactMask[i] :
                    (float.IsPositiveInfinity(arrivalDistance[i]) ? 0f :
                    (solidCache[i] == 3 ? contactMask[i] : 1f));
                fieldPixels[i] = new Vector2(potential, mask);
            }
            densityTexture.SetPixelData(fieldPixels, 0);
            densityTexture.Apply(false, false);
        }
        private void AddContactPotential(int index, ref float sum, ref int count)
        {
            if (solidCache[index] == 2 || float.IsPositiveInfinity(arrivalDistance[index])) return;
            sum += scratch[index];
            count++;
        }
        private void BlurAxis(float[] source, float[] target, int axis)
        {
            int stride = axis == 0 ? 1 : axis == 1 ? gridRes.x : gridRes.x * gridRes.y;
            int length = axis == 0 ? gridRes.x : axis == 1 ? gridRes.y : gridRes.z;
            for (int z = 0; z < gridRes.z; z++)
            for (int y = 0; y < gridRes.y; y++)
            for (int x = 0; x < gridRes.x; x++)
            {
                int index = x + y * gridRes.x + z * gridRes.x * gridRes.y;
                int coordinate = axis == 0 ? x : axis == 1 ? y : z;
                // Reconstruct the gas without averaging solid cells into it:
                // a wall must not create a low-density, brightly lit shell.
                float weight = solidCache[index] == 2 ? 0f : 2f;
                float sum = source[index] * weight;
                if (coordinate > 0 && solidCache[index - stride] != 2)
                { sum += source[index - stride]; weight += 1f; }
                if (coordinate + 1 < length && solidCache[index + stride] != 2)
                { sum += source[index + stride]; weight += 1f; }
                target[index] = weight > 0f ? sum / weight : 0f;
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.6f);
            Vector3 size = (Vector3)gridExtent * (2f * voxelSize);
            Gizmos.DrawWireCube(transform.position + Vector3.up * (size.y * 0.5f - voxelSize * 0.5f), size);
        }
    }
}
