# PaceScape

Unity/C# project for AR running training, currently in its initial setup stage. Read [README.md](README.md) for the product overview and current status. Technical setup and verification notes are in [docs/tracking-setup.md](docs/tracking-setup.md); README branding assets are in `docs/assets/`. All paths below are relative to the repository root.

The README uses light and dark variants of the PaceScape Control wordmark. The launcher icon uses the same AR runner symbol at `PaceScape/Assets/Art/Icons/PaceScapeAppIcon.png`; preserve its `.meta` when refreshing the artwork so the existing Player Settings references still resolve.

## Project context and layout

- `PaceScape/` is the Unity project; the repository root contains documentation and Git configuration. Open the Unity project subfolder, not the repository root.
- `PaceScape/ProjectSettings/ProjectVersion.txt` is the source of truth for the Editor version (currently `2022.3.62f3`). Keep code and assets compatible with it.
- `PaceScape/Packages/manifest.json` declares dependencies; `packages-lock.json` records resolved ones. Change dependencies through Unity Package Manager and include the resulting lockfile changes.
- Android Player Settings uses the custom main manifest at `PaceScape/Assets/Plugins/Android/AndroidManifest.xml` for Magic Leap, camera and BLE permissions and local cleartext WebSockets. Keep the Android OpenXR Spatial Anchor Subsystem enabled and Spatial Anchors Storage disabled for session-local anchors.
- `PaceScape/Assets/Scenes/MarkerTrackingScene.unity` is the current scene. The Magic Leap SDK 2.6.0 and Android OpenXR configuration are present. `PaceScape/Assets/Scripts/Tracking/TagTracker.cs` implements AprilTag 36H11 detection, placement on classified floor planes beneath marker X/Z positions through `MarkerPlanePlacement.cs` (floor mode ignores marker height; legacy strict surface mode remains available), and local OpenXR spatial anchors for the running app (no cross-session storage); marker poses are retained while waiting for asynchronous plane extraction, following MarkerML2; startup is sequenced with phase diagnostics, individual permission requests, active-time timeouts, pause/resume recovery, bounded marker capacity, and explicit retry after failures; scene wiring includes an active AR Session and is configured and checked through Unity MCP; target-device verification remains pending; current completed and outstanding release checks are in `docs/production-readiness.md`. EditMode regression tests are in `PaceScape/Assets/Tests/Editor/` (`PaceScape.Tracking.EditorTests`); FTMS BLE treadmill input is implemented through the embedded user package; target-device verification is pending. The older RunningServer integration is not configured.
- `PaceScape/Assets/Scripts/RunningTrack/` generates the eight-lane oval markings from MarkerML2 dimensions. `TagTracker` creates a shared content root for marker object and track under each local anchor; visibility and cleanup apply to both. The bundled Resources shader supports URP stereo rendering. The marker is the lane-one entry, facing its printed top edge, ten nominal metres before the bend. The spawned object is centered on the selected lane (1–8, default 1) and defaults to 0.50 m behind the start line. `TagTracker` exposes spawn lane, start distance, and live pace (default 6:00 min/km). `RunningTrackMotion` follows the lane centerline at the configured physical speed using unscaled time, including bends, with a frame-independent 0.35 s response for live pace changes, and pauses while content is hidden. Track instances share a reference-counted mesh/material; inputs and visual prefabs are validated, and creation is limited to one marker per frame. Headset position and rotation must both be tracked for visibility. This is visual placement, not surveyed stadium calibration.
- Magic Leap SDK 2.6.0 is embedded via Unity Package Manager at `PaceScape/Packages/com.magicleap.unitysdk/`. Its OpenXR `MLXrAnchorSubsystem.cs` contains a local anchor-completion batching fix: an ID completed before its initial addition is reported must appear only in the added batch, with the latest tracked pose. The same file cancels pending native futures on removal and clears managed anchor state without conflicting add/update/remove batches. See `docs/magicleap-anchor-fix.md` and `AnchorChangeTests.cs`. Preserve this patch until an upstream replacement is verified; do not patch the package cache.
- MarkerML2 (`../MarkerML2/`) is the user's earlier Magic Leap project with marker, floor, and running-track integration. Use it as a read-only reference; do not assume its packages or features exist here. For PaceScape marker tracking, follow the official [Magic Leap OpenXR Marker Understanding documentation](https://developer-docs.magicleap.cloud/docs/guides/unity-openxr/marker-understanding/marker-understanding-overview/), use AprilTag 36H11, and keep the implementation simple.
- `PaceScape/Assets/Editor/RemoteControlSettingsWindow.cs` exposes **PaceScape > Serververbindung** using UI Toolkit, with its adjacent USS. The **Rechner-IP übernehmen** button reads local active IPv4 interfaces (with an adapter chooser), preserving the endpoint port; it does not discover remote servers or headset IPs. Saving creates `Assets/Resources/PaceScapeConnection.asset` through Unity; `RemoteControlSettings` supplies validated build defaults. Editor Play mode uses project defaults and supports explicit reconnect; device JSON overrides retain precedence on headsets. Connection changes pause active runs. `RemoteControlSettingsTests` covers default and URL validation behavior; Editor and device checks remain pending.
- Place runtime scripts in the matching feature folder under `PaceScape/Assets/Scripts/`, editor tools in `PaceScape/Assets/Editor/`. Create these folders when needed.

