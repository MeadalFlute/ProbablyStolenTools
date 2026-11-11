# Restricted shape entity writer pilot

## Previous live acceptance

2026-11-11 23:52 session, a86dd23 / 0.8.14:

- Six captures including manual id=4, eight restores; all final diff=0 /
  releaseOk=True, no final fallback. Three independent entity/runtime restore
  matches, runtimeDiffSamples=0, runtime verification 33/33/36 ms.
- 4306 bound handles and 101015 runtime objects; 78095 entity owner slots plus
  485137 runtime slots match the full 563232 old slots. Combined sampled records
  use 583694 logical slots / 4669552 bytes, well within configured limits.
- First whole capture 2241 ms; entityRecords 1264 ms, of which independent runtime
  capture/coverage/verification 893 ms. Later captures 309/418/319/284/313 ms.
  This confirms significant added diagnostic cost, not a capture optimization.
- Restore totals 705/712/593/701/652/666/591/635 ms, median 659 ms, not A/B.
  Seven cassettePlayer tag dictionary/storage differences were repaired as
  three owners / 45 slots. Post-restore four-item group drag completed.
- No FAILED lines; last 100 Player.log lines had no Exception/Error. All
  bargain flags remained default. Rendering correctness cannot be established
  solely from diff=0; player inspection is still necessary.

## Scope

This starts roadmap step 4, one exact entity type only: GridShapeBuilder.
It does not enable runtime-payload restoration or replace production capture.
The original full graph, including every shape field, remains the full oracle
and retains its unchanged ordinary Restore and reconciliation paths.

Snapshot.SemanticShapeRestore defaults true, separately from read-only records.
It prepares a writer only for the one sampled entity checkpoint per context.
Other checkpoints use the unchanged old first write. Disable the setting to
use original first writes everywhere. It is unavailable when records are off.
Capture-time unavailable/schema/coverage/budget errors log SHAPE PILOT UNAVAILABLE
and leave the old checkpoint and read-only diagnostics intact. Write errors do
not silently retry the old path; they follow the existing failed-restore flow.

SemanticShapeWriter independently captures every exact shape's seven scalars
and original private data array reference, plus retained owner/data identities.
All eight declared fields and types must match the native manifest. Derived
shape types are never admitted. There is no rebuilding, rotation/shape method,
inventory/transaction callback or replacement array allocation.

The private data reference and auto-property backing fields use FieldInfo;
the known native scalar auto-getters read the validated schema. outside uses
its real public byte field. Exact unchanged values need no setter/field write.
All byte-array contents, aliasing and capacity remain the old graph's job.
Shape record slots/bytes consume the remainder of the combined diagnostic
budget, not an additional full allowance.

StateGraph.PrepareEntityRestore freezes the owner set and checks exact complete
captured owner fields, reference kinds and uniqueness, plus metadata budgets.
Only backends explicitly permitting real owner storage writes are accepted;
unknown replacement blocks reject. Plans are bound to their original graph.
Writer identity validation occurs before any graph storage write.

First write order is all ordinary nonshape scalars, nonshape block scalars,
shape scalars, ordinary nonshape references, nonshape block references, shape
data references. All scalar writes still precede every reference write.
The full old graph retains those shape slots but skips their first write only.
Original refresh, complete reconciliation scan/owner or full second write,
visuals/geometry/final transforms and complete final verification stay unchanged.

SemanticEntitySnapshot and SemanticRuntimeSnapshot remain read-only. This
separate shape writer is not permission to restore items/inventories/windows,
rebuild collections or drop old capture. RuntimeAccess still rejects every
ordinary and block write.

## Logs

Session adds semanticShapeRestore. RESTORE OK adds shapePilotOwners: positive
means the shape writer actually participated, zero means ordinary first write.
RECORD summaries include prepared shapePilotOwners/Slots and pilotCombinedSlots/
Bytes; these describe capture eligibility, not proof of later use. Existing
combinedSlots/Bytes still describe the independent entity/runtime pair alone.
recordRestoreAllowed=false / runtimeRestoreAllowed=false distinguish read-only
record verification from the separate restricted writer.

## Verification

302 offline tests. Eight writer protocol tests cover six repeated restores,
all scalars before references, validation before all writes, error propagation
without hidden retry, missing/extra/duplicate/wrong-kind fields, owners/budgets/
filtered roots, cross-node plans, and a deliberately incorrect writer exposed
by the unchanged full oracle.

Native fixture performs six actual entity-write restores, mutating all eight
shape fields including orientation/flipped/outside/dimensions and replacing the
data array while mutating original byte content. Old graph, independent records
and runtime payload all match afterward, preserving shared array identity.
Disposed writer validation prevents any ordinary old graph write, and normal
full Restore remains available. Limits reject without invalidating old nodes.
Release build zero warnings/errors, 57 hooks / 74 fields, HardFlags empty.

No isolated or Unity performance gain is claimed. Only first sampled node uses
the writer, and its independent diagnostics remain expensive.

## Next live boundary

Rewind to the first sampled node at least three times after rotating/flipping,
moving and selling items; also visit a later/manual node. Check both rendering
and collision alignment, original container placement, group drag after restore.
Need positive shapePilotOwners for sampled restores, zero for ordinary nodes,
final diff=0 and RECORD RESTORE MATCH / runtimeDiffSamples=0.

Stop at this first actual entity-write Unity boundary before enabling another
type or paired runtime restoration. Preserve logs and compare write/total times
without treating this non-A/B run as proof of speed.

## Deployment

2026-11-12 00:03, runtime a3484f0, version 0.8.14, no push.
Backup: out/full-release-deploy-20261112-000339.
DLL SHA256: 72FC7D7F15DD1C049DF4C0E631C315A351697A95A7B4A6C26E929B6AD3BF8DA5.
78 save files verified unchanged, SettingsImported=False, GameStarted=False.
Pending the first actual shape entity-write Unity acceptance described above.
