// Defines dashboard messages and dispatches validated command payloads.

using System;
using PaceScape.Performance;
using PaceScape.RunningTrack;
using PaceScape.Tracking;
using PaceScape.Treadmill;
using UnityEngine;

namespace PaceScape.RemoteControl
{
    // Public wire fields deliberately match the camelCase JSON contract used by the server.
    [Serializable]
    internal sealed class RemoteMessage
    {
        public string type;
        public int protocol;
        public string role;
        public string token;
        public string commandId;
        public string commandType;
        public RemotePayload payload;
        public bool success;
        public string error;
        public RemoteState state;
    }

    [Serializable]
    internal sealed class RemotePayload
    {
        // Sentinels ensure omitted numeric members fail validation instead of silently becoming zero.
        public RunnerWarmup.Step[] steps;
        public string deviceId;
        public int targetSpeedUnits;
        public string runnerMode;
        public string presentationMode;
        // Zero is outside the allowed offset range and stays valid JSON when Unity serializes an empty payload.
        public float sideOffsetMetres;
        public float visibilityDistanceMetres = -1;
        public float comparisonDistanceMetres = -1;
        public bool useDemoRecords;
        public float paceMinutesPerKilometre = -1f;
        public int spawnLane = -1;
        public float distanceBehindStartLine = -1f;
        public float markerLength = -1f;
        public float startupTimeoutSeconds = -1f;
        public int maxTrackedMarkers = -1;
        public bool placeOnFloor;
        public float queryPadding = -1f;
        public float maxMarkerPlaneDistance = -1f;
        public float minimumNormalAlignment = -1f;
    }

    [Serializable]
    internal sealed class RemoteState
    {
        public string deviceName;
        public bool lanMotorControlEnabled;
        public int cameraControlVersion;
        public RemoteCameraState camera;
        public int treadmillControlVersion;
        public TreadmillState treadmill;
        public bool isEditor;
        public bool testScene;
        public int spawnControlVersion;
        public bool runnerSpawned;
        public bool trackingEnabled;
        public string phase;
        public string startupStatus;
        public string startupError;
        public int placedMarkers;
        public int pendingMarkers;
        public int warmupControlVersion;
        public RunnerWarmup.State warmup;
        public int runnerControlVersion;
        public int runnerModeControlVersion;
        public int ghostControlVersion;
        public int ghostPresentationVersion;
        public int sideBySideVersion;
        public GhostPresentationState ghostPresentation;
        public string runnerMode;
        public int comparisonControlVersion;
        public PerformanceState performance;
        public float activeRunnerPace;
        public string runState;
        public int runnableMarkers;
        public bool canRun;
        public float paceMinutesPerKilometre;
        public int spawnLane;
        public float distanceBehindStartLine;
        public float markerLength;
        public float startupTimeoutSeconds;
        public int maxTrackedMarkers;
        public bool placeOnFloor;
        public float queryPadding;
        public float maxMarkerPlaneDistance;
        public float minimumNormalAlignment;
    }

    // Validates and dispatches supported commands on Unity's main thread.
    public static class RemoteControlCommands
    {
        // Applies BLE commands without routing them through marker or test-scene configuration.
        public static bool TryApplyTreadmill(string json, TreadmillInput treadmill, out string error)
        {
            error = "A treadmill command ID, payload and active input are required.";
            if (treadmill == null)
            {
                return false;
            }

            try
            {
                RemoteMessage message = JsonUtility.FromJson<RemoteMessage>(json);
                if (message == null || message.type != "command" || string.IsNullOrEmpty(message.commandId) ||
                    message.payload == null)
                {
                    return false;
                }

                return treadmill.TryApply(message.commandType, message.payload.deviceId, out error);
            }
            catch (Exception exception)
            {
                error = $"Treadmill command failed ({exception.GetType().Name}).";
                return false;
            }
        }

        // Applies the same wire commands to the marker-free scene target.
        public static bool TryApplyTestScene(string json, ServerTestScene scene, out string error)
        {
            error = "The test scene is unavailable or the command is malformed.";
            if (scene == null)
            {
                return false;
            }

            try
            {
                RemoteMessage message = JsonUtility.FromJson<RemoteMessage>(json);
                if (message == null || message.type != "command" || string.IsNullOrEmpty(message.commandId) ||
                    message.payload == null)
                {
                    return false;
                }

                return scene.TryApply(message, out error);
            }
            catch (Exception exception)
            {
                error = $"Test scene command failed ({exception.GetType().Name}).";
                return false;
            }
        }

