// Detects AprilTags and manages anchored track content, runner controls, and tracking recovery.

using System;
using System.Collections;
using System.Collections.Generic;
using MagicLeap.Android;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;
using MagicLeap.OpenXR.Features.Planes;
using MagicLeap.OpenXR.Features.SpatialAnchors;
using PaceScape.Performance;
using PaceScape.RunningTrack;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace PaceScape.Tracking
{
    // Anchors content on the detected surface beneath each AprilTag 36H11 for the current session.
    [DisallowMultipleComponent]
    public sealed class TagTracker : MonoBehaviour
    {
        // Current phase of the tracking startup sequence.
        public enum StartupPhase
        {
            Stopped,
            WaitingForScene,
            CheckingConfiguration,
            RequestingPermissions,
            WaitingForOpenXR,
            CheckingFeatures,
            StartingSubsystems,
            StartingMarkerDetector,
            Ready,
            Failed,
            UnsupportedPlatform,
            Suspended,
            Recovering
        }

        [SerializeField] private XROrigin _xrOrigin;
        [SerializeField] private GameObject _markerPrefab;
        [SerializeField] private GameObject _warmupCoachPrefab;
        [SerializeField, Min(0f)]
        [Tooltip("Object distance behind the start line in meters, opposite to the running direction. Can be changed for existing objects while waiting for a run.")]
        private float _distanceBehindStartLine = 0.5f;
        [SerializeField, Range(1, RunningTrackGeometry.LaneCount)]
        [Tooltip("Lane for waiting objects and new placements; 1 is the innermost lane.")]
        private int _spawnLane = 1;
        [SerializeField, Min(0.1f)]
        [Tooltip("Running pace in minutes per kilometre. 6 means 6:00 min/km (10 km/h). Changes also apply to existing runners.")]
        private float _paceMinutesPerKilometre = 6f;
        [SerializeField] private ARAnchorManager _anchorManager;
        [SerializeField] private MarkerPlanePlacement _planePlacement;
        [SerializeField, Min(0.001f)]
        [Tooltip("Printed tag side length in meters, including its black border but excluding the white margin. Restart tracking after changing this value.")]
        private float _markerLength = 0.1f;
        [SerializeField, Min(1f)] private float _startupTimeoutSeconds = 15f;
        [SerializeField, Min(1)] private int _maxTrackedMarkers = 64;

        private readonly TreadmillGhostPresentation _ghostPresentation = new TreadmillGhostPresentation();
        private string _presentationStatus = "Track";
        private bool _presentationVisible;

        [Header("Startup diagnostics (runtime)")]
        [SerializeField] private StartupPhase _startupPhase;
        [SerializeField] private string _startupStatus;
        [SerializeField] private string _startupError;

        // Gets the current startup phase.
        public StartupPhase Phase => _startupPhase;

        // Gets the current startup progress or failure description.
        public string StartupStatus => _startupStatus;

        // Gets the last failure, retained until the next startup attempt.
        public string StartupError => _startupError;

        // Gets the configured target pace in decimal minutes per kilometre.
        public float PaceMinutesPerKilometre => _paceMinutesPerKilometre;

        // Gets the lane configured for this running session.
        public int SpawnLane => _spawnLane;

        // Gets the configured start offset in metres.
        public float DistanceBehindStartLine => _distanceBehindStartLine;

        // Gets the physical printed marker side length in metres.
        public float MarkerLength => _markerLength;

        // Gets the active-time startup timeout in seconds.
        public float StartupTimeoutSeconds => _startupTimeoutSeconds;

        // Gets the marker capacity for this session.
        public int MaxTrackedMarkers => _maxTrackedMarkers;

        // Gets the number of placed local anchors, including temporarily untracked anchors.
        public int PlacedMarkerCount => _markers.Count;

        // Gets the number of marker poses awaiting a matching plane.
        public int PendingMarkerCount => _pendingMarkers.Count;

        // Gets the selected avatar speed mode.
        public RunningSession.RunnerMode Mode => _runSession.Mode;

        public bool IsSideBySide => Mode == RunningSession.RunnerMode.SideBySide;

        /// <summary>Whether the operator sets the speed of both the track and its companion.</summary>
        public bool IsManual => _manualEnvironment || Mode == RunningSession.RunnerMode.Manual;

        private bool _manualEnvironment;
        private float _manualTrackSpeedKmh = 10f;
        private double _trackTravelledMetres;
        private bool UsesCompanionPlacement => IsSideBySide || Mode == RunningSession.RunnerMode.Manual ||
            (IsManual && Mode == RunningSession.RunnerMode.FixedPace);
        private bool UsesRelativePlacement => UsesCompanionPlacement || IsManual || _ghostPresentation.Enabled;

        internal string RunEnvironment => IsManual ? "Manual" : UsesRelativePlacement ? "FTMS" : "Stadium";
        internal string GhostMode => UsesCompanionPlacement ? "SideBySide" : Mode.ToString();
        internal float ManualTrackSpeedKmh => _manualTrackSpeedKmh;

        internal bool TrySetManualTrackSpeed(float speedKmh)
        {
            if (!IsManual || !(speedKmh >= 1f && speedKmh <= 30f))
            {
                return false;
            }

            _manualTrackSpeedKmh = speedKmh;
            return true;
        }

        internal bool TryConfigureRunSetup(string environment, string ghostMode)
        {
            if (RunState != RunningSession.RunState.Ready ||
                (environment != "Stadium" && environment != "FTMS" && environment != "Manual") ||
                (ghostMode != "FixedPace" && ghostMode != "AllTimePersonalBest" && ghostMode != "SideBySide") ||
                (environment == "Stadium" && ghostMode == "SideBySide") ||
                (environment != "Stadium" && _markers.Count + _pendingMarkers.Count > 1))
            {
                return false;
            }

            if (!_runSession.TrySetMode(ghostMode))
            {
                return false;
            }
            _manualEnvironment = environment == "Manual";
            _trackTravelledMetres = 0;
            _ghostPresentation.TryConfigure(environment == "FTMS" ? "TreadmillRelative" : "Track",
                _ghostPresentation.SideOffsetMetres, _ghostPresentation.VisibilityDistanceMetres);
            PerformanceSource?.RunnerModeChanged();
            ApplyGhostPresentation();
            return true;
        }

        // Gets the active pace in minutes per kilometre; zero denotes a stopped or unavailable belt.
        public float ActivePaceMinutesPerKilometre
        {
            get
            {
                if (Mode == RunningSession.RunnerMode.AllTimePersonalBest)
                {
                    return PerformanceSource?.GetTargetPace(Mode, _paceMinutesPerKilometre) ?? _paceMinutesPerKilometre;
                }

                if (UsesCompanionPlacement)
                {
                    if (IsManual)
                    {
                        return 60f / _manualTrackSpeedKmh;
                    }

                    return PerformanceSource?.GetTargetPace(Mode, _paceMinutesPerKilometre) ?? 0f;
                }

                return _paceMinutesPerKilometre;
            }
        }

        // Gets or assigns the separate belt-measurement and record owner.
        public RunnerPerformance PerformanceSource { get; set; }

        // Selects the runner mode and pauses a running session when the mode changes.
        public bool TrySetRunnerMode(string mode)
        {
            bool selectsStadium = mode == "Stadium";
            if (selectsStadium)
            {
                if (RunState != RunningSession.RunState.Ready)
                {
                    return false;
                }
                mode = nameof(RunningSession.RunnerMode.FixedPace);
            }

            if (RunState == RunningSession.RunState.Countdown)
            {
                return false;
            }

            bool selectsManual = mode == nameof(RunningSession.RunnerMode.Manual);
            bool entersCompanionMode = (selectsManual && !IsManual) ||
                (mode == nameof(RunningSession.RunnerMode.SideBySide) && !IsSideBySide);
            if ((entersCompanionMode && (RunState != RunningSession.RunState.Ready ||
                _markers.Count + _pendingMarkers.Count > 1)) ||
                (IsManual && !selectsManual && RunState != RunningSession.RunState.Ready))
            {
                return false;
            }

            RunningSession.RunnerMode previous = Mode;
            bool success = _runSession.TrySetMode(mode);
            if (success && selectsStadium)
            {
                _manualEnvironment = false;
                _trackTravelledMetres = 0;
            }
            if (success && selectsStadium)
            {
                _ghostPresentation.TryConfigure("Track", _ghostPresentation.SideOffsetMetres,
                    _ghostPresentation.VisibilityDistanceMetres);
            }
            if (success && (previous != Mode || selectsStadium))
            {
                PerformanceSource?.RunnerModeChanged();
                ApplyGhostPresentation();
            }
            return success;
        }

        // Updates the target pace of existing and future objects after validation.
        public bool TrySetPace(float paceMinutesPerKilometre)
        {
            if (!IsPositiveFinite(paceMinutesPerKilometre) ||
                paceMinutesPerKilometre < 0.1f || paceMinutesPerKilometre > 60f)
            {
                return false;
            }

            _paceMinutesPerKilometre = paceMinutesPerKilometre;
            return true;
        }

        // Gets the explicit running state shared by all placed objects.
        public RunningSession.RunState RunState => _runSession.State;

        // Gets the number of visible objects with a tracked local anchor.
        public int RunnableMarkerCount
        {
            get
            {
                int count = 0;
                foreach (MarkerVisual marker in _markers.Values)
                {
                    if (marker.Instance != null && marker.Instance.activeInHierarchy && marker.Anchor != null &&
                        !marker.Anchor.pending && marker.Anchor.trackingState == TrackingState.Tracking)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        // Reports whether the foreground tracking session can start or resume all placed objects.
        public bool CanRun => !_warmup.Active && isActiveAndEnabled && IsForeground && _hasHeadTracking &&
            _startupPhase == StartupPhase.Ready && _markers.Count > 0 && RunnableMarkerCount == _markers.Count &&
            (!UsesRelativePlacement || (HasGhostCalibration &&
                (IsManual || (PerformanceSource != null && PerformanceSource.HasFreshInput))));

        private bool CanStartWarmup => !_warmup.Active && RunState == RunningSession.RunState.Ready &&
            isActiveAndEnabled && IsForeground && _hasHeadTracking && Phase == StartupPhase.Ready &&
            _warmupCoachPrefab != null && _xrOrigin != null && _xrOrigin.Camera != null &&
            _xrOrigin.Camera.isActiveAndEnabled;

        /// <summary>Starts an exercise sequence on a separate camera-relative avatar.</summary>
        public bool TryStartWarmup(RunnerWarmup.Step[] steps)
        {
            if (!CanStartWarmup || !RunnerWarmup.IsValidPlan(steps) ||
                !WarmupCoachView.TryCreate(_warmupCoachPrefab, _xrOrigin.Camera, out _warmupCoach))
            {
                return false;
            }
            if (_warmup.TryStart(_warmupCoach.Animator, steps))
            {
                _warmupCoach.ShowCountdown(_warmup.CountdownDisplay);
                return true;
            }

            StopWarmup();
            return false;
        }

        /// <summary>Skips the current exercise or removes the coach after the last step.</summary>
        public bool TryNextWarmupExercise()
        {
            bool skipped = _warmup.TryNext();
            if (!_warmup.Active)
            {
                StopWarmup();
            }
            return skipped;
        }

        /// <summary>Ends the warm-up and removes the camera-relative avatar.</summary>
        public bool StopWarmup(string status = "Stopped")
        {
            bool stopped = _warmup.Stop(status);
            _warmupCoach?.Dispose();
            _warmupCoach = null;
            return stopped;
        }

        /// <summary>Captures warm-up eligibility and playback for remote controls.</summary>
        public RunnerWarmup.State CaptureWarmup(bool beltStarting)
        {
            return _warmup.CaptureState(!beltStarting && CanStartWarmup);
        }

        // Gets the initial longitudinal separation used only by the relative distance display.
        public float RelativeStartOffset => !UsesCompanionPlacement && _ghostPresentation.Enabled ? _distanceBehindStartLine : 0;

        private bool HasGhostCalibration
        {
            get
            {
                if (_markers.Count != 1)
                {
                    return false;
                }
                foreach (MarkerVisual marker in _markers.Values)
                {
                    return marker.HasGhostCalibration;
                }
                return false;
            }
        }

        // Changes only presentation while Ready; treadmill-relative placement uses exactly one marker.
        public bool TryConfigureGhostPresentation(string mode, float sideOffset, float visibilityDistance)
        {
            if (IsManual || RunState != RunningSession.RunState.Ready ||
                (mode == "TreadmillRelative" && _markers.Count + _pendingMarkers.Count > 1) ||
                !_ghostPresentation.TryConfigure(mode, sideOffset, visibilityDistance))
            {
                return false;
            }
            ApplyGhostPresentation();
            return true;
        }

        // Locks the floor point below the head for the lane-relative reference.
        public bool TryCalibrateGhostPresentation()
        {
            return TryCalibrateGhostDirection(false);
        }

        internal bool TryCalibrateGhostDirection(bool reverseDirection)
        {
            if (!UsesRelativePlacement || RunState != RunningSession.RunState.Ready ||
                _markers.Count != 1 || RunnableMarkerCount != 1 || !_hasHeadTracking || !IsForeground ||
                _xrOrigin == null || _xrOrigin.Camera == null)
            {
                return false;
            }
            foreach (MarkerVisual marker in _markers.Values)
            {
                Transform frame = marker.Instance.transform;
                Transform head = _xrOrigin.Camera.transform;
                if (!TreadmillGhostPresentation.TryCalibrate(frame.InverseTransformPoint(head.position),
                    out Vector3 origin))
                {
                    return false;
                }
                marker.GhostOrigin = origin;
                marker.ReverseGhostDirection = reverseDirection;
                marker.HasGhostCalibration = true;
            }
            ApplyGhostPresentation();
            return true;
        }

        // Presents the track and ghost relative to the calibrated floor point.
        public void ApplyGhostPresentation()
        {
            if (UsesRelativePlacement && RunState == RunningSession.RunState.Paused)
            {
                _presentationStatus = IsSideBySide && (PerformanceSource == null || !PerformanceSource.HasFreshInput)
                    ? "BeltSignalUnavailable"
                    : "Paused";
                return;
            }
            _presentationVisible = false;
            _presentationStatus = UsesRelativePlacement ? "CalibrationRequired" : "Track";
            bool isWaitingToRun = RunState == RunningSession.RunState.Ready || RunState == RunningSession.RunState.Countdown;
            foreach (MarkerVisual marker in _markers.Values)
            {
                if (marker.Motion == null)
                {
                    continue;
                }
                if (!UsesRelativePlacement)
                {
                    if (marker.HasTreadmillTrackPose && marker.Track != null)
                    {
                        marker.Track.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                        marker.HasTreadmillTrackPose = false;
                    }
                    marker.Motion.ClearPresentationPose();
                    continue;
                }
                if (!marker.HasGhostCalibration || _markers.Count != 1)
                {
                    marker.Motion.SetPresentationPose(Pose.identity, false);
                    continue;
                }
                bool reverse = marker.ReverseGhostDirection;
                Pose trackPose = TreadmillGhostPresentation.EvaluateDirectedTrackPose(_spawnLane, marker.GhostOrigin,
                    _trackTravelledMetres, reverse, out double laneMetres);
                if (marker.Track != null)
                {
                    marker.Track.SetLocalPositionAndRotation(trackPose.position, trackPose.rotation);
                    marker.HasTreadmillTrackPose = true;
                }
                if (UsesCompanionPlacement)
                {
                    Pose companionPose;
                    if (IsManual)
                    {
                        _ghostPresentation.EvaluateDirectedGhost(_spawnLane, laneMetres, trackPose, 0,
                            true, false, reverse, out companionPose);
                    }
                    else
                    {
                        companionPose = _ghostPresentation.EvaluateDirectedCompanion(_spawnLane, marker.GhostOrigin, reverse);
                    }
                    marker.Motion.SetPresentationPose(companionPose, true);
                    _presentationVisible = true;
                    _presentationStatus = isWaitingToRun ? "Ready" : Mode.ToString();
                    continue;
                }

                bool isReplay = Mode == RunningSession.RunnerMode.AllTimePersonalBest;
                bool hasGap = isReplay || isWaitingToRun || IsManual ||
                    (PerformanceSource != null && PerformanceSource.TryGetGhostGap(out _));
                double gap = marker.Motion.TravelledMetres - _trackTravelledMetres;
                double displayGap = gap - _distanceBehindStartLine;
                bool inRange = _ghostPresentation.EvaluateDirectedGhost(_spawnLane, laneMetres, trackPose,
                    displayGap, isReplay && IsManual, isReplay, reverse, out Pose pose);
                _presentationVisible = hasGap && inRange;
                marker.Motion.SetPresentationPose(pose, _presentationVisible);
                _presentationStatus = !hasGap ? "MeasurementUnavailableResetRequired" : !inRange ? "OutsideVisibleRange" :
                    isWaitingToRun ? "Ready" : "Relative";
            }
        }

        // Reports presentation capability and calibration separately from run/measurement validity.
        public GhostPresentationState CaptureGhostPresentation()
        {
            bool reverseDirection = false;
            if (HasGhostCalibration)
            {
                foreach (MarkerVisual marker in _markers.Values)
                {
                    reverseDirection = marker.ReverseGhostDirection;
                }
            }

            return new GhostPresentationState
            {
                mode = IsManual ? "Manual" : UsesCompanionPlacement ? "SideBySide" : _ghostPresentation.Enabled ? "TreadmillRelative" : "Track",
                sideOffsetMetres = _ghostPresentation.SideOffsetMetres,
                visibilityDistanceMetres = _ghostPresentation.VisibilityDistanceMetres,
                calibrated = HasGhostCalibration,
                reverseDirection = reverseDirection,
                visible = _presentationVisible && _hasHeadTracking && RunnableMarkerCount == 1,
                status = _presentationStatus
            };
        }

        // Applies pace, lane and offset together; positional changes require a reset waiting session.
        public bool TryConfigureRunner(float paceMinutesPerKilometre, int lane, float distanceBehindStartLine)
        {
            if (!IsPositiveFinite(paceMinutesPerKilometre) || paceMinutesPerKilometre < 0.1f ||
                paceMinutesPerKilometre > 60f || lane < 1 || lane > RunningTrackGeometry.LaneCount ||
                float.IsNaN(distanceBehindStartLine) || float.IsInfinity(distanceBehindStartLine) ||
                distanceBehindStartLine < 0f || distanceBehindStartLine > 1000f)
            {
                return false;
            }

            bool changesPosition = lane != _spawnLane || distanceBehindStartLine != _distanceBehindStartLine;
            if (changesPosition && _runSession.State != RunningSession.RunState.Ready)
            {
                return false;
            }

            if (changesPosition)
            {
                foreach (MarkerVisual marker in _markers.Values)
                {
                    marker.Motion?.ConfigureStart(lane, distanceBehindStartLine);
                }
            }

            _paceMinutesPerKilometre = paceMinutesPerKilometre;
            _spawnLane = lane;
            _distanceBehindStartLine = distanceBehindStartLine;
            if (changesPosition && UsesRelativePlacement)
            {
                ApplyGhostPresentation();
            }
            return true;
        }

        // Updates existing waiting objects and future placements; running or paused positional changes fail.
        public bool TrySetSpawn(int lane, float distanceBehindStartLine)
        {
            return TryConfigureRunner(_paceMinutesPerKilometre, lane, distanceBehindStartLine);
        }

        // Starts from the configured position only after all current placements are tracked.
        public bool TryStartRun(float paceMinutesPerKilometre, int lane, float distanceBehindStartLine)
        {
            return TryScheduleRunStart(paceMinutesPerKilometre, lane, distanceBehindStartLine, null);
        }

        internal bool TryScheduleRunStart(float paceMinutesPerKilometre, int lane, float distanceBehindStartLine,
            Action<bool> startRequested)
        {
            if (_runSession.State != RunningSession.RunState.Ready || !CanRun)
            {
                return false;
            }

            if (!TryConfigureRunner(paceMinutesPerKilometre, lane, distanceBehindStartLine) ||
                !_runSession.TryBeginCountdown(CanRun))
            {
                return false;
            }

            _runStartRequested = startRequested;
            Camera camera = _xrOrigin != null ? _xrOrigin.Camera : null;
            if (camera != null)
            {
                _runCountdownView = new CountdownView(camera, camera.gameObject.layer, "Run countdown");
                _runCountdownView.Show(RunCountdownSeconds);
            }
            return true;
        }

        /// <summary>Gets the visible seconds remaining until the run start is requested.</summary>
        public int RunCountdownSeconds => Mathf.CeilToInt(_runSession.CountdownRemaining);

        internal bool CompleteRunStart()
        {
            _runCountdownView?.Dispose();
            _runCountdownView = null;
            return _runSession.TryCompleteCountdown(CanRun);
        }

        // Pauses movement while retaining positions, anchors and the selected settings.
        public bool TryPauseRun()
        {
            bool paused = _runSession.TryPause();
            CancelRunCountdown();
            return paused;
        }

        private void CancelRunCountdown()
        {
            _runCountdownView?.Dispose();
            _runCountdownView = null;
            Action<bool> request = _runStartRequested;
            _runStartRequested = null;
            request?.Invoke(false);
        }

        private void UpdateRunCountdown(float deltaTime)
        {
            if (RunState != RunningSession.RunState.Countdown)
            {
                return;
            }
            if (!CanRun)
            {
                TryPauseRun();
                return;
            }

            bool expired = _runSession.TickCountdown(deltaTime);
            _runCountdownView?.Show(RunCountdownSeconds);
            _runCountdownView?.UpdateLayout();
            if (expired)
            {
                Action<bool> request = _runStartRequested;
                _runStartRequested = null;
                if (request != null)
                {
                    request(true);
                }
                else if (!CompleteRunStart())
                {
                    TryPauseRun();
                }
            }
        }

        // Resumes a paused run only when all existing placements are tracked again.
        public bool TryResumeRun()
        {
            return _runSession.TryResume(CanRun);
        }

        // Stops movement and returns existing objects to their configured start positions.
        public void ResetRun()
        {
            _trackTravelledMetres = 0;
            StopWarmup();
            _runSession.Reset();
            PerformanceSource?.ResetSession();
            foreach (MarkerVisual marker in _markers.Values)
            {
                marker.Motion?.ResetToStart();
            }
            ApplyGhostPresentation();
            CancelRunCountdown();
        }

        // Updates detector settings; the caller must restart active tracking to apply them.
        public bool TryConfigureTracking(float markerLength, float timeoutSeconds, int maxTrackedMarkers)
        {
            if (!IsPositiveFinite(markerLength) || markerLength < 0.001f || markerLength > 10f ||
                !IsPositiveFinite(timeoutSeconds) || timeoutSeconds < 1f || timeoutSeconds > 300f ||
                maxTrackedMarkers < 1 || maxTrackedMarkers > 64)
            {
                return false;
            }

            _markerLength = markerLength;
            _startupTimeoutSeconds = timeoutSeconds;
            _maxTrackedMarkers = maxTrackedMarkers;
            return true;
        }

        private readonly RunnerWarmup _warmup = new RunnerWarmup();
        private WarmupCoachView _warmupCoach;
        private CountdownView _runCountdownView;
        private Action<bool> _runStartRequested;
        private readonly RunningSession _runSession = new RunningSession();
        private bool _hasHeadTracking;
        private readonly Dictionary<ulong, MarkerVisual> _markers = new Dictionary<ulong, MarkerVisual>();
        private readonly Dictionary<ulong, Pose> _pendingMarkers = new Dictionary<ulong, Pose>();
        private readonly List<ulong> _placedMarkerIds = new List<ulong>();
        private MagicLeapMarkerUnderstandingFeature _markerFeature;
        private MarkerDetector _markerDetector;
        private Transform _trackingOrigin;
        private string _deniedPermission;
        private int _startupAttempt;
        private float _startupStartedAt;
        private bool _controlsAnchorManager;
        private bool _isApplicationPaused;
        private bool _hasApplicationFocus = true;
        private bool _hasPermissionFailure;
        private float _inactiveStartedAt;
        private float _inactiveSeconds;

        private bool IsForeground => !_isApplicationPaused && _hasApplicationFocus;
        private float ActiveTime => Time.realtimeSinceStartup - _inactiveSeconds -
            (IsForeground ? 0f : Time.realtimeSinceStartup - _inactiveStartedAt);

        private sealed class MarkerVisual
        {
            public GameObject Instance;
            public ARAnchor Anchor;
            public float CreatedAt;
            public bool HasTracked;
            public RunningTrackMotion Motion;
            public Transform Track;
            public bool HasTreadmillTrackPose;
            public bool HasGhostCalibration;
            public bool ReverseGhostDirection;
            public Vector3 GhostOrigin;
        }

        private void OnEnable()
        {
            _isApplicationPaused = false;
            _hasApplicationFocus = Application.isFocused;
            _inactiveStartedAt = Time.realtimeSinceStartup;
            _inactiveSeconds = 0f;
            BeginTracking();
        }

        // Starts a fresh local session after an error; previous content and pending poses are cleared.
        public void RetryTracking()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            StopAllCoroutines();
            ReleaseResources();
            BeginTracking();
        }

        private void BeginTracking()
        {
            _startupAttempt++;
            _startupStartedAt = ActiveTime;
            _startupError = string.Empty;
            _deniedPermission = null;
            _hasPermissionFailure = false;
            if (Application.isEditor || Application.platform != RuntimePlatform.Android)
            {
                SetStartupPhase(StartupPhase.UnsupportedPlatform,
                    "Tracking requires an OpenXR build running on Magic Leap 2.");
                enabled = false;
                return;
            }

            StartCoroutine(RunGuarded(InitializeTracking()));
        }

        // Routes startup or recovery coroutine exceptions through the tracking failure handler.
        private IEnumerator RunGuarded(IEnumerator operation)
        {
            using (operation as IDisposable)
            {
                while (true)
                {
                    object current;
                    try
                    {
                        if (!operation.MoveNext())
                        {
                            yield break;
                        }

                        current = operation.Current;
                    }
                    catch (Exception exception)
                    {
                        StopWithError($"Tracking operation failed: {exception.Message}");
                        yield break;
                    }

                    if (current is IEnumerator nested)
                    {
                        yield return RunGuarded(nested);
                    }
                    else
                    {
                        yield return current;
                    }

                    if (_startupPhase == StartupPhase.Failed)
                    {
                        yield break;
                    }
                }
            }
        }

        // Waits for permissions, OpenXR, and tracking subsystems in startup order.
        private IEnumerator InitializeTracking()
        {
            SetStartupPhase(StartupPhase.WaitingForScene, "Waiting for scene components to activate.");
            yield return null;

            SetStartupPhase(StartupPhase.CheckingConfiguration, "Checking XR Origin, camera, and tracking references.");
            if (!ConfigureReferences(out string configurationError))
            {
                StopWithError(configurationError);
                yield break;
            }

            int attempt = _startupAttempt;
            string[] requiredPermissions =
                { Permissions.MarkerTracking, Permissions.SpatialMapping, Permissions.SpatialAnchors };
            foreach (string permission in requiredPermissions)
            {
                yield return RequestTrackingPermission(attempt, permission);
                if (_startupPhase == StartupPhase.Failed)
                {
                    yield break;
                }
            }

            yield return WaitForStartupStep(StartupPhase.WaitingForOpenXR,
                "Waiting for the OpenXR loader and display to start.", IsOpenXRRunning);
            if (_startupPhase == StartupPhase.Failed)
            {
                yield break;
            }

            SetStartupPhase(StartupPhase.CheckingFeatures, "Checking Magic Leap marker, anchor, and plane features.");
            if (!ValidateFeatures(out string featureError))
            {
                StopWithError(featureError);
                yield break;
            }

            _anchorManager.enabled = true;
            _planePlacement.StartSubsystem();
            yield return WaitForStartupStep(StartupPhase.StartingSubsystems,
                "Waiting for spatial anchor and plane subsystems to run.", AreTrackingSubsystemsRunning);
            if (_startupPhase == StartupPhase.Failed)
            {
                yield break;
            }

            SetStartupPhase(StartupPhase.StartingMarkerDetector, "Starting the AprilTag 36H11 detector.");
            var settings = new MarkerDetectorSettings
            {
                MarkerType = MarkerType.AprilTag,
                MarkerDetectorProfile = MarkerDetectorProfile.Default,
                AprilTagSettings = new AprilTagSettings
                {
                    AprilTagType = AprilTagType.Dictionary_36H11,
                    AprilTagLength = _markerLength,
                    EstimateAprilTagLength = false
                }
            };
            _markerDetector = _markerFeature.CreateMarkerDetector(settings);
            if (_markerDetector == null)
            {
                StopWithError("The AprilTag detector could not be created.");
                yield break;
            }

            yield return WaitForStartupStep(StartupPhase.StartingMarkerDetector,
                "Waiting for the AprilTag detector to report Ready.", IsMarkerDetectorReady);
            if (_startupPhase != StartupPhase.Failed)
            {
                while (!IsForeground)
                {
                    yield return null;
                }

                SetStartupPhase(StartupPhase.Ready, "Tracking is ready. Show a 36H11 marker and the surrounding surface.");
            }
        }

        private IEnumerator RequestTrackingPermission(int attempt, string permission)
        {
            SetStartupPhase(StartupPhase.RequestingPermissions, $"Waiting for permission: {permission}.");
            if (!Permissions.CheckPermission(permission))
            {
                // Ask separately so the active device dialog and missing permission are unambiguous.
                Permissions.RequestPermission(permission,
                    onPermissionDenied: denied => HandlePermissionDenied(attempt, denied),
                    onPermissionDeniedDontAskAgain: denied => HandlePermissionDenied(attempt, denied));
            }

            // A permission dialog waits for the user's decision rather than a subsystem timeout.
            while (!Permissions.CheckPermission(permission))
            {
                if (_deniedPermission != null)
                {
                    _hasPermissionFailure = true;
                    StopWithError($"Permission denied: {_deniedPermission}. Grant it in device settings and re-enable TagTracker.");
                    yield break;
                }

                yield return null;
            }

            Debug.Log($"[TrackingStartup] Permission granted: {permission}.", this);
        }

        private bool ConfigureReferences(out string error)
        {
            error = null;
            if (!ValidateRunningConfiguration(out error))
            {
                return false;
            }

            ARSession session = FindObjectOfType<ARSession>();
            if (session == null || !session.isActiveAndEnabled)
            {
                error = "An active AR Session is required in the tracking scene. Add it in the Editor and rebuild the app.";
                return false;
            }

            if (_xrOrigin == null)
            {
                _xrOrigin = FindObjectOfType<XROrigin>();
            }

            if (_xrOrigin == null || _xrOrigin.Camera == null ||
                _xrOrigin.CameraFloorOffsetObject == null || !IsPositiveFinite(_markerLength) ||
                !IsPositiveFinite(_startupTimeoutSeconds) || _maxTrackedMarkers < 1)
            {
                error = "Assign a complete XR Origin and finite positive marker length, timeout, and marker capacity.";
                return false;
            }

            if (!MarkerPlanePlacement.IsFinitePose(new Pose(_xrOrigin.transform.position, _xrOrigin.transform.rotation)) ||
                !IsPositiveFinite(_xrOrigin.transform.lossyScale.sqrMagnitude) ||
                (_xrOrigin.transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f ||
                Vector3.Dot(_xrOrigin.transform.up, Vector3.up) < 0.9999f ||
                (_xrOrigin.CameraFloorOffsetObject.transform.position - _xrOrigin.transform.position).sqrMagnitude > 0.000001f ||
                Quaternion.Angle(_xrOrigin.CameraFloorOffsetObject.transform.rotation, _xrOrigin.transform.rotation) > 0.01f)
            {
                error = "Tracking requires unit XR Origin scale, an upright origin, and zero camera floor offset (including Camera Y Offset).";
                return false;
            }

            if (!Mathf.Approximately(_xrOrigin.CameraYOffset, 0f))
            {
                error = "Set XR Origin Camera Y Offset to zero so marker and AR Foundation plane spaces agree.";
                return false;
            }

            _trackingOrigin = _xrOrigin.CameraFloorOffsetObject.transform;
            if (_anchorManager == null)
            {
                _anchorManager = _xrOrigin.GetComponent<ARAnchorManager>();
            }

            if (_anchorManager == null || _anchorManager.gameObject != _xrOrigin.gameObject ||
                !_anchorManager.gameObject.activeInHierarchy)
            {
                error = "An AR Anchor Manager on an active XR Origin is required.";
                return false;
            }

            if (_planePlacement == null)
            {
                _planePlacement = _xrOrigin.GetComponent<MarkerPlanePlacement>();
            }

            if (_planePlacement == null || _planePlacement.gameObject != _xrOrigin.gameObject ||
                !_planePlacement.isActiveAndEnabled)
            {
                error = "An active Marker Plane Placement component is required on the XR Origin.";
                return false;
            }

            if (!_planePlacement.Initialize(_xrOrigin, out error))
            {
                return false;
            }

            _controlsAnchorManager = true;
            _anchorManager.enabled = false;
            return true;
        }

        private bool ValidateRunningConfiguration(out string error)
        {
            error = null;
            if (_spawnLane < 1 || _spawnLane > RunningTrackGeometry.LaneCount ||
                !IsPositiveFinite(_paceMinutesPerKilometre) ||
                float.IsNaN(_distanceBehindStartLine) || float.IsInfinity(_distanceBehindStartLine) ||
                _distanceBehindStartLine < 0f)
            {
                error = "Select a lane from 1 to 8, a finite positive pace, and a finite nonnegative start distance.";
                return false;
            }

            if (_markerPrefab != null && (_markerPrefab.GetComponentInChildren<ARAnchor>(true) != null ||
                _markerPrefab.GetComponentInChildren<Rigidbody>(true) != null ||
                !_markerPrefab.activeSelf))
            {
                error = "Use an active visual marker prefab without ARAnchor or Rigidbody components; the tracker owns movement and anchors.";
                return false;
            }

            return true;
        }

        private static bool IsPositiveFinite(float value)
        {
            return value > 0f && !float.IsInfinity(value) && !float.IsNaN(value);
        }

        private bool ValidateFeatures(out string error)
        {
            error = null;
            var settings = OpenXRSettings.Instance;
            if (settings == null)
            {
                error = "OpenXR settings are unavailable.";
                return false;
            }

            _markerFeature = settings.GetFeature<MagicLeapMarkerUnderstandingFeature>();
            var anchorFeature = settings.GetFeature<MagicLeapSpatialAnchorsFeature>();
            var planeFeature = settings.GetFeature<MagicLeapPlanesFeature>();
            if (_markerFeature == null || !_markerFeature.enabled ||
                !OpenXRRuntime.IsExtensionEnabled("XR_ML_marker_understanding"))
            {
                error = "Enable Magic Leap 2 Marker Understanding in Android OpenXR settings.";
            }
            else if (anchorFeature == null || !anchorFeature.enabled ||
                !OpenXRRuntime.IsExtensionEnabled("XR_ML_spatial_anchors") ||
                !OpenXRRuntime.IsExtensionEnabled("XR_EXT_future"))
            {
                error = "Enable Magic Leap 2 Spatial Anchor Subsystem and its required extensions.";
            }
            else if (planeFeature == null || !planeFeature.enabled ||
                !OpenXRRuntime.IsExtensionEnabled("XR_EXT_plane_detection"))
            {
                error = "Enable Magic Leap 2 Planes Subsystem in Android OpenXR settings.";
            }

            return error == null;
        }

        // Waits for a readiness condition using the active-time startup timeout.
        private IEnumerator WaitForStartupStep(StartupPhase phase, string description, Func<bool> isReady)
        {
            SetStartupPhase(phase, description);
            float deadline = ActiveTime + _startupTimeoutSeconds;
            while (!IsForeground || !isReady())
            {
                if (_startupPhase == StartupPhase.Failed)
                {
                    yield break;
                }

                if (IsForeground && ActiveTime >= deadline)
                {
                    StopWithError($"Timeout after {_startupTimeoutSeconds:F1} seconds. {description}");
                    yield break;
                }

                yield return null;
            }
        }

        private static bool HasTrackingPermissions()
        {
            return Permissions.CheckPermission(Permissions.MarkerTracking) &&
                Permissions.CheckPermission(Permissions.SpatialAnchors) &&
                Permissions.CheckPermission(Permissions.SpatialMapping);
        }

        private static bool IsOpenXRRunning()
        {
            var manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
            if (manager == null || !manager.isInitializationComplete || !(manager.activeLoader is OpenXRLoader))
            {
                return false;
            }

            var display = manager.activeLoader.GetLoadedSubsystem<XRDisplaySubsystem>();
            return display != null && display.running;
        }

        private bool AreTrackingSubsystemsRunning()
        {
            return _anchorManager != null && _anchorManager.isActiveAndEnabled &&
                _anchorManager.subsystem != null && _anchorManager.subsystem.running &&
                _planePlacement != null && _planePlacement.IsSubsystemRunning;
        }

        private bool IsMarkerDetectorReady()
        {
            if (!HasDetector())
            {
                StopWithError("The OpenXR session removed the marker detector during startup.");
                return false;
            }

            _markerFeature.UpdateMarkerDetectors();
            if (_markerDetector.Status == MarkerDetectorStatus.Error)
            {
                StopWithError("The AprilTag detector reported an error during startup.");
                return false;
            }

            return _markerDetector.Status == MarkerDetectorStatus.Ready;
        }

        private void SetStartupPhase(StartupPhase phase, string description)
        {
            if (_startupPhase == phase && _startupStatus == description)
            {
                return;
            }

            _startupPhase = phase;
            _startupStatus = description;
            Debug.Log($"[TrackingStartup] {phase} (+{ActiveTime - _startupStartedAt:F1}s active): {description}", this);
        }

        private void Update()
        {
            if (_startupPhase != StartupPhase.Ready || !IsForeground)
            {
                return;
            }

            try
            {
                UpdateTracking();
            }
            catch (Exception exception)
            {
                StopWithError($"Tracking update failed: {exception.Message}");
            }
        }

        // Keeps detected marker poses available until a matching plane can be used.
        private void UpdateTracking()
        {
            if (!HasTrackingPermissions())
            {
                _hasPermissionFailure = true;
                StopWithError("Tracking permission was revoked. Grant it in device settings, then retry tracking.");
                return;
            }

            if (!IsOpenXRRunning() || !AreTrackingSubsystemsRunning() || !HasDetector())
            {
                BeginRecovery();
                return;
            }

            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid || !head.TryGetFeatureValue(CommonUsages.isTracked, out bool isHeadTracked) ||
                !isHeadTracked || !head.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState headState) ||
                (headState & (InputTrackingState.Position | InputTrackingState.Rotation)) !=
                (InputTrackingState.Position | InputTrackingState.Rotation))
            {
                _hasHeadTracking = false;
                HideVisuals();
                return;
            }

            _hasHeadTracking = true;
            if (!UpdateVisuals())
            {
                return;
            }

            _markerFeature.UpdateMarkerDetectors();
            if (_markerDetector.Status == MarkerDetectorStatus.Error)
            {
                StopWithError("The AprilTag detector reported an error.");
                return;
            }

            if (_markerDetector.Status == MarkerDetectorStatus.Ready)
            {
                var data = _markerDetector.Data;
                for (int i = 0; data != null && i < data.Count; i++)
                {
                    MarkerData observation = data[i];
                    if (observation.MarkerNumber.HasValue && observation.MarkerPose.HasValue)
                    {
                        RememberMarker(observation.MarkerNumber.Value, observation.MarkerPose.Value);
                    }
                }
            }

            _placedMarkerIds.Clear();
            bool hasPendingBounds = false;
            Bounds pendingBounds = default;
            foreach (Pose pendingPose in _pendingMarkers.Values)
            {
                if (!hasPendingBounds)
                {
                    pendingBounds = new Bounds(pendingPose.position, Vector3.zero);
                    hasPendingBounds = true;
                }
                else
                {
                    pendingBounds.Encapsulate(pendingPose.position);
                }
            }

            if (hasPendingBounds)
            {
                _planePlacement.UpdateMarkerQuery(pendingBounds);
            }

            // New anchors join only while waiting, so a running session keeps a stable set of objects.
            if (_runSession.State != RunningSession.RunState.Ready)
            {
                return;
            }

            foreach (KeyValuePair<ulong, Pose> marker in _pendingMarkers)
            {
                ulong markerId = marker.Key;
                Pose sessionPose = marker.Value;
                var markerWorldPose = new Pose(_trackingOrigin.TransformPoint(sessionPose.position),
                    _trackingOrigin.rotation * sessionPose.rotation);
                if (!_planePlacement.TryGetPlacementPose(markerWorldPose, out Pose placementPose))
                {
                    continue;
                }

                _markers.Add(markerId, CreateVisual(markerId, placementPose));
                _placedMarkerIds.Add(markerId);
                LogTrackingEvent($"Creating a session anchor for AprilTag 36H11 {markerId} at {placementPose.position:F3} on the detected surface.");
                // Bound native creation and content setup to one placement per frame.
                break;
            }

            foreach (ulong markerId in _placedMarkerIds)
            {
                _pendingMarkers.Remove(markerId);
            }
        }

        // Gates content visibility on anchor and headset tracking.
        private bool UpdateVisuals()
        {
            double beltSpeed = 0;
            bool hasBeltSpeed = UsesRelativePlacement && !IsManual && PerformanceSource != null &&
                PerformanceSource.TryGetBeltSpeedMetresPerSecond(out beltSpeed);
            if (IsSideBySide && !IsManual && !hasBeltSpeed)
            {
                TryPauseRun();
            }

            float effectivePace = IsSideBySide ? 0 : ActivePaceMinutesPerKilometre;

            // Check every anchor before advancing any runner; tracking loss pauses the entire session.
            foreach (MarkerVisual marker in _markers.Values)
            {
                if (marker.Anchor == null || marker.Anchor.pending || marker.Anchor.trackingState != TrackingState.Tracking)
                {
                    TryPauseRun();
                    break;
                }
            }

            bool recorded = Mode == RunningSession.RunnerMode.AllTimePersonalBest && PerformanceSource != null;
            bool canAdvance = _runSession.State == RunningSession.RunState.Running;
            AdvanceTrack(Time.unscaledDeltaTime, beltSpeed);
            double recordedTravel = 0;
            if (recorded && canAdvance)
            {
                foreach (MarkerVisual marker in _markers.Values)
                {
                    if (marker.Motion != null && marker.Instance != null && marker.Instance.activeSelf)
                    {
                        recordedTravel = PerformanceSource.AdvanceGhost(Time.unscaledDeltaTime,
                            marker.Motion.TravelledMetres, _paceMinutesPerKilometre);
                        break;
                    }
                }
            }

            foreach (MarkerVisual marker in _markers.Values)
            {
                if (marker.Instance == null || marker.Anchor == null)
                {
                    StopWithError("A placed object or its anchor was removed. Retry tracking to begin a fresh session.");
                    return false;
                }

                bool isTracked = !marker.Anchor.pending && marker.Anchor.trackingState == TrackingState.Tracking;
                marker.HasTracked |= isTracked;
                if (!marker.HasTracked && ActiveTime - marker.CreatedAt >= _startupTimeoutSeconds)
                {
                    StopWithError("A spatial anchor did not become tracked before the timeout. Retry after XR tracking recovers.");
                    return false;
                }

                bool wasVisible = marker.Instance.activeSelf;
                if (wasVisible != isTracked)
                {
                    marker.Instance.SetActive(isTracked);
                }

                if (isTracked && wasVisible)
                {
                    if (UsesCompanionPlacement && marker.Motion != null)
                    {
                        AdvanceCompanion(marker.Motion, Time.unscaledDeltaTime, beltSpeed);
                    }
                    else if (recorded && canAdvance)
                    {
                        marker.Motion?.AdvanceRecorded(Time.unscaledDeltaTime, recordedTravel,
                            PerformanceSource.GhostSpeedMetresPerSecond);
                    }
                    else
                    {
                        _runSession.Advance(marker.Motion, Time.unscaledDeltaTime, effectivePace);
                        if (marker.Motion != null)
                        {
                            PerformanceSource?.ObserveGhostDistance(marker.Motion.TravelledMetres);
                        }
                    }
                }
            }

            UpdateRunCountdown(Time.unscaledDeltaTime);
            UpdateWarmup(Time.unscaledDeltaTime);
            ApplyGhostPresentation();
            return true;
        }

        private double TrackSpeed(double beltSpeed)
        {
            return IsManual ? _manualTrackSpeedKmh / 3.6 : beltSpeed;
        }

        private void AdvanceTrack(float delta, double beltSpeed)
        {
            if (UsesRelativePlacement && RunState == RunningSession.RunState.Running)
            {
                _trackTravelledMetres += delta * TrackSpeed(beltSpeed);
            }
        }

        private void AdvanceCompanion(RunningTrackMotion motion, float delta, double beltSpeed)
        {
            double speed = RunState == RunningSession.RunState.Running ? TrackSpeed(beltSpeed) : 0;
            // Use the same speed for track travel and the companion gait.
            motion.AdvanceRecorded(delta, speed * delta, speed);
            PerformanceSource?.ObserveGhostDistance(motion.TravelledMetres);
        }

        private void UpdateWarmup(float deltaTime)
        {
            if (!_warmup.Active)
            {
                return;
            }

            if (_xrOrigin == null || !_warmupCoach.IsAvailable(_xrOrigin.Camera) || !_warmupCoach.UpdateLayout())
            {
                StopWarmup("ViewUnavailable");
                return;
            }

            _warmup.Tick(deltaTime);
            if (!_warmup.Active)
            {
                StopWarmup();
            }
            else
            {
                _warmupCoach.ShowCountdown(_warmup.CountdownDisplay);
            }
        }

        // Retains a marker pose while asynchronous plane detection catches up.
        private void RememberMarker(ulong markerId, Pose observedPose)
        {
            if (_markers.ContainsKey(markerId) || !MarkerPlanePlacement.IsFinitePose(observedPose))
            {
                return;
            }

            // Plane extraction is asynchronous; keep observations while looking at the surface.
            if (!_pendingMarkers.ContainsKey(markerId))
            {
                if (_markers.Count + _pendingMarkers.Count >= (UsesRelativePlacement ? 1 : _maxTrackedMarkers))
                {
                    return;
                }

                LogTrackingEvent($"AprilTag {markerId}: detected. Waiting for a matching tracked surface.");
            }

            observedPose.rotation = Quaternion.Normalize(observedPose.rotation);
            _pendingMarkers[markerId] = observedPose;
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        [System.Diagnostics.Conditional("DEVELOPMENT_BUILD")]
        private void LogTrackingEvent(string message)
        {
            Debug.Log(message, this);
        }

        private void OnApplicationPause(bool isPaused)
        {
            SetApplicationState(isPaused, _hasApplicationFocus);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            SetApplicationState(_isApplicationPaused, hasFocus);
        }

        private void SetApplicationState(bool isPaused, bool hasFocus)
        {
            bool wasForeground = IsForeground;
            _isApplicationPaused = isPaused;
            _hasApplicationFocus = hasFocus;
            if (wasForeground && !IsForeground)
            {
                _inactiveStartedAt = Time.realtimeSinceStartup;
                HideVisuals();
                if (_startupPhase == StartupPhase.Ready)
                {
                    SetStartupPhase(StartupPhase.Suspended, "Tracking suspended while the app is inactive.");
                }
            }
            else if (!wasForeground && IsForeground)
            {
                _inactiveSeconds += Time.realtimeSinceStartup - _inactiveStartedAt;
                if (!isActiveAndEnabled)
                {
                    return;
                }

                if (_startupPhase == StartupPhase.Suspended)
                {
                    BeginRecovery();
                }
                else if (_startupPhase == StartupPhase.Failed && _hasPermissionFailure && HasTrackingPermissions())
                {
                    RetryTracking();
                }
            }
        }

        private void HideVisuals()
        {
            StopWarmup("TrackingLost");
            _hasHeadTracking = false;
            TryPauseRun();
            foreach (MarkerVisual marker in _markers.Values)
            {
                if (marker.Instance != null && marker.Instance.activeSelf)
                {
                    marker.Instance.SetActive(false);
                }
            }
        }

        private void BeginRecovery()
        {
            if (_startupPhase == StartupPhase.Recovering)
            {
                return;
            }

            HideVisuals();
            SetStartupPhase(StartupPhase.Recovering, "Waiting for the OpenXR session and tracking subsystems to recover.");
            StartCoroutine(RunGuarded(RecoverTracking()));
        }

        // Waits for tracking readiness after the application returns.
        private IEnumerator RecoverTracking()
        {
            float deadline = ActiveTime + _startupTimeoutSeconds;
            while (true)
            {
                if (!IsForeground)
                {
                    yield return null;
                    continue;
                }

                if (!HasTrackingPermissions())
                {
                    _hasPermissionFailure = true;
                    StopWithError("Tracking permission was revoked. Grant it in device settings, then retry tracking.");
                    yield break;
                }

                // The SDK removes detectors when their native XR session ends. Never reuse old session poses.
                if (!HasDetector())
                {
                    ReleaseResources();
                    BeginTracking();
                    yield break;
                }

                if (IsOpenXRRunning() && AreTrackingSubsystemsRunning() && IsMarkerDetectorReady())
                {
                    SetStartupPhase(StartupPhase.Ready, "Tracking recovered; existing local anchors are retained.");
                    yield break;
                }

                if (_startupPhase == StartupPhase.Failed)
                {
                    yield break;
                }

                if (ActiveTime >= deadline)
                {
                    StopWithError("XR tracking did not recover before the timeout. Retry tracking when the device is ready.");
                    yield break;
                }

                yield return null;
            }
        }

        private MarkerVisual CreateVisual(ulong markerId, Pose placementPose)
        {
            // The plane projection is already in world space; AR Foundation owns later corrections.
            var anchorObject = new GameObject($"AprilTag Anchor {markerId}");
            anchorObject.transform.SetPositionAndRotation(
                placementPose.position, placementPose.rotation);
            try
            {
                return PopulateVisual(anchorObject, markerId);
            }
            catch
            {
                anchorObject.SetActive(false);
                DestroyOwnedObject(anchorObject);
                throw;
            }
        }

        // Builds the runner and track under a shared root beneath the anchor.
        private MarkerVisual PopulateVisual(GameObject anchorObject, ulong markerId)
        {
            if (!ValidateRunningConfiguration(out string configurationError))
            {
                throw new InvalidOperationException(configurationError);
            }

            ARAnchor anchor = anchorObject.AddComponent<ARAnchor>();
            var content = new GameObject($"AprilTag Content {markerId}");
            content.transform.SetParent(anchor.transform, false);
            content.SetActive(false);
            GameObject instance;
            if (_markerPrefab != null)
            {
                instance = Instantiate(_markerPrefab, content.transform, false);
            }
            else
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                instance.transform.SetParent(content.transform, false);
                instance.transform.localScale = Vector3.one * _markerLength;
                Collider collider = instance.GetComponent<Collider>();
                if (Application.isPlaying)
                {
                    Destroy(collider);
                }
                else
                {
                    DestroyImmediate(collider);
                }
            }

            instance.name = $"AprilTag 36H11 {markerId}";
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            PlaceVisualOnPlane(instance.transform, anchor.transform);
            var motion = new RunningTrackMotion(instance.transform, _spawnLane,
                _distanceBehindStartLine);
            // Ground the marker object before adding track bounds, which extend across the oval.
            RunningTrackVisual track = RunningTrackVisual.Create(content.transform);
            return new MarkerVisual
            {
                Instance = content, Anchor = anchor, CreatedAt = ActiveTime, Motion = motion, Track = track.transform
            };
        }

        private static void PlaceVisualOnPlane(Transform visual, Transform anchor)
        {
            Animator animator = visual.GetComponentInChildren<Animator>(true);
            if (animator != null && animator.isHuman && animator.avatar != null && animator.avatar.isValid)
            {
                Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                if (leftFoot != null && rightFoot != null)
                {
                    // Skinned bounds include animation margins, not the actual standing soles.
                    Vector3 leftSole = leftFoot.position - animator.transform.up *
                        (animator.leftFeetBottomHeight * animator.transform.lossyScale.y);
                    Vector3 rightSole = rightFoot.position - animator.transform.up *
                        (animator.rightFeetBottomHeight * animator.transform.lossyScale.y);
                    float soleHeight = Mathf.Min(anchor.InverseTransformPoint(leftSole).y,
                        anchor.InverseTransformPoint(rightSole).y);
                    visual.localPosition -= Vector3.up * soleHeight;
                    RunnerFootPlacement footPlacement = animator.GetComponent<RunnerFootPlacement>();
                    if (footPlacement != null)
                    {
                        footPlacement.Configure(anchor);
                    }

                    return;
                }
            }

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            float nearestSurface = float.PositiveInfinity;
            foreach (Renderer renderer in renderers)
            {
                // Use local bounds, since world-aligned bounds introduce gaps on tilted markers.
                Bounds bounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    var localCorner = new Vector3(
                        (corner & 1) == 0 ? bounds.min.x : bounds.max.x,
                        (corner & 2) == 0 ? bounds.min.y : bounds.max.y,
                        (corner & 4) == 0 ? bounds.min.z : bounds.max.z);
                    Vector3 markerCorner = anchor.InverseTransformPoint(
                        renderer.transform.TransformPoint(localCorner));
                    nearestSurface = Mathf.Min(nearestSurface, markerCorner.y);
                }
            }

            if (renderers.Length > 0)
            {
                // Plane-space +Y is the surface normal; put the object's bottom on its XZ plane.
                visual.localPosition = new Vector3(0f, -nearestSurface, 0f);
            }
        }

        private static void DestroyOwnedObject(GameObject instance)
        {
            // Unity does not guarantee OnDestroy for components that have never been active.
            foreach (RunningTrackVisual track in instance.GetComponentsInChildren<RunningTrackVisual>(true))
            {
                track.ReleaseGeneratedResources();
            }

            if (Application.isPlaying)
            {
                Destroy(instance);
            }
            else
            {
                DestroyImmediate(instance);
            }
        }

        private bool HasDetector()
        {
            if (_markerFeature == null || _markerDetector == null)
            {
                return false;
            }

            for (int i = 0; i < _markerFeature.MarkerDetectors.Count; i++)
            {
                if (_markerFeature.MarkerDetectors[i] == _markerDetector)
                {
                    return true;
                }
            }

            return false;
        }

        private void HandlePermissionDenied(int attempt, string permission)
        {
            if (attempt == _startupAttempt && isActiveAndEnabled &&
                _startupPhase == StartupPhase.RequestingPermissions)
            {
                _deniedPermission = permission;
            }
        }

        private void StopWithError(string message)
        {
            StartupPhase failedPhase = _startupPhase;
            _startupError = $"{failedPhase}: {message}";
            _startupPhase = StartupPhase.Failed;
            _startupStatus = _startupError;
            Debug.LogError($"[TrackingStartup] Failed during {_startupError}", this);
            ReleaseResources();
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            ReleaseResources();
            if (_startupPhase != StartupPhase.Failed && _startupPhase != StartupPhase.UnsupportedPlatform)
            {
                SetStartupPhase(StartupPhase.Stopped, "Tracking stopped and session resources released.");
            }
        }

        // Tears down the detector, scanning, anchors, and owned content.
        private void ReleaseResources()
        {
            _trackTravelledMetres = 0;
            StopWarmup("TrackingLost");
            _runSession.Reset();
            CancelRunCountdown();
            PerformanceSource?.ResetSession();
            _hasHeadTracking = false;
            _startupAttempt++;
            foreach (MarkerVisual marker in _markers.Values)
            {
                GameObject ownedObject = marker.Anchor != null ? marker.Anchor.gameObject : marker.Instance;
                if (ownedObject == null)
                {
                    continue;
                }

                try
                {
                    ownedObject.SetActive(false);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[TrackingStartup] Could not deactivate an ended anchor: {exception.Message}", this);
                }
                finally
                {
                    DestroyOwnedObject(ownedObject);
                }
            }

            _markers.Clear();
            _pendingMarkers.Clear();
            _placedMarkerIds.Clear();
            try
            {
                if (HasDetector())
                {
                    _markerFeature.DestroyMarkerDetector(_markerDetector);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[TrackingStartup] Could not release the ended marker session: {exception.Message}", this);
            }
            finally
            {
                _markerDetector = null;
                _markerFeature = null;
            }

            if (_planePlacement != null)
            {
                _planePlacement.StopScanning();
            }

            if (_controlsAnchorManager && _anchorManager != null)
            {
                _anchorManager.enabled = false;
            }

            _controlsAnchorManager = false;
        }
    }
}
