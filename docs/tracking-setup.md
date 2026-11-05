# PaceScape — Tracking setup

PaceScape is a Unity project for augmented reality running training. The planned app overlays a virtual running track and an animated running partner onto the real environment, with treadmill and stadium training, live running statistics, and control through a web dashboard.

The project is in its initial setup stage. A simple Magic Leap 2 OpenXR AprilTag tracker is implemented; the tracking scene is configured; device verification is still required. The marker-spawned oval and configurable moving cube are implemented; animated companions and connected training data remain planned.

## Getting started

1. Install **Unity 2022.3.62f3** and **Git LFS**.
2. After cloning, run `git lfs install` and `git lfs pull` in the repository folder.
3. In Unity Hub, add and open the **`PaceScape/` subfolder** as the Unity project.
4. Open `Assets/Scenes/MarkerTrackingScene.unity`.

Binary assets are managed with Git LFS. Contributor instructions are in [AGENTS.md](../AGENTS.md).

Before testing on your own hardware, read the [testing disclaimer](../DISCLAIMER.md).

## Simple AprilTag tracking (Magic Leap 2)

`Assets/Scripts/Tracking/TagTracker.cs` uses the Magic Leap OpenXR Marker Understanding API with **AprilTag 36H11** and the default detector profile. It requests marker tracking, spatial anchor, and spatial mapping permissions. `Assets/Scripts/Tracking/MarkerPlanePlacement.cs` uses each marker as a placement trigger, projects its horizontal position vertically onto a detected floor, and then creates a local spatial anchor for the running app. MarkerML2 was used as a read-only reference; persistent anchor storage and BLE are outside this implementation.

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

## Marker-spawned running track

`TagTracker.PopulateVisual` now creates an initially hidden content root containing both the marker object and `RunningTrackVisual`. The existing anchor tracking, pause/resume, failure, retry, and cleanup lifecycle controls the entire root. Each distinct marker ID gets its own object and oval once per local session; repeated observations do not duplicate placed content. No extra scene wiring or packages are needed.

The track follows MarkerML2's `StadiumTrackGeometry` and `TreadmillOvalSetup`: two 84.39 m straights, 36.50 m inner radius, eight 1.22 m lanes, white boundaries, yellow edge ticks, a transverse entry line, and a direction arrow. The actual lane-one measurement-line circumference from those rounded dimensions is approximately 400.001 m. The marker lies on that measurement line, 0.30 m outside the inner boundary, at MarkerML2's nominal 390 m entry point (ten nominal metres before the bend). Anchor-local +Z follows the printed top edge projected onto the detected floor, as provided by `MarkerPlanePlacement`. The marker object is grounded separately before the track is added; its scale does not scale the oval. The object is centered on the selected lane (`Spawn Lane`, 1–8; default 1, with 0.31 m outward offset from the marker measurement line) and placed 0.50 m behind the start line, opposite to the initial running direction. `Distance Behind Start Line` on `TagTracker` defines this visual offset; the dashboard can also update already placed waiting objects; the floor anchor and track entry remain at the marker.

Only the markings are drawn, leaving the real floor visible. The generated mesh sits 1 cm above the anchor's floor plane to limit z-fighting; it is a full-size oval, not a miniature. The entire oval is coplanar with that one detected surface and does not conform to terrain. A single marker does not establish surveyed alignment with a physical stadium. Legacy strict surface placement also aligns the oval to that selected surface; use floor mode for the running track.

`Resources/PaceScapeTrack.shader` is bundled so runtime material creation also works in a device build, and includes URP stereo instancing support. All active track instances share one immutable mesh and one material. Reference-counted ownership releases both after the last track is removed, including content that was never visible; cleanup is idempotent. The unlit mesh needs no normals or retained CPU-side copy. Multiple distinct markers generate multiple full-size ovals; device memory and rendering cost still need measurement.

Run the existing `PaceScape.Tracking.EditorTests` assembly, including `RunningTrackTests`, for entry alignment, closed lane edges, oval lengths, shared visibility, separate object grounding, and generated-resource cleanup. Editor rendering and compilation do not verify marker alignment on hardware. Build the updated app and check object/track coappearance, printed-top-edge heading, scale, floor height, tracking loss/recovery, pause/resume, and retry on Magic Leap 2.

### Runner movement

`TagTracker` exposes `Pace Minutes Per Kilometre` (default 6 = 6:00 min/km = 10 km/h), `Spawn Lane` (1–8), and `Distance Behind Start Line`. Pace changes affect existing objects immediately. Server commands configure pace, lane and start offset together; positional changes also reset existing waiting objects to that start, and are rejected during Running or Paused until reset. Decimal pace values are decimal minutes (6.5 = 6:30), not minutes/seconds notation.

`RunningTrackMotion` advances the object along the lane centerline and turns it into the tangent direction. Each lane uses its actual centerline radius and circumference, so physical speed stays constant through bends and outer lanes take longer to complete. The track and anchor stay fixed. Objects remain still after placement until an explicit `run.start` command. `RunningSession` gates movement separately from anchor visibility. Tracking loss and backgrounding pause the run; recovery restores visibility but requires explicit `run.resume`. Server disconnect or a missing application heartbeat also pauses movement. `run.reset` returns existing objects to the configured start, retains their anchors/track, and waits for another start. New marker placements are deferred while Running or Paused, keeping the active set stable. Movement uses Unity's unscaled frame time, keeping the configured physical pace independent of timeScale. The first visible frame after tracking recovery does not advance the object. No treadmill input, animation, or race timing is added. The transport heartbeat timeout is a technical liveness setting, not a training parameter.

## SDK anchor completion correction

The Magic Leap SDK remains at version 2.6.0, now embedded through Unity Package Manager. Its local anchor completion path coalesces an initial add and a fast completion into one tracked addition, preventing AR Foundation's duplicate-action validation exception and the resulting anchor timeout seen on the device. See [the patch notes and device verification steps](magicleap-anchor-fix.md). `AnchorChangeTests` checks the original failure and both fast and delayed completion through the actual provider change batch and Unity validator. Hardware confirmation requires a new build.

## Runtime hardening

Startup rejects non-finite or nonpositive pace, lanes outside 1–8, non-finite or negative start offsets, inactive marker prefabs, and prefabs with ARAnchor or Rigidbody components. The tracker owns the anchor and movement. Existing runners hold position for invalid live pace values; constructors reject invalid start data before moving an object. One new marker placement is created per frame, bounded by the existing marker capacity. Content is hidden and movement paused unless the OpenXR head device reports both position and rotation tracked.

The SDK patch also cancels pending anchor futures rather than trying to destroy a nonexistent native space. Session-ended removal clears managed anchors and pending/ready futures; unreported additions disappear without generating invalid same-batch removal events. Native cancellation results still require verification on hardware. See [production verification status](production-readiness.md) for completed and outstanding checks.

## Local WebSocket control

The separate PaceScape Control dashboard now has a runtime client in `Assets/Scripts/RemoteControl/`. It exposes the explicit run start/pause/resume/reset, unified pace/lane/offset settings, tracker lifecycle and detector/plane configuration. See [remote control setup and verification](remote-control.md). The older RunningServer protocol and BLE remain unconfigured. A new device build is required; server tests and the simulator do not verify Unity or headset behavior.
