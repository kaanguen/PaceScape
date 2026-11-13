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

## Current ghost-only scope — 2026-11-05

Supersedes the avatar behavior in the historical FTMS, Tempo Runner and current-run comparison sections above. Exactly two references remain: `FixedPace` (default preset) and `AllTimePersonalBest` (constant average pace of the selected-distance PR). Belt speed and current-run bests never directly drive ghost speed; belt data still measures user distance, laps and rolling PRs. Side-by-side behavior is deferred. Existing anchor-relative lane motion remains; no new user-relative positioning was added.

Verification: 109 Unity EditMode tests and 21 server/simulator tests passed. Browser simulation confirmed the two choices, preset controls without BLE and an acknowledged all-time selection. Android build `build-d093966938` succeeded with 0 errors and 14 warnings in 288 s, output `/tmp/pacescape-ghost-only-device/PaceScape.apk`. Installation could not run because the previously connected headset was no longer detected by ADB. The installed app therefore still needs this new build. Control was updated with the ghost-only protocol; foreground headset, actual BLE, movement and user-relative visual behavior remain unverified.

## Dynamic PR trace replay — 2026-11-06

Implemented measured winning-window trace persistence, exact replay, transition into the smallest longer stored PR at the current distance, final-speed holding, time-only record fallback, reference snapshots, unchanged fixed mode, and dashboard replay/progress labels. Ten pure NUnit tests passed under the bundled Mono runtime outside the Editor; 22 additional standalone assertions passed. The Unity-specific JSON round-trip and recorded-lane-motion regressions are added but unexecuted. The Editor was not reachable through the available integration; no Unity compile/import, full Editor Test Runner, device build or hardware pass is claimed. A new full device build and headset verification are required. Check pause/resume, mode changes without rewind, multiple marker instances sharing one playback step, exact reference transitions, stationary segments, comparison gaps, backup/load and actual animation on the device. The runtime sources and Editor test sources also passed a standalone C# compiler check using copied existing Unity compiler inputs with outputs redirected to temporary files; this is not an Editor import or Test Runner result. 25 server/simulator/dashboard tests passed in the prepared Control copy, including four new display regressions; JavaScript syntax checks and diff whitespace checks passed. Existing user changes to ProjectSettings were preserved.

## Editor-only replay verification — 2026-11-06

The user explicitly restricted verification to the Unity Editor; do not initiate headset tests, device builds or installation. The running Unity 2022.3.62f3 Editor imported the updated scripts and ran the project test selection through its Test Runner UI. Final result: 121/121 passed, zero failed/inconclusive/skipped. This includes the ten replay regressions and the new Unity JSON and exact lane-motion tests. A redundant global `CanRun` pause gate introduced with replay was removed after the existing belt-independence regression exposed it; replay now advances once per frame from visible content while the established tracking/head/foreground gates remain intact.

The first UI Run All also executed bundled SDK tests despite the text filter and produced additional SDK failures, plus the now-fixed project regression. The final run explicitly selected only `PaceScape.Tests`; all 121 selected cases passed. No SDK package was modified. No headset tests, device builds or installations were performed. This Editor result does not establish real headset/BLE behavior or replace a visual long-session evaluation. The result XML was copied to `/tmp/pacescape-replay-editor-results.xml` for inspection.

## Visual Editor replay preview — 2026-11-06

Added an isolated Editor-only preview window with fictitious 20 m/40 m records, using production ghost replay/motion and the existing Jogger/track. Unity imported the new C# and USS; the rendered window visibly confirmed deterministic progression (11 m, 24 m, 44.67 m), longer-reference selection, final 16.8 km/h holding, continuously changing animation/position, stable pause and reset. The inspected post-import Editor log showed no preview-specific errors. No current scene, actual record storage, dashboard/server state, runtime asset or device was modified by the preview. Full-session visual performance, end-to-end transport and foot-contact accuracy remain outside this short visual check. No additional automatic tests were needed for the Editor-only preview; the previously completed 121 project EditMode tests are unchanged. The standard IMGUI render surface is hosted within UI Toolkit, following Unity preview rendering rather than adding runtime UI.

## Relative treadmill presentation — 2026-11-06

Implemented opt-in Track/TreadmillRelative presentation, explicit floor/heading calibration, one-marker scope, configurable signed side separation/visibility distance, renderer-only hiding outside range, cached renderer restoration, and dashboard settings/capability gates. FixedPace and AllTimePersonalBest remain the only pacing modes. The ghost's speed is never driven by the relative gap. Unknown user measurement intervals are not bridged; relative rendering requires a new continuous comparison after reset. No scene or prefab YAML was edited for this feature; runtime integration is implemented in C#.

Editor Test Runner: 135/135 project cases passed, zero failed/skipped. Fourteen new cases cover calibrated frame/heading, signed overtaking, range boundaries/invalid inputs, renderer ownership and independent logical motion, measurement validity and command validation. The initial run with the visual preview still open failed three pre-existing shared-resource cleanup assertions; closing that legitimate extra track owner produced the full passing result without changing those tests. Keep the preview closed when running these resource-release cases.

Final first-person visual check identified the PreviewRenderUtility default 2 m near clip as the cause of the clipped companion. The preview camera now explicitly uses 0.03 m. In the Unity window the ghost was visible nearby, progressed from 0.5 m ahead to 1.5 m behind, disappeared when the display range was shortened to 1 m, continued to 44.67 m while hidden, and reappeared 4.17 m ahead after restoring 10 m range and turning the view. Runtime/XR cameras are unchanged. Latest post-import log inspection showed no preview-specific compilation errors/exceptions. Control files were reviewed/syntax checked, but no new server test run or complete dashboard/runtime calibration session was executed. No headset tests, device build/install or live server restart was performed. Hardware behavior and long-session/physical-contact accuracy remain outside verified scope.

## Camera-relative warm-up — 2026-11-11

The warm-up reuses the PaceScape Jogger in a separate camera-relative instance. Invalid sequences and unavailable camera views fail before playback. The instance is removed on completion, manual stop, tracking or view loss, and loss of the control connection. Unity and the dashboard relay both reject run starts while the warm-up is active.

Verification is limited to Unity Editor and local dashboard simulation. All 150 project EditMode tests passed. The warm-up regressions exercise start without markers, viewport stability through head rotation, invalid input and camera settings, timed completion, and cleanup without automatic resume. The local browser passed start, live progress, skip, stop, completion, and disconnect. The server and simulator tests passed (28/28). The full dashboard suite reports 38/42 because four existing Ghost replay display tests fail.

Release verification remains open for a new Android build, the installed app's WebSocket connection, and the coach's placement and clipping in both eyes on Magic Leap 2. The working tree currently contains an existing relocation of the Android manifest into `Assets/Starter Assets/Plugins/Android/`. It is byte-for-byte identical to the previous XML, and Android Player Settings has Custom Main Manifest disabled. Verify the effective merged permissions and network configuration in the next authorized build. [Unity 2022.3 manifest documentation](https://docs.unity3d.com/2022.3/Documentation/Manual/overriding-android-manifest.html)
