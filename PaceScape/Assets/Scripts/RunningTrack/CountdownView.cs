using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PaceScape.RunningTrack
{
    // Renders the same camera-relative digits for exercise and run starts.
    internal sealed class CountdownView : IDisposable
    {
        private readonly Camera _camera;
        private readonly int _layer;
        private readonly string _name;
        private TextMesh _countdown;
        private int _displayedSeconds;

        public CountdownView(Camera camera, int layer, string name)
        {
            _camera = camera;
            _layer = layer;
            _name = name;
        }

        public void Dispose()
        {
            Show(0);
        }

        public void Show(int seconds)
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
                var label = new GameObject(_name, typeof(TextMesh));
                label.layer = _layer;
                label.transform.SetParent(_camera.transform, false);
                _countdown = label.GetComponent<TextMesh>();
                _countdown.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                _countdown.fontSize = 96;
                _countdown.fontStyle = FontStyle.Bold;
                _countdown.anchor = TextAnchor.MiddleCenter;
                _countdown.color = Color.white;
                label.GetComponent<MeshRenderer>().sharedMaterial = _countdown.font.material;
                UpdateLayout();
            }

            _countdown.text = seconds.ToString();
        }

        public void UpdateLayout()
        {
            if (_countdown == null || _camera == null)
            {
                return;
            }

            float depth = Mathf.Max(0.85f, _camera.nearClipPlane + 0.25f);
            Vector3 centre = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0.62f, depth));
            Vector3 top = _camera.ViewportToWorldPoint(new Vector3(0.5f, 0.76f, depth));
            _countdown.transform.localPosition = _camera.transform.InverseTransformPoint(centre);
            // TextMesh glyphs use world units independent of the font's raster size.
            _countdown.transform.localScale = Vector3.one * (Vector3.Distance(centre, top) / 12f);
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
