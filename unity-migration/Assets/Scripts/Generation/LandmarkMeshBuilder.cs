using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Nyc.Core;
using Nyc.Data;

namespace Nyc.Generation
{
    /// <summary>
    /// Procedural mesh generator for Manhattan's iconic architectural landmarks.
    /// Full 1:1 port of js/landmarks.js and buildings.js landmark logic.
    /// Builds Empire State, Chrysler, One Vanderbilt, Grand Central / MetLife,
    /// Bryant Park / NYPL, One Times Square, 30 Rock, Citigroup Center,
    /// Flatiron, Seagram, and One World Trade Center with exact setbacks, spires,
    /// and collision boxes.
    /// </summary>
    public sealed class LandmarkMeshBuilder : MonoBehaviour
    {
        public bool buildEmpireState = true;
        public bool buildChrysler = true;
        public bool buildOneVanderbilt = true;
        public bool buildGrandCentralMetLife = true;
        public bool buildBryantParkLibrary = true;
        public bool buildTimesSquare = true;
        public bool buildRockefellerCenter = true;
        public bool buildCitigroupCenter = true;
        public bool buildFlatiron = true;
        public bool buildSeagram = true;
        public bool buildOneWtc = true;
        public bool buildPark432 = true;
        public bool buildVessel = true;
        public bool buildOculus = true;
        public bool buildWoolworth = true;
        public bool buildHudsonYards30 = true;
        public bool buildUnSecretariat = true;
        public bool buildStatueOfLiberty = true;

        [Header("Materials")]
        public Material limestoneTrimMaterial;
        public Material stainlessSteelMaterial;
        public Material chryslerCrownMaterial;
        public Material darkBronzeFrameMaterial;
        public Material copperRoofMaterial;
        public Material beaconMaterial;
        public Material warmFloodMaterial;
        public Material marbleStoneMaterial;
        public Material silverCurtainMaterial;
        public Material bronzeCurtainMaterial;
        public Material parkGrassMaterial;
        public Material waterMaterial;
        public Material signMaterial;

        public readonly List<CollisionBox> CollisionBoxes = new List<CollisionBox>(64);
        public Transform LandmarkRoot { get; private set; }

        public GameObject Build(NycGeoData geo = null, Transform parent = null)
        {
            if (geo == null) geo = CityConfig.Geo;
            EnsureMaterials();

            var rootGo = new GameObject("ManhattanLandmarks");
            rootGo.hideFlags = HideFlags.DontSave;
            if (parent != null) rootGo.transform.SetParent(parent, false);
            else rootGo.transform.SetParent(transform, false);
            LandmarkRoot = rootGo.transform;
            CollisionBoxes.Clear();

            if (buildEmpireState) BuildEmpireState(geo, LandmarkRoot);
            if (buildChrysler) BuildChrysler(geo, LandmarkRoot);
            if (buildOneVanderbilt) BuildOneVanderbilt(geo, LandmarkRoot);
            if (buildGrandCentralMetLife) BuildGrandCentralMetLife(geo, LandmarkRoot);
            if (buildBryantParkLibrary) BuildBryantParkLibrary(geo, LandmarkRoot);
            if (buildTimesSquare) BuildTimesSquare(geo, LandmarkRoot);
            if (buildRockefellerCenter) BuildRockefellerCenter(geo, LandmarkRoot);
            if (buildCitigroupCenter) BuildCitigroupCenter(geo, LandmarkRoot);
            if (buildFlatiron) BuildFlatiron(geo, LandmarkRoot);
            if (buildSeagram) BuildSeagram(geo, LandmarkRoot);
            if (buildOneWtc) BuildOneWtc(geo, LandmarkRoot);
            if (buildPark432) BuildPark432(geo, LandmarkRoot);
            if (buildVessel) BuildVessel(geo, LandmarkRoot);
            if (buildOculus) BuildOculus(geo, LandmarkRoot);
            if (buildWoolworth) BuildWoolworth(geo, LandmarkRoot);
            if (buildHudsonYards30) BuildHudsonYards30(geo, LandmarkRoot);
            if (buildUnSecretariat) BuildUnSecretariat(geo, LandmarkRoot);
            if (buildStatueOfLiberty) BuildStatueOfLiberty(geo, LandmarkRoot);

            return rootGo;
        }

