# Paired Entity And Independent Runtime Writes

## Scope

Follows accepted inventory tests and the01:04:53.592 post-restore group-drag
follow-up. Snapshot.SemanticRuntimeRestore defaults true, separately gated.
Only the existing sampled checkpoint per context participates.

Its first graph restore can now use the complete entity writer group together
with the independently captured remaining runtime payload. No ordinary old
graph first writes occur on that path. Keep the complete old production capture,
full preflight/final comparison, independent entity/runtime checks and original
owner/full reconciliation. Production capture and Census are not replaced.
Migration diagnostics and their accepted first-node cost remain.

The runtime payload still uses the original physical storage algorithms:
original collections/backing arrays/capacity/version, callback and closure
references, tags/int blocks and unclassified ordinary graph objects. No new
game entities, collection reconstruction, native save/encode/decode, gameplay
getter/setter, inventory/transaction callback or business-method replay.
Unknown entity subclasses remain physical runtime storage, not newly certified
semantic schemas. Existing visuals/geometry/last transforms remain unchanged.

This is an actual paired-write prototype, not the completed production semantic
backend or an established speed improvement.

## Exact Pairing

StateGraph.PreparePartitionRestore binds the complete old graph, independent
runtime graph and the old graph's prepared entity plan. Check object identity
sets, bounded object/slot counts, every full StateSlot descriptor (owner, name,
offset, size, reference kind), and captured reference identities. Every old slot
must occur in exactly one partition. Missing, overlapping, extra or duplicate
slots and another checkpoint's entity plan reject preparation.

SemanticRuntimeSnapshot also binds its original oracle object, rejecting another
checkpoint even if all retained identities and topology happen to match.
Transient coverage metadata is discarded after preparing the immutable plan.
No coverage map is allocated on each restore.

Runtime storage defaults read-only. Enable writes only after full pairing has
prepared and the independent underlying access explicitly permits storage-only
writes. Every runtime block must be an approved owner-repair/storage block;
unknown blocks leave the payload read-only. Blocks retain guarded wrappers,
not permission for independent owner reconciliation. Entity fields remain
excluded from runtime writes and whole-owner entity block capture.

SemanticEntityShadow freezes the complete prepared writer-family mask.
Runtime restoration requires that exact enabled selection; disabling any
necessary family selects existing entity/legacy restoration instead.
Incomplete preparation logs RUNTIME PILOT UNAVAILABLE without publishing a
runtime plan or damaging previous entity combinations. No admission is inferred
only from configured switches or object counts.

## Write And Failure Order

Before writes: both graph lifetimes, original access identities, runtime access
and external handles, runtime write gate and every entity writer validate.
Then runtime ordinary/block scalars, all entity scalars, runtime ordinary/block
references, and all entity references. All original objects stay retained.
Existing ordinary Restore uses the same extracted scalar/reference loops
without behavioral changes.

Real storage-write errors propagate without silently retrying the old graph.
The already-existing final completed-difference owner/full repair policy is
unchanged; this does not introduce an exception retry.
Disposed runtime/entity/oracle/boundary reject before either partition writes.

## Diagnostics

Session logs semanticRuntimeRestore. RECORD summary logs
runtimeRestoreAllowed, runtimeCut=partition-write-pilot,
runtimePayload=independent-write-pilot and prepared runtimePilotSlots.
productionRuntimeCut=false still means production capture has not been cut.
The RECORD checks themselves remain read-only.

RESTORE OK logs actual runtimePilot and runtimePilotSlots, plus
runtimeScalarWrites(checked/skipped) for independent ordinary scalar writes.
Existing ordinaryScalarWrites counts only the original graph's ordinary writes,
including later old-graph reconciliation, not the independent runtime writes or
entity writes/blocks. Zero there is not evidence of no runtime work.
Keep CAPTURE captureMs/ms(...) and RESTORE ms(...) for actual Unity timing;
new coverage preparation may add first-node migration cost.

