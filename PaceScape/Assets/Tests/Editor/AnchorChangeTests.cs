using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARSubsystems;

namespace PaceScape.Tests
{
    /// <summary>Exercises the embedded SDK's anchor completion batches without native device calls.</summary>
    public sealed class AnchorChangeTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        [TestCase(true)]
        [TestCase(false)]
        public void CompletionProducesOneValidTrackedChangeWhetherInitialAddWasReported(bool completesInFirstBatch)
        {
            Type subsystemType = Type.GetType("MagicLeap.OpenXR.Subsystems.MLXrAnchorSubsystem, MagicLeap.SDK", true);
            Type providerType = subsystemType.GetNestedType("MagicLeapProvider", BindingFlags.NonPublic);
            object provider = Activator.CreateInstance(providerType, true);
            var id = new TrackableId(0, 0xf52b75076e45ad88);
            var pending = new XRAnchor(id, Pose.identity, TrackingState.None, IntPtr.Zero);
            var completed = new XRAnchor(id, new Pose(Vector3.one, Quaternion.identity), TrackingState.Tracking, IntPtr.Zero);
            var additions = (List<XRAnchor>)providerType.GetField("addedAnchors", PrivateInstance).GetValue(provider);
            additions.Add(pending);
            var validator = new ValidationUtility<XRAnchor>();
            if (!completesInFirstBatch)
            {
                using (TrackableChanges<XRAnchor> initial = GetChanges(providerType, provider))
                {
                    validator.ValidateAndThrow(initial);
                    Assert.That(initial.added.Length, Is.EqualTo(1));
                    Assert.That(initial.added[0].trackingState, Is.EqualTo(TrackingState.None));
                }
            }

            MethodInfo complete = providerType.GetMethod("QueueCompletedAnchor", PrivateInstance);
            complete.Invoke(provider, new object[] { completed });
            // Repeated completion must not create a second change for the same ID.
            complete.Invoke(provider, new object[] { completed });
            using (TrackableChanges<XRAnchor> changes = GetChanges(providerType, provider))
            {
                Assert.DoesNotThrow(() => validator.ValidateAndThrow(changes));
                Assert.That(changes.added.Length, Is.EqualTo(completesInFirstBatch ? 1 : 0));
                Assert.That(changes.updated.Length, Is.EqualTo(completesInFirstBatch ? 0 : 1));
                XRAnchor reported = completesInFirstBatch ? changes.added[0] : changes.updated[0];
                Assert.That(reported.trackableId, Is.EqualTo(id));
                Assert.That(reported.trackingState, Is.EqualTo(TrackingState.Tracking));
                Assert.That(reported.pose.position, Is.EqualTo(Vector3.one));
            }

            using (TrackableChanges<XRAnchor> next = GetChanges(providerType, provider))
            {
                Assert.DoesNotThrow(() => validator.ValidateAndThrow(next));
                Assert.That(next.added.Length + next.updated.Length + next.removed.Length, Is.Zero);
            }
        }

        [Test]
        public void OriginalAddedAndUpdatedBatchReproducesReportedValidationError()
        {
            var id = new TrackableId(0, 0xf52b75076e45ad88);
            using (var changes = new TrackableChanges<XRAnchor>(1, 1, 0, Allocator.Temp))
            {
                NativeArray<XRAnchor> added = changes.added;
                NativeArray<XRAnchor> updated = changes.updated;
                added[0] = new XRAnchor(id, Pose.identity, TrackingState.None, IntPtr.Zero);
                updated[0] = new XRAnchor(id, Pose.identity, TrackingState.Tracking, IntPtr.Zero);
                var validator = new ValidationUtility<XRAnchor>();
                InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => validator.ValidateAndThrow(changes));
                StringAssert.Contains("being updated this frame has at least one other action associated with it", error.Message);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void RemovingPendingAnchorClearsFutureAndProducesAValidBatch(bool additionWasReported)
        {
            Type subsystem = Type.GetType("MagicLeap.OpenXR.Subsystems.MLXrAnchorSubsystem, MagicLeap.SDK", true);
            Type providerType = subsystem.GetNestedType("MagicLeapProvider", BindingFlags.NonPublic);
            object provider = Activator.CreateInstance(providerType, true);
            var id = new TrackableId(7, 0xf52b75076e45ad88);
            var anchor = new XRAnchor(id, Pose.identity, TrackingState.None, IntPtr.Zero);
            Type dataType = providerType.GetNestedType("AnchorSubsystemAnchorData", BindingFlags.NonPublic);
            object data = Activator.CreateInstance(dataType);
            dataType.GetField("AnchorObject").SetValue(data, anchor);
            dataType.GetField("AnchorFuture").SetValue(data, 42UL);
            var current = (IList)providerType.GetField("currentAnchors", PrivateInstance).GetValue(provider);
            current.Add(data);
            var added = (List<XRAnchor>)providerType.GetField("addedAnchors", PrivateInstance).GetValue(provider);
            added.Add(anchor);
            var futures = (HashSet<ulong>)providerType.GetField("pendingFutures", PrivateInstance).GetValue(provider);
            var ready = (HashSet<ulong>)providerType.GetField("readyFutures", PrivateInstance).GetValue(provider);
            futures.Add(42);
            ready.Add(42);
            var validator = new ValidationUtility<XRAnchor>();
            if (additionWasReported)
            {
                using (TrackableChanges<XRAnchor> initial = GetChanges(providerType, provider))
                {
                    validator.ValidateAndThrow(initial);
                }
            }

            // No native feature/session is attached: removal must still forget all managed state.
            Assert.That(providerType.GetMethod("TryRemoveAnchor").Invoke(provider, new object[] { id }), Is.True);
            Assert.That(current, Is.Empty);
            Assert.That(futures, Is.Empty);
            Assert.That(ready, Is.Empty);
            using (TrackableChanges<XRAnchor> changes = GetChanges(providerType, provider))
            {
                Assert.DoesNotThrow(() => validator.ValidateAndThrow(changes));
                Assert.That(changes.added.Length + changes.updated.Length, Is.Zero);
                Assert.That(changes.removed.Length, Is.EqualTo(additionWasReported ? 1 : 0));
            }
        }

        private static TrackableChanges<XRAnchor> GetChanges(Type providerType, object provider)
        {
            return (TrackableChanges<XRAnchor>)providerType.GetMethod("GetChanges")
                .Invoke(provider, new object[] { default(XRAnchor), Allocator.Temp });
        }
    }
}
