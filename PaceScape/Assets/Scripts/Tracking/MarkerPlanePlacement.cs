using MagicLeap.OpenXR.Features.Planes;
using MagicLeap.OpenXR.Subsystems;
using Unity.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace PaceScape.Tracking
{
    /// <summary>
    /// Queries nearby Magic Leap planes and projects marker poses onto matching boundaries.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MarkerPlanePlacement : MonoBehaviour
    {
        [SerializeField] private ARPlaneManager _planeManager;
        [SerializeField]
        [Tooltip("Use the marker's horizontal position and place on a classified floor, ignoring marker height and tilt.")]
        private bool _placeOnFloor = true;
        [SerializeField, Min(0.1f)] private float _queryPadding = 1f;
        [SerializeField, Min(0.001f)]
        [Tooltip("Maximum distance between the marker and a candidate surface, in meters.")]
        private float _maxMarkerPlaneDistance = 0.15f;
        [SerializeField, Range(0f, 1f)]
        [Tooltip("Minimum absolute dot product between marker and surface normals.")]
        private float _minimumNormalAlignment = 0.9f;

        /// <summary>Gets whether placement uses classified floors rather than strict surface matching.</summary>
        public bool PlaceOnFloor => _placeOnFloor;

        /// <summary>Gets the plane query padding in metres.</summary>
        public float QueryPadding => _queryPadding;

        /// <summary>Gets the strict-mode maximum marker-to-surface distance in metres.</summary>
        public float MaxMarkerPlaneDistance => _maxMarkerPlaneDistance;

        /// <summary>Gets the strict-mode minimum absolute normal alignment.</summary>
        public float MinimumNormalAlignment => _minimumNormalAlignment;

        /// <summary>Updates plane settings; the caller must restart active tracking to rebuild the query.</summary>
        public bool TryConfigure(bool placeOnFloor, float queryPadding, float maxMarkerPlaneDistance,
            float minimumNormalAlignment)
        {
            if (!IsFinite(queryPadding) || queryPadding < 0.1f || queryPadding > 100f ||
                !IsFinite(maxMarkerPlaneDistance) || maxMarkerPlaneDistance < 0.001f || maxMarkerPlaneDistance > 10f ||
                !IsFinite(minimumNormalAlignment) || minimumNormalAlignment < 0f || minimumNormalAlignment > 1f)
            {
                return false;
            }

            _placeOnFloor = placeOnFloor;
            _queryPadding = queryPadding;
            _maxMarkerPlaneDistance = maxMarkerPlaneDistance;
            _minimumNormalAlignment = minimumNormalAlignment;
            return true;
        }

        private XROrigin _xrOrigin;
        private Bounds _queryBounds;
        private float _nextDiagnosticTime;
        private Transform _trackingOrigin;
        private bool _isInitialized;

        private enum ProjectionResult
        {
            Accepted,
            NotFloor,
            TooFar,
            Misaligned,
            OutsideBoundary
        }

        /// <summary>Reports whether the Magic Leap plane subsystem is running.</summary>
        public bool IsSubsystemRunning => _isInitialized && isActiveAndEnabled && _planeManager != null &&
            _planeManager.isActiveAndEnabled && _planeManager.subsystem is MLXrPlaneSubsystem &&
            _planeManager.subsystem.running;

        /// <summary>
        /// Prepares the manager without scanning before spatial mapping permission is granted.
        /// </summary>
        public bool Initialize(XROrigin xrOrigin, out string error)
        {
            error = null;
            if (xrOrigin == null || xrOrigin.Camera == null || xrOrigin.CameraFloorOffsetObject == null)
            {
                error = "A complete XR Origin is required before initializing plane placement.";
                return false;
            }

            if (!IsFinite(_queryPadding) || _queryPadding <= 0f ||
                !IsFinite(_maxMarkerPlaneDistance) || _maxMarkerPlaneDistance <= 0f ||
                !IsFinite(_minimumNormalAlignment) || _minimumNormalAlignment < 0f || _minimumNormalAlignment > 1f)
            {
                error = "Plane placement settings must be finite and inside their Inspector ranges.";
                return false;
            }

            if (_planeManager == null || _planeManager.gameObject != xrOrigin.gameObject)
            {
                _planeManager = xrOrigin.GetComponent<ARPlaneManager>();
            }

            if (_planeManager == null)
            {
                error = "AR Plane Manager is missing. Assign it to Marker Plane Placement or add it to the XR Origin.";
                return false;
            }

            if (_planeManager.planePrefab == null)
            {
                error = "AR Plane Manager has no Plane Prefab. Assign DetectedPlane.prefab and rebuild the app.";
                return false;
            }

            if (!_planeManager.planePrefab.activeSelf || _planeManager.planePrefab.GetComponent<ARPlane>() == null)
            {
                error = "The Plane Prefab must be active and have an ARPlane component on its root.";
                return false;
            }

            _xrOrigin = xrOrigin;
            _trackingOrigin = xrOrigin.CameraFloorOffsetObject.transform;
            _isInitialized = true;
            _planeManager.enabled = false;
            _planeManager.requestedDetectionMode = _placeOnFloor
                ? PlaneDetectionMode.Horizontal
                : PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            _nextDiagnosticTime = 0f;
            return true;
        }

        /// <summary>Starts plane updates after permissions and OpenXR features have been checked.</summary>
        public void StartSubsystem()
        {
            if (!_isInitialized || _planeManager == null || _trackingOrigin == null || _xrOrigin.Camera == null)
            {
                return;
            }

            Vector3 cameraPosition = _trackingOrigin.InverseTransformPoint(_xrOrigin.Camera.transform.position);
            SetQuery(new Bounds(cameraPosition, Vector3.zero));
            _planeManager.enabled = true;
        }

        /// <summary>
        /// Finds the nearest tracked surface containing the projected marker center.
        /// </summary>
        public bool TryGetPlacementPose(Pose markerWorldPose, out Pose placementPose)
        {
            placementPose = default;
            if (!_isInitialized || !IsFinitePose(markerWorldPose) || !IsSubsystemRunning ||
                _trackingOrigin == null || _xrOrigin.Camera == null)
            {
                return false;
            }

            Vector3 sessionPosition = _trackingOrigin.InverseTransformPoint(markerWorldPose.position);

            float bestDistance = float.PositiveInfinity;
            int planeCount = 0;
            int trackedCount = 0;
            int tooFarCount = 0;
            int misalignedCount = 0;
            int outsideBoundaryCount = 0;
            int notFloorCount = 0;
            ARPlane nearestPlane = null;
            float nearestPlaneDistance = float.PositiveInfinity;
            bool shouldLogDiagnostics = (Application.isEditor || Debug.isDebugBuild) &&
                Time.realtimeSinceStartup >= _nextDiagnosticTime;
            foreach (ARPlane plane in _planeManager.trackables)
            {
                planeCount++;
                if (plane.subsumedBy != null || plane.trackingState != TrackingState.Tracking)
                {
                    continue;
                }

                trackedCount++;
                if (shouldLogDiagnostics)
                {
                    float surfaceDistance = Mathf.Abs(Vector3.Dot(markerWorldPose.position - plane.transform.position,
                        plane.transform.up));
                    if (surfaceDistance < nearestPlaneDistance)
                    {
                        nearestPlaneDistance = surfaceDistance;
                        nearestPlane = plane;
                    }
                }

                var planePose = new Pose(plane.transform.position, plane.transform.rotation);
                Pose candidate;
                ProjectionResult result = _placeOnFloor
                    ? TryProjectFloor(markerWorldPose, planePose, plane.classification, plane.alignment,
                        plane.boundary, out candidate)
                    : TryProjectMarker(markerWorldPose, planePose, plane.boundary,
                        _maxMarkerPlaneDistance, _minimumNormalAlignment, out candidate);
                if (result != ProjectionResult.Accepted)
                {
                    if (result == ProjectionResult.NotFloor)
                    {
                        notFloorCount++;
                    }
                    else if (result == ProjectionResult.TooFar)
                    {
                        tooFarCount++;
                    }
                    else if (result == ProjectionResult.Misaligned)
                    {
                        misalignedCount++;
                    }
                    else
                    {
                        outsideBoundaryCount++;
                    }

                    continue;
                }

                float distance = (candidate.position - markerWorldPose.position).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    Vector3 normal = candidate.rotation * Vector3.up;
                    if (!_placeOnFloor && Vector3.Dot(normal, _xrOrigin.Camera.transform.position - candidate.position) < 0f)
                    {
                        candidate.rotation = Quaternion.LookRotation(candidate.rotation * Vector3.forward, -normal);
                    }

                    placementPose = candidate;
                }
            }

            bool hasPlacement = !float.IsPositiveInfinity(bestDistance);
            // Throttle diagnostics while waiting so device logs stay useful over long scans.
            if (!hasPlacement && shouldLogDiagnostics)
            {
                _nextDiagnosticTime = Time.realtimeSinceStartup + 5f;
                Debug.Log($"[PlanePlacement] No matching plane for marker at {markerWorldPose.position:F3}. " +
                    $"FloorMode={_placeOnFloor}, planes={planeCount}, tracked={trackedCount}, notFloor={notFloorCount}, tooFar={tooFarCount}, " +
                    $"misaligned={misalignedCount}, outsideOrMissingBoundary={outsideBoundaryCount}. " +
                    $"Query center={_queryBounds.center:F3}, extents={MLXrPlaneSubsystem.Query.BoundsExtents:F3}; " +
                    $"maxDistance={_maxMarkerPlaneDistance:F3}m, minimumAlignment={_minimumNormalAlignment:F3}.", this);
                if (nearestPlane != null)
                {
                    var nearestPose = new Pose(nearestPlane.transform.position, nearestPlane.transform.rotation);
                    Vector3 projected = ProjectMarkerPosition(markerWorldPose.position, nearestPose, _placeOnFloor);
                    Vector3 localMarker = nearestPlane.transform.InverseTransformPoint(projected);
                    float alignment = Mathf.Abs(Vector3.Dot(markerWorldPose.rotation * Vector3.forward,
                        nearestPlane.transform.up));
                    bool isInside = ContainsPoint(nearestPlane.boundary, new Vector2(localMarker.x, localMarker.z));
                    Debug.Log($"[PlanePlacement] Nearest plane={nearestPlane.trackableId}, " +
                        $"classification={nearestPlane.classification}, alignment={nearestPlane.alignment}, " +
                        $"distance={nearestPlaneDistance:F3}m, normalAlignment={alignment:F3}, " +
                        $"insideBoundary={isInside}, boundaryPoints={(nearestPlane.boundary.IsCreated ? nearestPlane.boundary.Length : 0)}, " +
                        $"planePosition={nearestPlane.transform.position:F3}, planeNormal={nearestPlane.transform.up:F3}, " +
                        $"markerSessionPosition={sessionPosition:F3}, cameraPosition={_xrOrigin.Camera.transform.position:F3}, " +
                        $"cameraOffsetPosition={_trackingOrigin.position:F3}, originPosition={_xrOrigin.transform.position:F3}.", this);
                }

                LogPlaneCandidates(markerWorldPose);
            }

            return hasPlacement;
        }

        private void SetQuery(Bounds bounds)
        {
            if (!IsFinitePose(new Pose(bounds.center, Quaternion.identity)) || !IsFinite(bounds.extents.sqrMagnitude))
            {
                throw new System.ArgumentException("Plane query bounds must be finite.", nameof(bounds));
            }

            _queryBounds = bounds;
            // The native query volume is in OpenXR tracking space, like marker observations.
            MLXrPlaneSubsystem.Query = new MLXrPlaneSubsystem.PlanesQuery
            {
                Flags = _planeManager.requestedDetectionMode.ToMLXrQueryFlags() |
                    MLXrPlaneSubsystem.MLPlanesQueryFlags.SemanticAll,
                BoundsCenter = bounds.center,
                BoundsRotation = Quaternion.identity,
                BoundsExtents = GetQueryExtents(bounds),
                MaxResults = 64,
                MinPlaneArea = 0.04f
            };
        }

        private Vector3 GetQueryExtents(Bounds bounds)
        {
            Vector3 extents = bounds.extents + Vector3.one * _queryPadding;
            // A large floor can have its detected center far from the marker. Match the SDK example in all axes.
            return _placeOnFloor ? Vector3.Max(extents, Vector3.one * 20f) : extents;
        }

        private void LogPlaneCandidates(Pose marker)
        {
            int count = 0;
            foreach (ARPlane plane in _planeManager.trackables)
            {
                if (plane.subsumedBy != null || plane.trackingState != TrackingState.Tracking)
                {
                    continue;
                }

                var pose = new Pose(plane.transform.position, plane.transform.rotation);
                Vector3 projected = ProjectMarkerPosition(marker.position, pose, _placeOnFloor);
                Vector3 local = plane.transform.InverseTransformPoint(projected);
                bool isInside = ContainsPoint(plane.boundary, new Vector2(local.x, local.z));
                Debug.Log($"[PlanePlacement] Candidate={plane.trackableId}, classification={plane.classification}, " +
                    $"alignment={plane.alignment}, insideBoundary={isInside}, position={pose.position:F3}.", this);
                if (++count >= 8)
                {
                    break;
                }
            }
        }

        /// <summary>Queries the current pending marker positions in tracking space, without accumulating old poses.</summary>
        public void UpdateMarkerQuery(Bounds pendingMarkerBounds)
        {
            if (_isInitialized)
            {
                SetQuery(pendingMarkerBounds);
            }
        }

        private static ProjectionResult TryProjectFloor(Pose marker, Pose plane,
            PlaneClassification classification, PlaneAlignment alignment, NativeArray<Vector2> boundary,
            out Pose placement)
        {
            placement = default;
            if (!IsFinitePose(marker) || !IsFinitePose(plane))
            {
                return ProjectionResult.Misaligned;
            }

            marker.rotation = Quaternion.Normalize(marker.rotation);
            plane.rotation = Quaternion.Normalize(plane.rotation);
            if (classification != PlaneClassification.Floor ||
                (alignment != PlaneAlignment.HorizontalUp && alignment != PlaneAlignment.HorizontalDown))
            {
                return ProjectionResult.NotFloor;
            }

            Vector3 normal = plane.rotation * Vector3.up;
            if (Mathf.Abs(normal.y) < 0.0001f)
            {
                return ProjectionResult.Misaligned;
            }

            // Keep marker X/Z; the detected floor supplies height, including on a sloped plane.
            if (normal.y < 0f)
            {
                normal = -normal;
            }

            Vector3 projected = ProjectMarkerPosition(marker.position, plane, true);
            return TryCreatePlacement(marker, plane, boundary, projected, normal, out placement);
        }

        private static ProjectionResult TryProjectMarker(Pose marker, Pose plane, NativeArray<Vector2> boundary,
            float maxDistance, float minimumAlignment, out Pose placement)
        {
            placement = default;
            if (!IsFinitePose(marker) || !IsFinitePose(plane) || !IsFinite(maxDistance) || maxDistance <= 0f ||
                !IsFinite(minimumAlignment) || minimumAlignment < 0f || minimumAlignment > 1f)
            {
                return ProjectionResult.Misaligned;
            }

            marker.rotation = Quaternion.Normalize(marker.rotation);
            plane.rotation = Quaternion.Normalize(plane.rotation);
            Vector3 normal = plane.rotation * Vector3.up;
            Vector3 markerNormal = marker.rotation * Vector3.forward;
            float signedDistance = Vector3.Dot(marker.position - plane.position, normal);
            if (Mathf.Abs(signedDistance) > maxDistance)
            {
                return ProjectionResult.TooFar;
            }

            if (Mathf.Abs(Vector3.Dot(markerNormal, normal)) < minimumAlignment)
            {
                return ProjectionResult.Misaligned;
            }

            Vector3 projected = ProjectMarkerPosition(marker.position, plane, false);
            return TryCreatePlacement(marker, plane, boundary, projected, normal, out placement);
        }

        private static Vector3 ProjectMarkerPosition(Vector3 markerPosition, Pose plane, bool vertically)
        {
            Vector3 normal = plane.rotation * Vector3.up;
            float signedDistance = Vector3.Dot(markerPosition - plane.position, normal);
            if (vertically && Mathf.Abs(normal.y) >= 0.0001f)
            {
                markerPosition.y -= signedDistance / normal.y;
                return markerPosition;
            }

            return markerPosition - normal * signedDistance;
        }

        private static ProjectionResult TryCreatePlacement(Pose marker, Pose plane, NativeArray<Vector2> boundary,
            Vector3 projected, Vector3 normal, out Pose placement)
        {
            placement = default;
            if (!IsFinite(projected.x) || !IsFinite(projected.y) || !IsFinite(projected.z))
            {
                return ProjectionResult.OutsideBoundary;
            }

            Vector3 local = Quaternion.Inverse(plane.rotation) * (projected - plane.position);
            if (!ContainsPoint(boundary, new Vector2(local.x, local.z)))
            {
                return ProjectionResult.OutsideBoundary;
            }

            // Marker +Y supplies the heading; model +Y stands on the detected plane.
            Vector3 forward = Vector3.ProjectOnPlane(marker.rotation * Vector3.up, normal).normalized;
            if (forward.sqrMagnitude < 0.000001f)
            {
                forward = plane.rotation * Vector3.forward;
            }

            placement = new Pose(projected, Quaternion.LookRotation(forward, normal));
            return ProjectionResult.Accepted;
        }

        private static bool ContainsPoint(NativeArray<Vector2> boundary, Vector2 point)
        {
            if (!boundary.IsCreated || boundary.Length < 3)
            {
                return false;
            }

            // Validate the whole polygon before an edge hit can return early.
            for (int i = 0; i < boundary.Length; i++)
            {
                if (!IsFinite(boundary[i].x) || !IsFinite(boundary[i].y))
                {
                    return false;
                }
            }

            bool isInside = false;
            for (int i = 0, j = boundary.Length - 1; i < boundary.Length; j = i++)
            {
                Vector2 a = boundary[j];
                Vector2 b = boundary[i];
                Vector2 edge = b - a;
                float fraction = edge.sqrMagnitude > 0f
                    ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0f;
                if ((point - (a + edge * fraction)).sqrMagnitude <= 0.000001f)
                {
                    return true;
                }

                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                {
                    isInside = !isInside;
                }
            }

            return isInside;
        }

        /// <summary>
        /// Stops scanning when the tracker is disabled or loses its permissions.
        /// </summary>
        public void StopScanning()
        {
            if (_isInitialized && _planeManager != null)
            {
                _planeManager.enabled = false;
            }

            _isInitialized = false;
            _trackingOrigin = null;
            _xrOrigin = null;
        }

        /// <summary>Rejects invalid device poses before they can reach a native query or an anchor.</summary>
        internal static bool IsFinitePose(Pose pose)
        {
            Quaternion rotation = pose.rotation;
            float rotationLength = rotation.x * rotation.x + rotation.y * rotation.y +
                rotation.z * rotation.z + rotation.w * rotation.w;
            return IsFinite(pose.position.sqrMagnitude) && IsFinite(rotationLength) && rotationLength > 0.000001f;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }

        private void OnDisable()
        {
            StopScanning();
        }
    }
}
