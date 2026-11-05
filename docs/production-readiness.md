# Production verification status

Scope: the existing AprilTag-triggered oval and moving cube, not the planned companion, BLE integration, dashboard, or scientific training validation.

Status: runtime hardening and Editor regression checks completed on 2026-11-04. Production release is not approved: the new runtime behavior and SDK changes still require device confirmation. No training efficacy, placement accuracy, surveyed stadium alignment, or performance threshold is inferred from these checks.

## Completed

- Unity 2022.3.62f3 / Magic Leap SDK 2.6.0 retained; Android target, x86_64, IL2CPP configuration inspected. The APK contains x86_64 native libraries, matching the Magic Leap 2 target.
- 60 EditMode regression cases passed, none skipped: projection and startup lifecycle, lane/pace geometry, lap closure, invalid values, resource ownership, and native-independent SDK change batching and cleanup.
- No C# errors reported by Unity MCP after the final import. Existing SDK deprecation warnings are not treated as proof of failure or silently removed.
- One mesh/material shared by track instances; resources released after the last owner. Mesh upload discards its CPU copy; the unlit mesh has no unused normals.
- One new placement per frame; bounded marker count; validation of pace, lane, start offset, and visual prefab ownership.
- Head pose must report position and rotation tracked for visibility. Hidden/background/recovering content does not advance. Movement uses unscaled frame time.
- Local anchor completion merges fast add/update events; pending anchors cancel futures rather than destroying space zero; session-ended cleanup clears managed state and emits consistent change batches.

## Build and visual evidence

Android verification build succeeded: 0 errors, 26 warnings, 206.6 seconds, Development disabled. The APK is approximately 39 MiB at `/tmp/pacescape-readiness/PaceScape.apk`; SHA-256: `a6453ca661b2a45c90e10d2cf2ee3886998a1e590461196ac11759dad70d0588`. The build report also counts debug/support artifacts; its approximately 740.84 MB total is not the APK download size. A verification APK is not a store-signed distribution release. Signing credentials, distribution destination, and a release/versioning process have not been configured as part of this work.

## Device checks still required

1. Install the new verification build; confirm headset permissions and scan the stationary AprilTag on a detected floor. Check that object and track become visible without duplicate-ID exceptions or anchor timeout.
2. Check printed-top-edge heading, selected spawn lane, object center and floor contact, direction of travel, bend following, and lap closure. Visual placement from one marker is not surveyed stadium calibration.
3. Check 6:00 min/km and a changed pace against elapsed foreground time. Pace is decimal minutes; 6.5 means 6:30. Lane/start offset changes apply to new placements.
4. Lose and recover head tracking, look away from the tag, suspend/resume, and restart tracking. Check that movement resumes without catching up hidden/background time and that object and track hide/reappear together.
5. Deny/restore permissions; stop while an anchor is pending; repeat placement/retry/enable/disable. Check native cancellation, failure recovery, and absence of orphaned or duplicate content.
6. Profile CPU, GPU, GC, memory, temperature, and visual stability on the headset with the intended number of markers and the agreed session duration. Shared meshes reduce geometry memory, but every visible oval still adds rendering work.

SDK limitation requiring explicit device review: the 2.6.0 provider's native local-anchor completion path does not implement continuous per-anchor pose-validity polling after creation. The new head-pose gate hides content on global tracking loss, but it does not independently prove that each anchor's SDK state is fresh. Check anchor refinement and tracking loss/recovery before declaring the app production ready; an upstream fix or a separately validated native polling change may be needed.

The conceptual/scientific owner must define acceptable position/heading error and training evaluation criteria. No such acceptance numbers are chosen by this engineering pass.

## Local remote control addition

- Implemented: separate PaceScape Control HTTP/WebSocket server and dashboard in `/Users/kaan/Work/PaceScapeControl/`; auto-created scene client, main-thread command dispatcher, validated setting APIs, Android Internet/cleartext declarations and EditMode regression tests.
- Server verification: 13 local server/simulator tests pass. JavaScript syntax checks pass. Browser simulation verifies unified settings, start, pause, changed pace, resume and reset; this is not device evidence.
- Pending: Unity compilation and `PaceScape.Tracking.EditorTests` (including `RemoteControlTests` and `RunControlTests`); device build, USB forwarding, Android ClientWebSocket/IL2CPP behavior, LAN setup, lifecycle/reconnect, marker configuration restarts and the actual tracking/placement effects.
- See [remote control](remote-control.md) for the setup, technical input limits and limitations.

