using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaceScape.Performance;
using PaceScape.Tracking;
using PaceScape.Treadmill;
using UnityEngine;

namespace PaceScape.RemoteControl
{
    /// <summary>Connects the active tracking scene to the local control server without a scene asset change.</summary>
    [DisallowMultipleComponent]
    public sealed class RemoteControlClient : MonoBehaviour
    {
        [Serializable]
        private sealed class ConnectionSettings
        {
            public bool Enabled = true;
            public string ServerUrl = RemoteControlSettings.DefaultServerUrl;
            public string AccessToken = "";
        }

        private sealed class IncomingCommand
        {
            public string Json;
            public long ReceivedAt;
        }

        private sealed class ConnectionSession
        {
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public readonly ConcurrentQueue<IncomingCommand> Incoming = new ConcurrentQueue<IncomingCommand>();
            public readonly ConcurrentQueue<string> Outgoing = new ConcurrentQueue<string>();
            public readonly ClientWebSocket Socket = new ClientWebSocket();
            public Task Task;
            public string LatestState;
            public string Error;
            public volatile bool IsConnected;

            public void Stop()
            {
                try
                {
                    Cancellation.Cancel();
                    Socket.Abort();
                }
                catch (ObjectDisposedException)
                {
                    // The background task may already have finished cleanup.
                }
            }
        }

        private RunnerPerformance _performance;
        private TreadmillInput _treadmill;
        private TagTracker _tracker;
        private ServerTestScene _testScene;
        private MarkerPlanePlacement _placement;
        private ConnectionSettings _settings;
        private ConnectionSession _session;
        private string _hello;
        private Uri _serverUri;
        private float _nextConnectAt;
        private float _nextStateAt;
        private float _connectionStartedAt;
        private float _lastControlAt;
        private bool _isPaused;
        private bool _isConfigured;
        private bool _reloadConnectionRequested;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateForTrackingScene()
        {
            ServerTestScene testScene = FindObjectOfType<ServerTestScene>();
            TagTracker tracker = FindObjectOfType<TagTracker>(true);
            if ((tracker == null && testScene == null) || FindObjectOfType<RemoteControlClient>(true) != null)
            {
                return;
            }

            var root = new GameObject("PaceScape Remote Control");
            root.SetActive(false);
            var client = root.AddComponent<RemoteControlClient>();
            client._testScene = testScene;
            client._tracker = tracker;
            client._placement = FindObjectOfType<MarkerPlanePlacement>(true);
            root.SetActive(true);
        }

        private void OnEnable()
        {
            if (_treadmill != null)
            {
                _treadmill.enabled = true;
            }
        }

        private void Start()
        {
            // Keep BLE ownership local to this scene and avoid serialized scene changes.
            if (_tracker != null)
            {
                _treadmill = gameObject.AddComponent<TreadmillInput>();
            }
            _performance = gameObject.AddComponent<RunnerPerformance>();
            _performance.Bind(_tracker, _treadmill, _testScene);
            if (_tracker != null)
            {
                _tracker.PerformanceSource = _performance;
            }
            if (_testScene != null)
            {
                _testScene.PerformanceSource = _performance;
            }
            LoadConnectionSettings();
        }

        /// <summary>Pauses movement, cancels the old socket and reloads project defaults before reconnecting.</summary>
        public void ReloadConnectionSettings()
        {
            PauseTarget();
            _session?.Stop();
            _reloadConnectionRequested = true;
        }

        private void LoadConnectionSettings()
        {
            _isConfigured = false;
            _settings = new ConnectionSettings();
            string path = Path.Combine(Application.persistentDataPath, "pacescape-control.json");
            try
            {
                RemoteControlSettings defaults = Resources.Load<RemoteControlSettings>(RemoteControlSettings.ResourceName);
                if (defaults != null)
                {
                    _settings.Enabled = defaults.ConnectionEnabled;
                    _settings.ServerUrl = defaults.ServerUrl;
                    _settings.AccessToken = defaults.AccessToken;
                }

                // Device overrides take precedence over build defaults; Editor tests use the project tool.
                if (!Application.isEditor && File.Exists(path))
                {
                    JsonUtility.FromJsonOverwrite(File.ReadAllText(path), _settings);
                }

                if (!_settings.Enabled)
                {
                    return;
                }

                if (!RemoteControlSettings.TryParseServerUrl(_settings.ServerUrl, out _serverUri))
                {
                    Debug.LogWarning("[RemoteControl] Invalid server URL. Use a ws:// or wss:// address ending in /ws.", this);
                    return;
                }

                _hello = JsonUtility.ToJson(new RemoteMessage
                {
                    type = "hello",
                    protocol = 1,
                    role = "headset",
                    token = _settings.AccessToken
                });
                _isConfigured = _testScene != null || _tracker != null;
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[RemoteControl] Could not read connection settings ({exception.GetType().Name}).", this);
            }
        }

