# Single-Pass Exact Entity Candidate

## Accepted Previous Live Session

2026-11-12 01:50:26.494 session, runtime `774b719`:

- 3 captures: primary waterStockpiler, ordinary clinicNurse, later manual probe.
- 8 restores, all final `diff=0`, `releaseOk=True`, no final full fallback.
- 7 sampled restores used the independent Entity/Runtime writer and matched both payloads.
- Three complete primary/manual roundtrips, ordinary restore, and four-item group drag after the final primary restore.
- Captured/restored/frame+2 customer visual states agree. The manual checkpoint was intentionally after departure, so its hidden renderer is correct.
- No snapshot FAILED/UNAVAILABLE. Player.log has native startup TLS/Curl messages, not a snapshot exception.
- Player.log also has an InventoryScene frame-273 watchdog stall (5.1 s warning, resumed after 7.7 s), with scene/window breadcrumbs around 01:50:35, before the first customer capture (approximately 01:50:54-57). Other startup messages include a third-party TryEquipItem hook mismatch and invalid/duplicate sprite entries. Do not report this session as stall-free or attribute that loading stall to the customer capture without evidence.
- Primary capture 3129 ms, manual 2103 ms, ordinary 299 ms; restores 569-799 ms, median 710.5 ms.
- `entityRecords` 2157/1805 ms includes runtime capture/verification 1016/820 ms and runtime plan preparation 409/378 ms. These times overlap, not additive.
- Previous session primary/manual 4066/2511 ms, restore median 717 ms. This is not controlled A/B; do not attribute the whole change to plan composition.

## Candidate Design

The previous independent pilot proved exact entity fields and remaining-runtime writes, but still built a complete old graph plus separate payloads and 15 writer combinations. It was not production capture replacement.

The candidate stores each admitted entity once during the existing graph walk:

- Exact `GameItemElement`, `GameGridInventory`, `GameSlotInventory`, `PixelWindow`, `GridShapeBuilder`.
- Reuses exhaustive Entity/Runtime/Visual manifests, checks exact complete descriptors before capture, never applies a base manifest to subclasses.
- One owner block retains all its nonvisual fields, including Runtime callbacks/caches/tag-map references. The Entity/Runtime role boundary is preserved and counted; it is not permission to drop Runtime state.
- Remaining owners, collection backing storage, closures, delegates and unknown types keep the existing graph rules. Tag/int payloads compose unchanged.
- Captured references still participate in the same identity discovery and budgets. Logical slot/byte counts, original object identities and full collection capacities do not change.
- Existing complete capture verification, preflight, census, all-scalars-before-references order, full difference scans, owner repair and final full fallback remain.
- Blocks use existing bounded field reader/comparison caches with reflection fallback, not generated writers or gameplay setters. Float/double comparisons preserve bits; references use identity.
- No game save/encode/decode, replacement entity/collection reconstruction, background-thread reads or frame splitting.

This is a production-oriented hybrid semantic payload. It does NOT claim all game types are semantic, collections are reconstructed semantically, or Runtime becomes a smaller pruned object graph. Exact Entity and Runtime fields share an owner block to avoid the extra discovery/preparation pass.

## Offline Foundation

371 tests. New cases include inherited fields, cycles, callbacks, list/dictionary physical growth, integer arrays, same-text identity, signed zero/NaN/full-width enum, selected roots, unknown subclasses, malformed descriptors, frozen layouts, complete difference counts, exact budgets, owner/full repair, six two-node roundtrips, readonly instance storage, disabled cache/typed comparison parity, census and independent oracle failure/lifetime.

Installed-game fixture checks all five schemas / six owners and every nonvisual field, callbacks not invoked, six complete mutation restores, separate composed Tag/int factories, visual cache exclusions and six two-node roundtrips against an independent ordinary-field graph. Official scanner HardFlags remains empty, 57 hooks / 76 fields.

Optional benchmark:

```powershell
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- `
  "E:\SteamLibrary\steamapps\common\Probably Stolen" `
  src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll release/manifest.xml `
  --benchmark-entity-blocks
