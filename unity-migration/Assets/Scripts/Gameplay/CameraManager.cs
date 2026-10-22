using System;
using UnityEngine;
using Nyc.Core;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Nyc.Gameplay
{
    [Serializable]
    public sealed class CameraPreset
    {
        public string id;
        public string displayName;
        public bool follow;
        public float fov = 56f;
    }

    /// <summary>
    /// 11 camera presets with the exact framing and smoothing of cameras.js:
    /// 3 follow cameras (chase / low action / POV) and 8 scenic ones, plus orbit, zoom and the
    /// entity-frame smoothing that keeps a speeding car from making the camera surge.
    ///
    /// All positions are authored in the SOURCE frame and converted once per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CameraManager : MonoBehaviour
    {
        [Header("References")]
        public Camera cam;
        public ThirdPersonControllerManager controllers;

        [Header("Presets")]
        public CameraPreset[] presets = DefaultPresets();
        public string activePresetId = "chase";

        [Header("Orbit (runtime)")]
        public float orbitYawOffset;
        public float orbitPitchOffset = 0.09f;
        public float zoomMultiplier = 1f;

        [Header("Scenic anchors")]
        public Vector4 bryantParkLawn = new Vector4(-260f, -150f, 40f, 120f); // minX, maxX, minZ, maxZ
        public float bryantParkTop = 1.3f;

        public Vector3 CurrentPosition => _currentPos;
        public Vector3 CurrentLookAt => _currentLook;

        Vector3 _currentPos, _currentLook;
        Vector3 _followOffset, _followLookOffset;
        TravelController _followEntity;
        float _followBaseY;
        float _smoothedSpeed;
        float _userOrbitTimer;
        bool _initialized;
        bool _dragging;

        const float SmoothK = 8.5f;

        // ------------------------------------------------------------------ lifecycle

        void Reset() => presets = DefaultPresets();

        void Awake()
        {
            if (!cam) cam = GetComponent<Camera>();
            if (!cam) cam = Camera.main;
            if (!controllers) controllers = FindFirstObjectByType<ThirdPersonControllerManager>();
        }

        void LateUpdate() => Tick(Time.deltaTime, Time.time);

        // ------------------------------------------------------------------ public API

        public CameraPreset GetActivePreset()
        {
            for (int i = 0; i < presets.Length; i++)
                if (presets[i].id == activePresetId) return presets[i];
            return presets.Length > 0 ? presets[0] : null;
        }

        public void SetPreset(string id, bool snap = false)
        {
            CameraPreset p = null;
            for (int i = 0; i < presets.Length; i++) if (presets[i].id == id) { p = presets[i]; break; }
            if (p == null) return;

            activePresetId = id;
            if (cam) cam.fieldOfView = p.fov;
            if (snap) _initialized = false;
        }

        public void Cycle(int step)
        {
            if (presets == null || presets.Length == 0) return;
            int idx = 0;
            for (int i = 0; i < presets.Length; i++) if (presets[i].id == activePresetId) { idx = i; break; }
            idx = (idx + step + presets.Length) % presets.Length;
            SetPreset(presets[idx].id, true);
        }

        public void ResetForTeleport()
        {
            orbitYawOffset = 0f;
            orbitPitchOffset = 0.09f;
            zoomMultiplier = 1f;
            _userOrbitTimer = 0f;
            _smoothedSpeed = 0f;
            _followEntity = null;
            _followOffset = Vector3.zero;
            _followLookOffset = Vector3.zero;
            _dragging = false;
            SetPreset("chase", true);
        }

        // ------------------------------------------------------------------ tick

        public void Tick(float dt, float elapsed)
        {
            if (!cam || !controllers) return;
            TravelController ent = controllers.Active;
            if (ent == null) return;

            HandlePointer(dt);

            if (_userOrbitTimer > 0f && !_dragging) _userOrbitTimer = Mathf.Max(0f, _userOrbitTimer - dt);

            if (!_dragging && _userOrbitTimer <= 0f && Mathf.Abs(ent.Speed) > 0.5f)
            {
                orbitYawOffset -= Mathf.Round(orbitYawOffset / (Mathf.PI * 2f)) * (Mathf.PI * 2f);
                orbitYawOffset += (0f - orbitYawOffset) * (1f - Mathf.Exp(-dt * 2.2f));
            }

            _smoothedSpeed += (Mathf.Abs(ent.Speed) - _smoothedSpeed) * (1f - Mathf.Exp(-dt * 3.0f));
            float speedLag = _smoothedSpeed * 0.1f;

            float totalYaw = ent.Heading + orbitYawOffset;
            Vector2 p = ent.Position;
            float y = ent.Height;

            Vector3 dp = default, dl = default;

            switch (activePresetId)
            {
                case "chase":
                {
                    float dist = ent.ChaseDistance * zoomMultiplier * (1f + Mathf.Min(0.25f, _smoothedSpeed * 0.01f));
                    float height = ent.ChaseHeight * zoomMultiplier + Mathf.Sin(orbitPitchOffset) * dist * 0.85f;
                    float horiz = Mathf.Cos(orbitPitchOffset) * dist + speedLag;

                    dp = new Vector3(p.x - Mathf.Sin(totalYaw) * horiz, Mathf.Max(0.65f, y + height),
                                     p.y - Mathf.Cos(totalYaw) * horiz);
                    dl = new Vector3(p.x + Mathf.Sin(ent.Heading) * 5.2f,
                                     y + ent.CameraTargetOffset.y + ent.LookUpBias,
                                     p.y + Mathf.Cos(ent.Heading) * 5.2f);
                    break;
                }

                case "action":
                {
                    float side = ent.Mode == TravelMode.Car ? 2.65f : ent.Mode == TravelMode.Helicopter ? 4.2f : 0.95f;
                    float back = (ent.Mode == TravelMode.Car ? 3.6f : ent.Mode == TravelMode.Helicopter ? 8.5f : 2.8f) + speedLag;
                    float lowY = ent.Mode == TravelMode.Helicopter ? y + 1.2f : y + 0.92f;

                    dp = new Vector3(p.x - Mathf.Sin(ent.Heading) * back + Mathf.Cos(ent.Heading) * side,
                                     Mathf.Max(0.55f, lowY),
                                     p.y - Mathf.Cos(ent.Heading) * back - Mathf.Sin(ent.Heading) * side);
                    dl = new Vector3(p.x + Mathf.Sin(ent.Heading) * 3.2f, y + 1.05f,
                                     p.y + Mathf.Cos(ent.Heading) * 3.2f);
                    break;
                }

                case "pov":
                {
                    float fwd = 0.35f, eyeY = 1.68f;
                    if (ent.Mode == TravelMode.Car) { fwd = 0.55f; eyeY = 1.52f; }
                    else if (ent.Mode == TravelMode.Cyclist) { fwd = 0.35f; eyeY = 1.55f; }
                    else if (ent.Mode == TravelMode.Helicopter) { fwd = 1.65f; eyeY = 1.65f; }

                    dp = new Vector3(p.x + Mathf.Sin(ent.Heading) * fwd, y + eyeY,
                                     p.y + Mathf.Cos(ent.Heading) * fwd);
                    dl = new Vector3(p.x + Mathf.Sin(totalYaw) * 40f,
                                     y + eyeY + Mathf.Sin(-orbitPitchOffset) * 18f + 1.5f,
                                     p.y + Mathf.Cos(totalYaw) * 40f);
                    break;
                }

                case "centralPark":
                {
                    float sway = Mathf.Sin(elapsed * 0.22f) * 3.5f;
                    dp = new Vector3(-70f + sway, 14f, -1425f);
                    dl = new Vector3(-250f, 6.5f, -1690f);
                    break;
                }

                case "fifthAve":
                {
                    float sway = Mathf.Sin(elapsed * 0.25f) * 1.8f;
                    dp = new Vector3(0f + sway, 6.5f, -20f);
                    dl = new Vector3(-60f, 150f, 686f);
                    break;
                }

                case "timesSquare":
                {
                    float sway = Mathf.Sin(elapsed * 0.3f) * 3f;
                    dp = new Vector3(-603f + sway, 5.5f, -290f);
                    dl = new Vector3(-566f, 40f, -40f);
                    break;
                }

                case "chryslerGCT":
                {
                    float sway = Mathf.Cos(elapsed * 0.22f) * 2.5f;
                    dp = new Vector3(300f + sway, 9.5f, 95f);
                    dl = new Vector3(470f, 120f, -50f);
                    break;
                }

                case "observatory":
                {
                    float pan = Mathf.Sin(elapsed * 0.18f) * 80f;
                    dp = new Vector3(-58f, 322f, 660f);
                    dl = new Vector3(420f + pan, 150f, -200f);
                    break;
                }

                case "bryantPark":
                {
                    float sway = Mathf.Sin(elapsed * 0.25f) * 1.5f;
                    float lz = (bryantParkLawn.z + bryantParkLawn.w) * 0.5f;
                    dp = new Vector3(bryantParkLawn.x + 18f, bryantParkTop + 1.7f, lz + sway);
                    dl = new Vector3(bryantParkLawn.y + 260f, 70f, lz - 60f);
                    break;
                }

                case "waterfront":
                {
                    float drift = Mathf.Sin(elapsed * 0.15f) * 40f;
                    dp = new Vector3(1640f, 60f, 60f + drift);
                    dl = new Vector3(150f, 150f, -150f);
                    break;
                }

                case "drone":
                {
                    float angle = elapsed * 0.07f + 0.65f;
                    float radius = 720f + Mathf.Sin(elapsed * 0.23f) * 140f;
                    float alt = 330f + Mathf.Cos(elapsed * 0.19f) * 90f;
                    dp = new Vector3(Mathf.Cos(angle) * radius + 60f, alt, Mathf.Sin(angle) * radius + 250f);
                    dl = new Vector3(60f, 160f, 250f);
                    break;
                }
            }

            Vector3 desiredPos = CityFrame.ToUnity(dp.x, dp.y, dp.z);
            Vector3 desiredLook = CityFrame.ToUnity(dl.x, dl.y, dl.z);

            CameraPreset preset = GetActivePreset();
            bool followPreset = preset != null && preset.follow;

            if (!_initialized || activePresetId == "pov")
            {
                _currentPos = desiredPos;
                _currentLook = desiredLook;
                _initialized = true;
                _followEntity = null;
            }
            else if (followPreset && activePresetId != "pov")
            {
                Vector3 entityPos = CityFrame.ToUnity(p.x, y, p.y);

                if (_followEntity != ent)
                {
                    _followEntity = ent;
                    _followBaseY = entityPos.y;
                    _followOffset = _currentPos - entityPos;
                    _followLookOffset = _currentLook - entityPos;
                }

                float k = 1f - Mathf.Exp(-dt * SmoothK);
                _followBaseY += (entityPos.y - _followBaseY) * k;
                _followOffset = Vector3.Lerp(_followOffset, desiredPos - entityPos, k);
                _followLookOffset = Vector3.Lerp(_followLookOffset, desiredLook - entityPos, k);

                Vector3 anchor = new Vector3(entityPos.x, _followBaseY, entityPos.z);
                _currentPos = anchor + _followOffset;
                _currentLook = anchor + _followLookOffset;
                _currentPos.y = Mathf.Max(0.55f, _currentPos.y);
            }
            else
            {
                _followEntity = null;
                float k = 1f - Mathf.Exp(-dt * SmoothK);
                _currentPos = Vector3.Lerp(_currentPos, desiredPos, k);
                _currentLook = Vector3.Lerp(_currentLook, desiredLook, k);
            }

            Vector3 forward = _currentLook - _currentPos;
            if (forward.sqrMagnitude > 1e-6f)
                cam.transform.SetPositionAndRotation(_currentPos, Quaternion.LookRotation(forward, Vector3.up));
            else
                cam.transform.position = _currentPos;

            if (preset != null) cam.fieldOfView = preset.fov;
        }

        // ------------------------------------------------------------------ pointer

        void HandlePointer(float dt)
        {
#if ENABLE_INPUT_SYSTEM
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) { _dragging = true; _userOrbitTimer = 4f; }
                if (mouse.leftButton.wasReleasedThisFrame) _dragging = false;

                if (_dragging)
                {
                    Vector2 d = mouse.delta.ReadValue();
                    orbitYawOffset -= d.x * 0.0065f;
                    orbitPitchOffset = Mathf.Clamp(orbitPitchOffset + d.y * 0.005f, -0.32f, 0.68f);
                    _userOrbitTimer = 4f;
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    zoomMultiplier = Mathf.Clamp(zoomMultiplier + scroll * 0.001f, 0.45f, 2.4f);
            }

            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
            {
                Vector2 d = touch.primaryTouch.delta.ReadValue();
                orbitYawOffset -= d.x * 0.0065f;
                orbitPitchOffset = Mathf.Clamp(orbitPitchOffset + d.y * 0.005f, -0.32f, 0.68f);
                _userOrbitTimer = 4f;
                _dragging = true;
            }
            else if (touch != null && !touch.primaryTouch.press.isPressed && _dragging && mouse == null)
            {
                _dragging = false;
            }
#endif
        }

        // ------------------------------------------------------------------ defaults

        public static CameraPreset[] DefaultPresets() => new[]
        {
            new CameraPreset { id = "chase",       displayName = "3rd-Person Chase",         follow = true,  fov = 56f },
            new CameraPreset { id = "action",      displayName = "Low-Angle Action",         follow = true,  fov = 60f },
            new CameraPreset { id = "pov",         displayName = "First-Person / Cockpit",   follow = true,  fov = 68f },
            new CameraPreset { id = "centralPark", displayName = "Central Park Oasis",       follow = false, fov = 52f },
            new CameraPreset { id = "fifthAve",    displayName = "5th Ave & Empire State",   follow = false, fov = 48f },
            new CameraPreset { id = "timesSquare", displayName = "Times Square Crossroads",  follow = false, fov = 58f },
            new CameraPreset { id = "chryslerGCT", displayName = "42nd St · Chrysler & GCT", follow = false, fov = 50f },
            new CameraPreset { id = "observatory", displayName = "Empire State 86th Deck",   follow = false, fov = 52f },
            new CameraPreset { id = "bryantPark",  displayName = "Bryant Park & NYPL",       follow = false, fov = 52f },
            new CameraPreset { id = "waterfront",  displayName = "East River & Skyline",     follow = false, fov = 46f },
            new CameraPreset { id = "drone",       displayName = "Auto-Director Aerial",     follow = false, fov = 50f }
        };
    }
}
