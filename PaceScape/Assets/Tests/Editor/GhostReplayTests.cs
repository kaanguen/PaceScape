using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;

namespace PaceScape.Tests
{
    /// <summary>Checks exact trace extraction and chained replay independently of Unity, BLE and frame rate.</summary>
    public sealed class GhostReplayTests
    {
        private static Type RuntimeType(string name) => Type.GetType("PaceScape.Performance." + name + ", Assembly-CSharp", true);
        private static object Create(string name) => Activator.CreateInstance(RuntimeType(name));
        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name).Invoke(target, args);
        private static double Number(object target, string name) => Convert.ToDouble(target.GetType().GetProperty(name).GetValue(target));
        private static object Field(object target, string name) => target.GetType().GetField(name).GetValue(target);

        private static IList Trace(params double[] boundaries)
        {
            var result = (IList)Activator.CreateInstance(typeof(System.Collections.Generic.List<>).MakeGenericType(RuntimeType("EffortPoint")));
            for (int i = 0; i < boundaries.Length; i += 2)
            {
                object point = Create("EffortPoint");
                point.GetType().GetField("seconds").SetValue(point, boundaries[i]);
                point.GetType().GetField("distanceMetres").SetValue(point, boundaries[i + 1]);
                result.Add(point);
            }
            return result;
        }

        private static object References()
        {
            object book = Create("PersonalBestBook");
            Assert.That(Call(book, "TryImproveWithTrace", 100, 15.0, "short", Trace(0, 0, 10, 50, 15, 100)), Is.True);
            Assert.That(Call(book, "TryImproveWithTrace", 200, 40.0, "long", Trace(0, 0, 25, 100, 35, 150, 40, 200)), Is.True);
            return book;
        }

        private static object Replay(object book, int distance = 100, double start = 0)
        {
            object replay = Create("GhostReplay");
            Call(replay, "Reset", book, distance, 2.0, start);
            return replay;
        }

        [Test]
        public void LongerReferenceStartsAtCurrentDistanceRatherThanItsBeginning()
        {
            object replay = Replay(References());
            Assert.That(Call(replay, "Advance", 10.0), Is.EqualTo(50.0));
            Assert.That(Number(replay, "SpeedMetresPerSecond"), Is.EqualTo(10));
            Call(replay, "Advance", 5.0);
            Assert.That(Number(replay, "ReferenceDistanceMetres"), Is.EqualTo(200));
            Assert.That(Number(replay, "DistanceMetres"), Is.EqualTo(100));
            Assert.That(Number(replay, "SpeedMetresPerSecond"), Is.EqualTo(5));
            Assert.That(Call(replay, "Advance", 10.0), Is.EqualTo(50.0));
            Assert.That(Number(replay, "SpeedMetresPerSecond"), Is.EqualTo(10));
        }

        [Test]
        public void MissingLongerRecordRetainsFinalSegmentSpeedInsteadOfAverage()
        {
            object replay = Replay(References());
            Call(replay, "Advance", 30.0);
            Assert.That(replay.GetType().GetProperty("IsHoldingSpeed").GetValue(replay), Is.True);
            Assert.That(Number(replay, "DistanceMetres"), Is.EqualTo(200));
            Assert.That(Number(replay, "SpeedMetresPerSecond"), Is.EqualTo(10));
            Assert.That(Call(replay, "Advance", 5.0), Is.EqualTo(50.0));
        }

        [Test]
        public void MultipleReferencesAndFrameSplitsGiveTheSameTravel()
        {
            object book = References();
            Call(book, "TryImprove", 300, 60.0, "legacy");
            object coarse = Replay(book);
            object fine = Replay(book);
            Call(coarse, "Advance", 53.0);
            for (int i = 0; i < 5300; i++)
            {
                Call(fine, "Advance", 0.01);
            }
            Assert.That(Number(coarse, "DistanceMetres"), Is.EqualTo(315).Within(0.000001));
            Assert.That(Number(fine, "DistanceMetres"), Is.EqualTo(Number(coarse, "DistanceMetres")).Within(0.000001));
        }

        [Test]
        public void NewRecordsCannotReplaceTheActiveReplaySnapshot()
        {
            object book = References();
            object replay = Replay(book);
            Call(book, "TryImprove", 100, 1.0, "faster");
            Call(book, "TryImprove", 200, 1.0, "faster");
            Call(replay, "Advance", 25.0);
            Assert.That(Number(replay, "DistanceMetres"), Is.EqualTo(150));
        }

