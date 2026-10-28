const fs = require('fs');

let code = fs.readFileSync('unity-migration/Assets/Scripts/Rendering/StreetDetailsSystem.cs', 'utf8');

// 1. Add fields in Expanded Street Furniture
const targetHeader = `        [Header("Expanded Street Furniture")]
        public Material benchMaterial;
        public Material dumpsterMaterial;
        public Material stopSignMaterial;
        public Material trafficConeMaterial;`;

const replaceHeader = `        [Header("Expanded Street Furniture")]
        public Material benchMaterial;
        public Material dumpsterMaterial;
        public Material stopSignMaterial;
        public Material trafficConeMaterial;
        public Material foodCartBodyMaterial;
        public Material foodCartUmbrellaMaterial;
        public Material subwayGrateMaterial;`;

code = code.replace(targetHeader, replaceHeader);

// 2. Add mesh variables and matrix lists
const targetMeshes = `        Mesh _stopSignMesh;
        Mesh _trafficConeMesh;

        // Instanced batches`;

const replaceMeshes = `        Mesh _stopSignMesh;
        Mesh _trafficConeMesh;
        Mesh _foodCartMesh;
        Mesh _subwayGrateMesh;

        // Instanced batches`;

code = code.replace(targetMeshes, replaceMeshes);

const targetLists = `        readonly List<Matrix4x4> _stopSignMatrices = new List<Matrix4x4>(512);
        readonly List<Matrix4x4> _trafficConeMatrices = new List<Matrix4x4>(256);`;

const replaceLists = `        readonly List<Matrix4x4> _stopSignMatrices = new List<Matrix4x4>(512);
        readonly List<Matrix4x4> _trafficConeMatrices = new List<Matrix4x4>(256);
        readonly List<Matrix4x4> _foodCartMatrices = new List<Matrix4x4>(128);
        readonly List<Matrix4x4> _subwayGrateMatrices = new List<Matrix4x4>(256);`;

code = code.replace(targetLists, replaceLists);

// 3. Render batches
const targetRender = `            DrawInstancedBatch(_stopSignMesh, stopSignMaterial, _stopSignMatrices);
            DrawInstancedBatch(_trafficConeMesh, trafficConeMaterial, _trafficConeMatrices);
        }`;

const replaceRender = `            DrawInstancedBatch(_stopSignMesh, stopSignMaterial, _stopSignMatrices);
            DrawInstancedBatch(_trafficConeMesh, trafficConeMaterial, _trafficConeMatrices);
            DrawInstancedBatch(_foodCartMesh, foodCartBodyMaterial, _foodCartMatrices);
            DrawInstancedBatch(_subwayGrateMesh, subwayGrateMaterial, _subwayGrateMatrices);
        }`;

code = code.replace(targetRender, replaceRender);

// 4. Clear lists in Populate
const targetClear = `            _stopSignMatrices.Clear();
            _trafficConeMatrices.Clear();
            _steamPuffs.Clear();`;

const replaceClear = `            _stopSignMatrices.Clear();
            _trafficConeMatrices.Clear();
            _foodCartMatrices.Clear();
            _subwayGrateMatrices.Clear();
            _steamPuffs.Clear();`;

code = code.replace(targetClear, replaceClear);

// 5. Populate logic at end of PopulateStreetFurniture
const targetPopulateEnd = `            Debug.Log($"[StreetDetails] Furniture populated: {_signalMatrices.Count} signals, {_stopSignMatrices.Count} stop signs, {_streetlightMatrices.Count} streetlights, {_hydrantMatrices.Count} hydrants, {_benchMatrices.Count} benches, {_dumpsterMatrices.Count} dumpsters, {_trafficConeMatrices.Count} cones, {_mailboxMatrices.Count} mailboxes, {_trashMatrices.Count} trash, {_subwayMatrices.Count} subways, {_steamOrangeMatrices.Count} steam stacks.");`;

