using System;
using System.Collections.Generic;
using UnityEngine;
using Nyc.Core;

namespace Nyc.Gameplay
{
    /// <summary>
    /// Procedural visual component that animates the articulated pedestrian's limbs.
    /// Runs walk/sprint cycle, arm swings, knee bending, and air jump pose.
    /// </summary>
    public sealed class PedestrianVisual : MonoBehaviour
    {
        public PedestrianController controller;
        public Transform head;
        public Transform torso;
        public Transform leftArm, rightArm;
        public Transform leftLeg, rightLeg;

        float _gaitPhase;

        void LateUpdate()
        {
            if (!controller) return;

            float speed = controller.Speed;
            bool grounded = controller.IsGrounded;

            if (grounded && Mathf.Abs(speed) > 0.05f)
            {
                _gaitPhase += speed * Time.deltaTime * 5.0f;
            }

            float swing = Mathf.Sin(_gaitPhase);
            float armSwing = swing * Mathf.Clamp01(Mathf.Abs(speed) / 4.4f) * 35.0f;
            float legSwing = swing * Mathf.Clamp01(Mathf.Abs(speed) / 4.4f) * 38.0f;

            if (!grounded)
            {
                // Airborne jump pose: arms raised, legs slightly tucked back
                if (leftArm) leftArm.localRotation = Quaternion.Euler(-45f, 0f, -20f);
                if (rightArm) rightArm.localRotation = Quaternion.Euler(-45f, 0f, 20f);
                if (leftLeg) leftLeg.localRotation = Quaternion.Euler(25f, 0f, 0f);
                if (rightLeg) rightLeg.localRotation = Quaternion.Euler(20f, 0f, 0f);
            }
            else
            {
                // Grounded walk / run gait
                if (leftArm) leftArm.localRotation = Quaternion.Euler(-armSwing, 0f, 0f);
                if (rightArm) rightArm.localRotation = Quaternion.Euler(armSwing, 0f, 0f);
                if (leftLeg) leftLeg.localRotation = Quaternion.Euler(legSwing, 0f, 0f);
                if (rightLeg) rightLeg.localRotation = Quaternion.Euler(-legSwing, 0f, 0f);
            }

            if (torso) torso.localPosition = new Vector3(0f, 0.95f + (grounded ? Mathf.Abs(Mathf.Sin(_gaitPhase * 2f)) * 0.04f : 0f), 0f);
        }
    }

    /// <summary>
    /// Procedural visual component for the cyclist and bicycle.
    /// Steers fork, leans frame into turns, rotates cranks/pedals and spins wheels.
    /// </summary>
    public sealed class BikeVisual : MonoBehaviour
    {
        public BikeController controller;
        public Transform frameRoot;
        public Transform forkAndHandlebars;
        public Transform frontWheel, rearWheel;
        public Transform crank;

        void LateUpdate()
        {
            if (!controller) return;

            // Roll wheels with spin
            float wheelSpinDeg = -controller.WheelSpin * Mathf.Rad2Deg;
            if (frontWheel) frontWheel.localRotation = Quaternion.Euler(wheelSpinDeg, 0f, 0f);
            if (rearWheel) rearWheel.localRotation = Quaternion.Euler(wheelSpinDeg, 0f, 0f);

            // Crank and pedal rotation
            if (crank) crank.localRotation = Quaternion.Euler(-controller.PedalPhase * Mathf.Rad2Deg, 0f, 0f);

            // Lean entire bicycle and rider into turns
            if (frameRoot)
            {
                float leanDeg = -controller.LeanAngle * Mathf.Rad2Deg;
                frameRoot.localRotation = Quaternion.Euler(0f, 0f, leanDeg);
            }
        }
    }

    /// <summary>
    /// Procedural visual component for the sports car / NYC taxi.
    /// Steers front wheels, spins wheels, and applies pitch/roll/heave suspension dynamics.
    /// </summary>
    public sealed class CarVisual : MonoBehaviour
    {
        public CarController controller;
        public Transform chassisRoot;
        public Transform frontLeftWheel, frontRightWheel;
        public Transform rearLeftWheel, rearRightWheel;

        void LateUpdate()
        {
            if (!controller) return;

            // Suspension attitude (pitch under accel/brake, roll on turns, heave on bumps)
            if (chassisRoot)
            {
                float pitchDeg = controller.ChassisPitch * Mathf.Rad2Deg;
                float rollDeg = -controller.ChassisRoll * Mathf.Rad2Deg;
                chassisRoot.localPosition = new Vector3(0f, controller.HeaveY, 0f);
                chassisRoot.localRotation = Quaternion.Euler(pitchDeg, 0f, rollDeg);
            }

            // Steer front wheels
            float steerDeg = -controller.SteerAngle * Mathf.Rad2Deg;
            float spinDeg = -controller.WheelSpin * Mathf.Rad2Deg;

            if (frontLeftWheel)
                frontLeftWheel.localRotation = Quaternion.Euler(0f, steerDeg, 0f) * Quaternion.Euler(spinDeg, 0f, 0f);
            if (frontRightWheel)
                frontRightWheel.localRotation = Quaternion.Euler(0f, steerDeg, 0f) * Quaternion.Euler(spinDeg, 0f, 0f);

            // Rear wheels spin only
            if (rearLeftWheel) rearLeftWheel.localRotation = Quaternion.Euler(spinDeg, 0f, 0f);
            if (rearRightWheel) rearRightWheel.localRotation = Quaternion.Euler(spinDeg, 0f, 0f);
        }
    }

