using System.Collections.Generic;
using UnityEngine;

namespace Nyc.Gameplay.Traffic
{
    /// <summary>
    /// Sculpted, aerodynamic vehicle meshes matching the GTA 6 / three.js actor-models.js silhouettes.
    /// uv1.x carries a material id consumed by Nyc/VehiclePaint:
    ///   0 body · 1 glass · 2 headlight · 3 taillight · 4 tyre
    /// Features:
    ///   - Raked aerodynamic cabins with sloped windshields and contoured backlights.
    ///   - 12-segment cylindrical wheels with rubber tyre tread and alloy rim hubs.
    ///   - Front and rear bumpers, grilles, and recessed lamps.
    ///   - NYC Taxi rooftop aerodynamic medallion sign.
    ///   - NYPD Patrol cruiser with push bumper and dual red/blue emergency lightbar.
    ///   - NYC MTA Transit bus with destination sign, curved windscreen, roof AC pods.
    ///   - Heavy box truck with cab, fuel tanks, dual rear wheels, cargo box.
    /// </summary>
    public static class VehicleMeshFactory
    {
        public const int Body = 0, Glass = 1, Headlight = 2, Taillight = 3, Tyre = 4, Alloy = 5, TaxiSign = 6, BusBlueStripe = 7, BusCyanRibbon = 8, PoliceRed = 9, PoliceBlue = 10;