- `PaceScape/Assets/Characters/Jogger/` contains the humanoid runner, prefab and `JoggerStarterAssets.controller`. `Locomotion/` contains Walking, Running and Sprint copied with their import settings from MarkerML2 (read-only source). The controller has one Idle/Walk/Run/Sprint blend tree: the existing Starter Assets Idle plus the three distinct MarkerML2 cycles, without duplicate run entries or jumping. Thresholds use imported horizontal root velocities (approximately 1.616, 4.297 and 5.582 m/s). `RunningTrackMotion` binds the initially hidden Animator only after activation, sets `Speed` from smoothed physical speed and uses `PlaybackRate` below walking or above sprint speed; intermediate gaits blend at 1x. Root motion application remains disabled: native clip velocity calibrates animation, while the lane path owns displacement. This is average-cycle calibration, not exact stance-foot locking. Live pace changes have a frame-independent 0.35 s exponential response; initial speed is immediate. `RunnerFootPlacement` applies ankle/toe-aware IK against the fixed anchor plane; humanoid placement uses sole heights instead of padded bounds. The Animator uses unscaled time. Tests cover hidden spawning, movement, gait weights/rate through walking, running and sprinting, frame independence and sole placement. Target-device foot contact and perceived naturalness remain pending.

## Language

- Talk to the user in German. Write code, identifiers, code comments, commit messages, and this file in English.

## Working scope and collaboration

- The user leads the conceptual work and makes scientific and methodological decisions. Implement individual code sections within the agreed scope.
- Research and explain relevant knowledge and options. Distinguish evidence from assumptions and state uncertainties.
- Examine concepts critically for scientific soundness. Point out overlooked assumptions and missing information. Do not silently choose scientific methods, training parameters, or evaluation criteria.
- Avoid unrelated package upgrades, restructuring, and speculative infrastructure. Keep this file aligned with the repository and update it when structure or workflows change.

## Git

- Before editing, run `git status --short` and inspect relevant changes with `git diff -- <path>`. Preserve the user's uncommitted changes.
- Do not commit, push, create branches, or rewrite history unless the user asks. When asked to commit, use short imperative English messages (e.g. `Add pace display prototype`).
- Before reporting completion, run `git diff --check` and review `git diff -- <changed-paths>` for unintended edits.

## Unity assets and generated files

