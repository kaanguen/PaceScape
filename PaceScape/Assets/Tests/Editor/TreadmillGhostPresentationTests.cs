// Checks calibrated lane placement, visibility, and progress preservation.

using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PaceScape.Tests
{
    // Checks presentation-only overtaking, locked calibration and visibility without hardware.
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

        [TestCase(-1.5f)]
        [TestCase(1.5f)]
        public void SideBySideUsesTheCalibratedOriginAndSignedOffset(float offset)
        {
            object settings = Create("RunningTrack.TreadmillGhostPresentation");
            Assert.That(Call(settings, "TryConfigure", "Track", offset, 10f), Is.True);
            Pose lanePose = EvaluateLane(1, 0);
            Vector3 right = lanePose.rotation * Vector3.right;
            Vector3 origin = lanePose.position + 2f * right;
            Pose companion = (Pose)Call(settings, "EvaluateSideBySide", 1, origin);
            Assert.That(Vector3.Distance(companion.position, origin + offset * right), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(companion.rotation, lanePose.rotation), Is.LessThan(0.01f));
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
            object[] args = { lane, reference, Pose.identity, gap, Pose.identity };
            Assert.That(Call(settings, "Evaluate", args), Is.True);
            Pose pose = (Pose)args[4];
            Pose expected = EvaluateLane(lane, reference + gap);
            Assert.That(Vector3.Distance(pose.position, expected.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(pose.rotation, expected.rotation), Is.LessThan(0.01f));
        }

        [TestCase(5.0)]
        [TestCase(35.0)]
        [TestCase(435.0)]
        public void AdjacentLaneKeepsLateralSeparationAtTheSameTrackStation(double travel)
        {
            object settings = Create("RunningTrack.TreadmillGhostPresentation");
            MethodInfo evaluate = settings.GetType().GetMethod("EvaluateAdjacentLane", PrivateInstance);
            foreach (int lane in new[] { 1, 8 })
            {
                foreach (double gap in new[] { -4.0, 0.0, 4.0 })
                {
                    object[] args = { lane, travel, Pose.identity, gap, Pose.identity };
                    Assert.That(evaluate.Invoke(settings, args), Is.True);
                    Pose runner = EvaluateLane(lane, travel + gap);
                    Pose ghost = (Pose)args[4];
                    Vector3 separation = Quaternion.Inverse(runner.rotation) * (ghost.position - runner.position);
                    Assert.That(separation.x, Is.EqualTo(lane == 1 ? 1.22f : -1.22f).Within(0.001f));
                    Assert.That(separation.z, Is.EqualTo(0f).Within(0.001f));
                }
            }
        }

        [TestCase(5.0)]
        [TestCase(35.0)]
        [TestCase(435.0)]
        public void MovingTrackKeepsTheCurrentLaneUnderTheCalibration(double travel)
        {
            var origin = new Vector3(2, 0, 1);
            object[] args = { 1, origin, travel, 0.0 };
            Type type = RuntimeType("RunningTrack.TreadmillGhostPresentation");
            Pose track = (Pose)type.GetMethod("EvaluateTrackPose").Invoke(null, args);
            double currentMetres = (double)args[3];
            Assert.That(currentMetres, Is.EqualTo(1.0 + travel).Within(0.00001));
            Pose current = EvaluateLane(1, currentMetres);
            Assert.That(Vector3.Distance(track.position + track.rotation * current.position, origin), Is.LessThan(0.0001f));
            Assert.That(Vector3.Angle(track.rotation * current.rotation * Vector3.forward, Vector3.forward), Is.LessThan(0.01f));

            object[] ghost = { 1, currentMetres, track, 0.0, Pose.identity };
            Assert.That(Call(Create("RunningTrack.TreadmillGhostPresentation"), "Evaluate", ghost), Is.True);
            Assert.That(Vector3.Distance(((Pose)ghost[4]).position, origin), Is.LessThan(0.0001f));
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
            object[] args = { 1, 0.0, Pose.identity, gap, Pose.identity };
            Assert.That(Call(settings, "Evaluate", args), Is.EqualTo(expected));
            Assert.That(Mathf.Abs(((Pose)args[4]).position.z), Is.LessThanOrEqualTo(10));
        }

        [Test]
        public void ReverseCalibrationKeepsTheStartPointAndReversesHeadingAndProgress()
        {
            Type type = RuntimeType("RunningTrack.TreadmillGhostPresentation");
            var origin = new Vector3(2f, 0f, 1f);
            object[] trackArgs = { 1, origin, 30.0, true, 0.0 };
            Pose track = (Pose)type.GetMethod("EvaluateDirectedTrackPose", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, trackArgs);
            double laneMetres = (double)trackArgs[4];
            Assert.That(laneMetres, Is.EqualTo(-29.0).Within(0.0001));
            Pose lane = EvaluateLane(1, laneMetres);
            Assert.That(Vector3.Distance(track.position + track.rotation * lane.position, origin), Is.LessThan(0.0001f));

            object settings = Create("RunningTrack.TreadmillGhostPresentation");
            MethodInfo evaluate = type.GetMethod("EvaluateDirectedGhost", PrivateInstance);
            object[] ghostArgs = { 1, laneMetres, track, 5.0, false, true, true, Pose.identity };
            Assert.That(evaluate.Invoke(settings, ghostArgs), Is.True);
            Pose ghost = (Pose)ghostArgs[7];
            Assert.That(ghost.position.z, Is.EqualTo(origin.z - 5f).Within(0.0001f));
            Assert.That(Vector3.Angle(ghost.rotation * Vector3.forward, Vector3.back), Is.LessThan(0.01f));
        }

        [Test]
        public void ReplayStaysVisibleBeyondTheLegacyDistanceInEitherDirection()
        {
            object settings = Create("RunningTrack.TreadmillGhostPresentation");
            MethodInfo evaluate = settings.GetType().GetMethod("EvaluateReplay", PrivateInstance);
            foreach (double gap in new[] { -405.0, -120.0, 120.0, 405.0 })
            {
                object[] args = { 1, 0.0, Pose.identity, gap, false, Pose.identity };
                Assert.That(evaluate.Invoke(settings, args), Is.True);
                Assert.That(Vector3.Distance(((Pose)args[5]).position, EvaluateLane(1, gap).position), Is.LessThan(0.001f));
                args[4] = true;
                Assert.That(evaluate.Invoke(settings, args), Is.True);
                Assert.That(Vector3.Distance(((Pose)args[5]).position, EvaluateLane(1, gap).position),
                    Is.EqualTo(1.22f).Within(0.001f));
            }
        }

        [Test]
        public void InvalidSettingsAndCalibrationCannotReplaceValidValues()
        {
            object settings = Create("RunningTrack.TreadmillGhostPresentation");
            Assert.That(Call(settings, "TryConfigure", "TreadmillRelative", -2f, 8f), Is.True);
            Assert.That(Call(settings, "TryConfigure", "Other", 1.5f, 10f), Is.False);
            Assert.That(Call(settings, "TryConfigure", "Track", 0f, 10f), Is.False);
            Assert.That(Call(settings, "TryConfigure", "Track", 1f, float.NaN), Is.False);
            object[] evaluation = { 1, 0.0, Pose.identity, 9.0, Pose.identity };
            Assert.That(Call(settings, "Evaluate", evaluation), Is.False);
            Assert.That(((Pose)evaluation[4]).position.z, Is.EqualTo(8).Within(0.0001f));
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