        public static Mesh CreateCar(bool suv = false, bool taxi = false)
        {
            var b = new MeshBuilder();
            float w = suv ? 1.95f : 1.82f;
            float len = suv ? 4.90f : 4.45f;
            float sillY = suv ? 0.38f : 0.28f;
            float beltY = suv ? 0.88f : 0.72f;
            float roofY = suv ? 1.68f : 1.42f;
            float wheelRadius = suv ? 0.38f : 0.34f;
            float wheelWidth = suv ? 0.26f : 0.22f;

            float noseZ = -(len * 0.5f);
            float cowlZ = -(len * 0.16f);
            float roofFrontZ = -(len * 0.02f);
            float roofRearZ = suv ? (len * 0.38f) : (len * 0.22f);
            float deckZ = suv ? (len * 0.48f) : (len * 0.36f);
            float tailZ = len * 0.5f;

            // 1. Lower chassis waist & sculpted wheel well arches
            float waistY = (wheelRadius * 1.08f + beltY) * 0.5f;
            float waistH = beltY - wheelRadius * 1.08f;
            b.Box(new Vector3(0f, waistY, 0f), new Vector3(w * 0.94f, waistH, len * 0.94f), Body);

            float wheelZ = len * 0.31f;
            float archR = wheelRadius * 1.18f;
            float centerSillLen = (wheelZ - archR) * 2f;
            b.Box(new Vector3(0f, (sillY + waistY - waistH * 0.5f) * 0.5f, 0f),
                  new Vector3(w * 0.86f, (waistY - waistH * 0.5f) - sillY, centerSillLen), Body);

            float frontOverhangLen = Mathf.Abs((-wheelZ + archR) - noseZ);
            b.Box(new Vector3(0f, (sillY + waistY - waistH * 0.5f) * 0.5f, noseZ + frontOverhangLen * 0.5f),
                  new Vector3(w * 0.88f, (waistY - waistH * 0.5f) - sillY, frontOverhangLen), Body);

            float rearOverhangLen = Mathf.Abs(tailZ - (wheelZ + archR));
            b.Box(new Vector3(0f, (sillY + waistY - waistH * 0.5f) * 0.5f, tailZ - rearOverhangLen * 0.5f),
                  new Vector3(w * 0.88f, (waistY - waistH * 0.5f) - sillY, rearOverhangLen), Body);

            // 2. Front Hood (slopes down from cowl to nose)
            b.Frustum(
                new Vector3(0f, beltY, cowlZ), new Vector3(w * 0.96f, 0.12f, 0f),
                new Vector3(0f, beltY - 0.10f, noseZ), new Vector3(w * 0.88f, 0.16f, 0f),
                Body
            );

            // 3. Cabin Greenhouse (sloped windshield, roof, sloped backlight)
            // Windshield
            b.Frustum(
                new Vector3(0f, beltY, cowlZ), new Vector3(w * 0.88f, 0.05f, 0f),
                new Vector3(0f, roofY, roofFrontZ), new Vector3(w * 0.72f, 0.05f, 0f),
                Glass
            );
            // Cabin roof & sides
            float cabinLen = roofRearZ - roofFrontZ;
            b.Box(new Vector3(0f, (beltY + roofY) * 0.5f, (roofFrontZ + roofRearZ) * 0.5f),
                  new Vector3(w * 0.80f, roofY - beltY, cabinLen), Glass);
            // Roof cap
            b.Box(new Vector3(0f, roofY + 0.02f, (roofFrontZ + roofRearZ) * 0.5f),
                  new Vector3(w * 0.74f, 0.04f, cabinLen * 0.96f), Body);

            // Rear Backlight
            if (suv)
            {
                // SUV: upright rear tailgate glass
                b.Box(new Vector3(0f, (beltY + roofY) * 0.5f, roofRearZ + 0.02f),
                      new Vector3(w * 0.76f, (roofY - beltY) * 0.88f, 0.06f), Glass);
                // Roof rails
                b.Box(new Vector3(-w * 0.35f, roofY + 0.08f, (roofFrontZ + roofRearZ) * 0.5f), new Vector3(0.04f, 0.06f, cabinLen), Body);
                b.Box(new Vector3(w * 0.35f, roofY + 0.08f, (roofFrontZ + roofRearZ) * 0.5f), new Vector3(0.04f, 0.06f, cabinLen), Body);
            }
            else
            {
                // Sedan: sloped rear window down to deck
                b.Frustum(
                    new Vector3(0f, roofY, roofRearZ), new Vector3(w * 0.72f, 0.05f, 0f),
                    new Vector3(0f, beltY, deckZ), new Vector3(w * 0.86f, 0.05f, 0f),
                    Glass
                );
                // Trunk deck
                b.Box(new Vector3(0f, beltY - 0.02f, (deckZ + tailZ) * 0.5f),
                      new Vector3(w * 0.90f, 0.14f, tailZ - deckZ), Body);
            }

            // 4. Front & Rear Bumpers
            b.Box(new Vector3(0f, sillY + 0.12f, noseZ - 0.04f), new Vector3(w * 0.92f, 0.28f, 0.12f), Body);
            b.Box(new Vector3(0f, sillY + 0.14f, tailZ + 0.04f), new Vector3(w * 0.92f, 0.30f, 0.12f), Body);

            // 5. Headlights & Taillights
            float lampX = w * 0.34f;
            float frontLampZ = noseZ - 0.08f;
            float rearLampZ = tailZ + 0.08f;
            b.Box(new Vector3(-lampX, beltY - 0.06f, frontLampZ), new Vector3(0.28f, 0.11f, 0.04f), Headlight);
            b.Box(new Vector3(lampX, beltY - 0.06f, frontLampZ), new Vector3(0.28f, 0.11f, 0.04f), Headlight);
            b.Box(new Vector3(-lampX, beltY - 0.04f, rearLampZ), new Vector3(0.28f, 0.10f, 0.04f), Taillight);
            b.Box(new Vector3(lampX, beltY - 0.04f, rearLampZ), new Vector3(0.28f, 0.10f, 0.04f), Taillight);

            // 6. Side Mirrors
            b.Box(new Vector3(-w * 0.48f, beltY + 0.08f, cowlZ + 0.12f), new Vector3(0.14f, 0.08f, 0.10f), Body);
            b.Box(new Vector3(w * 0.48f, beltY + 0.08f, cowlZ + 0.12f), new Vector3(0.14f, 0.08f, 0.10f), Body);

            // 7. Cylindrical Wheels with deep rubber tyre tread and alloy rim hubcaps
            float halfAxle = w * 0.5f - 0.04f;
            b.CylinderX(new Vector3(-halfAxle, wheelRadius, -wheelZ), wheelRadius, wheelWidth, 14, Tyre);
            b.CylinderX(new Vector3(halfAxle, wheelRadius, -wheelZ), wheelRadius, wheelWidth, 14, Tyre);
            b.CylinderX(new Vector3(-halfAxle, wheelRadius, wheelZ), wheelRadius, wheelWidth, 14, Tyre);
            b.CylinderX(new Vector3(halfAxle, wheelRadius, wheelZ), wheelRadius, wheelWidth, 14, Tyre);

            // Bright Alloy Rims (prominent hubcap offset flush with tyre sidewall)
            float rimRadius = wheelRadius * 0.65f;
            float rimOffset = wheelWidth * 0.50f;
            b.CylinderX(new Vector3(-halfAxle - rimOffset, wheelRadius, -wheelZ), rimRadius, 0.035f, 12, Alloy);
            b.CylinderX(new Vector3(halfAxle + rimOffset, wheelRadius, -wheelZ), rimRadius, 0.035f, 12, Alloy);
            b.CylinderX(new Vector3(-halfAxle - rimOffset, wheelRadius, wheelZ), rimRadius, 0.035f, 12, Alloy);
            b.CylinderX(new Vector3(halfAxle + rimOffset, wheelRadius, wheelZ), rimRadius, 0.035f, 12, Alloy);

            // 8. NYC Yellow Taxi Rooftop Medallion Sign
            if (taxi)
            {
                float signZ = (roofFrontZ + roofRearZ) * 0.5f;
                // Base mounting brackets
                b.Box(new Vector3(0f, roofY + 0.04f, signZ), new Vector3(0.68f, 0.04f, 0.30f), Body);
                // Aerodynamic illuminated taxi sign
                b.Frustum(
                    new Vector3(0f, roofY + 0.06f, signZ), new Vector3(0.62f, 0.04f, 0.28f),
                    new Vector3(0f, roofY + 0.22f, signZ), new Vector3(0.50f, 0.04f, 0.18f),
                    TaxiSign
                );
            }

            return b.ToMesh(suv ? "VehicleSUV" : taxi ? "VehicleTaxi" : "VehicleSedan");
        }

