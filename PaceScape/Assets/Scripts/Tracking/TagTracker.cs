using System.Collections;
using System.Collections.Generic;
using MagicLeap.Android;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;
using MagicLeap.OpenXR.Features.SpatialAnchors;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;

namespace PaceScape.Tracking
{
    /// <summary>
    /// Anchors content at each AprilTag 36H11's first valid pose for the current session.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TagTracker : MonoBehaviour
    {
        [SerializeField] private XROrigin _xrOrigin;
        [SerializeField] private GameObject _markerPrefab;
        [SerializeField] private ARAnchorManager _anchorManager;
        [SerializeField, Min(0.001f)]
        [Tooltip("Printed tag side length in meters, including its black border but excluding the white margin. Restart tracking after changing this value.")]
        private float _markerLength = 0.1f;

        private readonly Dictionary<ulong, MarkerVisual> _markers = new Dictionary<ulong, MarkerVisual>();
        private MagicLeapMarkerUnderstandingFeature _markerFeature;
        private MarkerDetector _markerDetector;
        private Transform _trackingOrigin;
        private bool _isPermissionDenied;

        private sealed class MarkerVisual
        {
            public GameObject Instance;
            public ARAnchor Anchor;
        }

        private void OnEnable()
        {
            if (Application.isEditor || Application.platform != RuntimePlatform.Android)
            {
                Debug.LogWarning("AprilTag tracking requires an OpenXR build running on Magic Leap 2.", this);
                enabled = false;
                return;
            }

            StartCoroutine(InitializeTracking());
        }

        private IEnumerator InitializeTracking()
        {
            if (_xrOrigin == null)
            {
                _xrOrigin = FindObjectOfType<XROrigin>();
            }

            if (_xrOrigin == null || _xrOrigin.Camera == null ||
                _xrOrigin.CameraFloorOffsetObject == null || _markerLength <= 0f)
            {
                StopWithError("Assign an XR Origin with a Camera, a Camera Floor Offset Object, and a positive marker length.");
                yield break;
            }

            _trackingOrigin = _xrOrigin.CameraFloorOffsetObject.transform;
            if (_anchorManager == null)
            {
                _anchorManager = _xrOrigin.GetComponent<ARAnchorManager>();
            }

            if (_anchorManager == null || !_anchorManager.isActiveAndEnabled)
            {
                StopWithError("Add an enabled AR Anchor Manager to the XR Origin.");
                yield break;
            }

            _isPermissionDenied = false;
            Permissions.RequestPermissions(new[] { Permissions.MarkerTracking, Permissions.SpatialAnchors },
                onPermissionDenied: HandlePermissionDenied,
                onPermissionDeniedDontAskAgain: HandlePermissionDenied);

            while (!Permissions.CheckPermission(Permissions.MarkerTracking) ||
                   !Permissions.CheckPermission(Permissions.SpatialAnchors))
            {
                if (_isPermissionDenied)
                {
                    StopWithError("Marker tracking or spatial anchor permission was denied. Enable both in device settings and re-enable this component.");
                    yield break;
                }

                yield return null;
            }

            // Automatic XR initialization must finish before native detector creation.
            float deadline = Time.realtimeSinceStartup + 10f;
            while (XRGeneralSettings.Instance == null || XRGeneralSettings.Instance.Manager == null ||
                   !XRGeneralSettings.Instance.Manager.isInitializationComplete)
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    StopWithError("OpenXR did not initialize. Check Android XR Plug-in Management settings.");
                    yield break;
                }

                yield return null;
            }

            _markerFeature = OpenXRSettings.Instance != null
                ? OpenXRSettings.Instance.GetFeature<MagicLeapMarkerUnderstandingFeature>()
                : null;
            if (_markerFeature == null || !_markerFeature.enabled ||
                !OpenXRRuntime.IsExtensionEnabled("XR_ML_marker_understanding"))
            {
                StopWithError("Enable Magic Leap 2 Marker Understanding in the Android OpenXR settings.");
                yield break;
            }

            var anchorFeature = OpenXRSettings.Instance.GetFeature<MagicLeapSpatialAnchorsFeature>();
            if (anchorFeature == null || !anchorFeature.enabled ||
                !OpenXRRuntime.IsExtensionEnabled("XR_ML_spatial_anchors"))
            {
                StopWithError("Enable Magic Leap 2 Spatial Anchor Subsystem in the Android OpenXR settings.");
                yield break;
            }

