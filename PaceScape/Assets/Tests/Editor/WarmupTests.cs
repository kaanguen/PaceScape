using System;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PaceScape.Tests
{
    public sealed class WarmupTests
    {
        private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;
        private GameObject _root;
        private Camera _camera;
        private Component _tracker;
        private Type _trackerType;
        private Type _stepType;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Warm-up test");
            _root.SetActive(false);
            var cameraObject = new GameObject("Head camera");
            cameraObject.transform.SetParent(_root.transform, false);
            _camera = cameraObject.AddComponent<Camera>();
            _trackerType = Type.GetType("PaceScape.Tracking.TagTracker, Assembly-CSharp", true);
            _stepType = Type.GetType("PaceScape.RunningTrack.RunnerWarmup+Step, Assembly-CSharp", true);
            FieldInfo originField = _trackerType.GetField("_xrOrigin", PrivateInstance);
            Component origin = _root.AddComponent(originField.FieldType);
            origin.GetType().GetProperty("Camera").SetValue(origin, _camera);
            _tracker = _root.AddComponent(_trackerType);
            originField.SetValue(_tracker, origin);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/WarmupCoach/WarmupCoach.prefab");
            Assert.That(prefab, Is.Not.Null);
            SetTrackerField("_warmupCoachPrefab", prefab);
            _root.SetActive(true);
            FieldInfo phase = _trackerType.GetField("_startupPhase", PrivateInstance);
            phase.SetValue(_tracker, Enum.Parse(phase.FieldType, "Ready"));
            SetTrackerField("_hasHeadTracking", true);
            SetTrackerField("_hasApplicationFocus", true);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void PlaysWithoutMarkersAndRemovesCoachAfterTimedCompletion()
        {
            Assert.That(_trackerType.GetProperty("PlacedMarkerCount").GetValue(_tracker), Is.EqualTo(0));
            Array plan = MakePlan(1.5f, 2f);
            Assert.That(Start(plan), Is.True);
            Assert.That(Start(plan), Is.False);
            Assert.That(_camera.transform.childCount, Is.EqualTo(2));
            Assert.That(State("status"), Is.EqualTo("Countdown"));
            Assert.That(State("countdownRemaining"), Is.EqualTo(3f));
            Assert.That(_trackerType.GetMethod("TryNextWarmupExercise").Invoke(_tracker, null), Is.False);

            var animator = _camera.GetComponentInChildren<Animator>();
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            Vector3 viewport = _camera.WorldToViewportPoint(head.position);
            Assert.That(viewport.x, Is.InRange(0.5f, 1f));
            Assert.That(viewport.y, Is.InRange(0f, 0.5f));
            _camera.transform.SetPositionAndRotation(Vector3.one, Quaternion.Euler(30f, 80f, 12f));
            UpdateWarmup(2f);
            Assert.That(State("stepIndex"), Is.Zero);
            Assert.That(State("stepElapsed"), Is.Zero);
            Assert.That(State("countdownRemaining"), Is.EqualTo(1f));
            var countdown = _camera.GetComponentInChildren<TextMesh>();
            Assert.That(countdown.text, Is.EqualTo("1"));
            Assert.That(countdown.font, Is.Not.Null);
            UpdateWarmup(2.75f);
            Assert.That(State("status"), Is.EqualTo("Playing"));
            Assert.That(_camera.GetComponentInChildren<TextMesh>(), Is.Null);
            Assert.That(Vector3.Distance(viewport, _camera.WorldToViewportPoint(head.position)), Is.LessThan(0.001f));
            Assert.That(State("stepIndex"), Is.EqualTo(1));
            Assert.That(State("stepElapsed"), Is.EqualTo(0.25f).Within(0.001f));

            UpdateWarmup(1.75f);
            Assert.That(State("status"), Is.EqualTo("Completed"));
            Assert.That(_camera.transform.childCount, Is.Zero);
            Assert.That(State("canStart"), Is.True);
        }

        [Test]
        public void RejectsInvalidPlansAndViewsWithoutLeavingAClone()
        {
            Assert.That(Start(MakePlan(float.NaN)), Is.False);
            Assert.That(_camera.transform.childCount, Is.Zero);
            int cameraLayers = _camera.cullingMask;
            _camera.cullingMask = 0;
            Assert.That(Start(MakePlan(2f)), Is.False);
            Assert.That(_camera.transform.childCount, Is.Zero);
            _camera.cullingMask = cameraLayers;
            _camera.farClipPlane = 0.9f;
            Assert.That(Start(MakePlan(2f)), Is.False);
            Assert.That(_camera.transform.childCount, Is.Zero);
            Assert.That(State("active"), Is.False);
        }

        [Test]
        public void TrackingOrViewLossRemovesCoachWithoutResumingAutomatically()
        {
            Assert.That(Start(MakePlan(2f)), Is.True);
            _camera.enabled = false;
            UpdateWarmup(0.2f);
            Assert.That(State("status"), Is.EqualTo("ViewUnavailable"));
            Assert.That(_camera.transform.childCount, Is.Zero);

            _camera.enabled = true;
            UpdateWarmup(0.2f);
            Assert.That(_camera.transform.childCount, Is.Zero);
            Assert.That(Start(MakePlan(2f)), Is.True);
            _trackerType.GetMethod("HideVisuals", PrivateInstance).Invoke(_tracker, null);
            Assert.That(State("status"), Is.EqualTo("TrackingLost"));
            Assert.That(State("canStart"), Is.False);
            Assert.That(_camera.transform.childCount, Is.Zero);
        }

        private void SetTrackerField(string name, object value)
        {
            _trackerType.GetField(name, PrivateInstance).SetValue(_tracker, value);
        }

        private Array MakePlan(params float[] durations)
        {
            Array plan = Array.CreateInstance(_stepType, durations.Length);
            for (int i = 0; i < durations.Length; i++)
            {
                object step = Activator.CreateInstance(_stepType);
                _stepType.GetField("animationId").SetValue(step, i == 0 ? "Mixamo_StartJumpingJacks" : "Mixamo_AirSquat");
                _stepType.GetField("seconds").SetValue(step, durations[i]);
                plan.SetValue(step, i);
            }
            return plan;
        }

        private bool Start(Array plan)
        {
            return (bool)_trackerType.GetMethod("TryStartWarmup").Invoke(_tracker, new object[] { plan });
        }

        private void UpdateWarmup(float delta)
        {
            _trackerType.GetMethod("UpdateWarmup", PrivateInstance).Invoke(_tracker, new object[] { delta });
        }

        private object State(string name)
        {
            object state = _trackerType.GetMethod("CaptureWarmup").Invoke(_tracker, new object[] { false });
            return state.GetType().GetField(name).GetValue(state);
        }
    }
}
