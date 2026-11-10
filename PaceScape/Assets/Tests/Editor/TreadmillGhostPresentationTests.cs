using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PaceScape.Tests
{
    /// <summary>Checks presentation-only overtaking, locked calibration and visibility without hardware.</summary>
    public sealed class TreadmillGhostPresentationTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private static Type RuntimeType(string name) => Type.GetType("PaceScape." + name + ", Assembly-CSharp", true);
        private static object Create(string name) => Activator.CreateInstance(RuntimeType(name));
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, PrivateInstance).SetValue(target, value);

        [Test]
        public void CalibrationProjectsOntoFloor()
        {
            Type type = RuntimeType("RunningTrack.TreadmillGhostPresentation");
            object[] args = { new Vector3(3, 1.8f, 4), Vector3.zero };
            Assert.That(type.GetMethod("TryCalibrate").Invoke(null, args), Is.True);
            Assert.That(args[1], Is.EqualTo(new Vector3(3, 0, 4)));
        }

        [TestCase(1, 0.0, -2.0)]
        [TestCase(8, 0.0, 2.0)]
        [TestCase(3, 8.0, 5.0)]
        [TestCase(4, 35.0, -2.0)]
        [TestCase(5, 175.0, 0.0)]
        [TestCase(8, 270.0, 2.0)]
        public void RelativeGapFollowsTheSelectedLane(int lane, double reference, double gap)
        {
            object settings = Create("RunningTrack.TreadmillGhostPresentation");
            Pose referencePose = EvaluateLane(lane, reference);
            Vector3 calibration = referencePose.position + referencePose.rotation * Vector3.right * 2;
            object[] args = { lane, calibration, gap, Pose.identity };
            Assert.That(Call(settings, "Evaluate", args), Is.True);
            Pose pose = (Pose)args[3];
            Pose expected = EvaluateLane(lane, reference + gap);
            Assert.That(Vector3.Distance(pose.position, expected.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(pose.rotation, expected.rotation), Is.LessThan(0.01f));
        }

        private static Pose EvaluateLane(int lane, double distance)
        {
            return (Pose)RuntimeType("RunningTrack.RunningTrackGeometry").GetMethod("EvaluateLane")
                .Invoke(null, new object[] { lane, distance });
        }

        [TestCase(-10.01, false)]
        [TestCase(-10.0, true)]
        [TestCase(10.0, true)]
        [TestCase(10.01, false)]
        [TestCase(double.NaN, false)]
        [TestCase(double.PositiveInfinity, false)]
        public void OnlyFiniteGapsWithinTheConfiguredDistanceAreVisible(double gap, bool expected)
        {
            object settings = Create("RunningTrack.TreadmillGhostPresentation");
            object[] args = { 1, Vector3.zero, gap, Pose.identity };
            Assert.That(Call(settings, "Evaluate", args), Is.EqualTo(expected));
            Assert.That(Mathf.Abs(((Pose)args[3]).position.z), Is.LessThanOrEqualTo(10));
        }

        [Test]
        public void InvalidSettingsAndCalibrationCannotReplaceValidValues()
        {
            object settings = Create("RunningTrack.TreadmillGhostPresentation");
            Assert.That(Call(settings, "TryConfigure", "TreadmillRelative", -2f, 8f), Is.True);
            Assert.That(Call(settings, "TryConfigure", "Other", 1.5f, 10f), Is.False);
            Assert.That(Call(settings, "TryConfigure", "Track", 0f, 10f), Is.False);
            Assert.That(Call(settings, "TryConfigure", "Track", 1f, float.NaN), Is.False);
            object[] evaluation = { 1, Vector3.zero, 9.0, Pose.identity };
            Assert.That(Call(settings, "Evaluate", evaluation), Is.False);
            Assert.That(((Pose)evaluation[3]).position.z, Is.EqualTo(8).Within(0.0001f));
            object[] calibration = { new Vector3(float.NaN, 0, 0), Vector3.zero };
            Assert.That(settings.GetType().GetMethod("TryCalibrate").Invoke(null, calibration), Is.False);
        }

        [Test]
        public void RelativePlacementAndHiddenRenderersNeverChangeLogicalTravel()
        {
            var root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var disabled = GameObject.CreatePrimitive(PrimitiveType.Cube);
            disabled.transform.SetParent(root.transform, false);
            disabled.GetComponent<Renderer>().enabled = false;
            try
            {
                object motion = Activator.CreateInstance(RuntimeType("RunningTrack.RunningTrackMotion"), root.transform, 1, 0.5f);
                Call(motion, "Advance", 1f, 6f);
                Call(motion, "SetPresentationPose", new Pose(new Vector3(1.5f, 0, -2), Quaternion.identity), false);
                Assert.That(root.GetComponent<Renderer>().enabled, Is.False);
                Assert.That(root.activeSelf, Is.True);
                Call(motion, "Advance", 1f, 6f);
                double distance = (double)motion.GetType().GetProperty("TravelledMetres").GetValue(motion);
                Assert.That(distance, Is.EqualTo(1000.0 / 60 / 6 * 2).Within(0.000001));
                Assert.That(root.transform.localPosition, Is.EqualTo(new Vector3(1.5f, 0, -2)));
                Call(motion, "SetPresentationPose", new Pose(new Vector3(1.5f, 0, 2), Quaternion.identity), true);
                Assert.That(root.GetComponent<Renderer>().enabled, Is.True);
                Assert.That(disabled.GetComponent<Renderer>().enabled, Is.False);
                Call(motion, "ClearPresentationPose");
                Pose expected = (Pose)RuntimeType("RunningTrack.RunningTrackGeometry").GetMethod("EvaluateLane")
                    .Invoke(null, new object[] { 1, distance - 0.5 });
                Assert.That(Vector3.Distance(root.transform.localPosition, expected.position), Is.LessThan(0.0001f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MeasurementGapsCannotCreateAnApparentRelativePosition()
        {
            var root = new GameObject("Relative comparison test");
            try
            {
                Component performance = root.AddComponent(RuntimeType("Performance.RunnerPerformance"));
                Set(performance, "_ghostDistance", 10.0);
                Set(performance, "_comparisonGhostOrigin", 2.0);
                object laps = performance.GetType().GetField("_laps", PrivateInstance).GetValue(performance);
                Call(laps, "Advance", 2.0, 7.2);
                object[] args = { 0.0 };
                Assert.That(Call(performance, "TryGetGhostGap", args), Is.False);
                Set(performance, "_wasMeasuring", true);
                Assert.That(Call(performance, "TryGetGhostGap", args), Is.True);
                Assert.That(args[0], Is.EqualTo(4.0));
                Set(performance, "_hasMeasurementGap", true);
                Assert.That(Call(performance, "TryGetGhostGap", args), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PresentationCommandsRequireReadyAndExplicitValidSettings()
        {
            var root = new GameObject("Relative settings test");
            root.SetActive(false);
            try
            {
                Component tracker = root.AddComponent(RuntimeType("Tracking.TagTracker"));
                Type commands = RuntimeType("RemoteControl.RemoteControlCommands");
                string prefix = "{\"type\":\"command\",\"commandId\":\"test\",\"commandType\":\"runner.presentation\",\"payload\":";
                object[] args = { prefix + "{\"presentationMode\":\"TreadmillRelative\"}}", tracker, null, null };
                Assert.That(commands.GetMethod("TryApply").Invoke(null, args), Is.False);
                args[0] = prefix + "{\"presentationMode\":\"TreadmillRelative\",\"sideOffsetMetres\":1.5,\"visibilityDistanceMetres\":10}}";
                Assert.That(commands.GetMethod("TryApply").Invoke(null, args), Is.True);
                Assert.That(Call(tracker, "TryCalibrateGhostPresentation"), Is.False);
                object session = tracker.GetType().GetField("_runSession", PrivateInstance).GetValue(tracker);
                Call(session, "TryStart", true);
                Assert.That(Call(tracker, "TryConfigureGhostPresentation", "Track", 1.5f, 10f), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }
    }
}