        private void Update()
        {
            if (_reloadConnectionRequested)
            {
                // Never start a replacement socket until the old send/receive tasks have stopped.
                if (_session != null && !_session.Task.IsCompleted)
                {
                    return;
                }

                _session = null;
                _reloadConnectionRequested = false;
                LoadConnectionSettings();
                _nextConnectAt = Time.unscaledTime;
            }

            if (!_isConfigured || _isPaused || (_tracker == null && _testScene == null))
            {
                return;
            }

            if (_session != null && _session.Task.IsCompleted)
            {
                PauseTarget();
                if (!string.IsNullOrEmpty(_session.Error))
                {
                    Debug.LogWarning($"[RemoteControl] Connection ended ({_session.Error}); retrying in 3 seconds.", this);
                }

                _session = null;
                _nextConnectAt = Time.unscaledTime + 3f;
            }

            if (_session == null && Time.unscaledTime >= _nextConnectAt)
            {
                _session = new ConnectionSession();
                _session.Socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                _session.Task = RunSessionAsync(_session, _serverUri, _hello);
                _nextStateAt = 0f;
                _connectionStartedAt = Time.unscaledTime;
            }

            if (_session == null)
            {
                return;
            }

            // Unity objects and JSON serialization stay on the main thread; socket tasks only move strings.
            if (_session.Incoming.TryDequeue(out IncomingCommand incoming))
            {
                if (!_session.IsConnected)
                {
                    try
                    {
                        RemoteMessage welcome = JsonUtility.FromJson<RemoteMessage>(incoming.Json);
                        if (welcome == null || welcome.type != "welcome" || welcome.protocol != 1)
                        {
                            _session.Stop();
                            return;
                        }

                        _session.IsConnected = true;
                        _lastControlAt = Time.unscaledTime;
                    }
                    catch (Exception)
                    {
                        _session.Stop();
                        return;
                    }
                }
                else
                {
                    RemoteMessage message = null;
                    try
                    {
                        message = JsonUtility.FromJson<RemoteMessage>(incoming.Json);
                    }
                    catch (Exception)
                    {
                        _session.Stop();
                    }

                    if (message != null && message.type == "control" && message.protocol == 1)
                    {
                        if (DateTime.UtcNow.Ticks - incoming.ReceivedAt <= TimeSpan.FromSeconds(3).Ticks)
                        {
                            _lastControlAt = Time.unscaledTime;
                        }
                    }
                    else if (message != null && message.type == "command")
                    {
                        ApplyCommand(incoming);
                    }
                }
            }

            if (_session.IsConnected && Time.unscaledTime - _lastControlAt > 6f)
            {
                PauseTarget();
                _session.Stop();
                return;
            }

            if (!_session.IsConnected)
            {
                PauseTarget();
                if (Time.unscaledTime - _connectionStartedAt > 10f)
                {
                    _session.Stop();
                }

                return;
            }

            if (Time.unscaledTime >= _nextStateAt)
            {
                Interlocked.Exchange(ref _session.LatestState, CreateStateJson());
                _nextStateAt = Time.unscaledTime + 1f;
            }
        }

        private void ApplyCommand(IncomingCommand incoming)
        {
            string commandId = null;
            bool success = false;
            string error;
            try
            {
                RemoteMessage message = JsonUtility.FromJson<RemoteMessage>(incoming.Json);
                commandId = message?.commandId;
                if (Time.unscaledTime - _lastControlAt > 6f ||
                    DateTime.UtcNow.Ticks - incoming.ReceivedAt > TimeSpan.FromSeconds(3).Ticks)
                {
                    error = "Command expired before application; it was not executed.";
                }
                else
                {
                    if (message != null && message.commandType != null &&
                        message.commandType.StartsWith("treadmill.", StringComparison.Ordinal))
                    {
                        success = RemoteControlCommands.TryApplyTreadmill(incoming.Json, _treadmill, out error);
                    }
                    else
                    {
                        success = _testScene != null
                            ? RemoteControlCommands.TryApplyTestScene(incoming.Json, _testScene, out error)
                            : RemoteControlCommands.TryApply(incoming.Json, _tracker, _placement, out error);
                    }
                }
            }
            catch (Exception exception)
            {
                error = $"Command failed ({exception.GetType().Name}).";
            }

            // Publish the actual applied state before acknowledging so the dashboard cannot show optimistic values.
            Interlocked.Exchange(ref _session.LatestState, null);
            _session.Outgoing.Enqueue(CreateStateJson());
            _session.Outgoing.Enqueue(JsonUtility.ToJson(new RemoteMessage
            {
                type = "ack",
                commandId = commandId,
                success = success,
                error = error
            }));
        }

        private void PauseTarget()
        {
            if (_testScene != null)
            {
                _testScene.PauseRun();
            }
            else
            {
                _tracker?.TryPauseRun();
            }
        }

