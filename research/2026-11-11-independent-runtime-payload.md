# Independent runtime partition payload

## Previous live acceptance

2026-11-11 23:39 session, runtime aacb0d8, version 0.8.14:

- Five captures, eight restores, all final diff=0 / releaseOk=True, no final
  fallback. Two complete entity record restore matches, each 20 ms.
- 4306 admitted entities, 100969 old objects, 78095 entity slots / 484618 runtime
  slots, total 562713. 17170 runtime seeds rediscovered every old object.
  2782 runtime-to-entity links and 4223 entity-to-entity links.
- First record capture/verification 228 ms; first whole capture 1308 ms,
  later captures 292/466/313/294 ms. Restore totals
  638/665/670/615/852/648/861/644 ms; median 656.5 ms, not A/B.
- Four cassettePlayer.modifiedState dictionary fields repaired each restore,
  one full owner / ten slots. Four-item group drag ended normally after restore.
- No manual checkpoint is present in this session; all bargain flags default.
  No FAILED lines in session; Player.log last 100 lines had no Exception/Error.
  Logs do not independently certify rotation, sold-item rendering, nondefault
  bargains or full fallback.

## Implementation

The shadow now captures a separate StateGraph payload for Runtime slots only.
This is real independent value/reference storage, not just an adjacency count.
It is still read-only diagnostics, never used by production Restore.

IIndependentStateAccess creates a fresh access: retained objects, storage paths,
value tokens, descriptions and array caches are independent. Explicit roots and
their selected field sets are copied, not widened by the new runtime seeds.
Known exact-tag block factories are recreated per access through a configuration
callback; arbitrary BlockCapture delegates and their state are not borrowed.
Int array blocks retain the existing configuration and full array semantics.
Only immutable field-reader metadata remains shared as before.

RuntimeAccess retains original entities under scoped boundary handles. Entity
owners describe only their explicit Runtime fields, preserving original storage
offsets; callbacks/cache fields remain. Nonentities keep the complete old field
surface, including dictionary buckets/entries/capacity/version/views/enumerators,
closure fields, primitive arrays, shape bytes and all opaque old boundaries.
Full-owner blocks are disallowed for entity owners, so a block cannot bypass
field exclusion. Nonentity blocks remain complete.

Seeds are old explicit roots and all semantic outbound references. Discovery
outside the oracle rejects. Exact captured owner/StateSlot/reference sets must
match the oracle Runtime partition, with no duplicate, missing or extra slot.
Every original object must be retained, including entities with no runtime
fields. Runtime logical bytes must match the expected full partition. Fresh
payload and old oracle both undergo complete difference scans before acceptance;
these detect schema/topology changes and value changes during capture.

The temporary exact coverage hash set is bounded by the captured slot budget
and released after capture. The access releases its oracle-membership callback
after capture: the independent runtime payload does not retain the old graph.
The old graph can be disposed while runtime values/identities remain valid.
Boundary and entity records remain independently owned by the sampling node.

Runtime slot/byte budgets use the remainder after independent entity records,
not a second full allowance. Objects refer to the same unique oracle identities
and remain bounded by MaxObjects. Sequence samples overlap physical array/list
storage intentionally; no collection rebuilding or capacity trimming occurs.
Consequently combined logical slots may exceed the old graph's logical slots.
Logical budgets are not a guarantee of real managed heap consumption.

Both ordinary access writes and block restore methods explicitly throw. The
class has no Restore API. Old production capture, Census, first restore,
owner/full reconciliation, final full validation, visuals and geometry ordering
are unchanged. Sampling remains once per context through SemanticEntityRecords;
failures dispose the new payload and only fail the diagnostic, preserving old
checkpoints and the primary capture.

## Logging and cost

RECORD CAPTURE MATCH requires the new runtime payload's coverage and difference
checks in addition to the existing checks. RECORD RESTORE MATCH requires both
entity differences=0 and runtimeDiffSamples=0. The runtime sample count is bounded
diagnostic output, not the full difference count; the scan still visits all slots.

Summary adds runtimePayload=independent-readonly, runtimeObjects/Slots/Bytes/
Blocks/BoundHandles, runtimeCaptureVerifyMs and combinedSlots/Bytes.
runtimeCut=readonly-shadow and productionRuntimeCut=false distinguish diagnostic
field exclusion from production migration. entityVerifyMs/runtimeVerifyMs split
the restore diagnostic cost. Existing CAPTURE entityRecords includes all this
extra first-node work. It can increase the first checkpoint pause and memory.

Do not call this a speed improvement. The previous session suggests only 13.9%
of old slots belong to admitted entity fields; runtime object count stays equal
to the old graph. Further speed depends on actual typed entity payloads and
remaining runtime/collection traversal, not merely fewer roots.

## Verification and next boundary

294 offline tests, including independent old-graph disposal, scoped handles,
past/future values and independent disposal, selected roots, signed-zero/NaN/
nullable arrays, dictionary storage/views/enumerator version, six restoration
cycles, ordinary/block write rejection, changed values/topology and exact
object/slot/byte budgets. Existing array preallocation, full scan and exception
tests remain active.

Native fixtures check real exact tag block capture/differences using a fresh
factory, complete remaining inventory/window callbacks/cache, shape byte storage,
same-uniqueId objects and closures, six old-graph restore cycles, cross-node
handles, callback noninvocation, and both records/runtime valid after oracle
disposal. Release build zero warnings/errors; 57 hooks / 74 fields; official
scanner HardFlags empty, capability declarations unchanged.

Next live run: record first checkpoint runtimeCaptureVerifyMs and combined
budgets; ordinary trades/containers, manual checkpoint, six or more backward/
forward restores, post-restore group drag and rotated/sold-item visuals.
Need RECORD CAPTURE/RESTORE MATCH with runtimeDiffSamples=0.

Next development: only after live independent payload acceptance, introduce
restricted typed in-place entity writes and paired runtime restore behind a
separate switch, with original full graph oracle retained. Then measure before
production capture replacement. No complete semantic backend or runtime restore
is claimed in this stage.

## Deployment

2026-11-11 23:49, runtime commit a86dd23, version 0.8.14, no push.
Backup: out/full-release-deploy-20261111-234913.
DLL SHA256: 516BC5CC3EA23883F9CAF3A5854A1597C71308B74440697337CEC2EFA695439C.
78 save files verified unchanged; SettingsImported=False; GameStarted=False.
Pending first live independent runtime payload acceptance, not further
production cutover in this deployment.