    /// <summary>
    /// Procedural visual component for the sightseeing helicopter.
    /// Spins main rotor and tail rotor at high RPM, and tilts fuselage with flight attitude.
    /// </summary>
    public sealed class HelicopterVisual : MonoBehaviour
    {
        public HelicopterController controller;
        public Transform fuselageRoot;
        public Transform mainRotor;
        public Transform tailRotor;

        float _mainRotorAngle;
        float _tailRotorAngle;

        void LateUpdate()
        {
            if (!controller) return;

            // Rotors spin continuously at high RPM
            float dt = Time.deltaTime;
            _mainRotorAngle = (_mainRotorAngle + dt * 1440f) % 360f;
            _tailRotorAngle = (_tailRotorAngle + dt * 2880f) % 360f;

            if (mainRotor) mainRotor.localRotation = Quaternion.Euler(0f, _mainRotorAngle, 0f);
            if (tailRotor) tailRotor.localRotation = Quaternion.Euler(_tailRotorAngle, 0f, 0f);

            // Fuselage flight attitude & bob
            if (fuselageRoot)
            {
                float pitchDeg = controller.PitchAngle * Mathf.Rad2Deg;
                float rollDeg = -controller.RollAngle * Mathf.Rad2Deg;
                fuselageRoot.localPosition = new Vector3(0f, controller.Bob, 0f);
                fuselageRoot.localRotation = Quaternion.Euler(pitchDeg, 0f, rollDeg);
            }
        }
    }

    /// <summary>
    /// Procedural Actor Mesh Factory. Generates high-fidelity procedural 3D models and rigs
    /// for all 4 controllable actors (Pedestrian, Cyclist, Car, Helicopter) without external assets.
    /// Port of js/actor-models.js and js/pedestrians.js.
    /// </summary>
    public static class ActorModelFactory
    {
        public static void AttachActorModels(ThirdPersonControllerManager manager)
        {
            if (!manager) return;

            if (manager.person) BuildPedestrianActor(manager.person);
            if (manager.bike) BuildCyclistActor(manager.bike);
            if (manager.car) BuildCarActor(manager.car, isTaxi: true);
            if (manager.helicopter) BuildHelicopterActor(manager.helicopter);
        }

        // =========================================================================================
        // 1. PEDESTRIAN ACTOR
        // =========================================================================================

        public static void BuildPedestrianActor(PedestrianController controller)
        {
            if (!controller) return;

            Transform existing = controller.transform.Find("PedestrianModel");
            if (existing) UnityEngine.Object.Destroy(existing.gameObject);

            var root = new GameObject("PedestrianModel");
            root.transform.SetParent(controller.transform, false);

            var vis = root.AddComponent<PedestrianVisual>();
            vis.controller = controller;

            // Torso root
            var torsoGo = new GameObject("Torso");
            torsoGo.transform.SetParent(root.transform, false);
            torsoGo.transform.localPosition = new Vector3(0f, 0.95f, 0f);
            vis.torso = torsoGo.transform;

            Material shirtMat = CreateMaterial("PedestrianShirt", new Color(0.18f, 0.42f, 0.76f), 0.35f, 0.0f);
            Material skinMat  = CreateMaterial("PedestrianSkin", new Color(0.88f, 0.72f, 0.58f), 0.30f, 0.0f);
            Material hairMat  = CreateMaterial("PedestrianHair", new Color(0.12f, 0.10f, 0.09f), 0.20f, 0.0f);
            Material pantsMat = CreateMaterial("PedestrianPants", new Color(0.14f, 0.22f, 0.38f), 0.35f, 0.0f);
            Material shoeMat  = CreateMaterial("PedestrianShoe", new Color(0.85f, 0.85f, 0.85f), 0.40f, 0.0f);

            // Torso mesh (jacket / shirt)
            AddMeshChild(torsoGo, "TorsoMesh", CreateBoxMesh(0.36f, 0.52f, 0.22f), shirtMat);

            // Head & Neck
            var headGo = new GameObject("Head");
            headGo.transform.SetParent(torsoGo.transform, false);
            headGo.transform.localPosition = new Vector3(0f, 0.38f, 0f);
            vis.head = headGo.transform;
            AddMeshChild(headGo, "HeadMesh", CreateBoxMesh(0.20f, 0.24f, 0.22f), skinMat);
            var hairGo = AddMeshChild(headGo, "HairMesh", CreateBoxMesh(0.22f, 0.09f, 0.23f), hairMat);
            hairGo.transform.localPosition = new Vector3(0f, 0.10f, -0.01f);

            // Left Arm (Sleeve + Hand)
            var leftArmGo = new GameObject("LeftArm");
            leftArmGo.transform.SetParent(torsoGo.transform, false);
            leftArmGo.transform.localPosition = new Vector3(-0.24f, 0.18f, 0f);
            vis.leftArm = leftArmGo.transform;
            var lArmMesh = AddMeshChild(leftArmGo, "Sleeve", CreateBoxMesh(0.11f, 0.42f, 0.12f), shirtMat);
            lArmMesh.transform.localPosition = new Vector3(0f, -0.18f, 0f);
            var lHandMesh = AddMeshChild(leftArmGo, "Hand", CreateBoxMesh(0.09f, 0.12f, 0.10f), skinMat);
            lHandMesh.transform.localPosition = new Vector3(0f, -0.42f, 0f);

            // Right Arm (Sleeve + Hand)
            var rightArmGo = new GameObject("RightArm");
            rightArmGo.transform.SetParent(torsoGo.transform, false);
            rightArmGo.transform.localPosition = new Vector3(0.24f, 0.18f, 0f);
            vis.rightArm = rightArmGo.transform;
            var rArmMesh = AddMeshChild(rightArmGo, "Sleeve", CreateBoxMesh(0.11f, 0.42f, 0.12f), shirtMat);
            rArmMesh.transform.localPosition = new Vector3(0f, -0.18f, 0f);
            var rHandMesh = AddMeshChild(rightArmGo, "Hand", CreateBoxMesh(0.09f, 0.12f, 0.10f), skinMat);
            rHandMesh.transform.localPosition = new Vector3(0f, -0.42f, 0f);

            // Left Leg (Pants + Shoe)
            var leftLegGo = new GameObject("LeftLeg");
            leftLegGo.transform.SetParent(root.transform, false);
            leftLegGo.transform.localPosition = new Vector3(-0.11f, 0.72f, 0f);
            vis.leftLeg = leftLegGo.transform;
            var lLegMesh = AddMeshChild(leftLegGo, "Pants", CreateBoxMesh(0.14f, 0.58f, 0.15f), pantsMat);
            lLegMesh.transform.localPosition = new Vector3(0f, -0.27f, 0f);
            var lShoeMesh = AddMeshChild(leftLegGo, "Shoe", CreateBoxMesh(0.14f, 0.14f, 0.20f), shoeMat);
            lShoeMesh.transform.localPosition = new Vector3(0f, -0.61f, -0.02f);

            // Right Leg (Pants + Shoe)
            var rightLegGo = new GameObject("RightLeg");
            rightLegGo.transform.SetParent(root.transform, false);
            rightLegGo.transform.localPosition = new Vector3(0.11f, 0.72f, 0f);
            vis.rightLeg = rightLegGo.transform;
            var rLegMesh = AddMeshChild(rightLegGo, "Pants", CreateBoxMesh(0.14f, 0.58f, 0.15f), pantsMat);
            rLegMesh.transform.localPosition = new Vector3(0f, -0.27f, 0f);
            var rShoeMesh = AddMeshChild(rightLegGo, "Shoe", CreateBoxMesh(0.14f, 0.14f, 0.20f), shoeMat);
            rShoeMesh.transform.localPosition = new Vector3(0f, -0.61f, -0.02f);
        }