        public static Mesh CreatePoliceCar()
        {
            var b = new MeshBuilder();
            float w = 1.84f;
            float len = 4.52f;
            float sillY = 0.28f;
            float beltY = 0.72f;
            float roofY = 1.42f;
            float wheelRadius = 0.34f;
            float wheelWidth = 0.22f;

            float noseZ = -(len * 0.5f);
            float cowlZ = -(len * 0.16f);
            float roofFrontZ = -(len * 0.02f);
            float roofRearZ = len * 0.22f;
            float deckZ = len * 0.36f;
            float tailZ = len * 0.5f;

            // 1. Lower chassis waist & sculpted wheel well arches
            float waistY = (wheelRadius * 1.08f + beltY) * 0.5f;
            float waistH = beltY - wheelRadius * 1.08f;
            b.Box(new Vector3(0f, waistY, 0f), new Vector3(w * 0.94f, waistH, len * 0.94f), Body);

            float wheelZ = len * 0.31f;
            float archR = wheelRadius * 1.18f;
            float centerSillLen = (wheelZ - archR) * 2f;
            b.Box(new Vector3(0f, (sillY + waistY - waistH * 0.5f) * 0.5f, 0f),
                  new Vector3(w * 0.86f, (waistY - waistH * 0.5f) - sillY, centerSillLen), Body);

            float frontOverhangLen = Mathf.Abs((-wheelZ + archR) - noseZ);
            b.Box(new Vector3(0f, (sillY + waistY - waistH * 0.5f) * 0.5f, noseZ + frontOverhangLen * 0.5f),
                  new Vector3(w * 0.88f, (waistY - waistH * 0.5f) - sillY, frontOverhangLen), Body);

            float rearOverhangLen = Mathf.Abs(tailZ - (wheelZ + archR));
            b.Box(new Vector3(0f, (sillY + waistY - waistH * 0.5f) * 0.5f, tailZ - rearOverhangLen * 0.5f),
                  new Vector3(w * 0.88f, (waistY - waistH * 0.5f) - sillY, rearOverhangLen), Body);

            // Hood
            b.Frustum(
                new Vector3(0f, beltY, cowlZ), new Vector3(w * 0.96f, 0.12f, 0f),
                new Vector3(0f, beltY - 0.10f, noseZ), new Vector3(w * 0.88f, 0.16f, 0f),
                Body
            );

            // Windshield & Cabin
            b.Frustum(
                new Vector3(0f, beltY, cowlZ), new Vector3(w * 0.88f, 0.05f, 0f),
                new Vector3(0f, roofY, roofFrontZ), new Vector3(w * 0.72f, 0.05f, 0f),
                Glass
            );
            float cabinLen = roofRearZ - roofFrontZ;
            b.Box(new Vector3(0f, (beltY + roofY) * 0.5f, (roofFrontZ + roofRearZ) * 0.5f),
                  new Vector3(w * 0.80f, roofY - beltY, cabinLen), Glass);
            b.Box(new Vector3(0f, roofY + 0.02f, (roofFrontZ + roofRearZ) * 0.5f),
                  new Vector3(w * 0.74f, 0.04f, cabinLen * 0.96f), Body);

            // Backlight & Trunk
            b.Frustum(
                new Vector3(0f, roofY, roofRearZ), new Vector3(w * 0.72f, 0.05f, 0f),
                new Vector3(0f, beltY, deckZ), new Vector3(w * 0.86f, 0.05f, 0f),
                Glass
            );
            b.Box(new Vector3(0f, beltY - 0.02f, (deckZ + tailZ) * 0.5f),
                  new Vector3(w * 0.90f, 0.14f, tailZ - deckZ), Body);

            // NYPD Front Push Bumper (Bullbar)
            b.Box(new Vector3(0f, sillY + 0.16f, noseZ - 0.16f), new Vector3(0.92f, 0.42f, 0.08f), Body);
            b.Box(new Vector3(-0.35f, sillY + 0.22f, noseZ - 0.10f), new Vector3(0.08f, 0.46f, 0.14f), Body);
            b.Box(new Vector3(0.35f, sillY + 0.22f, noseZ - 0.10f), new Vector3(0.08f, 0.46f, 0.14f), Body);

            // Rooftop Emergency Lightbar (Red left / Blue right)
            float signZ = (roofFrontZ + roofRearZ) * 0.5f;
            b.Box(new Vector3(0f, roofY + 0.04f, signZ), new Vector3(0.98f, 0.04f, 0.18f), Body);
            b.Box(new Vector3(-0.28f, roofY + 0.12f, signZ), new Vector3(0.38f, 0.12f, 0.18f), PoliceRed);
            b.Box(new Vector3(0.28f, roofY + 0.12f, signZ), new Vector3(0.38f, 0.12f, 0.18f), PoliceBlue);

            // Lamps
            float lampX = w * 0.34f;
            b.Box(new Vector3(-lampX, beltY - 0.06f, noseZ - 0.08f), new Vector3(0.28f, 0.11f, 0.04f), Headlight);
            b.Box(new Vector3(lampX, beltY - 0.06f, noseZ - 0.08f), new Vector3(0.28f, 0.11f, 0.04f), Headlight);
            b.Box(new Vector3(-lampX, beltY - 0.04f, tailZ + 0.08f), new Vector3(0.28f, 0.10f, 0.04f), Taillight);
            b.Box(new Vector3(lampX, beltY - 0.04f, tailZ + 0.08f), new Vector3(0.28f, 0.10f, 0.04f), Taillight);

            // 4 Wheels
            float halfAxle = w * 0.5f - 0.04f;
            b.CylinderX(new Vector3(-halfAxle, wheelRadius, -wheelZ), wheelRadius, wheelWidth, 14, Tyre);
            b.CylinderX(new Vector3(halfAxle, wheelRadius, -wheelZ), wheelRadius, wheelWidth, 14, Tyre);
            b.CylinderX(new Vector3(-halfAxle, wheelRadius, wheelZ), wheelRadius, wheelWidth, 14, Tyre);
            b.CylinderX(new Vector3(halfAxle, wheelRadius, wheelZ), wheelRadius, wheelWidth, 14, Tyre);

            // Bright Alloy Rims (prominent hubcap offset flush with tyre sidewall)
            float rimRadius = wheelRadius * 0.65f;
            float rimOffset = wheelWidth * 0.50f;
            b.CylinderX(new Vector3(-halfAxle - rimOffset, wheelRadius, -wheelZ), rimRadius, 0.035f, 12, Alloy);
            b.CylinderX(new Vector3(halfAxle + rimOffset, wheelRadius, -wheelZ), rimRadius, 0.035f, 12, Alloy);
            b.CylinderX(new Vector3(-halfAxle - rimOffset, wheelRadius, wheelZ), rimRadius, 0.035f, 12, Alloy);
            b.CylinderX(new Vector3(halfAxle + rimOffset, wheelRadius, wheelZ), rimRadius, 0.035f, 12, Alloy);

            return b.ToMesh("VehiclePolice");
        }

