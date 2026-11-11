// Connects the scene to the dashboard and routes commands, telemetry, and preview frames.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PaceScape.Performance;
using PaceScape.RunningTrack;
using PaceScape.Tracking;
using PaceScape.Treadmill;
using UnityEngine;

namespace PaceScape.RemoteControl
{
    // Connects the active tracking scene to the local control server without a scene asset change.
    [DisallowMultipleComponent]
    public sealed class RemoteControlClient : MonoBehaviour
    {
        [Serializable]
        private sealed class ConnectionSettings
        {
            public bool Enabled = true;
            public string ServerUrl = RemoteControlSettings.DefaultServerUrl;
            public string AccessToken = "";
            public bool AllowLanMotorControl;
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
            public readonly SemaphoreSlim CameraFrameReady = new SemaphoreSlim(0, 1);
            public readonly ClientWebSocket Socket = new ClientWebSocket();
            public Task Task;
            public string LatestState;
            public byte[] LatestCameraFrame;
            public string Error;
            public volatile bool IsConnected;

            public void PublishCameraFrame(byte[] frame)
            {
                Interlocked.Exchange(ref LatestCameraFrame, frame);
                // The main thread is the only producer. Coalesce wakeups like the frames themselves.
                if (CameraFrameReady.CurrentCount == 0)
                {
                    CameraFrameReady.Release();
                }
            }

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
        private readonly RemoteCameraPreview _camera = new RemoteCameraPreview();
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

        // Pauses movement, cancels the old socket and reloads project defaults before reconnecting.
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
                    _settings.AllowLanMotorControl = defaults.AllowLanMotorControl;
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
                    Debug.LogWarning($"[RemoteControl] Connection to {_serverUri.GetLeftPart(UriPartial.Path)} ended ({_session.Error}); retrying in 3 seconds.", this);
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
                            _session.Error = "Expected protocol 1 welcome";
                            _session.Stop();
                            return;
                        }

