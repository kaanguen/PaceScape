# Current-run and all-time tempo comparison

The tempo partner uses a constant average speed from a fully completed treadmill effort. The selected measurement distance is either **400 m** or an arbitrary whole-metre distance entered in kilometres, for example 5 km or 10 km. It is independent of the selected visual lane circumference; the oval, anchor, start offset and XR rig are not recalibrated or moved by this feature.

## Ghost modes and workflow

The avatar is an independent ghost, not a mirror of the user or a side-by-side companion. There are exactly two references:

| Ghost reference | Source |
| --- | --- |
| `FixedPace` (default) | The preset pace/speed slider, unchanged by live belt speed and current-run results |
| `AllTimePersonalBest` | The stored real personal best for the selected exact distance, played as constant average pace |

1. Install the ghost-only Android build, restart Control, and select **Ghost · voreingestelltes Tempo** or **Ghost · All-time PRs** under **Ghost einstellen**.
2. Set preset speed, lane and start position as before. For PR comparison choose 400 m or a custom kilometre distance. A distance change requires reset; mode changes pause without rewinding.
3. Place and track the ghost, then explicitly start/resume. It advances independently along the configured lane; neither zero belt speed nor missing BLE changes its target. Tracking loss, backgrounding and server loss still pause the session.
4. Connect the treadmill to measure the user's distance, time, laps and real PRs. These come only from belt samples, never ghost displacement or animation. Missing input cannot manufacture user progress. Current-run bests remain visible as user statistics but are not a ghost reference.
5. All-time mode uses the persisted distance record. When none exists, it retains the preset fallback or an explicitly enabled, labelled example. A new faster real PR updates the all-time reference; it does not override FixedPace mode.

A measurement lap is a fixed block of the selected distance. Rolling best efforts can start between lap boundaries. The current selected distance is evaluated for that run; historical activities are not retained for retrospective evaluation of other distances. No side-by-side behavior, head-following position or new user-relative placement/calibration was added. The ghost keeps the existing independent lane path and configured starting offset.

## Measurement definition

- The data source is the reported FTMS belt speed, not avatar travel, animated gait, configured target pace or device total-distance counter. The preceding known sample is held across each frame interval; constant-speed distance boundaries are interpolated. Thus results describe this sampled belt telemetry, not certified physical running speed or stadium calibration.
- Fastest continuous windows are evaluated when either end crosses a speed-change boundary, including interior crossings. Coalescing equal speeds avoids retaining per-frame points; old points outside the relevant window are pruned. Exactness refers to this piecewise constant observed trace, not unknown changes between BLE notifications.
- Fresh 0 km/h is a valid stopped interval and contributes time. Paused sessions, missing/invalid/stale speed, source disconnects, unavailable placement, backgrounding and frame gaps longer than the existing two-second freshness policy break continuity. No effort spans an unknown interval. A partially measured fixed lap is flagged invalid until its next boundary; subsequent complete laps can qualify. Previously completed records survive interruptions.
- This is inspired by [Strava running Best Efforts](https://support.strava.com/en-us/articles/15401661-how-do-best-efforts-work-for-running-on-strava): the fastest completed distance within a run and elapsed time including known stops. It is not a Strava API integration or a claim to reproduce their proprietary GPS processing. Strava's manually entered official-race **All-Time PRs** are distinct from their automatically calculated Best Efforts; this app's displayed PRs are explicitly treadmill-derived.
- User reset/tracking teardown begins new current-run measurements. All-time bests survive. Changing avatar mode preserves completed current-run measurements; pausing still invalidates an incomplete measurement block. No peak instantaneous speed or incomplete projected finish time becomes a record.

## Real records and demonstrations

Real records are stored on this device at `Application.persistentDataPath/pacescape-treadmill-personal-bests.json`, keyed by integer metres, with elapsed seconds and a UTC timestamp. They load once for the scene, validate version/data, and fall back to a `.bak` file if needed. Faster or first genuine efforts update memory immediately. Main-thread JSON snapshots are written by one background task through a temporary file and replacement/backup. Writes are coalesced and normally at most once per five seconds; pending writes/errors appear in telemetry. Backgrounding requests a flush. Avoid terminating the app while saving is pending; an abrupt OS kill can lose the newest not-yet-written improvement.

Storage is local to this app installation and has no account/user separation or cloud sync. Uninstalling/clearing app storage can remove records. Engineering limits are 1–1,000,000 whole metres per distance, 1,024 stored distances, 1 MiB input files, and 200,000 retained speed-change points per continuous trace. Capacity failures do not manufacture records.

Fictional examples are separate constants: 400 m in 120 s, 5 km in 1,500 s, and 10 km in 3,300 s. **Gekennzeichnete Beispiel-PRs als Ersatz verwenden** is off by default. When enabled, an example may pace the all-time partner only if that distance has no measured PR. The badge says **BEISPIEL · fiktiver PR**. Examples never populate the real record table/file; the first real result wins even if slower than the example. FixedPace mode always uses the preset, even when a real current-run best or all-time record exists.

## Code and protocol

- `Assets/Scripts/Performance/DistancePerformance.cs`: fixed-distance blocks and bounded lap history.
- `RollingDistanceBest.cs`: continuous-window search with interpolated boundaries and retained session best.
- `PersonalBestBook.cs`: versioned, exact-distance measured records and strictly faster updates.
- `RunnerPerformance.cs`: scene-owned sampling, target selection, storage and telemetry. It binds to the runtime client, tracker and treadmill input without serialized scene changes.
- `TagTracker` / `ServerTestScene`: apply the selected comparison target through existing `RunningTrackMotion`; explicit reset clears session statistics. Preview has no real BLE measurements, but supports demonstration targets.
- State advertises `comparisonControlVersion: 1`, `activeRunnerPace`, and a `performance` object. `runner.comparison` requires `{ "comparisonDistanceMetres": 5000, "useDemoRecords": false }`. `runner.mode` accepts exactly `FixedPace` and `AllTimePersonalBest`. State additionally advertises `ghostControlVersion: 1`; older mirror/current-run builds must be updated before the ghost controls are enabled.
- Control's simulator advertises the capability and can exercise configuration/demo display, but never creates measured PRs or simulates real BLE by itself.

## Verification

109 EditMode tests completed with no reported failures, including fixed boundaries, rolling records inside longer runs, zero-speed time, interruptions, frame splitting, 2,500 speed-change points with pruning against an independent time-grid reference, measured/demo precedence, current-run fallback, JSON round trip and actual temporary/backup file replacement. Existing tracking, gait, run-control and BLE regressions also ran. Server/simulator tests and browser verification are recorded in the completion report. No target-device BLE, persistence under Android/IL2CPP, timing, animation or full-session accuracy is inferred from Editor tests; a new installed Android build and hardware checks are required.

Final validation: 109/109 EditMode tests passed, none skipped; 19/19 server/simulator tests passed in the updated Control project. Browser checks verified 5 km example selection, explicit all-time mode, visible 25:00 / 5:00 min/km example target and separation from the empty measured record table. Telemetry caps the displayed history to 20 laps and 32 PR rows (including the selected distance), with the full real book retained locally, to respect the existing relay message limit. No new C# errors were reported by Unity MCP; device verification remains pending.

## Ghost-only scope correction

The user narrowed avatar behavior to preset or all-time PR only. Retired modes `FollowInput` and `TempoRunner` and the BLE-to-avatar source API were removed. Measurement and persistence remain intact. No measured user result is inferred from the ghost. Updated regressions check the fixed default, rejection of old/side-by-side modes, independence from belt stop/disconnect/current-run bests, and retained all-time targets. Hardware behavior remains pending.