        public static Mesh CreateTruck()
        {
            var b = new MeshBuilder();
            const float w = 2.45f;
            float wheelRadius = 0.44f;
            float wheelWidth = 0.28f;

            // 1. Driver's Cab (Z = -3.85 .. -1.65)
            b.Box(new Vector3(0f, 1.45f, -2.65f), new Vector3(w * 0.94f, 1.95f, 2.15f), Body);
            // Windshield & side glass
            b.Box(new Vector3(0f, 1.82f, -3.42f), new Vector3(w * 0.88f, 0.72f, 0.12f), Glass);
            b.Box(new Vector3(0f, 1.82f, -2.55f), new Vector3(w * 0.95f, 0.65f, 1.45f), Glass);

            // Front Grille & Heavy Bumper
            b.Box(new Vector3(0f, 0.62f, -3.76f), new Vector3(w * 0.96f, 0.38f, 0.22f), Body);
            b.Box(new Vector3(0f, 1.15f, -3.74f), new Vector3(w * 0.75f, 0.55f, 0.10f), Body);

            // 2. Large Box Freight Container (Z = -1.45 .. 3.85)
            b.Box(new Vector3(0f, 2.05f, 1.18f), new Vector3(w, 2.82f, 5.25f), Body);
            // Aerodynamic roof deflector above cab
            b.Frustum(
                new Vector3(0f, 2.45f, -1.55f), new Vector3(w * 0.90f, 0.05f, 0f),
                new Vector3(0f, 3.42f, -1.45f), new Vector3(w * 0.94f, 0.05f, 0f),
                Body
            );

            // 3. Side Cylindrical Fuel Tanks
            b.CylinderX(new Vector3(-w * 0.46f, 0.52f, -0.65f), 0.26f, 0.95f, 10, Body);
            b.CylinderX(new Vector3(w * 0.46f, 0.52f, -0.65f), 0.26f, 0.95f, 10, Body);

            // 4. Wheels: Single front axle, Dual rear axles (6 wheels total) with heavy alloy hubs
            float halfAxle = w * 0.5f - 0.04f;
            b.CylinderX(new Vector3(-halfAxle, wheelRadius, -2.60f), wheelRadius, wheelWidth, 12, Tyre);
            b.CylinderX(new Vector3(halfAxle, wheelRadius, -2.60f), wheelRadius, wheelWidth, 12, Tyre);

            // Dual rear tandem
            b.CylinderX(new Vector3(-halfAxle, wheelRadius, 1.45f), wheelRadius, wheelWidth * 1.3f, 12, Tyre);
            b.CylinderX(new Vector3(halfAxle, wheelRadius, 1.45f), wheelRadius, wheelWidth * 1.3f, 12, Tyre);
            b.CylinderX(new Vector3(-halfAxle, wheelRadius, 2.65f), wheelRadius, wheelWidth * 1.3f, 12, Tyre);
            b.CylinderX(new Vector3(halfAxle, wheelRadius, 2.65f), wheelRadius, wheelWidth * 1.3f, 12, Tyre);

            // Alloy Rims
            float rimRadius = wheelRadius * 0.58f;
            float rimOffset = wheelWidth * 0.52f;
            b.CylinderX(new Vector3(-halfAxle - rimOffset, wheelRadius, -2.60f), rimRadius, 0.02f, 10, Alloy);
            b.CylinderX(new Vector3(halfAxle + rimOffset, wheelRadius, -2.60f), rimRadius, 0.02f, 10, Alloy);
            b.CylinderX(new Vector3(-halfAxle - rimOffset, wheelRadius, 1.45f), rimRadius, 0.02f, 10, Alloy);
            b.CylinderX(new Vector3(halfAxle + rimOffset, wheelRadius, 1.45f), rimRadius, 0.02f, 10, Alloy);
            b.CylinderX(new Vector3(-halfAxle - rimOffset, wheelRadius, 2.65f), rimRadius, 0.02f, 10, Alloy);
            b.CylinderX(new Vector3(halfAxle + rimOffset, wheelRadius, 2.65f), rimRadius, 0.02f, 10, Alloy);

            // 5. Rear Cargo Door Frame & Impact Bumper
            b.Box(new Vector3(0f, 2.05f, 3.82f), new Vector3(w * 0.94f, 2.65f, 0.06f), Alloy);
            b.Box(new Vector3(0f, 0.52f, 3.84f), new Vector3(w * 0.96f, 0.28f, 0.14f), Body);

            // 6. Lights
            b.Box(new Vector3(-0.85f, 0.78f, -3.76f), new Vector3(0.34f, 0.18f, 0.06f), Headlight);
            b.Box(new Vector3(0.85f, 0.78f, -3.76f), new Vector3(0.34f, 0.18f, 0.06f), Headlight);
            b.Box(new Vector3(-0.95f, 0.65f, 3.86f), new Vector3(0.28f, 0.18f, 0.06f), Taillight);
            b.Box(new Vector3(0.95f, 0.65f, 3.86f), new Vector3(0.28f, 0.18f, 0.06f), Taillight);

            return b.ToMesh("VehicleTruck");
        }