        private string CreateStateJson()
        {
            if (_testScene != null)
            {
                RemoteState preview = _testScene.CaptureState();
                preview.comparisonControlVersion = 1;
                preview.performance = _performance != null ? _performance.CaptureState() : null;
                return JsonUtility.ToJson(new RemoteMessage { type = "state", state = preview });
            }

            return JsonUtility.ToJson(new RemoteMessage
            {
                type = "state",
                state = new RemoteState
                {
                    deviceName = SystemInfo.deviceName,
                    treadmillControlVersion = 1,
                    treadmill = _treadmill != null ? _treadmill.CaptureState() : null,
                    isEditor = Application.isEditor,
                    trackingEnabled = _tracker.isActiveAndEnabled,
                    phase = _tracker.Phase.ToString(),
                    startupStatus = _tracker.StartupStatus,
                    startupError = _tracker.StartupError,
                    placedMarkers = _tracker.PlacedMarkerCount,
                    pendingMarkers = _tracker.PendingMarkerCount,
                    runnerControlVersion = 1,
                    runnerModeControlVersion = 1,
                    ghostControlVersion = 1,
                    runnerMode = _tracker.Mode.ToString(),
                    comparisonControlVersion = 1,
                    performance = _performance != null ? _performance.CaptureState() : null,
                    activeRunnerPace = _tracker.ActivePaceMinutesPerKilometre,
                    runState = _tracker.RunState.ToString(),
                    runnableMarkers = _tracker.RunnableMarkerCount,
                    canRun = _tracker.CanRun,
                    paceMinutesPerKilometre = _tracker.PaceMinutesPerKilometre,
                    spawnLane = _tracker.SpawnLane,
                    distanceBehindStartLine = _tracker.DistanceBehindStartLine,
                    markerLength = _tracker.MarkerLength,
                    startupTimeoutSeconds = _tracker.StartupTimeoutSeconds,
                    maxTrackedMarkers = _tracker.MaxTrackedMarkers,
                    placeOnFloor = _placement != null && _placement.PlaceOnFloor,
                    queryPadding = _placement != null ? _placement.QueryPadding : 1f,
                    maxMarkerPlaneDistance = _placement != null ? _placement.MaxMarkerPlaneDistance : 0.15f,
                    minimumNormalAlignment = _placement != null ? _placement.MinimumNormalAlignment : 0.9f
                }
            });
        }

        private static async Task RunSessionAsync(ConnectionSession session, Uri uri, string hello)
        {
            Task sending = null;
            Task receiving = null;
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(session.Cancellation.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await session.Socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
                }

                await SendAsync(session, hello).ConfigureAwait(false);
                sending = SendLoopAsync(session);
                receiving = ReceiveLoopAsync(session);
                await Task.WhenAny(sending, receiving).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                session.Error = exception.GetType().Name;
            }
            finally
            {
                session.IsConnected = false;
                session.Stop();
                try
                {
                    if (sending != null && receiving != null)
                    {
                        await Task.WhenAll(sending, receiving).ConfigureAwait(false);
                    }
                }
                catch (Exception exception)
                {
                    session.Error = exception.GetType().Name;
                }

                session.Socket.Dispose();
                session.Cancellation.Dispose();
            }
        }

        private static async Task SendLoopAsync(ConnectionSession session)
        {
            CancellationToken token = session.Cancellation.Token;
            while (!token.IsCancellationRequested)
            {
                while (session.Outgoing.TryDequeue(out string message))
                {
                    await SendAsync(session, message).ConfigureAwait(false);
                }

                string state = Interlocked.Exchange(ref session.LatestState, null);
                if (state != null && session.IsConnected)
                {
                    await SendAsync(session, state).ConfigureAwait(false);
                }

                await Task.Delay(50, token).ConfigureAwait(false);
            }
        }

        private static async Task SendAsync(ConnectionSession session, string message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            await session.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                session.Cancellation.Token).ConfigureAwait(false);
        }

        private static async Task ReceiveLoopAsync(ConnectionSession session)
        {
            byte[] buffer = new byte[16384];
            while (!session.Cancellation.IsCancellationRequested)
            {
                int length = 0;
                WebSocketReceiveResult result;
                do
                {
                    result = await session.Socket.ReceiveAsync(new ArraySegment<byte>(buffer, length, buffer.Length - length),
                        session.Cancellation.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        return;
                    }

                    length += result.Count;
                    if (result.MessageType != WebSocketMessageType.Text || length >= buffer.Length)
                    {
                        throw new InvalidDataException("Unsupported WebSocket message.");
                    }
                }
                while (!result.EndOfMessage);

                string json = Encoding.UTF8.GetString(buffer, 0, length);
                if (session.Incoming.Count >= 8)
                {
                    throw new InvalidDataException("Command queue capacity exceeded.");
                }

                session.Incoming.Enqueue(new IncomingCommand { Json = json, ReceivedAt = DateTime.UtcNow.Ticks });
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            _isPaused = isPaused;
            if (isPaused)
            {
                PauseTarget();
                _session?.Stop();
            }
        }

        private void OnDisable()
        {
            PauseTarget();
            if (_treadmill != null)
            {
                _treadmill.enabled = false;
            }
            _session?.Stop();
        }
    }
}
