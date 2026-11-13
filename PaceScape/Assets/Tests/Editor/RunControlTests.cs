// Checks run state transitions and dashboard control of the test runner.

using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PaceScape.Tests
{
    // Exercises explicit run control with real lane motion while leaving native XR readiness unmocked.
    public sealed class RunControlTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject _root;
        private GameObject _runner;
        private Component _tracker;
        private object _session;
        private object _motion;
        private IDictionary _markers;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Run control tests");
            _root.SetActive(false);
            Type trackerType = RuntimeType("Tracking.TagTracker");
            _tracker = _root.AddComponent(trackerType);
            _session = trackerType.GetField("_runSession", PrivateInstance).GetValue(_tracker);
            _runner = new GameObject("Waiting runner");
            _runner.transform.SetParent(_root.transform, false);
            _runner.transform.localPosition = Vector3.up * 0.05f;
            _motion = Activator.CreateInstance(RuntimeType("RunningTrack.RunningTrackMotion"), _runner.transform, 1, 0.5f);
            _markers = (IDictionary)trackerType.GetField("_markers", PrivateInstance).GetValue(_tracker);
            Type visualType = trackerType.GetNestedType("MarkerVisual", BindingFlags.NonPublic);
            object visual = Activator.CreateInstance(visualType);
            visualType.GetField("Instance").SetValue(visual, _runner);
            visualType.GetField("Motion").SetValue(visual, _motion);
            _markers.Add(1UL, visual);
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
        }

        [TestCase("FixedPace")]
        [TestCase("AllTimePersonalBest")]
        [TestCase("SideBySide")]
        [TestCase("Manual")]
        public void StartCountdownReservesTheRunAndKeepsTheRunnerStillUntilConfirmed(string mode)
        {
            Assert.That(SessionCall("TrySetMode", mode), Is.True);
            Vector3 start = _runner.transform.localPosition;
            Assert.That(SessionCall("TryBeginCountdown", false), Is.False);
            Assert.That(SessionCall("TryBeginCountdown", true), Is.True);
            Assert.That(SessionCall("TryBeginCountdown", true), Is.False);
            Assert.That(SessionCall("TryCompleteCountdown", true), Is.False);
            Assert.That(SessionCall("TickCountdown", 2f), Is.False);
            Advance();
            Assert.That(_runner.transform.localPosition, Is.EqualTo(start));

            Assert.That(SessionCall("TryPause"), Is.True);
            Assert.That(SessionCall("TickCountdown", 3f), Is.False);
            Assert.That(SessionCall("TryBeginCountdown", true), Is.True);
            Assert.That(SessionCall("TickCountdown", 3f), Is.True);
            Assert.That(SessionCall("TickCountdown", 1f), Is.False);
            Advance();
            Assert.That(_runner.transform.localPosition, Is.EqualTo(start));
            Assert.That(SessionCall("TryCompleteCountdown", false), Is.False);
            Assert.That(SessionCall("TryCompleteCountdown", true), Is.True);
            Advance();
            Assert.That(_runner.transform.localPosition, Is.Not.EqualTo(start));
        }

        [Test]
        public void WaitingAndPausedSessionsDoNotMoveAndResumeContinuesFromTheSamePosition()
        {
            Vector3 start = _runner.transform.localPosition;
            Advance();
            Assert.That(_runner.transform.localPosition, Is.EqualTo(start));
            Assert.That(SessionCall("TryStart", false), Is.False);
            Assert.That(SessionCall("TryStart", true), Is.True);
            Assert.That(SessionCall("TryStart", true), Is.False);
            Advance();
            Vector3 running = _runner.transform.localPosition;
            Assert.That(Vector3.Distance(running, start), Is.GreaterThan(1f));
            Assert.That(SessionCall("TryPause"), Is.True);
            Advance();
            Assert.That(_runner.transform.localPosition, Is.EqualTo(running));
            Assert.That(SessionCall("TryResume", false), Is.False);
            Advance();
            Assert.That(_runner.transform.localPosition, Is.EqualTo(running));
            Assert.That(SessionCall("TryResume", true), Is.True);
            Advance();
            Assert.That(Vector3.Distance(_runner.transform.localPosition, running), Is.GreaterThan(1f));
        }

        [Test]
        public void ConfigurationMovesWaitingObjectsAndRejectsAllChangesWhenPositionalEditIsNotAllowed()
        {
            Assert.That(TrackerCall("TryConfigureRunner", 6.5f, 4, 0.75f), Is.True);
            Assert.That(_runner.transform.localPosition.x, Is.EqualTo((4f - 0.5f) * 1.22f - 0.3f).Within(0.0001f));
            Assert.That(_runner.transform.localPosition.z, Is.EqualTo(-0.75f).Within(0.0001f));
            Assert.That(_runner.transform.localPosition.y, Is.EqualTo(0.05f));
            SessionCall("TryStart", true);
            Vector3 before = _runner.transform.localPosition;
            Assert.That(TrackerCall("TryConfigureRunner", 5f, 8, 2f), Is.False);
            Assert.That(Property("PaceMinutesPerKilometre"), Is.EqualTo(6.5f));
            Assert.That(Property("SpawnLane"), Is.EqualTo(4));
            Assert.That(_runner.transform.localPosition, Is.EqualTo(before));
            Assert.That(TrackerCall("TryConfigureRunner", 5f, 4, 0.75f), Is.True);
            Assert.That(Property("RunState").ToString(), Is.EqualTo("Running"));
        }

        [Test]
        public void ResetReturnsToConfiguredStartAndRetainsThePlacedContent()
        {
            TrackerCall("TryConfigureRunner", 6f, 8, 1.25f);
            Vector3 configured = _runner.transform.localPosition;
            SessionCall("TryStart", true);
            Advance();
            _tracker.GetType().GetMethod("ResetRun").Invoke(_tracker, null);
            Assert.That(Property("RunState").ToString(), Is.EqualTo("Ready"));
            Assert.That(_runner.transform.localPosition, Is.EqualTo(configured));
            Assert.That(_markers.Count, Is.EqualTo(1));
            Assert.That(_runner, Is.Not.Null);
            Advance();
            Assert.That(_runner.transform.localPosition, Is.EqualTo(configured));
        }

        [Test]
        public void ResetImmediatelyRestoresTheCalibratedLaneStart()
        {
            object visual = _markers[1UL];
            Type visualType = visual.GetType();
            visualType.GetField("HasGhostCalibration").SetValue(visual, true);
            visualType.GetField("GhostOrigin").SetValue(visual, new Vector3(0, 0, 4));
            Assert.That(TrackerCall("TryConfigureGhostPresentation", "TreadmillRelative", 1.5f, 10f), Is.True);
            Vector3 start = _runner.transform.localPosition;

            SessionCall("TryStart", true);
            _motion.GetType().GetMethod("SetPresentationPose").Invoke(_motion,
                new object[] { new Pose(new Vector3(1, 0, 9), Quaternion.identity), true });
            _tracker.GetType().GetMethod("ResetRun").Invoke(_tracker, null);

            Assert.That(Property("RunState").ToString(), Is.EqualTo("Ready"));
            Assert.That(_runner.transform.localPosition, Is.EqualTo(start));
        }

        [TestCase("FTMS", "FixedPace", true)]
        [TestCase("FTMS", "AllTimePersonalBest", true)]
        [TestCase("FTMS", "SideBySide", true)]
        [TestCase("Manual", "FixedPace", true)]
        [TestCase("Manual", "AllTimePersonalBest", true)]
        [TestCase("Manual", "SideBySide", true)]
        [TestCase("Stadium", "FixedPace", false)]
        [TestCase("Stadium", "AllTimePersonalBest", false)]
        public void TrackFollowsEnvironmentSpeedRegardlessOfGhostMode(string environment, string ghostMode, bool moves)
        {
            var track = new GameObject("Track visual").transform;
            track.SetParent(_root.transform, false);
            object visual = _markers[1UL];
            Type visualType = visual.GetType();
            visualType.GetField("Track").SetValue(visual, track);
            visualType.GetField("HasGhostCalibration").SetValue(visual, true);
            visualType.GetField("GhostOrigin").SetValue(visual, Vector3.zero);
            MethodInfo configure = _tracker.GetType().GetMethod("TryConfigureRunSetup", PrivateInstance);
            MethodInfo advance = _tracker.GetType().GetMethod("AdvanceTrack", PrivateInstance);
            MethodInfo applyPresentation = _tracker.GetType().GetMethod("ApplyGhostPresentation");
            Assert.That(configure.Invoke(_tracker, new object[] { environment, ghostMode }), Is.True);
            Assert.That(TrackerCall("TrySetPace", 8f), Is.True);
            if (environment == "Manual")
            {
                Assert.That(_tracker.GetType().GetMethod("TrySetManualTrackSpeed", PrivateInstance)
                    .Invoke(_tracker, new object[] { 12f }), Is.True);
            }
            Vector3 start = track.localPosition;
            Vector3 companionStart = _runner.transform.localPosition;

            Assert.That(SessionCall("TryStart", true), Is.True);
            advance.Invoke(_tracker, new object[] { 2f, environment == "Manual" ? 0.0 : 2.5 });
            applyPresentation.Invoke(_tracker, null);
            float expectedTravel = !moves ? 0f : environment == "Manual" ? 20f / 3f : 5f;
            Assert.That(track.localPosition.z, Is.EqualTo(start.z - expectedTravel).Within(0.0001f));
            if (ghostMode == "SideBySide")
            {
                Assert.That(Vector3.Distance(_runner.transform.localPosition, companionStart), Is.LessThan(0.0001f));
            }

            Vector3 movingPosition = track.localPosition;
            Assert.That(SessionCall("TryPause"), Is.True);
            advance.Invoke(_tracker, new object[] { 1f, 2.5 });
            applyPresentation.Invoke(_tracker, null);
            Assert.That(track.localPosition, Is.EqualTo(movingPosition));
            _tracker.GetType().GetMethod("ResetRun").Invoke(_tracker, null);
            Assert.That(track.localPosition, Is.EqualTo(start));

            Assert.That(configure.Invoke(_tracker, new object[] { "Stadium", "FixedPace" }), Is.True);
            Assert.That(track.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(track.localRotation, Is.EqualTo(Quaternion.identity));
        }

        [Test]
        public void ManualModeMovesTrackAtSelectedPaceAndKeepsCompanionBesideTheStart()
        {
            var track = new GameObject("Track visual").transform;
            track.SetParent(_root.transform, false);
            object visual = _markers[1UL];
            Type visualType = visual.GetType();
            visualType.GetField("Track").SetValue(visual, track);
            visualType.GetField("HasGhostCalibration").SetValue(visual, true);
            Assert.That(TrackerCall("TrySetRunnerMode", "Manual"), Is.True);
            MethodInfo setSpeed = _tracker.GetType().GetMethod("TrySetManualTrackSpeed", PrivateInstance);
            Assert.That(setSpeed.Invoke(_tracker, new object[] { 12f }), Is.True);
            Vector3 start = track.localPosition;
            Vector3 companionStart = _runner.transform.localPosition;
            MethodInfo advance = _tracker.GetType().GetMethod("AdvanceCompanion", PrivateInstance);
            MethodInfo advanceTrack = _tracker.GetType().GetMethod("AdvanceTrack", PrivateInstance);
            MethodInfo present = _tracker.GetType().GetMethod("ApplyGhostPresentation");

            Assert.That(SessionCall("TryStart", true), Is.True);
            advance.Invoke(_tracker, new object[] { _motion, 3f, 0.0 });
            advanceTrack.Invoke(_tracker, new object[] { 3f, 0.0 });
            present.Invoke(_tracker, null);
            Assert.That(track.localPosition.z, Is.EqualTo(start.z - 10f).Within(0.0001f));
            Assert.That(Vector3.Distance(_runner.transform.localPosition, companionStart), Is.LessThan(0.0001f));
            Assert.That(TrackerCall("TrySetRunnerMode", "FixedPace"), Is.False);

            Assert.That(TrackerCall("TrySetPace", 10f), Is.True);
            Assert.That(Property("ActivePaceMinutesPerKilometre"), Is.EqualTo(5f));
            Assert.That(setSpeed.Invoke(_tracker, new object[] { 6f }), Is.True);
            advance.Invoke(_tracker, new object[] { _motion, 3f, 0.0 });
            advanceTrack.Invoke(_tracker, new object[] { 3f, 0.0 });
            present.Invoke(_tracker, null);
            Assert.That(_motion.GetType().GetProperty("TravelledMetres").GetValue(_motion), Is.EqualTo(15.0).Within(0.0001));
            Assert.That(track.localPosition.z, Is.LessThan(start.z - 10f));
            Vector3 movingPosition = track.localPosition;
            Quaternion movingRotation = track.localRotation;
            Assert.That(SessionCall("TryPause"), Is.True);
            advance.Invoke(_tracker, new object[] { _motion, 3f, 0.0 });
            present.Invoke(_tracker, null);
            Assert.That(track.localPosition, Is.EqualTo(movingPosition));
            Assert.That(track.localRotation, Is.EqualTo(movingRotation));
            Assert.That(_motion.GetType().GetProperty("TravelledMetres").GetValue(_motion), Is.EqualTo(15.0).Within(0.0001));
            _tracker.GetType().GetMethod("ResetRun").Invoke(_tracker, null);
            Assert.That(track.localPosition, Is.EqualTo(start));
        }

        [Test]
        public void ManualTrackSpeedDoesNotChangeTheExamplePrGhostPace()
        {
            Component performance = _root.AddComponent(RuntimeType("Performance.RunnerPerformance"));
            _tracker.GetType().GetProperty("PerformanceSource").SetValue(_tracker, performance);
            Assert.That(performance.GetType().GetMethod("TryConfigure")
                .Invoke(performance, new object[] { 5000f, true }), Is.True);
            Assert.That(_tracker.GetType().GetMethod("TryConfigureRunSetup", PrivateInstance)
                .Invoke(_tracker, new object[] { "Manual", "AllTimePersonalBest" }), Is.True);
            MethodInfo setSpeed = _tracker.GetType().GetMethod("TrySetManualTrackSpeed", PrivateInstance);
            Assert.That(setSpeed.Invoke(_tracker, new object[] { 8f }), Is.True);
            Assert.That(Property("ActivePaceMinutesPerKilometre"), Is.EqualTo(5f));

            MethodInfo advance = performance.GetType().GetMethod("AdvanceGhost");
            double travel = (double)advance.Invoke(performance, new object[] { 3f, 0.0, 6f });
            Assert.That(setSpeed.Invoke(_tracker, new object[] { 16f }), Is.True);
            Assert.That(Property("ActivePaceMinutesPerKilometre"), Is.EqualTo(5f).Within(0.001f));
            Assert.That(advance.Invoke(performance, new object[] { 3f, travel, 6f }), Is.EqualTo(travel).Within(0.001));
        }

        [Test]
        public void RunSetupSelectsEnvironmentAndGhostIndependently()
        {
            MethodInfo configure = _tracker.GetType().GetMethod("TryConfigureRunSetup", PrivateInstance);
            Assert.That(configure.Invoke(_tracker, new object[] { "Manual", "AllTimePersonalBest" }), Is.True);
            Assert.That(Property("IsManual"), Is.True);
            Assert.That(Property("Mode").ToString(), Is.EqualTo("AllTimePersonalBest"));
            Assert.That(configure.Invoke(_tracker, new object[] { "Stadium", "SideBySide" }), Is.False);
            Assert.That(Property("IsManual"), Is.True);
            Assert.That(configure.Invoke(_tracker, new object[] { "FTMS", "SideBySide" }), Is.True);
            Assert.That(Property("IsManual"), Is.False);
            Assert.That(Property("Mode").ToString(), Is.EqualTo("SideBySide"));
            Assert.That(configure.Invoke(_tracker, new object[] { "Stadium", "FixedPace" }), Is.True);
            Assert.That(Property("Mode").ToString(), Is.EqualTo("FixedPace"));
        }

        [Test]
        public void HidingContentPausesAndReadinessCannotBeBypassedByRemoteStartOrResume()
        {
            SessionCall("TryStart", true);
            _tracker.GetType().GetMethod("HideVisuals", PrivateInstance).Invoke(_tracker, null);
            Assert.That(Property("RunState").ToString(), Is.EqualTo("Paused"));
            Assert.That(_runner.activeSelf, Is.False);
            Assert.That(TrackerCall("TryResumeRun"), Is.False);
            _tracker.GetType().GetMethod("ResetRun").Invoke(_tracker, null);
            Assert.That(TrackerCall("TryStartRun", 5f, 8, 3f), Is.False);
            Assert.That(Property("PaceMinutesPerKilometre"), Is.EqualTo(6f));
            Assert.That(Property("SpawnLane"), Is.EqualTo(1));
        }

        [Test]
        public void SessionCleanupReturnsToWaitingAndRemovesOldPlacements()
        {
            SessionCall("TryStart", true);
            _tracker.GetType().GetMethod("ReleaseResources", PrivateInstance).Invoke(_tracker, null);
            Assert.That(Property("RunState").ToString(), Is.EqualTo("Ready"));
            Assert.That(_markers, Is.Empty);
        }

        [Test]
        public void ModeSwitchPausesWithoutRewindingAndResetRetainsTheMode()
        {
            SessionCall("TryStart", true);
            Advance();
            Vector3 before = _runner.transform.localPosition;
            Assert.That(TrackerCall("TrySetRunnerMode", "AllTimePersonalBest"), Is.True);
            Assert.That(Property("RunState").ToString(), Is.EqualTo("Paused"));
            Assert.That(Property("Mode").ToString(), Is.EqualTo("AllTimePersonalBest"));
            Assert.That(_runner.transform.localPosition, Is.EqualTo(before));
            Assert.That(SessionCall("TryResume", true), Is.True);
            Assert.That(TrackerCall("TrySetRunnerMode", "AllTimePersonalBest"), Is.True);
            Assert.That(Property("RunState").ToString(), Is.EqualTo("Running"));
            Assert.That(TrackerCall("TrySetRunnerMode", "GhostRunner"), Is.False);
            Assert.That(TrackerCall("TrySetRunnerMode", "1"), Is.False);
            Assert.That(Property("RunState").ToString(), Is.EqualTo("Running"));
            _tracker.GetType().GetMethod("ResetRun").Invoke(_tracker, null);
            Assert.That(Property("Mode").ToString(), Is.EqualTo("AllTimePersonalBest"));
            Assert.That(Property("RunState").ToString(), Is.EqualTo("Ready"));
        }

        private void Advance()
        {
            _session.GetType().GetMethod("Advance").Invoke(_session, new object[] { _motion, 1f, 6f });
        }

        private bool SessionCall(string method, params object[] arguments)
        {
            return (bool)_session.GetType().GetMethod(method).Invoke(_session, arguments);
        }

        private bool TrackerCall(string method, params object[] arguments)
        {
            return (bool)_tracker.GetType().GetMethod(method).Invoke(_tracker, arguments);
        }

        private object Property(string name)
        {
            return _tracker.GetType().GetProperty(name).GetValue(_tracker);
        }

        private static Type RuntimeType(string name)
        {
            return Type.GetType("PaceScape." + name + ", Assembly-CSharp", true);
        }
    }
}