```

7000 installed-type entity owners, seven warm samples per mode, alternating order, .NET 6 NOT Unity:

| Run | Capture old/new ms | Verify old/new ms | Restore old/new ms | Capture allocated old/new bytes |
| --- | --- | --- | --- | --- |
| 1 | 30.08 / 28.07 | 7.26 / 2.83 | 16.80 / 2.94 | 30729416 / 12259480 |
| 2 | 35.72 / 31.11 | 6.75 / 2.57 | 15.43 / 3.32 | 30729216 / 12282808 |

Warm verify allocation 88 bytes and restore 0 in both modes. Initial reflection-only prototype regressed verification/restoration and was revised before integration. These fixtures are mostly default fields and do not establish game frame latency, whole-restore speedup, dense-change speedup or rendering correctness.

Foundation commit is inactive in CustomerCheckpoint; subsequent integration/deployment is a separate reversible phase.

Foundation `fdecc9e` was deployed at 02:06:56, backup `out/full-release-deploy-20261112-020656`, DLL SHA256 `2FA2AC68BEFBA1F6AF4CEE540D176A8F54D0E05985CA6CE5C19EBCE48C68BA60`. Game closed, 78 save-area files unchanged, no settings import/game start.

## Integrated Candidate

`Snapshot.SemanticEntityBlocks=true` is the new capture backend, on all nodes, not just sampled nodes. No independent Entity/Runtime duplicate payload or 15 writer combinations are created in this mode. The existing complete StateGraph facade holds the owner blocks and remaining ordinary/Tag/int storage and performs every first write and repair.

`Snapshot.SemanticDevelopmentChecks=false` is the RELEASE DEFAULT. It gates ALL extra tag shadows, entity boundary audits, entity/runtime duplicate records and writer-pilot preparation. Old persisted individual diagnostic flags cannot override this master gate.

For candidate acceptance only, local settings enable `SemanticDevelopmentChecks=true`:

- Entity-block mode samples the primary checkpoint and first later manual checkpoint (unless SemanticManualSample=false), maximum two attempts per context using the existing sampler.
- Each sample builds a separate ordinary-entity graph from the same explicit roots, with separate access/tokens/retention. Existing Tag/int formats are retained. Constructor checks exact objects, logical budgets and every owner/slot/reference edge against the candidate, not just counts.
- Sample restore validates independent retention before any write, then the ACTUAL candidate writes. After normal full verification/repair, the independent old graph checks the final state.
- Old oracle NEVER writes or hides a candidate mismatch by repairing it. Capture mismatch rejects the new checkpoint and preserves older checkpoints; restore mismatch follows the existing post-write failure/reload path.
- Ordinary nodes have no duplicate oracle but still perform mandatory complete validation/repair.
- Oracle lifetime is per checkpoint, released on eviction/context clear. It is not enabled for census.
- With entity blocks disabled AND development checks enabled, previous shadows/audits/pilots remain available for diagnosis and rollback. This path is not mixed with production owner blocks, which would violate partial-block writer ownership.

`CAPTURE` reports backend `+exact-entity`, `entityBlocks`, `entityBlockSlots` (Entity + Runtime owner fields), `entityRuntimeSlots`, `entitySchemaFallbacks`, developmentChecks and blockOracleMs. Tag block counts now exclude BOTH int blocks and entity blocks. Block comparison work is not included in the ordinary accessor comparison counters.

Sample lines:

```text
[SemanticEntities] SINGLEPASS CAPTURE MATCH ... oldPayload=development-only actualWriter=single-pass fullCoverage=true
[SemanticEntities] SINGLEPASS RESTORE MATCH ... actualWriter=single-pass oracle=independent-legacy diff=0
```

`RESTORE OK` reports actual retained entity block counts and whether blockOracle exists. Old pilot counts are expected to be ZERO / runtimePilot=False in this mode; that does NOT mean entity restoration was disabled.

373 regression tests pass. Native fixtures additionally cover selected-root preservation, complete-owner repair and late full fallback with an independent oracle. Offline integration does not run CustomerCheckpoint in a live Unity scene.

## Acceptance / Release Gate

Do not push until live acceptance; no claim that the new capture path has already passed Unity.

After rest, one diagnostic run:

1. Create the ordinary first checkpoint, change items/customer state, create one later manual checkpoint, and allow at least one ordinary later checkpoint.
2. Move/rotate/stack/split items across grid, counter and slot inventories, buy/sell, close/reopen container windows. Alternate first/manual at least three complete roundtrips; also restore the ordinary node.
3. Group-drag 2-4 items after restores; check visible position against collision/hover position and retained members/counts/locks.
4. Include customer departure and restore, idle for a moment, trade/dismiss/call the next customer. A manual node captured after departure should remain hidden, not force a customer to appear.
5. If practical, exercise nondefault bargaining and a closed-shop manual node. Those old coverage gaps have not magically been tested offline.

Inspect the NEW Session, not earlier MATCH lines. Require entityBlocks>0 on primary/manual/ordinary captures and restores, primary/manual SINGLEPASS matches, all final diff=0/releaseOk=True, no FAILED or unexpected schema fallback, and visually correct results. Preserve captureMs / RESTORE ms partitions. blockOracleMs is INCLUDED in entityRecords time, not additive.

Then close the game, set only `Snapshot.SemanticDevelopmentChecks=False`, restart, and do a short production-mode first/manual/ordinary rewind plus post-rewind group drag/customer check. Expect entityBlocks>0, blockOracle=False, no SINGLEPASS oracle lines and no duplicate shadow/audit/record preparation. Compare first-node cost separately from normal-node cost, without making an uncontrolled percentage claim.

No source-code removal or rebuild is needed to withdraw the extra checks: the SAME candidate DLL already defaults them off. Keep bounded timing/failure/visual diagnostics and all mandatory safety checks.

Once both runs are accepted: review final logs; increment manifest/Plugin patch version only immediately before an authorized push; build, run both test projects, deploy versioned artifact and perform the ordinary release smoke test. No push has been authorized here.

Rollback: close game; set SemanticEntityBlocks=False and SemanticDevelopmentChecks=False, restart. New nodes use the previous ordinary-entity/Tag/int backend. DevelopmentChecks=True additionally re-enables earlier isolated writer pilots, so it is not the clean rollback setting. Backed-up DLLs are also available. Existing in-memory nodes cannot survive a game restart by design.

## Remaining Boundary

Core exact-entity capture and writes are now integrated and offline-verified. Live Unity geometry/customer/interaction acceptance is the next required boundary. Unknown exact types and third-party opaque state retain their old limitations; physical collection storage is deliberately preserved. There is no authorization to reconstruct collections, remove full verification, add background reads, split a mutable graph across frames, claim complete game-wide semantic coverage or publish while the user rests.

## Deployment Record

- Runtime integration commit `1c81be2`, foundation `fdecc9e`; version remains 0.8.14, no push.
- Final build: zero warnings/errors; 373 regressions; 57 hooks / 76 fields; native fixtures pass; official scanner HardFlags/Error empty.
- Deployed 2026-11-12 02:13:12 while the game was closed; game not started.
- Backup `out/full-release-deploy-20261112-021312`; DLL SHA256 `8A72D8428C72277E3048651237BFD23D70A81465E12FFB9FC153E4A58821F466`.
- Deployment itself preserved all 78 save-area files and did not import old settings.
- Afterwards only the local Mod configuration gained two True entries: Snapshot.SemanticEntityBlocks and Snapshot.SemanticDevelopmentChecks. Parsed XML verified both unique entries; all other 77 files match deployment backup hashes. The old configuration is in that backup. No native game save was edited.
- Current local build is in diagnostic candidate mode; the compiled release default for development checks remains FALSE.
- No new live session has been generated for this candidate. The 01:50 live acceptance belongs to the PREVIOUS runtime only.
