# Running Lab AR Coach — Magic Leap 2 Proof of Concept

**Concept report | 1 June 2026 | Treadmill-first prototype**
**Client: Absolute RUN Kaufmann, Heilbronn**
Companion illustration: `running_lab_spatial_concept.svg` (editable Inkscape SVG)

## 1. Executive summary

The proposed demonstration turns a conventional treadmill gait-analysis session into a **spatial coaching experience**. A runner wears **Magic Leap 2** while an external **Orbbec Femto Bolt RGB-D camera** observes their movement. Instead of reading measurements on a display, the runner sees a small number of augmented-reality cues registered to the treadmill and their own movement: synchronized step-timing targets, a cadence rhythm, a posture reference, and a virtual pacing partner. After the session, a ghosted gait replay explains what happened and what changed between trials.

**Value of AR:** the guidance is shown *at the place and moment of movement*, not as a detached dashboard. A store coach can demonstrate how running mechanics relate to a trial, without claiming to diagnose injury or identify a universally “correct” running style.

**Recommended proof of concept:** live rhythm/posture coaching from the depth camera, followed by a short spatial replay. Ochy can enrich the session with an established gait-analysis report, but is **not required** to make the demo work.

## 2. Experience: what a customer sees

1. **Calibrate.** The customer steps onto the treadmill. The coach selects a target pace and a comfortable cadence goal. The headset locates a pre-defined treadmill coordinate frame.
2. **Run with minimal cues.** Discreet left/right step-timing rings appear *near* the expected step locations; a rhythm cue pulses at the desired timing. A shoulder-to-hip posture reference appears only when useful. A translucent virtual runner or stride-cycle silhouette provides a **cadence/phase reference**, not a misleading indication of advancing down a stationary treadmill.
3. **Capture a baseline.** The RGB-D camera estimates body joint positions and a local processor calculates cadence, step regularity, coarse trunk inclination, and provisional left/right timing metrics.
4. **Review in space.** With the treadmill stopped, the customer sees their recorded skeleton/ghost replay positioned in the same physical running volume. The coach can inspect foot-contact estimates, rhythm stability, and movement differences.
5. **Optional shoe A/B trial.** Record a second short run under the same speed and conditions. Compare changes in cadence, rhythm regularity, movement symmetry and a separately entered comfort rating. *Changes are observations, not proof of superior injury prevention or shoe performance.*

**Suggested on-headset UI:** at most one active coaching cue plus a subtle rhythm indicator while running; richer metric labels only during the stationary review.

## 3. Equipment and technical architecture

| Component | Role |
|---|---|
| **Magic Leap 2** | Optical see-through AR headset; Unity-rendered spatial cues and offline replay, placed relative to the treadmill using calibration / spatial anchors. |
| **Treadmill** | Controlled, repeatable running speed; optionally expose belt speed over an available device interface, otherwise enter it manually. |
| **Orbbec Femto Bolt RGB-D camera** | Fixed front-oblique or side-oblique view of the runner; RGB, depth, and body pose capture. Ensure the full lower limbs remain visible. |
| **Local PC / edge workstation** | Camera SDK, body tracking and/or pose inference, gait-event estimation, per-stride metrics, recording, and low-latency network stream to Unity. |
| **Coach station** | Start/stop and cue selection, run labeling (shoe A/B), review, and operator override. |
| **Optional Ochy service** | Post-run gait assessment, reporting, coaching interpretation, and retail-specific shoe recommendations if the client has suitable export/API rights. |

**Data flow:** Femto Bolt → Orbbec SDK / K4A wrapper + body tracker on PC → timestamped joints and derived metrics → UDP/WebSocket (prototype) → Unity on Magic Leap 2. Separately: Ochy video analysis or exported report → normalized session results → Unity replay and comparison layer.

**Important integration caveats:**