            deadline = Time.realtimeSinceStartup + 10f;
            while (_anchorManager.subsystem == null || !_anchorManager.subsystem.running)
            {
                if (Time.realtimeSinceStartup >= deadline)
                {
                    StopWithError("The spatial anchor subsystem did not start.");
                    yield break;
                }

                yield return null;
            }

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
            }
        }

        private void Update()
        {
            foreach (MarkerVisual marker in _markers.Values)
            {
                if (marker.Instance != null)
                {
                    bool isTracked = marker.Anchor != null && !marker.Anchor.pending &&
                        marker.Anchor.trackingState == TrackingState.Tracking &&
                        _anchorManager.isActiveAndEnabled && _anchorManager.subsystem != null &&
                        _anchorManager.subsystem.running;
                    marker.Instance.SetActive(isTracked);
                }
            }

            if (_markerDetector == null)
            {
                return;
            }

            if (!Permissions.CheckPermission(Permissions.MarkerTracking) ||
                !Permissions.CheckPermission(Permissions.SpatialAnchors))
            {
                StopWithError("Marker tracking or spatial anchor permission is no longer granted.");
                return;
            }

            // The feature owns detector lifetime and also removes detectors when its XR session ends.
            if (!HasDetector())
            {
                StopWithError("The OpenXR marker session ended. Re-enable this component after XR restarts.");
                return;
            }

            _markerFeature.UpdateMarkerDetectors();
            if (_markerDetector.Status == MarkerDetectorStatus.Error)
            {
                StopWithError("The AprilTag detector reported an error.");
                return;
            }

            if (_markerDetector.Status != MarkerDetectorStatus.Ready)
            {
                return;
            }

            var data = _markerDetector.Data;
            for (int i = 0; i < data.Count; i++)
            {
                MarkerData observation = data[i];
                if (!observation.MarkerNumber.HasValue || !observation.MarkerPose.HasValue)
                {
                    continue;
                }

                ulong markerId = observation.MarkerNumber.Value;
                if (_markers.ContainsKey(markerId))
                {
                    continue;
                }

                _markers.Add(markerId, CreateVisual(markerId, observation.MarkerPose.Value));
                Debug.Log($"Creating a session anchor for AprilTag 36H11 {markerId}.", this);
            }
        }

        private MarkerVisual CreateVisual(ulong markerId, Pose markerPose)
        {
            // Convert once; AR Foundation owns subsequent anchor poses and tracking corrections.
            var anchorObject = new GameObject($"AprilTag Anchor {markerId}");
            anchorObject.transform.SetPositionAndRotation(
                _trackingOrigin.TransformPoint(markerPose.position),
                _trackingOrigin.rotation * markerPose.rotation);
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
            // Unity's handedness conversion may reverse the marker normal; use the visible face.
            float normalDirection = Vector3.Dot(anchor.transform.forward,
                _xrOrigin.Camera.transform.position - anchor.transform.position) >= 0f ? 1f : -1f;
            PlaceVisualOnMarker(instance.transform, anchor.transform, normalDirection);
            instance.SetActive(false);
            return new MarkerVisual { Instance = instance, Anchor = anchor };
        }

        private static void PlaceVisualOnMarker(Transform visual, Transform anchor, float normalDirection)
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
                    nearestSurface = Mathf.Min(nearestSurface, markerCorner.z * normalDirection);
                }
            }

            if (renderers.Length > 0)
            {
                // The marker's XY plane is the contact surface; move only along its normal.
                visual.localPosition = new Vector3(0f, 0f, -nearestSurface * normalDirection);
            }
        }

        private bool HasDetector()
        {
            for (int i = 0; i < _markerFeature.MarkerDetectors.Count; i++)
            {
                if (_markerFeature.MarkerDetectors[i] == _markerDetector)
                {
                    return true;
                }
            }

            return false;
        }

        private void HandlePermissionDenied(string permission)
        {
            _isPermissionDenied = true;
        }

        private void StopWithError(string message)
        {
            Debug.LogError(message, this);
            enabled = false;
        }

        private void OnDisable()
        {
            StopAllCoroutines();
            if (_markerFeature != null && _markerDetector != null && HasDetector())
            {
                _markerFeature.DestroyMarkerDetector(_markerDetector);
            }

            _markerDetector = null;
            _markerFeature = null;
            foreach (MarkerVisual marker in _markers.Values)
            {
                if (marker.Anchor != null)
                {
                    Destroy(marker.Anchor.gameObject);
                }
            }

            _markers.Clear();
        }
    }
}
