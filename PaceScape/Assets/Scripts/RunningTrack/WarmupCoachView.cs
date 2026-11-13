using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PaceScape.RunningTrack
{
    /// <summary>Owns the exercise avatar and keeps it in the camera's lower-right viewport.</summary>
    public sealed class WarmupCoachView : IDisposable
    {
        private const float ViewportX = 0.75f;
        private const float ViewportY = 0.29f;
        private const float ViewportHeight = 0.23f;
        private const float PreferredDepth = 0.85f;
        private const float DepthClearance = 0.25f;

        private readonly Camera _camera;
        private readonly Transform _model;
        private readonly Bounds _standingBounds;
        private readonly int _renderedLayers;
        private TextMesh _countdown;
        private int _displayedSeconds;

        private WarmupCoachView(GameObject instance, Camera camera)
        {
            _camera = camera;
            _model = instance.transform;
            Animator = instance.GetComponentInChildren<Animator>();
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || renderer.forceRenderingOff)
                {
                    continue;
                }

                if (_renderedLayers == 0)
                {
                    _standingBounds = renderer.bounds;
                }
                else
                {
                    _standingBounds.Encapsulate(renderer.bounds);
                }
                _renderedLayers |= 1 << renderer.gameObject.layer;
            }

            _model.SetParent(camera.transform, false);
        }

        /// <summary>Creates the coach only when it can display and play the exercises.</summary>
        public static bool TryCreate(GameObject prefab, Camera camera, out WarmupCoachView view)
        {
            view = null;
            if (prefab == null || camera == null)
            {
                return false;
            }

            GameObject instance = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            instance.name = "Warm-up coach";
            try
            {
                var candidate = new WarmupCoachView(instance, camera);
                Animator animator = candidate.Animator;
                if (animator == null || !animator.isActiveAndEnabled || !animator.isHuman ||
                    animator.runtimeAnimatorController == null || !candidate.UpdateLayout())
                {
                    return false;
                }

                animator.Rebind();
                animator.Update(0f);
                if (!RunnerWarmup.Supports(animator))
                {
                    return false;
                }

                view = candidate;
                return true;
            }
            finally
            {
                if (view == null)
                {
                    Release(instance);
                }
            }
        }

        /// <summary>Gets the animator used by the exercise player.</summary>
        public Animator Animator { get; }

        /// <summary>Shows the seconds until the first exercise, or hides the countdown at zero.</summary>
        public void ShowCountdown(int seconds)
        {
            if (_displayedSeconds == seconds)
            {
                return;
            }

            _displayedSeconds = seconds;
            if (seconds == 0)
            {
                if (_countdown != null)
                {
                    Release(_countdown.gameObject);
                    _countdown = null;
                }
                return;
            }

            if (_countdown == null)
            {
                var label = new GameObject("Warm-up countdown", typeof(TextMesh));
                label.layer = _model.gameObject.layer;
                label.transform.SetParent(_camera.transform, false);
                _countdown = label.GetComponent<TextMesh>();
                _countdown.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _countdown.fontSize = 96;
                _countdown.fontStyle = FontStyle.Bold;
                _countdown.anchor = TextAnchor.MiddleCenter;
                _countdown.color = Color.white;
                label.GetComponent<MeshRenderer>().sharedMaterial = _countdown.font.material;
                LayoutCountdown(Mathf.Max(PreferredDepth, _camera.nearClipPlane + DepthClearance));
            }

            _countdown.text = seconds.ToString();
        }

        internal bool IsAvailable(Camera camera)
        {
            return _camera != null && _camera == camera && _camera.isActiveAndEnabled &&
                _model != null && _model.parent == camera.transform && Animator != null && Animator.isActiveAndEnabled;
        }

        /// <summary>Updates the camera-relative placement for the current projection.</summary>
        public bool UpdateLayout()
        {
            if (_model == null || _camera == null || (_camera.cullingMask & _renderedLayers) == 0 ||
                !(_standingBounds.size.y > 0f))
            {
                return false;
            }

            // Preserve clearance for limbs when OpenXR raises the camera's near clip.
            float depth = Mathf.Max(PreferredDepth, _camera.nearClipPlane + DepthClearance);
            if (!(_camera.farClipPlane > depth + DepthClearance))
            {
                return false;
            }

            Vector3 centre = _camera.ViewportToWorldPoint(new Vector3(ViewportX, ViewportY, depth));
            Vector3 top = _camera.ViewportToWorldPoint(new Vector3(ViewportX, ViewportY + ViewportHeight, depth));
            float scale = Vector3.Distance(centre, top) / _standingBounds.size.y;
            Vector3 cameraSpace = _camera.transform.InverseTransformPoint(centre);
            if (!(scale > 0f && scale < float.PositiveInfinity) ||
                !(cameraSpace.sqrMagnitude < float.PositiveInfinity && _standingBounds.center.sqrMagnitude < float.PositiveInfinity))
            {
                return false;
            }

            Quaternion rotation = Quaternion.LookRotation(new Vector3(-cameraSpace.x, 0f, -cameraSpace.z), Vector3.up);
            _model.localScale = Vector3.one * scale;
            _model.SetLocalPositionAndRotation(cameraSpace - rotation * (_standingBounds.center * scale), rotation);
            if (_countdown != null)
            {
                LayoutCountdown(depth);
            }
            return true;
        }

        private void LayoutCountdown(float depth)
        {
            Vector3 centre = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0.62f, depth));
            Vector3 top = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0.76f, depth));
            _countdown.transform.localPosition = _camera.transform.InverseTransformPoint(centre);
            // TextMesh glyphs use world units independent of the font's raster size.
            _countdown.transform.localScale = Vector3.one * (Vector3.Distance(centre, top) / 12f);
        }

        /// <summary>Hides the avatar immediately and releases its instance.</summary>
        public void Dispose()
        {
            ShowCountdown(0);
            if (_model == null)
            {
                return;
            }

            Release(_model.gameObject);
        }

        private static void Release(GameObject instance)
        {
            instance.SetActive(false);
            if (Application.isPlaying)
            {
                Object.Destroy(instance);
            }
            else
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
