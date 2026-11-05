using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PaceScape.Tests
{
    /// <summary>Feeds synthetic FTMS notifications through the reused decoder and runner speed source.</summary>
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
            SetField(_source, "<FollowTreadmill>k__BackingField", true);

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
        public void BeltSpeedChangesSetRunnerPaceAndZeroStopsPhysicalMotion()
        {
            Receive(new byte[] { 0, 0, 0xE8, 3 }); // 10 km/h
            Assert.That(Pace(), Is.EqualTo(6f));
            Receive(new byte[] { 0, 0, 0xB0, 4 }); // 12 km/h
            Assert.That(Pace(), Is.EqualTo(5f));
            var runner = new GameObject("FTMS runner");
            runner.transform.SetParent(_root.transform);
            object motion = Activator.CreateInstance(RuntimeType("RunningTrack.RunningTrackMotion"), runner.transform, 1, 0.5f);
            MethodInfo advance = motion.GetType().GetMethod("Advance");
            advance.Invoke(motion, new object[] { 1f, Pace() });
            Vector3 movingPosition = runner.transform.localPosition;
            Receive(new byte[] { 0, 0, 0, 0 });
            object[] speedArguments = { 1.0 };
            Assert.That(_source.GetType().GetMethod("TryGetSpeedKmh").Invoke(_source, speedArguments), Is.True);
            Assert.That(speedArguments[0], Is.EqualTo(0.0));
            advance.Invoke(motion, new object[] { 1f, Pace() });
            Assert.That(runner.transform.localPosition, Is.EqualTo(movingPosition));
        }

        [Test]
        public void DistanceOnlyPacketDoesNotRefreshSpeedAndDisconnectNeverFallsBackToManualPace()
        {
            Receive(new byte[] { 0, 0, 0xE8, 3 });
            _channelValue.GetType().GetField("At").SetValue(_channelValue, Time.realtimeSinceStartup - 3f);
            Receive(new byte[] { 5, 0, 0xD0, 0x11, 1 }); // More Data + UINT24 distance 70096 m
            Assert.That(Pace(), Is.Zero);
            object[] distanceArguments = { 0u };
            Assert.That(_measurements.GetType().GetMethod("TryGetTreadmillTotalDistanceMetres").Invoke(_measurements, distanceArguments), Is.True);
            Assert.That(distanceArguments[0], Is.EqualTo(70096u));
            Receive(new byte[] { 0, 0, 0xE8, 3 });
            SetField(_connection, "<Connected>k__BackingField", false);
            Assert.That(Pace(), Is.Zero);
            SetField(_source, "<FollowTreadmill>k__BackingField", false);
            Assert.That(Pace(), Is.EqualTo(4f));
        }

        [Test]
        public void MissingSignalPausesSessionAndFreshPacketsDoNotAutomaticallyResumeIt()
        {
            var trackerRoot = new GameObject("FTMS tracking gate");
            trackerRoot.SetActive(false);
            trackerRoot.transform.SetParent(_root.transform);
            Component tracker = trackerRoot.AddComponent(RuntimeType("Tracking.TagTracker"));
            tracker.GetType().GetProperty("TreadmillSource").SetValue(tracker, _source);
            object session = Field(tracker, "_runSession");
            session.GetType().GetMethod("TryStart").Invoke(session, new object[] { true });
            tracker.GetType().GetMethod("UpdateVisuals", PrivateInstance).Invoke(tracker, null);
            Assert.That(tracker.GetType().GetProperty("RunState").GetValue(tracker).ToString(), Is.EqualTo("Paused"));
            Receive(new byte[] { 0, 0, 0xE8, 3 });
            tracker.GetType().GetMethod("UpdateVisuals", PrivateInstance).Invoke(tracker, null);
            Assert.That(tracker.GetType().GetProperty("RunState").GetValue(tracker).ToString(), Is.EqualTo("Paused"));
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

        private float Pace()
        {
            return (float)_source.GetType().GetMethod("GetRunnerPace").Invoke(_source, new object[] { 4f });
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