## Verification

336 offline tests pass, including13 new cases: exact complete pair, six
restores of original arrays/links, all scalars before references, incomplete
and overlapping partitions, cross-checkpoint plans, bounded metadata and valid
retry, disposed entity/runtime/oracle/boundary preflight, unknown block refusing
write permission, real error propagation without fallback, and six repeated
past/future round trips with independent values and references.
Existing diagnostic-only ordinary/block writes still reject until admitted.

Native fixture first rejects incomplete entity preparation, then admits a
complete pair. Six restores mutate original tag/int blocks and dictionary
growth/buckets, original item/member/type/shape/window collections, inventory
callbacks/caches, window callback and native captured closure.
Full old graph and independent entity/runtime comparisons agree, original
storage/callback identities return, and old ordinary scalar counters do not
advance. All15 partial switch masks (including0) reject without writes.
Disposed inventories reject before independent tag writes; original entity-only
combinations and full old restoration remain usable.
Release build zero warnings/errors,57 hooks/74 original fields and strict native
manifests pass; official scanner HardFlags empty, capabilities unchanged.

## Live Boundary

Restart game; build sampled first node and later/manual nodes. Move/rotate items
between containers/counter/single-slot holders, buy/sell, close/reopen windows.
Return to first node at least three times with backward/forward restores.
Group-drag2-4 items after restoring it, then visit another node and repeat.
Inspect membership/quantities/locks, customer/dialogue state and sprites/collision.

Only sampled node should show runtimePilot=True and positive runtimePilotSlots
(prior session484208 slots); other nodes False/0. Require diff=0,
releaseOk=True, independent MATCH/runtimeDiffSamples=0, no unavailable/failure.
Do not remove old capture/diagnostics, broaden sampling or alter visual ordering
before this first paired-runtime Unity acceptance.

## Deployment

2026-11-12 01:15, runtime commit7a35d27, version0.8.14, no push.
Backup: out/full-release-deploy-20261112-011552.
DLL SHA256: CF99A5648C9774735D1559F898DC9DF926145ECB7E533608D1FEE30B77227DF5.
78 save files verified unchanged; no settings imported, no game started.
Awaiting the first paired-runtime live test above.

## First Live Acceptance

2026-11-12 01:18:14.077 session, user reports completed.
Four captures (manual id4) and seven restores, all final diff=0,
releaseOk=True, finalFallback=False. Four id1 restores actually used
runtimePilot=True/runtimePilotSlots=483607 with all78095 Entity slots in the
five admitted exact entity types. Independent entity/runtime all MATCH,
runtimeDiffSamples=0. Other nodes False/0, as intended.

Runtime ordinary scalar checks127200 per sampled restore, skips
126990/127033/127026/127026. Original ordinary checks4/skips2 are only later
dictionary owner repair, not the initial runtime writer. Reconcile all four
dictionary fields across one owner/ten planned slots, no final fallback.
Four-item group drag at01:21:24.948-25.188 follows sampled restore at23.749;
another at29.435-29.825 follows id2 restoration. User reports completed.
No FAILED/UNAVAILABLE. Bargain flags remain default; Player.log exception
matches are native startup TLS/Curl reset, not a demonstrated snapshot error.

First capture3553 ms: access3 walk587 items79 semanticShadow136 entityAudit52
entityRecords2643 verify45 prepareHistory7; runtimeCaptureVerifyMs882 is included
in entityRecords. Other captures315/349/329 ms.
Restore totals780/615/809/678/789/769/730 ms, median769 ms.
Sampled first writes204/191/197/189 ms. Non-A/B, no attributable speedup;
the complete duplicate capture/diagnostics still dominate the first node.

Next boundary is two independently captured paired nodes in the same live
context. Keep complete old capture and all checks; add one bounded later
manual sample rather than expand to all nodes or delete diagnostics.
