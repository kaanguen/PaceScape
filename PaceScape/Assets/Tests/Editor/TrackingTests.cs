// Checks plane placement and tracking configuration with synthetic inputs.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.XR.ARSubsystems;

namespace PaceScape.Tests
{
    // Regression checks for placement geometry and device-independent lifecycle behavior.
    public sealed class TrackingTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
        private Type _placementType;
        private Type _trackerType;
        private NativeArray<Vector2> _boundary;
        private GameObject _testObject;

        [SetUp]
        public void SetUp()
        {
            // Tests stay outside the predefined runtime assembly without restructuring existing scene scripts.
            _placementType = Type.GetType("PaceScape.Tracking.MarkerPlanePlacement, Assembly-CSharp", true);
            _trackerType = Type.GetType("PaceScape.Tracking.TagTracker, Assembly-CSharp", true);
            _boundary = new NativeArray<Vector2>(new[]
            {
                new Vector2(-1f, -1f), new Vector2(1f, -1f),
                new Vector2(1f, 1f), new Vector2(-1f, 1f)
            }, Allocator.Temp);
        }

        [TearDown]
        public void TearDown()
        {
            if (_testObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_testObject);
            }

            _boundary.Dispose();
        }

        [TestCase(0f)]
        [TestCase(10f)]
        [TestCase(180f)]
        public void FloorUsesDetectedHeightAndPreservesHorizontalPosition(float tilt)
        {
            Pose marker = new Pose(new Vector3(0.015f, -0.520f, 0.634f), Quaternion.identity);
            Pose plane = new Pose(new Vector3(0f, -1.162f, 0f), Quaternion.Euler(tilt, 0f, 0f));
            object[] arguments = { marker, plane, PlaneClassification.Floor,
                PlaneAlignment.HorizontalUp, _boundary, default(Pose) };
            Assert.That(ProjectFloor(arguments), Is.EqualTo("Accepted"));
            Pose placed = (Pose)arguments[5];
            Assert.That(placed.position.x, Is.EqualTo(marker.position.x).Within(0.0001f));
            Assert.That(placed.position.z, Is.EqualTo(marker.position.z).Within(0.0001f));
            Assert.That(Mathf.Abs(Vector3.Dot(placed.position - plane.position, plane.rotation * Vector3.up)),
                Is.LessThan(0.0001f));
            Assert.That(Vector3.Dot(placed.rotation * Vector3.up, Vector3.up), Is.GreaterThan(0.9f));
        }

        [TestCase(PlaneClassification.Wall)]
        [TestCase(PlaneClassification.Table)]
        [TestCase(PlaneClassification.Ceiling)]
        [TestCase(PlaneClassification.None)]
        public void FloorRejectsOtherSurfaceClassifications(PlaneClassification classification)
        {
            object[] arguments = { Pose.identity, Pose.identity, classification,
                PlaneAlignment.HorizontalUp, _boundary, default(Pose) };
            Assert.That(ProjectFloor(arguments), Is.EqualTo("NotFloor"));
        }

        [Test]
        public void FloorRejectsVerticalPlanesEvenWhenMisclassified()
        {
            object[] arguments = { Pose.identity, Pose.identity, PlaneClassification.Floor,
                PlaneAlignment.Vertical, _boundary, default(Pose) };
            Assert.That(ProjectFloor(arguments), Is.EqualTo("NotFloor"));
        }

        [TestCase(0f, "Accepted")]
        [TestCase(1f, "Accepted")]
        [TestCase(1.01f, "OutsideBoundary")]
        public void FloorRespectsItsBoundary(float x, string expected)
        {
            object[] arguments = { new Pose(new Vector3(x, 0.64f, 0f), Quaternion.identity),
                Pose.identity, PlaneClassification.Floor, PlaneAlignment.HorizontalUp, _boundary, default(Pose) };
            Assert.That(ProjectFloor(arguments), Is.EqualTo(expected));
        }

        [Test]
        public void FloorRequiresBoundaryData()
        {
            object[] arguments = { Pose.identity, Pose.identity, PlaneClassification.Floor,
                PlaneAlignment.HorizontalUp, default(NativeArray<Vector2>), default(Pose) };
            Assert.That(ProjectFloor(arguments), Is.EqualTo("OutsideBoundary"));
        }

        [TestCase(-1f)]
        [TestCase(0f)]
        public void FloorRejectsNonFiniteBoundaryData(float markerX)
        {
            _boundary[1] = new Vector2(float.NaN, 0f);
            object[] arguments = { new Pose(new Vector3(markerX, 0f, 0f), Quaternion.identity), Pose.identity, PlaneClassification.Floor,
                PlaneAlignment.HorizontalUp, _boundary, default(Pose) };
            Assert.That(ProjectFloor(arguments), Is.EqualTo("OutsideBoundary"));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void InvalidDevicePositionsAreRejected(float x)
        {
            Pose pose = new Pose(new Vector3(x, 0f, 0f), Quaternion.identity);
            Assert.That((bool)_placementType.GetMethod("IsFinitePose", PrivateStatic).Invoke(null,
                new object[] { pose }), Is.False);
        }

        [Test]
        public void ZeroQuaternionIsRejected()
        {
            Assert.That((bool)_placementType.GetMethod("IsFinitePose", PrivateStatic).Invoke(null,
                new object[] { default(Pose) }), Is.False);
        }

        [TestCase(0.05f, "Accepted")]
        [TestCase(0.3f, "TooFar")]
        public void StrictSurfaceModeRetainsItsDistanceLimit(float height, string expected)
        {
            Pose marker = new Pose(new Vector3(0f, height, 0f),
                Quaternion.LookRotation(Vector3.up, Vector3.forward));
            object[] arguments = { marker, Pose.identity, _boundary, 0.15f, 0.9f, default(Pose) };
            Assert.That(_placementType.GetMethod("TryProjectMarker", PrivateStatic).Invoke(null, arguments).ToString(),
                Is.EqualTo(expected));
        }

        [Test]
        public void PlacementBeforeInitializationFailsWithoutThrowing()
        {
            Component placement = CreateInactiveComponent(_placementType);
            object[] arguments = { Pose.identity, default(Pose) };
            Assert.That((bool)_placementType.GetMethod("TryGetPlacementPose").Invoke(placement, arguments), Is.False);
            _placementType.GetMethod("StartSubsystem").Invoke(placement, null);
            _placementType.GetMethod("StopScanning").Invoke(placement, null);
            _placementType.GetMethod("StopScanning").Invoke(placement, null);
        }

        [Test]
        public void InitializeRejectsMissingOriginWithoutTakingOwnership()
        {
            Component placement = CreateInactiveComponent(_placementType);
            object[] arguments = { null, null };
            Assert.That((bool)_placementType.GetMethod("Initialize").Invoke(placement, arguments), Is.False);
            Assert.That(arguments[1], Is.Not.Null);
            Assert.That(_placementType.GetField("_isInitialized", PrivateInstance).GetValue(placement), Is.False);
        }

        [Test]
        public void ReleaseClearsPendingMarkersAndCanRunTwice()
        {
            Component tracker = CreateInactiveComponent(_trackerType);
            var pending = (Dictionary<ulong, Pose>)_trackerType.GetField("_pendingMarkers", PrivateInstance).GetValue(tracker);
            pending.Add(39, Pose.identity);
            MethodInfo release = _trackerType.GetMethod("ReleaseResources", PrivateInstance);
            release.Invoke(tracker, null);
            release.Invoke(tracker, null);
            Assert.That(pending, Is.Empty);
            Assert.That(_trackerType.GetField("_markerDetector", PrivateInstance).GetValue(tracker), Is.Null);
        }

        [Test]
        public void PermissionCallbackWhileDisabledIsIgnored()
        {
            Component tracker = CreateInactiveComponent(_trackerType);
            _trackerType.GetField("_startupAttempt", PrivateInstance).SetValue(tracker, 5);
            _trackerType.GetMethod("HandlePermissionDenied", PrivateInstance).Invoke(tracker,
                new object[] { 4, "test.permission" });
            Assert.That(_trackerType.GetField("_deniedPermission", PrivateInstance).GetValue(tracker), Is.Null);
        }

        [Test]
        public void PauseSuspendsReadyTrackingAndExcludesInactiveTime()
        {
            Component tracker = CreateInactiveComponent(_trackerType);
            FieldInfo phase = _trackerType.GetField("_startupPhase", PrivateInstance);
            phase.SetValue(tracker, Enum.Parse(phase.FieldType, "Ready"));
            _trackerType.GetField("_hasApplicationFocus", PrivateInstance).SetValue(tracker, true);
            _trackerType.GetMethod("SetApplicationState", PrivateInstance).Invoke(tracker, new object[] { true, true });
            Assert.That(phase.GetValue(tracker).ToString(), Is.EqualTo("Suspended"));
            _trackerType.GetField("_inactiveStartedAt", PrivateInstance).SetValue(tracker, Time.realtimeSinceStartup - 60f);
            float activeTime = (float)_trackerType.GetProperty("ActiveTime", PrivateInstance).GetValue(tracker);
            Assert.That(activeTime, Is.EqualTo(Time.realtimeSinceStartup - 60f).Within(0.1f));
        }

        [Test]
        public void FailureRetainsErrorAndClearsPendingData()
        {
            Component tracker = CreateInactiveComponent(_trackerType);
            var pending = (Dictionary<ulong, Pose>)_trackerType.GetField("_pendingMarkers", PrivateInstance).GetValue(tracker);
            pending.Add(39, Pose.identity);
            LogAssert.Expect(LogType.Error, "[TrackingStartup] Failed during Stopped: Test failure");
            _trackerType.GetMethod("StopWithError", PrivateInstance).Invoke(tracker, new object[] { "Test failure" });
            Assert.That(pending, Is.Empty);
            Assert.That(_trackerType.GetProperty("StartupError").GetValue(tracker), Is.EqualTo("Stopped: Test failure"));
        }

        private string ProjectFloor(object[] arguments)
        {
            return _placementType.GetMethod("TryProjectFloor", PrivateStatic).Invoke(null, arguments).ToString();
        }

        [Test]
        public void FloorQueryIncludesSurfaceCentersBeyondMarkerPadding()
        {
            Component placement = CreateInactiveComponent(_placementType);
            var bounds = new Bounds(new Vector3(-1f, -1f, -0.4f), Vector3.zero);
            Vector3 extents = (Vector3)_placementType.GetMethod("GetQueryExtents", PrivateInstance)
                .Invoke(placement, new object[] { bounds });
            var query = new Bounds(bounds.center, extents * 2f);
            Assert.That(query.Contains(bounds.center + new Vector3(3f, -1.7f, 3f)), Is.True);
            Assert.That(query.Contains(bounds.center + new Vector3(-3f, -1.7f, -3f)), Is.True);
        }

        [Test]
        public void BroadFloorQueryDoesNotShrinkMultipleMarkerBounds()
        {
            Component placement = CreateInactiveComponent(_placementType);
            var bounds = new Bounds(Vector3.zero, new Vector3(60f, 2f, 4f));
            Vector3 extents = (Vector3)_placementType.GetMethod("GetQueryExtents", PrivateInstance)
                .Invoke(placement, new object[] { bounds });
            Assert.That(extents.x, Is.GreaterThanOrEqualTo(bounds.extents.x));
            Assert.That(extents.y, Is.GreaterThanOrEqualTo(bounds.extents.y));
            Assert.That(extents.z, Is.GreaterThanOrEqualTo(bounds.extents.z));
        }

        [Test]
        public void StrictSurfaceQueryRetainsItsConfiguredPadding()
        {
            Component placement = CreateInactiveComponent(_placementType);
            _placementType.GetField("_placeOnFloor", PrivateInstance).SetValue(placement, false);
            Vector3 extents = (Vector3)_placementType.GetMethod("GetQueryExtents", PrivateInstance)
                .Invoke(placement, new object[] { new Bounds(Vector3.zero, Vector3.zero) });
            Assert.That(extents, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void GuardedOperationConvertsAnExceptionIntoRetainedFailure()
        {
            Component tracker = CreateInactiveComponent(_trackerType);
            var operation = (IEnumerator)_trackerType.GetMethod("RunGuarded", PrivateInstance).Invoke(tracker,
                new object[] { ThrowAfterFrame() });
            Assert.That(operation.MoveNext(), Is.True);
            LogAssert.Expect(LogType.Error,
                "[TrackingStartup] Failed during Stopped: Tracking operation failed: Test SDK error");
            Assert.That(operation.MoveNext(), Is.False);
            Assert.That(_trackerType.GetProperty("Phase").GetValue(tracker).ToString(), Is.EqualTo("Failed"));
        }

        private static IEnumerator ThrowAfterFrame()
        {
            yield return null;
            throw new InvalidOperationException("Test SDK error");
        }

        [Test]
        public void MarkerCapacityStillAllowsUpdatesToExistingPendingMarkers()
        {
            Component tracker = CreateInactiveComponent(_trackerType);
            _trackerType.GetField("_maxTrackedMarkers", PrivateInstance).SetValue(tracker, 2);
            MethodInfo remember = _trackerType.GetMethod("RememberMarker", PrivateInstance);
            remember.Invoke(tracker, new object[] { 1UL, Pose.identity });
            remember.Invoke(tracker, new object[] { 2UL, Pose.identity });
            remember.Invoke(tracker, new object[] { 3UL, Pose.identity });
            Pose updated = new Pose(Vector3.one, Quaternion.identity);
            remember.Invoke(tracker, new object[] { 1UL, updated });
            var pending = (Dictionary<ulong, Pose>)_trackerType.GetField("_pendingMarkers", PrivateInstance).GetValue(tracker);
            Assert.That(pending.Count, Is.EqualTo(2));
            Assert.That(pending.ContainsKey(3), Is.False);
            Assert.That(pending[1].position, Is.EqualTo(Vector3.one));
        }

        [Test]
        public void InvalidMarkerNeverEntersPendingQueue()
        {
            Component tracker = CreateInactiveComponent(_trackerType);
            Pose invalid = new Pose(new Vector3(float.NaN, 0f, 0f), Quaternion.identity);
            _trackerType.GetMethod("RememberMarker", PrivateInstance).Invoke(tracker, new object[] { 39UL, invalid });
            var pending = (Dictionary<ulong, Pose>)_trackerType.GetField("_pendingMarkers", PrivateInstance).GetValue(tracker);
            Assert.That(pending, Is.Empty);
        }

        [Test]
        public void UntrackedAnchorTimesOutAndReleasesItsObject()
        {
            Component tracker = CreateInactiveComponent(_trackerType);
            var anchorObject = new GameObject("Pending anchor");
            anchorObject.transform.SetParent(_testObject.transform, false);
            var anchor = anchorObject.AddComponent<UnityEngine.XR.ARFoundation.ARAnchor>();
            var instance = new GameObject("Pending visual");
            instance.transform.SetParent(anchorObject.transform, false);
            Type visualType = _trackerType.GetNestedType("MarkerVisual", BindingFlags.NonPublic);
            object visual = Activator.CreateInstance(visualType);
            visualType.GetField("Anchor").SetValue(visual, anchor);
            visualType.GetField("Instance").SetValue(visual, instance);
            float now = (float)_trackerType.GetProperty("ActiveTime", PrivateInstance).GetValue(tracker);
            visualType.GetField("CreatedAt").SetValue(visual, now - 20f);
            var markers = (IDictionary)_trackerType.GetField("_markers", PrivateInstance).GetValue(tracker);
            markers.Add(39UL, visual);
            LogAssert.Expect(LogType.Error,
                "[TrackingStartup] Failed during Stopped: A spatial anchor did not become tracked before the timeout. Retry after XR tracking recovers.");
            Assert.That((bool)_trackerType.GetMethod("UpdateVisuals", PrivateInstance).Invoke(tracker, null), Is.False);
            Assert.That(markers, Is.Empty);
            Assert.That(anchorObject == null, Is.True);
        }

        [Test]
        public void EndedSessionDiscardsCachedMarkerPoses()
        {
            Component tracker = CreateInactiveComponent(_trackerType);
            var pending = (Dictionary<ulong, Pose>)_trackerType.GetField("_pendingMarkers", PrivateInstance).GetValue(tracker);
            pending.Add(39, Pose.identity);
            var recovery = (IEnumerator)_trackerType.GetMethod("RecoverTracking", PrivateInstance).Invoke(tracker, null);
            Assert.That(recovery.MoveNext(), Is.False);
            Assert.That(pending, Is.Empty);
        }

        private Component CreateInactiveComponent(Type type)
        {
            _testObject = new GameObject("Tracking test");
            _testObject.SetActive(false);
            return _testObject.AddComponent(type);
        }
    }
}
