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
