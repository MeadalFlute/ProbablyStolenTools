# Probably Stolen Tools

English | [中文](README.md)

A helper mod for **Probably Stolen**, targeting the full release **0.8.14**.
The full release runs Unity 2021.3.45f2 / Mono / x64 (game 0.46D) and provides its own **IMod / Harmony** loader — MelonLoader and BepInEx are not required.

---

## Features

### Rewind

- **Customer checkpoints**: a full snapshot is taken automatically after each ordinary customer's opening dialogue; double-click a row to return to that moment.
- **Manual checkpoints**: press `F9` at any time; they appear as "Manual" and do not require a customer.
- **Back to before opening** (`F7`) and **back to the previous day** (`F5`).
- Up to **20** customer/manual checkpoints, plus separate day-start and closed-shop nodes.
- **Cross-customer rewind**: while the third customer is present you can return to the second customer's checkpoint.
- Manual checkpoints created while closed (including the morning state before opening) can be restored.
- Abandoned future branches are truncated only when the next customer is accepted.
- Memory checkpoints are released when you leave the shop, the day changes or the scene changes; cross-scene rewinds are not supported.

### Appraisal and information

- **Quick appraisal**: the appraisal screen lists authenticity, issues and a suggested price, with optional auto-submit.
- **Double-click to inspect**: double-click an inspectable item to use a magnifying glass from your inventory.
- **Water quality labels**: tags water quality in real time and highlights mismatches in red.
- **Chemistry assist**: the water-quality row shows the dominant component and `share% | purity% | grade`; empty bottles show their size; droppers, graduated cylinders and containers under 200 mL keep their original names.
- **Wine labels**: shows glowberry condition; unfermented bottles show an expected quality, low water is reported on its own line, and a negative estimate reads "Brewing failed"; illegal yeast gets a separate native-style "Contraband" tag line.
- **Dialogue fast-forward**: advances ordinary customer small talk and stops at choices, SEC encounters and important dialogue.

### Logs

- **Settings → View log** opens a scrollable in-game view of the last 400 lines of `PSTools.log` and copies the log path to the clipboard.
- Log headers are unified as `[QuickSL]` / `[QuickSL/subsystem]` (for example `[QuickSL/Customer]`).

---

## Hotkeys

| Default | Action |
|---|---|
| `F6` | Return to the current customer's arrival checkpoint (memory restore) |
| `F5` | Return to the previous day's before-opening checkpoint |
| `F7` | Return to before opening today |
| `F8` | Show / hide the checkpoint panel |
| `F9` | Create a manual checkpoint immediately |

All keys are rebindable: `F8` → Settings → Hotkeys. Click a row and press the new key; `Esc` cancels, `Backspace` restores that row's default, `Delete` disables the hotkey, and "Reset hotkeys" restores everything. A conflicting key is swapped automatically.

> If a `dev.txt` file exists in the game folder (developer console), `F8` / `F9` clash with the game's own tools; the startup log warns about it. Regular players do not enable it.

---

## Panel

- Drag the title to move, resize from the bottom-right grip, collapse with the title button; position, size and collapsed state persist.
- Double-click a checkpoint to rewind. When refused, every concrete reason is listed (customer moving, dragging, on the phone, wrong state, ...) and the log is suggested.
- Failed captures appear as "Damaged" rows; hover to see the reason.
- The settings page scrolls and contains feature toggles, hotkeys and "View log".

---

## Chemistry rules

- Composition is shown only for containers **≤ 2999 mL**; larger tanks are left untouched.
- Water or alcohol is treated as the main component only above **65%**; otherwise the largest component other than water/alcohol is shown.
- Row format: `component (share% | purity% | grade)`, where share is the component divided by the total volume.
- Purity is based on the product expected after purification: water, alcohol, lipobond agent, fat, physical contaminant, microbe, plant matter, insoluble debris and the three mushroom pastes are removed; protein/acid/base are kept below 1 mL; chemical contaminant is capped at 0.7%.
- Grades: Pure / Pharmaceutical / Near-pharmaceutical / Street standard / Adulterated / Junk / Starch, split by purity and colour-coded.
- Alcohol-dominant liquids show `Alcohol (share%)` and the name becomes "Alcohol"; 74–76% reads "Medical alcohol" and ≥99.95% reads "Dehydrated ethanol".
- Empty bottles are named `Empty bottle (mini/small/standard/large)` and no longer use the water-quality row.
- Name replacement applies only to **tooltips** and **drag use/pour prompts**; inventory, trade lists and quest text keep the original name.

---

## Save safety

- The mod **never** calls the game's save function and never modifies save files on its own.
- Customer checkpoints are pure memory restores.
- "Back to opening / previous day" now **loads the checkpoint without writing it into the save**: the checkpoint is copied to a temporary file and read through `NewGameData.saveFile`; all later saves are performed by the game itself. A `.pre_restore` backup is still kept.
- Make a manual save before the first use, especially with many mods installed.

---

## Download and installation

