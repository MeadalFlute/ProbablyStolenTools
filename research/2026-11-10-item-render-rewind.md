# Item Rendering After Rewind

Date: 2026-11-10. Runtime: full release Mono / IMod / Harmony.
Final build version: 0.8.12. No Git push or Workshop publication requested here.

## Evidence

- The user's collision/slot state rewinds, but sprites stay at moved positions,
  pack into rows after sales, or disappear on the counter. Dragging repairs some
  of these views.
- The real GameItemElement.UpdateRectTransform method compares modifiedShape
  against appliedMinX, appliedMinY, appliedOrientation, appliedFlipped and
  appliedGridSize. It only calls ApplyRectTransform if one differs.
- The previous graph snapshot restored these caches but did not capture middle,
  rotator or background RectTransform state. Restoring the cache can therefore
  suppress a required native layout. Dragging changes the cache inputs again.
- PSTools.log at 14:45:23 shows processed_meat at shape (4,0), expecting (64,0),
  but middle remains (192,-128); RESTORE OK reports diff=0. Graph equality alone
  does not validate Unity-native rendering.
- UI refresh can alter business state; the existing reconciliation restores the
  graph again after the prior geometry refresh. Shapes/origins can change after
  that refresh, making its native coordinates stale again.
- The previous inferred parentInventory.inner reparenting is not authoritative:
  a native inventory can render through a window or a different host.

Decompilation uses this installation's Assembly-CSharp.dll, SHA256:
725CB999D019DFF1FF31BED353C7F9BB8D81A7605A15DE1F80F5A3A6CF7B1D05.
Read-only decompiled sources are in out/rewind-render-fix-20261110.
Pre-test save, index, settings and log copies are in
out/rewind-render-pretest-20261110-145549.

## Changes

1. Exclude the five applied* cache fields and validateLock from Mono business
   snapshots. Keep item shape, origin, count, ownership and inventory state.
2. Capture item handler/middle/rotator/background/inner and corresponding fake
   nodes, plus inventory/window roots. Preserve exact native and logical render
   parents, RectTransform anchors/pivot/size/anchoredPosition3D/rotation/scale,
   activeSelf, and Image sprite/enabled/color/raycastTarget.
3. Invalidate appliedGridSize before the initial native Validate. After any final
   business reconciliation, restore captured visuals again and force native
   layout from the final shape, including rotation, flip and fake geometry.
   Write both background origins and final anchors last.
   Concurrent commit 0e1a1ed adds a final RestoreTransforms pass after this layout:
   captured native rotation/flip/anchors/size are authoritative. This change was
   preserved and included in the final build and compatibility run.
4. Preserve the pre-existing uncommitted manual-anchor correction, moving its
   behavior to this final phase. Remove obsolete early coordinate writes.
5. Check business differences after final geometry; fail rather than declaring
   success with remaining differences. Add hidden-image diagnostics and a
   final geometry synchronization log. Closed/inactive windows are not treated
   as hidden-image faults.

The final geometry phase does not run Validate, inventory acceptance, arrival,
trade, or UI state-change callbacks. No SaveGame or user ES3 writes are added.
Initial Validate and UI refresh remain part of the existing restore pipeline.

## Verification

- Mono Release build: zero warnings and zero errors.
- CheckpointTests: 63 passed, including read-only parsing of the actual ES3.
- Added two modeled regressions: reproduce the old stale-cache bug independently
  using the frozen legacy backend; verify cache exclusion, shape/count recovery,
  final reconciliation, rotation/flip and repeated backward/forward restores.
  These are managed models, not Unity rendering tests.
- ReleaseCompatibility: 55 Harmony targets and 73 required/private fields.
- The real GameItemElement data graph excludes all six cache fields but restores
  count/origin and modifiedShape identity/position/size/rotation/flip.
  Existing real PlayerStore/StoreStation data, four distinct array
  items, dictionaries and compiled readers still pass (334 compiled readers).
- Official scanner: no HardFlags or Error; capabilities unchanged.
- Native configuration round trip passes; no user configuration is edited.

## Deployment

Deployed with scripts/Deploy-FullRelease.ps1. Backup:
out/full-release-deploy-20261110-145843.

DLL SHA256:
704D75B11656817901F5D41CFCB6C0EC27ABC4C3FD90B2708921776765AB3B9F.

The deployed DLL matches the build. All 316 pre-existing save-directory files,
game Managed assemblies and other mods were verified unchanged.
SettingsImported=False; GameStarted=False. This task did not run commit or push;
concurrent commit 0e1a1ed was observed during final verification.

## Manual Acceptance Still Required

Start the game with the deployed build and capture fresh checkpoints.
Do not rely on nodes captured by a previous running build.

1. Move an item within an inventory and between inventory/counter/display hosts;
   rewind without dragging it. Check sprite position, hover outline and drop
   blocking all agree.
2. Sell one and then multiple items. Rewind and check that sprites return to
   their original slots, not a newly packed first row.
3. Move or sell counter items. Rewind and confirm visible sprites can be clicked,
   dragged and sold, and that no invisible item blocks a slot.
4. Check rotated/flipped items and nested inventories; browse earlier and later
   checkpoints repeatedly. Check quantities and trade state as well as images.
5. Logs should show the final geometry synchronization line and RESTORE OK
   diff=0. Collect any restore-post MISPLACED/NOSPRITE/HIDDEN entries and the
   reproduction sequence if a visual failure remains.

No game process, native canvas, pointer interaction or live trade was exercised
by these automated checks. In-game rendering acceptance remains pending.
