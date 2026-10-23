using System;
using System.Text;
using System.Threading.Tasks;
#if UNITY_5_3_OR_NEWER
using NativeWebSocket;
#endif
using UnityEngine;
using Nyc.Core;
using Nyc.Gameplay;
using Nyc.Gameplay.Traffic;
using Nyc.Rendering;

#if UNITY_5_3_OR_NEWER
namespace Nyc.Networking
{
    /// <summary>
    /// Optional remote-control and telemetry bridge for the port.
    ///
    /// The original browser project has no network authority to preserve, so this component is
    /// deliberately opt-in. It provides a small, deterministic protocol for tools, a browser HUD,
    /// or a future multiplayer service without coupling simulation code to a transport.
    /// NativeWebSocket 2.x dispatches events on Unity's SynchronizationContext; no Update polling
    /// is required. WebGL callers should enable Run In Background when they need background traffic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NycRemoteSession : MonoBehaviour
    {
        [Header("Connection")]
        public string endpoint = "ws://localhost:3000";
        public bool connectOnPlay;
        public bool sendTelemetry = true;
        [Min(0.1f)] public float telemetryInterval = 0.25f;

        [Header("Runtime")]
        public bool IsConnected { get; private set; }
        public string LastError { get; private set; }
        public int ReceivedMessages { get; private set; }

        WebSocket _socket;
        float _nextTelemetry;
        NycMainBootstrapper _bootstrapper;

        [Serializable]
        sealed class RemoteCommand
        {
            public string type;
            public float x;
            public float z;
            public float hour = -1f;
            public string mode;
        }

        [Serializable]
        sealed class Telemetry
        {
            public string type = "state";
            public bool initialized;
            public int traffic;
            public float hour;
            public float x;
            public float z;
            public string mode;
        }

        void Awake()
        {
            _bootstrapper = NycMainBootstrapper.Instance;
            if (connectOnPlay) _ = ConnectAsync();
        }

        void Update()
        {
            if (!sendTelemetry || !IsConnected || Time.unscaledTime < _nextTelemetry) return;
            _nextTelemetry = Time.unscaledTime + telemetryInterval;
            _ = SendTelemetryAsync();
        }

        public async Task ConnectAsync()
        {
            if (IsConnected || string.IsNullOrWhiteSpace(endpoint)) return;

            Application.runInBackground = true;
            LastError = null;
            _socket = new WebSocket(endpoint);
            _socket.OnOpen += HandleOpen;
            _socket.OnError += HandleError;
            _socket.OnClose += HandleClose;
            _socket.OnMessage += HandleMessage;

            try
            {
                await _socket.Connect();
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                Debug.LogWarning($"[NYC Remote] Connect failed: {ex.Message}");
            }
        }

        public async Task DisconnectAsync()
        {
            if (_socket == null) return;
            try { await _socket.Close(); }
            catch (Exception ex) { LastError = ex.Message; }
            finally { IsConnected = false; _socket = null; }
        }

        public async Task SendTextAsync(string message)
        {
            if (!IsConnected || _socket == null || string.IsNullOrEmpty(message)) return;
            try { await _socket.SendText(message); }
            catch (Exception ex) { LastError = ex.Message; }
        }

        async Task SendTelemetryAsync()
        {
            if (_bootstrapper == null) _bootstrapper = NycMainBootstrapper.Instance;
            var active = _bootstrapper != null && _bootstrapper.controllerManager != null
                ? _bootstrapper.controllerManager.Active : null;
            var traffic = _bootstrapper != null && _bootstrapper.trafficSystem != null
                ? _bootstrapper.trafficSystem.activeCount : 0;
            var lighting = _bootstrapper != null ? _bootstrapper.lightingSystem : null;

            var payload = new Telemetry
            {
                initialized = _bootstrapper != null && _bootstrapper.IsInitialized,
                traffic = traffic,
                hour = lighting != null ? lighting.Hour : 0f,
                x = active != null ? active.Position.x : 0f,
                z = active != null ? active.Position.y : 0f,
                mode = _bootstrapper != null && _bootstrapper.controllerManager != null
                    ? _bootstrapper.controllerManager.ActiveMode.ToString() : string.Empty
            };
            await SendTextAsync(JsonUtility.ToJson(payload));
        }

        void HandleOpen()
        {
            IsConnected = true;
            Debug.Log($"[NYC Remote] Connected: {endpoint}");
        }

        void HandleError(string message)
        {
            LastError = message;
            IsConnected = false;
            Debug.LogWarning($"[NYC Remote] {message}");
        }

        void HandleClose(WebSocketCloseCode code)
        {
            IsConnected = false;
            Debug.Log($"[NYC Remote] Closed: {code}");
        }

        void HandleMessage(byte[] bytes)
        {
            ReceivedMessages++;
            string text = Encoding.UTF8.GetString(bytes);
            RemoteCommand command;
            try { command = JsonUtility.FromJson<RemoteCommand>(text); }
            catch (Exception ex)
            {
                LastError = ex.Message;
                return;
            }
            if (command == null || string.IsNullOrWhiteSpace(command.type)) return;
            ApplyCommand(command);
        }

        void ApplyCommand(RemoteCommand command)
        {
            if (_bootstrapper == null) _bootstrapper = NycMainBootstrapper.Instance;
            if (_bootstrapper == null) return;

            if (string.Equals(command.type, "teleport", StringComparison.OrdinalIgnoreCase) &&
                _bootstrapper.controllerManager != null)
            {
                _bootstrapper.controllerManager.TeleportActiveTo(command.x, command.z, 0f);
            }
            else if (string.Equals(command.type, "hour", StringComparison.OrdinalIgnoreCase) &&
                     _bootstrapper.lightingSystem != null && command.hour >= 0f)
            {
                _bootstrapper.lightingSystem.SetHour(command.hour);
            }
            else if (string.Equals(command.type, "mode", StringComparison.OrdinalIgnoreCase) &&
                     _bootstrapper.controllerManager != null &&
                     Enum.TryParse(command.mode, true, out TravelMode mode))
            {
                _bootstrapper.controllerManager.SetMode(mode);
            }
        }

        async void OnApplicationQuit()
        {
            await DisconnectAsync();
        }
    }
}
#else
namespace Nyc.Networking
{
    /// <summary>
    /// Compile-safe fallback used by the standalone headless harness, which intentionally does not
    /// reference Unity packages. The Unity player compiles the full NativeWebSocket implementation.
    /// </summary>
    [UnityEngine.DisallowMultipleComponent]
    public sealed class NycRemoteSession : UnityEngine.MonoBehaviour
    {
        public string endpoint = "ws://localhost:3000";
        public bool connectOnPlay;
        public bool sendTelemetry = true;
        public float telemetryInterval = 0.25f;
        public bool IsConnected { get; private set; }
        public string LastError { get; private set; }
        public int ReceivedMessages { get; private set; }

        public System.Threading.Tasks.Task ConnectAsync() => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task DisconnectAsync() => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task SendTextAsync(string message) => System.Threading.Tasks.Task.CompletedTask;
    }
}
#endif