        [Test]
        public void ModeChangeSeekingDoesNotRewindProgress()
        {
            object replay = Replay(References(), 100, 125);
            Assert.That(Number(replay, "ReferenceDistanceMetres"), Is.EqualTo(200));
            Assert.That(Call(replay, "Advance", 5.0), Is.EqualTo(25.0));
            Assert.That(Number(replay, "DistanceMetres"), Is.EqualTo(150));
            replay = Replay(References(), 100, 250);
            Assert.That(Number(replay, "SpeedMetresPerSecond"), Is.EqualTo(10));
            Assert.That(Number(replay, "DistanceMetres"), Is.EqualTo(250));
        }

        [Test]
        public void LeadingAndInteriorStationaryIntervalsAreReplayed()
        {
            object book = Create("PersonalBestBook");
            Call(book, "TryImproveWithTrace", 10, 15.0, "stop", Trace(0, 0, 5, 0, 10, 5, 12, 5, 15, 10));
            object replay = Replay(book, 10);
            Assert.That(Call(replay, "Advance", 3.0), Is.EqualTo(0.0));
            Assert.That(Call(replay, "Advance", 8.0), Is.EqualTo(5.0));
            Assert.That(Number(replay, "SpeedMetresPerSecond"), Is.Zero);
            Call(replay, "Advance", 4.0);
            Assert.That(Number(replay, "DistanceMetres"), Is.EqualTo(10).Within(0.000001));
        }

        [Test]
        public void FallbackAndLegacyRecordsRemainPlayable()
        {
            object replay = Replay(Create("PersonalBestBook"), 100);
            Assert.That(Call(replay, "Advance", 55.0), Is.EqualTo(110.0));
            object book = Create("PersonalBestBook");
            Call(book, "TryImprove", 100, 10.0, "legacy");
            replay = Replay(book);
            Assert.That(Call(replay, "Advance", 5.0), Is.EqualTo(50.0));
            Assert.That(Call(replay, "Advance", double.NaN), Is.EqualTo(0.0));
        }

        [Test]
        public void TraceOwnershipAndValidationProtectStoredRecords()
        {
            object book = Create("PersonalBestBook");
            IList trace = Trace(0, 0, 5, 50, 10, 100);
            Assert.That(Call(book, "TryImproveWithTrace", 100, 10.0, "measured", trace), Is.True);
            trace[1].GetType().GetField("seconds").SetValue(trace[1], double.NaN);
            Assert.That(Call(book, "IsValid"), Is.True);
            Assert.That(Call(book, "TryImproveWithTrace", 100, 9.0, "bad", trace), Is.False);
            Assert.That(Call(book, "Find", 100), Is.EqualTo(10.0));
            object record = ((IList)Field(book, "records"))[0];
            IList stored = (IList)Field(record, "trace");
            stored[1].GetType().GetField("distanceMetres").SetValue(stored[1], -1.0);
            Assert.That(Call(book, "IsValid"), Is.False);
        }

        [Test]
        public void RollingTraceRetainsStopsAndDoesNotBridgeUnknownIntervals()
        {
            object rolling = Create("RollingDistanceBest");
            Call(rolling, "Reset", 100.0);
            IList trace = null;
            Action<double, double> capture = (distance, seconds) => trace = (IList)Call(rolling, "CopyImprovedTrace");
            rolling.GetType().GetEvent("Improved").AddEventHandler(rolling, capture);
            Call(rolling, "Advance", 5.0, 36.0);
            Call(rolling, "Advance", 30.0, 0.0);
            Call(rolling, "Advance", 5.0, 36.0);
            Assert.That(trace.Count, Is.EqualTo(4));
            Assert.That(Field(trace[1], "distanceMetres"), Is.EqualTo(50.0));
            Assert.That(Field(trace[2], "distanceMetres"), Is.EqualTo(50.0));
            Assert.That(Field(trace[3], "seconds"), Is.EqualTo(40.0));
            trace = null;
            Call(rolling, "Interrupt");
            Call(rolling, "Advance", 5.0, 36.0);
            Assert.That(trace, Is.Null);
        }

        [Test]
        public void RollingTraceClipsTheWinningWindowInsideAFrame()
        {
            object rolling = Create("RollingDistanceBest");
            Call(rolling, "Reset", 100.0);
            IList trace = null;
            Action<double, double> capture = (distance, seconds) => trace = (IList)Call(rolling, "CopyImprovedTrace");
            rolling.GetType().GetEvent("Improved").AddEventHandler(rolling, capture);
            Call(rolling, "Advance", 5.0, 7.2);
            Call(rolling, "Advance", 20.0, 36.0);
            Assert.That(trace.Count, Is.EqualTo(2));
            Assert.That(Field(trace[0], "seconds"), Is.EqualTo(0.0));
            Assert.That(Field(trace[1], "seconds"), Is.EqualTo(10.0).Within(0.000001));
            Assert.That(Field(trace[1], "distanceMetres"), Is.EqualTo(100.0));
        }
    }
}
