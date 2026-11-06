using System;
using System.Collections.Generic;
using PaceScape.Performance;
using PaceScape.RunningTrack;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace PaceScape.Editor
{
    /// <summary>Isolates synthetic PR playback in an unsaved preview scene without transport or record storage.</summary>
    public sealed class GhostReplayPreviewWindow : EditorWindow
    {
        private PreviewRenderUtility _preview;
        private GameObject _root;
        private Transform _runner;
        private Animator _animator;
        private Material _groundMaterial;
        private Material _userMaterial;
        private GameObject _userMarker;
        private readonly TreadmillGhostPresentation _relativePresentation = new TreadmillGhostPresentation();
        private bool _relativeView = true;
        private double _simulatedUserDistance;
        private float _simulatedUserSpeedKmh = 10.8f;
        private float _viewYaw = 90f;
        private bool _overview;
        private float _viewPitch = 15f;
        private Label _relativeStatus;
        private RunningTrackVisual _track;
        private RunningTrackMotion _motion;
        private readonly GhostReplay _replay = new GhostReplay();
        private Label _status;
        private HelpBox _error;
        private Button _playButton;
        private bool _isRunning;
        private double _lastUpdate;

        [MenuItem("PaceScape/Ghost-Vorschau (Testdaten)")]
        private static void OpenWindow()
        {
            var window = GetWindow<GhostReplayPreviewWindow>();
            window.titleContent = new GUIContent("Ghost-Vorschau");
            window.minSize = new Vector2(560f, 460f);
        }

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += PlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            EditorApplication.playModeStateChanged -= PlayModeChanged;
            Cleanup();
        }

        /// <summary>Creates preview controls; only the standard 3D preview surface uses an IMGUI container.</summary>
        public void CreateGUI()
        {
            // Rebuilt Editor windows must not mix surviving scene objects with a new playback clock.
            Cleanup();
            _simulatedUserDistance = 0;
            _replay.Reset(CreateSyntheticRecords(), 20, 2);
            rootVisualElement.Clear();
            var sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/GhostReplayPreviewWindow.uss");
            if (sheet != null)
            {
                rootVisualElement.styleSheets.Add(sheet);
            }
            rootVisualElement.AddToClassList("ghost-preview");
            rootVisualElement.Add(new HelpBox("TESTDATEN · Fiktive 20-m- und 40-m-Rekorde. Keine echten Bestzeiten, keine Speicherung und keine Serververbindung. Die geöffnete Szene bleibt unverändert.", HelpBoxMessageType.Info));
            _error = new HelpBox(string.Empty, HelpBoxMessageType.Error);
            _error.style.display = DisplayStyle.None;
            rootVisualElement.Add(_error);
            _status = new Label();
            _status.AddToClassList("ghost-preview-status");
            rootVisualElement.Add(_status);
            var relativeToggle = new Toggle("Laufbandansicht simulieren") { value = _relativeView };
            relativeToggle.RegisterValueChangedCallback(evt =>
            {
                _relativeView = evt.newValue;
                ApplyRelativePose();
                UpdateStatus();
                Repaint();
            });
            rootVisualElement.Add(relativeToggle);
            var speed = new Slider("Dein simuliertes Tempo (km/h)", 0, 25)
            {
                value = _simulatedUserSpeedKmh,
                showInputField = true
            };
            speed.RegisterValueChangedCallback(evt => _simulatedUserSpeedKmh = evt.newValue);
            rootVisualElement.Add(speed);
            var side = new FloatField("Seitlicher Abstand (m)") { value = 1.5f };
            var range = new FloatField("Sichtdistanz vor/hinter dir (m)") { value = 10f };
            Action configureRelative = () =>
            {
                if (_relativePresentation.TryConfigure("TreadmillRelative", side.value, range.value))
                {
                    ApplyRelativePose();
                    UpdateStatus();
                    _error.style.display = DisplayStyle.None;
                    Repaint();
                }
                else
                {
                    ShowError("Seitlicher Abstand: ±0,5–5 m. Sichtdistanz: 1–100 m.");
                }
            };
            side.RegisterValueChangedCallback(evt => configureRelative());
            range.RegisterValueChangedCallback(evt => configureRelative());
            rootVisualElement.Add(side);
            rootVisualElement.Add(range);
            _relativePresentation.TryConfigure("TreadmillRelative", 1.5f, 10f);
            var overview = new Toggle("Übersicht statt Blick vom Laufband") { value = _overview };
            overview.RegisterValueChangedCallback(evt => { _overview = evt.newValue; Repaint(); });
            rootVisualElement.Add(overview);
            var yaw = new Slider("Blickrichtung (Grad)", -180, 180) { value = _viewYaw, showInputField = true };
            yaw.RegisterValueChangedCallback(evt => { _viewYaw = evt.newValue; Repaint(); });
            rootVisualElement.Add(yaw);
            var pitch = new Slider("Blickneigung (Grad)", -45, 45) { value = _viewPitch, showInputField = true };
            pitch.RegisterValueChangedCallback(evt => { _viewPitch = evt.newValue; Repaint(); });
            rootVisualElement.Add(pitch);
            _relativeStatus = new Label();
            rootVisualElement.Add(_relativeStatus);
            var controls = new VisualElement();
            controls.AddToClassList("ghost-preview-controls");
            _playButton = new Button(TogglePlayback) { text = "Start" };
            controls.Add(_playButton);
            controls.Add(new Button(() => ResetPreview()) { text = "Zurücksetzen" });
            controls.Add(new Button(() =>
            {
                _isRunning = false;
                if (EnsurePreview())
                {
                    Advance(5f);
                }
                UpdateStatus();
            }) { text = "5 s weiter" });
            rootVisualElement.Add(controls);
            var image = new IMGUIContainer(DrawPreview);
            image.AddToClassList("ghost-preview-image");
            rootVisualElement.Add(image);
            rootVisualElement.Add(new Label("Ablauf: 2 → 3 m/s; bei 20 m Wechsel zum 40-m-Verlauf; danach 2 → 4,67 m/s. Ab 40 m bleibt das letzte Tempo erhalten.\nDie kurzen Distanzen dienen nur der Vorschau, nicht als Trainingsvorgabe."));
            UpdateStatus();
        }

        private bool EnsurePreview()
        {
            if (_preview != null)
            {
                return true;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                ShowError("Für diese isolierte Vorschau zuerst den Play Mode beenden.");
                return false;
            }
            try
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/Jogger/Jogger.prefab");
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (prefab == null || shader == null)
                {
                    throw new InvalidOperationException("Jogger prefab or URP Lit shader is unavailable.");
                }
                _preview = new PreviewRenderUtility();
                _preview.camera.fieldOfView = 45;
                // Unity's preview camera starts with a 2 m near plane, which clips a nearby companion.
                _preview.camera.nearClipPlane = 0.03f;
                _preview.camera.farClipPlane = 500;
                _preview.camera.backgroundColor = new Color(0.08f, 0.10f, 0.14f);
                _preview.camera.clearFlags = CameraClearFlags.SolidColor;
                _preview.camera.stereoTargetEye = StereoTargetEyeMask.None;
                _preview.lights[0].intensity = 1.2f;
                _preview.lights[0].transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                _preview.lights[1].intensity = 0.7f;
                _preview.ambientColor = new Color(0.5f, 0.5f, 0.5f);
                _root = new GameObject("Ghost preview - synthetic records only");
                _preview.AddSingleGO(_root);
                _track = RunningTrackVisual.Create(_root.transform);
                var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
                ground.transform.SetParent(_root.transform, false);
                ground.transform.localPosition = new Vector3(-40f, -0.02f, 50f);
                ground.transform.localScale = Vector3.one * 35f;
                DestroyImmediate(ground.GetComponent<Collider>());
                _groundMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _groundMaterial.SetColor("_BaseColor", new Color(0.12f, 0.18f, 0.16f));
                ground.GetComponent<Renderer>().sharedMaterial = _groundMaterial;
                var runnerRoot = new GameObject("Preview runner");
                runnerRoot.transform.SetParent(_root.transform, false);
                _runner = runnerRoot.transform;
                GameObject visual = Instantiate(prefab, _runner, false);
                _animator = visual.GetComponentInChildren<Animator>(true);
                if (_animator != null)
                {
                    _animator.applyRootMotion = false;
                    _animator.fireEvents = false;
                    _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    _animator.Rebind();
                    _animator.Update(0f);
                }
                foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
                {
                    collider.enabled = false;
                }
                _userMarker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                _userMarker.name = "Simulated treadmill position - stationary";
                _userMarker.transform.SetParent(_root.transform, false);
                _userMarker.transform.localPosition = Vector3.up * 0.85f;
                _userMarker.transform.localScale = new Vector3(0.4f, 0.85f, 0.4f);
                DestroyImmediate(_userMarker.GetComponent<Collider>());
                _userMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                _userMaterial.SetColor("_BaseColor", new Color(0.1f, 0.65f, 0.9f));
                _userMarker.GetComponent<Renderer>().sharedMaterial = _userMaterial;
                _motion = new RunningTrackMotion(_runner, 1, 0.5f);
                _replay.Reset(CreateSyntheticRecords(), 20, 2);
                _simulatedUserDistance = 0;
                ApplyRelativePose();
                _error.style.display = DisplayStyle.None;
                return true;
            }
            catch (Exception exception)
            {
                Cleanup();
                ShowError(exception.Message);
                Debug.LogException(exception);
                return false;
            }
        }

        private static PersonalBestBook CreateSyntheticRecords()
        {
            var book = new PersonalBestBook();
            book.TryImproveWithTrace(20, 8, "synthetic", new List<EffortPoint>
            {
                new EffortPoint { seconds = 0, distanceMetres = 0 },
                new EffortPoint { seconds = 4, distanceMetres = 8 },
                new EffortPoint { seconds = 8, distanceMetres = 20 }
            });
            book.TryImproveWithTrace(40, 14, "synthetic", new List<EffortPoint>
            {
                new EffortPoint { seconds = 0, distanceMetres = 0 },
                new EffortPoint { seconds = 8, distanceMetres = 20 },
                new EffortPoint { seconds = 11, distanceMetres = 26 },
                new EffortPoint { seconds = 14, distanceMetres = 40 }
            });
            return book;
        }

        private void TogglePlayback()
        {
            if (EnsurePreview())
            {
                _isRunning = !_isRunning;
                _lastUpdate = EditorApplication.timeSinceStartup;
                UpdateStatus();
            }
        }

        private void ResetPreview()
        {
            _isRunning = false;
            if (EnsurePreview())
            {
                _motion.ResetToStart();
                _simulatedUserDistance = 0;
                _replay.Reset(CreateSyntheticRecords(), 20, 2);
                _motion.AdvanceRecorded(0.001f, 0, _replay.SpeedMetresPerSecond);
                ApplyRelativePose();
                _animator?.Update(0f);
            }
            UpdateStatus();
            Repaint();
        }

        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            double delta = now - _lastUpdate;
            _lastUpdate = now;
            if (_isRunning && _preview != null && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                // Do not catch up long editor stalls or time spent outside this focused preview.
                if (focusedWindow == this && delta > 0 && delta <= 0.25)
                {
                    Advance((float)delta);
                }
            }
        }

        private void Advance(float delta)
        {
            double travel = _replay.Advance(delta);
            _motion.AdvanceRecorded(delta, travel, _replay.SpeedMetresPerSecond);
            _simulatedUserDistance += _simulatedUserSpeedKmh / 3.6 * delta;
            ApplyRelativePose();
            _animator?.Update(delta);
            UpdateStatus();
            Repaint();
        }

        private void ApplyRelativePose()
        {
            if (_motion == null)
            {
                return;
            }
            _userMarker.SetActive(_relativeView && _overview);
            if (_relativeView)
            {
                bool visible = _relativePresentation.Evaluate(Pose.identity,
                    _replay.DistanceMetres - _simulatedUserDistance - 0.5, out Pose pose);
                _motion.SetPresentationPose(pose, visible);
            }
            else
            {
                _motion.ClearPresentationPose();
            }
        }

        private void UpdateStatus()
        {
            if (_status == null)
            {
                return;
            }
            string state = _preview == null ? "Bereit" : _replay.IsHoldingSpeed ? "Letztes Tempo halten" : "Tempoverlauf";
            _status.text = _preview == null ? "Bereit · Start öffnet die isolierte Vorschau." :
                $"{(_isRunning ? "Läuft" : "Pausiert")} · {_replay.DistanceMetres:F2} m · {_replay.SpeedMetresPerSecond * 3.6:F1} km/h · Referenz {_replay.ReferenceDistanceMetres} m · {state}";
            _playButton.text = _isRunning ? "Pause" : "Start";
            if (_relativeStatus != null)
            {
                double gap = _replay.DistanceMetres - _simulatedUserDistance - 0.5;
                _relativeStatus.text = !_relativeView ? "Bahnansicht · keine relative Platzierung" :
                    $"TEST · Du: {_simulatedUserDistance:F2} m · Ghost: {Math.Abs(gap):F2} m {(gap >= 0 ? "vor dir" : "hinter dir")} · {(Math.Abs(gap) > _relativePresentation.VisibilityDistanceMetres ? "außerhalb der Sichtdistanz" : "sichtbar")} · Blau = dein fester Laufbandplatz";
            }
        }

        private void DrawPreview()
        {
            Rect rect = GUILayoutUtility.GetRect(100f, 10000f, 180f, 10000f);
            if (Event.current.type != EventType.Repaint || !EnsurePreview())
            {
                return;
            }
            Vector3 target = _relativeView ? Vector3.up : _runner.position + Vector3.up;
            if (_relativeView && !_overview)
            {
                _userMarker.SetActive(false);
                _preview.camera.fieldOfView = 65f;
                _preview.camera.transform.position = Vector3.up * 1.65f;
                _preview.camera.transform.rotation = Quaternion.Euler(_viewPitch, _viewYaw, 0);
            }
            else
            {
                _userMarker.SetActive(_relativeView);
                _preview.camera.fieldOfView = _relativeView ? 55f : 45f;
                _preview.camera.transform.position = target + (_relativeView ? new Vector3(10f, 9f, -14f) : new Vector3(5f, 3f, -7f));
                _preview.camera.transform.LookAt(target);
            }
            _preview.BeginPreview(rect, GUIStyle.none);
            _preview.Render(true);
            Texture texture = _preview.EndPreview();
            GUI.DrawTexture(rect, texture, ScaleMode.StretchToFill, false);
        }

        private void PlayModeChanged(PlayModeStateChange state)
        {
            _isRunning = false;
            Cleanup();
            UpdateStatus();
        }

        private void ShowError(string message)
        {
            if (_error != null)
            {
                _error.text = message;
                _error.style.display = DisplayStyle.Flex;
            }
        }

        private void Cleanup()
        {
            _isRunning = false;
            _track?.ReleaseGeneratedResources();
            _preview?.Cleanup();
            _preview = null;
            _track = null;
            _root = null;
            _runner = null;
            _motion = null;
            _animator = null;
            _userMarker = null;
            if (_userMaterial != null)
            {
                DestroyImmediate(_userMaterial);
                _userMaterial = null;
            }
            if (_groundMaterial != null)
            {
                DestroyImmediate(_groundMaterial);
                _groundMaterial = null;
            }
        }
    }
}