**Recommended: Steam Workshop** — [Probably Stolen Tools](https://steamcommunity.com/sharedfiles/filedetails/?id=3829188627)

Click Subscribe on the Workshop page, then enable the mod in the game's Mods menu. Steam keeps it up to date.

Manual installation:

1. Download the `ProbablyStolenTools` folder from the releases and put it into `<game folder>\Mods\`.
2. Enable **Probably Stolen Tools** in the game's Mods menu and restart when prompted.
3. Load a save, enter the shop and press `F8`.

Requires the `files`, `reflection-external` and `dynamic-code` capabilities; no network access.

---

## Compatibility

- Built for the **full release** (Mono); Demo / MelonLoader paths are no longer maintained.
- Generally compatible with mods that don't modify the same UI or add new data structures to the game.
- Snapshots walk the game's business object graph, so mods that add a very large object graph **can** make capture fail (the panel then shows a "Damaged" row with the reason).

---

## Project layout

```
src/ProbablyStolenTools/         The mod (Harmony patches, snapshot engine, native UI, config)
tests/CheckpointTests/           Snapshot / graph round-trip and layout regressions
tests/ReleaseCompatibility/      Patch targets and private fields against the release DLL
scripts/Deploy-FullRelease.ps1   Backup and deploy into the game's Mods folder
release/manifest.xml             Mod manifest (ID, version, capabilities)
research/                        Reverse-engineering, design and issue notes
docs/workshop/                   Workshop description text
tools/                           Reverse-engineering tools (MetaDump / ILDump)
AGENT.md                         Handover document: architecture, constraints, acceptance order
```

Key files:

| File | Responsibility |
|---|---|
| `CustomerCheckpoint.cs` | Capture, restore and branch truncation for customer/manual checkpoints |
| `Rewind.cs` | File checkpoints (before opening / previous day) and the load-only reload flow |
| `ManagedStateAccess.cs` | Managed object graph traversal and field rules |
| `StateGraph.cs` | Slot / object / reference serialization and diff verification |
| `LiquidComposition.cs` | Liquid composition, purity and grade |
| `WineAppraisal.cs` | Glowberry and unfermented-bottle annotations |
| `Panel.cs` | `F8` panel and settings |

---

## Build and test

The game path is never committed. Copy `src\ProbablyStolenTools\local.props.example` to `local.props` and fill in your game folder (already in `.gitignore`):

```xml
<Project>
  <PropertyGroup>
    <GameDir>C:\Program Files (x86)\Steam\steamapps\common\Probably Stolen</GameDir>
  </PropertyGroup>
</Project>
```

You can also use the `PROBABLY_STOLEN_FULL_DIR` environment variable.

```powershell
dotnet build src\ProbablyStolenTools\ProbablyStolenTools.csproj -c Release
dotnet run -c Release --project tests\CheckpointTests\CheckpointTests.csproj
dotnet run -c Release --project tests\ReleaseCompatibility\ReleaseCompatibility.csproj -- "<game folder>" src\ProbablyStolenTools\bin\Release\Mono\ProbablyStolenTools.dll release\manifest.xml
.\scripts\Deploy-FullRelease.ps1
```

Current source baseline: 0 warnings / 0 errors; `CheckpointTests` passes **373** tests; the release build passes **57** Harmony targets, **76** fields and the official mod scanner. The single-pass entity backend is a candidate awaiting live acceptance. Offline tests are not a substitute for in-game verification.

`Snapshot.SemanticDevelopmentChecks` gates duplicate development snapshots and defaults to false for release. Enabling it adds time and memory to the primary and extra manual sample. Disabling it preserves complete preflight, verification, repair and necessary diagnostics. `Snapshot.SemanticEntityBlocks` controls the new backend. Restart the game after changing these settings.

---

## Versioning

- The single source of truth is `ModVersion` in `release/manifest.xml`.
- Rule: **major and minor versions are manual; the patch version increments before every `git push`** (for example 0.8.13 → 0.8.14).
- README-only changes do not bump the version.

---

## Reporting issues

Attach the logs — almost every issue can be pinpointed from them:

| File | Location |
|---|---|
| `PSTools.log` | `%LOCALAPPDATA%Low\Questing Goose Studio\Probably Stolen\PSTools.log` |
| `Player.log` | Same folder |
| `PSTools_history.log` | Same folder |

Also state the mod version, the game version and whether other mods are installed. The log can also be read in game via Settings → View log.

---

## Adapting for other mods with your own AI

The source and handover document are public so that compatibility work can be done by anyone:

1. Clone the whole repository (do not download only the DLL).
2. Give your AI assistant the repository, the target mod and the conflict log, and tell it to read `AGENT.md`.
3. Apply the change within the constraints in `AGENT.md`, then run both test projects above.
4. Deploy, verify in game, and open a PR with the change description and logs.

This game's mod ecosystem has no shared framework, and compatibility issues are rarely a one-line fix; the author does not actively adapt to third-party mods.
