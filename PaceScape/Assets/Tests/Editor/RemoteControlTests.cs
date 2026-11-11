// Checks dashboard command routing and validation against Unity scene components.

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PaceScape.Tests
{
    // Checks remote commands against real scene components without a device or network connection.
    public sealed class RemoteControlTests
    {
        private GameObject _root;
        private Component _tracker;
        private Component _placement;
        private Type _commands;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Remote control tests");
            _root.SetActive(false);
            _tracker = _root.AddComponent(RuntimeType("Tracking.TagTracker"));
            _placement = _root.AddComponent(RuntimeType("Tracking.MarkerPlanePlacement"));
            _commands = RuntimeType("RemoteControl.RemoteControlCommands");
        }

        [TearDown]
        public void TearDown()
        {
            UnityEngine.Object.DestroyImmediate(_root);
        }

        [Test]
        public void PaceCommandChangesTheRuntimeTarget()
        {
            Assert.That(Apply("runner.pace", "{\"paceMinutesPerKilometre\":6.5}"), Is.True);
            Assert.That(Property(_tracker, "PaceMinutesPerKilometre"), Is.EqualTo(6.5f));
        }

        [TestCase("{\"paceMinutesPerKilometre\":0}")]
        [TestCase("{\"paceMinutesPerKilometre\":-1}")]
        [TestCase("{\"paceMinutesPerKilometre\":61}")]
        public void InvalidPaceLeavesThePreviousValueUnchanged(string payload)
        {
            Assert.That(Apply("runner.pace", payload), Is.False);
            Assert.That(Property(_tracker, "PaceMinutesPerKilometre"), Is.EqualTo(6f));
        }

        [Test]
        public void InvalidSpawnIsAtomicAndValidSpawnUsesBothSettings()
        {
            Assert.That(Apply("runner.spawn", "{\"spawnLane\":9,\"distanceBehindStartLine\":4}"), Is.False);
            Assert.That(Property(_tracker, "SpawnLane"), Is.EqualTo(1));
            Assert.That(Property(_tracker, "DistanceBehindStartLine"), Is.EqualTo(0.5f));
            Assert.That(Apply("runner.spawn", "{\"spawnLane\":8,\"distanceBehindStartLine\":0}"), Is.True);
            Assert.That(Property(_tracker, "SpawnLane"), Is.EqualTo(8));
            Assert.That(Property(_tracker, "DistanceBehindStartLine"), Is.EqualTo(0f));
        }

        [Test]
        public void DetectorConfigurationIsValidatedBeforeMutation()
        {
            Assert.That(Apply("tracking.configure", "{\"markerLength\":0.2,\"startupTimeoutSeconds\":20,\"maxTrackedMarkers\":65}"), Is.False);
            Assert.That(Property(_tracker, "MarkerLength"), Is.EqualTo(0.1f));
            Assert.That(Apply("tracking.configure", "{\"markerLength\":0.2,\"startupTimeoutSeconds\":20,\"maxTrackedMarkers\":32}"), Is.True);
            Assert.That(Property(_tracker, "MarkerLength"), Is.EqualTo(0.2f));
            Assert.That(Property(_tracker, "MaxTrackedMarkers"), Is.EqualTo(32));
        }

        [Test]
        public void PlacementConfigurationPreservesOldValuesOnFailure()
        {
            Assert.That(Apply("placement.configure", "{\"placeOnFloor\":false,\"queryPadding\":2,\"maxMarkerPlaneDistance\":0.1,\"minimumNormalAlignment\":2}"), Is.False);
            Assert.That(Property(_placement, "PlaceOnFloor"), Is.True);
            Assert.That(Apply("placement.configure", "{\"placeOnFloor\":false,\"queryPadding\":2,\"maxMarkerPlaneDistance\":0.1,\"minimumNormalAlignment\":0.8}"), Is.True);
            Assert.That(Property(_placement, "PlaceOnFloor"), Is.False);
            Assert.That(Property(_placement, "QueryPadding"), Is.EqualTo(2f));
        }

        [Test]
        public void MalformedUnknownAndNonCommandMessagesAreRejected()
        {
            Assert.That(Apply("avatar.spawn", "{}"), Is.False);
            object[] arguments = { "{invalid", _tracker, _placement, null };
            Assert.That(_commands.GetMethod("TryApply").Invoke(null, arguments), Is.False);
            arguments[0] = "{\"type\":\"state\",\"commandId\":\"test\",\"commandType\":\"tracking.stop\",\"payload\":{}}";
            Assert.That(_commands.GetMethod("TryApply").Invoke(null, arguments), Is.False);
        }

        [Test]
        public void UnifiedRunnerConfigurationSetsAllFieldsWithoutStartingMovement()
        {
            Assert.That(Apply("runner.configure", "{\"paceMinutesPerKilometre\":6.5,\"spawnLane\":4,\"distanceBehindStartLine\":0.75}"), Is.True);
            Assert.That(Property(_tracker, "PaceMinutesPerKilometre"), Is.EqualTo(6.5f));
            Assert.That(Property(_tracker, "SpawnLane"), Is.EqualTo(4));
            Assert.That(Property(_tracker, "DistanceBehindStartLine"), Is.EqualTo(0.75f));
            Assert.That(Property(_tracker, "RunState").ToString(), Is.EqualTo("Ready"));
        }

        [Test]
        public void RunStartWithoutTrackedPlacementDoesNotMutateSettingsOrQueueAnAutomaticStart()
        {
            Assert.That(Apply("run.start", "{\"paceMinutesPerKilometre\":5,\"spawnLane\":8,\"distanceBehindStartLine\":2}"), Is.False);
            Assert.That(Property(_tracker, "PaceMinutesPerKilometre"), Is.EqualTo(6f));
            Assert.That(Property(_tracker, "SpawnLane"), Is.EqualTo(1));
            Assert.That(Property(_tracker, "RunState").ToString(), Is.EqualTo("Ready"));
            Assert.That(Apply("run.pause", "{}"), Is.False);
            Assert.That(Apply("run.resume", "{}"), Is.False);
            Assert.That(Apply("run.reset", "{}"), Is.True);
            Assert.That(Property(_tracker, "RunState").ToString(), Is.EqualTo("Ready"));
        }

        [Test]
        public void DeviceTrackingCannotBeStartedInTheEditor()
        {
            Assert.That(Apply("tracking.start", "{}"), Is.False);
            Assert.That(Apply("tracking.retry", "{}"), Is.False);
        }

        [Test]
        public void RunnerModeCommandIsStrictAndAlsoSupportedByTheMarkerFreePreview()
        {
            Assert.That(Apply("runner.mode", "{\"runnerMode\":\"FixedPace\"}"), Is.True);
            Assert.That(Property(_tracker, "Mode").ToString(), Is.EqualTo("FixedPace"));
            Assert.That(Apply("runner.mode", "{}"), Is.False);
            Assert.That(Apply("runner.mode", "{\"runnerMode\":\"GhostRunner\"}"), Is.False);
            Assert.That(Property(_tracker, "Mode").ToString(), Is.EqualTo("FixedPace"));
            Assert.That(Apply("runner.mode", "{\"runnerMode\":\"FixedPace\"}"), Is.True);
            Component preview = _root.AddComponent(RuntimeType("RemoteControl.ServerTestScene"));
            object[] arguments =
            {
                "{\"type\":\"command\",\"commandId\":\"mode\",\"commandType\":\"runner.mode\",\"payload\":{\"runnerMode\":\"FixedPace\"}}",
                preview,
                null
            };
            Assert.That(_commands.GetMethod("TryApplyTestScene").Invoke(null, arguments), Is.True);
            Assert.That(Property(preview, "Mode").ToString(), Is.EqualTo("FixedPace"));
        }

        [Test]
        public void ComparisonCommandRequiresAnOwnerAndValidWholeDistance()
        {
            Assert.That(Apply("runner.comparison", "{\"comparisonDistanceMetres\":5000,\"useDemoRecords\":true}"), Is.False);
            Component performance = _root.AddComponent(Type.GetType("PaceScape.Performance.RunnerPerformance, Assembly-CSharp", true));
            _tracker.GetType().GetProperty("PerformanceSource").SetValue(_tracker, performance);
            Assert.That(Apply("runner.comparison", "{\"comparisonDistanceMetres\":5000,\"useDemoRecords\":true}"), Is.True);
            Assert.That(Property(performance, "ComparisonDistanceMetres"), Is.EqualTo(5000));
            Assert.That(Apply("runner.comparison", "{}"), Is.False);
            Assert.That(Apply("runner.comparison", "{\"comparisonDistanceMetres\":0}"), Is.False);
            Assert.That(Apply("runner.comparison", "{\"comparisonDistanceMetres\":400.5}"), Is.False);
            Assert.That(Property(performance, "ComparisonDistanceMetres"), Is.EqualTo(5000));
            Assert.That(Apply("runner.mode", "{\"runnerMode\":\"AllTimePersonalBest\"}"), Is.True);
            Assert.That(Property(_tracker, "Mode").ToString(), Is.EqualTo("AllTimePersonalBest"));
        }

        private bool Apply(string command, string payload)
        {
            string json = "{\"type\":\"command\",\"commandId\":\"test\",\"commandType\":\"" + command + "\",\"payload\":" + payload + "}";
            object[] arguments = { json, _tracker, _placement, null };
            return (bool)_commands.GetMethod("TryApply").Invoke(null, arguments);
        }

        private static object Property(Component target, string name)
        {
            return target.GetType().GetProperty(name).GetValue(target);
        }

        private static Type RuntimeType(string name)
        {
            Type type = Type.GetType("PaceScape." + name + ", Assembly-CSharp");
            Assert.That(type, Is.Not.Null);
            return type;
        }
    }
}
