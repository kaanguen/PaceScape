using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
using UnityEngine.XR.MagicLeap;
#endif

namespace PaceScape.RemoteControl
{
    [Serializable]
    internal sealed class RemoteCameraState
    {
        public bool supported;
        public bool enabled;
        public string status;
        public string error;
    }

    /// <summary>Opt-in mixed-reality JPEG preview. Never records or starts after a reconnect.</summary>
    [DisallowMultipleComponent]
    public sealed class RemoteCameraStream : MonoBehaviour
    {
        private const int MaxFrameBytes = 2 * 1024 * 1024;
        public event Action<byte[]> FrameReady;
        private bool _requested;
        private string _status = "Stopped";
        private string _error = "";
#if UNITY_ANDROID && !UNITY_EDITOR
        private MLCamera _camera;
        private bool _awaitingFrame;
        private float _nextCaptureAt;
        private float _captureStartedAt;
#endif
        private static bool Supported => Application.platform == RuntimePlatform.Android && !Application.isEditor;

        internal RemoteCameraState CaptureState()
        {
            return new RemoteCameraState { supported = Supported, enabled = _requested,
                status = Supported ? _status : "Unsupported", error = _error };
        }

        /// <summary>Accepts explicit start/stop requests; permission and camera readiness follow in telemetry.</summary>
        public bool TryApply(string command, out string error)
        {
            error = "Camera preview is only available in the Magic Leap Android build.";
            if (command == "camera.stop")
            {
                StopStream();
                error = "";
                return true;
            }
            if (command != "camera.start" || !Supported)
            {
                return false;
            }
            if (!_requested)
            {
                _requested = true;
                _error = "";
                _status = "Starting";
#if UNITY_ANDROID && !UNITY_EDITOR
                StartCoroutine(InitializeCamera());
#endif
            }
            error = "";
            return true;
        }

        /// <summary>Releases capture on pause, disconnect or disable, discarding all pending frames.</summary>
        public void StopStream()
        {
            _requested = false;
            StopAllCoroutines();
#if UNITY_ANDROID && !UNITY_EDITOR
            _awaitingFrame = false;
            if (_camera != null)
            {
                _camera.OnRawImageAvailable -= OnImage;
                _camera.Disconnect();
                _camera = null;
            }
#endif
            _status = "Stopped";
            _error = "";
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        private IEnumerator InitializeCamera()
        {
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                _status = "RequestingPermission";
                Permission.RequestUserPermission(Permission.Camera);
                float deadline = Time.realtimeSinceStartup + 20f;
                while (_requested && !Permission.HasUserAuthorizedPermission(Permission.Camera) &&
                    Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }
                if (!_requested)
                {
                    yield break;
                }
                if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    Fail("Kameraberechtigung fehlt. Auf der Brille erlauben und erneut starten.");
                    yield break;
                }
            }
            _status = "Starting";
            float availableUntil = Time.realtimeSinceStartup + 10f;
            bool available = false;
            while (_requested && Time.realtimeSinceStartup < availableUntil)
            {
                if (MLCamera.GetDeviceAvailabilityStatus(MLCamera.Identifier.Main, out available).IsOk && available)
                {
                    break;
                }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            if (!_requested)
            {
                yield break;
            }
            if (!available)
            {
                Fail("Kamera ist belegt oder nicht verfügbar.");
                yield break;
            }
            try
            {
                var context = MLCamera.ConnectContext.Create();
                context.CamId = MLCamera.Identifier.Main;
                context.Flags = MLCamera.ConnectFlag.MR;
                var mixed = MLCamera.MRConnectInfo.Create();
                mixed.MRQuality = MLCamera.MRQuality._960x720;
                context.MixedRealityConnectInfo = mixed;
                _camera = MLCamera.CreateAndConnect(context);
                if (_camera == null)
                {
                    Fail("Mixed-Reality-Kamera konnte nicht geöffnet werden.");
                    yield break;
                }
                var capabilities = MLCamera.GetImageStreamCapabilitiesForCamera(_camera, MLCamera.CaptureType.Image);
                if (capabilities.Length == 0 ||
                    !MLCamera.TryGetBestFitStreamCapabilityFromCollection(capabilities, 960, 720,
                        MLCamera.CaptureType.Image, out MLCamera.StreamCapability stream))
                {
                    Fail("JPEG-Vorschau wird in dieser Kamerakonfiguration nicht unterstützt.");
                    yield break;
                }
                var config = new MLCamera.CaptureConfig
                {
                    CaptureFrameRate = MLCamera.CaptureFrameRate.None,
                    StreamConfigs = new[] { MLCamera.CaptureStreamConfig.Create(stream, MLCamera.OutputFormat.JPEG) }
                };
                var result = _camera.PrepareCapture(config, out _);
                if (!result.IsOk)
                {
                    Fail("Kamerakonfiguration fehlgeschlagen: " + result);
                    yield break;
                }
                _camera.PreCaptureAEAWB();
                _camera.OnRawImageAvailable += OnImage;
                _nextCaptureAt = Time.unscaledTime;
            }
            catch (Exception exception)
            {
                Fail("Kamerastart fehlgeschlagen: " + exception.GetType().Name);
            }
        }

        private void Update()
        {
            if (!_requested || _camera == null)
            {
                return;
            }
            if (_awaitingFrame)
            {
                if (Time.unscaledTime - _captureStartedAt > 5f)
                {
                    Fail("Kein Kamerabild empfangen. Vorschau erneut starten.");
                }
                return;
            }
            if (Time.unscaledTime < _nextCaptureAt)
            {
                return;
            }
            _awaitingFrame = true;
            _captureStartedAt = Time.unscaledTime;
            _nextCaptureAt = Time.unscaledTime + 0.5f;
            try
            {
                if (!_camera.CaptureImage().IsOk)
                {
                    Fail("Kamerabild konnte nicht aufgenommen werden.");
                }
            }
            catch (Exception exception)
            {
                Fail("Kamera unterbrochen: " + exception.GetType().Name);
            }
        }

        private void OnImage(MLCamera.CameraOutput output, MLCamera.ResultExtras extras, MLCamera.Metadata metadata)
        {
            // The SDK dispatches this managed callback on Unity's main thread.
            if (!_requested || !_awaitingFrame)
            {
                return;
            }
            _awaitingFrame = false;
            byte[] data = output.Planes != null && output.Planes.Length > 0 ? output.Planes[0].Data : null;
            if (output.Format != MLCamera.OutputFormat.JPEG || data == null || data.Length < 4 || data.Length > MaxFrameBytes)
            {
                Fail("Kamerabild ungültig oder größer als 2 MiB.");
                return;
            }
            _status = "Streaming";
            FrameReady?.Invoke(data);
        }
#endif
        private void Fail(string message)
        {
            StopStream();
            _status = "Error";
            _error = message;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                StopStream();
            }
        }

        private void OnDisable()
        {
            StopStream();
        }
    }
}