                        _session.IsConnected = true;
                        _lastControlAt = Time.unscaledTime;
                    }
                    catch (Exception)
                    {
                        _session.Error = "Invalid welcome message";
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
                    _session.Error = "Timed out waiting for protocol 1 welcome";
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

        // Applies a queued dashboard command on the Unity main thread.
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
                    if (message?.commandType == "camera.start" && _testScene == null)
                    {
                        ConnectionSession session = _session;
                        success = _camera.TryStart(session.PublishCameraFrame, out error);
                    }
                    else if (message?.commandType == "camera.stop" && _testScene == null)
                    {
                        StopCamera();
                        success = true;
                        error = null;
                    }
                    else if (message?.commandType is "treadmill.startRun" or "treadmill.resumeRun" or "treadmill.pauseRun" or "treadmill.stopRun" or "treadmill.speed")
                    {
                        if (TryBeginBeltCommand(message, out error))
                        {
                            return;
                        }
                    }
                    else if (message != null && message.commandType != null &&
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

            SendAcknowledgement(_session, commandId, success, error);
        }

        // Checks command eligibility before starting an asynchronous FTMS operation.
        private bool TryBeginBeltCommand(RemoteMessage message, out string error)
        {
            if (_treadmill == null || !CanUseMotorControl() || message.payload == null)
            {
                error = "FTMS motor control requires USB loopback or explicit LAN permission for the configured server IP.";
                return false;
            }

            RemotePayload payload = message.payload;
            bool startsRun = message.commandType == "treadmill.startRun";
            if (startsRun && (_tracker == null || !_tracker.CanRun || _tracker.RunState != RunningSession.RunState.Ready ||
                !_tracker.TryConfigureRunner(payload.paceMinutesPerKilometre, payload.spawnLane, payload.distanceBehindStartLine)))
            {
                error = "Run start requires valid runner settings and a tracked placement.";
                return false;
            }

            bool resumesRun = message.commandType == "treadmill.resumeRun";
            if (resumesRun && (_tracker == null || !_tracker.CanRun || _tracker.RunState != RunningSession.RunState.Paused))
            {
                error = "Zum Weiterlaufen muss der pausierte Ghost wieder vollständig getrackt sein.";
                return false;
            }

            BeltOperation operation = message.commandType switch
            {
                "treadmill.pauseRun" => BeltOperation.Pause,
                "treadmill.stopRun" => BeltOperation.Stop,
                "treadmill.speed" => BeltOperation.SetSpeed,
                _ => BeltOperation.Start
            };

            if (operation == BeltOperation.SetSpeed && payload.targetSpeedUnits <= 0)
            {
                error = "Invalid treadmill target speed.";
                return false;
            }

            if (operation == BeltOperation.Stop)
            {
                _tracker?.ResetRun();
            }

            ConnectionSession session = _session;
            bool requested = _treadmill.TryControlBelt(payload.deviceId, operation, payload.targetSpeedUnits,
                () => _session == session && !_isPaused && session.IsConnected &&
                    !session.Cancellation.IsCancellationRequested && Time.unscaledTime - _lastControlAt < 3f &&
                    (operation != BeltOperation.Start || _tracker.CanRun &&
                        _tracker.RunState == (resumesRun ? RunningSession.RunState.Paused : RunningSession.RunState.Ready)),
                result =>
                {
                    if (_session != session || !session.IsConnected)
                    {
                        return;
                    }

                    if (result == null && startsRun &&
                        !_tracker.TryStartRun(payload.paceMinutesPerKilometre, payload.spawnLane, payload.distanceBehindStartLine))
                    {
                        result = "Das Laufband hat den Start bestätigt, aber der Ghost konnte nicht starten. Laufband an der Konsole prüfen.";
                    }

                    if (result == null && resumesRun && !_tracker.TryResumeRun())
                    {
                        result = "Das Laufband hat den Neustart bestätigt, aber der Ghost konnte nicht weiterlaufen. Laufband an der Konsole prüfen.";
                    }

                    if (result == null && operation == BeltOperation.Pause)
                    {
                        _tracker?.TryPauseRun();
                    }
                    else if (result != null && operation == BeltOperation.Stop)
                    {
                        result = $"Ghost zurückgesetzt. Laufband-Stopp unbestätigt: {result}";
                    }

                    SendAcknowledgement(session, message.commandId, result == null, result);
                }, out error);
            if (!requested && operation == BeltOperation.Stop)
            {
                error = $"Ghost zurückgesetzt. Laufband-Stopp konnte nicht gesendet werden: {error}";
            }

            return requested;
        }

        private void SendAcknowledgement(ConnectionSession session, string commandId, bool success, string error)
        {
            // Publish the actual applied state before acknowledging so the dashboard cannot show optimistic values.
            Interlocked.Exchange(ref session.LatestState, null);
            session.Outgoing.Enqueue(CreateStateJson());
            session.Outgoing.Enqueue(JsonUtility.ToJson(new RemoteMessage
            {
                type = "ack",
                commandId = commandId,
                success = success,
                error = error
            }));
        }

        private bool CanUseMotorControl()
        {
            return _serverUri.Host == "127.0.0.1" || CanUseLanMotorControl();
        }

        private bool CanUseLanMotorControl()
        {
            return _settings.AllowLanMotorControl && _serverUri.HostNameType == UriHostNameType.IPv4 &&
                !_serverUri.IsLoopback;
        }

        private void StopCamera()
        {
            _camera.Stop();
            if (_session != null)
            {
                Interlocked.Exchange(ref _session.LatestCameraFrame, null);
            }
        }

        private void PauseTarget()
        {
            StopCamera();
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
                    lanMotorControlEnabled = CanUseLanMotorControl(),
                    cameraControlVersion = 1,
                    camera = _camera.CaptureState(),
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
                    ghostPresentationVersion = 1,
                    sideBySideVersion = 1,
                    ghostPresentation = _tracker.CaptureGhostPresentation(),
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

        // Owns the WebSocket and coordinates send and receive tasks until the session ends.
        private static async Task RunSessionAsync(ConnectionSession session, Uri uri, string hello)
        {
            Task sending = null;
            Task receiving = null;
            string phase = "WebSocket handshake";
            try
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(session.Cancellation.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    await session.Socket.ConnectAsync(uri, timeout.Token).ConfigureAwait(false);
                }

                phase = "Sending hello";
                await SendAsync(session, hello).ConfigureAwait(false);
                phase = "WebSocket message exchange";
                sending = SendLoopAsync(session);
                receiving = ReceiveLoopAsync(session);
                Task completed = await Task.WhenAny(sending, receiving).ConfigureAwait(false);
                await completed.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (session.Cancellation.IsCancellationRequested)
            {
                // Pausing or reconnecting deliberately stops the current session.
            }
            catch (OperationCanceledException)
            {
                session.Error = $"{phase} timed out";
            }
            catch (Exception exception)
            {
                session.Error = $"{phase}: {exception.GetBaseException().GetType().Name}";
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
                catch (Exception)
                {
                    // Cleanup cancellation must not replace the original failure.
                }

                session.Socket.Dispose();
                session.Cancellation.Dispose();
            }
        }

        // Sends queued text and consumes the latest available camera frame.
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

                byte[] frame = Interlocked.Exchange(ref session.LatestCameraFrame, null);
                if (frame != null && session.IsConnected)
                {
                    await session.Socket.SendAsync(new ArraySegment<byte>(frame), WebSocketMessageType.Binary, true,
                        token).ConfigureAwait(false);
                }

                await session.CameraFrameReady.WaitAsync(50, token).ConfigureAwait(false);
            }
        }

        private static async Task SendAsync(ConnectionSession session, string message)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(message);
            await session.Socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true,
                session.Cancellation.Token).ConfigureAwait(false);
        }

        // Assembles WebSocket messages and queues them for main-thread processing.
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
                        session.Error = $"Server closed WebSocket ({result.CloseStatus?.ToString() ?? "no status"})";
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
