using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PaceScape.Tests
{
    /// <summary>Checks oval geometry and the marker object's shared placement and visibility lifecycle.</summary>
    public sealed class RunningTrackTests
    {
        private Type _geometryType;

        [SetUp]
        public void SetUp()
        {
            _geometryType = Type.GetType("PaceScape.RunningTrack.RunningTrackGeometry, Assembly-CSharp", true);
        }

        [Test]
        public void MarkerIsOnLaneOneAndFacesAlongInitialStraight()
        {
            Assert.That(Evaluate(0f).magnitude, Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(Evaluate(1f), Vector3.forward), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(Evaluate(0f, 1.22f), Vector3.right * 1.22f), Is.LessThan(0.0001f));
        }

        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(8)]
        public void LaneEdgesCloseWithoutGaps(int edge)
        {
            double length = (double)_geometryType.GetField("LapLength").GetRawConstantValue();
            float offset = edge * 1.22f - 0.3f;
            Assert.That(Vector3.Distance(Evaluate(0, offset), Evaluate(length, offset)), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(Evaluate(-0.001, offset), Evaluate(0.001, offset)), Is.LessThan(0.003f));
            double measuredLength = 0;
            Vector3 previous = Evaluate(0, offset);
            for (int segment = 1; segment <= 4096; segment++)
            {
                Vector3 next = Evaluate(length * segment / 4096, offset);
                measuredLength += Vector3.Distance(previous, next);
                previous = next;
            }

            // Offset oval boundaries have the same straights and a larger or smaller combined circle.
            Assert.That(measuredLength, Is.EqualTo(length + 2 * Math.PI * offset).Within(0.02));
        }

        [TestCase(0.5f, 1)]
        [TestCase(1f, 8)]
        public void MarkerAndTrackShareAnchorVisibilityAndReleaseGeneratedResources(float distanceBehindStartLine, int lane)
        {
            var root = new GameObject("Running track test");
            root.SetActive(false);
            Mesh mesh = null;
            Material material = null;
            try
            {
                Type trackerType = Type.GetType("PaceScape.Tracking.TagTracker, Assembly-CSharp", true);
                Component tracker = root.AddComponent(trackerType);
                trackerType.GetField("_spawnLane", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(tracker, lane);
                trackerType.GetField("_distanceBehindStartLine", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(tracker, distanceBehindStartLine);
                var prefab = GameObject.CreatePrimitive(PrimitiveType.Cube);
                prefab.transform.SetParent(root.transform, false);
                prefab.transform.localScale = Vector3.one * 0.1f;
                trackerType.GetField("_markerPrefab", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(tracker, prefab);
                var anchor = new GameObject("Anchor");
                anchor.transform.SetParent(root.transform, false);
                anchor.transform.SetPositionAndRotation(new Vector3(3f, -1f, 7f), Quaternion.Euler(0f, 42f, 0f));
                object visual = trackerType.GetMethod("PopulateVisual", BindingFlags.NonPublic | BindingFlags.Instance)
                    .Invoke(tracker, new object[] { anchor, 39UL });
                var content = (GameObject)visual.GetType().GetField("Instance").GetValue(visual);
                Assert.That(content.transform.parent, Is.EqualTo(anchor.transform));
                Assert.That(content.activeSelf, Is.False);
                Assert.That(content.transform.childCount, Is.EqualTo(2));
                Transform marker = content.transform.GetChild(0);
                Transform track = content.transform.GetChild(1);
                Assert.That(marker.localPosition.y, Is.EqualTo(0.05f).Within(0.0001f));
                Assert.That(marker.localPosition.x, Is.EqualTo((lane - 0.5f) * 1.22f - 0.3f).Within(0.0001f));
                Assert.That(marker.localPosition.z, Is.EqualTo(-distanceBehindStartLine).Within(0.0001f));
                Vector3 expectedPosition = anchor.transform.TransformPoint(new Vector3((lane - 0.5f) * 1.22f - 0.3f, 0.05f, -distanceBehindStartLine));
                Assert.That(Vector3.Distance(marker.position, expectedPosition), Is.LessThan(0.0001f));
                Assert.That(track.localPosition, Is.EqualTo(Vector3.zero));
                mesh = track.GetComponent<MeshFilter>().sharedMesh;
                material = track.GetComponent<MeshRenderer>().sharedMaterial;
                Assert.That(material.shader.isSupported, Is.True);
                Assert.That(mesh.bounds.size.x, Is.GreaterThan(80f));
                Assert.That(mesh.bounds.size.z, Is.GreaterThan(170f));
                Assert.That(mesh.bounds.min.y, Is.EqualTo(0.01f).Within(0.0001f));
                content.SetActive(true);
                Assert.That(marker.gameObject.activeSelf && track.gameObject.activeSelf, Is.True);
                content.SetActive(false);
                Assert.That(marker.gameObject.activeInHierarchy || track.gameObject.activeInHierarchy, Is.False);
                trackerType.GetMethod("DestroyOwnedObject", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { anchor });
                Assert.That(mesh == null, Is.True);
                Assert.That(material == null, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [TestCase(1)]
        [TestCase(8)]
        public void SixMinutePaceCoversOneHundredMetresInThirtySixSecondsIncludingBends(int lane)
        {
            var runner = new GameObject("Motion test");
            try
            {
                runner.transform.localPosition = Vector3.up * 0.05f;
                Type motionType = Type.GetType("PaceScape.RunningTrack.RunningTrackMotion, Assembly-CSharp", true);
                object motion = Activator.CreateInstance(motionType, runner.transform, lane, 0.5f);
                MethodInfo advance = motionType.GetMethod("Advance");
                Vector3 previous = runner.transform.localPosition;
                double distance = 0;
                for (int step = 0; step < 360; step++)
                {
                    advance.Invoke(motion, new object[] { 0.1f, 6f });
                    Vector3 current = runner.transform.localPosition;
                    distance += Vector3.Distance(previous, current);
                    Assert.That(Vector3.Dot((current - previous).normalized, runner.transform.forward), Is.GreaterThan(0.99f));
                    Assert.That(current.y, Is.EqualTo(0.05f).Within(0.0001f));
                    previous = current;
                }

                Assert.That(distance, Is.EqualTo(100).Within(0.01));
                Assert.That(Vector3.Angle(runner.transform.forward, Vector3.forward), Is.GreaterThan(80f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runner);
            }
        }

        [TestCase(1)]
        [TestCase(8)]
        public void MotionClosesLapAndChangesPaceWithoutResetting(int lane)
        {
            var runner = new GameObject("Lap test");
            try
            {
                Type motionType = Type.GetType("PaceScape.RunningTrack.RunningTrackMotion, Assembly-CSharp", true);
                object motion = Activator.CreateInstance(motionType, runner.transform, lane, 0.5f);
                MethodInfo advance = motionType.GetMethod("Advance");
                Vector3 initial = runner.transform.localPosition;
                Quaternion initialRotation = runner.transform.localRotation;
                double length = (double)_geometryType.GetMethod("GetLaneLength").Invoke(null, new object[] { lane });
                advance.Invoke(motion, new object[] { (float)(length * 0.36), 6f });
                Assert.That(Vector3.Distance(initial, runner.transform.localPosition), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(initialRotation, runner.transform.localRotation), Is.LessThan(0.01f));
                advance.Invoke(motion, new object[] { 1f, 6f });
                Vector3 afterSixPace = runner.transform.localPosition;
                Assert.That(Vector3.Distance(initial, afterSixPace), Is.EqualTo(1000f / 360f).Within(0.0001f));
                advance.Invoke(motion, new object[] { 1f, 5f });
                Assert.That(Vector3.Distance(afterSixPace, runner.transform.localPosition),
                    Is.GreaterThan(1000f / 360f).And.LessThan(1000f / 300f));
                Vector3 beforePause = runner.transform.localPosition;
                advance.Invoke(motion, new object[] { 0f, 6f });
                advance.Invoke(motion, new object[] { 1f, 0f });
                advance.Invoke(motion, new object[] { 1f, float.NaN });
                Assert.That(runner.transform.localPosition, Is.EqualTo(beforePause));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(runner);
            }
        }

        [Test]
        public void InitiallyHiddenJoggerFollowsLaneAndChangesAnimationWithLivePace()
        {
            var content = new GameObject("Hidden jogger test");
            content.SetActive(false);
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Jogger/Jogger.prefab");
                Assert.That(prefab, Is.Not.Null);
                GameObject runner = UnityEngine.Object.Instantiate(prefab, content.transform);
                Animator animator = runner.GetComponentInChildren<Animator>(true);
                Type motionType = Type.GetType("PaceScape.RunningTrack.RunningTrackMotion, Assembly-CSharp", true);
                object motion = Activator.CreateInstance(motionType, runner.transform, 3, 0.5f);
                MethodInfo advance = motionType.GetMethod("Advance");

                // Match marker placement: construct movement while hidden, then reveal the content.
                content.SetActive(true);
                animator.Rebind();
                animator.Update(0f);

                float previousPace = 12f;
                foreach (float pace in new[] { 12f, 6f, 3f, 2f, 12f })
                {
                    double distance = 0;
                    Vector3 previous = runner.transform.localPosition;
                    Quaternion initialLegRotation = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).localRotation;
                    bool hasAnimatedLeg = false;
                    for (int frame = 0; frame < 120; frame++)
                    {
                        advance.Invoke(motion, new object[] { 1f / 60f, pace });
                        animator.Update(1f / 60f);
                        Vector3 current = runner.transform.localPosition;
                        distance += Vector3.Distance(previous, current);
                        Assert.That(Vector3.Dot((current - previous).normalized, runner.transform.forward), Is.GreaterThan(0.99f));
                        previous = current;
                        hasAnimatedLeg |= Quaternion.Angle(initialLegRotation,
                            animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).localRotation) > 5f;
                    }

                    double previousTravel = 2000.0 / (previousPace * 60.0);
                    double targetTravel = 2000.0 / (pace * 60.0);
                    Assert.That(distance, Is.InRange(Math.Min(previousTravel, targetTravel) - 0.01,
                        Math.Max(previousTravel, targetTravel) + 0.01));
                    Assert.That(animator.GetFloat("Speed") * animator.GetFloat("PlaybackRate"),
                        Is.EqualTo(1000f / (pace * 60f)).Within(0.03f));
                    Assert.That(hasAnimatedLeg, Is.True, "The runner must animate while following the lane.");
                    float nativeSpeed = 0f;
                    foreach (AnimatorClipInfo clip in animator.GetCurrentAnimatorClipInfo(0))
                    {
                        Assert.That(new[] { "walking", "runninig", "sprint" }, Does.Contain(clip.clip.name));
                        nativeSpeed += new Vector2(clip.clip.averageSpeed.x, clip.clip.averageSpeed.z).magnitude * clip.weight;
                    }

                    Assert.That(nativeSpeed * animator.GetFloat("PlaybackRate"),
                        Is.EqualTo(1000f / (pace * 60f)).Within(0.03f));
                    previousPace = pace;
                }

                Assert.That(animator.applyRootMotion, Is.False);
                Assert.That(animator.updateMode, Is.EqualTo(AnimatorUpdateMode.UnscaledTime));
                Assert.That(animator.fireEvents, Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(content);
            }
        }

        [Test]
        public void SpeedTransitionsTravelTheSameDistanceAtDifferentFrameRates()
        {
            var slowerFrames = new GameObject("30 fps runner");
            var fasterFrames = new GameObject("90 fps runner");
            try
            {
                Type motionType = Type.GetType("PaceScape.RunningTrack.RunningTrackMotion, Assembly-CSharp", true);
                MethodInfo advance = motionType.GetMethod("Advance");
                object first = Activator.CreateInstance(motionType, slowerFrames.transform, 1, 0.5f);
                object second = Activator.CreateInstance(motionType, fasterFrames.transform, 1, 0.5f);
                advance.Invoke(first, new object[] { 0.1f, 6f });
                advance.Invoke(second, new object[] { 0.1f, 6f });
                for (int frame = 0; frame < 60; frame++)
                {
                    advance.Invoke(first, new object[] { 1f / 30f, 3f });
                }

                for (int frame = 0; frame < 180; frame++)
                {
                    advance.Invoke(second, new object[] { 1f / 90f, 3f });
                }

                Assert.That(Vector3.Distance(slowerFrames.transform.position, fasterFrames.transform.position), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(slowerFrames.transform.rotation, fasterFrames.transform.rotation), Is.LessThan(0.01f));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(slowerFrames);
                UnityEngine.Object.DestroyImmediate(fasterFrames);
            }
        }

        [Test]
        public void HumanoidGroundingUsesSolesInsteadOfAnimationBounds()
        {
            var floor = new GameObject("Tilted floor");
            floor.transform.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(8f, 25f, 0f));
            floor.SetActive(false);
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Jogger/Jogger.prefab");
                GameObject runner = UnityEngine.Object.Instantiate(prefab, floor.transform);
                Type trackerType = Type.GetType("PaceScape.Tracking.TagTracker, Assembly-CSharp", true);
                trackerType.GetMethod("PlaceVisualOnPlane", BindingFlags.NonPublic | BindingFlags.Static)
                    .Invoke(null, new object[] { runner.transform, floor.transform });
                Animator animator = runner.GetComponentInChildren<Animator>(true);
                Transform leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
                float leftHeight = floor.transform.InverseTransformPoint(leftFoot.position).y - animator.leftFeetBottomHeight;
                float rightHeight = floor.transform.InverseTransformPoint(rightFoot.position).y - animator.rightFeetBottomHeight;
                Assert.That(Mathf.Min(leftHeight, rightHeight), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(animator.GetComponent("RunnerFootPlacement"), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(floor);
            }
        }

        private Vector3 Evaluate(double metres, float offset = 0f)
        {
            return (Vector3)_geometryType.GetMethod("Evaluate").Invoke(null, new object[] { metres, offset });
        }
    }
}
