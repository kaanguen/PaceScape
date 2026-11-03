using System;
using System.Collections;
using System.Collections.Generic;
using MagicLeap.Android;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;
using MagicLeap.OpenXR.Features.Planes;
using MagicLeap.OpenXR.Features.SpatialAnchors;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace PaceScape.Tracking
{
    /// <summary>
    /// Anchors content on the detected surface beneath each AprilTag 36H11 for the current session.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TagTracker : MonoBehaviour
    {
        /// <summary>
        /// Current phase of the tracking startup sequence.
        /// </summary>
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
        [SerializeField] private ARAnchorManager _anchorManager;
        [SerializeField] private MarkerPlanePlacement _planePlacement;
        [SerializeField, Min(0.001f)]
        [Tooltip("Printed tag side length in meters, including its black border but excluding the white margin. Restart tracking after changing this value.")]
        private float _markerLength = 0.1f;
        [SerializeField, Min(1f)] private float _startupTimeoutSeconds = 15f;
        [SerializeField, Min(1)] private int _maxTrackedMarkers = 64;

        [Header("Startup diagnostics (runtime)")]
        [SerializeField] private StartupPhase _startupPhase;
        [SerializeField] private string _startupStatus;
        [SerializeField] private string _startupError;

        /// <summary>Gets the current startup phase.</summary>
        public StartupPhase Phase => _startupPhase;

        /// <summary>Gets the current startup progress or failure description.</summary>
        public string StartupStatus => _startupStatus;

        /// <summary>Gets the last failure, retained until the next startup attempt.</summary>
        public string StartupError => _startupError;

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
        }

        private void OnEnable()
        {
            _isApplicationPaused = false;
            _hasApplicationFocus = Application.isFocused;
            _inactiveStartedAt = Time.realtimeSinceStartup;
            _inactiveSeconds = 0f;
            BeginTracking();
        }

        /// <summary>Starts a fresh local session after an error; previous content and pending poses are cleared.</summary>
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
            }

            foreach (ulong markerId in _placedMarkerIds)
            {
                _pendingMarkers.Remove(markerId);
            }
        }

        private bool UpdateVisuals()
        {
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

                if (marker.Instance.activeSelf != isTracked)
                {
                    marker.Instance.SetActive(isTracked);
                }
            }

            return true;
        }

        private void RememberMarker(ulong markerId, Pose observedPose)
        {
            if (_markers.ContainsKey(markerId) || !MarkerPlanePlacement.IsFinitePose(observedPose))
            {
                return;
            }

            // Plane extraction is asynchronous; keep observations while looking at the surface.
            if (!_pendingMarkers.ContainsKey(markerId))
            {
                if (_markers.Count + _pendingMarkers.Count >= _maxTrackedMarkers)
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

        private MarkerVisual PopulateVisual(GameObject anchorObject, ulong markerId)
        {
            ARAnchor anchor = anchorObject.AddComponent<ARAnchor>();
            GameObject instance;
            if (_markerPrefab != null)
            {
                instance = Instantiate(_markerPrefab, anchor.transform, false);
            }
            else
            {
                instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
                instance.transform.SetParent(anchor.transform, false);
                instance.transform.localScale = Vector3.one * _markerLength;
                Destroy(instance.GetComponent<Collider>());
            }

            instance.name = $"AprilTag 36H11 {markerId}";
            instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            PlaceVisualOnPlane(instance.transform, anchor.transform);
            instance.SetActive(false);
            return new MarkerVisual { Instance = instance, Anchor = anchor, CreatedAt = ActiveTime };
        }

        private static void PlaceVisualOnPlane(Transform visual, Transform anchor)
        {
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

        private void ReleaseResources()
        {
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
