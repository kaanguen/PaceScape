# Remote camera preview and session records

The remote-control root adds `RemoteCameraStream` at runtime; no scene or prefab change is required. The dashboard sends explicit `camera.start` / `camera.stop` commands. The Android build requests CAMERA permission after start, opens the Main mixed-reality camera, and emits up to two 960×720 JPEG stills per second. Stream capabilities are checked before capture. JPEG encoding uses the camera's default quality, and a frame is capped at 2 MiB. Only one capture and one unsent frame can be pending; older unsent frames are discarded. These are preview limits, not motion-analysis measurements.

The SDK's Main-camera mixed-reality API supplies real and virtual content. Capture stops on application pause/disable or lost remote connection and is never automatically resumed. The server forwards binary JPEGs only while headset state is fresh and enabled. Frames are not recorded or replayed. The browser clears stale frames after three seconds. The Skeleton UI is a labelled placeholder, not a pose-estimation implementation.

`RunnerPerformance` reports `sessionRecordVersion`, the prior real record at session reset, and whether a measured effort improved the record book during this session. The dashboard confirms a PR only after storage completes without error. Existing real records, measurement validity and independent ghost behavior are preserved.

## Verification

- Remote camera start/stop capability checks, JPEG relay, invalid senders, stale telemetry, session PR confirmation and invalid progress handling have automated tests in PaceScape Control.
- Runtime sources compile against the installed Unity 2022.3.62f3 / Magic Leap SDK assemblies with the Android camera branch enabled. This is a source/API check, not a Unity player build.
- Pending on-device checks: CAMERA permission granted/denied, mixed-reality image orientation, camera/AprilTag tracking coexistence, image rate and resource use, low-bandwidth behavior, stop/pause/reconnect, and persisted PR behavior across runs.

A new Android build, installation on Magic Leap 2 and a restart of the control server are required. No scene assets were edited.

References: [Magic Leap camera capture](https://developer-docs.magicleap.cloud/docs/guides/unity/camera/camera-capture-walkthrough/), [camera formats and mixed reality](https://developer-docs.magicleap.cloud/docs/guides/features/ml-camera/).
