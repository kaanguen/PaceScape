using System.Collections.Generic;
using PaceScape.RunningTrack;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PaceScape.Editor
{
    // Runs the production warm-up player in an isolated, unsaved preview scene.
    public sealed class WarmupPreviewWindow : EditorWindow
    {
        private readonly RunnerWarmup _warmup = new RunnerWarmup();
        private PreviewRenderUtility _preview;
        private WarmupCoachView _coach;
        private GameObject _coachPrefab;
        private Material _floorMaterial;
        private double _lastUpdate;
        private Label _status;
        private IMGUIContainer _image;
        private float _viewAngle;

        [MenuItem("PaceScape/Warm-up-Vorschau")]
        private static void Open()
        {
            GetWindow<WarmupPreviewWindow>("Warm-up-Vorschau").minSize = new Vector2(400f, 480f);
        }

        /// <summary>Creates the exercise controls and the 3D preview surface.</summary>
        public void CreateGUI()
        {
            var exercise = new DropdownField("Übung", new List<string> { "Hampelmänner", "Kniebeugen" }, 0);
            var seconds = new FloatField("Vorschaudauer in Sekunden") { value = 8f };
            var angle = new Slider("Kopfdrehung", -180f, 180f) { value = _viewAngle };
            angle.RegisterValueChangedCallback(change =>
            {
                _viewAngle = change.newValue;
                _image.MarkDirtyRepaint();
            });
            var buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.Add(new Button(() =>
            {
                if (!EnsurePreview())
                {
                    return;
                }
                HideCoach();
                var step = new RunnerWarmup.Step
                {
                    animationId = exercise.index == 0 ? RunnerWarmup.JumpingJacks : RunnerWarmup.AirSquat,
                    seconds = seconds.value
                };
                var plan = new[] { step };
                if (!RunnerWarmup.IsValidPlan(plan) ||
                    !WarmupCoachView.TryCreate(_coachPrefab, _preview.camera, out _coach) ||
                    !_warmup.TryStart(_coach.Animator, plan))
                {
                    HideCoach();
                    _status.text = "Wähle eine Dauer zwischen 1 und 300 Sekunden. Der Coach benötigt seinen Warm-up-Animator.";
                }
                _image.MarkDirtyRepaint();
            }) { text = "Abspielen" });
            buttons.Add(new Button(() =>
            {
                HideCoach();
                _status.text = "Gestoppt";
                _image.MarkDirtyRepaint();
            }) { text = "Stoppen" });

            _status = new Label("Bereit");
            _image = new IMGUIContainer(DrawPreview);
            _image.style.flexGrow = 1;
            rootVisualElement.Add(exercise);
            rootVisualElement.Add(seconds);
            rootVisualElement.Add(angle);
            rootVisualElement.Add(buttons);
            rootVisualElement.Add(_status);
            rootVisualElement.Add(_image);
        }

        private bool EnsurePreview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _status.text = "Beende den Play Mode für die Vorschau.";
                return false;
            }
            if (_preview != null)
            {
                return true;
            }

            _coachPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/WarmupCoach/WarmupCoach.prefab");
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (_coachPrefab == null || shader == null)
            {
                _status.text = "Coach-Prefab oder URP-Lit-Shader fehlt.";
                return false;
            }

            _preview = new PreviewRenderUtility();
            _preview.camera.nearClipPlane = 0.03f;
            _preview.camera.fieldOfView = 48f;
            _preview.camera.clearFlags = CameraClearFlags.SolidColor;
            _preview.camera.backgroundColor = new Color(0.16f, 0.21f, 0.27f);
            _preview.camera.stereoTargetEye = StereoTargetEyeMask.None;
            _preview.lights[0].intensity = 2.2f;
            _preview.lights[0].transform.rotation = Quaternion.Euler(35f, 195f, 0f);
            _preview.lights[1].intensity = 1f;
            _preview.ambientColor = Color.gray;

            _preview.camera.transform.position = Vector3.up * 1.6f;

            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _preview.AddSingleGO(floor);
            floor.transform.localScale = new Vector3(0.8f, 1f, 1.6f);
            floor.transform.position = Vector3.forward * 2f;
            DestroyImmediate(floor.GetComponent<Collider>());
            _floorMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _floorMaterial.SetColor("_BaseColor", new Color(0.21f, 0.28f, 0.33f));
            floor.GetComponent<Renderer>().sharedMaterial = _floorMaterial;
            return true;
        }

        private void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            float delta = Mathf.Clamp((float)(now - _lastUpdate), 0f, 0.05f);
            _lastUpdate = now;
            if (!_warmup.Active || focusedWindow != this || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            _warmup.Tick(delta);
            if (_warmup.Active)
            {
                _coach.Animator.Update(delta);
                RunnerWarmup.State state = _warmup.CaptureState(false);
                _status.text = $"{state.stepElapsed:F1} / {state.stepDuration:F1} s";
            }
            else
            {
                HideCoach();
                _status.text = "Abgeschlossen";
            }
            _image?.MarkDirtyRepaint();
        }

        private void DrawPreview()
        {
            Rect rect = GUILayoutUtility.GetRect(100f, 10000f, 200f, 10000f);
            if (Event.current.type != EventType.Repaint || !EnsurePreview())
            {
                return;
            }

            _preview.camera.transform.rotation = Quaternion.Euler(0f, _viewAngle, 0f);
            _preview.BeginPreview(rect, GUIStyle.none);
            _coach?.UpdateLayout();
            _preview.Render(true);
            GUI.DrawTexture(rect, _preview.EndPreview(), ScaleMode.StretchToFill, false);
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Cleanup();
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            Cleanup();
            _image?.MarkDirtyRepaint();
        }

        private void HideCoach()
        {
            _warmup.Stop();
            _coach?.Dispose();
            _coach = null;
        }

        private void Cleanup()
        {
            HideCoach();
            _coachPrefab = null;
            _preview?.Cleanup();
            _preview = null;
            DestroyImmediate(_floorMaterial);
            _floorMaterial = null;
        }
    }
}
