using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Nyc.Gameplay
{
    /// <summary>
    /// Dual articulated weapon rig for the helicopter POV. One physical mouse drives both
    /// linked sights at once; the small offset/lag keeps the two reticles readable while both
    /// weapon mounts follow the same aim point. Trailer capture can inject deterministic aim.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DroneWeaponSystem : MonoBehaviour
    {
        public HelicopterController controller;
        public CameraManager cameras;

        [Header("Aim")]
        public float mouseSensitivity = 0.0085f;
        public float maxYaw = 34f;
        public float maxPitch = 22f;
        public float aimSmooth = 14f;

        [Header("Runtime")]
        public Vector2 LeftAim { get; private set; } = new Vector2(-0.24f, 0f);
        public Vector2 RightAim { get; private set; } = new Vector2(0.24f, 0f);
        public bool LeftTrigger { get; private set; }
        public bool RightTrigger { get; private set; }
        public Transform LeftMount => _leftMount;
        public Transform RightMount => _rightMount;
        public bool HasOverlay => _overlayCanvas != null;

        Transform _leftMount, _rightMount;
        Transform _leftBarrel, _rightBarrel;
        Vector2 _targetLeft = new Vector2(-0.24f, 0f);
        Vector2 _targetRight = new Vector2(0.24f, 0f);
        bool _demoInput;
        Vector2 _demoLeft, _demoRight;
        bool _demoLeftTrigger, _demoRightTrigger;
        float _yawLeft, _pitchLeft, _yawRight, _pitchRight;
        Material _weaponMaterial;
        Canvas _overlayCanvas;
        RectTransform _overlayRect;
        Text _overlayTitle, _overlayHint, _overlayStatus, _leftReticle, _rightReticle;

        public bool IsVisible => controller != null && cameras != null &&
                                  cameras.activePresetId == "pov";

        void Awake()
        {
            if (!controller) controller = GetComponent<HelicopterController>();
            if (!cameras) cameras = FindAnyObjectByType<CameraManager>();
            BuildRig();
            BuildOverlay();
        }

        void LateUpdate()
        {
            if (!controller) return;

            if (_demoInput)
            {
                _targetLeft = Vector2.ClampMagnitude(_demoLeft, 1f);
                _targetRight = Vector2.ClampMagnitude(_demoRight, 1f);
                LeftTrigger = _demoLeftTrigger;
                RightTrigger = _demoRightTrigger;
            }
            else if (IsVisible)
            {
                ReadMouseAim();
            }
            else
            {
                LeftTrigger = RightTrigger = false;
            }

            float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, aimSmooth) * Time.deltaTime);
            LeftAim = Vector2.Lerp(LeftAim, _targetLeft, k);
            RightAim = Vector2.Lerp(RightAim, _targetRight, k);
            ApplyMount(_leftMount, LeftAim, ref _yawLeft, ref _pitchLeft);
            ApplyMount(_rightMount, RightAim, ref _yawRight, ref _pitchRight);
            UpdateOverlay();
        }

        void ReadMouseAim()
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 delta = mouse.delta.ReadValue();
            Vector2 step = new Vector2(delta.x, delta.y) * mouseSensitivity;
            _targetLeft = Vector2.ClampMagnitude(_targetLeft + step, 1f);
            _targetRight = Vector2.ClampMagnitude(_targetRight + step * 0.96f, 1f);
            LeftTrigger = mouse.leftButton.isPressed;
            RightTrigger = mouse.rightButton.isPressed;
