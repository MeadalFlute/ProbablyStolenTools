# Restricted Complete Item Entity Writer

## Scope

Continues roadmap step 4 after the accepted 00:23:52 window session.
Snapshot.SemanticItemRestore defaults true, independent of shape/window switches.
Still requires the one sampled entity checkpoint per context. Full production
capture, independent read-only records/runtime payload and diagnostics remain.
User explicitly accepts migration overhead until development completes.

Only exact GameItemElement is admitted, with the existing exhaustive
GameItemElement/GameItem/GameItemFunc manifest chain. Entity fields include
parent inventory, counts, wide prices, original/modified states and shapes,
origins, flags, features/type/effect list references, descriptions/sprite metadata,
content/examine windows and temporary shape. Retain original objects and all
reference identities; never merge same-uniqueId items.

All callback fields remain Runtime and still restore through the complete old
graph. Six geometry caches and the established Visual fields remain outside
business capture. List contents/backing storage/capacity/version remain on the
old graph: restoring a list reference is not logical collection reconstruction.
Unknown subclasses are legacy-only.

No native save/encode/decode, new game entity, getter/setter, inventory/transaction
callback, Destroy or geometry method is used by entity writes. Existing refresh,
owner/full repair, visuals/geometry/final transforms and full verification remain
unchanged. Runtime payload restoration is still prohibited; inventory entities
are not migrated to actual writes yet.

## Shared Storage Writer

SemanticFieldEntityWriter extracts the accepted window storage-write logic so
items and windows use identical retention/reference validation and field writes.
Each wrapper supplies an exact type and native complete manifest chain.
Native window fixture is rerun unchanged in behavior.

Require supported primitive/enum scalars and explicit Entity/Runtime/Visual roles.
Capture field values and IDs without methods or collection enumeration. Float
and double comparisons preserve signed zero and NaN bits. Different values write
FieldInfo storage; references use ReferenceEquals guards, never business Equals.
Capture is single-use, including zero-owner captures; failed recapture leaves a
previous payload intact. Failures dispose partial capture.

Independent item slots/bytes consume the remainder after diagnostic entity/
runtime payloads and other pilot writers. Existing exact exhaustive owner
partition and combined metadata budgets remain mandatory.

## Writer Combinations

The three independent switches can select any subset. Prepare at most seven
nonempty plans at capture, never on each restore. Registration first prepares
every new combination locally; only then publish the candidate and plans.
Missing types contribute no writer. Preparation failure logs ITEM PILOT
UNAVAILABLE, preserving previously accepted shape/window plans and old graph.

At restore, intersect enabled flags with actually prepared writers. Use the
prevalidated matching plan or ordinary full Restore when none is selected.
All selected writer validations precede any graph write, and all writers'
scalars precede every reference write. Real write failures propagate without
hidden legacy retry. RESTORE OK adds itemPilotOwners for actual participation;
RECORD summary adds prepared itemPilotOwners/itemPilotSlots and combined budgets.

Preparing additional combinations adds first-node diagnostic work. No capture
replacement or measured speedup is claimed.

## Verification

323 offline tests: eleven new shared-writer tests cover inherited fields,
business/runtime/visual split, original lists and callback identities, float/
double bit patterns, unnamed wide enums, exact-type/subclass fallback, exact
object/slot/byte boundaries, changed/out-of-graph rejection, disposed preflight,
separate past/future payloads after old graph disposal/GC, incomplete inheritance
or runtime coverage, unsupported scalar schemas, cross-node plans and recapture.

Native fixture retains the previous six shape and six window/shape cycles.
Adds fourteen cycles covering all seven nonempty combinations twice: mutate
every Entity field on two real items sharing uniqueId=0, all three declaring
classes' roles, shared original item-type lists and backing contents, original
shape/window links, and throwing item callbacks. Every restore agrees with
complete legacy and independent entity/runtime oracles. All-disabled means no
entity writes.

An item-only candidate fits two owners but a combined plan exceeds its owner
budget: rejection leaves shape/window plans available, then a proper retry
registers successfully. Item object/slot/byte limits all reject. Disposed item
preflight prevents ordinary, shape and window business writes; disabling that
writer still permits retained shape/window-only plans.

Release build zero warnings/errors, native 57 hooks / 74 original private fields
and strict manifests pass. Official scanner HardFlags empty, capabilities unchanged.

## Live Boundary

Restart game. Capture first sampled node with items in a container and on the
counter. Rotate/flip/move items, buy/sell, and change stack quantities where
available. Close/reopen container windows. Create/visit later and manual nodes,
then return to the sampled node at least three times, including both backward
and forward restores. Group-drag after restore.

Verify original container membership, quantities/values, sprites/collisions and
window contents. Sampled RESTORE OK should report positive itemPilotOwners,
windowPilotOwners and shapePilotOwners; ordinary nodes remain zero.
Require final diff=0 / releaseOk=True and independent RECORD RESTORE MATCH /
runtimeDiffSamples=0, no unavailable or failed lines. Rendering needs inspection.

Stop here before inventory entity writes or independent runtime-payload restore.