        // Applies one JSON command; success means applied or startup requested, not device readiness.
        public static bool TryApply(string json, TagTracker tracker, MarkerPlanePlacement placement, out string error)
        {
            error = null;
            if (tracker == null)
            {
                error = "The scene tracker is unavailable.";
                return false;
            }

            RemoteMessage message;
            try
            {
                message = JsonUtility.FromJson<RemoteMessage>(json);
            }
            catch (Exception)
            {
                error = "Invalid command JSON.";
                return false;
            }

            if (message == null || message.type != "command" || string.IsNullOrEmpty(message.commandId) ||
                message.payload == null)
            {
                error = "A command ID and payload are required.";
                return false;
            }

            RemotePayload payload = message.payload;
            switch (message.commandType)
            {
                case "exercise.startWarmup":
                    if (tracker.TryStartWarmup(payload.steps))
                    {
                        return true;
                    }
                    error = "Warm-up requires a tracked headset in Ready, a visible coach and 1–20 supported exercises of 1–300 seconds.";
                    return false;
                case "exercise.nextWarmup":
                    if (tracker.TryNextWarmupExercise())
                    {
                        return true;
                    }
                    error = "No active warm-up.";
                    return false;
                case "exercise.stopWarmup":
                    if (tracker.StopWarmup())
                    {
                        return true;
                    }
                    error = "No active warm-up.";
                    return false;
                case "runner.presentation":
                    if (tracker.TryConfigureGhostPresentation(payload.presentationMode, payload.sideOffsetMetres,
                        payload.visibilityDistanceMetres))
                    {
                        return true;
                    }
                    error = "Reset before changing presentation. Use Track or TreadmillRelative, side offset ±0.5–5 m and visibility distance 1–100 m. Relative mode needs at most one placement.";
                    return false;
                case "runner.calibrate":
                    if (tracker.TryCalibrateGhostPresentation())
                    {
                        return true;
                    }
                    error = "Calibration requires Ready, treadmill-relative mode, one tracked marker and a tracked head pose. Face the treadmill running direction.";
                    return false;
                case "runner.comparison":
                    if (tracker.PerformanceSource != null && tracker.PerformanceSource.TryConfigure(
                        payload.comparisonDistanceMetres, payload.useDemoRecords))
                    {
                        return true;
                    }
                    error = "Reset before changing comparison distance; pause before changing demonstration settings. Use whole metres, 1–1000000.";
                    return false;
                case "runner.mode":
                    if (tracker.TrySetRunnerMode(payload.runnerMode))
                    {
                        return true;
                    }
                    error = "Use FixedPace, AllTimePersonalBest, or SideBySide. SideBySide requires a reset and at most one marker.";
                    return false;
                case "runner.configure":
                    if (tracker.TryConfigureRunner(payload.paceMinutesPerKilometre, payload.spawnLane,
                        payload.distanceBehindStartLine))
                    {
                        return true;
                    }

                    error = "Invalid runner settings. Reset the run before changing lane or start position.";
                    return false;
                case "run.start":
                    if (tracker.TryStartRun(payload.paceMinutesPerKilometre, payload.spawnLane,
                        payload.distanceBehindStartLine))
                    {
                        return true;
                    }

                    error = "Start requires a waiting run, valid settings and all placed objects visibly tracked.";
                    return false;
                case "run.pause":
                    if (tracker.TryPauseRun())
                    {
                        return true;
                    }

                    error = "Only a running session can be paused.";
                    return false;
                case "run.resume":
                    if (tracker.TryResumeRun())
                    {
                        return true;
                    }

                    error = "Resume requires a paused run and all placed objects visibly tracked.";
                    return false;
                case "run.reset":
                    tracker.ResetRun();
                    return true;
                case "runner.pace":
                    if (tracker.TrySetPace(payload.paceMinutesPerKilometre))
                    {
                        return true;
                    }
                    break;
                case "runner.spawn":
                    if (tracker.TrySetSpawn(payload.spawnLane, payload.distanceBehindStartLine))
                    {
                        return true;
                    }
                    break;
                case "tracking.configure":
                    if (tracker.TryConfigureTracking(payload.markerLength, payload.startupTimeoutSeconds,
                        payload.maxTrackedMarkers))
                    {
                        tracker.RetryTracking();
                        return true;
                    }
                    break;
                case "placement.configure":
                    if (placement != null && placement.TryConfigure(payload.placeOnFloor, payload.queryPadding,
                        payload.maxMarkerPlaneDistance, payload.minimumNormalAlignment))
                    {
                        tracker.RetryTracking();
                        return true;
                    }
                    break;
                case "tracking.start":
                case "tracking.retry":
                    if (Application.isEditor || Application.platform != RuntimePlatform.Android)
                    {
                        error = "Tracking requires a Magic Leap 2 device build.";
                        return false;
                    }

                    if (!tracker.gameObject.activeInHierarchy)
                    {
                        error = "The tracker GameObject is inactive.";
                        return false;
                    }

                    if (!tracker.enabled)
                    {
                        tracker.enabled = true;
                    }
                    else if (message.commandType == "tracking.retry")
                    {
                        tracker.RetryTracking();
                    }
                    return true;
                case "tracking.stop":
                    tracker.enabled = false;
                    return true;
            }

            error = "Unsupported command or invalid parameters.";
            return false;
        }
    }
}