#endif
        }

        void ApplyMount(Transform mount, Vector2 aim, ref float yaw, ref float pitch)
        {
            if (!mount) return;
            float targetYaw = aim.x * maxYaw;
            float targetPitch = -aim.y * maxPitch;
            float k = 1f - Mathf.Exp(-aimSmooth * Time.deltaTime);
            yaw = Mathf.Lerp(yaw, targetYaw, k);
            pitch = Mathf.Lerp(pitch, targetPitch, k);
            mount.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        void BuildRig()
        {
            if (_leftMount && _rightMount) return;
            Transform parent = transform.Find("HelicopterModel/FuselageRoot");
            if (!parent) parent = transform;

            _weaponMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                name = "DroneWeaponMat",
                color = new Color(0.08f, 0.10f, 0.12f)
            };
            _weaponMaterial.SetFloat("_Metallic", 0.72f);
            _weaponMaterial.SetFloat("_Smoothness", 0.72f);

            _leftMount = CreateWeapon(parent, "WeaponMount_L", new Vector3(-1.05f, 0.12f, -0.65f));
            _rightMount = CreateWeapon(parent, "WeaponMount_R", new Vector3(1.05f, 0.12f, -0.65f));
        }

        void BuildOverlay()
        {
            if (_overlayCanvas != null) return;
            Camera cam = cameras != null ? cameras.cam : Camera.main;
            if (!cam) return;

            var canvasGo = new GameObject("DronePovWorldspaceHub");
            canvasGo.transform.SetParent(cam.transform, false);
            canvasGo.transform.localPosition = new Vector3(0f, 0f, 0.48f);
            canvasGo.transform.localRotation = Quaternion.identity;
            canvasGo.transform.localScale = Vector3.one * 0.00035f;
            _overlayCanvas = canvasGo.AddComponent<Canvas>();
            _overlayCanvas.renderMode = RenderMode.WorldSpace;
            _overlayCanvas.worldCamera = cam;
            _overlayCanvas.sortingOrder = 200;
            _overlayRect = canvasGo.GetComponent<RectTransform>();
            _overlayRect.sizeDelta = new Vector2(1920f, 1080f);
            _overlayRect.anchorMin = new Vector2(0.5f, 0.5f);
            _overlayRect.anchorMax = new Vector2(0.5f, 0.5f);
            _overlayRect.pivot = new Vector2(0.5f, 0.5f);

            _overlayTitle = CreateText(canvasGo.transform, "DRONE POV  //  DUAL WEAPON LINK", 28, new Vector2(0f, -55f), new Vector2(1920f, 45f), Color.yellow);
            _overlayHint = CreateText(canvasGo.transform, "WASD FLY   SPACE/E UP   Q/C DOWN   MOUSE AIM", 22, new Vector2(0f, -105f), new Vector2(1920f, 38f), new Color(0.82f, 0.86f, 0.90f));
            _leftReticle = CreateText(canvasGo.transform, "+", 58, Vector2.zero, new Vector2(100f, 100f), Color.yellow);
            _rightReticle = CreateText(canvasGo.transform, "+", 58, Vector2.zero, new Vector2(100f, 100f), new Color(0.38f, 0.82f, 1f));
            _overlayStatus = CreateText(canvasGo.transform, "LINKED AIM  ·  STANDBY", 22, new Vector2(0f, 445f), new Vector2(1920f, 38f), new Color(0.86f, 0.88f, 0.92f));
            _overlayCanvas.enabled = false;
        }

        static Text CreateText(Transform parent, string value, int size, Vector2 position, Vector2 dimensions, Color color)
        {
            var go = new GameObject("DroneHubText");
            go.transform.SetParent(parent, false);
            var rect = go.AddComponent<RectTransform>();
            rect.sizeDelta = dimensions;
            rect.anchoredPosition = position;
            var text = go.AddComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        void UpdateOverlay()
        {
            if (_overlayCanvas == null) return;
            bool visible = IsVisible;
            _overlayCanvas.enabled = visible;
            if (!visible) return;

            PlaceOverlayReticle(_leftReticle, LeftAim);
            PlaceOverlayReticle(_rightReticle, RightAim);
            if (_overlayStatus != null)
            {
                string left = LeftTrigger ? "FIRE" : "AIM";
                string right = RightTrigger ? "FIRE" : "AIM";
                _overlayStatus.text = $"GUN L {left}   ·   GUN R {right}   ·   LINKED";
            }
        }

        static void PlaceOverlayReticle(Text reticle, Vector2 aim)
        {
            if (!reticle) return;
            RectTransform rect = reticle.rectTransform;
            rect.anchoredPosition = new Vector2(aim.x * 650f, aim.y * 420f);
        }

        Transform CreateWeapon(Transform parent, string name, Vector3 localPosition)
        {
            var mount = new GameObject(name).transform;
            mount.SetParent(parent, false);
            mount.localPosition = localPosition;

            GameObject housing = GameObject.CreatePrimitive(PrimitiveType.Cube);
            housing.name = name + "_Housing";
            housing.transform.SetParent(mount, false);
            housing.transform.localScale = new Vector3(0.32f, 0.24f, 0.42f);
            AssignMaterial(housing);
            RemoveCollider(housing);

            GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            barrel.name = name + "_Barrel";
            barrel.transform.SetParent(mount, false);
            barrel.transform.localPosition = new Vector3(0f, -0.01f, -0.48f);
            barrel.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            barrel.transform.localScale = new Vector3(0.055f, 0.55f, 0.055f);
            AssignMaterial(barrel);
            RemoveCollider(barrel);

            return mount;
        }

        void AssignMaterial(GameObject go)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer) renderer.sharedMaterial = _weaponMaterial;
        }

        static void RemoveCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider) Destroy(collider);
        }

        /// <summary>Deterministic trailer hook. Values are normalized screen aim coordinates.</summary>
        public void SetDemoAim(Vector2 left, Vector2 right, bool leftTrigger, bool rightTrigger)
        {
            _demoInput = true;
            _demoLeft = left;
            _demoRight = right;
            _demoLeftTrigger = leftTrigger;
            _demoRightTrigger = rightTrigger;
        }

        public void ClearDemoAim()
        {
            _demoInput = false;
            _targetLeft = new Vector2(-0.24f, 0f);
            _targetRight = new Vector2(0.24f, 0f);
            LeftTrigger = RightTrigger = false;
        }
    }
}
