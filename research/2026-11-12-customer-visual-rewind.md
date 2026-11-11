# Customer visual rewind repair

## Report and evidence

The user reports a missing customer sprite after the 01:27:42.959 session on
2026-11-12, and says similar reports predate the current semantic pilot.
Semantic expansion is paused pending visual acceptance.

The session has three captures and five successful business restores:
id=1 three times, id=2 once (legacy), id=3 once (manual independent runtime).
All have final diff=0, releaseOk=True and finalFallback=False. Both independently
sampled payloads report MATCH. Three completed group drags follow the first
restore; only one restore of id=3 is present, so this is not the requested three
two-sample round trips. No snapshot exception is visible in Player.log; its
network/TLS errors are unrelated evidence, not an explanation for missing sprites.

Previous logs do not record renderer state or tween progress. They cannot show
which node lost its image, whether it was hidden or offscreen, or whether the
semantic pilot introduced the reported symptom. Business diff=0 does not validate
Unity rendering.

## Native-source findings

Checked the currently installed Assembly-CSharp.dll, DOTween.dll and DOTweenPro.dll
using ilspycmd, rather than relying on older decompiled reference files.

- StoreClientMono owns a main DOTweenPath and pathRight. IsMoving checks playing
  status only; a finished path at the exit is also considered settled.
- The previous CustomerVisual restored the mono's local position, sprite/color,
  and two activeSelf flags, but neither path's cursor. A subsequent backwards
  leave can therefore start with progress from the abandoned timeline.
- The renderer's own/intermediate transforms, enabled, forceRenderingOff and
  MaterialPropertyBlock were not captured. A transform restoration on only the
  mono cannot restore a separately moved renderer.
- IdleAnimator.OnEnable -> Play overwrites originalPosition/timer/playing;
  OnDisable -> Stop can also move its transform. Previous activation occurred
  before position restoration. Graph reconciliation can repair managed idle
  fields, but the final customer pose was not reapplied after that write.
- DOTween.Goto is not callback-free, despite not using GotoWithCallbacks:
  native code can dispatch update/rewind/complete/pause and path waypoint events.
  Replaying OnLeft would hide the customer and change business state.
- Current native StoreClientMono additionally has leaveTimeScale; it was absent
  from the selected business root. This scalar now participates in the graph.

These are verified restoration gaps, not a conclusive reproduction of the
reported rendering failure.

## Scoped repair

- Capture settled original path/tween identities, elapsed time, direction and
  timeScale. Validate both paths before business writes; reject replaced/killed
  tweens without seeking. Do not construct replacement paths or kill tweens.
- Seek each original tween while temporarily detaching public callbacks.
  Restore callback identities in finally even on an exception. Goto's internal
  onStart is not used by its Goto update mode; no new private tween writes.
  Restore leaves paths paused and does not dispatch arrival, departure or trade.
- Capture the original renderer and its transform chain through the mono's
  immediate parent, including local position/rotation/scale and activeSelf.
  Reject destroyed/reparented views. Capture sprite/color/enabled/forceRenderingOff
  and the complete renderer property block.
- Restore pose before activation, then again after activation; restore the
  renderer's exact captured state, not unconditional visibility.
- Reapply only customer transforms after reconciliation and after a final
  full-graph fallback, before final business validation. Do not reactivate at
  those stages, which would introduce additional idle lifecycle writes.
- Bounded CustomerVisual diagnostics at capture, before restore, restored, and
  once at frame+2. Include sprite, active hierarchy, renderer switches, alpha,
  solid blend, local/world pose, transform targets and both path cursors.
  The deferred observation is discarded on context/epoch/active-node change.
  It does not continuously force the renderer or emit per-frame logs.

The full old graph, independent semantic oracle, owner/full repair and existing
item visual ordering remain. No native saving/loading, collection rebuilding,
or production semantic capture replacement is introduced.

## Verification and acceptance

- Release Mono build: zero warnings/errors.
- 347 offline tests, including six tests exercising production CustomerPathState
  against a callback-dispatching tween fixture: silent seeks, callback preservation
  on error, preflight rejection, optional/moving paths, six two-node round trips,
  and failed-node isolation. These do not execute Unity rendering.
- Formal compatibility: 57 hooks, 76 private/snapshot fields, original path and
  callback/seek contracts, all existing native semantic fixtures.
- Official scanner HardFlags empty; manifest capabilities unchanged.

Live acceptance remains required: use different customers and manual nodes,
rewind after a customer leaves, alternate nodes at least three times, wait after
each restore to observe idle animation, then transact/dismiss normally and call
the next customer. Check immediate and delayed visibility, correct identity,
and absence of duplicate arrival/leave effects. Repeat item group dragging once.
If the sprite still disappears, retain the CustomerVisual lines and the exact
action preceding the disappearance. Do not expand the backend until this passes.

## Deployment

Runtime commit `06a299e`, deployed 2026-11-12 01:37:59, version 0.8.14, not pushed.
Backup: `out/full-release-deploy-20261112-013759`.
DLL SHA256: `7EB544D0BE3F41E36B037398FCD6DF8B6F6D50F9C49A6C27FAC4643F48FF1B52`.
Game was closed, all 78 existing save-area files verified unchanged, settings
not imported, game not started. Live rendering acceptance remains pending.

## Live follow-up

The user now confirms the sprite is restored and clarifies that the historical
failure was visual-only: trading and dismissal were not affected; later state
updates could reload the image. The 01:39 session has six successful business
restores and expected visible/hidden renderer states immediately and at frame+2.
The right-leave parent moves offscreen; the previous visual snapshot did not
restore its pose. This is consistent with the report, not isolated proof of
every historical trigger. Continue bounded semantic work with this repair kept.
Full session results and remaining coverage gaps:
`research/2026-11-12-semantic-plan-composition.md`.
