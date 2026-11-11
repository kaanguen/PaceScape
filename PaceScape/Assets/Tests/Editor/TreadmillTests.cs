// Checks FTMS parsing, speed freshness, and the ghost's independence from belt input.

using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PaceScape.Tests
{
    // Feeds synthetic FTMS notifications through the reused decoder and runner speed source.
    public sealed class TreadmillTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject _root;
        private Component _source;
        private Component _connection;
        private Component _measurements;
        private object _channelValue;
        private string _channelKey;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("FTMS notification tests");
            _source = _root.AddComponent(RuntimeType("Treadmill.TreadmillInput"));
            if (Field(_source, "_measurements") == null)
            {
                _source.GetType().GetMethod("Awake", PrivateInstance).Invoke(_source, null);
            }
            _connection = (Component)Field(_source, "_connection");
            _measurements = (Component)Field(_source, "_measurements");
            SetField(_connection, "<Connected>k__BackingField", true);

            Type channelType = BleType("BleChannel");
            object channel = Activator.CreateInstance(channelType);
            channelType.GetField("format").SetValue(channel, Enum.Parse(BleType("BleValueFormat"), "FtmsTreadmillData"));
            channelType.GetField("staleSeconds").SetValue(channel, 2f);
            _channelValue = Activator.CreateInstance(_measurements.GetType().GetNestedType("Value", BindingFlags.NonPublic), true);
            _channelValue.GetType().GetField("Channel").SetValue(_channelValue, channel);
            _channelKey = (string)BleType("BleUuid").GetMethod("Key").Invoke(null, new object[] { "1826", "2acd" });
            ((IDictionary)Field(_measurements, "byChannel")).Add(_channelKey, _channelValue);
            ((IList)Field(_measurements, "ordered")).Add(_channelValue);
        }

        [TearDown]
        public void TearDown()
        {
            // The production owner destroys its profile asynchronously; EditMode requires immediate cleanup.
            UnityEngine.Object profile = (UnityEngine.Object)Field(_source, "_profile");
            SetField(_source, "_profile", null);
            UnityEngine.Object.DestroyImmediate(_root);
            UnityEngine.Object.DestroyImmediate(profile);
        }

        [Test]
        public void BeltReadoutsChangeWhileIndependentGhostKeepsItsConfiguredPace()
        {
            Receive(new byte[] { 0, 0, 0xE8, 3 }); // 10 km/h
            Assert.That(Speed(), Is.EqualTo(10.0));
            Receive(new byte[] { 0, 0, 0xB0, 4 }); // 12 km/h
            Assert.That(Speed(), Is.EqualTo(12.0));
            var runner = new GameObject("FTMS runner");
            runner.transform.SetParent(_root.transform);
            object motion = Activator.CreateInstance(RuntimeType("RunningTrack.RunningTrackMotion"), runner.transform, 1, 0.5f);
            MethodInfo advance = motion.GetType().GetMethod("Advance");
            advance.Invoke(motion, new object[] { 1f, 6f });
            Vector3 movingPosition = runner.transform.localPosition;
            Receive(new byte[] { 0, 0, 0, 0 });
            object[] speedArguments = { 1.0 };
            Assert.That(_source.GetType().GetMethod("TryGetSpeedKmh").Invoke(_source, speedArguments), Is.True);
            Assert.That(speedArguments[0], Is.EqualTo(0.0));
            advance.Invoke(motion, new object[] { 1f, 6f });
            Assert.That(Vector3.Distance(runner.transform.localPosition, movingPosition), Is.GreaterThan(2f));
        }

        [Test]
        public void DistanceOnlyPacketDoesNotRefreshSpeedAndDisconnectInvalidatesMeasurement()
        {
            Receive(new byte[] { 0, 0, 0xE8, 3 });
            _channelValue.GetType().GetField("At").SetValue(_channelValue, Time.realtimeSinceStartup - 3f);
            Receive(new byte[] { 5, 0, 0xD0, 0x11, 1 }); // More Data + UINT24 distance 70096 m
            Assert.That(Speed(), Is.Null);
            object[] distanceArguments = { 0u };
            Assert.That(_measurements.GetType().GetMethod("TryGetTreadmillTotalDistanceMetres").Invoke(_measurements, distanceArguments), Is.True);
            Assert.That(distanceArguments[0], Is.EqualTo(70096u));
            Receive(new byte[] { 0, 0, 0xE8, 3 });
            SetField(_connection, "<Connected>k__BackingField", false);
            Assert.That(Speed(), Is.Null);

        }

        [Test]
        public void SideBySideUsesFreshBeltSpeedAndPausesWhenItExpires()
        {
            var trackerRoot = new GameObject("Side By Side tracking gate");
            trackerRoot.SetActive(false);
            trackerRoot.transform.SetParent(_root.transform);
            Component tracker = trackerRoot.AddComponent(RuntimeType("Tracking.TagTracker"));
            Component performance = trackerRoot.AddComponent(RuntimeType("Performance.RunnerPerformance"));
            SetField(performance, "_input", _source);
            tracker.GetType().GetProperty("PerformanceSource").SetValue(tracker, performance);
            Assert.That(tracker.GetType().GetMethod("TrySetRunnerMode").Invoke(tracker, new object[] { "SideBySide" }), Is.True);

            object session = Field(tracker, "_runSession");
            PropertyInfo activePace = tracker.GetType().GetProperty("ActivePaceMinutesPerKilometre");
            PropertyInfo runState = tracker.GetType().GetProperty("RunState");
            MethodInfo update = tracker.GetType().GetMethod("UpdateVisuals", PrivateInstance);
            session.GetType().GetMethod("TryStart").Invoke(session, new object[] { true });

            Receive(new byte[] { 0, 0, 0xB0, 4 }); // 12 km/h
            Assert.That(activePace.GetValue(tracker), Is.EqualTo(5f).Within(0.001f));
            Receive(new byte[] { 0, 0, 0, 0 }); // Fresh standstill
            update.Invoke(tracker, null);
            Assert.That(activePace.GetValue(tracker), Is.EqualTo(0f));
            Assert.That(runState.GetValue(tracker).ToString(), Is.EqualTo("Running"));

            _channelValue.GetType().GetField("At").SetValue(_channelValue, Time.realtimeSinceStartup - 3f);
            update.Invoke(tracker, null);
            Assert.That(runState.GetValue(tracker).ToString(), Is.EqualTo("Paused"));
            Receive(new byte[] { 0, 0, 0xE8, 3 });
            update.Invoke(tracker, null);
            Assert.That(runState.GetValue(tracker).ToString(), Is.EqualTo("Paused"));
        }

        [Test]
        public void MissingBeltSignalDoesNotPauseTheGhost()
        {
            var trackerRoot = new GameObject("FTMS tracking gate");
            trackerRoot.SetActive(false);
            trackerRoot.transform.SetParent(_root.transform);
            Component tracker = trackerRoot.AddComponent(RuntimeType("Tracking.TagTracker"));
            object session = Field(tracker, "_runSession");
            session.GetType().GetMethod("TryStart").Invoke(session, new object[] { true });
            tracker.GetType().GetMethod("UpdateVisuals", PrivateInstance).Invoke(tracker, null);
            Assert.That(tracker.GetType().GetProperty("RunState").GetValue(tracker).ToString(), Is.EqualTo("Running"));
            Receive(new byte[] { 0, 0, 0xE8, 3 });
            tracker.GetType().GetMethod("UpdateVisuals", PrivateInstance).Invoke(tracker, null);
            Assert.That(tracker.GetType().GetProperty("RunState").GetValue(tracker).ToString(), Is.EqualTo("Running"));
        }

        [Test]
        public void FixedGhostIsIndependentOfBeltSpeedAndRejectsRetiredModes()
        {
            var trackerRoot = new GameObject("Ghost pace test");
            trackerRoot.SetActive(false);
            trackerRoot.transform.SetParent(_root.transform);
            Component tracker = trackerRoot.AddComponent(RuntimeType("Tracking.TagTracker"));
            Type type = tracker.GetType();
            Assert.That(type.GetProperty("Mode").GetValue(tracker).ToString(), Is.EqualTo("FixedPace"));
            Receive(new byte[] { 0, 0, 0xB0, 4 });
            Assert.That(type.GetProperty("ActivePaceMinutesPerKilometre").GetValue(tracker), Is.EqualTo(6f));
            Receive(new byte[] { 0, 0, 0, 0 });
            Assert.That(type.GetProperty("ActivePaceMinutesPerKilometre").GetValue(tracker), Is.EqualTo(6f));
            SetField(_connection, "<Connected>k__BackingField", false);
            Assert.That(type.GetProperty("ActivePaceMinutesPerKilometre").GetValue(tracker), Is.EqualTo(6f));
            type.GetMethod("TrySetPace").Invoke(tracker, new object[] { 4f });
            Assert.That(type.GetProperty("ActivePaceMinutesPerKilometre").GetValue(tracker), Is.EqualTo(4f));
            foreach (string retired in new[] { "FollowInput", "TempoRunner" })
            {
                Assert.That(type.GetMethod("TrySetRunnerMode").Invoke(tracker, new object[] { retired }), Is.False);
            }
        }

        [Test]
        public void AverageSpeedPrecedesUint24DistanceAndTruncatedDistanceIsRejected()
        {
            MethodInfo read = BleType("FtmsTreadmillData").GetMethod("Read");
            object[] arguments = { new byte[] { 6, 0, 0xE8, 3, 0x20, 3, 0xD0, 0x11, 1 }, null, null };
            read.Invoke(null, arguments);
            Assert.That(arguments[1], Is.EqualTo(10.0));
            Assert.That(arguments[2], Is.EqualTo(70096u));
            arguments[0] = new byte[] { 4, 0, 0xE8, 3, 1 };
            TargetInvocationException exception = Assert.Throws<TargetInvocationException>(() => read.Invoke(null, arguments));
            Assert.That(exception.InnerException, Is.TypeOf<FormatException>());
        }

        private double? Speed()
        {
            object[] arguments = { 0.0 };
            bool valid = (bool)_source.GetType().GetMethod("TryGetSpeedKmh").Invoke(_source, arguments);
            return valid ? (double?)arguments[0] : null;
        }

        private void Receive(byte[] packet)
        {
            _measurements.GetType().GetMethod("OnData", PrivateInstance).Invoke(_measurements,
                new object[] { "1826", "2acd", packet });
        }

        private static object Field(object target, string name)
        {
            return target.GetType().GetField(name, PrivateInstance).GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            target.GetType().GetField(name, PrivateInstance).SetValue(target, value);
        }

        private static Type RuntimeType(string name)
        {
            return Type.GetType("PaceScape." + name + ", Assembly-CSharp", true);
        }

        private static Type BleType(string name)
        {
            return Type.GetType(name + ", Kaan.BleSensors.Runtime", true);
        }
    }
}
