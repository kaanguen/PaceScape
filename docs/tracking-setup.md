# PaceScape — Tracking setup

PaceScape is a Unity project for augmented reality running training. The planned app overlays a virtual running track and an animated running partner onto the real environment, with treadmill and stadium training, live running statistics, and control through a web dashboard.

The project is in its initial setup stage. A simple Magic Leap 2 OpenXR AprilTag tracker is implemented; the tracking scene is configured; device verification is still required. The running features are not implemented yet.

## Getting started

1. Install **Unity 2022.3.62f3** and **Git LFS**.
2. After cloning, run `git lfs install` and `git lfs pull` in the repository folder.
3. In Unity Hub, add and open the **`PaceScape/` subfolder** as the Unity project.
4. Open `Assets/Scenes/MarkerTrackingScene.unity`.

Binary assets are managed with Git LFS. Contributor instructions are in [AGENTS.md](../AGENTS.md).

Before testing on your own hardware, read the [testing disclaimer](../DISCLAIMER.md).

## Simple AprilTag tracking (Magic Leap 2)

`Assets/Scripts/Tracking/TagTracker.cs` uses the Magic Leap OpenXR Marker Understanding API with **AprilTag 36H11** and the default detector profile. It requests marker tracking, spatial anchor, and spatial mapping permissions. `Assets/Scripts/Tracking/MarkerPlanePlacement.cs` uses each marker as a placement trigger, projects its horizontal position vertically onto a detected floor, and then creates a local spatial anchor for the running app. MarkerML2 was used as a read-only reference; persistent anchor storage, running logic, and BLE are outside this implementation.

1. Let Unity import and compile the script; check the Console for errors and warnings.
2. In Android **XR Plug-in Management**, enable **OpenXR** and automatic XR initialization. In OpenXR settings, enable **Magic Leap 2 Support**, **Magic Leap 2 Marker Understanding**, and **Magic Leap 2 Spatial Anchor Subsystem**, and **Magic Leap 2 Planes Subsystem**; resolve relevant Project Validation issues. The SDK is already installed; no additional package upgrade is needed for this script.
3. The scene already contains an active **AR Session** for the AR Foundation lifecycle, an **XR Origin**, **Camera Offset**, and one tracked **Main Camera**, with headset position, rotation, and tracking-state bindings. Keep the rig scale at `(1, 1, 1)`, the origin upright, and Camera Y Offset / Camera Offset at zero. Startup rejects incompatible transforms instead of mixing marker and plane coordinate spaces.
4. **AprilTag Tracking** already contains **Tag Tracker** with the XR Origin, its **AR Anchor Manager**, and **Marker Plane Placement** assigned. The XR Origin also has an **AR Plane Manager** using `Assets/Prefabs/DetectedPlane.prefab`; it stays disabled until the startup pipeline has checked permissions and OpenXR. A query begins around the headset, then is rebuilt from current pending marker positions; it does not accumulate historical observations. Floor mode uses search extents of at least 20 m in all axes, matching the official SDK query example. This keeps the query broad enough to include large floor surfaces whose detected centers are farther from the marker. This is a search-volume setting, not a placement distance or accuracy guarantee. Keep only one tracker component in the scene. Leave **Marker Prefab** empty for a default cube, or assign a visual prefab whose scale is already configured in meters.
5. Set **Marker Length** to the printed square's actual side length in meters, including the black border and excluding the surrounding white margin. The initial `0.1` value is only a 10 cm example. All tags handled by this detector must have that size. Settings are read when the component is enabled.
6. Confirm marker tracking, spatial anchor, and spatial mapping permissions are enabled under **Magic Leap > Manifest Settings**. The Android manifest declares `com.magicleap.permission.MARKER_TRACKING`, `com.magicleap.permission.SPATIAL_ANCHOR`, and `com.magicleap.permission.SPATIAL_MAPPING`. Grant all three when requested.
7. The scene is saved and enabled at build index 0. Create a new Magic Leap 2 device build, and grant the permission on the headset. Present a printed **36H11** tag with a white margin and good lighting.

Show a marker, then look at the surrounding floor. **Place On Floor** is enabled in the tracking scene. Placement uses the marker's world X/Z coordinates and computes its height from the detected floor plane. Marker height and normal alignment do not gate floor placement. A candidate must be actively tracked, classified as **Floor**, horizontally aligned, and its boundary must contain the vertically projected point. Walls, tables, unclassified planes, and points outside the floor boundary are rejected; no object is created while waiting for a floor. When several floor surfaces cover the point, the one with the smallest height difference from the marker pose is selected. Planes are detected but not rendered.

The floor supplies the object's upward normal; the marker supplies its heading. This corrects the placement height but does not correct marker X/Z errors or establish measured tracking accuracy. **Max Marker Plane Distance** and **Minimum Normal Alignment** apply only when **Place On Floor** is disabled; that legacy surface-placement mode retains the previous 0.15 m / 0.9 defaults.

Marker observations are retained in OpenXR tracking space while plane extraction finishes, following MarkerML2's deferred placement approach. After a marker has been observed, looking at the surrounding surface can complete placement even when the marker is temporarily out of view. Keep the physical marker stationary until placement; a newer observation updates its pending pose. Pending poses are cleared when tracking stops.

Each marker ID is anchored once at the projected point on the selected plane. In floor mode the object's local +Y axis follows the upward floor normal; its heading follows the tag's top edge projected into the surface. Renderer bounds place the object's bottom directly on the plane, accounting for prefab size and pivot. This checks contact at the projected center, not whether the entire object footprint fits inside the plane boundary.