        public static Mesh CreateBus()
        {
            var b = new MeshBuilder();
            const float w = 2.55f, len = 11.8f;
            float wheelRadius = 0.50f;
            float wheelWidth = 0.28f;
            float noseZ = -(len * 0.5f);
            float tailZ = len * 0.5f;

            // 1. Main Bus Body Shell (starts above wheel axle height to form distinct wheel arches)
            b.Box(new Vector3(0f, 1.86f, 0f), new Vector3(w, 2.56f, len), Body);

            // Lower side skirts (recessed to leave prominent wheel well cutouts for front & dual rear axles)
            float skirtW = w * 0.88f;
            float frontAxleZ = -(len * 0.33f);
            float rearAxle1Z = len * 0.24f;
            float rearAxle2Z = len * 0.36f;
            // Front overhang skirt
            b.Box(new Vector3(0f, 0.42f, (noseZ + frontAxleZ - 0.75f) * 0.5f), new Vector3(skirtW, 0.32f, (frontAxleZ - 0.75f) - noseZ), Body);
            // Mid wheelbase skirt
            b.Box(new Vector3(0f, 0.42f, (frontAxleZ + 0.75f + rearAxle1Z - 0.75f) * 0.5f), new Vector3(skirtW, 0.32f, (rearAxle1Z - 0.75f) - (frontAxleZ + 0.75f)), Body);
            // Rear overhang skirt
            b.Box(new Vector3(0f, 0.42f, (rearAxle2Z + 0.75f + tailZ) * 0.5f), new Vector3(skirtW, 0.32f, tailZ - (rearAxle2Z + 0.75f)), Body);

            // 2. Glass: Front Curved Windshield, Continuous Side Passenger Windows, and Rear Window
            b.Box(new Vector3(0f, 2.05f, noseZ - 0.04f), new Vector3(w * 0.94f, 1.15f, 0.10f), Glass);
            b.Box(new Vector3(0f, 2.22f, 0.25f), new Vector3(w + 0.02f, 0.85f, len * 0.84f), Glass);
            b.Box(new Vector3(0f, 2.22f, tailZ + 0.06f), new Vector3(w * 0.86f, 0.82f, 0.06f), Glass);

            // MTA livery: pearl-white shell with deep-blue belt and cyan ribbon on sides AND rear
            float sideX = w * 0.5f + 0.015f;
            b.Box(new Vector3(-sideX, 1.46f, 0.20f), new Vector3(0.035f, 0.34f, len * 0.82f), BusBlueStripe);
            b.Box(new Vector3(sideX, 1.46f, 0.20f), new Vector3(0.035f, 0.34f, len * 0.82f), BusBlueStripe);
            b.Box(new Vector3(-sideX, 1.16f, 0.20f), new Vector3(0.038f, 0.10f, len * 0.82f), BusCyanRibbon);
            b.Box(new Vector3(sideX, 1.16f, 0.20f), new Vector3(0.038f, 0.10f, len * 0.82f), BusCyanRibbon);

            // Rear wrap-around MTA stripes with clear relief
            b.Box(new Vector3(0f, 1.46f, tailZ + 0.08f), new Vector3(w * 0.96f, 0.34f, 0.06f), BusBlueStripe);
            b.Box(new Vector3(0f, 1.16f, tailZ + 0.08f), new Vector3(w * 0.96f, 0.10f, 0.06f), BusCyanRibbon);

            // 3. Destination Sign Boxes ("M15 SELECT BUS") front and rear
            b.Box(new Vector3(0f, 2.88f, noseZ - 0.06f), new Vector3(1.55f, 0.28f, 0.14f), Glass);
            b.Box(new Vector3(0f, 2.82f, tailZ + 0.08f), new Vector3(1.35f, 0.24f, 0.06f), Glass);

            // Rear Engine Compartment Vents & Grille
            b.Box(new Vector3(0f, 0.82f, tailZ + 0.08f), new Vector3(1.4f, 0.48f, 0.06f), Alloy);
            // Rear Illuminated NYC License Plate
            b.Box(new Vector3(0f, 0.50f, tailZ + 0.09f), new Vector3(0.42f, 0.16f, 0.04f), TaxiSign);

            // 4. Rooftop AC & Environmental Ventilation Units
            b.Box(new Vector3(0f, 3.28f, -1.8f), new Vector3(1.8f, 0.32f, 2.8f), Body);
            b.Box(new Vector3(0f, 3.28f, 2.2f), new Vector3(1.8f, 0.32f, 2.8f), Body);

            // 5. Front Bicycle Rack & Impact Bumpers
            b.Box(new Vector3(0f, 0.62f, noseZ - 0.24f), new Vector3(1.75f, 0.34f, 0.26f), Body);
            b.Box(new Vector3(0f, 0.52f, tailZ + 0.08f), new Vector3(w * 0.96f, 0.32f, 0.16f), Body);

            // 6. Cylindrical Wheels with Alloy Rims (Front axle & Dual rear axles)
            float halfAxle = w * 0.5f - 0.02f;
            b.CylinderX(new Vector3(-halfAxle, wheelRadius, frontAxleZ), wheelRadius, wheelWidth, 12, Tyre);
            b.CylinderX(new Vector3(halfAxle, wheelRadius, frontAxleZ), wheelRadius, wheelWidth, 12, Tyre);
            b.CylinderX(new Vector3(-halfAxle, wheelRadius, rearAxle1Z), wheelRadius, wheelWidth, 12, Tyre);
            b.CylinderX(new Vector3(halfAxle, wheelRadius, rearAxle1Z), wheelRadius, wheelWidth, 12, Tyre);
            b.CylinderX(new Vector3(-halfAxle, wheelRadius, rearAxle2Z), wheelRadius, wheelWidth, 12, Tyre);
            b.CylinderX(new Vector3(halfAxle, wheelRadius, rearAxle2Z), wheelRadius, wheelWidth, 12, Tyre);

            float rimRadius = wheelRadius * 0.58f;
            float rimOffset = wheelWidth * 0.50f;
            b.CylinderX(new Vector3(-halfAxle - rimOffset, wheelRadius, frontAxleZ), rimRadius, 0.02f, 10, Alloy);
            b.CylinderX(new Vector3(halfAxle + rimOffset, wheelRadius, frontAxleZ), rimRadius, 0.02f, 10, Alloy);
            b.CylinderX(new Vector3(-halfAxle - rimOffset, wheelRadius, rearAxle1Z), rimRadius, 0.02f, 10, Alloy);
            b.CylinderX(new Vector3(halfAxle + rimOffset, wheelRadius, rearAxle1Z), rimRadius, 0.02f, 10, Alloy);
            b.CylinderX(new Vector3(-halfAxle - rimOffset, wheelRadius, rearAxle2Z), rimRadius, 0.02f, 10, Alloy);
            b.CylinderX(new Vector3(halfAxle + rimOffset, wheelRadius, rearAxle2Z), rimRadius, 0.02f, 10, Alloy);

            // 7. Headlights & Taillights
            b.Box(new Vector3(-0.92f, 0.90f, noseZ - 0.06f), new Vector3(0.36f, 0.22f, 0.06f), Headlight);
            b.Box(new Vector3(0.92f, 0.90f, noseZ - 0.06f), new Vector3(0.36f, 0.22f, 0.06f), Headlight);
            b.Box(new Vector3(-0.92f, 1.10f, tailZ + 0.09f), new Vector3(0.36f, 0.32f, 0.06f), Taillight);
            b.Box(new Vector3(0.92f, 1.10f, tailZ + 0.09f), new Vector3(0.36f, 0.32f, 0.06f), Taillight);

            return b.ToMesh("VehicleBus");
        }

