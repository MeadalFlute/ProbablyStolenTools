# Single-Pass Exact Entity Candidate

## Accepted Previous Live Session

2026-11-12 01:50:26.494 session, runtime `774b719`:

- 3 captures: primary waterStockpiler, ordinary clinicNurse, later manual probe.
- 8 restores, all final `diff=0`, `releaseOk=True`, no final full fallback.
- 7 sampled restores used the independent Entity/Runtime writer and matched both payloads.
- Three complete primary/manual roundtrips, ordinary restore, and four-item group drag after the final primary restore.
- Captured/restored/frame+2 customer visual states agree. The manual checkpoint was intentionally after departure, so its hidden renderer is correct.
- No snapshot FAILED/UNAVAILABLE. Player.log has native startup TLS/Curl messages, not a snapshot exception.
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