        // =========================================================================================
        // 2. CYCLIST ACTOR
        // =========================================================================================

        public static void BuildCyclistActor(BikeController controller)
        {
            if (!controller) return;

            Transform existing = controller.transform.Find("CyclistModel");
            if (existing) UnityEngine.Object.Destroy(existing.gameObject);

            var root = new GameObject("CyclistModel");
            root.transform.SetParent(controller.transform, false);

            var vis = root.AddComponent<BikeVisual>();
            vis.controller = controller;

            // Frame root that leans into turns
            var frameGo = new GameObject("FrameRoot");
            frameGo.transform.SetParent(root.transform, false);
            vis.frameRoot = frameGo.transform;

            Material frameMat = CreateMaterial("BikeFrameMat", new Color(0.12f, 0.65f, 0.92f)); // CitiBike blue
            Material wheelMat = CreateMaterial("BikeWheelMat", new Color(0.15f, 0.15f, 0.16f));
            Material riderMat = CreateMaterial("CyclistClothing", new Color(0.22f, 0.24f, 0.28f));

            // Main diamond bicycle frame
            AddMeshChild(frameGo, "FrameMesh", CreateBikeFrameMesh(), frameMat);

            // Crank and pedals
            var crankGo = new GameObject("Crank");
            crankGo.transform.SetParent(frameGo.transform, false);
            crankGo.transform.localPosition = new Vector3(0f, 0.28f, 0f);
            vis.crank = crankGo.transform;
            AddMeshChild(crankGo, "CrankMesh", CreateBoxMesh(0.18f, 0.04f, 0.04f), frameMat);

            // Rear wheel (at +Z rear)
            var rearWheelGo = new GameObject("RearWheel");
            rearWheelGo.transform.SetParent(frameGo.transform, false);
            rearWheelGo.transform.localPosition = new Vector3(0f, 0.35f, 0.52f);
            vis.rearWheel = rearWheelGo.transform;
            AddMeshChild(rearWheelGo, "RearWheelMesh", CreateWheelDiscMesh(radius: 0.35f, width: 0.06f), wheelMat);

            // Front fork & handlebars (at -Z front)
            var forkGo = new GameObject("ForkAndHandlebars");
            forkGo.transform.SetParent(frameGo.transform, false);
            forkGo.transform.localPosition = new Vector3(0f, 0.35f, -0.55f);
            vis.forkAndHandlebars = forkGo.transform;

            // Front wheel attached to fork
            var frontWheelGo = new GameObject("FrontWheel");
            frontWheelGo.transform.SetParent(forkGo.transform, false);
            vis.frontWheel = frontWheelGo.transform;
            AddMeshChild(frontWheelGo, "FrontWheelMesh", CreateWheelDiscMesh(radius: 0.35f, width: 0.06f), wheelMat);

            // Rider seated on saddle
            var riderGo = new GameObject("Rider");
            riderGo.transform.SetParent(frameGo.transform, false);
            riderGo.transform.localPosition = new Vector3(0f, 0.85f, 0.05f);
            riderGo.transform.localRotation = Quaternion.Euler(18f, 0f, 0f); // Leaned forward
            AddMeshChild(riderGo, "RiderBody", CreateBoxMesh(0.32f, 0.58f, 0.24f), riderMat);

            var riderHead = new GameObject("RiderHead");
            riderHead.transform.SetParent(riderGo.transform, false);
            riderHead.transform.localPosition = new Vector3(0f, 0.40f, -0.06f);
            AddMeshChild(riderHead, "Helmet", CreateBoxMesh(0.20f, 0.22f, 0.26f), frameMat);
        }