        // ------------------------------------------------------------------ mesh builder

        sealed class MeshBuilder
        {
            readonly List<Vector3> _v = new List<Vector3>(512);
            readonly List<Vector3> _n = new List<Vector3>(512);
            readonly List<Vector2> _uv0 = new List<Vector2>(512);
            readonly List<Vector2> _uv1 = new List<Vector2>(512);
            readonly List<int> _t = new List<int>(1024);

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
                Quad(new Vector3(min.x, min.y, max.z), new Vector3(min.x, min.y, min.z),
                     new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z), Vector3.down, materialId);
            }

            public void Frustum(Vector3 centerBack, Vector3 sizeBack, Vector3 centerFront, Vector3 sizeFront, int materialId)
            {
                float hbW = sizeBack.x * 0.5f, hbH = sizeBack.y * 0.5f;
                float hfW = sizeFront.x * 0.5f, hfH = sizeFront.y * 0.5f;

                Vector3 bL = centerBack + new Vector3(-hbW, -hbH, 0f);
                Vector3 bR = centerBack + new Vector3(hbW, -hbH, 0f);
                Vector3 tL = centerBack + new Vector3(-hbW, hbH, 0f);
                Vector3 tR = centerBack + new Vector3(hbW, hbH, 0f);

                Vector3 fbL = centerFront + new Vector3(-hfW, -hfH, 0f);
                Vector3 fbR = centerFront + new Vector3(hfW, -hfH, 0f);
                Vector3 ftL = centerFront + new Vector3(-hfW, hfH, 0f);
                Vector3 ftR = centerFront + new Vector3(hfW, hfH, 0f);

                Vector3 nTop = Vector3.Cross(ftR - tR, tL - tR).normalized;
                Quad(tL, tR, ftR, ftL, nTop, materialId);

                Vector3 nBottom = Vector3.Cross(bL - bR, fbR - bR).normalized;
                Quad(bL, fbL, fbR, bR, nBottom, materialId);

                Vector3 nLeft = Vector3.Cross(ftL - tL, bL - tL).normalized;
                Quad(tL, ftL, fbL, bL, nLeft, materialId);

                Vector3 nRight = Vector3.Cross(bR - tR, ftR - tR).normalized;
                Quad(tR, bR, fbR, ftR, nRight, materialId);
            }

