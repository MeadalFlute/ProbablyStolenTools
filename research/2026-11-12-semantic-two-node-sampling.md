# Two Independent Paired Checkpoints

## Scope

Follows the accepted01:18:14.077 paired-runtime session: four captures/seven
restores all final zero differences, four actual483607-slot runtime restores,
all independent MATCH, and post-restore group drag completed.

The next risk is independent values and identities across two semantic payloads
coexisting in Unity. Add Snapshot.SemanticManualSample=true: retain the first
entity/runtime attempt per context, plus one additional attempt on the first
later manual checkpoint. If the first sample is manual, the second manual
checkpoint can be the additional probe. Ordinary later customer/closed nodes
do not consume the manual probe and remain legacy.

SemanticCheckpointSampling consumes attempts, not successful/retained nodes.
Failure, replacement, eviction or branch truncation does not free a retry.
At most two attempts per existing context reset/file reload boundary.
Disabling the new switch restores the original single-attempt limit.
Disabling SemanticEntityRecords means no sampling attempts or new payloads.
Toggling the extra-sample flag never destroys existing payloads.

Each sample independently captures records, scoped handles, runtime values,
writer values/references and all prepared combinations, with the unchanged
per-node combined budgets and failure isolation. No shared saved-value cache,
identity merge, uniqueId lookup or copying values from the primary node.
Capture logs add sample=primary/manual-probe; Session logs the switch.
The extra manual node has real repeated diagnostic cost and memory retention.
This is accepted development overhead, not capture optimization.

No change to underlying captures, pair validation/write protocol, all-writer
preflight, final checks, owner/full repair, visual ordering, collection physical
storage, existing drag gates or history lifecycle. No migration to all nodes,
no removal of old graph or diagnostics, no production capture replacement.

## Verification

341 offline tests: five additional sampling-policy cases cover initial/manual
admission, first node itself manual, disabled extra probe, attempt consumption
without success/retention notification and repeated context reset.
Existing past/future paired-runtime isolation tests rerun.

Native fixture adds a second complete actual-game paired checkpoint with
different item count/parent, slot currentItem, grid member order, tag value,
dictionary size/capacity and shape position. Both independent payloads coexist;
six round trips each agree with their own complete old graph and independent
entity/runtime oracle. Another node's restore argument rejects before writes.
Disposing the future payload/graph leaves the past fully usable.
All previous native single-node, masks, failure/preflight tests still pass.
Release build zero warnings/errors;57 hooks/74 original fields, strict manifests
and official scanner HardFlags empty, capabilities unchanged.

## Live Boundary

Restart game. Let the first normal checkpoint form, then move/rotate items,
alter member order, buy/sell or change quantities where available, and progress
to a later customer. Press F9 once to form the additional manual sample.
This manual capture may pause noticeably because it builds the same diagnostic
payloads and plans as the primary sample.

Alternate first and manual sampled nodes at least three round trips. On each
node verify its own item positions/membership/quantities and customer state;
group-drag after returning to each. Also restore an ordinary intermediate node.
Optionally create another manual node: it should remain legacy, proving the
attempt cap, without requiring another expensive semantic capture.

Expected: primary/manual-probe RECORD CAPTURE MATCH once each; both selected
nodes have runtimePilot=True and positive slots on restore, ordinary nodes
False/0; all final diff=0/releaseOk=True and independent MATCH.
User rendering/interaction feedback remains necessary.
Stop before production capture replacement or removing migration diagnostics.
