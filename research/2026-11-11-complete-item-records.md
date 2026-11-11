# Complete item hierarchy and external identity groundwork

## Previous deployment acceptance

2026-11-11 23:15 session: five captures, six restores, all final diff=0 /
releaseOk=True, no final fallback. The first inventory/window record matched at
capture (174 ms) and both restorations (20/16 ms). Five capture totals were
1086/352/381/427/310 ms; six restore totals 781/635/669/627/744/634 ms.
These are not an A/B speed comparison. Read-only records add diagnostic cost.
The seven reconcile differences were cassettePlayer.modifiedState dictionary
count/version, entry[5] hashCode/next/key/value and bucket[2], repaired as three
complete owners / 45 slots. No new unrelated owner was implicated.
Post-restore four-item group dragging ended normally. Player.log tail contained
no exception; this is not a full historical Player.log audit.

## Complete item manifest

Exact GameItemElement now uses exhaustive declared-field manifests for
GameItemElement, GameItem and GameItemFunc. Four derived entity fields are added:
fakePinOverlayVisible, markHiddenWithBackground, destroyed and tempShape.
All item callbacks remain Runtime, retained/restored by the authoritative graph.
Render nodes and the six existing geometry-cache exclusions remain Visual.
Unknown subclasses remain legacy-only, not partially certified entities.

Coverage uses shared immutable schema metadata and separate per-owner multiword
bit vectors. Every nonvisual field must occur once with the correct reference
kind. Unknown, visual, duplicate and missing fields reject the diagnostic.
Index object/slot limits are checked before allocating per-owner progress.

Validation: 276 offline tests, including widths 63/64/65/128/129, late-field
duplicates and wrong kinds, owner isolation and missing coverage; Release build
zero warnings/errors; native fixtures include four derived state mutations,
legacy callback restoration without invocation, all six excluded layout caches,
and the existing repeated restore/filtered-root tests. 57 hooks / 74 fields;
official scanner HardFlags empty, capabilities unchanged.

This remains read-only, with no production graph pruning or entity writeback.

Deployment 1: commit 6f9ba14, 23:27, backup
out/full-release-deploy-20261111-232759, DLL
F3BE5D26FE10E4C397AA9885B63EC19F237CABD4C768697BF4C859810BC86E1F.
78 save files unchanged; settings not imported; game not started.

## Node-local handles and runtime boundary protocol

Each admitted entity retains its original object, exact type and complete role
schema in a node-local table. Handles include the originating table, not just
the numeric object ID. Cross-node/default/unknown/wrong-type handles reject;
disposal clears strong entity references and runtime seeds. Unknown entities
remain in the old runtime graph, never silently promoted.

Captured slots are exhaustively partitioned; both Entity and Runtime fields
must occur once with matching reference kind and no Visual field may occur.
All selected owner Runtime callbacks/cache fields remain represented.
The old roots and every semantic outbound reference form explicit runtime seeds.
A transient runtime-only adjacency replay must rediscover exactly every old
object; semantic edges are not used in that traversal. This proves the proposed
entry set covers the captured topology, not unobserved static/opaque state.
No capacity trimming or collection rebuilding is involved.

Only the entity handles, role schemas, runtime seed IDs and bounded aggregate
counts survive capture; temporary adjacency, coverage vectors and discovery
queues do not. Limits apply before owner/field metadata and unique discovery
allocation. Snapshot-owned records retain referents; the seed IDs alone are not
an independently usable runtime snapshot.

The restore diagnostic validates all retained identities before comparing the
independent entity records. Errors still only fail this diagnostic; main graph
restoration remains authoritative. Summary adds handles/rootHandles,
runtimeHandleLinks/entityHandleLinks, partition(entity/runtime/total),
runtimeSeeds/runtimeReachable and runtimeCut=false.

284 offline tests, including same-value identities with hostile Equals,
cycles/closure aliases, root entities, cross-node handles, semantic-only payload
branches, runtime full discovery, type/default/unknown/duplicate identities,
missing/visual/mistyped fields, complete-scan and metadata limits, disposal,
failed captures preserving old nodes. Native fixtures additionally check two
same-uniqueId items, shared inventory/window links, two independent node scopes
and exact full object/slot coverage. Release build zero warnings/errors;
57 hooks / 74 fields and scanner HardFlags empty.

No independent runtime graph value capture or entity writer is enabled.
Topology replay is a migration precondition, not the actual cutover.

Deployment 2: commit aacb0d8, 23:35, backup
out/full-release-deploy-20261111-233523, DLL
8F3CF650922DF39673567D6C90A66D788A0AD18C37F015A9A6BF5A7FADC73C73.
78 save files unchanged; settings not imported; game not started. Version 0.8.14,
no push. Pending live acceptance: normal trade and container/window interaction,
rotated/sold items, manual checkpoint, at least six backward/forward restores
and post-restore group drag. Keep RECORD CAPTURE/RESTORE lines and captureMs.
