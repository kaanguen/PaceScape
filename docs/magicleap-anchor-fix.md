# Magic Leap SDK 2.6.0 anchor change fix

## Device failure

A device run on 2026-11-04 reported `InvalidOperationException: Trackable 0000000000000000-F52B75076E45AD88 being updated this frame has at least one other action associated with it.` immediately after requesting an AprilTag anchor. The anchor then remained untracked until PaceScape's 15-second timeout. The timeout is consistent with AR Foundation rejecting the change batch before applying it; a new device run is still required to confirm the fix.

## Cause and correction

In SDK 2.6.0, `MLXrAnchorSubsystem.MagicLeapProvider.TryAddAnchor` queues a pending anchor in `addedAnchors`. On a subsequent `GetChanges`, its native future can already be ready. The completion branch previously queued the same ID in `updatedAnchors`, leaving the ID in both arrays. AR Foundation 5.2.0's `ValidationUtility<XRAnchor>` rejects that batch. The ID salt in the device error matches this provider's `AnchorTrackableIdSalt`.

The SDK is embedded through Unity Package Manager at `PaceScape/Packages/com.magicleap.unitysdk/`, retaining version 2.6.0 and its original metadata and license. Only `Runtime/OpenXR/SpatialAnchors/MLXrAnchorSubsystem.cs` differs from the original SDK source. A baseline SHA-256 comparison confirmed that all other 1,622 files matched immediately after the patch. `Packages/packages-lock.json` records the embedded source; the existing manifest version remains unchanged. No package upgrade or application placement change is required.

The completion branch now calls `QueueCompletedAnchor`. If the ID is still waiting in `addedAnchors`, it replaces that entry with the completed pose and Tracking state, without queuing an update. Otherwise it queues one update, replacing an already queued update for that ID if necessary. A slow completion continues to be reported as an update in a later batch. The readiness pass also fixes local-anchor cleanup. Removing an anchor with no native space yet now cancels its future through `xrCancelFutureEXT` (supported for pending and ready futures), then removes its managed pending/ready records. Removing anchors after the native session ended clears managed state without accessing invalid handles. `ForgetAnchor` suppresses unreported additions and queued updates before emitting at most one removal for previously reported IDs; removing stored anchors from the subsystem uses the same batch cleanup. Persistent storage operations themselves are not extended.

Cancellation behavior follows the [OpenXR XR_EXT_future specification](https://registry.khronos.org/OpenXR/specs/1.1-khr/html/xrspec.html#XR_EXT_future). Tests exercise managed cleanup without a native session; cancellation on the Magic Leap runtime remains a device check.

The `ARAnchor` creation path remains the one documented in the [Magic Leap Spatial Anchor API overview](https://developer-docs.magicleap.cloud/docs/guides/unity-openxr/spatial-anchors/unity-spatial-anchor-api/): position the object, add `ARAnchor`, then wait for it to stop being pending and report Tracking. Bypassing validation, consuming subsystem changes from a second owner, and arbitrary frame delays are not part of this fix.

## Verification and maintenance

`Assets/Tests/Editor/AnchorChangeTests.cs` reproduces the original duplicate batch using AR Foundation's actual validator. Two completion cases invoke the patched provider's completion queue and actual `GetChanges` with native calls inactive: completion in the initial batch and completion after the initial batch was delivered. Both must report exactly one completed change with the latest pose, pass Unity validation, and leave the next batch empty.

Verification on 2026-11-04: all 48 tests in `PaceScape.Tracking.EditorTests` passed, including the three anchor regression cases. Unity MCP reported no C# errors after the final import; the unchanged SDK also emitted deprecation warnings during its full recompilation. `git diff --check` passed.

Two further tests remove pending anchors before and after their initial addition was delivered, checking managed future cleanup and Unity validation. The current tracking assembly contains 60 passing tests after the readiness pass.

These tests do not exercise OpenXR futures on hardware. Install a new build and check that scanning the marker produces no duplicate-ID exception, the anchor reaches Tracking, object and track become visible, and the runner moves on the selected lane. Check pause/resume and retry as before.

Keep the embedded package and its `.meta` files in source control; binary files follow the repository's Git LFS attributes. On a future SDK upgrade, compare the upstream completion logic and remove or reapply this patch deliberately. Do not edit `Library/PackageCache`.
