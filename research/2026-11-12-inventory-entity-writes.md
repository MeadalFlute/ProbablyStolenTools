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

## Deployment

2026-11-12 00:51, runtime commit ae2ec9c, version0.8.14, no push.
Backup: out/full-release-deploy-20261112-005125.
DLL SHA256: 82CC372580C54DBA8ACA1DEFB3FCD7C66F89B6C4C243F9D1AF41EC7D91DAAEC9.
78 save files verified unchanged; no settings imported, no game started.
Awaiting the first live inventory entity writer test.

## First Live Results

2026-11-12 00:54:04.901 session; user reports completed except group drag.
Five captures including manual id5, nine restores. All final diff=0,
releaseOk=True, finalFallback=False. Three sampled id1 restores actually used
286 inventory writers (107 grid/179 slot), alongside953 item/101 window/2966
shape writers. Inventory slots4148 plus the other pilots exactly cover78095
Entity slots. Independent entity/runtime records all MATCH/runtimeDiffSamples=0.
Other nodes report zero pilot owners.

First capture3084 ms: access3, walk613, items103, semanticShadow148,
entityAudit44, entityRecords2115, verify49, prepareHistory7.
runtimeCaptureVerifyMs864 is included in entityRecords. Other captures
278/345/307/263 ms. Restore totals724/618/672/772/711/623/723/687/739 ms,
median711 ms. Non-A/B; no attributable overall speedup. Retain accepted
migration diagnostics for now.

Ordinary nodes and sampled id1 report four cassettePlayer.modifiedState
dictionary-field changes (buckets/entries/count/version), repaired as one owner/
ten slots. Manual id5 reports seven slots/three owners/45 planned slots.
No FAILED/UNAVAILABLE in this session. Bargain flags remain default.
Player.log error/exception matches include native startup ModTrustList TLS
failure and Curl35 connection reset, not a demonstrated snapshot regression.

Ten group drags completed before the first restore, ranging2-8 items.
None occurred after the first restore at00:57:58.915. Do not count these as
post-restore group-drag acceptance, consistent with the user's explicit caveat.
Need a short follow-up using the same deployed build: restore the sampled
first node, group-drag2-4 items, visit a later node and return, repeat the drag.
Inspect membership and sprite/collision alignment. No new runtime deployment
or independent-runtime writes until this interaction gap is checked.
