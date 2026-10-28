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
    /// Runtime animator driver for high-fidelity rigged pedestrian characters (e.g. CityPeople prefabs).
    /// Switches between idle, walking, and jogging states based on speed and boost.
    /// </summary>
    public sealed class PedestrianAnimatorDriver : MonoBehaviour
    {
        public PedestrianController controller;
        public Animator animator;

        void Update()
        {
            if (!controller || !animator) return;
            float speed = Mathf.Abs(controller.Speed);
            if (speed < 0.15f)
            {
                animator.Play("idle_m_2_220f");
                animator.speed = 1.0f;
            }
            else if (speed > 5.5f)
            {
                animator.Play("locom_m_jogging_30f");
                animator.speed = Mathf.Clamp(speed / 6.0f, 0.8f, 1.5f);
            }
            else
            {
                animator.Play("locom_m_basicWalk_30f");
                animator.speed = Mathf.Clamp(speed / 3.5f, 0.7f, 1.4f);
            }
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

        public bool isWrecked;
        private Material _burnedMat;
        private Material _burnedGlassMat;
        private MeshRenderer[] _bodyRenderers;
        private Mesh _originalBodyMesh;
        private Mesh _originalCabinMesh;

        void OnEnable()
        {
            if (controller != null) controller.OnCarWrecked += HandleWrecked;
        }

        void OnDisable()
        {
            if (controller != null) controller.OnCarWrecked -= HandleWrecked;
        }

        void HandleWrecked(Vector3 pos, float impactSpeed)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            Effects.NycVehicleExplosionManager.TriggerCrashExplosion(pos, impactSpeed, spawnStandaloneWreck: false);
#endif
            SetWrecked(true, impactSpeed);
        }

        public void SetWrecked(bool wrecked, float impactSpeed = 15f)
        {
            isWrecked = wrecked;
            if (wrecked)
            {
                // 1. Procedural accordion crush and buckling deformation of CarBody
                var bodyFilter = chassisRoot ? chassisRoot.Find("CarBody")?.GetComponent<MeshFilter>() : null;
                if (bodyFilter != null && bodyFilter.sharedMesh != null)
                {
                    if (_originalBodyMesh == null) _originalBodyMesh = bodyFilter.sharedMesh;
                    Mesh deformed = Instantiate(bodyFilter.sharedMesh);
                    deformed.name = "WreckedCarBody";
                    Vector3[] verts = deformed.vertices;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 v = verts[i];
                        if (v.z < 0.2f)
                        {
                            float t = Mathf.InverseLerp(0.2f, -2.35f, v.z);
                            // Accordion compression along Z
                            v.z += 0.65f * Mathf.Pow(t, 1.4f);

                            // Hood buckling upwards with jagged creases
                            if (v.y > 0.55f)
                            {
                                float hoodCenter = 1.0f - Mathf.Clamp01(Mathf.Abs(v.x) / 0.85f);
                                float buckle = Mathf.Sin(t * Mathf.PI * 1.5f) * 0.35f * hoodCenter;
                                float wrinkle = Mathf.Sin(v.x * 12.0f + v.z * 18.0f) * 0.06f * t;
                                v.y += buckle + wrinkle;
                            }
                            else
                            {
                                v.y += 0.10f * t;
                            }

                            // Splay fender panels
                            float flare = Mathf.Sin(v.z * 6.0f) * 0.08f * t;
                            v.x += Mathf.Sign(v.x != 0f ? v.x : 1f) * flare;
                        }
                        verts[i] = v;
                    }
                    deformed.vertices = verts;
                    deformed.RecalculateNormals();
                    deformed.RecalculateBounds();
                    bodyFilter.sharedMesh = deformed;
                }

                // 2. Procedural windshield collapse and roof crush of CarCabin
                var cabinFilter = chassisRoot ? chassisRoot.Find("CarCabin")?.GetComponent<MeshFilter>() : null;
                if (cabinFilter != null && cabinFilter.sharedMesh != null)
                {
                    if (_originalCabinMesh == null) _originalCabinMesh = cabinFilter.sharedMesh;
                    Mesh deformed = Instantiate(cabinFilter.sharedMesh);
                    deformed.name = "WreckedCarCabin";
                    Vector3[] verts = deformed.vertices;
                    for (int i = 0; i < verts.Length; i++)
                    {
                        Vector3 v = verts[i];
                        if (v.z < 0.1f)
                        {
                            float t = Mathf.InverseLerp(0.1f, -1.7f, v.z);
                            v.z += 0.38f * t;
                            v.y -= 0.18f * t;
                            float glassWrinkle = Mathf.Sin(v.x * 14f + v.z * 16f) * 0.04f * t;
                            v.y += glassWrinkle;
                        }
                        verts[i] = v;
                    }
                    deformed.vertices = verts;
                    deformed.RecalculateNormals();
                    deformed.RecalculateBounds();
                    cabinFilter.sharedMesh = deformed;
                }

                // 3. Broken Camber and Steering Damage on Wheels
                if (frontLeftWheel)
                {
                    frontLeftWheel.localRotation = Quaternion.Euler(0f, -22f, 32f);
                    frontLeftWheel.localPosition += new Vector3(-0.06f, 0.08f, 0.15f);
                }
                if (frontRightWheel)
                {
                    frontRightWheel.localRotation = Quaternion.Euler(0f, 15f, -26f);
                    frontRightWheel.localPosition += new Vector3(-0.05f, 0.06f, 0.20f);
                }
                if (rearLeftWheel) rearLeftWheel.localRotation = Quaternion.Euler(0f, 0f, 4f);
                if (rearRightWheel) rearRightWheel.localRotation = Quaternion.Euler(0f, 0f, -4f);

                // 4. Detach / Hide severed components (front bumper, headlights, plates, mirrors, taxi sign)
                var bumper = chassisRoot ? chassisRoot.Find("BumperFront") : null;
                if (bumper) bumper.gameObject.SetActive(false);
                var hlL = chassisRoot ? chassisRoot.Find("Headlight_L") : null;
                if (hlL) hlL.gameObject.SetActive(false);
                var hlR = chassisRoot ? chassisRoot.Find("Headlight_R") : null;
                if (hlR) hlR.gameObject.SetActive(false);
                var plF = chassisRoot ? chassisRoot.Find("PlateFront") : null;
                if (plF) plF.gameObject.SetActive(false);
                var mirL = chassisRoot ? chassisRoot.Find("Mirror_L") : null;
                if (mirL) mirL.gameObject.SetActive(false);
                var mirR = chassisRoot ? chassisRoot.Find("Mirror_R") : null;
                if (mirR) mirR.gameObject.SetActive(false);
                var sign = chassisRoot ? chassisRoot.Find("TaxiSign") : null;
                if (sign) sign.gameObject.SetActive(false);
                var signM = chassisRoot ? chassisRoot.Find("TaxiSignMount") : null;
                if (signM) signM.gameObject.SetActive(false);

                // 5. Charred Soot & Burnt Paint Materials
                if (_burnedMat == null)
                {
                    Shader s = Shader.Find("Universal Render Pipeline/Lit");
                    _burnedMat = new Material(s)
                    {
                        color = new Color(0.08f, 0.07f, 0.07f),
                        name = "CarCharredPaint"
                    };
                    _burnedMat.SetFloat("_Smoothness", 0.04f);
                    _burnedMat.SetFloat("_Metallic", 0.22f);
                }
                if (_burnedGlassMat == null)
                {
                    Shader s = Shader.Find("Universal Render Pipeline/Lit");
                    _burnedGlassMat = new Material(s)
                    {
                        color = new Color(0.04f, 0.04f, 0.04f),
                        name = "CarCrackedSootGlass"
                    };
                    _burnedGlassMat.SetFloat("_Smoothness", 0.02f);
                }

                if (chassisRoot)
                {
                    _bodyRenderers = chassisRoot.GetComponentsInChildren<MeshRenderer>(true);
                    for (int i = 0; i < _bodyRenderers.Length; i++)
                    {
                        if (_bodyRenderers[i].gameObject.name == "CarCabin")
                            _bodyRenderers[i].sharedMaterial = _burnedGlassMat;
                        else
                            _bodyRenderers[i].sharedMaterial = _burnedMat;
                    }
                }

                // 6. Attach Burning Engine Fire & Volumetric Smoke Plume to the crumpled hood
#if UNITY_EDITOR || UNITY_STANDALONE
                if (chassisRoot)
                {
                    Effects.NycBurningCarWreck.AttachToVehicle(chassisRoot.gameObject, impactSpeed);
                }
#endif
            }
            else
            {
                // Restore original state if ever repaired
                if (frontLeftWheel) { frontLeftWheel.gameObject.SetActive(true); frontLeftWheel.localRotation = Quaternion.Euler(0f, 0f, 0f); }
                if (frontRightWheel) { frontRightWheel.localRotation = Quaternion.Euler(0f, 0f, 0f); }
                if (rearLeftWheel) rearLeftWheel.localRotation = Quaternion.Euler(0f, 0f, 0f);
                if (rearRightWheel) rearRightWheel.localRotation = Quaternion.Euler(0f, 0f, 0f);

                var bumper = chassisRoot ? chassisRoot.Find("BumperFront") : null;
                if (bumper) bumper.gameObject.SetActive(true);
                var sign = chassisRoot ? chassisRoot.Find("TaxiSign") : null;
                if (sign) sign.gameObject.SetActive(true);

                var bodyFilter = chassisRoot ? chassisRoot.Find("CarBody")?.GetComponent<MeshFilter>() : null;
                if (bodyFilter != null && _originalBodyMesh != null) bodyFilter.sharedMesh = _originalBodyMesh;

                var cabinFilter = chassisRoot ? chassisRoot.Find("CarCabin")?.GetComponent<MeshFilter>() : null;
                if (cabinFilter != null && _originalCabinMesh != null) cabinFilter.sharedMesh = _originalCabinMesh;
            }
        }

        void LateUpdate()
        {
            if (!controller) return;

            if (isWrecked || controller.IsWrecked)
            {
                if (chassisRoot)
                {
                    float pitchDeg = (controller.ChassisPitch * Mathf.Rad2Deg) + 8.5f;
                    float rollDeg = (-controller.ChassisRoll * Mathf.Rad2Deg) + 3.5f;
                    chassisRoot.localPosition = new Vector3(0f, controller.HeaveY - 0.08f, 0f);
                    chassisRoot.localRotation = Quaternion.Euler(pitchDeg, 0f, rollDeg);
                }
                return;
            }

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
                frontLeftWheel.localRotation = Quaternion.Euler(0f, 180f + steerDeg, 0f) * Quaternion.Euler(-spinDeg, 0f, 0f);
            if (frontRightWheel)
                frontRightWheel.localRotation = Quaternion.Euler(0f, steerDeg, 0f) * Quaternion.Euler(spinDeg, 0f, 0f);

            // Rear wheels spin only
            if (rearLeftWheel) rearLeftWheel.localRotation = Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(-spinDeg, 0f, 0f);
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

            // Check if high-fidelity 3D rigged humanoid pedestrian prefab is available in Resources
            var pedPrefab = Resources.Load<GameObject>("Prefabs/Pedestrian");
            if (pedPrefab != null)
            {
                var pedInstance = UnityEngine.Object.Instantiate(pedPrefab, root.transform, false);
                pedInstance.name = "City_Pedestrian_PBR";
                pedInstance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                pedInstance.transform.localPosition = Vector3.zero;

                var anim = pedInstance.GetComponent<Animator>();
                if (anim != null)
                {
                    var walkerDriver = pedInstance.AddComponent<PedestrianAnimatorDriver>();
                    walkerDriver.controller = controller;
                    walkerDriver.animator = anim;
                }
                return;
            }

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

            // Web reference uses taxi paint #ffbe14 with roughness 0.24 / metalness 0.34.
            // URP's smoothness is the inverse of roughness, so keep the same visual response here.
            Material paintMat = CreateMaterial("CarPaint", isTaxi ? new Color(1.0f, 0.745f, 0.08f) : new Color(0.85f, 0.12f, 0.15f), 0.76f, 0.34f);
            Material glassMat = CreateMaterial("CarGlass", new Color(0.06f, 0.08f, 0.11f), 0.95f, 0.10f);
            Material wheelMat = CreateMaterial("CarWheel", new Color(0.12f, 0.12f, 0.14f), 0.25f, 0.0f);
            Material trimMat  = CreateMaterial("CarTrim", new Color(0.20f, 0.20f, 0.22f), 0.70f, 0.30f);
            Material headlightMat = CreateMaterial("CarHeadlight", new Color(1.0f, 0.96f, 0.88f), 0.9f, 0f, new Color(2.5f, 2.3f, 1.8f));
            Material taillightMat = CreateMaterial("CarTaillight", new Color(0.85f, 0.06f, 0.04f), 0.8f, 0f, new Color(2.0f, 0.15f, 0.1f));

            // Sculpted aerodynamic car body & raked cabin
            // The web taxi is a lofted, rounded sedan (BODY_SPECS.taxi), not a stack of boxes.
            // Use the same sectioned body/cabin construction for the controllable actor.
            AddMeshChild(chassisGo, "CarBody", CreateTaxiLoftBodyMesh(), paintMat);
            AddMeshChild(chassisGo, "CarCabin", CreateTaxiLoftCabinMesh(), glassMat);

            // Front and rear bumpers
            AddMeshChild(chassisGo, "BumperFront", CreateBoxMesh(1.72f, 0.26f, 0.16f), trimMat)
                .transform.localPosition = new Vector3(0f, 0.45f, -2.30f);
            AddMeshChild(chassisGo, "BumperRear", CreateBoxMesh(1.72f, 0.28f, 0.16f), trimMat)
                .transform.localPosition = new Vector3(0f, 0.48f, 2.30f);

            // Front Headlights
            AddMeshChild(chassisGo, "Headlight_L", CreateBoxMesh(0.40f, 0.12f, 0.04f), headlightMat)
                .transform.localPosition = new Vector3(-0.56f, 0.86f, -2.38f);
            AddMeshChild(chassisGo, "Headlight_R", CreateBoxMesh(0.40f, 0.12f, 0.04f), headlightMat)
                .transform.localPosition = new Vector3(0.56f, 0.86f, -2.38f);

            // Rear Taillights
            AddMeshChild(chassisGo, "Taillight_L", CreateBoxMesh(0.46f, 0.14f, 0.05f), taillightMat)
                .transform.localPosition = new Vector3(-0.585f, 0.98f, 2.39f);
            AddMeshChild(chassisGo, "Taillight_R", CreateBoxMesh(0.46f, 0.14f, 0.05f), taillightMat)
                .transform.localPosition = new Vector3(0.585f, 0.98f, 2.39f);

            // NYC taxi identification details from the web actor: front/rear NY plates and
            // the black-and-gold T&LC medallion on both front doors. These sit just off the
            // body surface so they remain readable at the chase camera distance.
            Material plateMat = CreateMaterial("TaxiPlate", new Color(0.92f, 0.84f, 0.48f), 0.36f, 0.35f);
            AddMeshChild(chassisGo, "PlateFront", CreateBoxMesh(0.34f, 0.14f, 0.025f), plateMat)
                .transform.localPosition = new Vector3(0f, 0.84f, -2.40f);
            AddMeshChild(chassisGo, "PlateRear", CreateBoxMesh(0.34f, 0.14f, 0.025f), plateMat)
                .transform.localPosition = new Vector3(0f, 0.84f, 2.40f);

            if (isTaxi)
            {
                Material medallionMat = CreateMaterial("TaxiMedallion", new Color(0.08f, 0.06f, 0.035f), 0.48f, 0.22f,
                    new Color(0.12f, 0.07f, 0.015f));
                for (int side = -1; side <= 1; side += 2)
                {
                    var badge = AddMeshChild(chassisGo, side < 0 ? "DoorBadge_L" : "DoorBadge_R",
                        CreateBoxMesh(0.025f, 0.22f, 0.42f), medallionMat);
                    badge.transform.localPosition = new Vector3(side * 0.918f, 0.78f, -0.18f);
                }
            }

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
            float halfX = 0.895f;
            float wheelY = 0.35f;

            vis.frontLeftWheel = CreateWheelChild(root.transform, "Wheel_FL", new Vector3(-halfX, wheelY, frontZ), wheelMat, true);
            vis.frontRightWheel = CreateWheelChild(root.transform, "Wheel_FR", new Vector3(halfX, wheelY, frontZ), wheelMat, false);
            vis.rearLeftWheel = CreateWheelChild(root.transform, "Wheel_RL", new Vector3(-halfX, wheelY, rearZ), wheelMat, true);
            vis.rearRightWheel = CreateWheelChild(root.transform, "Wheel_RR", new Vector3(halfX, wheelY, rearZ), wheelMat, false);
        }

        // =========================================================================================
        // 4. HELICOPTER ACTOR
        // =========================================================================================

        public static void BuildHelicopterActor(HelicopterController controller)
        {
            if (!controller) return;

#if UNITY_EDITOR || UNITY_STANDALONE
            if (!controller.GetComponent<Effects.AircraftWakeSource>())
            {
                var wake = controller.gameObject.AddComponent<Effects.AircraftWakeSource>();
                wake.aircraftType = "Helicopter";
                wake.rotorRadius = 5.2f;
                wake.downwashStrength = 18.0f;
                wake.carveRadius = 8.0f;
            }
#endif

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

            // Check if high-fidelity 3D NYPD Helicopter prefab is available in Resources
            var heliPrefab = Resources.Load<GameObject>("Prefabs/Helicopter");
            if (heliPrefab != null)
            {
                var heliInstance = UnityEngine.Object.Instantiate(heliPrefab, fuselageGo.transform, false);
                heliInstance.name = "NYPD_Helicopter_PBR";
                // Rotate 180 deg around Y so +Z aligns with travel heading (-Z forward)
                heliInstance.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
                heliInstance.transform.localPosition = new Vector3(0f, 0f, 0f);

                // Wire up rotating rotors to HelicopterVisual for continuous high-RPM spin
                Transform mainRotorTrans = heliInstance.transform.Find("Main_Rotor");
                Transform tailRotorTrans = heliInstance.transform.Find("Tail_Rotor");
                if (mainRotorTrans) vis.mainRotor = mainRotorTrans;
                if (tailRotorTrans) vis.tailRotor = tailRotorTrans;
                AttachDroneWeaponSystem(controller);
                return;
            }

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
            AttachDroneWeaponSystem(controller);
        }

        static void AttachDroneWeaponSystem(HelicopterController controller)
        {
            if (!controller) return;
            var weapons = controller.GetComponent<DroneWeaponSystem>();
            if (!weapons) weapons = controller.gameObject.AddComponent<DroneWeaponSystem>();
            weapons.controller = controller;
            if (!weapons.cameras) weapons.cameras = UnityEngine.Object.FindAnyObjectByType<CameraManager>();
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

        // ---------------------------------------------------------------------------------
        // Faithful taxi loft port (actor-models.js BODY_SPECS.taxi / buildCarGeometry)
        // ---------------------------------------------------------------------------------

        static Mesh CreateTaxiLoftBodyMesh()
        {
            var sections = new List<List<Vector3>>(28);
            for (int i = 0; i <= 24; i++)
            {
                float zWeb = Mathf.Lerp(-2.25f, 2.25f, i / 24f);
                TaxiBodySample(zWeb, out float w, out float yB, out float yS, out float yE, out float yT, out float xE);
                float xB = w - 0.075f;
                var right = new List<Vector2>(12);
                for (int k = 0; k <= 4; k++)
                {
                    float a = (k / 4f) * Mathf.PI * 0.5f;
                    float sn = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(a)), 2f / 2.8f);
                    float cs = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a)), 2f / 2.8f);
                    right.Add(new Vector2(xB + (w - xB) * sn, yS - (yS - yB) * cs));
                }
                for (int k = 1; k <= 3; k++)
                {
                    float a = (k / 4f) * Mathf.PI * 0.5f;
                    float sn = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(a)), 2f / 3.2f);
                    float cs = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a)), 2f / 3.2f);
                    right.Add(new Vector2(xE + (w - xE) * cs, yS + (yE - yS) * sn));
                }
                right.Add(new Vector2(xE * 0.66f, yE + (yT - yE) * 0.56f));
                right.Add(new Vector2(xE * 0.33f, yE + (yT - yE) * 0.89f));
                right.Add(new Vector2(0f, yT));
                sections.Add(MirrorTaxiSection(right, -zWeb));
            }
            return BuildTaxiLoftMesh(sections, "TaxiLoftBody", true);
        }

        static Mesh CreateTaxiLoftCabinMesh()
        {
            var sections = new List<List<Vector3>>(16);
            float[] stations = { -1.68f, -1.48f, -1.25f, -1.0f, -0.72f, -0.42f, -0.16f, 0f, 0.22f, 0.46f, 0.68f, 0.86f };
            for (int i = 0; i < stations.Length; i++)
            {
                float z = stations[i];
                TaxiBodySample(z, out float w, out _, out _, out float yE, out _, out float xE);
                float beltX = xE - 0.035f;
                float beltY = yE - 0.004f;
                float railX, railY, h;
                if (z >= 0f)
                {
                    float t = Mathf.Clamp01((0.86f - z) / 0.86f);
                    railX = Mathf.Lerp(beltX, 0.645f, t);
                    railY = Mathf.Lerp(beltY, 1.35f, t);
                    h = Mathf.Lerp(0.05f, 0.085f, t);
                }
                else if (z >= -1f)
                {
                    float t = Mathf.Clamp01(-z);
                    railX = Mathf.Lerp(0.645f, 0.625f, t);
                    railY = 1.435f;
                    h = 0.085f;
                }
                else
                {
                    float t = Mathf.Clamp01((z + 1.68f) / 0.68f);
                    railX = Mathf.Lerp(beltX, 0.625f, t);
                    railY = Mathf.Lerp(beltY, 1.435f, t);
                    h = Mathf.Lerp(0.05f, 0.085f, t);
                }

                var right = new List<Vector2>(9) { new Vector2(beltX, beltY),
                    new Vector2(Mathf.Lerp(beltX, railX, 0.34f), Mathf.Lerp(beltY, railY, 0.34f)),
                    new Vector2(Mathf.Lerp(beltX, railX, 0.68f), Mathf.Lerp(beltY, railY, 0.68f)),
                    new Vector2(railX, railY) };
                for (int k = 1; k <= 3; k++)
                {
                    float f = k / 4f;
                    float a = f * Mathf.PI * 0.5f;
                    right.Add(new Vector2(railX * Mathf.Pow(Mathf.Max(0f, Mathf.Cos(a)), 2f / 4f),
                        railY + h * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(a)), 2f / 4f)));
                }
                right.Add(new Vector2(0f, railY + h));
                sections.Add(MirrorTaxiSection(right, -z));
            }
            return BuildTaxiLoftMesh(sections, "TaxiLoftCabin", false);
        }

        static void TaxiBodySample(float z, out float w, out float yB, out float yS, out float yE,
                                   out float yT, out float xE)
        {
            float t = Mathf.Clamp01((z + 2.25f) / 4.5f);
            w = 0.915f + 0.045f * Mathf.Sin(t * Mathf.PI);
            yB = 0.29f + 0.01f * Mathf.Abs(t - 0.5f) * 2f;
            yS = Mathf.Lerp(0.76f, 0.66f, t);
            yE = Mathf.Lerp(1.03f, 0.79f, t) + 0.045f * Mathf.Sin(t * Mathf.PI);
            yT = yE + 0.03f;
            xE = Mathf.Max(w - 0.12f, w * 0.55f);
            float archR = 0.415f;
            foreach (float axle in new[] { -1.38f, 1.38f })
            {
                float d = Mathf.Abs(z - axle);
                if (d < archR) yB = Mathf.Max(yB, 0.35f + Mathf.Sqrt(archR * archR - d * d));
            }
        }

        static List<Vector3> MirrorTaxiSection(List<Vector2> right, float unityZ)
        {
            var section = new List<Vector3>(right.Count * 2);
            for (int i = 0; i < right.Count; i++) section.Add(new Vector3(-right[i].x, right[i].y, unityZ));
            for (int i = right.Count - 1; i >= 0; i--) section.Add(new Vector3(right[i].x, right[i].y, unityZ));
            return section;
        }

        static Mesh BuildTaxiLoftMesh(List<List<Vector3>> sections, string name, bool capEnds)
        {
            var mesh = new Mesh { name = name };
            var vertices = new List<Vector3>(sections.Count * sections[0].Count);
            var triangles = new List<int>((sections.Count - 1) * sections[0].Count * 6);
            var uv = new List<Vector2>(vertices.Capacity);
            int ring = sections[0].Count;
            for (int s = 0; s < sections.Count; s++)
            {
                for (int i = 0; i < ring; i++)
                {
                    Vector3 p = sections[s][i];
                    vertices.Add(p);
                    uv.Add(new Vector2(i / (float)Mathf.Max(1, ring - 1), s / (float)Mathf.Max(1, sections.Count - 1)));
                }
            }
            for (int s = 0; s < sections.Count - 1; s++)
            {
                for (int i = 0; i < ring; i++)
                {
                    int a = s * ring + i;
                    int b = s * ring + ((i + 1) % ring);
                    int c = (s + 1) * ring + ((i + 1) % ring);
                    int d = (s + 1) * ring + i;
                    triangles.Add(a); triangles.Add(d); triangles.Add(c);
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                }
            }
            if (capEnds)
            {
                int start = vertices.Count;
                Vector3 p0 = Vector3.zero;
                for (int i = 0; i < ring; i++) p0 += sections[0][i];
                p0 /= ring;
                vertices.Add(p0); uv.Add(Vector2.zero);
                int end = vertices.Count;
                Vector3 p1 = Vector3.zero;
                for (int i = 0; i < ring; i++) p1 += sections[sections.Count - 1][i];
                p1 /= ring;
                vertices.Add(p1); uv.Add(Vector2.one);
                for (int i = 0; i < ring; i++)
                {
                    triangles.Add(start); triangles.Add((i + 1) % ring); triangles.Add(i);
                    int baseEnd = (sections.Count - 1) * ring;
                    triangles.Add(end); triangles.Add(baseEnd + i); triangles.Add(baseEnd + (i + 1) % ring);
                }
            }
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uv);
            mesh.RecalculateNormals();
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

        static Transform CreateWheelChild(Transform parent, string name, Vector3 localPos, Material material, bool isLeft = false)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            if (isLeft) go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = CreateWheelDiscMesh(radius: 0.35f, width: 0.22f);
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;

            // Separate alloy hub keeps the taxi wheels readable against the dark tyre, matching
            // the five-spoke wheel insert used by actor-models.js.
            var hub = new GameObject(name + "_AlloyHub");
            hub.transform.SetParent(go.transform, false);
            var hubFilter = hub.AddComponent<MeshFilter>();
            hubFilter.sharedMesh = CreateWheelDiscMesh(radius: 0.165f, width: 0.235f);
            var hubRenderer = hub.AddComponent<MeshRenderer>();
            hubRenderer.sharedMaterial = CreateMaterial("WheelAlloy", new Color(0.78f, 0.82f, 0.88f), 0.72f, 0.82f);
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