const replacePopulateEnd = `            // 6. NYC Food Carts & Halal/Hot Dog Stands at commercial plazas and Central Park perimeter
            if (geo.Streets != null && geo.Avenues != null)
            {
                foreach (var ave in geo.Avenues)
                {
                    float ax = ave.X;
                    float aveW = ave.Width > 0f ? ave.Width : 30f;
                    string aName = ave.Name ?? "";
                    bool isMajorAve = aName.Contains("5") || aName.Contains("6") || aName.Contains("7") || aName.Contains("Broadway") || aName.Contains("Park");
                    if (!isMajorAve) continue;

                    for (int sIdx = 0; sIdx < geo.Streets.Count; sIdx += 5)
                    {
                        var st = geo.Streets[sIdx];
                        float fcX = ax - aveW * 0.5f - 2.4f;
                        float fcZ = st.Z + 7.5f;
                        if (CitySpatialIndex.IsOnSidewalk(fcX, fcZ))
                            AddMatrix(_foodCartMatrices, fcX, 0.18f, fcZ, 45f, 1f);
                    }
                }

                // Central Park South (59th St) iconic food carts
                float cpZ = CityConfig.StreetZ(59);
                for (float cpx = -600f; cpx <= 200f; cpx += 85f)
                {
                    float southSidewalkZ = cpZ + 6.5f;
                    if (CitySpatialIndex.IsOnSidewalk(cpx, southSidewalkZ))
                        AddMatrix(_foodCartMatrices, cpx, 0.18f, southSidewalkZ, 0f, 1f);
                }
            }

            // 7. NYC Subway Ventilation Grates embedded into sidewalk pavement
            if (geo.Streets != null && geo.Avenues != null)
            {
                foreach (var ave in geo.Avenues)
                {
                    float ax = ave.X;
                    float aveW = ave.Width > 0f ? ave.Width : 30f;
                    for (int sIdx = 1; sIdx < geo.Streets.Count - 1; sIdx += 2)
                    {
                        var s0 = geo.Streets[sIdx];
                        var s1 = geo.Streets[sIdx + 1];
                        float zMid = (s0.Z + s1.Z) * 0.5f;

                        // Grate pair along curb line
                        float gxLeft = ax - aveW * 0.5f - 1.4f;
                        if (CitySpatialIndex.IsOnSidewalk(gxLeft, zMid))
                        {
                            AddMatrix(_subwayGrateMatrices, gxLeft, 0.19f, zMid - 2.2f, 90f, 1f);
                            AddMatrix(_subwayGrateMatrices, gxLeft, 0.19f, zMid + 2.2f, 90f, 1f);
                        }

                        float gxRight = ax + aveW * 0.5f + 1.4f;
                        if (CitySpatialIndex.IsOnSidewalk(gxRight, zMid))
                        {
                            AddMatrix(_subwayGrateMatrices, gxRight, 0.19f, zMid - 2.2f, -90f, 1f);
                            AddMatrix(_subwayGrateMatrices, gxRight, 0.19f, zMid + 2.2f, -90f, 1f);
                        }
                    }
                }
            }

            Debug.Log($"[StreetDetails] Furniture populated: {_signalMatrices.Count} signals, {_stopSignMatrices.Count} stop signs, {_streetlightMatrices.Count} streetlights, {_hydrantMatrices.Count} hydrants, {_benchMatrices.Count} benches, {_dumpsterMatrices.Count} dumpsters, {_trafficConeMatrices.Count} cones, {_foodCartMatrices.Count} food carts, {_subwayGrateMatrices.Count} grates, {_mailboxMatrices.Count} mailboxes, {_trashMatrices.Count} trash, {_subwayMatrices.Count} subways, {_steamOrangeMatrices.Count} steam stacks.");`;

code = code.replace(targetPopulateEnd, replacePopulateEnd);

// 6. Build meshes call
const targetBuildMeshes = `            _stopSignMesh = CreateStopSignMesh();
            _trafficConeMesh = CreateTrafficConeMesh();
        }`;

const replaceBuildMeshes = `            _stopSignMesh = CreateStopSignMesh();
            _trafficConeMesh = CreateTrafficConeMesh();
            _foodCartMesh = CreateFoodCartMesh();
            _subwayGrateMesh = CreateSubwayGrateMesh();
        }`;

code = code.replace(targetBuildMeshes, replaceBuildMeshes);

// 7. Add CreateFoodCartMesh and CreateSubwayGrateMesh before EnsureMaterials
const targetBeforeEnsure = `        void EnsureMaterials()`;

