using System.Collections.Generic;
using UnityEngine;

namespace Nyc.Gameplay.Traffic
{
    /// <summary>
    /// Low-poly vehicle meshes built in code so the traffic system runs without the
    /// actor-models port. uv2.x carries a material id consumed by Nyc/VehiclePaint:
    ///   0 body · 1 glass · 2 headlight · 3 taillight · 4 tyre
    /// The silhouettes match the taxi / sedan / SUV / MTA bus proportions of actor-models.js.
    /// </summary>
    public static class VehicleMeshFactory
    {
        public const int Body = 0, Glass = 1, Headlight = 2, Taillight = 3, Tyre = 4;

        public static Mesh CreateCar(bool suv = false, bool taxi = false)
        {
            var b = new MeshBuilder();
            float h = suv ? 1.72f : 1.45f;
            float w = suv ? 1.95f : 1.82f;
            float len = suv ? 4.9f : 4.45f;

            // Body + cabin (the mesh faces -Z, matching CityFrame.HeadingToRotation)
            b.Box(new Vector3(0f, 0.62f, 0f), new Vector3(w, 0.62f, len), Body);
            b.Box(new Vector3(0f, h * 0.78f, 0.18f), new Vector3(w * 0.9f, 0.55f, len * 0.46f), Glass);

            // Wheels
            float halfW = w * 0.5f - 0.1f;
            float wheelZ = len * 0.30f;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    b.Box(new Vector3(sx * halfW, 0.34f, sz * wheelZ), new Vector3(0.24f, 0.64f, 0.64f), Tyre);

            // Lights: front at -Z
            float front = -(len * 0.5f - 0.03f);
            foreach (float sx in new[] { -1f, 1f })
            {
                b.Box(new Vector3(sx * 0.58f, 0.66f, front), new Vector3(0.36f, 0.16f, 0.06f), Headlight);
                b.Box(new Vector3(sx * 0.58f, 0.68f, -front), new Vector3(0.36f, 0.14f, 0.06f), Taillight);
            }

            if (taxi)
            {
                // Detailed NYC Yellow Cab Rooftop Medallion Ad / Sign
                b.Box(new Vector3(0f, h + 0.12f, 0.1f), new Vector3(0.72f, 0.18f, 0.34f), Glass);
                b.Box(new Vector3(0f, h + 0.04f, 0.1f), new Vector3(0.78f, 0.04f, 0.38f), Body);
            }

            return b.ToMesh(suv ? "VehicleSUV" : taxi ? "VehicleTaxi" : "VehicleSedan");
        }

        public static Mesh CreatePoliceCar()
        {
            var b = new MeshBuilder();
            float h = 1.45f;
            float w = 1.84f;
            float len = 4.5f;

            // Patrol cruiser chassis
            b.Box(new Vector3(0f, 0.62f, 0f), new Vector3(w, 0.62f, len), Body);
            b.Box(new Vector3(0f, h * 0.78f, 0.18f), new Vector3(w * 0.9f, 0.55f, len * 0.46f), Glass);

            // Front push bumper (bullbar)
            float front = -(len * 0.5f - 0.03f);
            b.Box(new Vector3(0f, 0.62f, front - 0.12f), new Vector3(0.92f, 0.44f, 0.08f), Body);

            // Rooftop emergency lightbar (red/blue lenses)
            b.Box(new Vector3(0f, h + 0.08f, 0.12f), new Vector3(1.1f, 0.12f, 0.22f), Headlight);
            b.Box(new Vector3(-0.35f, h + 0.08f, 0.12f), new Vector3(0.38f, 0.12f, 0.22f), Taillight);

            // Wheels
            float halfW = w * 0.5f - 0.1f;
            float wheelZ = len * 0.30f;
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sz in new[] { -1f, 1f })
                    b.Box(new Vector3(sx * halfW, 0.34f, sz * wheelZ), new Vector3(0.24f, 0.64f, 0.64f), Tyre);

            // Headlights & Taillights
            foreach (float sx in new[] { -1f, 1f })
            {
                b.Box(new Vector3(sx * 0.58f, 0.66f, front), new Vector3(0.36f, 0.16f, 0.06f), Headlight);
                b.Box(new Vector3(sx * 0.58f, 0.68f, -front), new Vector3(0.36f, 0.14f, 0.06f), Taillight);
            }

            return b.ToMesh("VehiclePolice");
        }

        public static Mesh CreateTruck()
        {
            var b = new MeshBuilder();
            const float w = 2.45f, len = 7.8f;

            // Cab
            b.Box(new Vector3(0f, 1.35f, -2.4f), new Vector3(w * 0.95f, 2.2f, 2.2f), Body);
            b.Box(new Vector3(0f, 1.85f, -2.3f), new Vector3(w * 0.96f, 0.72f, 1.4f), Glass);

            // Large Cargo Box
            b.Box(new Vector3(0f, 1.95f, 0.9f), new Vector3(w, 2.8f, 4.9f), Body);

            // Rear bumper & step
            b.Box(new Vector3(0f, 0.45f, 3.4f), new Vector3(w * 0.9f, 0.2f, 0.35f), Body);

            // Wheels: single front, dual rear
            float halfW = w * 0.5f - 0.1f;
            foreach (float sx in new[] { -1f, 1f })
            {
                b.Box(new Vector3(sx * halfW, 0.44f, -2.4f), new Vector3(0.28f, 0.88f, 0.88f), Tyre);
                b.Box(new Vector3(sx * halfW, 0.44f, 1.2f), new Vector3(0.34f, 0.88f, 0.88f), Tyre);
                b.Box(new Vector3(sx * halfW, 0.44f, 2.4f), new Vector3(0.34f, 0.88f, 0.88f), Tyre);
            }

            // Lights
            float front = -3.52f;
            float rear = len * 0.5f - 0.52f;
            foreach (float sx in new[] { -1f, 1f })
            {
                b.Box(new Vector3(sx * 0.78f, 0.75f, front), new Vector3(0.38f, 0.2f, 0.06f), Headlight);
                b.Box(new Vector3(sx * 0.85f, 0.65f, rear), new Vector3(0.35f, 0.22f, 0.06f), Taillight);
            }

            return b.ToMesh("VehicleTruck");
        }

