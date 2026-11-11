# Restricted window entity writer

## Scope

Continues roadmap step 4 after the accepted 00:05 shape pilot. Only the sampled
checkpoint per context is eligible. Snapshot.SemanticWindowRestore defaults true,
independent of the shape switch, but both still require SemanticEntityRecords.
Development-time duplicate diagnostics remain enabled by explicit user request.

SemanticWindowWriter admits only GetType()==typeof(PixelWindow), with the complete
existing native Entity/Runtime/Visual manifest. Independently captures all Entity
values and original reference identities, within the combined remaining
entity-record/runtime/shape/window slot and byte budgets.

Writes real fields through FieldInfo, not properties or gameplay methods.
Float comparisons preserve signed zero and NaN payloads. Original parentItems,
linkedContainerStorage and childElement links return to their retained originals;
all list contents, versions, capacities and backing arrays remain the old graph's
job. No reconstruction, window methods, callbacks or replacement objects.

Runtime fields (headerButtons, callback delegates, focusStamp) still use the old
writer. Visual fields remain outside the business graph. Derived types are not
admitted. Preparation/schema/budget failures log WINDOW PILOT UNAVAILABLE and
leave the full old checkpoint and shape plan available. Actual write exceptions
propagate through existing restore failure handling, without hidden retry.

## Complete Partition Protocol

IStateEntityPartitionWriter declares LegacyFields separately from the entity
writer's Fields. Their disjoint union must exactly match every captured slot of
every owner, including reference kind and uniqueness. Missing, extra, duplicate
and filtered surfaces reject preparation. Whole-owner shape writers still need
complete original coverage.

PrepareEntityRestores groups disjoint writers under combined metadata budgets.
The plan freezes the owner/selected-field index and writer array and belongs to
one checkpoint. Affected blocks require explicit owner-repair permission and
complete replacement; partial block replacement is rejected.

Before any old or entity business write, validate every writer. Restore order:
ordinary unselected scalars, unselected block scalars, all entity scalars,
ordinary unselected references, unselected block references, all entity references.
This keeps runtime fields on the old path without double-writing entity fields,
and preserves scalar-before-reference ordering across all writers.

Full legacy capture/Differences, refresh, complete reconciliation and owner/full
fallback, visuals/geometry/final transforms and final verification remain intact.
Independent runtime payload remains strictly read-only. No production capture
replacement or performance improvement is claimed.

## Verification

312 offline tests, ten new partition/group regressions: exact exhaustive split,
legacy scalar and reference retention, combined budgets/disjoint owners,
all scalars before references across writers, validation before any write,
frozen metadata/cross-node rejection, errors without retry, incorrect writer
visible in the full oracle, unknown/partial blocks rejected.

Native fixture retains earlier six actual shape restores and adds six cycles:
two combined, two window-only, two shape-only. Mutates every window business
field, original lists, shared aliases, signed-zero transparency, enum/string
values, runtime callbacks and focus stamp. Old graph plus independent entity
and runtime comparisons match after every restore. Throwing callbacks are never
invoked. Disabled writers perform no writes; a disposed window rejects the
combined plan before any shape or ordinary business field changes.
Window slot limits reject without invalidating the retained checkpoint.

Release build zero warnings/errors. 57 hooks / 74 original private fields plus
native manifests pass. Official scanner HardFlags empty, capabilities unchanged.

## Live Boundary

Restart game with the deployed build. Before the first sampled node, leave a
container window open. After capture, move/resize/close/reopen container windows;
rotate/flip/move/sell items and return to the sampled node at least three times.
Also visit later/manual checkpoints and group-drag after a restore.

Check window visibility/position/size, container contents and item sprite/collision
alignment. Sampled RESTORE OK should report both windowPilotOwners>0 and
shapePilotOwners>0; ordinary nodes report zero. Require final diff=0,
releaseOk=True and RECORD RESTORE MATCH / runtimeDiffSamples=0, with no pilot
unavailable or restore errors. Rendering still needs human inspection.

Stop here before item/inventory entity writes or runtime-payload restoration.

## Deployment

2026-11-12 00:22, runtime commit 69f20cd, version 0.8.14, no push.
Backup: out/full-release-deploy-20261112-002237.
DLL SHA256: 02BE83621A1A89AB0CF9B858C2C7BEF35216D38BE841ECD85FB4C860A3171C5D.
78 save files verified unchanged; settings not imported, game not started.
Awaiting the first live window writer validation above.