- Move or rename assets through Unity, or move them together with their `.meta` files. Include `.meta` files for all new assets and folders. Preserve existing GUIDs and references.
- Do not hand-edit `.unity`, `.prefab`, or other serialized YAML assets unless the user agrees for that specific change. Prefer a small Editor script or step-by-step instructions for the user to perform in the Editor.
- Follow `.gitattributes` and `.gitignore`. Track binary assets with Git LFS according to the existing rules; keep scripts, text-serialized Unity assets, and `.meta` files in regular Git.
- Do not edit or commit `Library/`, `Temp/`, `Obj/`, `Logs/`, `UserSettings/`, generated solution/project files, local IDE files, or builds. Do not edit `Library/PackageCache/`.

## C# coding guidelines

Style follows the [Unity C# style guide (Unity 6 edition)](https://unity.com/resources/c-sharp-style-guide-unity-6), summarized here so it does not need to be fetched each time. `.editorconfig` in the repository root enforces formatting and naming where possible; if it conflicts with this section, `.editorconfig` wins.

- Formatting: 4 spaces, Allman braces (opening brace on its own line), always use braces for `if`/`for`/`foreach`, one statement per line.
- Naming: `PascalCase` for types, methods, properties, events, enums, and public members; `camelCase` for locals and parameters; `_camelCase` for private fields; `I` prefix for interfaces. Use descriptive names; booleans read as questions (`isRunning`, `hasTarget`).
- Inspector fields: `[SerializeField] private` instead of public fields. Renaming a serialized field loses its scene/prefab value; use `[FormerlySerializedAs]` or ask first.
- Comments explain why, not what. Add XML summaries for public APIs.
- Keep `Find*`, `GetComponent`, and allocations out of `Update`; cache references in `Awake`/`Start`.
- Keep code compatible with the Editor version in `ProjectVersion.txt`. Do not introduce unsupported APIs or language features to follow an example.
- Where the guide offers alternatives, use the convention already established in the project code. Do not reformat unrelated code or rename serialized fields, assets, or public APIs solely for style.
- Mention any necessary style deviation in the completion report.

## Verification

- User-directed verification scope: test only inside the Unity Editor. Do not run headset tests or initiate device build/install workflows unless the user explicitly changes this instruction. Keep hardware limitations documented without asking the user to perform device tests.

- The agent usually cannot run the Unity Editor. After C# changes, ask the user to let Unity compile and to paste Console errors or warnings. Do not claim the code compiles before the user confirms it.
- Run relevant existing tests through Unity's Test Runner when available; an empty test suite is not behavior verification.
- Scenes, UI, placement, and animations must be verified in the Editor. Hardware-dependent behavior remains outside the verified scope; do not initiate target-device checks under the current user instruction.
- Command-line test workflows and device build tools are not configured yet. Do not invent commands or claim checks that were not performed. If Unity or hardware is unavailable, report the pending checks.
- Changes to C# code, scenes, or bundled assets require a new device build to take effect on an installed app. Editor verification does not verify installed-device behavior.
- Documentation-only changes need a content and diff review; no compile or device build is required.

## Completion report

- Small changes: 2 to 3 sentences covering what changed, which files, and what was not verified.
- Larger changes: state what changed and which files; checks actually performed and their results; checks not performed (Unity compilation, tests, device verification); material assumptions, style deviations, and limitations; and whether a new device build is needed.

## Live pace and gait adjustment

The dashboard runner-panel speed slider converts km/h to pace and automatically submits edits through `runner.pace` after 500 ms, independent of lane/start configuration. Queued pace updates are dropped on disconnect. `RunningTrackMotion` begins walk/jog blending at 85% of imported walking speed and compensates playback rate to retain physical speed; intermediate walk/run blends may therefore use a rate below 1x. Editor and device gait verification remain required.

## FTMS treadmill input

`PaceScape/Assets/Scripts/Treadmill/TreadmillInput.cs` reuses the embedded user BLE package and pinned Velorexe revision. Fresh belt speed feeds user distance, laps and real personal bests. Fresh FTMS speed also drives and gates SideBySide; FixedPace and AllTimePersonalBest retain their own pace sources. `treadmill.manual`, `FollowTreadmill`, and `GetRunnerPace` have been removed from this wrapper. Opt-in `treadmill.startRun` starts the ghost after the stopped belt acknowledges FTMS start. `treadmill.speed` adjusts a moving belt within its reported FTMS range. Pause freezes the ghost after FTMS acknowledgement. Stop resets the ghost immediately on receipt while confirming motor stop separately. Both remain available without fresh speed telemetry. Resume of a stopped belt requires fresh zero-speed telemetry and a paused, tracked session. Motor operations require a local dashboard on the server Mac, explicitly enabled USB loopback or opt-in WLAN on both endpoints, and a matching FTMS response. Commands never retry automatically. See `docs/treadmill.md`; hardware verification remains pending.

## Independent Tempo Runner

Tracking clients also expose `Manual` via `runner.mode` (`manualModeVersion: 1`). It uses the preset pace to move the oval under the calibrated start point and animate a companion beside it, without requiring FTMS or collecting user records. Use one marker and calibrate before starting. Reset before entering or leaving Manual during a session. The separate dashboard uses its existing speed control and the virtual run commands in this mode.

The avatar supports `FixedPace` (default), `AllTimePersonalBest`, and `SideBySide` through `runner.mode`. The first two use preset pace or stored PRs and retain their optional treadmill-relative presentation. SideBySide uses fresh FTMS speed and holds a signed lateral offset from the calibrated treadmill position. Entering SideBySide requires Ready and at most one marker; starting and resuming require calibration and fresh input. Signal loss pauses the run. Tracking clients advertise `sideBySideVersion: 1`; the marker-free test scene does not support this mode.

## Distance efforts and personal bests

`Assets/Scripts/Performance/` owns user-only fixed-distance laps, rolling fastest continuous windows and device-local treadmill PR storage. Statistics and current-run bests remain measurable in both ghost modes, but only the stored all-time PR may drive the PR ghost. Configure 400 m or custom whole metres through `runner.comparison` (`comparisonControlVersion: 1`). Records originate only from fresh belt measurements, never avatar speed, target pace or examples. Zero-speed intervals count; pauses/gaps break measurement continuity without changing ghost target. Real records persist across reset/relaunch with validated JSON/backup, coalesced writes and visible save status. See `docs/ghost-comparison.md`.

## Dynamic PR ghost replay

`GhostReplay` replays optional time/distance traces stored with newly improved real PRs, then enters the smallest longer stored PR at the ghost's current absolute distance. Without a longer record it holds its final segment speed; legacy records use their average speed. References are snapshotted for the active playback. The selected comparison distance remains the measurement distance and initial ghost reference. `RunningTrackMotion.AdvanceRecorded` integrates recorded travel exactly and smooths only animation; FixedPace retains physical speed smoothing. Local traces are bounded and excluded from telemetry; `ghostReplayVersion: 1` and replay/progress fields describe active references. Treadmill-relative placement is configured separately from replay. See `docs/ghost-comparison.md`; the latest 135 project EditMode tests passed in the Editor. Hardware behavior remains outside the user-authorized verification scope.

## Dashboard camera preview

`Assets/Scripts/RemoteControl/RemoteCameraPreview.cs` opens the Magic Leap Main Camera only after `camera.start` and Android camera permission. It captures 960×720 mixed-reality RGBA video and encodes sampled frames as JPEG (quality 60) on a worker thread using a reusable RGBA buffer, and sends them over the existing WebSocket at up to the camera’s 30 frames per second. A new camera frame wakes the sender immediately. The app keeps only the latest frame, stops on explicit stop, app pause or server connection loss, and does not resume automatically. Hardware capture and coexistence with marker tracking remain unverified.

## Editor ghost preview

The preview also offers **Side by Side (adaptives Laufbandtempo)**. Synthetic belt speed drives the production gait path and `TreadmillGhostPresentation.EvaluateSideBySide` keeps the avatar beside the fixed synthetic treadmill origin. Mode switches pause and reset playback. The EditMode treadmill test covers FTMS sample freshness separately.

`Assets/Editor/GhostReplayPreviewWindow.cs` and its USS expose **PaceScape > Ghost-Vorschau (Testdaten)**. It uses UI Toolkit controls and the standard IMGUI 3D preview surface (`PreviewRenderUtility`), with the production `GhostReplay`, `RunningTrackMotion`, Jogger prefab and track in an isolated unsaved preview scene. Fictitious 20 m/40 m traces demonstrate speed changes, reference chaining and final-speed holding; these are technical preview fixtures, not training parameters or real records. It never creates a transport client or record file and never modifies the open scene. Playback advances only while this window is focused; long Editor stalls do not catch up. Closing/reloading releases the preview and its generated resources. Visual Editor checks completed for stepping, continuous movement/animation, pause and reset; this is not an end-to-end server or hardware test.

## Treadmill-relative ghost presentation

In `TreadmillRelative` and `SideBySide`, the visible oval moves according to measured belt distance. Its selected lane point and tangent are transformed to the fixed calibrated treadmill position and heading. `TagTracker` moves only the track child under the anchor, and the Editor preview uses the same transform with synthetic distance. Pause holds the pose, and reset rewinds the track. Stadium mode retains its marker-aligned oval.

`Assets/Scripts/RunningTrack/TreadmillGhostPresentation.cs` maps progress difference along the selected lane centerline. `runner.presentation` selects `Track` (default) or `TreadmillRelative`, with explicit signed side offset and visibility distance; `runner.calibrate` locks the floor point below the tracked head. Its nearest point on the selected lane is the zero-gap reference, and the lane tangent sets the ghost heading. Configuration/calibration require Ready and exactly one placement; relative mode limits new placements to one. Calibration is anchor-local and never follows subsequent head turns. It is lost with tracking teardown. Settings are session-local. Relative start/resume requires fresh belt input; an already running ghost keeps its independent logical speed if input is lost, while invalid relative placement is hidden until reset. A deliberate pause freezes the last presented pose.

`RunningTrackMotion` preserves unwrapped travel and gait while only the runner pose/renderers are overridden; track/content visibility and existing anchor/head gates are retained. Renderer flags are cached/restored. Relative gap includes the configured start offset; performance telemetry includes that offset in `userLeadMetres` while relative mode is selected. PR ghosts follow the full oval without a distance cutoff. Other relative ghosts still use the configured visibility distance, defaulting to ±10 m. The legacy side offset remains on the wire for compatibility.

The dashboard advertises `ghostPresentationVersion: 1` and exposes settings/calibration only on capable tracking clients. Marker-free server preview does not pretend to have real head/anchor calibration. The isolated Editor ghost preview instead supplies labelled synthetic user speed, a fixed blue user marker in overview and a first-person camera with independent yaw/pitch. Its near clip is explicitly 0.03 m; Unity's default preview near clip of 2 m clipped the initial companion. Close the preview window before running resource-release tests, because its shared track remains a legitimate resource owner while open. Latest project Test Runner result: 135 passed, zero failed/skipped. Manual Editor checks covered crossing ahead/behind, range hiding, continued hidden progress and reappearance. Full dashboard-to-runtime calibration and hardware behavior were not verified; test only in the Editor per the user's instruction.

## Warm-up

`Assets/Scripts/RunningTrack/RunnerWarmup.cs` plays operator-defined timed sequences before a run. `exercise.startWarmup`, `exercise.nextWarmup`, and `exercise.stopWarmup` are exposed by the tracking client (`warmupControlVersion: 1`). Warm-up requires Ready and a tracked headset. `WarmupCoachView` attaches the coach prefab under `Assets/Characters/WarmupCoach/` to the XR camera. It reuses the PaceScape Jogger model and materials and fits the model to the lower-right viewport. The placed runner stays in its run presentation. The coach is released on completion, interruption, or tracking teardown. Its Animator uses the prepared MarkerML2 Mixamo clips under `Assets/Characters/Jogger/Warmup/`. **PaceScape > Warm-up-Vorschau** uses the same prefab, playback, and layout with a synthetic head rotation. See `docs/remote-control.md#warm-up`.