const replaceBeforeEnsure = `        static Mesh CreateFoodCartMesh()
        {
            var m = new Mesh { name = "FoodCart" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Stainless steel main cart body (1.4m x 0.95m x 0.95m)
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.58f, 0), new Vector3(1.4f, 0.85f, 0.95f));
            // Stainless front service counter shelf
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.98f, -0.55f), new Vector3(1.4f, 0.06f, 0.22f));
            // Condiment squeeze bottles (mustard / ketchup)
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-0.35f, 1.12f, -0.55f), new Vector3(0.09f, 0.22f, 0.09f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-0.20f, 1.12f, -0.55f), new Vector3(0.09f, 0.22f, 0.09f));
            // Pretzel warmer glass display box
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0.28f, 1.18f, -0.15f), new Vector3(0.65f, 0.38f, 0.55f));

            // Side wheels (left and right spoke cart wheels)
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-0.74f, 0.30f, 0), new Vector3(0.08f, 0.60f, 0.60f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0.74f, 0.30f, 0), new Vector3(0.08f, 0.60f, 0.60f));
            // Front small swivel caster
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, 0.12f, -0.42f), new Vector3(0.12f, 0.24f, 0.12f));

            // Chrome umbrella pole mast
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0.48f, 1.70f, 0.35f), new Vector3(0.06f, 1.50f, 0.06f));

            // Octagonal umbrella canopy
            int umbrellaSides = 8;
            float umbrellaRadius = 1.15f;
            float umbrellaApexY = 2.65f;
            float umbrellaBaseY = 2.30f;
            Vector3 uApex = new Vector3(0.48f, umbrellaApexY, 0.35f);

            for (int i = 0; i < umbrellaSides; i++)
            {
                float a0 = i * Mathf.PI * 2f / umbrellaSides;
                float a1 = (i + 1) * Mathf.PI * 2f / umbrellaSides;
                Vector3 p0 = new Vector3(0.48f + Mathf.Cos(a0) * umbrellaRadius, umbrellaBaseY, 0.35f + Mathf.Sin(a0) * umbrellaRadius);
                Vector3 p1 = new Vector3(0.48f + Mathf.Cos(a1) * umbrellaRadius, umbrellaBaseY, 0.35f + Mathf.Sin(a1) * umbrellaRadius);
                Vector3 normal = Vector3.Cross(p1 - p0, uApex - p0).normalized;

                int bIdx = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(uApex);
                norms.Add(normal); norms.Add(normal); norms.Add(normal);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0.5f, 1));
                tris.Add(bIdx); tris.Add(bIdx + 1); tris.Add(bIdx + 2);

                // Scalloped fringe flap
                Vector3 flap0 = p0 + Vector3.down * 0.14f;
                Vector3 flap1 = p1 + Vector3.down * 0.14f;
                Vector3 fNorm = Vector3.Cross(flap0 - p0, p1 - p0).normalized;
                int fbIdx = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(flap1); verts.Add(flap0);
                norms.Add(fNorm); norms.Add(fNorm); norms.Add(fNorm); norms.Add(fNorm);
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(1, 1)); uvs.Add(new Vector2(0, 1));
                tris.Add(fbIdx); tris.Add(fbIdx + 1); tris.Add(fbIdx + 2);
                tris.Add(fbIdx); tris.Add(fbIdx + 2); tris.Add(fbIdx + 3);
            }

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh CreateSubwayGrateMesh()
        {
            var m = new Mesh { name = "SubwayGrate" };
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();

            // Cast iron grate outer perimeter frame (1.30m x 2.40m x 0.04m)
            float gw = 1.30f, gl = 2.40f, gh = 0.04f, fw = 0.08f;
            // Border lips
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, gh * 0.5f, -gl * 0.5f + fw * 0.5f), new Vector3(gw, gh, fw));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, gh * 0.5f, gl * 0.5f - fw * 0.5f), new Vector3(gw, gh, fw));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(-gw * 0.5f + fw * 0.5f, gh * 0.5f, 0), new Vector3(fw, gh, gl - fw * 2f));
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(gw * 0.5f - fw * 0.5f, gh * 0.5f, 0), new Vector3(fw, gh, gl - fw * 2f));

            // Center longitudinal spine bar
            AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, gh * 0.5f, 0), new Vector3(0.06f, gh, gl - fw * 2f));

            // Transverse ventilation slats
            for (float sz = -gl * 0.45f; sz <= gl * 0.45f; sz += 0.22f)
            {
                AddBoxSimple(verts, norms, uvs, tris, new Vector3(0, gh * 0.5f, sz), new Vector3(gw - fw * 2f, gh * 0.9f, 0.045f));
            }

            m.SetVertices(verts);
            m.SetNormals(norms);
            SetAllUVs(m, uvs, verts.Count);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        void EnsureMaterials()`;

code = code.replace(targetBeforeEnsure, replaceBeforeEnsure);

// 8. Add EnsureMaterials initialization
const targetEnsureEnd = `            if (trafficConeMaterial == null) trafficConeMaterial = MakeMat("Cone_Orange", new Color(1.0f, 0.45f, 0.05f), 0.50f, 0.1f);
            }
        }`;

const replaceEnsureEnd = `            if (trafficConeMaterial == null) trafficConeMaterial = MakeMat("Cone_Orange", new Color(1.0f, 0.45f, 0.05f), 0.50f, 0.1f);
            }

            if (foodCartBodyMaterial == null) foodCartBodyMaterial = MakeMat("FoodCart_Stainless", new Color(0.85f, 0.87f, 0.90f), 0.85f, 0.85f);
            if (foodCartUmbrellaMaterial == null) foodCartUmbrellaMaterial = MakeMat("FoodCart_Umbrella", new Color(0.96f, 0.78f, 0.14f), 0.35f, 0.05f);
            if (subwayGrateMaterial == null) subwayGrateMaterial = MakeMat("Subway_Grate_CastIron", new Color(0.20f, 0.22f, 0.25f), 0.35f, 0.75f);
        }`;

code = code.replace(targetEnsureEnd, replaceEnsureEnd);

fs.writeFileSync('unity-migration/Assets/Scripts/Rendering/StreetDetailsSystem.cs', code, 'utf8');
console.log('StreetDetailsSystem updated successfully');
