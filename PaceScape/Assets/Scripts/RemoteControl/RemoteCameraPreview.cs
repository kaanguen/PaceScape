using System;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using MagicLeap.OpenXR.Features;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Experimental.Rendering;
using UnityEngine.XR.MagicLeap;
using UnityEngine.XR.OpenXR;

namespace PaceScape.RemoteControl
{
    internal sealed class RemoteCameraPreview
    {
        private const int CaptureWidth = 960;
        private const int CaptureHeight = 720;
        private const MLCamera.CaptureFrameRate CaptureRate = MLCamera.CaptureFrameRate._30FPS;
        private const int PreviewIntervalMilliseconds = 100;
        private const int PreviewJpegQuality = 60;
        private const int MaxFrameBytes = 2 * 1024 * 1024;
        private CancellationTokenSource _cancellation;
        private Task _captureTask;
        private string _status = "Stopped";
        private string _error;

        private static bool IsSupported => !Application.isEditor && Application.platform == RuntimePlatform.Android;

        public bool TryStart(Action<byte[]> publish, out string error)
        {
            error = null;
            if (!IsSupported || (_captureTask != null && !_captureTask.IsCompleted))
            {
                error = "Kamera ist nicht verfügbar oder wird noch gestartet oder beendet.";
                return false;
            }

            _error = null;
            _status = "Starting";
            _cancellation = new CancellationTokenSource();
            _captureTask = CaptureAsync(publish, _cancellation);
            return true;
        }

        public void Stop()
        {
            _cancellation?.Cancel();
            if (_cancellation != null)
            {
                _status = "Stopped";
            }
        }

        public RemoteCameraState CaptureState()
        {
            return new RemoteCameraState
            {
                supported = IsSupported,
                enabled = _cancellation != null && !_cancellation.IsCancellationRequested && _status != "Error",
                status = IsSupported ? _status : "Unsupported",
                error = _error
            };
        }

