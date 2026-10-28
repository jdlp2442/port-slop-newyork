using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Nyc.Core;
using Nyc.Data;
using Nyc.Gameplay;

namespace Nyc.UI
{
    /// <summary>
    /// Radar minimap + full-screen street atlas, both driven by one baked texture.
    ///
    /// The three.js project draws the maps every frame on a 2D canvas (nyc-map.js). Here the
    /// cartography is rasterised once (NycMapBaker) and the UI only moves/scales the texture,
    /// which costs nothing per frame. Both maps are "grid north up", exactly like the original.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NycMapSystem : MonoBehaviour
    {
        [Header("References")]
        public ThirdPersonControllerManager controllers;
        public CameraManager cameras;

        [Header("Minimap")]
        public float minimapRadius = 260f;

        [Header("Atlas")]
        public float minZoom = 0.2f;
        public float maxZoom = 4f;

        public bool IsFullMapOpen => _open;

        NycMapBaker.MapTexture _map;
        VisualElement _radar, _mapEl, _player, _fullMap, _fullWrapper, _fullMapEl, _results;
        TextField _search;
        Label _hint, _info;

        bool _open;
        float _scale = 1f, _tx, _ty;
        bool _dragging, _moved;
        Vector2 _dragStart;
        float _txStart, _tyStart;
        float _S => _map.Size;

        readonly List<Place> _places = new List<Place>(512);

        struct Place
        {
            public string Name;
            public Vector2 Pos;
        }

        // ------------------------------------------------------------------ init

        public void Initialize(VisualElement root)
        {
            if (root == null) return;

            _radar = root.Q("radar");
            _mapEl = root.Q("minimap-map");
            _player = root.Q("minimap-player");
            _fullMap = root.Q("fullmap");
            _fullWrapper = root.Q("fullmap-canvas-wrap");
            _fullMapEl = root.Q("fullmap-map");
            _search = root.Q<TextField>("fullmap-search");
            _results = root.Q("fullmap-results");
            _hint = root.Q<Label>("fullmap-hint");
            _info = root.Q<Label>("fullmap-player-info");

            if (CityConfig.Geo == null) return;
            _map = NycMapBaker.Bake(CityConfig.Geo, 2048);

            if (_mapEl != null)
            {
                _mapEl.style.backgroundImage = new StyleBackground(_map.Texture);
                _mapEl.style.width = (float)_map.Size;
                _mapEl.style.height = (float)_map.Size;
            }
            if (_fullMapEl != null)
            {
                _fullMapEl.style.backgroundImage = new StyleBackground(_map.Texture);
                _fullMapEl.style.width = (float)_map.Size;
                _fullMapEl.style.height = (float)_map.Size;
            }

            BuildPlaces();

            if (_search != null) _search.RegisterValueChangedCallback(OnSearchChanged);
            if (_fullWrapper != null)
            {
                _fullWrapper.RegisterCallback<PointerDownEvent>(OnMapDown);
                _fullWrapper.RegisterCallback<PointerMoveEvent>(OnMapMove);
                _fullWrapper.RegisterCallback<PointerUpEvent>(OnMapUp);
                _fullWrapper.RegisterCallback<WheelEvent>(OnMapWheel);
            }

            SetOpen(false);
        }

        // ------------------------------------------------------------------ per frame

        public void Tick()
        {
            if (_map.Texture == null || controllers == null) return;
            TravelController a = controllers.Active;
            if (a == null) return;

            UpdateMinimap(a);
            if (_open) UpdateInfo(a);
        }

        void UpdateMinimap(TravelController a)
        {
            if (_radar == null || _mapEl == null) return;

            float w = _radar.resolvedStyle.width;
            float h = _radar.resolvedStyle.height;
            if (float.IsNaN(w) || w <= 1f) w = 288f;
            if (float.IsNaN(h) || h <= 1f) h = 184f;

            Vector2 pixel = _map.WorldToPixel(a.Position.x, a.Position.y);
            float s = (h / (2f * Mathf.Max(10f, minimapRadius))) / _map.PixelsPerMetre;
            float S = _S;

            _mapEl.style.scale = new Scale(new Vector3(s, s, 1f));
            _mapEl.style.translate = new Translate(
                w * 0.5f - pixel.x * s + S * (s - 1f) * 0.5f,
                h * 0.5f - pixel.y * s + S * (s - 1f) * 0.5f);

            if (_player != null)
                _player.transform.rotation = Quaternion.Euler(0f, 0f, -a.Heading * Mathf.Rad2Deg);
        }

        void UpdateInfo(TravelController a)
        {
            if (_info == null) return;
            string where = LocationLabel.Get(CityConfig.Geo, a.Position.x, a.Position.y);
            _info.text = $"You are at {where}";
        }

        // ------------------------------------------------------------------ open / close

        public void Toggle() => SetOpen(!_open);

        public void SetOpen(bool open)
        {
            _open = open;
            if (_fullMap != null)
                _fullMap.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;

            if (open)
            {
                TravelController a = controllers != null ? controllers.Active : null;
                if (a != null) CenterOn(a.Position);
                UpdateInfo(a);
            }
        }

        // ------------------------------------------------------------------ atlas camera

        void CenterOn(Vector2 world)
        {
            if (_map.Texture == null) return;

            float w = _fullWrapper != null ? _fullWrapper.resolvedStyle.width : 1200f;
            float h = _fullWrapper != null ? _fullWrapper.resolvedStyle.height : 700f;
            if (float.IsNaN(w) || w <= 1f) w = 1200f;
            if (float.IsNaN(h) || h <= 1f) h = 700f;

            _scale = Mathf.Min(w, h) / _S * 0.92f;
            Vector2 pixel = _map.WorldToPixel(world.x, world.y);

            _tx = w * 0.5f - pixel.x * _scale + _S * (_scale - 1f) * 0.5f;
            _ty = h * 0.5f - pixel.y * _scale + _S * (_scale - 1f) * 0.5f;

            ApplyFullTransform();
        }

        void ApplyFullTransform()
        {
            if (_fullMapEl == null) return;
            _fullMapEl.style.scale = new Scale(new Vector3(_scale, _scale, 1f));
            _fullMapEl.style.translate = new Translate(_tx, _ty);
        }

        Vector2 ScreenToPixel(Vector2 local)
        {
            float S = _S;
            return new Vector2(
                (local.x - _tx - S * 0.5f) / _scale + S * 0.5f,
                (local.y - _ty - S * 0.5f) / _scale + S * 0.5f);
        }

        // ------------------------------------------------------------------ input

        void OnMapDown(PointerDownEvent evt)
        {
            _dragging = true;
            _moved = false;
            _dragStart = evt.position;
            _txStart = _tx;
            _tyStart = _ty;
        }

        void OnMapMove(PointerMoveEvent evt)
        {
            if (!_dragging) return;
            Vector2 d = (Vector2)evt.position - _dragStart;
            if (d.sqrMagnitude > 16f) _moved = true;
            _tx = _txStart + d.x;
            _ty = _tyStart + d.y;
            ApplyFullTransform();
        }

        void OnMapUp(PointerUpEvent evt)
        {
            if (!_dragging) return;
            _dragging = false;
            if (_moved) return;

            Vector2 pixel = ScreenToPixel(evt.localPosition);
            Vector2 world = _map.PixelToWorld(pixel);

            if (controllers != null && controllers.Active != null)
            {
                controllers.TeleportActiveTo(world.x, world.y, controllers.Active.Heading);
                if (cameras != null) cameras.ResetForTeleport();
            }
            SetOpen(false);
        }

        void OnMapWheel(WheelEvent evt)
        {
            Vector2 local = evt.localMousePosition;
            Vector2 before = ScreenToPixel(local);

            float factor = Mathf.Exp(-evt.delta.y * 0.0015f);
            _scale = Mathf.Clamp(_scale * factor, minZoom, maxZoom);

            float S = _S;
            _tx = local.x - S * 0.5f - (before.x - S * 0.5f) * _scale;
            _ty = local.y - S * 0.5f - (before.y - S * 0.5f) * _scale;
            ApplyFullTransform();
        }

        // ------------------------------------------------------------------ search

        void OnSearchChanged(ChangeEvent<string> evt)
        {
            if (_results == null) return;
            _results.Clear();

            string q = (evt.newValue ?? string.Empty).Trim().ToLowerInvariant();
            if (q.Length < 2) return;

            int shown = 0;
            for (int i = 0; i < _places.Count && shown < 6; i++)
            {
                Place p = _places[i];
                if (p.Name.ToLowerInvariant().IndexOf(q, System.StringComparison.Ordinal) < 0) continue;

                Place captured = p;
                var b = new Button(() => CenterOn(captured.Pos)) { text = p.Name };
                b.AddToClassList("map-result");
                _results.Add(b);
                shown++;
            }
        }

        void BuildPlaces()
        {
            _places.Clear();
            NycGeoData geo = CityConfig.Geo;
            if (geo == null) return;

            for (int i = 0; i < geo.Streets.Count; i++)
            {
                StreetRec s = geo.Streets[i];
                if (s.Segs.Count == 0) continue;
                StreetSeg widest = s.Segs[0];
                for (int k = 1; k < s.Segs.Count; k++)
                    if (s.Segs[k].X1 - s.Segs[k].X0 > widest.X1 - widest.X0) widest = s.Segs[k];

                _places.Add(new Place
                {
                    Name = LocationLabel.Ordinal(s.N) + " St",
                    Pos = new Vector2((widest.X0 + widest.X1) * 0.5f, s.Z)
                });
            }

            for (int i = 0; i < geo.Avenues.Count; i++)
            {
                AvenueRec a = geo.Avenues[i];
                if (a.Segs.Count == 0) continue;
                _places.Add(new Place
                {
                    Name = a.Label,
                    Pos = new Vector2(a.X, (a.Segs[0].Z0 + a.Segs[0].Z1) * 0.5f)
                });
            }

            foreach (var kv in geo.Landmarks)
                _places.Add(new Place { Name = kv.Key, Pos = new Vector2(kv.Value.X, kv.Value.Z) });

            foreach (var kv in CityConfig.Teleports)
                _places.Add(new Place { Name = kv.Value.Name, Pos = new Vector2(kv.Value.X, kv.Value.Z) });
        }

        void OnDestroy()
        {
            if (_map.Texture) Destroy(_map.Texture);
        }
    }
}