            public void CylinderX(Vector3 center, float radius, float width, int segments, int materialId)
            {
                float hw = width * 0.5f;
                float xL = center.x - hw;
                float xR = center.x + hw;

                for (int i = 0; i < segments; i++)
                {
                    float a0 = (i / (float)segments) * Mathf.PI * 2f;
                    float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;

                    float y0 = Mathf.Sin(a0) * radius;
                    float z0 = Mathf.Cos(a0) * radius;
                    float y1 = Mathf.Sin(a1) * radius;
                    float z1 = Mathf.Cos(a1) * radius;

                    Vector3 p0L = new Vector3(xL, center.y + y0, center.z + z0);
                    Vector3 p0R = new Vector3(xR, center.y + y0, center.z + z0);
                    Vector3 p1R = new Vector3(xR, center.y + y1, center.z + z1);
                    Vector3 p1L = new Vector3(xL, center.y + y1, center.z + z1);

                    Vector3 normTread = new Vector3(0f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f).normalized;
                    Quad(p0L, p0R, p1R, p1L, normTread, materialId);

                    // End caps (clean non-degenerate radial triangles)
                    Vector3 centerL = new Vector3(xL, center.y, center.z);
                    Vector3 centerR = new Vector3(xR, center.y, center.z);
                    Triangle(centerL, p1L, p0L, Vector3.left, materialId);
                    Triangle(centerR, p0R, p1R, Vector3.right, materialId);
                }
            }

            void Triangle(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 n, int materialId)
            {
                int i0 = Add(p0, n, materialId);
                int i1 = Add(p1, n, materialId);
                int i2 = Add(p2, n, materialId);

                Vector3 cross = Vector3.Cross(_v[i1] - _v[i0], _v[i2] - _v[i0]);
                if (Vector3.Dot(cross, n) >= 0f)
                {
                    _t.Add(i0); _t.Add(i1); _t.Add(i2);
                }
                else
                {
                    _t.Add(i0); _t.Add(i2); _t.Add(i1);
                }
            }

            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 n, int materialId)
            {
                int i0 = Add(p0, n, materialId);
                int i1 = Add(p1, n, materialId);
                int i2 = Add(p2, n, materialId);
                int i3 = Add(p3, n, materialId);

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
