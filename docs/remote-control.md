# Local PaceScape Control

The local server and German dashboard are in `/Users/kaan/Work/PaceScapeControl/`. This is a separate implementation inspired by the earlier `/Users/kaan/RunningServer/`; it uses its own version 1 protocol and exposes functions actually present in PaceScape. Neither the old server nor MarkerML2 is modified.

## Change the server address in Unity

Open **PaceScape > Serververbindung** in the Unity Editor. Enter the complete WebSocket endpoint, for example `ws://192.168.1.42:8787/ws`, an optional LAN access token, and whether the connection is enabled. Choose **Im Projekt speichern**. The tool creates `Assets/Resources/PaceScapeConnection.asset` through Unity's AssetDatabase on the first save; this asset supplies defaults to the Editor and subsequent device builds. No scene editing or manually authored asset YAML is required.

**Rechner-IP übernehmen** reads this computer's active IPv4 network interfaces and fills the WebSocket host while preserving a valid existing scheme/port and `/ws` path. It excludes loopback, tunnel and link-local addresses. If multiple addresses exist, choose the adapter on the headset's network from **Netzwerkadresse**; an interface with an IPv4 gateway is suggested first, or the currently entered local address is retained. This button assumes the server runs on the Editor computer: it does not obtain the headset IP, scan the LAN, start the server or prove reachability. For a server on another computer enter that server's address manually; USB still uses loopback with ADB forwarding. Save after accepting the address.

In Play mode, **Speichern & neu verbinden** saves the defaults, pauses a running session, cancels the old socket, waits for its tasks to finish and requests a connection with the new values. The updated connection must be checked in the dashboard; this button does not claim a successful network handshake. Without reconnect, saved defaults apply on the next Play/app start. Changing a saved project default for an installed headset requires a new build and installation.

The Editor always uses the project defaults. On the headset, an existing `pacescape-control.json` in `Application.persistentDataPath` still takes precedence, so update or remove that override if it should use the newly built defaults. Without an asset or a device override, the original USB endpoint `ws://127.0.0.1:8787/ws` is retained. Project settings include any supplied access token in plaintext in the project/build; use a token intended for the local test environment.

Validation requires `ws://` or `wss://`, a hostname, and path `/ws`, without URL credentials, query parameters or fragments. The settings window supports the project's Unity 2022.3 Editor using UI Toolkit. `RemoteControlSettingsTests` adds EditMode validation cases. Editor compilation, window appearance, saved-asset persistence, Play-mode reconnect and device build verification remain pending.

## Start and connect over USB

Requires Node.js 22+ on the computer, ADB, and an updated device build:

```sh
cd /Users/kaan/Work/PaceScapeControl
npm ci
npm start
```

Open `http://localhost:8787` on the computer. Connect the headset by USB, authorize debugging and run:

```sh
adb reverse tcp:8787 tcp:8787
```

The Unity client defaults to `ws://127.0.0.1:8787/ws`. USB forwarding connects the headset's loopback port to the computer's loopback port; without it, localhost on the headset refers to the headset itself. Reapply forwarding after reconnecting/rebooting when necessary. With multiple ADB devices, use `adb -s SERIAL reverse tcp:8787 tcp:8787`.

`RemoteControlClient` is created after the first scene loads when a `TagTracker` exists. It finds the scene tracker and plane placement once. It has its own GameObject so stopping `TagTracker` does not stop the connection. No scene or prefab YAML changes are needed. It is scoped to that scene; later additive-scene switching is not implemented.

The Android manifest now declares `android.permission.INTERNET` and allows cleartext traffic for local `ws://`. No new Unity packages or project setting changes are required by this implementation. It uses .NET `ClientWebSocket`; actual Android/IL2CPP behavior still needs a device build and test.

## Optional LAN connection

The server binds only to `127.0.0.1` by default. To allow LAN connections:

```sh
HOST=0.0.0.0 ACCESS_TOKEN='replace-with-your-own-random-shared-key' npm start
```

LAN binding requires a shared key of at least 16 characters. Enter the same key on the dashboard and choose reconnect. The browser keeps it in memory only.

Before starting the app, put `pacescape-control.json` in `Application.persistentDataPath`:

```json
{
  "Enabled": true,
  "ServerUrl": "ws://192.168.1.42:8787/ws",
  "AccessToken": "replace-with-your-own-random-shared-key"
}
```

Use the computer's reachable IP. With the current Android application identifier `com.Kaan.PaceScape`, the expected external files directory is `/sdcard/Android/data/com.Kaan.PaceScape/files/`. For example:

```sh
adb shell mkdir -p /sdcard/Android/data/com.Kaan.PaceScape/files
adb push pacescape-control.json /sdcard/Android/data/com.Kaan.PaceScape/files/pacescape-control.json
```

Restart the app after changing this file. Without the file, saved project/build defaults apply, falling back to USB/loopback when no project settings asset exists. `{"Enabled":false}` disables remote connections on the next app start. The settings asset persists build defaults; this optional file persists a per-device override; settings changed by commands are not saved across app launches.

`ws://` is plaintext, including the shared key. Use this service on loopback or a trusted local network without public port forwarding. The server does not provide TLS or user accounts. The client does not bypass certificate validation for `wss://`.

## Supported behavior