        public static Mesh CreateBus()
        {
            var b = new MeshBuilder();
            const float w = 2.55f, len = 11.6f;

            b.Box(new Vector3(0f, 1.55f, 0f), new Vector3(w, 2.7f, len), Body);
            b.Box(new Vector3(0f, 2.35f, 0.2f), new Vector3(w + 0.02f, 0.85f, len * 0.86f), Glass);
            b.Box(new Vector3(0f, 3.05f, 0f), new Vector3(w * 0.92f, 0.22f, len * 0.9f), Body);

            // Rooftop AC & ventilation pods
            b.Box(new Vector3(0f, 3.28f, -1.8f), new Vector3(1.7f, 0.35f, 2.6f), Body);
            b.Box(new Vector3(0f, 3.28f, 2.4f), new Vector3(1.7f, 0.35f, 2.6f), Body);

            // Folded front bike rack mounted to front bumper
            float front = -(len * 0.5f - 0.03f);
            b.Box(new Vector3(0f, 0.68f, front - 0.22f), new Vector3(1.6f, 0.35f, 0.28f), Body);

            // Destination roll-sign box above front windscreen
            b.Box(new Vector3(0f, 2.82f, front - 0.02f), new Vector3(1.4f, 0.32f, 0.12f), Glass);

            foreach (float sz in new[] { -1f, 1f })
            {
                foreach (float sx in new[] { -1f, 1f })
                    b.Box(new Vector3(sx * (w * 0.5f - 0.08f), 0.52f, sz * (len * 0.34f)),
                          new Vector3(0.26f, 1.0f, 1.0f), Tyre);
            }

            foreach (float sx in new[] { -1f, 1f })
            {
                b.Box(new Vector3(sx * 0.8f, 0.9f, front), new Vector3(0.4f, 0.22f, 0.06f), Headlight);
                b.Box(new Vector3(sx * 0.8f, 1.0f, -front), new Vector3(0.4f, 0.3f, 0.06f), Taillight);
            }

            return b.ToMesh("VehicleBus");
        }

        // ------------------------------------------------------------------ mesh builder

        sealed class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>(256);
            readonly List<Vector3> _n = new List<Vector3>(256);
            readonly List<Vector2> _uv0 = new List<Vector2>(256);
            readonly List<Vector2> _uv1 = new List<Vector2>(256);
            readonly List<int> _t = new List<int>(512);

            public void Box(Vector3 center, Vector3 size, int materialId)
            {
                Vector3 h = size * 0.5f;
                Vector3 min = center - h;
                Vector3 max = center + h;

                // -X, +X, -Z, +Z, +Y, -Y
                Quad(new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
                     new Vector3(min.x, max.y, max.z), new Vector3(min.x, max.y, min.z), Vector3.left, materialId);
                Quad(new Vector3(max.x, min.y, max.z), new Vector3(max.x, min.y, min.z),
                     new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z), Vector3.right, materialId);
                Quad(new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                     new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z), Vector3.back, materialId);
                Quad(new Vector3(max.x, min.y, max.z), new Vector3(min.x, min.y, max.z),
                     new Vector3(min.x, max.y, max.z), new Vector3(max.x, max.y, max.z), Vector3.forward, materialId);
                Quad(new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
                     new Vector3(max.x, max.y, max.z), new Vector3(max.x, max.y, min.z), Vector3.up, materialId);
            }

            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, int materialId)
            {
                int i0 = Add(p0, n, materialId);
                int i1 = Add(p1, n, materialId);
                int i2 = Add(p2, n, materialId);
                int i3 = Add(p3, n, materialId);

                // Unity front face: dot(cross(b-a, c-a), n) > 0
                Vector3 cross = Vector3.Cross(_v[i1] - _v[i0], _v[i2] - _v[i0]);
                if (Vector3.Dot(cross, n) >= 0f)
                {
                    _t.Add(i0); _t.Add(i1); _t.Add(i2);
                    _t.Add(i0); _t.Add(i2); _t.Add(i3);
                }
                else
                {
                    _t.Add(i0); _t.Add(i2); _t.Add(i1);
                    _t.Add(i0); _t.Add(i3); _t.Add(i2);
                }
            }

            int Add(Vector3 p, Vector3 n, int materialId)
            {
                _v.Add(p);
                _n.Add(n);
                _uv0.Add(new Vector2(p.x, p.z));
                _uv1.Add(new Vector2(materialId, 0f));
                return _v.Count - 1;
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(_v);
                mesh.SetNormals(_n);
                mesh.SetUVs(0, _uv0);
                mesh.SetUVs(1, _uv1);
                mesh.SetTriangles(_t, 0, false);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
