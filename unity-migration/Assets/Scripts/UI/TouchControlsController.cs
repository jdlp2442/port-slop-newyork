using System;
using UnityEngine;
using UnityEngine.UIElements;
using Nyc.Gameplay;

namespace Nyc.UI
{
    /// <summary>
    /// Virtual on-screen touch joystick and action buttons for mobile / touch displays.
    /// Port of slop-newyork-main/js/touch-controls.js.
    /// Interfaces directly with UI Toolkit UIDocument and feeds analog input into PlayerInputReader.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TouchControlsController : MonoBehaviour
    {
        [Header("References")]
        public UIDocument uiDocument;
        public PlayerInputReader inputReader;
        public ThirdPersonControllerManager controllerManager;

        [Header("Joystick Settings (Matching touch-controls.js)")]
        public float deadZone = 0.15f;
        public float maxRadius = 44.0f;
        public bool forceEnableOnDesktop = false;

        VisualElement _root;
        VisualElement _touchControls;
        VisualElement _stick;
        VisualElement _knob;
        Button _btnUp;
        Button _btnDown;
        Button _btnBoost;

        bool _stickActive;
        int _stickPointerId = -1;
        Vector2 _stickCenter;

        void Awake()
        {
            if (!uiDocument) uiDocument = GetComponent<UIDocument>();
            if (!inputReader) inputReader = FindAnyObjectByType<PlayerInputReader>();
            if (!controllerManager) controllerManager = FindAnyObjectByType<ThirdPersonControllerManager>();
        }

        void OnEnable()
        {
            if (!uiDocument) return;
            _root = uiDocument.rootVisualElement;
            if (_root == null) return;

            BindElements();

            if (controllerManager != null)
            {
                controllerManager.ModeChanged += OnModeChanged;
                SetMode(controllerManager.ActiveMode);
            }
        }

        void OnDisable()
        {
            if (controllerManager != null)
            {
                controllerManager.ModeChanged -= OnModeChanged;
            }
            ResetStick();
        }

        void BindElements()
        {
            _touchControls = _root.Q<VisualElement>("touch-controls");
            _stick = _root.Q<VisualElement>("touch-stick");
            _knob = _root.Q<VisualElement>("touch-stick-knob");
            _btnUp = _root.Q<Button>("touch-btn-up");
            _btnDown = _root.Q<Button>("touch-btn-down");
            _btnBoost = _root.Q<Button>("touch-btn-boost");

            if (_touchControls != null)
            {
                // On mobile / handheld devices, show touch controls; on desktop only if forced
                bool isTouch = Input.touchSupported || forceEnableOnDesktop;
                _touchControls.style.display = isTouch ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_stick != null)
            {
                _stick.RegisterCallback<PointerDownEvent>(OnStickPointerDown);
                _stick.RegisterCallback<PointerMoveEvent>(OnStickPointerMove);
                _stick.RegisterCallback<PointerUpEvent>(OnStickPointerUp);
                _stick.RegisterCallback<PointerCaptureOutEvent>(e => ResetStick());
            }

            if (_btnUp != null)
            {
                _btnUp.RegisterCallback<PointerDownEvent>(e => SetButtonAction("up", 1f));
                _btnUp.RegisterCallback<PointerUpEvent>(e => SetButtonAction("up", 0f));
                _btnUp.RegisterCallback<PointerCaptureOutEvent>(e => SetButtonAction("up", 0f));
            }

            if (_btnDown != null)
            {
                _btnDown.RegisterCallback<PointerDownEvent>(e => SetButtonAction("down", 1f));
                _btnDown.RegisterCallback<PointerUpEvent>(e => SetButtonAction("down", 0f));
                _btnDown.RegisterCallback<PointerCaptureOutEvent>(e => SetButtonAction("down", 0f));
            }

            if (_btnBoost != null)
            {
                _btnBoost.RegisterCallback<PointerDownEvent>(e => SetButtonAction("boost", 1f));
                _btnBoost.RegisterCallback<PointerUpEvent>(e => SetButtonAction("boost", 0f));
                _btnBoost.RegisterCallback<PointerCaptureOutEvent>(e => SetButtonAction("boost", 0f));
            }
        }

        void OnModeChanged(TravelMode mode) => SetMode(mode);

        public void SetMode(TravelMode mode)
        {
            ResetStick();

            if (_btnUp == null || _btnDown == null || _btnBoost == null) return;

            switch (mode)
            {
                case TravelMode.Person:
                    _btnUp.style.display = DisplayStyle.Flex;
                    _btnUp.text = "↑ Jump";
                    _btnDown.style.display = DisplayStyle.None;
                    _btnBoost.text = "⚡ Sprint";
                    break;

                case TravelMode.Cyclist:
                    _btnUp.style.display = DisplayStyle.None;
                    _btnDown.style.display = DisplayStyle.None;
                    _btnBoost.text = "⚡ Sprint";
                    break;

                case TravelMode.Car:
                    _btnUp.style.display = DisplayStyle.None;
                    _btnDown.style.display = DisplayStyle.None;
                    _btnBoost.text = "⚡ Boost";
                    break;

                case TravelMode.Helicopter:
                    _btnUp.style.display = DisplayStyle.Flex;
                    _btnUp.text = "↑ Ascend";
                    _btnDown.style.display = DisplayStyle.Flex;
                    _btnDown.text = "↓ Descend";
                    _btnBoost.text = "⚡ Boost";
                    break;
            }
        }

        void OnStickPointerDown(PointerDownEvent evt)
        {
            if (_stick == null) return;
            _stickActive = true;
            _stickPointerId = evt.pointerId;
            _stick.CapturePointer(evt.pointerId);

            Rect layout = _stick.layout;
            _stickCenter = new Vector2(layout.width * 0.5f, layout.height * 0.5f);
            UpdateStickPosition(evt.localPosition);
        }

        void OnStickPointerMove(PointerMoveEvent evt)
        {
            if (!_stickActive || evt.pointerId != _stickPointerId) return;
            UpdateStickPosition(evt.localPosition);
        }

        void OnStickPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId == _stickPointerId) ResetStick();
        }

        void UpdateStickPosition(Vector2 localPos)
        {
            Vector2 delta = localPos - _stickCenter;
            float dist = delta.magnitude;
            float radius = Mathf.Min(maxRadius, _stickCenter.x);

            float reach = Mathf.Clamp01(dist / Mathf.Max(1f, radius));
            float strength = Mathf.Max(0f, (reach - deadZone) / (1f - deadZone));

            Vector2 dir = dist > 0.001f ? delta / dist : Vector2.zero;
            Vector2 knobOffset = dir * (reach * radius);

            if (_knob != null)
            {
                _knob.style.translate = new Translate(knobOffset.x, knobOffset.y);
            }

            if (inputReader != null)
            {
                inputReader.SetTouch("left", Mathf.Max(0f, -dir.x * strength));
                inputReader.SetTouch("right", Mathf.Max(0f, dir.x * strength));
                inputReader.SetTouch("forward", Mathf.Max(0f, -dir.y * strength));
                inputReader.SetTouch("backward", Mathf.Max(0f, dir.y * strength));
            }
        }

        void ResetStick()
        {
            _stickActive = false;
            _stickPointerId = -1;

            if (_stick != null && _stick.HasPointerCapture(_stickPointerId))
            {
                _stick.ReleasePointer(_stickPointerId);
            }

            if (_knob != null)
            {
                _knob.style.translate = new Translate(0f, 0f);
            }

            if (inputReader != null)
            {
                inputReader.SetTouch("forward", 0f);
                inputReader.SetTouch("backward", 0f);
                inputReader.SetTouch("left", 0f);
                inputReader.SetTouch("right", 0f);
            }
        }

        void SetButtonAction(string action, float value)
        {
            if (inputReader != null)
            {
                inputReader.SetTouch(action, value);
            }
        }
    }
}