- **Magic Leap 2 Unity compatibility:** Unity documents Magic Leap new-project support through **Unity 6.2 (6000.2)**, but not Unity 6.3+; select and freeze a supported editor / Magic Leap OpenXR package combination before development. See [Unity XR support](https://docs.unity.com/en-us/engine/6000.3/manual/xr/support/support-packages) and [Magic Leap OpenXR guidance](https://developer-docs.magicleap.cloud/docs/category/unity-openxr/).
- The **Femto Bolt is an external sensor**, not a direct headset input. Its SDK and compatible body-tracking route run on the workstation, with results sent to the headset. See [Orbbec documentation](https://doc.orbbec.com/documentation/Orbbec%20Femto%20Bolt%20Documentation/About%20Femto%20Bolt) and [K4A wrapper](https://doc.orbbec.com/documentation/Orbbec%20Femto%20Bolt%20Documentation/Using%20Orbbec%20SDK%20K4A%20Wrapper).
- A single camera may lose sight of the far leg or feet. Begin with a tested camera angle and a restricted metric set; add a second view later if needed. Register camera coordinates to treadmill/headset coordinates with a measured calibration target and verify drift before each run.
- Step contacts and ground-contact time based on pose are **estimates**. A force instrumented treadmill, pressure insoles or contact sensors would be required for stronger event ground truth. Distance, exact foot strike, joint kinetics and true running economy should not be inferred confidently from one unvalidated camera.

## 4. What is Ochy, and do we need it?

**Ochy** is a commercial, video-based running gait-analysis product. Its proprietary computer-vision system tracks body joints and gait events from short recordings; its published offering includes running metrics such as **cadence, ground-contact time and flight time**, posture / overstride-related assessments, and coach-facing explanations. Ochy markets running-retail workflows and business/API partnerships. See [Ochy for Business](https://www.ochy.io/business), [Ochy science](https://www.ochy.io/science), and the [API access request page](https://forms.ochy.io/r/2Ex2Yb).

**With Ochy:** run its existing analysis as a *batch assessment* before/after the treadmill trial; ingest licensed structured results if the retailer has API access, or an authorized export if not. Translate the results to a replay highlight and a small number of actionable coaching cues. **Do not assume Ochy offers live skeletal streaming, guaranteed API access, or a particular export schema** without confirming the client’s agreement.

**Without Ochy:** keep exactly the same headset experience. Replace the analysis layer with open-source pose estimation / the Femto Bolt body tracker and a transparent set of custom signal-processing rules. This provides cadence and rhythm coaching relatively quickly, while some gait metrics will need validation. Ochy is an **optional analytics provider**, not a dependency of the AR concept.

## 5. Alternative tracking models and analytics

These tools estimate **pose**, not necessarily clinical gait metrics. Gait parameters must be calculated from trajectories, timestamps, treadmill speed and validated contact events.

| Option | Best use | Limitations / implementation note |
|---|---|---|
| **Orbbec / Azure Kinect-compatible body tracking** | **First choice for live prototype**: direct metric-scale 3D skeleton from the existing Femto Bolt. | Validate foot visibility, occlusion and contact detection on treadmill; check SDK/runtime and deployment terms. [Orbbec](https://doc.orbbec.com/documentation/Orbbec%20Femto%20Bolt%20Documentation/Using%20Orbbec%20SDK%20K4A%20Wrapper) |
| **MediaPipe Pose Landmarker (BlazePose family)** | Fast RGB-only fallback; 33 landmarks, supports live video inference. | World landmark output is not automatically calibrated to treadmill coordinates; fine ankle/heel events may be noisy. [Google](https://ai.google.dev/edge/api/mediapipe/python/mp/tasks/vision/PoseLandmarker) |
| **RTMPose / MMPose** | Configurable open-source RGB pose inference; useful if the depth camera’s built-in tracker struggles. | 2D pose needs depth lifting / triangulation for reliable 3D. Verify selected model-weight licensing. [MMPose](https://github.com/open-mmlab/mmpose) |
| **Sports2D** | Quick side-view running replay and joint-angle analysis from one camera; good offline baseline. | Results are strongest when motion stays close to a sagittal or frontal plane, not arbitrary 3D. [Sports2D](https://github.com/davidpagnon/Sports2D) |
| **Pose2Sim / OpenSim** | Later, research-oriented multi-camera 3D kinematics and detailed replay. | Higher calibration and computation burden; not the shortest path to responsive coaching. [Pose2Sim](https://github.com/perfanalytics/pose2sim) |
| **OpenCap** | Additional research / validation pathway using phone video. | Different acquisition workflow; research access and commercial terms need checking. [OpenCap](https://www.opencap.ai/) |

### Proposed measurable outputs

| Metric / cue | V1 feasibility | Source / interpretation |
|---|---|---|
| Cadence and visual metronome | **High** | Time between detected gait cycles or alternating steps. |
| Step-timing regularity | **Medium–high** | Step-to-step intervals; suppress values when tracking confidence drops. |
| Posture reference (trunk lean) | **Medium** | Angle of torso segment relative to vertical; individual coaching target, not a universal optimum. |
| Ghost cadence / pace guide | **High** | Render from target cadence and entered treadmill speed; ghost pose is a reference animation, not detected athlete performance. |
| Left/right timing asymmetry | **Medium** | Compare event intervals only when both legs are reliably observed. |
| Overstride *indicator* | **Experimental** | Foot position relative to pelvis proxy near estimated contact; not a diagnosis. |
| Ground-contact time / vertical oscillation | **Experimental** | Derived from estimated contact or pelvis movement; validate against reference hardware before showing as precise measurements. |
| Heart rate / recovery state | **Not in V1** | Requires independent wearable or longitudinal physiological data, not the depth camera. |

Use **goal-specific feedback**, e.g., cadence adherence and step regularity. Avoid a universal running-form score. Show unavailable or low-confidence metrics as unavailable rather than fabricating precision.

## 6. Prototype phases and decision points

**Phase 1 — functional live demo:** fixed treadmill + Femto Bolt, local body-tracking stream, Unity/Magic Leap anchoring, one cadence target, one posture cue, and recorded skeleton playback. Success criterion: cues remain spatially stable, frame timing is coherent, and a runner can complete a short coached segment safely.

**Phase 2 — useful lab replay:** calculate repeatable per-stride metrics, confidence flags, synchronized replay and shoe A/B comparison. Run comparisons at matched speed and duration; collect subjective comfort independently.

**Phase 3 — Ochy integration:** if authorized, ingest analysis reports or business API outputs, map Ochy’s assessments to spatial annotations, and test whether recommendations are clearer than conventional reports. Keep the provider adapter replaceable.

**Later roadmap — outdoors:** replace the fixed RGB-D tracking volume with **wearable IMUs** (e.g., foot/waist placements, possibly a watch) and GPS / other wearable inputs as appropriate. Develop sensor fusion, synchronization, and outdoor AR interfaces separately; outdoor motion capture is **not** included in the treadmill prototype.

## 7. Safety, privacy and validation

Treadmill use with a headset requires a **supervised session**, controlled speed, a readily reachable emergency stop / tether, clear real-world visibility, minimal visual clutter, and a stationary review mode. Do not place virtual objects that invite users to move off the belt. Confirm safe fit and visual conditions before running. Record video only with informed consent, define retention/deletion rules, and prefer local processing for raw body imagery where possible. Validate camera-derived events against high-speed video or reference sensing before making quantitative claims in customer reports.

## Decision requested

Approve a **treadmill-first Magic Leap 2 spatial coaching POC** with a **Femto Bolt + local motion analytics** baseline. Treat Ochy as an optional plug-in to enrich reporting, pending confirmation of the retailer’s integration rights and outputs. Keep outdoor IMU-based coaching for a subsequent phase.
