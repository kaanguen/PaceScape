using System;
using PaceScape.Tracking;
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
        public bool isEditor;
        public bool trackingEnabled;
        public string phase;
        public string startupStatus;
        public string startupError;
        public int placedMarkers;
        public int pendingMarkers;
        public int runnerControlVersion;
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

    /// <summary>Validates and dispatches supported commands on Unity's main thread.</summary>
    public static class RemoteControlCommands
    {
        /// <summary>Applies one JSON command; success means applied or startup requested, not device readiness.</summary>
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