        // =========================================================================================
        // 3. CAR ACTOR
        // =========================================================================================

        public static void BuildCarActor(CarController controller, bool isTaxi = true)
        {
            if (!controller) return;

            Transform existing = controller.transform.Find("CarModel");
            if (existing) UnityEngine.Object.Destroy(existing.gameObject);

            var root = new GameObject("CarModel");
            root.transform.SetParent(controller.transform, false);

            var vis = root.AddComponent<CarVisual>();
            vis.controller = controller;

            // Chassis root that responds to suspension pitch/roll/heave
            var chassisGo = new GameObject("ChassisRoot");
            chassisGo.transform.SetParent(root.transform, false);
            vis.chassisRoot = chassisGo.transform;

            Material paintMat = CreateMaterial("CarPaint", isTaxi ? new Color(1.0f, 0.745f, 0.08f) : new Color(0.85f, 0.12f, 0.15f), 0.85f, 0.25f);
            Material glassMat = CreateMaterial("CarGlass", new Color(0.06f, 0.08f, 0.11f), 0.95f, 0.10f);
            Material wheelMat = CreateMaterial("CarWheel", new Color(0.12f, 0.12f, 0.14f), 0.25f, 0.0f);
            Material trimMat  = CreateMaterial("CarTrim", new Color(0.20f, 0.20f, 0.22f), 0.70f, 0.30f);
            Material headlightMat = CreateMaterial("CarHeadlight", new Color(1.0f, 0.96f, 0.88f), 0.9f, 0f, new Color(2.5f, 2.3f, 1.8f));
            Material taillightMat = CreateMaterial("CarTaillight", new Color(0.85f, 0.06f, 0.04f), 0.8f, 0f, new Color(2.0f, 0.15f, 0.1f));

            // Sculpted aerodynamic car body & raked cabin
            AddMeshChild(chassisGo, "CarBody", CreateCarBodyMesh(isTaxi), paintMat);
            AddMeshChild(chassisGo, "CarCabin", CreateCarCabinMesh(), glassMat);

            // Front and rear bumpers
            AddMeshChild(chassisGo, "BumperFront", CreateBoxMesh(1.72f, 0.26f, 0.16f), trimMat)
                .transform.localPosition = new Vector3(0f, 0.45f, -2.18f);
            AddMeshChild(chassisGo, "BumperRear", CreateBoxMesh(1.72f, 0.28f, 0.16f), trimMat)
                .transform.localPosition = new Vector3(0f, 0.48f, 2.18f);

            // Front Headlights
            AddMeshChild(chassisGo, "Headlight_L", CreateBoxMesh(0.28f, 0.11f, 0.04f), headlightMat)
                .transform.localPosition = new Vector3(-0.60f, 0.65f, -2.19f);
            AddMeshChild(chassisGo, "Headlight_R", CreateBoxMesh(0.28f, 0.11f, 0.04f), headlightMat)
                .transform.localPosition = new Vector3(0.60f, 0.65f, -2.19f);

            // Rear Taillights
            AddMeshChild(chassisGo, "Taillight_L", CreateBoxMesh(0.28f, 0.10f, 0.04f), taillightMat)
                .transform.localPosition = new Vector3(-0.60f, 0.70f, 2.19f);
            AddMeshChild(chassisGo, "Taillight_R", CreateBoxMesh(0.28f, 0.10f, 0.04f), taillightMat)
                .transform.localPosition = new Vector3(0.60f, 0.70f, 2.19f);

            // Side Mirrors
            AddMeshChild(chassisGo, "Mirror_L", CreateBoxMesh(0.14f, 0.08f, 0.10f), paintMat)
                .transform.localPosition = new Vector3(-0.95f, 0.86f, -0.72f);
            AddMeshChild(chassisGo, "Mirror_R", CreateBoxMesh(0.14f, 0.08f, 0.10f), paintMat)
                .transform.localPosition = new Vector3(0.95f, 0.86f, -0.72f);

            if (isTaxi)
            {
                Material taxiSignMat = CreateMaterial("TaxiSignMat", new Color(1.0f, 0.82f, 0.15f), 0.85f, 0f, new Color(1.6f, 1.3f, 0.25f));
                var signMount = AddMeshChild(chassisGo, "TaxiSignMount", CreateBoxMesh(0.62f, 0.04f, 0.24f), trimMat);
                signMount.transform.localPosition = new Vector3(0f, 1.44f, 0.12f);
                var signGo = AddMeshChild(chassisGo, "TaxiSign", CreateBoxMesh(0.54f, 0.14f, 0.20f), taxiSignMat);
                signGo.transform.localPosition = new Vector3(0f, 1.53f, 0.12f);
            }

            // 4 Wheels
            float frontZ = -1.35f;
            float rearZ = 1.35f;
            float halfX = 0.82f;
            float wheelY = 0.35f;

            vis.frontLeftWheel = CreateWheelChild(root.transform, "Wheel_FL", new Vector3(-halfX, wheelY, frontZ), wheelMat);
            vis.frontRightWheel = CreateWheelChild(root.transform, "Wheel_FR", new Vector3(halfX, wheelY, frontZ), wheelMat);
            vis.rearLeftWheel = CreateWheelChild(root.transform, "Wheel_RL", new Vector3(-halfX, wheelY, rearZ), wheelMat);
            vis.rearRightWheel = CreateWheelChild(root.transform, "Wheel_RR", new Vector3(halfX, wheelY, rearZ), wheelMat);
        }

