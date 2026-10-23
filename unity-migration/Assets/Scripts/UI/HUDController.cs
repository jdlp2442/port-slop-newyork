using System;
using UnityEngine;
using UnityEngine.UIElements;
using Nyc.Core;
using Nyc.Gameplay;
using Nyc.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Nyc.UI
{
    /// <summary>
    /// UI Toolkit HUD: brand + FPS, time-of-day switch, teleport popover, mode switch,
    /// camera switch (11 presets), speed/altitude cluster, location signs, control hints and the
    /// keyboard map (1–4, 5–0, V, H, M, Esc).
    ///
    /// Port of the DOM wiring in main.js plus the layout in index.html / css/style.css.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class HUDController : MonoBehaviour
    {
        [Header("References")]
        public ThirdPersonControllerManager controllers;
        public CameraManager cameras;
        public LightingSystem lighting;
        public NycMapSystem mapSystem;

        [Header("Behaviour")]
        [Tooltip("Seconds between HUD telemetry refreshes (the original uses 250 ms).")]
        public float refreshInterval = 0.2f;

        VisualElement _root;
        VisualElement _modeIndicator;
        VisualElement _teleportPopover;
        VisualElement _cameraPopover;
        VisualElement _altitudeRow;
        VisualElement _speedMeter;

        Label _fps;
        Label _modeLabel;
        Label _cameraLabel;
        Label _speed;
        Label _altitude;
        Label _place;
        Label _signs;
        Label _hint;

        Button[] _modeButtons;
        Button[] _cameraButtons;
        Button[] _timeButtons;

        bool _hidden;
        float _fpsSmoothed = 60f;
        float _timer;
        TravelMode _lastMode = (TravelMode)(-1);
        string _lastCamera = string.Empty;

        static readonly string[] CameraIds =
        {
            "chase", "action", "pov", "centralPark", "fifthAve", "timesSquare",
            "chryslerGCT", "observatory", "bryantPark", "waterfront", "drone"
        };

        static readonly string[] TeleportIds =
        {
            "empire", "timessq", "chrysler", "grandcentral", "bryant", "flatiron", "centralpark", "owtc"
        };

        static readonly string[] TimeIds = { "golden", "midday", "twilight", "midnight" };

        // ------------------------------------------------------------------ lifecycle

        void Awake()
        {
            if (!controllers) controllers = FindAnyObjectByType<ThirdPersonControllerManager>();
            if (!cameras) cameras = FindAnyObjectByType<CameraManager>();
            if (!lighting) lighting = FindAnyObjectByType<LightingSystem>();
            if (!mapSystem) mapSystem = FindAnyObjectByType<NycMapSystem>();
        }

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            if (doc == null) return;

            _root = doc.rootVisualElement;
            if (_root == null) return;

            Bind();
        }

        void Bind()
        {
            _fps = _root.Q<Label>("fps");
            _modeLabel = _root.Q<Label>("mode-label");
            _cameraLabel = _root.Q<Label>("camera-label");
            _speed = _root.Q<Label>("speed");
            _altitude = _root.Q<Label>("altitude");
            _place = _root.Q<Label>("location-place");
            _signs = _root.Q<Label>("location-signs");
            _hint = _root.Q<Label>("controls-hint");
            _speedMeter = _root.Q("speed-meter");
            _altitudeRow = _root.Q("altitude-row");
            _modeIndicator = _root.Q("mode-indicator");
            _teleportPopover = _root.Q("teleport-popover");
            _cameraPopover = _root.Q("camera-popover");

            // Modes
            _modeButtons = new[]
            {
                _root.Q<Button>("mode-person"),
                _root.Q<Button>("mode-car"),
                _root.Q<Button>("mode-cyclist"),
                _root.Q<Button>("mode-helicopter")
            };
            for (int i = 0; i < _modeButtons.Length; i++)
            {
                int index = i;
                if (_modeButtons[i] != null) _modeButtons[i].clicked += () => SetMode((TravelMode)index);
            }

            // Time of day
            _timeButtons = new[]
            {
                _root.Q<Button>("time-golden"),
                _root.Q<Button>("time-midday"),
                _root.Q<Button>("time-twilight"),
                _root.Q<Button>("time-midnight")
            };
            for (int i = 0; i < _timeButtons.Length; i++)
            {
                string id = TimeIds[i];
                if (_timeButtons[i] != null) _timeButtons[i].clicked += () => SetTime(id);
            }

            // Cameras
            _cameraButtons = new Button[CameraIds.Length];
            for (int i = 0; i < CameraIds.Length; i++)
            {
                string id = CameraIds[i];
                _cameraButtons[i] = _root.Q<Button>("cam-" + id);
                if (_cameraButtons[i] != null) _cameraButtons[i].clicked += () => SetCamera(id);
            }

            var prev = _root.Q<Button>("cam-prev");
            var next = _root.Q<Button>("cam-next");
            if (prev != null) prev.clicked += () => CycleCamera(-1);
            if (next != null) next.clicked += () => CycleCamera(1);

            var camMenu = _root.Q<Button>("cam-menu");
            if (camMenu != null) camMenu.clicked += () => TogglePopover(_cameraPopover);

            // Teleport
            var tpButton = _root.Q<Button>("btn-teleport");
            if (tpButton != null) tpButton.clicked += () => TogglePopover(_teleportPopover);

            for (int i = 0; i < TeleportIds.Length; i++)
            {
                string id = TeleportIds[i];
                var b = _root.Q<Button>("tp-" + id);
                if (b != null) b.clicked += () => Teleport(id);
            }

            var mapButton = _root.Q<Button>("btn-map");
            if (mapButton != null) mapButton.clicked += () => mapSystem?.Toggle();

            var hudButton = _root.Q<Button>("btn-hud");
            if (hudButton != null) hudButton.clicked += ToggleHud;

            var radar = _root.Q("radar");
            if (radar != null) radar.RegisterCallback<PointerDownEvent>(_ => mapSystem?.Toggle());

            var closeMap = _root.Q<Button>("fullmap-close");
            if (closeMap != null) closeMap.clicked += () => mapSystem?.SetOpen(false);

            if (mapSystem != null) mapSystem.Initialize(_root);

            SyncModeClasses();
            SyncCameraClasses();
            SyncTimeClasses();
            SetPopover(_teleportPopover, false);
            SetPopover(_cameraPopover, false);
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            HandleKeyboard();

            _timer += Time.unscaledDeltaTime;
            float dt = Mathf.Max(1e-4f, Time.unscaledDeltaTime);
            _fpsSmoothed = Mathf.Lerp(_fpsSmoothed, 1f / dt, 0.08f);

            mapSystem?.Tick();

            if (_timer < refreshInterval) return;
            _timer = 0f;

            RefreshTelemetry();
        }

        void RefreshTelemetry()
        {
            if (_fps != null) _fps.text = Mathf.RoundToInt(_fpsSmoothed).ToString();

            TravelController a = controllers != null ? controllers.Active : null;
            if (a == null) return;

            float mph = Mathf.Abs(a.Speed) * 2.237f;
            if (_speed != null) _speed.text = Mathf.RoundToInt(mph).ToString();
            if (_altitude != null) _altitude.text = Mathf.Max(0, Mathf.RoundToInt(a.Height * 3.281f)).ToString();

            if (_speedMeter != null)
            {
                float top = controllers.ActiveMode == TravelMode.Helicopter ? 116f
                          : controllers.ActiveMode == TravelMode.Car ? 81f
                          : controllers.ActiveMode == TravelMode.Cyclist ? 43f : 18f;
                _speedMeter.style.width = new Length(Mathf.Clamp01(mph / top) * 100f, LengthUnit.Percent);
            }

            if (_altitudeRow != null)
                _altitudeRow.style.display = controllers.ActiveMode == TravelMode.Helicopter
                    ? DisplayStyle.Flex : DisplayStyle.None;

            if (_modeLabel != null) _modeLabel.text = ModeLabel(controllers.ActiveMode);

            CameraPreset preset = cameras != null ? cameras.GetActivePreset() : null;
            if (preset != null && _cameraLabel != null) _cameraLabel.text = preset.displayName;

            if (_place != null && CityConfig.Geo != null)
            {
                string label = LocationLabel.Get(CityConfig.Geo, a.Position.x, a.Position.y);
                int split = label.IndexOf(" · ", StringComparison.Ordinal);
                if (split >= 0)
                {
                    _place.text = label.Substring(0, split);
                    _signs.text = label.Substring(split + 3);
                }
                else if (label.Contains(" & "))
                {
                    _place.text = string.Empty;
                    _signs.text = label;
                }
                else
                {
                    _place.text = label;
                    _signs.text = string.Empty;
                }
            }

            if (_hint != null) _hint.text = HintFor(controllers.ActiveMode);
            PlaceModeIndicator();
        }

        // ------------------------------------------------------------------ actions

        public void SetMode(TravelMode mode)
        {
            if (controllers == null) return;
            controllers.SetMode(mode);
            _lastMode = mode;
            SyncModeClasses();
            PlaceModeIndicator();
            if (cameras != null)
            {
                CameraPreset preset = cameras.GetActivePreset();
                if (preset == null || !preset.follow) SetCamera("chase");
            }
        }

        public void SetCamera(string id)
        {
            cameras?.SetPreset(id, true);
            SyncCameraClasses();
            SetPopover(_cameraPopover, false);
        }

        void CycleCamera(int step)
        {
            cameras?.Cycle(step);
            SyncCameraClasses();
        }

        public void SetTime(string id)
        {
            lighting?.ApplyPreset(id);
            SyncTimeClasses();
        }

        public void Teleport(string id)
        {
            if (controllers == null) return;
            controllers.TeleportTo(id);
            cameras?.ResetForTeleport();
            SyncCameraClasses();
            SetPopover(_teleportPopover, false);
        }

        public void ToggleHud()
        {
            _hidden = !_hidden;
            _root?.EnableInClassList("hud-hidden", _hidden);
            if (_hidden)
            {
                SetPopover(_teleportPopover, false);
                SetPopover(_cameraPopover, false);
            }
        }

        // ------------------------------------------------------------------ sync

        void SyncModeClasses()
        {
            if (_modeButtons == null || controllers == null) return;
            for (int i = 0; i < _modeButtons.Length; i++)
                _modeButtons[i]?.EnableInClassList("active", (int)controllers.ActiveMode == i);
        }

        void SyncCameraClasses()
        {
            if (_cameraButtons == null || cameras == null) return;
            for (int i = 0; i < _cameraButtons.Length; i++)
                _cameraButtons[i]?.EnableInClassList("active", CameraIds[i] == cameras.activePresetId);
        }

        void SyncTimeClasses()
        {
            if (_timeButtons == null || lighting == null) return;
            string active = lighting.Active != null ? lighting.Active.id : "golden";
            for (int i = 0; i < _timeButtons.Length; i++)
                _timeButtons[i]?.EnableInClassList("active", TimeIds[i] == active);
        }

        void PlaceModeIndicator()
        {
            if (_modeIndicator == null || _modeButtons == null) return;

            Button active = null;
            for (int i = 0; i < _modeButtons.Length; i++)
                if (_modeButtons[i] != null && _modeButtons[i].ClassListContains("active")) { active = _modeButtons[i]; break; }
            if (active == null) return;

            Rect layout = active.layout;
            if (layout.width <= 1f) return;

            _modeIndicator.style.left = layout.x;
            _modeIndicator.style.width = layout.width;
        }

        static void SetPopover(VisualElement popover, bool open)
        {
            if (popover == null) return;
            popover.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
            popover.EnableInClassList("is-open", open);
        }

        static void TogglePopover(VisualElement popover)
        {
            if (popover == null) return;
            bool open = popover.resolvedStyle.display == DisplayStyle.Flex;
            SetPopover(popover, !open);
        }

        // ------------------------------------------------------------------ keyboard

        void HandleKeyboard()
        {
#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.digit1Key.wasPressedThisFrame) SetMode(TravelMode.Person);
            else if (kb.digit2Key.wasPressedThisFrame) SetMode(TravelMode.Car);
            else if (kb.digit3Key.wasPressedThisFrame) SetMode(TravelMode.Cyclist);
            else if (kb.digit4Key.wasPressedThisFrame) SetMode(TravelMode.Helicopter);
            else if (kb.digit5Key.wasPressedThisFrame) SetCamera("chase");
            else if (kb.digit6Key.wasPressedThisFrame) SetCamera("action");
            else if (kb.digit7Key.wasPressedThisFrame) SetCamera("pov");
            else if (kb.digit8Key.wasPressedThisFrame) SetCamera("fifthAve");
            else if (kb.digit9Key.wasPressedThisFrame) SetCamera("timesSquare");
            else if (kb.digit0Key.wasPressedThisFrame) SetCamera("chryslerGCT");

            if (kb.vKey.wasPressedThisFrame) CycleCamera(kb.leftShiftKey.isPressed ? -1 : 1);
            if (kb.hKey.wasPressedThisFrame) ToggleHud();
            if (kb.mKey.wasPressedThisFrame) mapSystem?.Toggle();
            if (kb.escapeKey.wasPressedThisFrame && mapSystem != null && mapSystem.IsFullMapOpen)
                mapSystem.SetOpen(false);
#endif
        }

        // ------------------------------------------------------------------ text

        static string ModeLabel(TravelMode mode)
        {
            switch (mode)
            {
                case TravelMode.Person: return "Pedestrian";
                case TravelMode.Car: return "NYC Yellow Taxi";
                case TravelMode.Cyclist: return "NYC Bike Lane Cyclist";
                default: return "Sightseeing Helicopter";
            }
        }

        static string HintFor(TravelMode mode)
        {
            switch (mode)
            {
                case TravelMode.Person: return "WASD walk · Shift sprint · Space jump · Drag orbit";
                case TravelMode.Car: return "WASD drive · Shift boost · Drag orbit";
                case TravelMode.Cyclist: return "WASD pedal · Shift sprint · Drag orbit";
                default: return "WASD pitch/yaw · Space/E up · Q/C down · Shift boost";
            }
        }
    }
}
