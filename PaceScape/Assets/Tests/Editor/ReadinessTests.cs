using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PaceScape.Tests
{
    /// <summary>Checks invalid configuration, runner lifetime, and shared resource ownership.</summary>
    public sealed class ReadinessTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(-1f)]
        public void InvalidStartDistanceIsRejectedBeforeChangingTheObject(float distance)
        {
            var root = new GameObject("Invalid start test");
            try
            {
                Type motion = RuntimeType("RunningTrack.RunningTrackMotion");
                TargetInvocationException error = Assert.Throws<TargetInvocationException>(() =>
                    Activator.CreateInstance(motion, root.transform, 1, distance));
                Assert.That(error.InnerException, Is.TypeOf<ArgumentOutOfRangeException>());
                Assert.That(root.transform.localPosition, Is.EqualTo(Vector3.zero));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCase("_spawnLane", 0f)]
        [TestCase("_spawnLane", 9f)]
        [TestCase("_paceMinutesPerKilometre", 0f)]
        [TestCase("_paceMinutesPerKilometre", float.NaN)]
        [TestCase("_distanceBehindStartLine", float.PositiveInfinity)]
        public void TrackerRejectsInvalidRunningConfiguration(string field, float value)
        {
            var root = new GameObject("Configuration test");
            root.SetActive(false);
            try
            {
                Type type = RuntimeType("Tracking.TagTracker");
                Component tracker = root.AddComponent(type);
                FieldInfo setting = type.GetField(field, PrivateInstance);
                if (setting.FieldType == typeof(int))
                {
                    setting.SetValue(tracker, (int)value);
                }
                else
                {
                    setting.SetValue(tracker, value);
                }

                object[] arguments = { null };
                Assert.That(type.GetMethod("ValidateRunningConfiguration", PrivateInstance).Invoke(tracker, arguments), Is.False);
                Assert.That(arguments[0], Is.Not.Null.And.Not.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SharedTrackResourcesSurviveOneOwnerAndReleaseAfterTheLastOwner()
        {
            var first = new GameObject("First track");
            var second = new GameObject("Second track");
            first.SetActive(false);
            second.SetActive(false);
            Type type = RuntimeType("RunningTrack.RunningTrackVisual");
            Component firstVisual = null;
            Component secondVisual = null;
            try
            {
                firstVisual = (Component)type.GetMethod("Create").Invoke(null, new object[] { first.transform });
                secondVisual = (Component)type.GetMethod("Create").Invoke(null, new object[] { second.transform });
                Mesh mesh = firstVisual.GetComponent<MeshFilter>().sharedMesh;
                Material material = firstVisual.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.That(secondVisual.GetComponent<MeshFilter>().sharedMesh, Is.SameAs(mesh));
                Assert.That(secondVisual.GetComponent<MeshRenderer>().sharedMaterial, Is.SameAs(material));
                type.GetMethod("ReleaseGeneratedResources").Invoke(firstVisual, null);
                type.GetMethod("ReleaseGeneratedResources").Invoke(firstVisual, null);
                Assert.That(mesh != null && material != null, Is.True);
                type.GetMethod("ReleaseGeneratedResources").Invoke(secondVisual, null);
                Assert.That(mesh == null && material == null, Is.True);
            }
            finally
            {
                if (firstVisual != null)
                {
                    type.GetMethod("ReleaseGeneratedResources").Invoke(firstVisual, null);
                }

                if (secondVisual != null)
                {
                    type.GetMethod("ReleaseGeneratedResources").Invoke(secondVisual, null);
                }
                UnityEngine.Object.DestroyImmediate(first);
                UnityEngine.Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void ExtremelyLargeFinitePaceStillMovesAndRemovedRunnerFailsExplicitly()
        {
            var root = new GameObject("Motion lifetime test");
            Type type = RuntimeType("RunningTrack.RunningTrackMotion");
            object motion = Activator.CreateInstance(type, root.transform, 1, 0f);
            MethodInfo advance = type.GetMethod("Advance");
            try
            {
                advance.Invoke(motion, new object[] { float.MaxValue, float.MaxValue });
                Assert.That(root.transform.localPosition.z, Is.EqualTo(1000f / 60f).Within(0.1f));
                UnityEngine.Object.DestroyImmediate(root);
                TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => advance.Invoke(motion, new object[] { 1f, 6f }));
                Assert.That(error.InnerException, Is.TypeOf<InvalidOperationException>());
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
        }

        private static Type RuntimeType(string name)
        {
            return Type.GetType("PaceScape." + name + ", Assembly-CSharp", true);
        }
    }
}
