# Domyos / FTMS treadmill input

PaceScape reads Bluetooth FTMS Treadmill Data (service `1826`, characteristic `2ACD`) on Magic Leap 2 through the user's embedded `com.kaan.ble-sensors` package and its pinned Velorexe dependency. The treadmill's reported belt speed measures the user, independently of the [ghost's preset or all-time reference](ghost-comparison.md). Motor control is opt-in and available from the local dashboard over USB or WLAN.

## Connect

1. Let Unity import and compile the updated project. The BLE package is embedded under `PaceScape/Packages/com.kaan.ble-sensors`; do not import a second copy of the supplied tarball under Assets.
2. Build and install the updated Android app. The existing tracking scene needs no serialized changes: its remote-control client creates the BLE input at runtime. Start the updated separate server in `/Users/kaan/Work/PaceScapeControl` with `npm start`, then open its dashboard. Restart an already-running server after updating it.
3. Enable Bluetooth on the headset. On Android 11 and earlier, enable location services as required by Android BLE scanning. Put the treadmill in its Bluetooth advertising mode and disconnect other fitness apps using it.
4. In **Laufband verbinden**, choose **Bluetooth-Geräte suchen**. Grant the requested permissions on the headset, select the treadmill by its name and ID, then choose **Laufband verbinden**. The scan is performed on the headset, not in the browser. All advertised BLE devices can appear; offering FTMS Treadmill Data determines compatibility.
5. Wait for **Gemeldete Geschwindigkeit**, place and track the runner as usual, then choose **Lauf starten**. Changes to belt speed update the user's measurements. The ghost follows its selected pace reference.

The Bluetooth panel also reports whether the connected device exposes the FTMS Fitness Machine Control Point (`1826/2AD9`). Discovery is passive. It does not request control or verify that the device accepts a remote start.

## Optional USB motor start

Use a new app build with `ServerUrl` set to `ws://127.0.0.1:8787/ws`. Keep the normal USB port forwarding. From the server project, start:

```sh
HOST=127.0.0.1 PACESCAPE_MOTOR_START=1 npm start
```

Open the dashboard from the same Mac at `http://127.0.0.1:8787`.

On the dashboard main page, **Lauf starten** starts both the ghost session and a stationary treadmill in USB motor mode. It requires a tracked placement and fresh 0 km/h telemetry. After browser confirmation, the headset requests control and sends FTMS Start or Resume. The ghost starts after the device confirms. If the belt is already moving, **Lauf starten** starts the ghost session directly. Initial belt speed is controlled by the treadmill.

The **Laufbandgeschwindigkeit** slider uses the device's Supported Speed Range (`2AD4`) and is enabled while the belt is moving. Load the range explicitly with **Tempobereich laden** while the belt is stopped. Normal connection and telemetry do not perform this optional read. The RUN500 log showed a failed native read and Bluetooth timeout, so a failed range request can still drop the connection. **Tempo übernehmen** sends the selected FTMS Set Target Speed value after confirmation. The displayed measured speed still comes from belt telemetry. Missing responses are inconclusive and commands are never retried automatically. In USB motor mode, **Lauf pausieren** sends FTMS Pause and freezes the ghost after acknowledgement. **Lauf fortsetzen** sends Start or Resume when the belt reports 0 km/h and resumes the ghost after acknowledgement. **Lauf beenden** resets the ghost session immediately when the headset receives the command and requests FTMS Stop separately. The dashboard confirms motor stop only after acknowledgement. Pause and stop are available with a connected control point even if belt speed telemetry is stale. If the device rejects the operation or the response is missing, check the console and use the treadmill controls or physical safety stop.

## Optional WLAN motor control

Under **PaceScape > Serververbindung**, enter the Mac's IPv4 address in the WebSocket URL and enable **Laufbandsteuerung im WLAN erlauben**. Save and use a new headset build. If the headset uses a connection override, it can set `AllowLanMotorControl` and `ServerUrl` in `pacescape-control.json`.

Start the server in the separate server project:

```sh
HOST=0.0.0.0 PACESCAPE_WLAN_MOTOR_START=1 npm start
```

Open the dashboard on the server Mac at `http://127.0.0.1:8787`. The headset can use `ws://<Mac-IP>:8787/ws` over WLAN without USB or a shared key. Plain WebSocket traffic is unencrypted. Enable this mode only on a trusted local network. Dashboards opened on other devices retain telemetry and ghost control. Domyos RUN500 control-point discovery was reported, but acceptance of the start command has not been verified on hardware.

## Stopping and interruptions

- Fresh 0 km/h is valid user standstill and contributes measurement time. It does not stop the independent ghost.
- Missing, invalid, disconnected or stale speed breaks user measurement continuity and cannot produce PRs across the gap. It does not change ghost target pace. Distance-only packets do not renew speed freshness.
- Connect/disconnect affects only the measurement input. The retired manual-input selection command is no longer exposed.
- The ghost uses preset speed or the stored all-time distance record. Current-run statistics do not override its preset. Its selected speed, position and path are independent of user distance.
- Explicit pause, tracking loss, app backgrounding and server/liveness loss still pause ghost motion. Recovery requires explicit resume. App backgrounding also disconnects the reused BLE transport.
- Device total distance remains a separate raw counter, not the source of record calculations. See [measurement definition](ghost-comparison.md).

## Protocol

Tracking-scene state advertises `treadmillControlVersion: 1` and a `treadmill` object with `supported`, `status`, `scanning`, `busy`, `connected`, `hasTreadmillData`, `deviceId`, `deviceName`, `hasSpeed`, `speedKmh`, `hasDistance`, `totalDistanceMetres` and `devices: [{id,name}]`. Validity flags distinguish missing samples from zero. The marker-free Editor test scene does not advertise BLE support; Android BLE cannot be tested in Editor Play mode.

Commands use the existing acknowledged command envelope:

| Command | Payload |
| --- | --- |
| `treadmill.scan` | `{}` |
| `treadmill.scan.stop` | `{}` |
| `treadmill.connect` | `{"deviceId":"ID from the current scan"}` |
| `treadmill.disconnect` | `{}` |

A connection ACK accepts the asynchronous attempt. Subsequent state reports connection, FTMS discovery and fresh data. The additive `treadmill.hasFtmsControlPoint` flag reports discovery of the control characteristic only.

## Sources and package provenance

- [Decathlon Domyos developer resources](https://github.com/Decathlon/domyos-developers) list supported FTMS models; the exact user model is still unconfirmed.
- [Official treadmill example](https://github.com/Decathlon/domyos-developers/blob/main/ftms-treadmill-web-console/ftms_treadmill.html) documents the FTMS UUIDs and little-endian speed scale of 0.01 km/h. PaceScape reuses the existing MarkerML2 decoder rather than copying the browser example. That decoder additionally handles More Data packets omitting instantaneous speed and the optional average-speed field before UINT24 distance.
- BLE package copied from `../MarkerML2/Packages/com.kaan.ble-sensors/` with existing metadata and native JAR preserved. MarkerML2 was not modified. The supplied `../MarkerML2/BLE-Delivery/com.kaan.ble-sensors-1.0.0.tgz` was compared: despite the same version, it predates the FTMS decoder and scan fixes, so it does not replace the newer source package.
- Velorexe revision: `55d8c12e155cc45bc26cea120d5e8c1e5f231b9f`, installed through Unity Package Manager. Keep this revision: the companion JAR accesses a private upstream field. Existing plugin code retains its original style and limits; new PaceScape code follows repository conventions.

## Verification status

Server tests and browser checks use synthetic, clearly labelled data and do not establish real BLE compatibility. `TreadmillTests` adds EditMode regressions for speed changes, zero-speed motion, distance-only freshness, disconnect/manual fallback, pause without automatic resume, and UINT24 offsets/truncation. They still require Unity's Test Runner when the Editor connection is available.

Pending: Unity compilation and EditMode tests, Android build, headset permission/scan/connect flow, actual model/firmware compatibility, notification cadence against the two-second freshness window, physical speed changes and 0 km/h, BLE loss/recovery, backgrounding and reconnect, multiple connect/disconnect cycles, tracking loss, and animation perception on hardware. No installed app changes until a new build is installed.
