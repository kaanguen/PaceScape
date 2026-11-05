# Independent Tempo Runner

The Tempo Runner implements MarkerML2's independent target-pace behavior, as requested. It uses the existing anchored avatar, selected lane, start offset, sole placement and pace/animation response. It does not add a second avatar, move the XR rig or scroll the track. This initial mode has now been extended with current-run and all-time average-pace comparison; see [ghost comparison](ghost-comparison.md) for the current behavior. The target-only description below records the initial implementation.

## Use

1. Install a new PaceScape Android build and restart the updated PaceScape Control server.
2. In **Läufer einstellen → Läufermodus → Tempoquelle**, select **Tempo Runner · eigenes Zieltempo** and choose **Modus übernehmen**.
3. Set the desired target with the existing km/h or min/km slider. The existing configured default is retained; no new training target is prescribed.
4. Place the runner and wait for tracked content, then select **Lauf starten**, or **Weiterlaufen** for an already paused run.

Changing mode pauses an active run without rewinding the avatar, changing lane/start offset or clearing placement. Repeating the already selected mode is idempotent. Reset returns the avatar to its configured start and retains the selected mode and target pace. Closing the app restores runtime defaults next launch; no mode persistence was added.

| Mode | Avatar target | Missing/zero belt speed |
| --- | --- | --- |
| `FollowInput` (existing default) | Selected treadmill source; manual pace when explicitly using manual input | Selected treadmill at 0 km/h stops; missing/stale data pauses without fallback |
| `TempoRunner` | Configured `paceMinutesPerKilometre`, independent of BLE | Does not stop or pause the avatar |

In Tempo Runner mode, the slider remains enabled with a connected treadmill; overview metrics show the independent avatar target, while the Bluetooth panel separately shows belt telemetry. Connecting/disconnecting BLE or selecting manual input does not pause this independent mode. Android permission prompts may background the app and still trigger the normal lifecycle pause. Explicit run pause, tracking loss, app backgrounding and server/heartbeat loss continue to pause both modes. Recovery never automatically resumes a paused run. Switching back to `FollowInput` while a treadmill source is selected requires fresh belt data before resume; it does not silently choose the manual target.

## Implementation and protocol

- `RunningSession.RunnerMode` defines `FollowInput` and `TempoRunner`; `TrySetMode` validates exact names and pauses only on a real mode change.
- `TagTracker.ActivePaceMinutesPerKilometre` selects the target and its readiness gate requires a fresh belt signal only in `FollowInput`. The existing `RunningTrackMotion` applies the selected speed and gait; no motion, animation, prefab or scene assets changed.
- `RemoteControlCommands`, `RemoteControlClient` and `ServerTestScene` expose `runner.mode` with `{"runnerMode":"TempoRunner"}` or `{"runnerMode":"FollowInput"}` in the existing command envelope. State advertises `runnerModeControlVersion: 1` and `runnerMode`. Older clients retain their existing behavior and the new UI is disabled until capability is advertised.
- The separate Control repository adds the selector, mode-aware slider/overview, strict relay validation, and simulator support. Existing uncommitted BLE changes in that repository were preserved.

## Verification

17 server/simulator tests passed in the prepared copy and updated Control repository, including new mode relay/capability/validation tests and mode transition/reset/idempotence tests. Browser checks used explicitly labelled synthetic data: switching a running follower from 12 km/h belt speed to 10 km/h target pauses and displays 6:00 min/km; after resume, zero belt speed and missing BLE do not change the independent target; a live slider edit is acknowledged; switching back to missing belt input pauses and blocks resume. No browser errors were reported during these checks.

New Unity regressions cover independent target selection through belt stop/disconnect, preservation of position and mode on pause/reset, strict mode validation and the marker-free preview command. Unity compilation and Test Runner execution remain pending because Unity MCP was unavailable. A new device build and verification of motion/animation, real BLE loss, tracking loss and background/reconnect are required. Browser/simulator checks do not establish headset behavior or training efficacy.