        // =========================================================================
        // 1. EMPIRE STATE BUILDING (5th Ave & 34th St)
        // =========================================================================
        void BuildEmpireState(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("EmpireStateBuilding", root);

            // ESB uses limestone/facade
            int matFacade = ctx.RegisterMaterial(limestoneTrimMaterial);
            int matTrim = ctx.RegisterMaterial(limestoneTrimMaterial);
            int matSteel = ctx.RegisterMaterial(stainlessSteelMaterial);
            int matBronze = ctx.RegisterMaterial(darkBronzeFrameMaterial);
            int matFlood = ctx.RegisterMaterial(warmFloodMaterial);
            int matBeacon = ctx.RegisterMaterial(beaconMaterial);

            // Podium
            ctx.AddBox(new Vector3(0, 11, 0), new Vector3(68, 22, 50), matFacade);
            // 5th Ave Portal
            ctx.AddBox(new Vector3(34.2f, 7, 0), new Vector3(3.2f, 14, 12), matTrim);
            // Tier 1
            ctx.AddBox(new Vector3(0, 39, 0), new Vector3(54, 34, 38), matFacade);
            // Tier 2
            ctx.AddBox(new Vector3(0, 70, 0), new Vector3(44, 28, 32), matFacade);
            // Main Shaft
            ctx.AddBox(new Vector3(0, 140, 0), new Vector3(35, 112, 26), matFacade);
            // Wings
            ctx.AddBox(new Vector3(-18.5f, 132, 0), new Vector3(4.5f, 96, 16), matFacade);
            ctx.AddBox(new Vector3(18.5f, 132, 0), new Vector3(4.5f, 96, 16), matFacade);
            // Tier 3
            ctx.AddBox(new Vector3(0, 208, 0), new Vector3(29, 24, 21), matFacade);
            // Tier 4
            ctx.AddBox(new Vector3(0, 229, 0), new Vector3(23, 18, 17), matFacade);

            // 86th-Floor Observation Deck Terrace & Parapet
            ctx.AddBox(new Vector3(0, 238.4f, 0), new Vector3(23.8f, 1.1f, 17.8f), matTrim);
            ctx.AddBox(new Vector3(0, 239.5f, -8.6f), new Vector3(23.6f, 1.25f, 0.32f), matSteel);
            ctx.AddBox(new Vector3(0, 239.5f, 8.6f), new Vector3(23.6f, 1.25f, 0.32f), matSteel);
            ctx.AddBox(new Vector3(-11.6f, 239.5f, 0), new Vector3(0.32f, 1.25f, 17.6f), matSteel);
            ctx.AddBox(new Vector3(11.6f, 239.5f, 0), new Vector3(0.32f, 1.25f, 17.6f), matSteel);

            // Enclosed glass core
            ctx.AddBox(new Vector3(0, 242.0f, 0), new Vector3(18.6f, 6.4f, 13.6f), matFlood);

            // Mullions
            for (float mx = -7.5f; mx <= 7.5f; mx += 3.0f)
                ctx.AddBox(new Vector3(mx, 242.0f, 0), new Vector3(0.45f, 6.4f, 13.95f), matBronze);
            for (float mz = -5.0f; mz <= 5.0f; mz += 2.5f)
                ctx.AddBox(new Vector3(0, 242.0f, mz), new Vector3(18.95f, 6.4f, 0.45f), matBronze);

            ctx.AddBox(new Vector3(0, 245.2f, 0), new Vector3(19.8f, 0.9f, 14.8f), matTrim);

            // Mooring mast
            ctx.AddCylinder(new Vector3(0, 245, 0), 4.6f, 6.2f, 30, 16, matSteel);
            // 102nd floor observatory ring
            ctx.AddCylinder(new Vector3(0, 271 - 1.6f, 0), 5.1f, 5.1f, 3.2f, 16, matFlood);
            // Dome
            ctx.AddCylinder(new Vector3(0, 276.5f - 4f, 0), 1.8f, 4.6f, 8, 16, matSteel);
            // Antenna spire
            ctx.AddCylinder(new Vector3(0, 280.5f, 0), 0.18f, 1.4f, 44, 12, matSteel);
            // Red beacon
            ctx.AddSphere(new Vector3(0, 325.2f, 0), 0.95f, 8, 8, matBeacon);

            // Accurate stepped collision matching the iconic ESB wedding-cake tiers
            AddCollider(c0, new Vector3(-34, 0, -25), new Vector3(34, 22, 25));    // Podium
            AddCollider(c0, new Vector3(-27, 0, -19), new Vector3(27, 56, 19));    // Tier 1
            AddCollider(c0, new Vector3(-22, 0, -16), new Vector3(22, 84, 16));    // Tier 2
            AddCollider(c0, new Vector3(-16, 0, -11), new Vector3(16, 250, 11));   // Main Shaft
            AddCollider(c0, new Vector3(-4, 0, -4), new Vector3(4, 325, 4));       // Observatory Spire

            FitToSite(ctx, geo, "Empire State Building", c0, 443f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 2. CHRYSLER BUILDING (Lexington Ave & 42nd St)
        // =========================================================================
        void BuildChrysler(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("ChryslerBuilding", root);

            int matShaft = ctx.RegisterMaterial(marbleStoneMaterial);
            int matCrown = ctx.RegisterMaterial(chryslerCrownMaterial);
            int matSteel = ctx.RegisterMaterial(stainlessSteelMaterial);
            int matBeacon = ctx.RegisterMaterial(beaconMaterial);

            // Base & Steps
            ctx.AddBox(new Vector3(0, 12, 0), new Vector3(50, 24, 44), matShaft);
            ctx.AddBox(new Vector3(0, 36, 0), new Vector3(40, 24, 34), matShaft);
            ctx.AddBox(new Vector3(0, 59, 0), new Vector3(31, 22, 28), matShaft);
            ctx.AddBox(new Vector3(0, 121, 0), new Vector3(24, 102, 24), matShaft);

            // 61st floor Silver Eagle Gargoyles
            for (int gx = -1; gx <= 1; gx += 2)
            {
                for (int gz = -1; gz <= 1; gz += 2)
                {
                    Vector3 pos = new Vector3(gx * 12.2f * 1.06f, 169f, gz * 12.2f * 1.06f);
                    ctx.AddCone(pos, 0.95f, 3.8f, 6, matSteel);
                }
            }

            // 7-Tier Terraced Art Deco Sunburst Crown
            const int numTiers = 7;
            float curY = 172f;
            for (int t = 0; t < numTiers; t++)
            {
                float u = t / (float)numTiers;
                float w = 22.8f * (1.0f - u * 0.72f);
                float h = 6.8f * (1.0f - u * 0.16f);

                ctx.AddBox(new Vector3(0, curY + h * 0.5f, 0), new Vector3(w, h, w * 0.82f), matCrown);
                ctx.AddBox(new Vector3(0, curY + h * 0.5f, 0), new Vector3(w * 0.82f, h, w), matCrown);
                ctx.AddBox(new Vector3(0, curY + h, 0), new Vector3(w + 0.7f, 0.55f, w + 0.7f), matSteel);
                curY += h;
            }

            // Needle spire
            ctx.AddCylinder(new Vector3(0, curY, 0), 0.12f, 1.35f, 38f, 12, matSteel);
            // Beacon
            ctx.AddSphere(new Vector3(0, curY + 37.5f, 0), 0.8f, 8, 8, matBeacon);

            // Stepped collision for Chrysler Building matching its distinct tiers
            AddCollider(c0, new Vector3(-25, 0, -22), new Vector3(25, 36, 22));    // Base podium
            AddCollider(c0, new Vector3(-18, 0, -18), new Vector3(18, 78, 18));    // Lower setback
            AddCollider(c0, new Vector3(-12, 0, -12), new Vector3(12, 215, 12));   // Main tower
            AddCollider(c0, new Vector3(-8, 0, -8), new Vector3(8, 260, 8));       // Crown
            AddCollider(c0, new Vector3(-2, 0, -2), new Vector3(2, 319, 2));       // Spire needle

            FitToSite(ctx, geo, "Chrysler Building", c0, 319f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 3. ONE VANDERBILT (Vanderbilt Ave & 42nd St)
        // =========================================================================
        void BuildOneVanderbilt(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("OneVanderbilt", root);

            int matCurtain = ctx.RegisterMaterial(silverCurtainMaterial);
            int matFlood = ctx.RegisterMaterial(warmFloodMaterial);
            int matSteel = ctx.RegisterMaterial(stainlessSteelMaterial);

            // 5 stepped tapering segments
            var segs = new[]
            {
                new { w = 40f, d = 38f, h = 68f, y = 0f, ox = 0f, oz = 0f },
                new { w = 35f, d = 33f, h = 72f, y = 66f, ox = 1.0f, oz = -1.0f },
                new { w = 29f, d = 28f, h = 62f, y = 136f, ox = -1.0f, oz = 1.0f },
                new { w = 23f, d = 22f, h = 42f, y = 196f, ox = 0.5f, oz = 0.5f },
                new { w = 15f, d = 15f, h = 24f, y = 236f, ox = 0f, oz = 0f }
            };
            foreach (var s in segs)
            {
                ctx.AddBox(new Vector3(s.ox, s.y + s.h * 0.5f, s.oz), new Vector3(s.w, s.h, s.d), matCurtain);
            }

            // SUMMIT cantilevered sky-box & fins
            ctx.AddBox(new Vector3(0.5f, 206f, 0.5f), new Vector3(24.6f, 8.2f, 23.2f), matFlood);
            for (float fx = -11f; fx <= 11f; fx += 2.75f)
                ctx.AddBox(new Vector3(0.5f + fx, 206f, 0.5f), new Vector3(0.42f, 8.6f, 23.8f), matSteel);
            for (float fz = -10f; fz <= 10f; fz += 2.5f)
                ctx.AddBox(new Vector3(0.5f, 206f, 0.5f + fz), new Vector3(25.2f, 8.6f, 0.42f), matSteel);

            // Spire
            ctx.AddCylinder(new Vector3(0, 260f, 0), 0.18f, 0.95f, 30f, 8, matFlood);

            // Tapered collision for One Vanderbilt matching its 5 setback blocks
            AddCollider(c0, new Vector3(-20, 0, -19), new Vector3(20, 68, 19));    // Base segment
            AddCollider(c0, new Vector3(-17.5f, 0, -16.5f), new Vector3(17.5f, 138, 16.5f)); // Tier 2
            AddCollider(c0, new Vector3(-14.5f, 0, -14), new Vector3(14.5f, 198, 14));       // Tier 3
            AddCollider(c0, new Vector3(-11.5f, 0, -11), new Vector3(11.5f, 238, 11));       // Tier 4
            AddCollider(c0, new Vector3(-7.5f, 0, -7.5f), new Vector3(7.5f, 260, 7.5f));     // Crown
            AddCollider(c0, new Vector3(-2, 0, -2), new Vector3(2, 290, 2));                 // Spire

            FitToSite(ctx, geo, "One Vanderbilt", c0, 427f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 4. GRAND CENTRAL TERMINAL, PARK AVE VIADUCT & METLIFE BUILDING (42nd St & Park Ave)
        // =========================================================================
        void BuildGrandCentralMetLife(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("GrandCentralMetLife", root);

            int matMarble = ctx.RegisterMaterial(marbleStoneMaterial);
            int matCopper = ctx.RegisterMaterial(copperRoofMaterial);
            int matFlood = ctx.RegisterMaterial(warmFloodMaterial);
            int matBronze = ctx.RegisterMaterial(darkBronzeFrameMaterial);
            int matTrim = ctx.RegisterMaterial(limestoneTrimMaterial);
            int matMetLife = ctx.RegisterMaterial(bronzeCurtainMaterial);
            int matSign = ctx.RegisterMaterial(signMaterial);

            // GCT Main Concourse Body & Copper Roof
            ctx.AddBox(new Vector3(0, 12, 14), new Vector3(62, 24, 28), matMarble);
            ctx.AddBox(new Vector3(0, 25.5f, 14), new Vector3(58, 3.5f, 24), matCopper);

            // 3 Monumental Beaux-Arts Arched Windows on 42nd St
            float[] archXs = new[] { -17f, 0f, 17f };
            foreach (float wx in archXs)
            {
                ctx.AddBox(new Vector3(wx, 11.8f, 28.05f), new Vector3(9.2f, 14.2f, 0.8f), matFlood);
                ctx.AddCylinder(new Vector3(wx, 18.9f, 28.05f), 4.6f, 4.6f, 0.8f, 16, matFlood, rotX90: true);

                // Mullions
                foreach (float mx in new[] { -2.3f, 0f, 2.3f })
                    ctx.AddBox(new Vector3(wx + mx, 13.0f, 28.1f), new Vector3(0.32f, 16.5f, 0.95f), matBronze);
                foreach (float my in new[] { 8.5f, 13.0f, 17.5f })
                    ctx.AddBox(new Vector3(wx, my, 28.1f), new Vector3(9.2f, 0.32f, 0.95f), matBronze);
            }

            // Columns flanking the arches
            float[] colXs = new[] { -25.5f, -23.0f, -9.8f, -7.2f, 7.2f, 9.8f, 23.0f, 25.5f };
            foreach (float cx in colXs)
                ctx.AddCylinder(new Vector3(cx, 3.1f, 28.45f), 0.62f, 0.70f, 18.2f, 12, matTrim);

            // Classical cornice & clock pediment
            ctx.AddBox(new Vector3(0, 22.4f, 28.2f), new Vector3(58, 1.8f, 2.2f), matTrim);
            ctx.AddBox(new Vector3(0, 25.4f, 27.6f), new Vector3(16, 4.8f, 2.2f), matTrim);
            ctx.AddBox(new Vector3(0, 28.6f, 27.6f), new Vector3(9.5f, 2.4f, 2.2f), matTrim);
            ctx.AddCylinder(new Vector3(0, 25.8f, 28.55f), 2.55f, 2.55f, 0.75f, 20, matBronze, rotX90: true);
            ctx.AddCylinder(new Vector3(0, 25.8f, 28.6f), 2.05f, 2.05f, 0.85f, 20, matFlood, rotX90: true);

            // Park Ave Viaduct Deck & Piers
            ctx.AddBox(new Vector3(-55, 9.5f, 38), new Vector3(11, 1.8f, 76), matTrim);
            foreach (float vz in new[] { 8f, 28f, 52f, 70f })
                ctx.AddBox(new Vector3(-55, 4.5f, vz), new Vector3(9.5f, 9.0f, 3.5f), matTrim);

            // MetLife Building Center & Angled Wings
            ctx.AddBox(new Vector3(0, 99, -12), new Vector3(44, 198, 24), matMetLife);
            ctx.AddBox(new Vector3(-24, 99, -12), new Vector3(16, 198, 16), matMetLife, rotY45: true);
            ctx.AddBox(new Vector3(24, 99, -12), new Vector3(16, 198, 16), matMetLife, rotY45: true);

            // MetLife sign band
            ctx.AddBox(new Vector3(0, 191, -12), new Vector3(28.5f, 6.8f, 25.4f), matSign);

            // Separate MetLife tower from Grand Central Terminal so the station rooftop is flyable/walkable
            AddCollider(c0, new Vector3(-34, 0, -26), new Vector3(34, 198, 2));    // MetLife tower (198m)
            AddCollider(c0, new Vector3(-30, 0, 2), new Vector3(30, 28, 44));       // Grand Central Terminal (28m)

            FitToSiteUnion(ctx, geo, new[] { "Grand Central Terminal", "MetLife Building" }, c0, 246f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 5. BRYANT PARK & NY PUBLIC LIBRARY (5th Ave & 42nd St)
        // =========================================================================
        void BuildBryantParkLibrary(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("BryantParkLibrary", root);

            int matMarble = ctx.RegisterMaterial(marbleStoneMaterial);
            int matCopper = ctx.RegisterMaterial(copperRoofMaterial);
            int matBronze = ctx.RegisterMaterial(darkBronzeFrameMaterial);
            int matFlood = ctx.RegisterMaterial(warmFloodMaterial);
            int matTrim = ctx.RegisterMaterial(limestoneTrimMaterial);
            int matGrass = ctx.RegisterMaterial(parkGrassMaterial);
            int matWater = ctx.RegisterMaterial(waterMaterial);

            // Library Main Body & Copper Roof
            ctx.AddBox(new Vector3(20, 10.5f, 0), new Vector3(26, 21, 56), matMarble);
            ctx.AddBox(new Vector3(20, 22.5f, 0), new Vector3(22, 3.2f, 50), matCopper);

            // Colonnaded Portico on 5th Ave
            ctx.AddBox(new Vector3(32.2f, 9.25f, 0), new Vector3(2.6f, 18.5f, 22), matMarble);
            foreach (float pz in new[] { -6.4f, 0f, 6.4f })
            {
                ctx.AddBox(new Vector3(33.3f, 8.2f, pz), new Vector3(1.1f, 11.8f, 4.2f), matBronze);
                ctx.AddBox(new Vector3(33.35f, 8.0f, pz), new Vector3(1.25f, 10.6f, 3.2f), matFlood);
            }
            foreach (float cz in new[] { -9.6f, -5.8f, -2.2f, 2.2f, 5.8f, 9.6f })
                ctx.AddCylinder(new Vector3(34.8f, 2.4f, cz), 0.58f, 0.66f, 14.2f, 12, matTrim);

            ctx.AddBox(new Vector3(33.6f, 17.6f, 0), new Vector3(4.2f, 2.2f, 22.4f), matTrim);
            ctx.AddBox(new Vector3(33.4f, 19.6f, 0), new Vector3(3.6f, 2.0f, 16.0f), matTrim);
            ctx.AddBox(new Vector3(35.5f, 1.2f, 0), new Vector3(5.5f, 2.4f, 18), matTrim);

            // Library Lions & Pedestals
            foreach (float lz in new[] { -10.5f, 10.5f })
            {
                ctx.AddBox(new Vector3(35.8f, 1.1f, lz), new Vector3(3.2f, 2.2f, 2.2f), matTrim);
                ctx.AddBox(new Vector3(36.0f, 2.8f, lz), new Vector3(2.6f, 1.4f, 1.2f), matTrim);
            }

            // Bryant Park Retaining Wall & Sunken Lawn
            ctx.AddBox(new Vector3(-14, 0.42f, 0), new Vector3(46, 0.85f, 60), matTrim);
            ctx.AddBox(new Vector3(-13, 0.92f, 0), new Vector3(38, 0.28f, 48), matGrass);

            // Lowell Memorial Fountain
            ctx.AddCylinder(new Vector3(-29, 0.35f, 0), 5.8f, 6.2f, 1.1f, 20, matTrim);
            ctx.AddCylinder(new Vector3(-29, 1.34f, 0), 5.2f, 5.2f, 0.16f, 20, matWater);

            // Carousel
            ctx.AddCylinder(new Vector3(-12, 0.8f, 21), 3.8f, 4.0f, 2.8f, 14, matTrim);
            ctx.AddCone(new Vector3(-12, 3.5f, 21), 4.4f, 2.2f, 14, matBronze);

            Vector3 bMin = new Vector3(6, 0, -28);
            Vector3 bMax = new Vector3(35, 24, 28);
            AddCollider(c0, bMin, bMax);

            FitToParkBlock(ctx, geo, "Bryant Park", c0, 30f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 6. ONE TIMES SQUARE & BROADWAY BILLBOARDS (Times Square)
        // =========================================================================
        void BuildTimesSquare(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("OneTimesSquare", root);

            int matDark = ctx.RegisterMaterial(bronzeCurtainMaterial);
            int matSteel = ctx.RegisterMaterial(stainlessSteelMaterial);
            int matBall = ctx.RegisterMaterial(beaconMaterial);

            // Times Square's web landmark uses a stack of saturated emissive screens rather
            // than one neutral sign material. Keep the panels in the same combined mesh so the
            // port gains the visual cue without adding per-screen GameObjects or draw calls.
            var screenMaterials = new[]
            {
                MakeBillboardMaterial(0, new Color(0.42f, 0.015f, 0.025f), new Color(2.8f, 0.08f, 0.04f)),
                MakeBillboardMaterial(1, new Color(0.015f, 0.08f, 0.52f), new Color(0.04f, 0.35f, 2.6f)),
                MakeBillboardMaterial(2, new Color(0.55f, 0.015f, 0.04f), new Color(2.5f, 0.05f, 0.05f)),
                MakeBillboardMaterial(3, new Color(0.15f, 0.12f, 0.08f), new Color(2.6f, 0.72f, 0.04f)),
                MakeBillboardMaterial(4, new Color(0.01f, 0.40f, 0.58f), new Color(0.02f, 1.7f, 2.4f)),
                MakeBillboardMaterial(5, new Color(0.55f, 0.015f, 0.34f), new Color(2.2f, 0.03f, 1.4f))
            };

            float otsW = 18f, otsD = 26f, otsH = 118f, otsX = -16f;
            ctx.AddBox(new Vector3(otsX, otsH * 0.5f, 14), new Vector3(otsW, otsH, otsD), matDark);

            // 5 tiers of advertising screens
            for (int s = 0; s < 5; s++)
            {
                float panelH = 17.5f;
                float panelY = 14f + s * 19.5f;
                int matScreen = ctx.RegisterMaterial(screenMaterials[s % screenMaterials.Length]);
                // West screen
                ctx.AddBox(new Vector3(otsX - otsW * 0.5f - 0.25f, panelY, 14), new Vector3(0.5f, panelH, otsD - 2), matScreen);
                // South screen
                ctx.AddBox(new Vector3(otsX, panelY, 14 + otsD * 0.5f + 0.25f), new Vector3(otsW - 2, panelH, 0.5f), matScreen);
                // North screen
                ctx.AddBox(new Vector3(otsX, panelY, 14 - otsD * 0.5f - 0.25f), new Vector3(otsW - 2, panelH, 0.5f), matScreen);
            }

            // Broadway bow-tie frontage: the web scene carries large ad walls on the blocks
            // around One Times Square. These four shallow panels reproduce that visible mass
            // while remaining part of the same combined landmark mesh.
            int frontageA = ctx.RegisterMaterial(screenMaterials[1]);
            int frontageB = ctx.RegisterMaterial(screenMaterials[2]);
            int frontageC = ctx.RegisterMaterial(screenMaterials[3]);
            int frontageD = ctx.RegisterMaterial(screenMaterials[4]);
            ctx.AddBox(new Vector3(-43f, 31f, 10f), new Vector3(0.45f, 42f, 54f), frontageA);
            ctx.AddBox(new Vector3(18f, 34f, 10f), new Vector3(0.45f, 48f, 54f), frontageB);
            ctx.AddBox(new Vector3(-12f, 28f, -24f), new Vector3(54f, 36f, 0.45f), frontageC);
            ctx.AddBox(new Vector3(-12f, 40f, 48f), new Vector3(54f, 56f, 0.45f), frontageD);

            // Ball mast & NYE ball
            ctx.AddCylinder(new Vector3(otsX, otsH, 14), 0.35f, 0.5f, 26f, 8, matSteel);
            ctx.AddSphere(new Vector3(otsX, otsH + 22, 14), 2.8f, 14, 14, matBall);

            Vector3 bMin = new Vector3(otsX - 9, 0, 1);
            Vector3 bMax = new Vector3(otsX + 9, otsH, 27);
            AddCollider(c0, bMin, bMax);

            FitToSite(ctx, geo, "One Times Square", c0, 120f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 7. ROCKEFELLER CENTER / 30 ROCK (5th Ave & 50th St)
        // =========================================================================
        void BuildRockefellerCenter(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("RockefellerCenter", root);

            int matRock = ctx.RegisterMaterial(limestoneTrimMaterial);
            int matGold = ctx.RegisterMaterial(warmFloodMaterial);

            // Main stepped slab
            ctx.AddBox(new Vector3(-14, 106, 0), new Vector3(24, 212, 56), matRock);
            ctx.AddBox(new Vector3(-14, 67.5f, 0), new Vector3(34, 135, 44), matRock);
            ctx.AddBox(new Vector3(18, 14, -18), new Vector3(28, 28, 18), matRock);
            ctx.AddBox(new Vector3(18, 14, 18), new Vector3(28, 28, 18), matRock);

            // Prometheus fountain statue
            ctx.AddCylinder(new Vector3(1, 3.8f, 0), 2.6f, 2.6f, 0.9f, 16, matGold, rotX90: true);

            // Stepped collision for 30 Rock matching stepped tower massing
            AddCollider(c0, new Vector3(-32, 0, -28), new Vector3(4, 70, 28));     // Base stepped wings
            AddCollider(c0, new Vector3(-26, 0, -26), new Vector3(-2, 212, 26));   // Central slender high-rise tower

            FitToSite(ctx, geo, "30 Rockefeller Plaza", c0, 259f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 8. CITIGROUP CENTER (Lexington Ave & 53rd St)
        // =========================================================================
        void BuildCitigroupCenter(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("CitigroupCenter", root);

            int matSteel = ctx.RegisterMaterial(stainlessSteelMaterial);
            int matCurtain = ctx.RegisterMaterial(silverCurtainMaterial);

            // 4 base stilts & central core
            ctx.AddBox(new Vector3(-18, 15, 0), new Vector3(7.5f, 30, 7.5f), matSteel);
            ctx.AddBox(new Vector3(18, 15, 0), new Vector3(7.5f, 30, 7.5f), matSteel);
            ctx.AddBox(new Vector3(0, 15, -18), new Vector3(7.5f, 30, 7.5f), matSteel);
            ctx.AddBox(new Vector3(0, 15, 18), new Vector3(7.5f, 30, 7.5f), matSteel);
            ctx.AddBox(new Vector3(0, 15, 0), new Vector3(16, 30, 16), matSteel);

            // Tower shaft
            ctx.AddBox(new Vector3(0, 102.5f, 0), new Vector3(42, 145, 42), matCurtain);

            // 45-degree sloped roof crown
            ctx.AddWedge(new Vector3(0, 175, 0), new Vector3(42, 34, 42), matSteel);

            // Citigroup Center: central core at plaza level, full tower above stilts
            AddCollider(c0, new Vector3(-8, 0, -8), new Vector3(8, 30, 8));        // Central elevator core on plaza
            AddCollider(c0, new Vector3(-21, 0, -21), new Vector3(21, 209, 21));   // Main tower block

            FitToSite(ctx, geo, "Citigroup Center", c0, 279f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 9. FLATIRON BUILDING (5th Ave / Broadway & 23rd St)
        // =========================================================================
        void BuildFlatiron(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("FlatironBuilding", root);

            int matMarble = ctx.RegisterMaterial(marbleStoneMaterial);
            int matTrim = ctx.RegisterMaterial(limestoneTrimMaterial);

            // Triangular extruded footprint
            var triPoints = new[]
            {
                new Vector2(0, -26f),
                new Vector2(11f, 24f),
                new Vector2(-13f, 24f)
            };
            ctx.AddExtrudedPolygon(triPoints, 0, 88f, matMarble);
            // Overhanging cornice at top
            var cornicePoints = new[]
            {
                new Vector2(0, -27.5f),
                new Vector2(12f, 25.5f),
                new Vector2(-14f, 25.5f)
            };
            ctx.AddExtrudedPolygon(cornicePoints, 88f, 2.8f, matTrim);

            // Tapered wedge collision matching triangular geometry (prevents clipping on 5th Ave and Broadway)
            AddCollider(c0, new Vector3(-3.8f, 0, -26f), new Vector3(3.8f, 90f, -10f)); // Prow (narrow tip)
            AddCollider(c0, new Vector3(-8f, 0, -10f), new Vector3(7.2f, 90f, 8f));     // Midsection
            AddCollider(c0, new Vector3(-13f, 0, 8f), new Vector3(11f, 90f, 24f));      // Wide southern base

            FitToSite(ctx, geo, "Flatiron Building", c0, 87f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 10. SEAGRAM BUILDING (Park Ave & 53rd St)
        // =========================================================================
        void BuildSeagram(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("SeagramBuilding", root);

            int matBronze = ctx.RegisterMaterial(bronzeCurtainMaterial);
            int matPool = ctx.RegisterMaterial(waterMaterial);

            // Modernist tower & rear bustle
            ctx.AddBox(new Vector3(8, 81, 0), new Vector3(42, 162, 36), matBronze);
            ctx.AddBox(new Vector3(22, 46, 0), new Vector3(24, 92, 26), matBronze);

            // Twin reflecting pools on Park Ave plaza
            ctx.AddBox(new Vector3(-22, 0.32f, -14), new Vector3(14, 0.55f, 12), matPool);
            ctx.AddBox(new Vector3(-22, 0.32f, 14), new Vector3(14, 0.55f, 12), matPool);

            Vector3 bMin = new Vector3(-13, 0, -18);
            Vector3 bMax = new Vector3(34, 162, 18);
            AddCollider(c0, bMin, bMax);

            FitToSite(ctx, geo, "Seagram Building", c0, 157f, offset: new Vector2(-14f, 0f));
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 11. ONE WORLD TRADE CENTER (Lower Manhattan)
        // =========================================================================
        void BuildOneWtc(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("OneWorldTradeCenter", root);

            int matSilver = ctx.RegisterMaterial(silverCurtainMaterial);
            int matSteel = ctx.RegisterMaterial(stainlessSteelMaterial);
            int matFlood = ctx.RegisterMaterial(warmFloodMaterial);

            // 52m-tall Square Podium Base with Louver Fins
            ctx.AddBox(new Vector3(0, 26, 0), new Vector3(48, 52, 48), matSilver);
            for (float f = -20; f <= 20; f += 5)
            {
                ctx.AddBox(new Vector3(f, 28, 0), new Vector3(0.45f, 44, 49.2f), matSteel);
                ctx.AddBox(new Vector3(0, 28, f), new Vector3(49.2f, 44, 0.45f), matSteel);
            }

            // Double-height warm illuminated lobby portals
            ctx.AddBox(new Vector3(0, 4.8f, 0), new Vector3(24, 9.5f, 48.6f), matFlood);
            ctx.AddBox(new Vector3(0, 4.8f, 0), new Vector3(48.6f, 9.5f, 24), matFlood);

            // Podium setback collar
            ctx.AddBox(new Vector3(0, 52.0f, 0), new Vector3(49.6f, 1.8f, 49.6f), matSteel);

            // Chamfered Square Antiprism Shaft (8 triangular glass facets)
            ctx.AddAntiprismShaft(new Vector3(0, 52 + 117.5f, 0), 23.5f, 33.9f, 235f, 45f, matSilver);
            ctx.AddAntiprismShaft(new Vector3(0, 52 + 117.5f, 0), 33.9f, 23.5f, 235f, 0f, matSilver);

            // Parapet Ring
            ctx.AddCylinder(new Vector3(0, 287f, 0), 19f, 19f, 8f, 16, matFlood);

            // Spire Mast
            ctx.AddCylinder(new Vector3(0, 295f, 0), 0.22f, 2.2f, 68f, 12, matSteel);

            // Beacon
            ctx.AddSphere(new Vector3(0, 364f, 0), 1.4f, 10, 10, matFlood);

            // Stepped collision for One WTC: solid tower shaft vs slender telecommunication spire
            AddCollider(c0, new Vector3(-25, 0, -25), new Vector3(25, 295, 25)); // Main tower block & parapet
            AddCollider(c0, new Vector3(-3.5f, 0, -3.5f), new Vector3(3.5f, 364, 3.5f)); // Spire mast & beacon

            FitToSite(ctx, geo, "One World Trade Center", c0, 541f, horizontal: 1.27f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 12. 432 PARK AVENUE (Billionaires' Row Supertall Pencil Tower)
        // =========================================================================
        void BuildPark432(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("Park432", root);

            int matConcrete = ctx.RegisterMaterial(marbleStoneMaterial);
            int matGlass = ctx.RegisterMaterial(darkBronzeFrameMaterial);
            int matSteel = ctx.RegisterMaterial(stainlessSteelMaterial);
            int matBeacon = ctx.RegisterMaterial(beaconMaterial);

            // Ground floor retail podium & entry
            ctx.AddBox(new Vector3(0, 7.5f, 0), new Vector3(29f, 15f, 29f), matConcrete);
            ctx.AddBox(new Vector3(0, 4.5f, 14.6f), new Vector3(12f, 9f, 0.6f), matGlass);

            // 6 stacked residential modules separated by 2-story open wind drums
            float yCur = 15f;
            const int modules = 6;
            const float modH = 58f;
            const float drumH = 9f;
            const float towerW = 28f;

            for (int m = 0; m < modules; m++)
            {
                // Core / facade shaft
                ctx.AddBox(new Vector3(0, yCur + modH * 0.5f, 0), new Vector3(towerW, modH, towerW), matConcrete);
                // Dark recessed window bays
                ctx.AddBox(new Vector3(0, yCur + modH * 0.5f, 0), new Vector3(towerW + 0.15f, modH - 1.5f, towerW * 0.88f), matGlass);
                ctx.AddBox(new Vector3(0, yCur + modH * 0.5f, 0), new Vector3(towerW * 0.88f, modH - 1.5f, towerW + 0.15f), matGlass);
                yCur += modH;

                if (m < modules - 1)
                {
                    // Open-air cylindrical wind pass-through drum
                    ctx.AddCylinder(new Vector3(0, yCur, 0), 7.5f, 7.5f, drumH, 16, matSteel);
                    // 4 structural perimeter corner columns
                    for (int sx = -1; sx <= 1; sx += 2)
                    {
                        for (int sz = -1; sz <= 1; sz += 2)
                        {
                            ctx.AddBox(new Vector3(sx * 12f, yCur + drumH * 0.5f, sz * 12f),
                                       new Vector3(3.2f, drumH, 3.2f), matConcrete);
                        }
                    }
                    yCur += drumH;
                }
            }

            // Top crown parapet & elevator penthouse
            ctx.AddBox(new Vector3(0, yCur + 5f, 0), new Vector3(towerW + 0.4f, 10f, towerW + 0.4f), matConcrete);
            ctx.AddCylinder(new Vector3(0, yCur + 10f, 0), 0.25f, 0.8f, 16f, 8, matSteel);
            ctx.AddSphere(new Vector3(0, yCur + 26f, 0), 1.1f, 8, 8, matBeacon);

            Vector3 bMin = new Vector3(-14.5f, 0, -14.5f);
            Vector3 bMax = new Vector3(14.5f, 426f, 14.5f);
            AddCollider(c0, bMin, bMax);

            FitToSite(ctx, geo, "432 Park Avenue", c0, 426f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 13. THE VESSEL (Hudson Yards Honeycomb Spiral Structure)
        // =========================================================================
        void BuildVessel(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("TheVessel", root);

            int matCopper = ctx.RegisterMaterial(copperRoofMaterial);
            int matBronze = ctx.RegisterMaterial(darkBronzeFrameMaterial);
            int matSteel = ctx.RegisterMaterial(stainlessSteelMaterial);

            // Inverted truncated cone composed of 16 interconnected tiers
            const int tiers = 16;
            const float totalH = 46f;
            const float rBase = 7.5f;
            const float rTop = 22.5f;
            const int sides = 16;

            for (int t = 0; t < tiers; t++)
            {
                float u0 = t / (float)tiers;
                float u1 = (t + 1) / (float)tiers;
                float y0 = u0 * totalH;
                float y1 = u1 * totalH;
                float r0 = Mathf.Lerp(rBase, rTop, u0);
                float r1 = Mathf.Lerp(rBase, rTop, u1);

                // Outer polygonal platform ring
                for (int s = 0; s < sides; s++)
                {
                    float a0 = (s / (float)sides) * Mathf.PI * 2f;
                    float a1 = ((s + 1) / (float)sides) * Mathf.PI * 2f;

                    Vector3 p0 = new Vector3(Mathf.Cos(a0) * r0, y0, Mathf.Sin(a0) * r0);
                    Vector3 p1 = new Vector3(Mathf.Cos(a1) * r0, y0, Mathf.Sin(a1) * r0);
                    Vector3 p2 = new Vector3(Mathf.Cos(a1) * r1, y1, Mathf.Sin(a1) * r1);
                    Vector3 p3 = new Vector3(Mathf.Cos(a0) * r1, y1, Mathf.Sin(a0) * r1);

                    Vector3 mid = (p0 + p1 + p2 + p3) * 0.25f;
                    Vector3 sz = new Vector3(Vector3.Distance(p0, p1), 1.2f, 2.2f);
                    ctx.AddBox(mid, sz, matCopper);

                    // Cross stairs
                    if ((s + t) % 2 == 0)
                    {
                        Vector3 stairMid = (p0 + p2) * 0.5f;
                        ctx.AddBox(stairMid, new Vector3(1.4f, y1 - y0, 1.4f), matBronze);
                    }
                }
            }

            // Central base plaza pedestal
            ctx.AddCylinder(new Vector3(0, 0, 0), rBase, rBase * 1.1f, 1.2f, 16, matSteel);

            Vector3 bMin = new Vector3(-24f, 0, -24f);
            Vector3 bMax = new Vector3(24f, 46f, 24f);
            AddCollider(c0, bMin, bMax);

            FitToSite(ctx, geo, "The Shed", c0, 46f, offset: new Vector2(40f, -30f));
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 14. THE OCULUS (WTC Transportation Hub)
        // =========================================================================
        void BuildOculus(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("TheOculus", root);

            int matRibs = ctx.RegisterMaterial(stainlessSteelMaterial);
            int matGlass = ctx.RegisterMaterial(warmFloodMaterial);
            int matFloor = ctx.RegisterMaterial(marbleStoneMaterial);

            // Elliptical floor slab
            ctx.AddCylinder(new Vector3(0, 0, 0), 16f, 16f, 2.5f, 24, matFloor);

            // Central glazed elliptical mezzanine
            ctx.AddBox(new Vector3(0, 6f, 0), new Vector3(24f, 8f, 70f), matGlass);

            // Longitudinal skylight spine ("Wedge of Light")
            ctx.AddBox(new Vector3(0, 14f, 0), new Vector3(3.2f, 2.8f, 78f), matRibs);

            // 40 Cantilevered steel structural rib pairs arching up into wings
            const int numRibs = 22;
            for (int r = 0; r < numRibs; r++)
            {
                float z = Mathf.Lerp(-34f, 34f, r / (float)(numRibs - 1));
                float spanFrac = 1.0f - Mathf.Pow(Mathf.Abs(z) / 36f, 2f);
                float wingH = Mathf.Lerp(16f, 42f, spanFrac);
                float wingSpread = Mathf.Lerp(14f, 32f, spanFrac);

                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 basePt = new Vector3(s * 8f, 5f, z);
                    Vector3 tipPt = new Vector3(s * wingSpread, wingH, z);
                    Vector3 midPt = (basePt + tipPt) * 0.5f;

                    ctx.AddBox(midPt, new Vector3(0.55f, wingH - 5f, 0.65f), matRibs);
                }
            }

            Vector3 bMin = new Vector3(-35f, 0, -40f);
            Vector3 bMax = new Vector3(35f, 48f, 40f);
            AddCollider(c0, bMin, bMax);

            FitToSite(ctx, geo, "One World Trade Center", c0, 48f, offset: new Vector2(75f, 10f));
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 15. WOOLWORTH BUILDING (Broadway & Park Row)
        // =========================================================================
        void BuildWoolworth(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("WoolworthBuilding", root);

            int matTerracotta = ctx.RegisterMaterial(limestoneTrimMaterial);
            int matCopper = ctx.RegisterMaterial(copperRoofMaterial);
            int matGlass = ctx.RegisterMaterial(darkBronzeFrameMaterial);
            int matBeacon = ctx.RegisterMaterial(beaconMaterial);

            // 30-story Base (54m x 54m x 108m high)
            ctx.AddBox(new Vector3(0, 54f, 0), new Vector3(54f, 108f, 54f), matTerracotta);
            // U-shaped central light court facing east
            ctx.AddBox(new Vector3(0, 58f, 18f), new Vector3(18f, 100f, 20f), matGlass);

            // 30-story Setback Tower (26m x 26m x 98m high) rising to 206m
            ctx.AddBox(new Vector3(0, 157f, -4f), new Vector3(26f, 98f, 26f), matTerracotta);

            // 4 Corner Gothic Tourelles (octagonal turrets) at the setback
            for (int sx = -1; sx <= 1; sx += 2)
            {
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    Vector3 p = new Vector3(sx * 13f, 108f, -4f + sz * 13f);
                    ctx.AddCylinder(p, 1.6f, 1.6f, 18f, 8, matTerracotta);
                    ctx.AddCone(p + new Vector3(0, 18f, 0), 1.8f, 7f, 8, matCopper);
                }
            }

            // Pyramidal Green Copper Roof
            ctx.AddCone(new Vector3(0, 206f, -4f), 15f, 22f, 4, matCopper);

            // Gothic Spire & Lantern
            ctx.AddCylinder(new Vector3(0, 228f, -4f), 0.25f, 1.5f, 13f, 8, matCopper);
            ctx.AddSphere(new Vector3(0, 241f, -4f), 0.85f, 8, 8, matBeacon);

            // Stepped collision for Woolworth Building matching podium, tower, and pinnacle spire
            AddCollider(c0, new Vector3(-27f, 0, -27f), new Vector3(27f, 108f, 27f)); // Base podium
            AddCollider(c0, new Vector3(-14f, 0, -18f), new Vector3(14f, 206f, 10f)); // Setback tower shaft
            AddCollider(c0, new Vector3(-8f, 0, -12f), new Vector3(8f, 241f, 4f));    // Spire & pinnacle

            FitToSite(ctx, geo, "Woolworth Building", c0, 241f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 16. 30 HUDSON YARDS (Supertall with Cantilevered "The Edge" Deck)
        // =========================================================================
        void BuildHudsonYards30(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("HudsonYards30", root);

            int matGlass = ctx.RegisterMaterial(silverCurtainMaterial);
            int matSteel = ctx.RegisterMaterial(stainlessSteelMaterial);
            int matDeck = ctx.RegisterMaterial(warmFloodMaterial);
            int matBeacon = ctx.RegisterMaterial(beaconMaterial);

            // Base Podium
            ctx.AddBox(new Vector3(0, 24f, 0), new Vector3(56f, 48f, 48f), matGlass);

            // Angled main shaft rising to 335m
            ctx.AddBox(new Vector3(0, 190f, 0), new Vector3(46f, 284f, 40f), matGlass);

            // Triangular Cantilevered Observation Deck "The Edge" at y = 335m
            // Juts 22m out towards the east/south corner
            Vector3 deckCenter = new Vector3(28f, 335f, 16f);
            ctx.AddBox(deckCenter, new Vector3(22f, 2.2f, 20f), matSteel);
            ctx.AddBox(deckCenter + new Vector3(0, 1.6f, 0), new Vector3(18f, 0.4f, 16f), matDeck);
            // Angled glass parapet wall
            ctx.AddBox(deckCenter + new Vector3(9f, 2.8f, 0), new Vector3(0.35f, 2.4f, 18f), matDeck);

            // Sliced Angled Crown Peak rising to 387m
            ctx.AddWedge(new Vector3(0, 335f, 0), new Vector3(44f, 52f, 38f), matGlass);
            ctx.AddSphere(new Vector3(0, 387f, 0), 1.2f, 8, 8, matBeacon);

            // 30 Hudson Yards: tiered podium, main shaft, and cantilevered observation deck "The Edge"
            AddCollider(c0, new Vector3(-28f, 0, -24f), new Vector3(28f, 48f, 24f));    // Base podium
            AddCollider(c0, new Vector3(-23f, 0, -20f), new Vector3(23f, 335f, 20f));   // Main tower shaft
            AddCollider(c0, new Vector3(17f, 335f, 6f), new Vector3(39f, 345f, 26f));      // "The Edge" cantilever at 335m
            AddCollider(c0, new Vector3(-22f, 0, -19f), new Vector3(22f, 387f, 19f));   // Angled crown peak

            FitToSite(ctx, geo, "30 Hudson Yards", c0, 387f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 17. UNITED NATIONS SECRETARIAT BUILDING (East River Modernist Slab)
        // =========================================================================
        void BuildUnSecretariat(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("UnSecretariat", root);

            int matGlass = ctx.RegisterMaterial(bronzeCurtainMaterial);
            int matMarble = ctx.RegisterMaterial(marbleStoneMaterial);
            int matSteel = ctx.RegisterMaterial(stainlessSteelMaterial);

            // 154m tall slender modernist slab (84m north-south x 22m east-west)
            const float slabW = 22f;
            const float slabD = 84f;
            const float slabH = 154f;

            // East & West curtain wall glass facades
            ctx.AddBox(new Vector3(0, slabH * 0.5f, 0), new Vector3(slabW - 1.2f, slabH, slabD), matGlass);

            // North and South solid windowless Vermont white marble end walls
            ctx.AddBox(new Vector3(0, slabH * 0.5f, -slabD * 0.5f + 0.6f), new Vector3(slabW, slabH, 1.4f), matMarble);
            ctx.AddBox(new Vector3(0, slabH * 0.5f, slabD * 0.5f - 0.6f), new Vector3(slabW, slabH, 1.4f), matMarble);

            // Recessed mechanical penthouse and communication antenna
            ctx.AddBox(new Vector3(0, slabH + 3.2f, 0), new Vector3(14f, 6.4f, 48f), matMarble);
            ctx.AddCylinder(new Vector3(0, slabH + 6.4f, 0), 0.15f, 0.6f, 18f, 8, matSteel);

            Vector3 bMin = new Vector3(-12f, 0, -43f);
            Vector3 bMax = new Vector3(12f, 154f, 43f);
            AddCollider(c0, bMin, bMax);

            FitToSite(ctx, geo, "United Nations Secretariat Building", c0, 154f);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // 18. STATUE OF LIBERTY (Fort Wood 11-Point Star & Pedestal)
        // =========================================================================
        void BuildStatueOfLiberty(NycGeoData geo, Transform root)
        {
            int c0 = CollisionBoxes.Count;
            var ctx = new LandmarkContext("StatueOfLiberty", root);

            int matFort = ctx.RegisterMaterial(marbleStoneMaterial);
            int matPedestal = ctx.RegisterMaterial(limestoneTrimMaterial);
            int matCopper = ctx.RegisterMaterial(copperRoofMaterial);
            int matFlame = ctx.RegisterMaterial(warmFloodMaterial);

            // 1. Fort Wood 11-pointed star fort base (height 12m, width 64m)
            const int points = 11;
            const float rOuter = 32f;
            for (int p = 0; p < points; p++)
            {
                float a0 = (p / (float)points) * Mathf.PI * 2f;
                float aMid = ((p + 0.5f) / (float)points) * Mathf.PI * 2f;
                float a1 = ((p + 1) / (float)points) * Mathf.PI * 2f;

                Vector3 tip = new Vector3(Mathf.Cos(aMid) * rOuter, 6f, Mathf.Sin(aMid) * rOuter);
                ctx.AddBox(tip, new Vector3(7.5f, 12f, 7.5f), matFort);
            }
            ctx.AddCylinder(new Vector3(0, 0, 0), 22f, 22f, 12f, 16, matFort);

            // 2. Stepped Neoclassical Granite Pedestal (47m high)
            ctx.AddBox(new Vector3(0, 16f, 0), new Vector3(24f, 8f, 24f), matPedestal);
            ctx.AddBox(new Vector3(0, 24f, 0), new Vector3(20f, 8f, 20f), matPedestal);
            ctx.AddBox(new Vector3(0, 36f, 0), new Vector3(16f, 16f, 16f), matPedestal);
            // Observation gallery balcony
            ctx.AddBox(new Vector3(0, 45f, 0), new Vector3(18f, 2f, 18f), matPedestal);

            // 3. Copper Statue of Libertas (46m high to torch)
            // Robed body and draping folds
            ctx.AddCylinder(new Vector3(0, 46f, 0), 3.2f, 5.2f, 26f, 12, matCopper);
            // Head and radiant 7-point crown
            Vector3 headPos = new Vector3(0, 74f, 0);
            ctx.AddSphere(headPos, 2.6f, 8, 8, matCopper);
            for (int i = 0; i < 7; i++)
            {
                float ca = (i - 3) * 0.22f;
                Vector3 spikeTip = headPos + new Vector3(Mathf.Sin(ca) * 4.8f, 3.2f + Mathf.Cos(ca) * 1.5f, -1.2f);
                ctx.AddCone(headPos + new Vector3(Mathf.Sin(ca) * 2.2f, 2.2f, -0.6f), 0.35f, 3.2f, 4, matCopper);
            }

            // Left arm cradling tabula ansata (tablet)
            ctx.AddBox(new Vector3(-3.8f, 62f, -1.2f), new Vector3(1.2f, 3.8f, 2.6f), matCopper);

            // Right arm raised high holding the torch
            ctx.AddCylinder(new Vector3(2.8f, 66f, 0), 0.8f, 1.1f, 18f, 8, matCopper);
            // Golden flame with floodlight glow
            ctx.AddCone(new Vector3(2.8f, 85f, 0), 1.6f, 5.5f, 8, matFlame);

            Vector3 bMin = new Vector3(-32f, 0, -32f);
            Vector3 bMax = new Vector3(32f, 93f, 32f);
            AddCollider(c0, bMin, bMax);

            Vector2 solWorld = new Vector2(-1236.7f, 8844.1f);
            if (geo != null && geo.Points.TryGetValue("statueOfLiberty", out var pSol)) solWorld = pSol;
            ApplyFit(ctx, solWorld.x, solWorld.y, 64f, 64f, c0, 93f, 0f, false, Vector2.zero);
            ctx.FinalizeMesh();
        }

        // =========================================================================
        // Site Fitting & Collision Box plumbing
        // =========================================================================
        void AddCollider(int c0, Vector3 localMin, Vector3 localMax)
        {
            CollisionBoxes.Add(new CollisionBox
            {
                MinX = localMin.x,
                MaxX = localMax.x,
                MinZ = localMin.z,
                MaxZ = localMax.z,
                MinY = localMin.y,
                MaxY = localMax.y
            });
        }

        void FitToSite(LandmarkContext ctx, NycGeoData geo, string name, int c0, float targetHeight,
                       float horizontal = 0f, bool stretch = false, Vector2 offset = default)
        {
            if (geo == null || !geo.Landmarks.TryGetValue(name, out var site))
            {
                // Fallback site bounds
                site = new LandmarkRec { Name = name, X = 0, Z = 0, W = 50, D = 50, H = targetHeight };
            }

            ApplyFit(ctx, site.X, site.Z, site.W, site.D, c0, targetHeight, horizontal, stretch, offset);
        }

        void FitToSiteUnion(LandmarkContext ctx, NycGeoData geo, string[] names, int c0, float targetHeight)
        {
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            bool found = false;
            foreach (var n in names)
            {
                if (geo.Landmarks.TryGetValue(n, out var l))
                {
                    found = true;
                    minX = Mathf.Min(minX, l.X - l.W * 0.5f);
                    maxX = Mathf.Max(maxX, l.X + l.W * 0.5f);
                    minZ = Mathf.Min(minZ, l.Z - l.D * 0.5f);
                    maxZ = Mathf.Max(maxZ, l.Z + l.D * 0.5f);
                }
            }
            if (!found) { minX = -30; maxX = 30; minZ = -30; maxZ = 30; }
            float cx = (minX + maxX) * 0.5f;
            float cz = (minZ + maxZ) * 0.5f;
            float w = maxX - minX;
            float d = maxZ - minZ;
            ApplyFit(ctx, cx, cz, w, d, c0, targetHeight, 0f, false, Vector2.zero);
        }

        void FitToParkBlock(LandmarkContext ctx, NycGeoData geo, string parkName, int c0, float targetHeight)
        {
            float cx = 0, cz = 0, w = 180, d = 80;
            if (geo != null)
            {
                foreach (var b in geo.Blocks)
                {
                    if (b.ParkName == parkName && !b.Median)
                    {
                        cx = b.Center.x;
                        cz = b.Center.y;
                        w = b.Bounds.y - b.Bounds.x - 9.2f;
                        d = b.Bounds.w - b.Bounds.z - 9.2f;
                        break;
                    }
                }
            }
            ApplyFit(ctx, cx, cz, w, d, c0, targetHeight, 0f, true, Vector2.zero);
        }

        void ApplyFit(LandmarkContext ctx, float sx0, float sz0, float siteW, float siteD, int c0,
                      float targetHeight, float horizontal, bool stretch, Vector2 offset)
        {
            Vector3 localSize = ctx.GetBoundsSize();
            Vector3 localCenter = ctx.GetBoundsCenter();

            float sy = targetHeight > 0f && localSize.y > 0.01f ? targetHeight / localSize.y : 1f;
            float sx, sz;
            if (stretch)
            {
                sx = siteW / Mathf.Max(0.1f, localSize.x);
                sz = siteD / Mathf.Max(0.1f, localSize.z);
            }
            else
            {
                float fit = Mathf.Min(siteW / Mathf.Max(0.1f, localSize.x), siteD / Mathf.Max(0.1f, localSize.z));
                sx = sz = horizontal > 0f ? horizontal : Mathf.Min(sy, fit);
            }

            float tx = sx0 + offset.x;
            float tz = sz0 + offset.y;

            // Transform vertices from local to Unity coordinate space:
            // x_unity = tx + (x_local - c.x) * sx
            // y_unity = y_local * sy
            // z_unity = -(tz + (z_local - c.z) * sz)
            ctx.TransformVertices(tx, tz, sx, sy, sz, localCenter);

            // Transform colliders
            for (int i = c0; i < CollisionBoxes.Count; i++)
            {
                var k = CollisionBoxes[i];
                float x0 = tx + (k.MinX - localCenter.x) * sx;
                float x1 = tx + (k.MaxX - localCenter.x) * sx;
                float z0 = tz + (k.MinZ - localCenter.z) * sz;
                float z1 = tz + (k.MaxZ - localCenter.z) * sz;
                CollisionBoxes[i] = new CollisionBox
                {
                    MinX = Mathf.Min(x0, x1),
                    MaxX = Mathf.Max(x0, x1),
                    MinZ = Mathf.Min(z0, z1),
                    MaxZ = Mathf.Max(z0, z1),
                    MinY = k.MinY * sy,
                    MaxY = k.MaxY * sy
                };
            }
        }

        void EnsureMaterials()
        {
            if (limestoneTrimMaterial == null) limestoneTrimMaterial = MakeLandmarkFacadeMaterial("Landmark_Limestone", new Color(0.85f, 0.81f, 0.74f), new Color(0.08f, 0.14f, 0.22f), 2.5f, 3.6f, 0.55f, 0.65f, 0.85f, 0.12f);
            if (stainlessSteelMaterial == null) stainlessSteelMaterial = MakeMaterial("Landmark_StainlessSteel", new Color(0.88f, 0.92f, 0.96f), 0.88f, 0.88f);
            if (chryslerCrownMaterial == null) chryslerCrownMaterial = MakeMaterial("Landmark_ChryslerCrown", new Color(0.94f, 0.96f, 0.98f), 0.95f, 0.95f);
            if (darkBronzeFrameMaterial == null) darkBronzeFrameMaterial = MakeMaterial("Landmark_DarkBronze", new Color(0.15f, 0.13f, 0.12f), 0.64f, 0.72f);
            if (copperRoofMaterial == null) copperRoofMaterial = MakeMaterial("Landmark_CopperRoof", new Color(0.29f, 0.53f, 0.44f), 0.48f, 0.28f);
            if (beaconMaterial == null) beaconMaterial = MakeEmissiveMaterial("Landmark_BeaconRed", new Color(1f, 0.17f, 0.17f), new Color(1f, 0.17f, 0.17f) * 2f);
            if (warmFloodMaterial == null) warmFloodMaterial = MakeEmissiveMaterial("Landmark_WarmFlood", new Color(1f, 0.91f, 0.75f), new Color(1f, 0.78f, 0.44f) * 1.5f);
            if (marbleStoneMaterial == null) marbleStoneMaterial = MakeLandmarkFacadeMaterial("Landmark_MarbleStone", new Color(0.92f, 0.90f, 0.86f), new Color(0.06f, 0.12f, 0.18f), 2.8f, 3.8f, 0.68f, 0.72f, 0.90f, 0.08f);
            if (silverCurtainMaterial == null) silverCurtainMaterial = MakeLandmarkFacadeMaterial("Landmark_SilverCurtain", new Color(0.78f, 0.84f, 0.90f), new Color(0.04f, 0.12f, 0.22f), 2.8f, 4.0f, 0.78f, 0.80f, 0.95f, 0.75f);
            if (bronzeCurtainMaterial == null) bronzeCurtainMaterial = MakeLandmarkFacadeMaterial("Landmark_BronzeCurtain", new Color(0.24f, 0.20f, 0.16f), new Color(0.05f, 0.08f, 0.12f), 2.6f, 3.8f, 0.72f, 0.78f, 0.92f, 0.65f);
            if (parkGrassMaterial == null) parkGrassMaterial = MakeMaterial("Landmark_ParkGrass", new Color(0.22f, 0.55f, 0.26f), 0.20f, 0.0f);
            if (waterMaterial == null) waterMaterial = MakeMaterial("Landmark_Water", new Color(0.14f, 0.47f, 0.61f), 0.92f, 0.65f);
            if (signMaterial == null) signMaterial = MakeEmissiveMaterial("Landmark_Sign", new Color(0.14f, 0.13f, 0.12f), new Color(1f, 0.96f, 0.86f));
        }

        static Material MakeLandmarkFacadeMaterial(string name, Color stoneCol, Color glassCol, float winW, float flrH, float winRatioX, float winRatioY, float smoothness, float metallic)
        {
            var shader = Shader.Find("Nyc/LandmarkFacade");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader) { name = name };
            mat.color = stoneCol;
            if (mat.HasProperty("_StoneColor")) mat.SetColor("_StoneColor", stoneCol);
            if (mat.HasProperty("_GlassColor")) mat.SetColor("_GlassColor", glassCol);
            if (mat.HasProperty("_WindowWidth")) mat.SetFloat("_WindowWidth", winW);
            if (mat.HasProperty("_FloorHeight")) mat.SetFloat("_FloorHeight", flrH);
            if (mat.HasProperty("_WindowRatioX")) mat.SetFloat("_WindowRatioX", winRatioX);
            if (mat.HasProperty("_WindowRatioY")) mat.SetFloat("_WindowRatioY", winRatioY);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            return mat;
        }

        static Material MakeMaterial(string name, Color color, float smoothness, float metallic)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = name };
            mat.color = color;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            return mat;
        }

        static Material MakeEmissiveMaterial(string name, Color baseCol, Color emCol)
        {
            var mat = MakeMaterial(name, baseCol, 0.8f, 0.2f);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emCol);
            }
            return mat;
        }

        static Material MakeBillboardMaterial(int themeIndex, Color fallbackBase, Color fallbackEm)
        {
            var shader = Shader.Find("Nyc/Billboard");
            if (shader != null)
            {
                var mat = new Material(shader) { name = $"TS_Billboard_{themeIndex}" };
                mat.SetFloat("_ThemeIndex", themeIndex);
                mat.SetFloat("_EmissionMultiplier", 2.6f);
                return mat;
            }
            return MakeEmissiveMaterial($"TS_Screen_{themeIndex}", fallbackBase, fallbackEm);
        }

        // =========================================================================
        // Context for building and buffering landmark geometry
        // =========================================================================
        sealed class LandmarkContext
        {
            public readonly string Name;
            public readonly Transform Parent;
            public readonly List<Material> Materials = new List<Material>();

            readonly List<Vector3> vertices = new List<Vector3>(2048);
            readonly List<Vector3> normals = new List<Vector3>(2048);
            readonly List<Vector2> uv0 = new List<Vector2>(2048);
            readonly List<Color32> colors = new List<Color32>(2048);
            readonly List<List<int>> subTris = new List<List<int>>();

            Vector3 bMin = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 bMax = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            public LandmarkContext(string name, Transform parent)
            {
                Name = name;
                Parent = parent;
            }

            public int RegisterMaterial(Material m)
            {
                for (int i = 0; i < Materials.Count; i++)
                    if (Materials[i] == m) return i;
                Materials.Add(m);
                subTris.Add(new List<int>(512));
                return Materials.Count - 1;
            }

            public Vector3 GetBoundsSize() => new Vector3(Mathf.Max(0.01f, bMax.x - bMin.x),
                                                          Mathf.Max(0.01f, bMax.y - bMin.y),
                                                          Mathf.Max(0.01f, bMax.z - bMin.z));

            public Vector3 GetBoundsCenter() => (bMin + bMax) * 0.5f;

            void ExpandBounds(Vector3 p)
            {
                if (p.x < bMin.x) bMin.x = p.x;
                if (p.x > bMax.x) bMax.x = p.x;
                if (p.y < bMin.y) bMin.y = p.y;
                if (p.y > bMax.y) bMax.y = p.y;
                if (p.z < bMin.z) bMin.z = p.z;
                if (p.z > bMax.z) bMax.z = p.z;
            }

            public void AddBox(Vector3 center, Vector3 size, int matSub, bool rotY45 = false)
            {
                float hx = size.x * 0.5f, hy = size.y * 0.5f, hz = size.z * 0.5f;
                Vector3[] corners = new[]
                {
                    new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz),
                    new Vector3(hx, -hy, hz), new Vector3(-hx, -hy, hz),
                    new Vector3(-hx, hy, -hz), new Vector3(hx, hy, -hz),
                    new Vector3(hx, hy, hz), new Vector3(-hx, hy, hz)
                };

                if (rotY45)
                {
                    float angle = 45f * Mathf.Deg2Rad;
                    float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
                    for (int i = 0; i < corners.Length; i++)
                    {
                        float rx = corners[i].x * cs - corners[i].z * sn;
                        float rz = corners[i].x * sn + corners[i].z * cs;
                        corners[i] = new Vector3(rx, corners[i].y, rz);
                    }
                }

                for (int i = 0; i < corners.Length; i++)
                {
                    corners[i] += center;
                    ExpandBounds(corners[i]);
                }

                int[][] faces = new[]
                {
                    new[] { 0, 1, 5, 4 }, // Front (-Z)
                    new[] { 2, 3, 7, 6 }, // Back (+Z)
                    new[] { 3, 0, 4, 7 }, // Left (-X)
                    new[] { 1, 2, 6, 5 }, // Right (+X)
                    new[] { 4, 5, 6, 7 }, // Top (+Y)
                    new[] { 3, 2, 1, 0 }  // Bottom (-Y)
                };

                Vector3[] faceNormals = new[]
                {
                    new Vector3(0, 0, -1), new Vector3(0, 0, 1),
                    new Vector3(-1, 0, 0), new Vector3(1, 0, 0),
                    new Vector3(0, 1, 0), new Vector3(0, -1, 0)
                };

                for (int f = 0; f < 6; f++)
                {
                    Vector3 fn = rotY45 ? Quaternion.Euler(0, 45, 0) * faceNormals[f] : faceNormals[f];
                    int baseIdx = vertices.Count;
                    for (int v = 0; v < 4; v++)
                    {
                        vertices.Add(corners[faces[f][v]]);
                        normals.Add(fn);
                        uv0.Add(new Vector2((v == 1 || v == 2) ? 1f : 0f, (v >= 2) ? 1f : 0f));
                        colors.Add(Color.white);
                    }
                    var s = subTris[matSub];
                    s.Add(baseIdx); s.Add(baseIdx + 1); s.Add(baseIdx + 2);
                    s.Add(baseIdx); s.Add(baseIdx + 2); s.Add(baseIdx + 3);
                }
            }

            public void AddCylinder(Vector3 basePos, float rTop, float rBottom, float height,
                                    int segments, int matSub, bool rotX90 = false)
            {
                int baseIdx = vertices.Count;
                var s = subTris[matSub];

                for (int i = 0; i <= segments; i++)
                {
                    float angle = (i / (float)segments) * Mathf.PI * 2f;
                    float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);

                    Vector3 bP = new Vector3(cs * rBottom, 0, sn * rBottom);
                    Vector3 tP = new Vector3(cs * rTop, height, sn * rTop);

                    if (rotX90)
                    {
                        bP = new Vector3(bP.x, -bP.z, bP.y);
                        tP = new Vector3(tP.x, -tP.z, tP.y);
                    }

                    bP += basePos;
                    tP += basePos;
                    ExpandBounds(bP);
                    ExpandBounds(tP);

                    Vector3 n = new Vector3(cs, 0, sn);
                    if (rotX90) n = new Vector3(n.x, -n.z, n.y);

                    vertices.Add(bP); normals.Add(n); uv0.Add(new Vector2(i / (float)segments, 0)); colors.Add(Color.white);
                    vertices.Add(tP); normals.Add(n); uv0.Add(new Vector2(i / (float)segments, 1)); colors.Add(Color.white);
                }

                for (int i = 0; i < segments; i++)
                {
                    int i0 = baseIdx + i * 2;
                    int i1 = baseIdx + i * 2 + 1;
                    int i2 = baseIdx + (i + 1) * 2;
                    int i3 = baseIdx + (i + 1) * 2 + 1;

                    s.Add(i0); s.Add(i1); s.Add(i2);
                    s.Add(i2); s.Add(i1); s.Add(i3);
                }
            }

            public void AddCone(Vector3 basePos, float radius, float height, int segments, int matSub)
            {
                int baseIdx = vertices.Count;
                var s = subTris[matSub];
                Vector3 tip = basePos + new Vector3(0, height, 0);
                ExpandBounds(tip);

                for (int i = 0; i <= segments; i++)
                {
                    float angle = (i / (float)segments) * Mathf.PI * 2f;
                    float cs = Mathf.Cos(angle), sn = Mathf.Sin(angle);
                    Vector3 bP = basePos + new Vector3(cs * radius, 0, sn * radius);
                    ExpandBounds(bP);

                    Vector3 n = new Vector3(cs, 0.5f, sn).normalized;
                    vertices.Add(bP); normals.Add(n); uv0.Add(new Vector2(i / (float)segments, 0)); colors.Add(Color.white);
                }
                int tipIdx = vertices.Count;
                vertices.Add(tip); normals.Add(Vector3.up); uv0.Add(new Vector2(0.5f, 1f)); colors.Add(Color.white);

                for (int i = 0; i < segments; i++)
                {
                    s.Add(baseIdx + i);
                    s.Add(tipIdx);
                    s.Add(baseIdx + i + 1);
                }
            }

            public void AddSphere(Vector3 center, float radius, int rings, int segments, int matSub)
            {
                int baseIdx = vertices.Count;
                var s = subTris[matSub];

                for (int r = 0; r <= rings; r++)
                {
                    float v = r / (float)rings;
                    float phi = v * Mathf.PI;
                    for (int seg = 0; seg <= segments; seg++)
                    {
                        float u = seg / (float)segments;
                        float theta = u * Mathf.PI * 2f;

                        Vector3 n = new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta),
                                                Mathf.Cos(phi),
                                                Mathf.Sin(phi) * Mathf.Sin(theta));
                        Vector3 p = center + n * radius;
                        ExpandBounds(p);

                        vertices.Add(p);
                        normals.Add(n);
                        uv0.Add(new Vector2(u, v));
                        colors.Add(Color.white);
                    }
                }

                for (int r = 0; r < rings; r++)
                {
                    for (int seg = 0; seg < segments; seg++)
                    {
                        int i0 = baseIdx + r * (segments + 1) + seg;
                        int i1 = i0 + segments + 1;
                        s.Add(i0); s.Add(i1); s.Add(i0 + 1);
                        s.Add(i0 + 1); s.Add(i1); s.Add(i1 + 1);
                    }
                }
            }

            public void AddWedge(Vector3 pos, Vector3 size, int matSub)
            {
                float hx = size.x * 0.5f, hy = size.y, hz = size.z * 0.5f;
                // Citigroup south-facing 45-degree slope
                Vector3 p0 = pos + new Vector3(-hx, 0, -hz);
                Vector3 p1 = pos + new Vector3(hx, 0, -hz);
                Vector3 p2 = pos + new Vector3(hx, 0, hz);
                Vector3 p3 = pos + new Vector3(-hx, 0, hz);
                Vector3 p4 = pos + new Vector3(-hx, hy, hz);
                Vector3 p5 = pos + new Vector3(hx, hy, hz);

                ExpandBounds(p0); ExpandBounds(p1); ExpandBounds(p2);
                ExpandBounds(p3); ExpandBounds(p4); ExpandBounds(p5);

                int bIdx = vertices.Count;
                var s = subTris[matSub];

                // Bottom
                AddQuad(s, bIdx, p3, p2, p1, p0, Vector3.down);
                // Back vertical wall (+Z)
                AddQuad(s, bIdx + 4, p2, p3, p4, p5, Vector3.forward);
                // Sloped top wall (from front to top-back)
                Vector3 slopeN = Vector3.Cross(p5 - p0, p1 - p0).normalized;
                AddQuad(s, bIdx + 8, p0, p1, p5, p4, slopeN);
                // Left triangle
                AddTri(s, bIdx + 12, p0, p4, p3, Vector3.left);
                // Right triangle
                AddTri(s, bIdx + 15, p1, p2, p5, Vector3.right);
            }

            void AddQuad(List<int> s, int b, Vector3 a, Vector3 bP, Vector3 c, Vector3 d, Vector3 n)
            {
                vertices.Add(a); normals.Add(n); uv0.Add(new Vector2(0, 0)); colors.Add(Color.white);
                vertices.Add(bP); normals.Add(n); uv0.Add(new Vector2(1, 0)); colors.Add(Color.white);
                vertices.Add(c); normals.Add(n); uv0.Add(new Vector2(1, 1)); colors.Add(Color.white);
                vertices.Add(d); normals.Add(n); uv0.Add(new Vector2(0, 1)); colors.Add(Color.white);
                s.Add(b); s.Add(b + 1); s.Add(b + 2);
                s.Add(b); s.Add(b + 2); s.Add(b + 3);
            }

            void AddTri(List<int> s, int b, Vector3 a, Vector3 bP, Vector3 c, Vector3 n)
            {
                vertices.Add(a); normals.Add(n); uv0.Add(new Vector2(0, 0)); colors.Add(Color.white);
                vertices.Add(bP); normals.Add(n); uv0.Add(new Vector2(1, 0)); colors.Add(Color.white);
                vertices.Add(c); normals.Add(n); uv0.Add(new Vector2(0.5f, 1)); colors.Add(Color.white);
                s.Add(b); s.Add(b + 1); s.Add(b + 2);
            }

            public void AddExtrudedPolygon(Vector2[] poly, float yBase, float height, int matSub)
            {
                int bIdx = vertices.Count;
                var s = subTris[matSub];
                int n = poly.Length;

                // Vertical side walls
                for (int i = 0; i < n; i++)
                {
                    Vector2 a = poly[i];
                    Vector2 b = poly[(i + 1) % n];
                    Vector3 p0 = new Vector3(a.x, yBase, a.y);
                    Vector3 p1 = new Vector3(b.x, yBase, b.y);
                    Vector3 p2 = new Vector3(b.x, yBase + height, b.y);
                    Vector3 p3 = new Vector3(a.x, yBase + height, a.y);

                    ExpandBounds(p0); ExpandBounds(p2);

                    Vector3 sideN = Vector3.Cross(p2 - p1, p0 - p1).normalized;
                    int cur = vertices.Count;
                    AddQuad(s, cur, p0, p1, p2, p3, sideN);
                }

                // Top polygon cap (simple fan for convex/star shapes)
                int topBase = vertices.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector3 p = new Vector3(poly[i].x, yBase + height, poly[i].y);
                    vertices.Add(p); normals.Add(Vector3.up); uv0.Add(new Vector2(p.x * 0.05f, p.z * 0.05f)); colors.Add(Color.white);
                }
                for (int i = 1; i + 1 < n; i++)
                {
                    s.Add(topBase);
                    s.Add(topBase + i);
                    s.Add(topBase + i + 1);
                }
            }

            public void AddAntiprismShaft(Vector3 center, float rTop, float rBottom, float height,
                                          float rotDeg, int matSub)
            {
                float angleOffset = rotDeg * Mathf.Deg2Rad;
                int baseIdx = vertices.Count;
                var s = subTris[matSub];

                Vector3[] bPts = new Vector3[4];
                Vector3[] tPts = new Vector3[4];
                for (int i = 0; i < 4; i++)
                {
                    float a = angleOffset + (i / 4f) * Mathf.PI * 2f;
                    bPts[i] = center + new Vector3(Mathf.Cos(a) * rBottom, -height * 0.5f, Mathf.Sin(a) * rBottom);
                    tPts[i] = center + new Vector3(Mathf.Cos(a) * rTop, height * 0.5f, Mathf.Sin(a) * rTop);
                    ExpandBounds(bPts[i]);
                    ExpandBounds(tPts[i]);
                }

                for (int i = 0; i < 4; i++)
                {
                    int next = (i + 1) % 4;
                    Vector3 p0 = bPts[i], p1 = bPts[next], p2 = tPts[next], p3 = tPts[i];
                    Vector3 fn = Vector3.Cross(p2 - p1, p0 - p1).normalized;
                    int cur = vertices.Count;
                    AddQuad(s, cur, p0, p1, p2, p3, fn);
                }
            }

            public void TransformVertices(float tx, float tz, float sx, float sy, float sz, Vector3 center)
            {
                for (int i = 0; i < vertices.Count; i++)
                {
                    Vector3 v = vertices[i];
                    float wx = tx + (v.x - center.x) * sx;
                    float wy = v.y * sy;
                    float wz = tz + (v.z - center.z) * sz;
                    vertices[i] = CityFrame.ToUnity(wx, wy, wz);

                    Vector3 n = normals[i];
                    normals[i] = new Vector3(n.x, n.y, -n.z);
                }
            }

            public GameObject FinalizeMesh()
            {
                if (vertices.Count == 0) return null;

                var go = new GameObject(Name);
                go.transform.SetParent(Parent, false);

                var mesh = new Mesh { name = Name };
                if (vertices.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;

                mesh.SetVertices(vertices);
                mesh.SetNormals(normals);
                mesh.SetUVs(0, uv0);
                var dummyUvs = new List<Vector2>(vertices.Count);
                for (int i = 0; i < vertices.Count; i++) dummyUvs.Add(Vector2.zero);
                mesh.SetUVs(1, dummyUvs);
                mesh.SetUVs(2, dummyUvs);
                mesh.SetUVs(3, dummyUvs);
                mesh.SetColors(colors);

                mesh.subMeshCount = subTris.Count;
                for (int m = 0; m < subTris.Count; m++)
                {
                    var tris = subTris[m];
                    for (int t = 0; t + 2 < tris.Count; t += 3)
                    {
                        int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                        Vector3 cr = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                        if (Vector3.Dot(cr, normals[a]) < 0f)
                        {
                            tris[t + 1] = c;
                            tris[t + 2] = b;
                        }
                    }
                    mesh.SetTriangles(tris, m);
                }
                mesh.RecalculateBounds();

                var mf = go.AddComponent<MeshFilter>();
                mf.sharedMesh = mesh;

                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = Materials.ToArray();
                mr.shadowCastingMode = ShadowCastingMode.On;
                mr.receiveShadows = true;

                return go;
            }
        }
    }
}
