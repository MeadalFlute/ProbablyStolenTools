# Validated entity plan composition

## Accepted feedback and latest live evidence

The user confirms customer sprites now restore. The reported defect was purely
visual: trading/dismissal continued to work, and a later state refresh could
reload the sprite. Do not characterize it as lost customer business state.

The 2026-11-12 01:39:25.146 session contains four captures and six successful
restores, all final diff=0/releaseOk=True, no final fallback, FAILED or UNAVAILABLE.
Primary id=1 restores twice through the independent runtime writer; manual id=4
restores once. Ordinary id=2/id=3 remain legacy. Independent records all MATCH.
The manual node was created after the customer left: hidden sprite and the
completed right-leave path are the correct captured state, not another failure.

Immediate/frame+2 logs restore the visible nodes to world (0, 1.58, 0), both
original path cursors and the intended sprite. The hidden manual node preserves
its original inactive state, right-leave elapsed=2 and parent (2.67, 1.49, 0).
This directly demonstrates that the parent position matters: the old visual
snapshot restored parent activation but not its pose. No isolated old/new
reproduction identifies which missing field caused every historical report.

Capture durations: primary 4066 ms (entityRecords 2800, including runtime
capture/verification 1139), ordinary 322/339 ms, manual probe 2511 ms
(entityRecords 2139 including runtime 692). Restore median 717 ms, range
602-756 ms. These are not A/B measurements. No group-drag events in this session;
only one manual-node restoration, not three complete two-node round trips.
Player.log shows startup trust-list TLS/Curl network errors, not snapshot errors.

## Next bounded change

Reduce repeated preparation work while keeping the same sampled nodes,
original values, every prebuilt switch combination and all restore safeguards.

Previously RegisterWriters checked complete old-graph topology once per
combination: 1 + 2 + 4 + 8 = 15 scans with all four writer families.
The original per-group validation still checks all declared Entity/Legacy
fields/kinds, actual captured owner coverage, block opt-in/full-block coverage,
owner identities and writer validity.

Each successful group now retains its validated EntityRestorePlan with frozen
owner/selected-field metadata and the complete metadata slot count, including
legacy fields. There are four full checks, one per nonempty group (the two
inventory types remain an atomic group).

ComposeEntityRestores only accepts plans bound to the same live checkpoint,
rejects duplicate/overlapping owners, sums complete original owner/slot budgets,
preserves input writer order and validates all writers again. Disjoint immutable
owner proofs can be unioned without rereading the complete captured topology.
Empty/null/foreign plans reject. Compositions themselves can be composed.

RegisterWriters still prepares every combination in a temporary dictionary before
publishing anything. The group order is the same sorted existing-bit order plus
the newly admitted group, including out-of-order enable calls. A failed group or
composition cannot dispose/replace previously registered groups and combinations.
All fifteen masks remain available, rather than deferring checks until restore.

There is no change to actual write code, all-participant preflight, scalar-before-
reference phases, independent runtime exact full union check, old complete graph,
owner/full repair, final verification, semantic oracles, visual ordering, path
repair or history lifecycle. No native save/load or collection reconstruction.
Production capture is still the complete old graph; this is preparation
deduplication within migration, not completion of the semantic backend.

## Diagnostics

RECORD summaries append:

```text
writerPlans(coveragePasses=4,compositions=11,prepared=15,
            coverageMs=...,composeMs=...,runtimePlanMs=...)
```

Coverage and composition counts are completed preparation attempts, not
necessarily published plans if a later step fails; prepared is the published
combination count. Timing includes failed attempts, accumulated in stopwatch
ticks, displayed as integer milliseconds. All three timers are subsets of the
existing entityRecords duration, never extra time to add to it. Capture of the
independent records/writer values is still additional work outside these timers.

## Verification

- Release Mono build: zero warnings/errors.
- 355 offline tests. Eight new cases compare all fifteen masks with fresh full
  validation, full budgets including legacy metadata, duplicates/overlap/foreign
  plans, immutable metadata despite changed writer descriptions, no captured
  block reference rereads, candidate-failure isolation, per-restore preflight,
  scalar/reference order, explicit/recursive ordering and six past/future cycles
  with disposed-node isolation.
- Formal native fixture asserts four coverage passes, eleven compositions and
  fifteen plans. Each native mask matches fresh original full validation's
  complete slot budget and writer ordering. All original thirty mask restores,
  independent runtime writes, two-node six-round-trip tests and failure/atomic
  registration cases still pass.
- 57 hooks, 76 snapshot/private fields and original path contracts pass.
  Official scanner HardFlags empty; no capability changes.

Optional native benchmark:

```powershell
dotnet run -c Release --project tests/ReleaseCompatibility/ReleaseCompatibility.csproj -- `
  "<game directory>" src/ProbablyStolenTools/bin/Release/Mono/ProbablyStolenTools.dll `
  release/manifest.xml --benchmark-entity-plans
```

Original game writers plus 32768 runtime-only native TagState objects yield
328086 captured slots. Compare construction of the same fifteen plans using
full revalidation versus four full validations plus eleven compositions.
Two warmups, seven samples, alternating order and GC before timed measurements:

| Run | Full revalidation median | Validated composition median | Allocation full / composed |
| --- | --- | --- | --- |
| 1 | 60.634 ms | 15.548 ms | 432704 / 76872 bytes |
| 2 | 58.381 ms | 15.936 ms | 432704 / 76872 bytes |

This is isolated .NET 6 preparation of native game data, not Unity Mono gameplay
or total capture speed. It does not explain/remove the whole 2-4 second diagnostic
capture cost. New in-game timing is needed before attributing a real benefit.

## Live boundary

Rebuild the first sampled checkpoint and a later manual checkpoint, preferably
with different item quantities/membership and customers. Alternate them at least
three times; include an ordinary node and a post-restore group drag. Keep the
visible/hidden customer expectations and check that the visual repair holds.
Expected successful full groups: coveragePasses=4/compositions=11/prepared=15,
both sampled nodes runtimePilot=True and independent MATCH, every final diff=0.
Keep all old capture/verification/repair and diagnostic payloads. Do not replace
production capture or widen sampling based only on the isolated benchmark.

## Deployment

Runtime commit `774b719`, deployed 2026-11-12 01:48:17, version 0.8.14, not pushed.
Backup: `out/full-release-deploy-20261112-014817`.
DLL SHA256: `72C2BBE11825647BA6C12B10592481CF33DAEC1CF5A0395CBC5BD9C21B6C4E29`.
Game was closed; 78 existing save-area files verified unchanged.
No settings imported, no game started. Awaiting in-game timing and acceptance.