- `runner.configure`: validates and applies pace, lane and start offset as one command. Existing waiting objects move to the configured start. Positional changes are rejected while Running or Paused; the target pace can still be changed live.
- `run.start`: includes the complete runner settings and starts only a Ready run with all placed objects visibly tracked. New placements always wait for an explicit start. No automatic start is queued when placement is missing.
- `run.pause`, `run.resume`: hold and continue the same positions. Resume requires recovered tracked content; it does not implicitly apply unsaved form input.
- `run.reset`: ends the run, returns objects to their configured start and retains anchors/track. It permits subsequent lane/offset changes.
- Legacy `runner.pace` and `runner.spawn` remain supported with the same validation rules; positional edits now update waiting objects and fail during active or paused runs.
- `tracking.start`, `tracking.stop`, `tracking.retry`: tracker component lifecycle. Stop/retry discard local anchors, track and objects; scan markers again. Start/retry require a Magic Leap device build.
- `tracking.configure`: printed marker side length, startup timeout and capacity; restart active tracking. When already stopped, save settings without starting it.
- `placement.configure`: floor mode, query padding and strict-surface distance/normal thresholds; restart active tracking, or save for the next start if stopped. Strict thresholds do not affect floor projection.
- Status: device name, Editor flag, phase/status/error, placed/pending marker counts, and configured settings. Target pace is not measured runner speed or user physiology.

Marker detection still initializes with the app. Run state starts as Ready and remains stationary until the dashboard start. All current objects share one run; new marker placement is deferred until Ready. The existing default settings, geometry, marker family and prefabs are preserved. A successful ACK confirms a command was applied or startup requested. It does not confirm that permissions, native subsystems, marker detection or placement subsequently succeed; those appear in later state messages.

Transport limits are explicit technical bounds, not scientific training parameters: pace 0.1–60 min/km, lane 1–8, start offset 0–1000 m, marker side 0.001–10 m, startup timeout 1–300 s, capacity 1–64, plane query padding 0.1–100 m, strict distance 0.001–10 m and normal alignment 0–1. Marker family stays AprilTag 36H11.

Runtime scene/prefab reference changes, BLE, treadmill data, heart rate and training protocols are outside the current app. They are not presented as functioning dashboard controls.

## Transport and implementation

`RemoteControlProtocol.cs` defines wire DTOs and `RemoteControlCommands.TryApply`. The only naming convention deviation is the DTOs' public camelCase fields, which match JSON consumed by JavaScript. Runtime APIs and other code use normal project conventions.

`RemoteControlClient.cs` bounds incoming messages/queue size, receives/sends strings asynchronously and applies commands/serializes state on Unity's main thread. State serialization allocates at the one-second reporting interval and after commands; no per-frame object lookup is added. A background transition pauses the run and cancels the socket session. Loss of head/anchor tracking also pauses it. On return, a new connection advertises the current state; the operator must explicitly resume. Disabled trackers retain their control connection. No commands are replayed after disconnects.

The server sends a `control` application heartbeat each second. Unity pauses a Running session on observed disconnect, or after six seconds without a recently received control heartbeat, and reconnects. Recovery never starts or resumes a run automatically. Closing only a browser tab does not sever the headset/server connection or pause the run.

The server supports one headset and multiple dashboards, checks same-origin browser upgrades, requires a key for LAN binding, caps messages at 16 KiB and 30/s per connection, pings for liveness and permits one pending command globally. It rejects commands if state is over five seconds old. Commands time out after eight seconds. Unity rejects commands queued on its main thread for over three seconds. A timeout/disconnect does not prove an already-started operation was never applied; inspect current state before resending.

The server's `PROTOCOL.md` documents exact camelCase envelopes and payloads. `npm run simulate` starts a deliberately labelled simulated headset. Stop it before connecting the real app.

## Verification

Completed: 13 server/simulator tests, JavaScript syntax checks, and browser verification of atomic start settings, pause, changed pace, resume and reset using the labelled simulation. Browser simulation does not exercise Unity code.

Pending: let Unity compile and report Console errors/warnings, then run the existing `PaceScape.Tracking.EditorTests` assembly including `RemoteControlTests` and `RunControlTests`. Unity MCP was unavailable while implementing this addition; no Editor compilation or test pass is claimed.

A new installed device build is required. Verify initial connection, live pace, future lane/offset, detector and placement restarts, stop/start, permission failure, retained diagnostics, USB or WLAN disconnects, background/resume, server restart and repeated cleanup on Magic Leap 2. Confirm memory/CPU overhead and Android/IL2CPP WebSocket support under sustained operation. Existing tracking/placement release checks remain applicable.

## Dashboard workflow

1. Start the updated server and updated device build; connect via USB forwarding or the configured LAN URL. The dashboard checks `runnerControlVersion: 1`; older clients cannot receive the new run commands.
2. Set pace/lane/start offset and choose **Läufer einstellen**, or leave the inputs for atomic application by **Lauf starten**.
3. Scan the marker and floor; start is disabled until all placed objects are visibly tracked. A Ready run state alone does not prove device readiness.
4. Choose **Lauf starten**; **Pause** then **Weiterlaufen** hold/continue the same positions. **Zurücksetzen** ends the run, resets its start positions and permits positional edits. It does not delete anchors.
5. Tracking stop/retry or detector/plane reconfiguration clears placements and returns the run to Ready. After reacquisition, another explicit start is required. Tracking/connection loss instead leaves a retained session Paused when anchors survive.

The .NET transport, native readiness and these physical lifecycle effects still require a new Magic Leap device build. Browser simulation is not evidence of those behaviors.