- Explicit run control is now implemented through `RunningSession`, tracker APIs and the dashboard. Confirm that no placed object advances before explicit start, pause/resume preserves position, reset preserves anchors, positional edits are blocked until Ready, all runners pause together on tracking loss, and disconnect/heartbeat loss cannot silently resume movement. These Unity/device checks remain pending.

## Editor server connection tool

Implemented: **PaceScape > Serververbindung** (UI Toolkit), validated project connection defaults saved as a Resources asset through Unity, explicit Play-mode reconnect that pauses a run, and retained device JSON overrides. Pending: Unity compilation, `RemoteControlSettingsTests`, window layout/persistence, switching between actual endpoints in Play mode, and a new device build using saved defaults. No compile, UI, network reconnect or device pass is claimed for this addition.

The settings window also reads the Editor computer's local IPv4 interfaces on request, with a selector when multiple addresses exist. It does not discover a remote server or prove network reachability. Verify adapter choice, retained port and manual fallback in the Editor; no runtime server protocol change is required.

## FTMS treadmill input addition

Implemented on 2026-11-05: the user's embedded BLE package and pinned Velorexe dependency, Android BLE permissions, read-only FTMS connection commands and telemetry, dashboard device selection, and treadmill-driven runner speed. Zero speed stops movement; missing/stale speed pauses with explicit resume and no manual fallback. No real motor commands were added. See [treadmill setup](treadmill.md).

Verification: 15 server/simulator tests passed both in a temporary copy and in the updated server repository, including new BLE command validation/relay and zero-speed telemetry cases. Browser checks used a labelled local fixture: 12 km/h shows 5:00 min/km, treadmill mode disables the manual slider, disconnection clears speed without restoring manual mode, and selecting manual mode keeps the run paused and re-enables the slider. These do not verify BLE transport or hardware. New `TreadmillTests` are present but have not run: the Unity MCP connection became unavailable after package installation. Unity compilation, EditMode tests, a fresh device build and the complete headset/treadmill checks remain pending; the exact Domyos model has not been confirmed.

## Independent Tempo Runner addition

Implemented: `FollowInput` / `TempoRunner` selection, own target independent of BLE, position-preserving pause on mode change, retained selection on reset, runtime/marker-free test-scene protocol, dashboard controls and simulator support. User scope excludes best-lap recording/playback. Server/simulator tests in the prepared and updated repository: 17 passed; labelled browser fixture checks mode switch, explicit resume, independent target at 0 km/h and BLE loss, live pace edits, and blocked follower resume without data. New Unity regressions are present but unexecuted: Unity MCP remained unavailable. Compilation, Test Runner, new device build and actual movement/animation/lifecycle checks remain pending. See [Tempo Runner](tempo-runner.md).

## Current-run and all-time comparison addition

Implemented: fixed-distance laps/history, fastest continuous windows, current-run and persisted all-time average pace, 400 m/custom distance configuration, measured/demo separation and dashboard statistics. 109 EditMode tests completed without reported failures; includes independent rolling-window comparison, genuine file replacement/backup, JSON validation, and existing regressions. Hardware BLE sampling accuracy, Android persistence/flush, lifecycle, visual pace and long-session verification remain pending. See [ghost comparison](ghost-comparison.md).

## PR device build and server refresh — 2026-11-05

Built a full Android APK through the running Unity 2022.3.62f3 Editor: succeeded, 0 errors, 18 SDK/compiler/native-alignment warnings, 210.5 s. APK: `/tmp/pacescape-pr-device/PaceScape.apk`, 43,315,593 bytes; SHA-256 `7223f128ef25690dacac3bd9af1fe7f3f3d6e6fc1c84e2eba2e986cd15fb9316`. The build report's 789.85 MB includes supporting artifacts and is not the APK size. Verified the full APK contains `comparisonControlVersion`, `RunnerPerformance`, `AllTimePersonalBest` and `PersonalBestBook` metadata and no Patch-and-Run shadow loader.

Installed successfully with `adb install -r` on the attached Magic Leap 2, preserving app data, and reapplied USB reverse port 8787. Restarted the previously running Control server so the current PR command handlers are loaded. The previous installed package used Patch-and-Run; its package timestamp alone does not identify all externally patched code. Live status was disconnected while the headset slept; end-to-end PR transfer and actual treadmill measurements remain pending because the user deferred foreground headset verification. The runtime source and the user's USB endpoint asset were not changed for this diagnosis.
