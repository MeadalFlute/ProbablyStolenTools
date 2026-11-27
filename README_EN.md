# QuickSL

English | [中文](README.md)

Real-time rewind for **Probably Stolen**, formerly Probably Stolen Tools. The existing
ID `dsh.probablystolen.tools`, DLLs, installation/configuration names and Workshop identity
stay unchanged. Current version **1.0.0**: inspection, water/brewing labels, Shift details,
chemistry assistance and window gestures have moved to independent **Shift Toolbox**
(`shift.toolbox`); each mod works alone, and together they share a panel while keeping
separate settings. The game uses Unity 2021.3.45f2 / Mono / x64 and its own
**IMod / Harmony** loader; MelonLoader and BepInEx are not required. One package contains
compatibility data for both **stable 0.46D** and **beta 1.0.0.16** and selects
automatically at startup.

The full version stays at **0.9.10** on branch `Full` (including the hotfix for F9/automatic
checkpoints failing when the optional signboard mod is inactive, and for rewind rejecting
native views captured without a parent). The split line starts at 1.0.0: each Workshop push
bumps the middle number and resets the patch number, every local round bumps the patch number,
and every Workshop push is mirrored as a matching GitHub release so players can roll back.
The published legacy 0.9.9 still bundles the utilities and is not this line.

Uses the game's native Mono / IMod / Harmony loader, with only `files` capability.
No MelonLoader, BepInEx or shared runtime dependency is needed. The universal
manifest covers stable **0.46D** and beta **1.0.0.16**. Only beta original assemblies
were available for this round; stable live acceptance is separate.

## Features and Controls

- Customer, manual, closed-shop and same-scene return checkpoints.
- Retain today and yesterday in one session: 20 ordinary nodes per day plus a morning anchor.
- Rewind by double-click or keyboard. Browsing preserves the future until a new branch is committed.
- Abandoned nodes remain collapsed, read-only summaries. Actual file reloads invalidate memory nodes.
- Complete graph/exact-entity backend retains original object identity; it is not native save serialization.
- Quick reload and random reseeding are available, unbound by default.

| Default | Action |
|---|---|
| F5 | Previous day's morning |
| F6 | Previous node; Up/W, Down/S select, Enter confirms |
| F7 | Today's morning |
| F8 | Toggle panel |
| F9 | Manual checkpoint |

Rebind in settings; middle/side mouse buttons are supported. Esc cancels capture,
Backspace restores a default, Delete disables it. Same-mod conflicts swap keys;
peer-owned action keys are refused. Both panel bindings may share F8; QuickSL
consumes it once, including in the main menu. Reset leaves conflicting peer action
defaults unbound. Manually overlapping action bindings require manual correction.

Open the feature panel from QuickSL's native Mods settings, including before
loading a save. With Toolbox present, tabs are Rewind / QuickSL options / Shift
Toolbox. The toolbox owns its own page and configuration. Without it, QuickSL
retains its normal panel. `dev.txt` enables native developer keys that conflict
with F8/F9, so rebind in that environment.

Update notices wait for loading/black screens and both day/night reports to close,
then show for eight seconds. Two continuously active seconds or successfully
opening details acknowledges them. Both installed mods show one notice, not two.

## Safety and Compatibility

QuickSL never requests a save or overwrites the active save. Native save events
produce bounded backups; file rewind loads a temporary copy through the native
lifecycle and retains a pre-restore backup. Memory nodes are session-only.

- **Proper Signboards 1.0.48**: verified builds capture independent placement and
  lighting records and rebuild caches. Absent mods are untouched. Unknown builds
  receive a notice and skip specialized support; their signboard rewind remains risky.
- **Claims & Uprisings**: safe file-reload coexistence only. First detection turns
  on Disable memory checkpoints once; a manual unsupported override survives
  restart. The warning requires dismissal, once per mod. Matching morning file
  nodes remain listed with memory disabled.
- VBE rewind state belongs to QuickSL; brewing annotations and ingredient-drop
  compatibility guards now belong to Toolbox.
- Unknown layouts fail closed; large third-party graphs may exceed limits.

Automatic checkpoints can be disabled without removing manual capture or existing
nodes. Use memory for whole-day rewind controls F5/F7 separately from node restore.
File reload loses unsaved progress and invalidates old memory graphs. Random
reseeding changes only future randomness, not existing items/customers.

## Limits and Layout Generator

Native Mods settings edit object, slot and logical-byte limits, persisted in
`PSTools.settings.xml`. Defaults are 200000 objects, 3000000 slots and 256 MiB per
snapshot. Raising caps does not add traversal or preallocate the full cap for
already-small graphs, but allows more expensive captures. Failures show concrete
causes and limits for reporting.

The merged helper selects recorded game profiles by assembly fingerprint; no
runtime code generation or dynamic loading occurs. New game builds may need an
updated helper or API changes. An independently distributed Static Layout
Generator can generate a helper outside the game process; this is not a generic
third-party mod adapter or a promise of future compatibility.

See [player tool instructions](tools/StaticSnapshotLayouts/PLAYER-README.md).
Close the game before replacing a helper, keep backups outside Mods, and never
install the generator, Cecil or test hosts as mods.

## Installation and Development

The [existing Workshop identity](https://steamcommunity.com/sharedfiles/filedetails/?id=3829188627)
is retained. The split candidate has not been uploaded. Manual packages belong in
`<game>\Mods\ProbablyStolenTools\` with the main DLL, helper and manifest.
Toolbox is a separate installation, never downloaded or dynamically loaded by QuickSL.

Use .NET 8 SDK and `src/ProbablyStolenTools/local.props` or the
`PROBABLY_STOLEN_FULL_DIR` environment variable. Neither repo references the other.

```powershell
dotnet build src\ProbablyStolenTools -c Release
dotnet run --project tests\CheckpointTests -c Release
dotnet run --project tests\OptionalModCompatibilityTests -c Release
.\scripts\Test-SplitMono.ps1 -GameDir "<game>" -QuickOnly
.\scripts\Test-SplitMono.ps1 -GameDir "<game>" -ToolboxDll "<ShiftToolbox.dll>"
```

Build `tests/CapabilityMigrationProbe/MonoFixture` and `MonoHost` before those
scripts. The 480 reference assertions include frozen pre-split tool-rule fixtures;
current toolbox code is covered by its own Rules/Contracts/Mono suites.

Every main build regenerates a **single current-game helper**. Universal packages
must merge the other original profile, then scan/deploy the actual merged result:

```powershell
.\scripts\Package-FullRelease.ps1 -GameDir "<game>" -ManifestPath release/universal/manifest.xml `
  -AdditionalLayoutPaths "<stable original helper>" -OutputRoot "<new output>"
.\scripts\Deploy-FullRelease.ps1 -GameDir "<game>" -PackageDirectory "<output>\ProbablyStolenTools"
```

Release packaging requires a clean tree. Development deployment is not a release;
keep SDK tools, host tables and test executables out of packages. Development
snapshot comparisons default off. The manifest is the version source of truth;
all manifests and Plugin must agree. Existing policy increments the patch before
a release push; documentation alone does not bump it.

Read `AGENT.md` and `research/2026-11-26-feature-split-handoff.md` for ownership,
protocol and acceptance. Routine maintenance can use one conversation per repo;
protocol or migration changes require coordinated testing in both. Offline tests
do not verify Unity rendering, real input or actual gameplay.

Report game/mod versions, mod list, `PSTools` logs/rotations, settings and
`Player.log`. Tool issues need `ShiftToolbox.log`; integration issues should
include both. The in-game log viewer shows the tail and copies its path.
