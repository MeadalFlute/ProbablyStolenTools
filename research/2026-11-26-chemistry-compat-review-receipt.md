# 2026-11-26 Chemistry Compatibility Review Receipt

Review: `2026-11-26-chemistry-compat-review-ds.md`. This receipt covers the dirty workspace; no release, version bump or push is implied.

## Disposition

- DS's 70% extrusion threshold and associated source/test/documentation edits are preserved.
- P2-1: addressed by deferring automatic captures BEFORE consuming customer, morning, closed or return boundaries while a supported signboard build is uninitialized/busy/dragging/faulted or has a different owner/run. The same readiness predicate gives manual capture a specific bilingual retry message. Capture wait reasons log once per distinct reason. Capture resumes when readiness recovers. Unsupported builds still skip their adapter without disabling ordinary memory checkpoints. Restore validation remains strict.
- The proposed `catch -> Signboards=null` is deliberately not used: the native graph and detached hanging inventory would then lack their matching signboard metadata, and the existing preflight rejects such nodes. Returning an apparently usable but non-restorable node would not solve the problem.
- P2-2: null item/state dictionaries are skipped when retaining lighting identities. Commit errors now explicitly say the native graph was already written and the save cache failed. They still fail recovery and require reload. A silent/warning-only success could permit saving stale hanging-item metadata; no safe cache rollback has been demonstrated. The existing finally restores retained TagState identities.
- P3-1: early null ingredient guard added. Review's claimed null exception is not reproducible: ChemistryData.Tag is null-safe and Parts(null) returns zeros. Early guard preserves manual admission explicitly.
- P3-2: coffee optimization retained unchanged, including exact 26 C/no-heating/ingredient whitelist and pre-admission capacity bounds.
- P3-3: per-frame tablet lookups/decoration retained in this round. These are small UI checks; removing them is not needed for the requested work and could regress the live-accepted refresh behavior.
- P3-4: both SceneUnloaded and Commit must be static and have zero parameters before the build is accepted. Official capabilities still require only files.

## New Work After Review

- Read-only late-frame target tooltips while holding the configured details key; ignore the native dragged item's fake background and refuse selected group members. Native action callbacks/input remain unchanged. Target text refreshes at most every 100 ms, target changes immediately, release/loading clears retained UI references.
- Brown localized details-key hints, following the configured binding.
- Hydroponic current/projected growth score, remaining solution volume and mineral / (water + mineral), crop-specific quality counts. Deterministic splits mirror native SplitByHealth without invoking harvest, allocating items or consuming RNG. Stochastic count reductions retain ranges; berry quality uses clearly labelled expected counts.
- Nutrient-only tooltip renaming: water largest, mineral carrier ratio >=1.5%, summed six native plant contaminants <=1% of total. Centralized constants; existing chemistry/rename switches and container exclusions retained.
- Replacement announcement follows the player's five rewritten sections plus three new sections, in Chinese/English. Revision r2 replays normally once; no player settings reset.

## Verification Boundary

The new work is NOT covered by DS's earlier approval. Logs use `out/20261126-hydro-tooltip-*`. Unity rendering, held-key input, live pouring/tool targeting, actual seed quality/localized size names and supported signboard Commit/save/reload require in-game acceptance. The offline signboard fixture exercises metadata/readiness, not third-party Unity views or Commit serialization.