        // =========================================================================================
        // 4. HELICOPTER ACTOR
        // =========================================================================================

        public static void BuildHelicopterActor(HelicopterController controller)
        {
            if (!controller) return;

            Transform existing = controller.transform.Find("HelicopterModel");
            if (existing) UnityEngine.Object.Destroy(existing.gameObject);

            var root = new GameObject("HelicopterModel");
            root.transform.SetParent(controller.transform, false);

            var vis = root.AddComponent<HelicopterVisual>();
            vis.controller = controller;

            // Fuselage root for attitude tilt
            var fuselageGo = new GameObject("FuselageRoot");
            fuselageGo.transform.SetParent(root.transform, false);
            vis.fuselageRoot = fuselageGo.transform;

            Material heliBodyMat = CreateMaterial("HeliBody", new Color(0.92f, 0.15f, 0.15f)); // NY sightseeing red
            Material heliGlassMat = CreateMaterial("HeliGlass", new Color(0.15f, 0.22f, 0.32f));
            Material heliMetalMat = CreateMaterial("HeliMetal", new Color(0.25f, 0.27f, 0.30f));

            // Main Fuselage & Tail Boom
            AddMeshChild(fuselageGo, "Fuselage", CreateHelicopterFuselageMesh(), heliBodyMat);
            AddMeshChild(fuselageGo, "Windscreen", CreateHelicopterGlassMesh(), heliGlassMat);
            AddMeshChild(fuselageGo, "LandingSkids", CreateHelicopterSkidsMesh(), heliMetalMat);

            // 4-Bladed Main Rotor (on top of mast at y = 2.45m)
            var mainRotorGo = new GameObject("MainRotor");
            mainRotorGo.transform.SetParent(fuselageGo.transform, false);
            mainRotorGo.transform.localPosition = new Vector3(0f, 2.45f, -0.4f);
            vis.mainRotor = mainRotorGo.transform;
            AddMeshChild(mainRotorGo, "MainRotorBlades", CreateMainRotorMesh(), heliMetalMat);

            // Tail Rotor (at rear vertical stabilizer at z = 4.8m, y = 1.4m)
            var tailRotorGo = new GameObject("TailRotor");
            tailRotorGo.transform.SetParent(fuselageGo.transform, false);
            tailRotorGo.transform.localPosition = new Vector3(0.18f, 1.45f, 4.85f);
            vis.tailRotor = tailRotorGo.transform;
            AddMeshChild(tailRotorGo, "TailRotorBlades", CreateTailRotorMesh(), heliMetalMat);
        }

        // =========================================================================================
        // PROCEDURAL MESH GENERATORS
        // =========================================================================================

        static Mesh CreateBoxMesh(float width, float height, float length)
        {
            var mesh = new Mesh { name = "BoxMesh" };
            float hw = width * 0.5f;
            float hh = height * 0.5f;
            float hl = length * 0.5f;

            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var u = new List<Vector2>();
            var t = new List<int>();

            void AddQuad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 norm)
            {
                int baseIdx = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                n.Add(norm); n.Add(norm); n.Add(norm); n.Add(norm);
                u.Add(new Vector2(0, 0)); u.Add(new Vector2(1, 0));
                u.Add(new Vector2(1, 1)); u.Add(new Vector2(0, 1));
                t.Add(baseIdx); t.Add(baseIdx + 1); t.Add(baseIdx + 2);
                t.Add(baseIdx); t.Add(baseIdx + 2); t.Add(baseIdx + 3);
            }

            // 6 Faces
            AddQuad(new Vector3(-hw, -hh, -hl), new Vector3(hw, -hh, -hl), new Vector3(hw, hh, -hl), new Vector3(-hw, hh, -hl), Vector3.back);
            AddQuad(new Vector3(hw, -hh, hl), new Vector3(-hw, -hh, hl), new Vector3(-hw, hh, hl), new Vector3(hw, hh, hl), Vector3.forward);
            AddQuad(new Vector3(-hw, hh, -hl), new Vector3(hw, hh, -hl), new Vector3(hw, hh, hl), new Vector3(-hw, hh, hl), Vector3.up);
            AddQuad(new Vector3(-hw, -hh, hl), new Vector3(hw, -hh, hl), new Vector3(hw, -hh, -hl), new Vector3(-hw, -hh, -hl), Vector3.down);
            AddQuad(new Vector3(-hw, -hh, hl), new Vector3(-hw, -hh, -hl), new Vector3(-hw, hh, -hl), new Vector3(-hw, hh, hl), Vector3.left);
            AddQuad(new Vector3(hw, -hh, -hl), new Vector3(hw, -hh, hl), new Vector3(hw, hh, hl), new Vector3(hw, hh, -hl), Vector3.right);

