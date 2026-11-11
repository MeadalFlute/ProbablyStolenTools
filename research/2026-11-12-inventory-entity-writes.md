# Restricted Inventory Entity Writers

## Scope

Continues roadmap step4 after the accepted 00:39 item session.
Snapshot.SemanticInventoryRestore defaults true. Only the sampled checkpoint
per context admits exact GameGridInventory and GameSlotInventory, as one atomic
switch family. Full GameInventory/GameInventoryFunc/TagSystem manifests apply.
The shared field writer restores 12 grid / 16 slot Entity fields, including
original membership references, parent/shape/borrowed-window links and locks.
No business getter/setter, Accept/Expel, validation callback, constructor or
native save/encode/decode is invoked. Unknown subclasses remain legacy-only.

TagSystem dictionary, inventory callbacks, grid rendering caches, slot
validateLock and all collection physical storage stay on the old graph.
No logical collection reconstruction or capacity trimming. Existing visual
exclusions, full capture, complete validation, owner/full repair and visual
ordering remain unchanged. Independent runtime payload is still read-only.

This fills Entity field writer coverage for the five admitted exact types
on a sampled node, not all game/mod types or a complete production backend.
Independent runtime writes, production capture replacement and removing
migration-time duplicate diagnostics remain later work.

## Preparation And Budgets

Grid and slot use SemanticFieldEntityWriter without core changes.
Capture grid then slot using remaining combined diagnostic/pilot slot and byte
budgets and remaining inventory object budget. Publish both together only
after every requested combination prepares successfully. Failure disposes
the candidates and preserves all earlier writer combinations and full graph.

Four switch families (shape/window/item/inventory) prepare at most15 nonempty
plans, not31. Missing grid or slot type contributes no writer; both absent
contribute no family. All selected writers preflight before any graph write;
all scalars precede all references. Retained disposed writers cannot silently
disappear from mask selection when disposal clears their owner lists.
Real write failure propagates without hidden retry.

Session records the inventory switch. RECORD summary records prepared grid,
slot and total inventory owners/slots; RESTORE OK reports actual inventory
participation. Other nodes remain on the full old graph.
Extra plans add accepted development-time first-node work; no speedup claim.

## Verification

Release build: zero warnings/errors. Existing323 offline tests pass.
Native57 hook targets /74 original private-field checks and strict manifests
pass. Official scanner HardFlags empty, capabilities unchanged.

Native fixture adds30 cycles covering all15 switch subsets twice. Mutate
every inherited inventory Entity field, original member list contents/capacity,
item parent/count, shared shape/window, shared original tag dictionary,
throwing inventory callbacks, grid caches and slot validateLock. Every result
matches the full old graph and independent entity/runtime oracles.
Total pilot slots exactly equal the complete native Entity partition.

Grid/slot object, slot and byte budgets reject. An inventory-only family fits
but its combination exceeds the object budget: atomic rejection leaves prior
plans usable and a proper retry succeeds. Both disposed inventory writers
are rejected before ordinary or earlier entity writes; disabling inventories
still permits prior shape/window/item combinations.
Grid-only and slot-only roots work independently. With neither type, attempting
inventory restoration returns false without writing; ordinary restoration
remains available. Previous shape/window/item tests still pass.

## Live Boundary

Restart game and establish the sampled first checkpoint. Move items between
containers/counter/single-slot holders, buy/sell where available, close/reopen
container windows. Create later and manual nodes, return to the sampled node
at least three times, including backward/forward travel, then group-drag.
Inspect original membership/order, lock states, borrowed windows, quantities,
sprites and collision alignment.

Expected for a comparable scene: sampled inventoryPilotOwners positive
(prior sample grid107 + slot179 =286); pilot slots cover the full Entity
partition. Ordinary nodes remain zero. Require final diff=0/releaseOk=True,
independent RECORD RESTORE MATCH/runtimeDiffSamples=0, no unavailable/failure.
Rendering and interactive behavior need Unity inspection.
Stop before independent runtime writes or production capture replacement.
