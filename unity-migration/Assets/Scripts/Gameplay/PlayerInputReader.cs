using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Nyc.Gameplay
{
    /// <summary>
    /// Keyboard + touch input, ported from ThirdPersonControllerManager.bindKeyboard /
    /// setTouchInput (controllers.js:132-174). Keyboard and touch are OR-ed, and touch may be
    /// analog (0..1) so the on-screen stick can drive partial throttle.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        public ControllerInput Current { get; private set; }
        public bool InputEnabled = true;

        float _tForward, _tBackward, _tLeft, _tRight, _tUp, _tDown, _tBoost;

        void Update() => Read();
        void OnApplicationFocus(bool focused) { if (!focused && !Application.isEditor) ResetAll(); }
        void OnApplicationPause(bool paused) { if (paused && !Application.isEditor) ResetAll(); }

        public void SetTouch(string action, float value)
        {
            value = Mathf.Clamp01(value);
            switch (action)
            {
                case "forward": _tForward = value; break;
                case "backward": _tBackward = value; break;
                case "left": _tLeft = value; break;
                case "right": _tRight = value; break;
                case "up": _tUp = value; break;
                case "down": _tDown = value; break;
                case "boost": _tBoost = value; break;
            }
        }

        public void ClearTouch()
        {
            _tForward = _tBackward = _tLeft = _tRight = _tUp = _tDown = _tBoost = 0f;
        }

        /// <summary>Editor/replay hook for deterministic movement captures.</summary>
        public void SetManualInput(ControllerInput state)
        {
            Current = state;
        }

        public void ResetAll()
        {
            ClearTouch();
            Current = default;
        }

        void Read()
        {
            if (!InputEnabled)
            {
                Current = default;
                return;
            }

            bool f, b, l, r, u, d, boost;

#if ENABLE_INPUT_SYSTEM
            Keyboard kb = Keyboard.current;
            if (kb != null)
            {
                f = kb.wKey.isPressed || kb.upArrowKey.isPressed;
                b = kb.sKey.isPressed || kb.downArrowKey.isPressed;
                l = kb.aKey.isPressed || kb.leftArrowKey.isPressed;
                r = kb.dKey.isPressed || kb.rightArrowKey.isPressed;
                u = kb.spaceKey.isPressed || kb.eKey.isPressed;
                d = kb.qKey.isPressed || kb.cKey.isPressed;
                boost = kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed;
            }
            else
            {
                f = b = l = r = u = d = boost = false;
            }
#else
            f = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow);
            b = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            l = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);
            r = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
            u = Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.E);
            d = Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.C);
            boost = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
#endif

            Current = new ControllerInput
            {
                Forward  = Mathf.Max(f ? 1f : 0f, _tForward),
                Backward = Mathf.Max(b ? 1f : 0f, _tBackward),
                Left     = Mathf.Max(l ? 1f : 0f, _tLeft),
                Right    = Mathf.Max(r ? 1f : 0f, _tRight),
                Up       = Mathf.Max(u ? 1f : 0f, _tUp),
                Down     = Mathf.Max(d ? 1f : 0f, _tDown),
                Boost    = Mathf.Max(boost ? 1f : 0f, _tBoost)
            };
        }
    }
}