            mesh.SetVertices(v);
            mesh.SetNormals(n);
            SetAllUVs(mesh, u, v.Count);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateWheelDiscMesh(float radius, float width)
        {
            var mesh = new Mesh { name = "WheelDiscMesh" };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var u = new List<Vector2>();
            var t = new List<int>();

            int segments = 16;
            float hw = width * 0.5f;

            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;

                float y0 = Mathf.Sin(a0) * radius;
                float z0 = Mathf.Cos(a0) * radius;
                float y1 = Mathf.Sin(a1) * radius;
                float z1 = Mathf.Cos(a1) * radius;

                // Tyre tread outer quad
                int baseIdx = v.Count;
                v.Add(new Vector3(-hw, y0, z0));
                v.Add(new Vector3(hw, y0, z0));
                v.Add(new Vector3(hw, y1, z1));
                v.Add(new Vector3(-hw, y1, z1));

                Vector3 norm = new Vector3(0f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f).normalized;
                for (int k = 0; k < 4; k++) n.Add(norm);
                u.Add(new Vector2(0, 0)); u.Add(new Vector2(1, 0));
                u.Add(new Vector2(1, 1)); u.Add(new Vector2(0, 1));

                t.Add(baseIdx); t.Add(baseIdx + 1); t.Add(baseIdx + 2);
                t.Add(baseIdx); t.Add(baseIdx + 2); t.Add(baseIdx + 3);

                // Outer sidewall / hubcap disc (facing +X)
                int capIdx = v.Count;
                v.Add(new Vector3(hw, 0f, 0f));
                v.Add(new Vector3(hw, y0, z0));
                v.Add(new Vector3(hw, y1, z1));
                n.Add(Vector3.right); n.Add(Vector3.right); n.Add(Vector3.right);
                u.Add(new Vector2(0.5f, 0.5f));
                u.Add(new Vector2(y0 / radius * 0.5f + 0.5f, z0 / radius * 0.5f + 0.5f));
                u.Add(new Vector2(y1 / radius * 0.5f + 0.5f, z1 / radius * 0.5f + 0.5f));
                t.Add(capIdx); t.Add(capIdx + 1); t.Add(capIdx + 2);

                // Inner sidewall disc (facing -X)
                int inIdx = v.Count;
                v.Add(new Vector3(-hw, 0f, 0f));
                v.Add(new Vector3(-hw, y1, z1));
                v.Add(new Vector3(-hw, y0, z0));
                n.Add(Vector3.left); n.Add(Vector3.left); n.Add(Vector3.left);
                u.Add(new Vector2(0.5f, 0.5f));
                u.Add(new Vector2(y1 / radius * 0.5f + 0.5f, z1 / radius * 0.5f + 0.5f));
                u.Add(new Vector2(y0 / radius * 0.5f + 0.5f, z0 / radius * 0.5f + 0.5f));
                t.Add(inIdx); t.Add(inIdx + 1); t.Add(inIdx + 2);
            }

