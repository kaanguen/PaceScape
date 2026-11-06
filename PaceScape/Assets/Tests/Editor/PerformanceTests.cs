using System;
using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PaceScape.Tests
{
    /// <summary>Checks complete laps, continuous-window records and measured/demo separation without BLE.</summary>
    public sealed class PerformanceTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static Type RuntimeType(string name) => Type.GetType("PaceScape.Performance." + name + ", Assembly-CSharp", true);
        private static object Create(string name) => Activator.CreateInstance(RuntimeType(name));
        private static object Call(object target, string method, params object[] args) => target.GetType().GetMethod(method).Invoke(target, args);
        private static double Number(object target, string property) => Convert.ToDouble(target.GetType().GetProperty(property).GetValue(target));

        [Test]
        public void FixedLapsInterpolateCrossingsAndKeepSlowerFirstResult()
        {
            object laps = Create("DistancePerformance");
            Call(laps, "Advance", 200.0, 7.2); // 400 m at 2 m/s
            Assert.That(Number(laps, "CompletedLaps"), Is.EqualTo(1));
            Assert.That(Number(laps, "BestSeconds"), Is.EqualTo(200).Within(0.000001));
            Call(laps, "Advance", 150.0, 14.4); // Another 600 m, one full lap plus 200 m
            Assert.That(Number(laps, "CompletedLaps"), Is.EqualTo(2));
            Assert.That(Number(laps, "BestSeconds"), Is.EqualTo(100).Within(0.000001));
            Assert.That(Number(laps, "LapDistanceMetres"), Is.EqualTo(200).Within(0.000001));
            Assert.That(Number(laps, "TotalDistanceMetres"), Is.EqualTo(1000).Within(0.000001));
        }

        [Test]
        public void RollingRecordFindsTheFastSectionInsideALongerRun()
        {
            object best = Create("RollingDistanceBest");
            Call(best, "Reset", 400.0);
            Call(best, "Advance", 50.0, 7.2); // Slow 100 m
            Call(best, "Advance", 40.0, 36.0); // Fast 400 m, not aligned with fixed lap boundaries
            Call(best, "Advance", 50.0, 7.2);
            Assert.That(Number(best, "BestSeconds"), Is.EqualTo(40).Within(0.000001));
        }

        [Test]
        public void ZeroSpeedTimeCountsButUnknownIntervalsCannotBeBridged()
        {
            object best = Create("RollingDistanceBest");
            Call(best, "Reset", 100.0);
            Call(best, "Advance", 5.0, 36.0);
            Call(best, "Advance", 30.0, 0.0);
            Call(best, "Advance", 5.0, 36.0);
            Assert.That(Number(best, "BestSeconds"), Is.EqualTo(40).Within(0.000001));
            Call(best, "Reset", 100.0);
            Call(best, "Advance", 5.0, 36.0);
            Call(best, "Interrupt");
            Call(best, "Advance", 5.0, 36.0);
            Assert.That(Number(best, "BestSeconds"), Is.Zero);
            Call(best, "Advance", 5.0, 36.0);
            Assert.That(Number(best, "BestSeconds"), Is.EqualTo(10).Within(0.000001));
        }

        [Test]
        public void LeadingStandstillIsExcludedAndFrameSplitsDoNotChangeTheResult()
        {
            object coarse = Create("RollingDistanceBest");
            object fine = Create("RollingDistanceBest");
            foreach (object tracker in new[] { coarse, fine })
            {
                Call(tracker, "Reset", 100.0);
                Call(tracker, "Advance", 60.0, 0.0);
            }
            Call(coarse, "Advance", 10.0, 36.0);
            for (int i = 0; i < 100; i++)
            {
                Call(fine, "Advance", 0.1, 36.0);
            }
            Assert.That(Number(coarse, "BestSeconds"), Is.EqualTo(10).Within(0.000001));
            // Floating point distance can end just short; this extra fraction crosses the same boundary.
            Call(fine, "Advance", 0.00001, 36.0);
            Assert.That(Number(fine, "BestSeconds"), Is.EqualTo(10).Within(0.000001));
        }

        [Test]
        public void InterruptedFixedLapDoesNotBecomeARecord()
        {
            object laps = Create("DistancePerformance");
            Call(laps, "Advance", 50.0, 14.4);
            Call(laps, "Interrupt");
            Call(laps, "Advance", 50.0, 14.4);
            Assert.That(Number(laps, "BestSeconds"), Is.Zero);
            Call(laps, "Advance", 100.0, 14.4);
            Assert.That(Number(laps, "BestSeconds"), Is.EqualTo(100).Within(0.000001));
        }

        [Test]
        public void RecordsAreDistanceSpecificPersistableAndOnlyImprove()
        {
            object book = Create("PersonalBestBook");
            Assert.That(Call(book, "TryImprove", 5000, 1800.0, "test"), Is.True);
            Assert.That(Call(book, "TryImprove", 5000, 1900.0, "test"), Is.False);
            Assert.That(Call(book, "TryImprove", 5000, 1700.0, "test"), Is.True);
            Assert.That(Call(book, "TryImprove", 10000, 4000.0, "test"), Is.True);
            object restored = JsonUtility.FromJson(JsonUtility.ToJson(book), RuntimeType("PersonalBestBook"));
            Assert.That(Call(restored, "IsValid"), Is.True);
            Assert.That(Call(restored, "Find", 5000), Is.EqualTo(1700.0));
            Assert.That(Call(restored, "Find", 400), Is.EqualTo(0.0));
            Assert.That(Call(book, "TryImprove", 400, double.NaN, "test"), Is.False);
            IList records = (IList)book.GetType().GetField("records").GetValue(book);
            records.Add(records[0]);
            Assert.That(Call(book, "IsValid"), Is.False);
        }

        [Test]
        public void RecordedTraceSurvivesUnityJsonRoundTripAndLegacyJsonStillLoads()
        {
            const string json = "{\"version\":1,\"records\":[{\"distanceMetres\":100,\"seconds\":15,\"achievedUtc\":\"test\",\"trace\":[{\"seconds\":0,\"distanceMetres\":0},{\"seconds\":10,\"distanceMetres\":50},{\"seconds\":15,\"distanceMetres\":100}]}]}";
            object book = JsonUtility.FromJson(json, RuntimeType("PersonalBestBook"));
            Assert.That(Call(book, "IsValid"), Is.True);
            object restored = JsonUtility.FromJson(JsonUtility.ToJson(book), RuntimeType("PersonalBestBook"));
            object record = ((IList)restored.GetType().GetField("records").GetValue(restored))[0];
            Assert.That(Call(record, "HasValidTrace"), Is.True);
            object legacy = JsonUtility.FromJson("{\"version\":1,\"records\":[{\"distanceMetres\":100,\"seconds\":15}]}", RuntimeType("PersonalBestBook"));
            Assert.That(Call(legacy, "IsValid"), Is.True);
        }

        [Test]
        public void DemoFallbackNeverEntersMeasuredBookAndRealSlowerRecordWins()
        {
            var root = new GameObject("PR target selection test");
            Component performance = root.AddComponent(RuntimeType("RunnerPerformance"));
            try
            {
                Type mode = Type.GetType("PaceScape.RunningTrack.RunningSession+RunnerMode, Assembly-CSharp", true);
                object allTime = Enum.Parse(mode, "AllTimePersonalBest");
                Assert.That(Call(performance, "TryConfigure", 5000f, false), Is.True);
                Assert.That(Call(performance, "GetTargetPace", allTime, 6f), Is.EqualTo(6f));
                Assert.That(Call(performance, "TryConfigure", 5000f, true), Is.True);
                Assert.That(Call(performance, "GetTargetPace", allTime, 6f), Is.EqualTo(5f));
                object book = performance.GetType().GetField("_book", PrivateInstance).GetValue(performance);
                Assert.That(Call(book, "Find", 5000), Is.EqualTo(0.0));
                Call(book, "TryImprove", 5000, 1800.0, "test");
                Assert.That(Call(performance, "GetTargetPace", allTime, 4f), Is.EqualTo(6f));
                Call(performance, "ResetSession");
                Assert.That(Call(book, "Find", 5000), Is.EqualTo(1800.0));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void FixedGhostIgnoresCurrentRunBestAndKeepsItsPreset()
        {
            var root = new GameObject("Session ghost target test");
            Component performance = root.AddComponent(RuntimeType("RunnerPerformance"));
            try
            {
                Type mode = Type.GetType("PaceScape.RunningTrack.RunningSession+RunnerMode, Assembly-CSharp", true);
                object selected = Enum.Parse(mode, "FixedPace");
                Assert.That(Call(performance, "GetTargetPace", selected, 4f), Is.EqualTo(4f));
                object efforts = performance.GetType().GetField("_efforts", PrivateInstance).GetValue(performance);
                Call(efforts, "Advance", 200.0, 7.2);
                Assert.That(Call(performance, "GetTargetPace", selected, 4f), Is.EqualTo(4f));
                Call(efforts, "Advance", 100.0, 14.4);
                Assert.That(Call(performance, "GetTargetPace", selected, 4f), Is.EqualTo(4f));
                Call(performance, "ResetSession");
                Assert.That(Call(performance, "GetTargetPace", selected, 4f), Is.EqualTo(4f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ContinuousWindowMatchesIndependentTimeGridAcrossManySpeedChangesAndPruning()
        {
            const int count = 2500;
            const double step = 0.1;
            const double length = 20;
            var distances = new double[count + 1];
            var random = new System.Random(17);
            object best = Create("RollingDistanceBest");
            Call(best, "Reset", length);
            for (int i = 0; i < count; i++)
            {
                double speed = 1 + random.Next(1, 10);
                distances[i + 1] = distances[i] + speed * step;
                Call(best, "Advance", step, speed * 3.6);
            }
            double expected = double.PositiveInfinity;
            for (double endTime = step; endTime < count * step; endTime += 0.01)
            {
                double endIndex = endTime / step;
                int segment = Math.Min(count - 1, (int)endIndex);
                double endDistance = distances[segment] + (endIndex - segment) * (distances[segment + 1] - distances[segment]);
                if (endDistance < length)
                {
                    continue;
                }
                double startDistance = endDistance - length;
                int low = 0;
                int high = count;
                while (high - low > 1)
                {
                    int middle = (low + high) / 2;
                    if (distances[middle] <= startDistance)
                    {
                        low = middle;
                    }
                    else
                    {
                        high = middle;
                    }
                }
                double startTime = (low + (startDistance - distances[low]) / (distances[low + 1] - distances[low])) * step;
                expected = Math.Min(expected, endTime - startTime);
            }
            Assert.That(Number(best, "BestSeconds"), Is.EqualTo(expected).Within(0.025));
        }

        [Test]
        public void TelemetryRemainsBoundedAndIncludesTheSelectedDistance()
        {
            var root = new GameObject("Bounded PR telemetry test");
            Component performance = root.AddComponent(RuntimeType("RunnerPerformance"));
            try
            {
                object book = performance.GetType().GetField("_book", PrivateInstance).GetValue(performance);
                for (int distance = 1; distance <= 1024; distance++)
                {
                    Call(book, "TryImprove", distance, 100.0, "2026-11-05T00:00:00Z");
                }
                object laps = performance.GetType().GetField("_laps", PrivateInstance).GetValue(performance);
                Call(laps, "Advance", 10000.0, 14.4);
                object snapshot = performance.GetType().GetMethod("CaptureState", PrivateInstance).Invoke(performance, null);
                Array records = (Array)snapshot.GetType().GetField("records").GetValue(snapshot);
                Array history = (Array)snapshot.GetType().GetField("laps").GetValue(snapshot);
                Assert.That(records.Length, Is.EqualTo(32));
                Assert.That(history.Length, Is.EqualTo(20));
                bool selectedIncluded = false;
                foreach (object record in records)
                {
                    selectedIncluded |= (int)record.GetType().GetField("distanceMetres").GetValue(record) == 400;
                }
                Assert.That(selectedIncluded, Is.True);
                Assert.That(System.Text.Encoding.UTF8.GetByteCount(JsonUtility.ToJson(snapshot)), Is.LessThan(12000));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RecordFileReplacementCreatesRecoverablePreviousSnapshot()
        {
            string directory = Path.Combine(Path.GetTempPath(), "pacescape-record-test-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(directory, "records.json");
            MethodInfo save = RuntimeType("RunnerPerformance").GetMethod("Save", BindingFlags.NonPublic | BindingFlags.Static);
            try
            {
                save.Invoke(null, new object[] { path, "first", false });
                save.Invoke(null, new object[] { path, "second", true });
                Assert.That(File.ReadAllText(path), Is.EqualTo("second"));
                Assert.That(File.ReadAllText(path + ".bak"), Is.EqualTo("first"));
                Assert.That(File.Exists(path + ".tmp"), Is.False);
                object book = Create("PersonalBestBook");
                Call(book, "TryImprove", 5000, 1500.0, "test");
                save.Invoke(null, new object[] { path, JsonUtility.ToJson(book), true });
                object loaded = RuntimeType("RunnerPerformance").GetMethod("Read", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { path });
                Assert.That(Call(loaded, "Find", 5000), Is.EqualTo(1500.0));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, true);
                }
            }
        }
    }
}