Later marker observations and plane refinements do not move an existing placement. The local spatial anchor maintains its pose and may refine it as tracking improves. Removing or moving the printed marker leaves the anchored object in place. The visual hides while anchor tracking is unavailable and reappears when it recovers. Disabling the tracker, ending the XR session, or closing the app clears these local anchors and stops plane scanning. Re-enabling starts a new placement session. No anchor is published or restored after an app restart. Use unique marker IDs for distinct positions.

Tracking runs on the headset, not in ordinary Editor Play mode. Verify floor detection and surface contact, placement despite a marker height error, waiting behavior with no floor, rejection of walls/tables and non-overlapping surfaces, position and rotation, marker removal/movement after placement, anchor tracking loss/recovery, permission denial, and component disable/re-enable on the device. These checks have not yet been performed.

References: [Magic Leap Marker Understanding overview](https://developer-docs.magicleap.cloud/docs/guides/unity-openxr/marker-understanding/marker-understanding-overview/), [OpenXR API overview](https://developer-docs.magicleap.cloud/docs/guides/unity-openxr/marker-understanding/unity-marker-understanding-api/), and [AprilTag dictionaries](https://developer-docs.magicleap.cloud/docs/unity-api/api/MagicLeap/OpenXR/Features/MarkerUnderstanding/).

Local anchor reference: [Magic Leap OpenXR Spatial Anchor API](https://developer-docs.magicleap.cloud/docs/guides/unity-openxr/spatial-anchors/unity-spatial-anchor-api/).

Plane detection reference: [Magic Leap OpenXR Querying Planes](https://developer-docs.magicleap.cloud/docs/guides/unity-openxr/plane-detection/openxr-unity-planes-query/).

## Tracking startup pipeline

`TagTracker` starts tracking in this order:

1. **WaitingForScene**: allow scene components to activate for one frame.
2. **CheckingConfiguration**: require an active AR Session and resolve the XR Origin, camera, anchor manager, and plane placement references. Keep both managers stopped.
3. **RequestingPermissions**: request marker tracking, spatial mapping, and spatial anchors one at a time. Log the exact permission being requested and each grant. Wait for the user's decision; a denied permission identifies itself in the failure.
4. **WaitingForOpenXR**: wait for automatic XR initialization, the OpenXR loader, and a running XR display.
5. **CheckingFeatures**: verify the Magic Leap marker, anchor, and plane features and their runtime extensions.
6. **StartingSubsystems**: enable the anchor and plane managers and wait for both subsystems to run.
7. **StartingMarkerDetector**: create the AprilTag 36H11 detector and wait for its Ready status.
8. **Ready**: process marker observations and place objects on matching planes.

The **Startup Timeout Seconds** setting defaults to 15 seconds of active foreground time for startup steps, recovery, and a new anchor becoming tracked. App pauses and lost focus do not consume this timeout. Permission prompts are not timed out. Inspector fields **Startup Phase**, **Startup Status**, and **Startup Error** expose progress. Device logs use the `[TrackingStartup]` prefix and include active elapsed time. Per-marker and detailed plane diagnostics are limited to Editor / Development Builds. Ready means that the tracking services are ready; it does not mean a marker or suitable plane has been detected yet.

Failures retain the phase and error, release owned resources, and leave the component available for an explicit `RetryTracking()` call. Disabling the component cancels all operations, invalidates old permission callbacks, clears local content, and stops owned managers. Permission failures can retry automatically when the app regains focus after permissions have been restored. Ordinary Editor Play mode reports **UnsupportedPlatform** rather than attempting device APIs.

## Runtime lifecycle and limits

- Pause or focus loss hides content immediately. Resume waits for the same XR session to recover and retains its local anchors. If the SDK has removed the detector because the session ended, all old poses and anchors are discarded and startup runs again; scan the markers again.
- Short anchor tracking loss hides its visual without relocating the anchor. An anchor that never becomes tracked fails after the configured active-time timeout instead of remaining invisible forever.
- `Max Tracked Markers` defaults to 64, counting both pending and placed IDs. At capacity, new IDs are ignored; existing pending IDs can still update. Pending poses remain until placement or session cleanup; physical markers must remain stationary while waiting.
- NaN, infinity, zero quaternions, invalid boundaries, invalid configuration, and unsupported rig transforms are rejected before native queries or anchor creation.
- Native SDK polling and creating a new visual/anchor can allocate. The project does not claim zero allocation per frame; its own detailed diagnostic strings are suppressed in release builds. Verify GC, CPU, and memory behavior on the headset before a release.

## Automated verification

`Assets/Tests/Editor/TrackingTests.cs` contains EditMode regression tests in the `PaceScape.Tracking.EditorTests` assembly. Run that assembly through Unity's Test Runner. Tests cover floor projection, classification, boundaries, invalid device data, strict-mode distance behavior, initialization guards, capacity, cleanup, pause timing, session invalidation, anchor timeouts, and retained failures.

These are device-independent checks; they do not emulate Magic Leap's native services. A release still requires a new device build and checks for permissions, pause/resume, session replacement, tracking loss/recovery, failed anchors, repeated enable/disable, stable placement, and sustained runtime performance. The earlier marker/floor position disagreement and X/Z accuracy remain target-device validation items; the floor projection is not an accuracy guarantee.