            mesh.SetVertices(v);
            mesh.SetNormals(n);
            SetAllUVs(mesh, u, v.Count);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateBikeFrameMesh()
        {
            var mesh = new Mesh { name = "BikeFrameMesh" };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var u = new List<Vector2>();
            var t = new List<int>();

            void AddTube(Vector3 p0, Vector3 p1, float radius)
            {
                Vector3 dir = (p1 - p0).normalized;
                Vector3 perp = Vector3.Cross(dir, Vector3.up).normalized * radius;
                if (perp.sqrMagnitude < 0.001f) perp = Vector3.right * radius;

                int baseIdx = v.Count;
                v.Add(p0 - perp); v.Add(p0 + perp);
                v.Add(p1 + perp); v.Add(p1 - perp);
                Vector3 norm = Vector3.up;
                for (int k = 0; k < 4; k++) n.Add(norm);
                u.Add(new Vector2(0, 0)); u.Add(new Vector2(1, 0));
                u.Add(new Vector2(1, 1)); u.Add(new Vector2(0, 1));

                t.Add(baseIdx); t.Add(baseIdx + 1); t.Add(baseIdx + 2);
                t.Add(baseIdx); t.Add(baseIdx + 2); t.Add(baseIdx + 3);
                // Double-sided
                t.Add(baseIdx); t.Add(baseIdx + 2); t.Add(baseIdx + 1);
                t.Add(baseIdx); t.Add(baseIdx + 3); t.Add(baseIdx + 2);
            }

            Vector3 bb = new Vector3(0f, 0.28f, 0f); // Bottom bracket
            Vector3 seat = new Vector3(0f, 0.78f, 0.15f); // Saddle clamp
            Vector3 head = new Vector3(0f, 0.86f, -0.45f); // Head tube
            Vector3 rearDrop = new Vector3(0f, 0.35f, 0.52f); // Rear wheel axle

            AddTube(bb, seat, 0.024f); // Seat tube
            AddTube(bb, head, 0.024f); // Down tube
            AddTube(seat, head, 0.022f); // Top tube
            AddTube(bb, rearDrop, 0.018f); // Chainstays
            AddTube(seat, rearDrop, 0.018f); // Seatstays

            mesh.SetVertices(v);
            mesh.SetNormals(n);
            SetAllUVs(mesh, u, v.Count);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateCarBodyMesh(bool isTaxi)
        {
            var mesh = new Mesh { name = "CarBodyMesh" };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var u = new List<Vector2>();
            var t = new List<int>();

            float w = 1.82f;

            // Lower rocker sills & main chassis
            AddLoftBox(v, n, u, t, new Vector3(0f, 0.46f, 0f), new Vector3(w, 0.36f, 4.30f));

            // Hood: slopes down from cowl (z = -0.85, y = 0.72) to nose (z = -2.15, y = 0.60)
            AddFrustumMesh(v, n, u, t,
                new Vector3(0f, 0.72f, -0.85f), new Vector3(w * 0.96f, 0.16f, 0f),
                new Vector3(0f, 0.60f, -2.15f), new Vector3(w * 0.88f, 0.18f, 0f));

            // Trunk / rear deck (z = 0.95 to 2.15)
            AddLoftBox(v, n, u, t, new Vector3(0f, 0.70f, 1.55f), new Vector3(w * 0.92f, 0.16f, 1.20f));

            mesh.SetVertices(v);
            mesh.SetNormals(n);
            SetAllUVs(mesh, u, v.Count);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateCarCabinMesh()
        {
            var mesh = new Mesh { name = "CarCabinMesh" };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var u = new List<Vector2>();
            var t = new List<int>();

            float w = 1.82f;
            // Aerodynamic greenhouse cabin:
            // Raked windshield: cowl (z = -0.85, y = 0.72) up to roof (z = -0.05, y = 1.40)
            AddFrustumMesh(v, n, u, t,
                new Vector3(0f, 0.72f, -0.85f), new Vector3(w * 0.88f, 0.05f, 0f),
                new Vector3(0f, 1.40f, -0.05f), new Vector3(w * 0.72f, 0.05f, 0f));

            // Cabin center block
            AddLoftBox(v, n, u, t, new Vector3(0f, 1.06f, 0.45f), new Vector3(w * 0.76f, 0.68f, 1.00f));

            // Raked rear backlight: roof (z = 0.95, y = 1.40) down to deck (z = 1.55, y = 0.72)
            AddFrustumMesh(v, n, u, t,
                new Vector3(0f, 1.40f, 0.95f), new Vector3(w * 0.72f, 0.05f, 0f),
                new Vector3(0f, 0.72f, 1.55f), new Vector3(w * 0.86f, 0.05f, 0f));

            mesh.SetVertices(v);
            mesh.SetNormals(n);
            SetAllUVs(mesh, u, v.Count);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateHelicopterFuselageMesh()
        {
            var mesh = new Mesh { name = "HelicopterFuselageMesh" };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var u = new List<Vector2>();
            var t = new List<int>();

            // Main passenger pod
            AddLoftBox(v, n, u, t, new Vector3(0f, 1.35f, -0.3f), new Vector3(1.65f, 1.55f, 2.8f));
            // Tail boom tapering back
            AddLoftBox(v, n, u, t, new Vector3(0f, 1.35f, 2.6f), new Vector3(0.42f, 0.45f, 3.4f));
            // Vertical stabilizer fin
            AddLoftBox(v, n, u, t, new Vector3(0f, 1.75f, 4.4f), new Vector3(0.08f, 1.10f, 0.85f));

            mesh.SetVertices(v);
            mesh.SetNormals(n);
            SetAllUVs(mesh, u, v.Count);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateHelicopterGlassMesh()
        {
            var mesh = new Mesh { name = "HelicopterGlassMesh" };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var u = new List<Vector2>();
            var t = new List<int>();

            // Bubble nose cockpit canopy
            AddLoftBox(v, n, u, t, new Vector3(0f, 1.42f, -1.55f), new Vector3(1.54f, 1.15f, 0.95f));

            mesh.SetVertices(v);
            mesh.SetNormals(n);
            SetAllUVs(mesh, u, v.Count);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateHelicopterSkidsMesh()
        {
            var mesh = new Mesh { name = "HelicopterSkidsMesh" };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var u = new List<Vector2>();
            var t = new List<int>();

            float halfX = 0.92f;
            foreach (float side in new[] { -1f, 1f })
            {
                // Horizontal skid tube
                AddLoftBox(v, n, u, t, new Vector3(side * halfX, 0.22f, -0.2f), new Vector3(0.10f, 0.10f, 3.4f));
                // Forward and rear vertical struts
                AddLoftBox(v, n, u, t, new Vector3(side * halfX * 0.75f, 0.55f, -0.9f), new Vector3(0.06f, 0.65f, 0.08f));
                AddLoftBox(v, n, u, t, new Vector3(side * halfX * 0.75f, 0.55f, 0.7f), new Vector3(0.06f, 0.65f, 0.08f));
            }

            mesh.SetVertices(v);
            mesh.SetNormals(n);
            SetAllUVs(mesh, u, v.Count);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateMainRotorMesh()
        {
            var mesh = new Mesh { name = "MainRotorMesh" };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var u = new List<Vector2>();
            var t = new List<int>();

            // Central hub disc
            AddLoftBox(v, n, u, t, Vector3.zero, new Vector3(0.45f, 0.08f, 0.45f));

            // 4 Airfoil blades extending outward (radius = 5.2m)
            float bladeLen = 4.8f;
            for (int b = 0; b < 4; b++)
            {
                float ang = b * Mathf.PI * 0.5f;
                Vector3 dir = new Vector3(Mathf.Sin(ang), 0f, Mathf.Cos(ang));
                Vector3 center = dir * (bladeLen * 0.5f + 0.25f);

                // Blade box oriented along direction
                Vector3 size = (b % 2 == 0) ? new Vector3(0.24f, 0.03f, bladeLen) : new Vector3(bladeLen, 0.03f, 0.24f);
                AddLoftBox(v, n, u, t, center, size);
            }

            mesh.SetVertices(v);
            mesh.SetNormals(n);
            SetAllUVs(mesh, u, v.Count);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh CreateTailRotorMesh()
        {
            var mesh = new Mesh { name = "TailRotorMesh" };
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var u = new List<Vector2>();
            var t = new List<int>();

            AddLoftBox(v, n, u, t, Vector3.zero, new Vector3(0.06f, 0.95f, 0.09f));
            AddLoftBox(v, n, u, t, Vector3.zero, new Vector3(0.06f, 0.09f, 0.95f));

            mesh.SetVertices(v);
            mesh.SetNormals(n);
            SetAllUVs(mesh, u, v.Count);
            mesh.SetTriangles(t, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // =========================================================================================
        // HELPERS
        // =========================================================================================

        static void AddLoftBox(List<Vector3> v, List<Vector3> n, List<Vector2> u, List<int> t, Vector3 center, Vector3 size)
        {
            float hw = size.x * 0.5f;
            float hh = size.y * 0.5f;
            float hl = size.z * 0.5f;

            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 norm)
            {
                int b = v.Count;
                v.Add(center + p0); v.Add(center + p1); v.Add(center + p2); v.Add(center + p3);
                n.Add(norm); n.Add(norm); n.Add(norm); n.Add(norm);
                u.Add(new Vector2(0, 0)); u.Add(new Vector2(1, 0));
                u.Add(new Vector2(1, 1)); u.Add(new Vector2(0, 1));
                t.Add(b); t.Add(b + 1); t.Add(b + 2);
                t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }

            Quad(new Vector3(-hw, -hh, -hl), new Vector3(hw, -hh, -hl), new Vector3(hw, hh, -hl), new Vector3(-hw, hh, -hl), Vector3.back);
            Quad(new Vector3(hw, -hh, hl), new Vector3(-hw, -hh, hl), new Vector3(-hw, hh, hl), new Vector3(hw, hh, hl), Vector3.forward);
            Quad(new Vector3(-hw, hh, -hl), new Vector3(hw, hh, -hl), new Vector3(hw, hh, hl), new Vector3(-hw, hh, hl), Vector3.up);
            Quad(new Vector3(-hw, -hh, hl), new Vector3(hw, -hh, hl), new Vector3(hw, -hh, -hl), new Vector3(-hw, -hh, -hl), Vector3.down);
            Quad(new Vector3(-hw, -hh, hl), new Vector3(-hw, -hh, -hl), new Vector3(-hw, hh, -hl), new Vector3(-hw, hh, hl), Vector3.left);
            Quad(new Vector3(hw, -hh, -hl), new Vector3(hw, -hh, hl), new Vector3(hw, hh, hl), new Vector3(hw, hh, -hl), Vector3.right);
        }

        static void AddFrustumMesh(List<Vector3> v, List<Vector3> n, List<Vector2> u, List<int> t,
                                  Vector3 centerBack, Vector3 sizeBack, Vector3 centerFront, Vector3 sizeFront)
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

            void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 norm)
            {
                int b = v.Count;
                v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3);
                n.Add(norm); n.Add(norm); n.Add(norm); n.Add(norm);
                u.Add(new Vector2(0, 0)); u.Add(new Vector2(1, 0));
                u.Add(new Vector2(1, 1)); u.Add(new Vector2(0, 1));
                t.Add(b); t.Add(b + 1); t.Add(b + 2);
                t.Add(b); t.Add(b + 2); t.Add(b + 3);
            }

            Vector3 nTop = Vector3.Cross(ftR - tR, tL - tR).normalized;
            Quad(tL, tR, ftR, ftL, nTop);

            Vector3 nBottom = Vector3.Cross(bL - bR, fbR - bR).normalized;
            Quad(bL, fbL, fbR, bR, nBottom);

            Vector3 nLeft = Vector3.Cross(ftL - tL, bL - tL).normalized;
            Quad(tL, ftL, fbL, bL, nLeft);

            Vector3 nRight = Vector3.Cross(bR - tR, ftR - tR).normalized;
            Quad(tR, bR, fbR, ftR, nRight);
        }

        static GameObject AddMeshChild(GameObject parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            return go;
        }

        static Transform CreateWheelChild(Transform parent, string name, Vector3 localPos, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = CreateWheelDiscMesh(radius: 0.35f, width: 0.22f);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            return go.transform;
        }

        static Material CreateMaterial(string name, Color color, float smoothness = 0.5f, float metallic = 0.0f, Color? emission = null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) shader = Shader.Find("Nyc/VehiclePaint");
            var mat = new Material(shader) { name = name };
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            if (emission.HasValue && (emission.Value.r > 0.01f || emission.Value.g > 0.01f || emission.Value.b > 0.01f))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
            }
            return mat;
        }

        static void SetAllUVs(Mesh mesh, List<Vector2> uv0, int count)
        {
            var dummy = new List<Vector2>(count);
            for (int i = 0; i < count; i++) dummy.Add(Vector2.zero);

            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, dummy);
            mesh.SetUVs(2, dummy);
            mesh.SetUVs(3, dummy);
        }
    }
}
