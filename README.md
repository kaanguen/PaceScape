# PaceScape

PaceScape is a Unity project for augmented reality running training. The planned app overlays a virtual running track and an animated running partner onto the real environment, with treadmill and stadium training, live running statistics, and control through a web dashboard.

The project is in its initial setup stage. A simple Magic Leap 2 OpenXR AprilTag tracker is implemented; the tracking scene is configured; device verification is still required. The running features are not implemented yet.

## Getting started

1. Install **Unity 2022.3.62f3** and **Git LFS**.
2. After cloning, run `git lfs install` and `git lfs pull` in the repository folder.
3. In Unity Hub, add and open the **`PaceScape/` subfolder** as the Unity project.
4. Open `Assets/Scenes/MarkerTrackingScene.unity`.

Binary assets are managed with Git LFS. Contributor instructions are in [AGENTS.md](AGENTS.md).

Before testing on your own hardware, read the [testing disclaimer](DISCLAIMER.md).

## Simple AprilTag tracking (Magic Leap 2)

`Assets/Scripts/Tracking/TagTracker.cs` uses the Magic Leap OpenXR Marker Understanding API with **AprilTag 36H11** and the default detector profile. It requests marker tracking and spatial anchor permissions. Each first detection creates a local Magic Leap OpenXR spatial anchor for the running app. MarkerML2 was used as a reference; floor detection, persistent anchor storage, running logic, and BLE are outside this implementation.

1. Let Unity import and compile the script; check the Console for errors and warnings.
2. In Android **XR Plug-in Management**, enable **OpenXR** and automatic XR initialization. In OpenXR settings, enable **Magic Leap 2 Support**, **Magic Leap 2 Marker Understanding**, and **Magic Leap 2 Spatial Anchor Subsystem**, and resolve relevant Project Validation issues. The SDK is already installed; no additional package upgrade is needed for this script.
3. The scene already contains an **XR Origin**, **Camera Offset**, and one tracked **Main Camera**, with headset position, rotation, and tracking-state bindings. Keep the rig scale at `(1, 1, 1)`.
4. **AprilTag Tracking** already contains **Tag Tracker** with the XR Origin and its **AR Anchor Manager** assigned. Keep only one tracker component in the scene. Leave **Marker Prefab** empty for a default cube, or assign a visual prefab whose scale is already configured in meters.
5. Set **Marker Length** to the printed square's actual side length in meters, including the black border and excluding the surrounding white margin. The initial `0.1` value is only a 10 cm example. All tags handled by this detector must have that size. Settings are read when the component is enabled.
6. Confirm marker tracking and spatial anchor permissions are enabled under **Magic Leap > Manifest Settings**. The Android manifest declares `com.magicleap.permission.MARKER_TRACKING` and `com.magicleap.permission.SPATIAL_ANCHOR`. Grant both when requested.
7. The scene is saved and enabled at build index 0. Create a new Magic Leap 2 device build, and grant the permission on the headset. Present a printed **36H11** tag with a white margin and good lighting.

Each marker ID is anchored once at its first valid position and rotation. The visual is a child of that anchor with identity local rotation. Its renderer bounds are used once at creation to place its nearest face directly against the marker XY plane, on the side facing the headset. This accounts for prefab size and pivot without adding a fixed world-height offset. The anchor itself stays at the measured marker center. Later marker observations do not move it, and removing or moving the printed marker does not remove or reposition the anchored object. The visual appears once the anchor is tracked, hides while anchor tracking is unavailable, and reappears when anchor tracking recovers. AR Foundation may refine the anchor pose as spatial tracking improves. Disabling the tracker, ending the XR session, or closing the app clears these local anchors; re-enabling starts a new placement session. No anchor is published or restored after an app restart. Use unique marker IDs for distinct positions.

Tracking runs on the headset, not in ordinary Editor Play mode. Verify detection, position and rotation, marker removal/movement after placement, anchor tracking loss/recovery, permission denial, and component disable/re-enable on the device. These checks have not yet been performed.

References: [Magic Leap Marker Understanding overview](https://developer-docs.magicleap.cloud/docs/guides/unity-openxr/marker-understanding/marker-understanding-overview/), [OpenXR API overview](https://developer-docs.magicleap.cloud/docs/guides/unity-openxr/marker-understanding/unity-marker-understanding-api/), and [AprilTag dictionaries](https://developer-docs.magicleap.cloud/docs/unity-api/api/MagicLeap/OpenXR/Features/MarkerUnderstanding/).

Local anchor reference: [Magic Leap OpenXR Spatial Anchor API](https://developer-docs.magicleap.cloud/docs/guides/unity-openxr/spatial-anchors/unity-spatial-anchor-api/).