        private async Task CaptureAsync(Action<byte[]> publish, CancellationTokenSource cancellation)
        {
            CancellationToken token = cancellation.Token;
            MLCamera camera = null;
            double videoStartedAt = double.NegativeInfinity;
            MagicLeapFeature perceptionFeature = null;
            TaskCompletionSource<bool> nextFrame = null;
            byte[] rgba = null;
            void OnVideo(MLCamera.CameraOutput output, MLCamera.ResultExtras extras, MLCamera.Metadata metadata)
            {
                // The SDK's native buffer expires as soon as this callback returns.
                TaskCompletionSource<bool> frame = Interlocked.Exchange(ref nextFrame, null);
                if (frame == null || token.IsCancellationRequested)
                {
                    return;
                }

                try
                {
                    CopyRgbaFrame(output, rgba);
                    frame.TrySetResult(true);
                }
                catch (Exception exception)
                {
                    frame.TrySetException(exception);
                }
            }

            try
            {
                if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    _status = "RequestingPermission";
                    var permission = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    var callbacks = new PermissionCallbacks();
                    callbacks.PermissionGranted += _ => permission.TrySetResult(true);
                    callbacks.PermissionDenied += _ => permission.TrySetResult(false);
                    Permission.RequestUserPermission(Permission.Camera, callbacks);
                    if (await Task.WhenAny(permission.Task, Task.Delay(30000, token)) != permission.Task ||
                        !await permission.Task)
                    {
                        token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("Kameraberechtigung auf der Brille fehlt.");
                    }
                }

                token.ThrowIfCancellationRequested();
                _status = "Starting";
                var magicLeap = OpenXRSettings.Instance?.GetFeature<MagicLeapFeature>();
                if (magicLeap != null && !magicLeap.EnablePerceptionSnapshots)
                {
                    perceptionFeature = magicLeap;
                    perceptionFeature.EnablePerceptionSnapshots = true;
                }

                var context = MLCamera.ConnectContext.Create();
                context.Flags = MLCamera.ConnectFlag.MR;
                context.MixedRealityConnectInfo.MRQuality = MLCamera.MRQuality._960x720;
                context.MixedRealityConnectInfo.FrameRate = CaptureRate;
                camera = await MLCamera.CreateAndConnectAsync(context);
                token.ThrowIfCancellationRequested();
                if (camera == null)
                {
                    throw new InvalidOperationException("Hauptkamera der Brille ist belegt oder nicht verfügbar.");
                }

                var stream = new MLCamera.CaptureStreamConfig
                {
                    CaptureType = MLCamera.CaptureType.Video,
                    Width = CaptureWidth,
                    Height = CaptureHeight,
                    OutputFormat = MLCamera.OutputFormat.RGBA_8888
                };
                var config = new MLCamera.CaptureConfig
                {
                    CaptureFrameRate = CaptureRate,
                    StreamConfigs = new[] { stream }
                };
                Check(camera.PrepareCapture(config, out _), "Mixed-Reality-Kamera vorbereiten");
                Check(await camera.PreCaptureAEAWBAsync(), "Belichtung vorbereiten");
                token.ThrowIfCancellationRequested();
                camera.OnRawVideoFrameAvailable_NativeCallbackThread += OnVideo;
                Check(await camera.CaptureVideoStartAsync(), "Mixed-Reality-Aufnahme starten");
                videoStartedAt = Time.realtimeSinceStartupAsDouble;

                // Only request another frame after encoding, so one buffer can serve the whole stream.
                rgba = new byte[CaptureWidth * CaptureHeight * 4];
                while (!token.IsCancellationRequested)
                {
                    Task nextSample = Task.Delay(PreviewIntervalMilliseconds, token);
                    var frame = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    Interlocked.Exchange(ref nextFrame, frame);
                    if (await Task.WhenAny(frame.Task, Task.Delay(4000, token)) != frame.Task)
                    {
                        token.ThrowIfCancellationRequested();
                        throw new InvalidOperationException("Kein Bild von der Brillenkamera empfangen.");
                    }

                    token.ThrowIfCancellationRequested();
                    await frame.Task;
                    byte[] jpeg = await Task.Run(() => EncodeFrame(rgba), token);
                    token.ThrowIfCancellationRequested();
                    if (jpeg == null || jpeg.Length < 4 || jpeg.Length > MaxFrameBytes ||
                        jpeg[0] != 0xff || jpeg[1] != 0xd8 || jpeg[jpeg.Length - 2] != 0xff || jpeg[jpeg.Length - 1] != 0xd9)
                    {
                        throw new InvalidOperationException("Das erzeugte JPEG ist ungültig oder zu groß.");
                    }

                    _status = "Streaming";
                    publish(jpeg);
                    await nextSample;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                _error = exception.Message;
                _status = "Error";
            }
            finally
            {
                if (camera != null)
                {
                    Interlocked.Exchange(ref nextFrame, null);
                    camera.OnRawVideoFrameAvailable_NativeCallbackThread -= OnVideo;
                    try
                    {
                        if (!double.IsNegativeInfinity(videoStartedAt))
                        {
                            // MLCamera requires at least 500 ms between start and stop.
                            int remaining = (int)Math.Ceiling(500 - (Time.realtimeSinceStartupAsDouble - videoStartedAt) * 1000);
                            if (remaining > 0)
                            {
                                await Task.Delay(remaining);
                            }

                            await camera.CaptureVideoStopAsync();
                        }
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[RemoteCamera] Video stop failed: {exception.GetType().Name}");
                    }

                    try
                    {
                        await camera.DisconnectAsync();
                    }
                    catch (Exception exception)
                    {
                        Debug.LogWarning($"[RemoteCamera] Camera cleanup failed: {exception.GetType().Name}");
                    }
                }

                if (perceptionFeature != null)
                {
                    perceptionFeature.EnablePerceptionSnapshots = false;
                }

                _cancellation = null;
                cancellation.Dispose();
            }
        }

        private static void CopyRgbaFrame(MLCamera.CameraOutput output, byte[] rgba)
        {
            if (output.Format != MLCamera.OutputFormat.RGBA_8888 || output.Planes?.Length != 1)
            {
                throw new InvalidOperationException("Die Brille hat kein Mixed-Reality-RGBA-Bild geliefert.");
            }

            var plane = output.Planes[0];
            const int rowBytes = CaptureWidth * 4;
            int stride = plane.Stride == 0 ? rowBytes : checked((int)plane.Stride);
            if (plane.Width != CaptureWidth || plane.Height != CaptureHeight || plane.DataPtr == IntPtr.Zero ||
                stride < rowBytes || plane.Size < (long)(CaptureHeight - 1) * stride + rowBytes)
            {
                throw new InvalidOperationException("Die Brille hat ein unvollständiges Mixed-Reality-Bild geliefert.");
            }

            for (int row = 0; row < CaptureHeight; row++)
            {
                // Reverse the camera's row order for Unity's JPEG encoder.
                int destinationRow = CaptureHeight - 1 - row;
                Marshal.Copy(IntPtr.Add(plane.DataPtr, row * stride), rgba, destinationRow * rowBytes, rowBytes);
            }
        }

        private static byte[] EncodeFrame(byte[] rgba)
        {
            return ImageConversion.EncodeArrayToJPG(rgba, GraphicsFormat.R8G8B8A8_UNorm, CaptureWidth, CaptureHeight, 0, PreviewJpegQuality);
        }

        private static void Check(MLResult result, string operation)
        {
            if (!result.IsOk)
            {
                throw new InvalidOperationException($"{operation}: {result}");
            }
        }
    }

    [Serializable]
    internal sealed class RemoteCameraState
    {
        public bool supported;
        public bool enabled;
        public string status;
        public string error;
    }
}
