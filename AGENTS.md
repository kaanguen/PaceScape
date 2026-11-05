# PaceScape

Unity/C# project for AR running training, currently in its initial setup stage. Read [README.md](README.md) for the product overview and current status. Technical setup and verification notes are in [docs/tracking-setup.md](docs/tracking-setup.md); README branding assets are in `docs/assets/`. All paths below are relative to the repository root.

## Project context and layout

- `PaceScape/` is the Unity project; the repository root contains documentation and Git configuration. Open the Unity project subfolder, not the repository root.
- `PaceScape/ProjectSettings/ProjectVersion.txt` is the source of truth for the Editor version (currently `2022.3.62f3`). Keep code and assets compatible with it.
- `PaceScape/Packages/manifest.json` declares dependencies; `packages-lock.json` records resolved ones. Change dependencies through Unity Package Manager and include the resulting lockfile changes.
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

- The agent usually cannot run the Unity Editor. After C# changes, ask the user to let Unity compile and to paste Console errors or warnings. Do not claim the code compiles before the user confirms it.
- Run relevant existing tests through Unity's Test Runner when available; an empty test suite is not behavior verification.
- Scenes, UI, placement, and animations must be verified in the Editor. Hardware-related behavior must be verified on the target device.
- Command-line test workflows and device build tools are not configured yet. Do not invent commands or claim checks that were not performed. If Unity or hardware is unavailable, report the pending checks.
- Changes to C# code, scenes, or bundled assets require a new device build to take effect on an installed app. Editor verification does not verify installed-device behavior.
- Documentation-only changes need a content and diff review; no compile or device build is required.

## Completion report

- Small changes: 2 to 3 sentences covering what changed, which files, and what was not verified.
- Larger changes: state what changed and which files; checks actually performed and their results; checks not performed (Unity compilation, tests, device verification); material assumptions, style deviations, and limitations; and whether a new device build is needed.

## Live pace and gait adjustment

The dashboard runner-panel speed slider converts km/h to pace and automatically submits edits through `runner.pace` after 500 ms, independent of lane/start configuration. Queued pace updates are dropped on disconnect. `RunningTrackMotion` begins walk/jog blending at 85% of imported walking speed and compensates playback rate to retain physical speed; intermediate walk/run blends may therefore use a rate below 1x. Editor and device gait verification remain required.

## FTMS treadmill input

`PaceScape/Assets/Scripts/Treadmill/TreadmillInput.cs` reuses the embedded `com.kaan.ble-sensors` package copied from MarkerML2, with its pinned Velorexe revision. The current source package includes FTMS and scan fixes absent from the older supplied 1.0.0 tarball; do not replace it blindly. `RemoteControlClient` creates the input for the tracking scene without serialized scene changes. `TagTracker` uses live belt speed after connection and an explicit run start; 0 km/h stops the runner, missing/stale speed pauses without automatic resume or manual fallback. Manual mode is explicit. No motor commands are implemented. Dashboard connection controls live in the separate PaceScapeControl repository. See `docs/treadmill.md`; compilation, EditMode tests and hardware validation are pending. Copied package code retains its original style.
