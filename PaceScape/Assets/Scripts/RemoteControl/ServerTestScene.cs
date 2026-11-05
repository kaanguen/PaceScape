using System;
using PaceScape.RunningTrack;
using UnityEngine;

namespace PaceScape.RemoteControl
{
    /// <summary>A marker-free scene target for testing the real WebSocket client in the Editor.</summary>
    [DisallowMultipleComponent]
    public sealed class ServerTestScene : MonoBehaviour
    {
        [SerializeField] private GameObject _runnerPrefab;
        [SerializeField] private Camera _previewCamera;
        private readonly RunningSession _session = new RunningSession();
        private GameObject _runner;
        private RunningTrackMotion _motion;
        private float _pace = 6f;
        private int _lane = 1;
        private float _startDistance = 0.5f;
        private bool _wasRunInBackground;
        private bool _isPaused;

        /// <summary>Gets whether a runner has been spawned.</summary>
        public bool HasRunner => _runner != null;

        /// <summary>Gets the active runner transform for preview and verification.</summary>
        public Transform RunnerTransform => _runner != null ? _runner.transform : null;

        /// <summary>Gets the explicit run state.</summary>
        public RunningSession.RunState RunState => _session.State;

        /// <summary>Gets whether this scene can start or resume its runner.</summary>
        public bool CanRun => isActiveAndEnabled && !_isPaused && HasRunner;

        private void OnEnable()
        {
            _wasRunInBackground = Application.runInBackground;
            Application.runInBackground = true;
        }

        private void Start()
        {
            RunningTrackVisual.Create(transform);
        }

        private void Update()
        {
            if (HasRunner && !_isPaused)
            {
                _session.Advance(_motion, Time.unscaledDeltaTime, _pace);
            }
        }

        private void LateUpdate()
        {
            if (_previewCamera != null)
            {
                Vector3 target = HasRunner ? _runner.transform.position : transform.position;
                _previewCamera.transform.position = target + new Vector3(0f, 5f, -9f);
                _previewCamera.transform.LookAt(target + Vector3.up * 0.7f);
            }
        }

        /// <summary>Pauses an active run without changing the test runner's position.</summary>
        public void PauseRun()
        {
            _session.TryPause();
        }

        /// <summary>Spawns exactly one waiting visual using the complete validated settings.</summary>
        public bool TrySpawnRunner(float pace, int lane, float startDistance)
        {
            if (!isActiveAndEnabled || HasRunner || _runnerPrefab == null || !_runnerPrefab.activeSelf ||
                _runnerPrefab.GetComponentInChildren<Rigidbody>(true) != null ||
                !TryConfigureRunner(pace, lane, startDistance))
            {
                return false;
            }

            try
            {
                _runner = new GameObject("Server test runner");
                _runner.transform.SetParent(transform, false);
                GameObject visual = Instantiate(_runnerPrefab, _runner.transform, false);
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                }

                Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
                float bottom = float.PositiveInfinity;
                foreach (Renderer renderer in renderers)
                {
                    bottom = Mathf.Min(bottom, renderer.bounds.min.y);
                }

                if (renderers.Length > 0)
                {
                    visual.transform.position += Vector3.up * (transform.position.y - bottom);
                }

                _motion = new RunningTrackMotion(_runner.transform, _lane, _startDistance);
                _session.Reset();
                return true;
            }
            catch
            {
                RemoveRunner();
                throw;
            }
        }

        /// <summary>Removes the test visual and ends the run while preserving the scene track.</summary>
        public void RemoveRunner()
        {
            _session.Reset();
            _motion = null;
            if (_runner != null)
            {
                _runner.SetActive(false);
                if (Application.isPlaying)
                {
                    Destroy(_runner);
                }
                else
                {
                    DestroyImmediate(_runner);
                }

                _runner = null;
            }
        }

        /// <summary>Applies pace and waiting start position together without an XR dependency.</summary>
        public bool TryConfigureRunner(float pace, int lane, float startDistance)
        {
            if (float.IsNaN(pace) || float.IsInfinity(pace) || pace < 0.1f || pace > 60f ||
                lane < 1 || lane > RunningTrackGeometry.LaneCount || float.IsNaN(startDistance) ||
                float.IsInfinity(startDistance) || startDistance < 0f || startDistance > 1000f)
            {
                return false;
            }

            bool changesPosition = lane != _lane || startDistance != _startDistance;
            if (changesPosition && _session.State != RunningSession.RunState.Ready)
            {
                return false;
            }

            if (changesPosition)
            {
                _motion?.ConfigureStart(lane, startDistance);
            }

            _pace = pace;
            _lane = lane;
            _startDistance = startDistance;
            return true;
        }

        internal bool TryApply(RemoteMessage message, out string error)
        {
            error = "Unsupported command or invalid test-scene state/settings.";
            RemotePayload p = message.payload;
            bool success = false;
            switch (message.commandType)
            {
                case "runner.create":
                    success = TrySpawnRunner(p.paceMinutesPerKilometre, p.spawnLane, p.distanceBehindStartLine);
                    break;
                case "runner.remove":
                    RemoveRunner();
                    success = true;
                    break;
                case "runner.configure":
                    success = TryConfigureRunner(p.paceMinutesPerKilometre, p.spawnLane, p.distanceBehindStartLine);
                    break;
                case "runner.pace":
                    success = TryConfigureRunner(p.paceMinutesPerKilometre, _lane, _startDistance);
                    break;
                case "runner.spawn":
                    success = TryConfigureRunner(_pace, p.spawnLane, p.distanceBehindStartLine);
                    break;
                case "run.start":
                    success = CanRun && _session.State == RunningSession.RunState.Ready &&
                        TryConfigureRunner(p.paceMinutesPerKilometre, p.spawnLane, p.distanceBehindStartLine) &&
                        _session.TryStart(CanRun);
                    break;
                case "run.pause":
                    success = _session.TryPause();
                    break;
                case "run.resume":
                    success = _session.TryResume(CanRun);
                    break;
                case "run.reset":
                    _session.Reset();
                    _motion?.ResetToStart();
                    success = true;
                    break;
            }

            if (success)
            {
                error = null;
            }

            return success;
        }

        internal RemoteState CaptureState()
        {
            return new RemoteState
            {
                deviceName = "PaceScape Server-Testszene",
                isEditor = Application.isEditor,
                testScene = true,
                spawnControlVersion = 1,
                runnerSpawned = HasRunner,
                trackingEnabled = false,
                phase = "TestScene",
                startupStatus = "Echte Unity-Testszene ohne Marker, Ebenenerkennung oder räumliche Anker.",
                startupError = string.Empty,
                runnerControlVersion = 1,
                runState = _session.State.ToString(),
                canRun = CanRun,
                paceMinutesPerKilometre = _pace,
                spawnLane = _lane,
                distanceBehindStartLine = _startDistance
            };
        }

        private void OnApplicationPause(bool isPaused)
        {
            _isPaused = isPaused;
            if (isPaused)
            {
                PauseRun();
            }
        }

        private void OnDisable()
        {
            PauseRun();
            Application.runInBackground = _wasRunInBackground;
        }
    }
}
