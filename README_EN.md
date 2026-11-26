# QuickSL

Previously Probably Stolen Tools. Only the display name changes: mod ID, DLLs, installation folder and configuration filenames stay unchanged, preserving existing settings. The Mods page shows both Chinese and English descriptions; the universal manifest declares 0.46D and 1.0.0.16.

English | [中文](README.md)

A helper mod for **Probably Stolen**, currently **0.9.9**. The game uses Unity 2021.3.45f2 / Mono / x64 and its own **IMod / Harmony** loader; MelonLoader and BepInEx are not required. One package contains compatibility data for both **stable 0.46D** and **beta 1.0.0.16** and selects automatically at startup.

## Game Beta Branch

Game **1.0.0.16 is beta**, separate from stable 0.46D. The main DLL plus merged helper selects the 0.46D or 1.0.0.16 layout by assembly fingerprint, so switching between these two recorded builds needs no manual helper swap; future versions are still rejected when the fingerprint is unknown. This update replaces the old 1.0.0.14 beta profile.
The merge only shares identical field-access code; version-specific type tokens stay isolated. No runtime code generation, extraction or dynamic loading is added, and game closure state is not dropped.
Maintainers use `release/universal/manifest.xml`; the single-branch entries remain. Only the beta Mono suite was rerun: the stable branch's original assemblies are unavailable and its universal-package branch still needs its own live acceptance. This is not a claim of compatibility with future beta builds or arbitrary third-party mods. See `research/2026-11-16-universal-static-layouts.md`.

## Optional Mods

- Neither expansion is required or bundled. Missing or disabled expansions leave vanilla behavior unchanged.
- **Vanilla Brewing Expansion 1.4.4**: preserve expansion names and native tooltips for marked products and expansion recipes (nutrifruit/dream-cap bottles and brewing buckets). Do not apply vanilla berry-wine predictions, liquid renaming or accelerated chemistry to them. Ordinary berry wine remains unchanged. Unfermented expansion bottles receive no additional prediction from this mod. Existing graph capture retains expansion tags; live fermentation/overnight rewind still needs acceptance.
- With the expansion enabled, nutrifruit/dream-cap drops into water jugs and brewing vessels no longer trigger the native drag handler's follow-up dissolution, including the first ingredient before `VBE_STATE` exists. Vanilla dissolution remains unchanged without the expansion.
- **Proper Signboards 1.0.48**: only when enabled and its build is verified, its own signboard placements and lighting records are saved, stale views are cleared after rewind and its save cache is rebuilt. Regular saves are not intercepted; unknown builds only show a notice and skip signboard-specific support without disabling QuickSL or memory checkpoints, though signboard rewind remains risky on those builds. Faulted or identity-changed boards on verified builds still refuse unsafe restores; live remove/re-hang plus save/reload needs combined acceptance.
- **Claims & Uprisings**: **safe file-reload coexistence only, not full memory-rewind support** (originally audited at 1.1.18). First detection enables **Disable memory checkpoints** once and persists the handled mod ID. You may switch it off to force-enable unsupported memory rewind; that override survives restarts, but resulting bugs are unsupported. Removing the mod does not reset the switch. Independent room, resource and soldier state is not fully captured. While Claims is active, file reloads always use the full native lifecycle without extra customer/shutter repair; no save is requested.
- Offline tests do not replace live combination testing. Details: `research/2026-11-23-optional-mod-compatibility.md`.

## Snapshot Limits and Manual Layout Generator

Snapshot failures now show the concrete exception, effective limit and recovery
instructions. Include that screenshot and `PSTools.log` in a bug report.

Use the mod's gear button in the main-menu Mods screen to change object, slot
and byte limits. Apply also updates `PSTools.settings.xml`; manual edits to that
XML while the game is closed remain authoritative on restart.
The same screen has **Open QuickSL feature panel**, allowing feature switches
and hotkeys to be adjusted before loading a save.

The default object cap is 200000 (supported: 1000–1000000). Raising it does not
traverse additional objects or preallocate the entire higher limit for graphs
already below the old cap. It does allow larger captures, which may use more
memory and take longer. Slot/byte budgets are logical payload limits, not a cap
on the whole mod's physical memory footprint.

After a game update, wait for a mod update. A separately published Windows x64
**Static Layout Generator** may help once available; extract it outside Mods and
close the game, then run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Generate-StaticLayouts.ps1 -GameDir "E:\SteamLibrary\steamapps\common\Probably Stolen"
```

Use your own game directory. Copy only the generated helper DLL from
`generated-<timestamp>` into the actual mod folder, keeping the old helper outside
Mods. For the default `Mods\ProbablyStolenTools` installation, `-Install` backs up
and replaces only the helper. The player package needs no .NET SDK/runtime.

Read [the player guide](tools/StaticSnapshotLayouts/PLAYER-README.md) for rollback
and limitations. This does not automatically support arbitrary third-party mod
types or changed gameplay APIs. Report persistent failures instead of bypassing
validation. Maintainers package the separate release attachment with
`scripts/Publish-StaticLayoutTool.ps1`; its executable and Mono.Cecil never belong
in Mods.

---

## Features

### Rewind

- **Customer checkpoints**: a full snapshot is taken automatically after each ordinary customer's opening dialogue; double-click a row to return to that moment.
- **Manual checkpoints**: press `F9` at any time; they appear as "Manual" and do not require a customer.
- **Back to before opening** (`F7`) and **back to the previous day** (`F5`); the in-memory morning anchor is preferred and adjacent days remain browsable.
- A session keeps today and yesterday: up to **20** customer/manual/closed-shop checkpoints per day, plus one free day-start anchor; older days are released when a third day begins.
- **F6 picker**: Up/W and Down/S to move, Enter to confirm, Esc to cancel, or double-click a node; middle/side mouse buttons can be rebound.
- **Cross-customer rewind**: while the third customer is present you can return to the second customer's checkpoint.
- Browsing old nodes does not delete the future; it is truncated only when the next customer starts a new branch, leaving up to 80 read-only summaries.
- Manual checkpoints created while closed (including the morning state before opening) can be restored.
- Leaving the same shop pauses capture; returning and staying settled for about 0.5 seconds adds a node, and cross-day history keeps the adjacent two days.
- **Since 0.9.0**: customer/manual checkpoints use a hybrid "full object graph + exact entity payload" backend that keeps original object identities, full verification and fail-closed fallback. It is still an in-memory rewind backend, not vanilla save replacement.

### Languages

- Water quality, purity, grade and wine annotations now read the game's native localization keys and follow the selected game language instead of supporting only Chinese/English.
- Missing entries fall back from the current language table to the English table and then to English text; unknown third-party liquids keep their own names.
- Settings can independently disable "Water quality labels", "Chemistry assist" and "Wine annotations".

### Appraisal and information

- **Quick appraisal**: the appraisal screen lists authenticity, issues and a suggested price, with optional auto-submit.
- **Double-click to inspect**: use your magnifying glass on an inspectable item. If an inspection page is already open, close it before inspecting the new item. Labelers and keycards are not used as magnifiers.
- **Water quality labels**: tags water quality in real time and highlights mismatches in red.
- **Chemistry assist**: independently select current percentage, predicted purity and grade; only current percentage is enabled by default. Empty containers show size/name, hydroponic seeds show the matching item name, and labels start at 150 mL with no maximum capacity cap.
- **Composition details**: hold the rebindable item-details key (either Shift by default) over the shop tap to append water quality and every nonzero constituent's percentage while preserving its connection description; release restores native behavior. Over liquid containers it replaces the entire tooltip with Name / Amount / Share columns. Constituents sort by descending share. The primary component is orange and the rest white, with columns aligned using measured text widths. Amounts select uL/mL/L automatically; percentages use two decimal places, or `<0.01%` for smaller nonzero traces. Both switches default to on and work independently of the chemistry master switch.
- Details also support droppers, measuring vessels and other containers below 150 mL. The minimum applies only to ordinary summary annotations, not held details; dropper/measuring-vessel auto-rename exclusions remain unchanged.
- **Compact reagents**: acid, base, lipobond agent and reducing agent show only the main substance, chemical contaminants and heavy metals, including zero impurities. Tooltip auto naming prefixes `*` when heavy metals are strictly below 0.10%; the item itself is not renamed.
- The three-row compact reagent view applies to ordinary hover only; holding the details key or pinned details shows every nonzero constituent, still sorted by descending share.
- **Unlock working machine modules** (default off): bypass only the working machine's module-bay locks, not batteries, ingredients, outputs or individually locked items. Turning it off immediately restores native lock queries without permanently changing lock fields.
- **Auto-blend**: dropping blendable items into a blender follows the game's own blend rules and sound; different products stay separate and insufficient capacity is rejected early.
- **Smart extrusion** (default on): inserting a liquid container into the chemical extruder selects the product of the unique matching recipe when the main constituent reaches 70%; it never produces automatically. Below 70%, empty containers, no recipe or ambiguous matches keep the native selection. Independent switch, gated by the chemistry master switch.
- **Rapid extraction** (off by default): adding liquid to a container with a positive amount of alcohol converts the three mushroom-mash types into extracts using the original ratios, rounding and conditions.
- **Rapid reactions** (off by default): a separate switch for the nine explicit non-thermal reactions, at most 512 extra steps per run, stopping when nothing changes. Existing saved choices are preserved.
- **Rapid boiling** (default off): hotplates only. Fully remove each matching substance when its native condition is met: lipobond agent >55°C, alcohol >78.37°C, water >100°C, microbes >=70°C. Requires the chemistry master switch; other heat reactions and expansion brewing containers are untouched.
- **Facility projections**: hold the details key over water purifiers, evaporators, hydroponics and fish tanks/RAS to see expected overnight output/removal. Missing water/container/power alerts also appear in ordinary tooltips. Purifiers report completion when the actual forecast removes nothing.
- **Forced tooltips**: while pouring liquids, moving items or using tools, hold the details key (Shift by default) to show the tooltip of the target under the cursor, preferring its detailed version and leaving native interaction unchanged. Items with details add a brown binding-key hint to ordinary tooltips, matching your configured key.
- **Hydroponic details**: show current and post-work growth score, remaining nutrient solution volume and mineral percentage; on harvest, list nonzero amounts per quality. Random yields show their range and glowberry random qualities show expected counts without reading or advancing game RNG.
- **Nutrient solution naming**: when water is the largest single component, minerals/(water+minerals) is at least 1.5%, and heavy metals, chemical contaminants, microbes, organic waste, acid and base together stay within 1% of total liquid, only the tooltip name becomes "Hydroponic nutrient solution (share%)". Controlled by chemistry assist and container auto rename; measuring vessels, droppers and VBE containers are not renamed.
- **Wine labels**: show glowberry condition, expected unfermented-bottle quality, low-water/failure warnings and native-style contraband labels directly, without holding a key. Inspection summaries also show wine information directly; the wine-annotation switch still controls both.
- **Dialogue fast-forward**: advances ordinary customer small talk and stops at choices, SEC encounters and important dialogue.

### Logs

- **Settings → View log** opens a scrollable in-game view of the last 400 lines of `PSTools.log` and copies the log path to the clipboard.
- Log headers are unified as `[QuickSL]` / `[QuickSL/subsystem]` (for example `[QuickSL/Customer]`).

### Update Notes

Once per version, after entering a save and closing the day/night report, a compact
top-centered notice appears for five seconds. Click anywhere on it for scrollable
details. Displaying it persists acknowledgement even without a click. Other compact
top windows are avoided on a best-effort basis, not a cross-mod layout guarantee.
An independent 6px blue bar fills from empty to full over a static full-width gray track below the details link during the five-second display. Details also explain the Vanilla Brewing Expansion nutrifruit/water-jug compatibility guard.

### Window Gestures

- **Shift-drag window snapping**: hold either physical Shift while dragging a native movable window near another to snap adjacent edges into alignment. Separate switch, on by default.
- **Shift-middle-click window pinning**: hold either physical Shift and middle-click a native movable window's title or empty background to pin it above peers. Most recently pinned stays highest; ordinary focus does not change pin order. Repeat to unpin. Closing clears its pin; disabling the setting clears all pins. Separate switch, on by default. Item/control clicks, drags and window opening do not pin; native menu/modal layers are not overridden.
- Titles use a brown `[置顶]` marker in Chinese and `[PIN]` otherwise, without loading system fonts. These gestures remain physical Shift regardless of the item-details binding, and pin state is not written to saves or checkpoints.
- **Pinned item details**: Shift-middle-click an item to pin or dismiss its detail view, including tap quality, liquid composition and machine projections; each feature's switch still applies. The tooltip follows the item and its owning window layer, not global always-on-top; closing an outer container or loading/restoring clears it. Pinning is disabled while holding the left button or dragging an item, group or window, preserving native middle-button flipping. Shift-left-click remains entirely native and never pins.
- Announcement acknowledgement includes the version and content revision. This replacement includes machine module unlocking, real section separators and no blank paragraph lines.

---

## Hotkeys

| Default | Action |
|---|---|
| `F6` | Open the checkpoint picker; Up/W, Down/S, Enter to confirm |
| `F5` | Return to the previous day's before-opening checkpoint |
| `F7` | Return to before opening today |
| `F8` | Show / hide the checkpoint panel |
| `F9` | Create a manual checkpoint immediately |
| `Shift` | Hold for item details, composition and machine projections |
| `Shift + middle-click` | Toggle item-detail pinning, or window pinning over its title (fixed gesture) |
| Unbound | Quick reload save / reroll random (assignable in Settings) |

Single-key actions and the details key are rebindable: `F8` → Settings → Hotkeys. Click a row and press the new key; `Esc` cancels, `Backspace` restores that row's default, `Delete` disables the hotkey, and "Reset hotkeys" restores everything. A conflicting key is swapped automatically. Snapping and pinning gestures still use physical Shift regardless of the details-key binding.

> If a `dev.txt` file exists in the game folder (developer console), `F8` / `F9` clash with the game's own tools; the startup log warns about it. Regular players do not enable it.

---

## Panel

- Drag the title to move, resize from the bottom-right grip, collapse with the title button; position, size and collapsed state persist.
- Double-click a checkpoint to rewind. When refused, every concrete reason is listed (customer moving, dragging, on the phone, wrong state, ...) and the log is suggested.
- Failed captures appear as "Damaged" rows; hover to see the reason.
- The settings page scrolls and contains feature toggles, hotkeys and "View log".
- **Automatic checkpoints** are on by default. When disabled, morning/customer/closed-shop/return nodes are no longer created automatically; manual F9, the picker and restoring retained nodes still work, while F5/F7 load a save file.
- **Disable memory checkpoints** is off by default; enabling it stops memory capture and restore (see the one-time blacklist policy above). **Use memory for whole-day rewind** is on by default; turning it off routes F5/F7 to disk without disabling individual memory nodes. File reloads invalidate all old memory nodes.
- The memory-conflict notice stays until manually closed and has a small blue source label. Each conflicting mod is shown only once across restarts, recorded in `Compatibility.NotifiedMemoryConflicts`. With memory disabled, existing matching morning files still appear as before-opening/file checkpoints; listing them does not request a game save.
- The settings page also has **Quick reload save** and **Reroll random** actions; both are unbound by default and can be assigned in the hotkeys section. Quick reload reads the active slot's latest disk save, warns that unsaved progress and all memory checkpoints will be lost, does not save first, and does not overwrite the active save. Reroll only changes future random results, does not rebuild existing customers/items or reload a save, and a new seed does not guarantee a different outcome.

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
2. Enable **QuickSL** in the game's Mods menu and restart when prompted.
3. Load a save, enter the shop and press `F8`.

Requires only the `files` capability and no network access; `reflection-external` and `dynamic-code` are no longer required. Keep both `ProbablyStolenTools.dll` and `ProbablyStolenTools.StaticLayouts.dll`; do not replace only the main DLL.

---

## Compatibility

- This package contains compatibility data for **stable 0.46D** and **beta 1.0.0.14**; switching between these recorded versions needs no helper swap, but future game versions still require a mod update.
- 1.0.0.14 is still a beta; back up saves before first use. If checkpoints fail after switching back to 0.46D, include the game version, log and mod list.
- Stable and beta use different game assemblies; the universal package does not replace each branch's own live acceptance.
- Generally compatible with mods that don't modify the same UI or add new data structures to the game.
- Snapshots walk the game's business object graph, so mods that add a very large object graph **can** make capture fail (the panel then shows a "Damaged" row with the reason).
- The shipped package declares only `files`; layout tables are bound to the target game/Mono assembly version and unknown layouts fail closed instead of capturing incomplete state.

---

## Project layout

```
src/ProbablyStolenTools/         The mod (Harmony patches, snapshot engine, native UI, config)
tests/CheckpointTests/           Snapshot / graph round-trip and layout regressions
tests/ReleaseCompatibility/      Patch targets and private fields against the release DLL
release/manifest.xml             Mod manifest (stable 0.46D)
release/beta/manifest.xml        Beta 1.0.0.14 manifest
release/universal/manifest.xml   Dual-version universal manifest
scripts/Package-FullRelease.ps1  Clean-source package build (single/universal)
scripts/Deploy-FullRelease.ps1   Backup and deploy into the game's Mods folder
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

For the current beta installation, use the beta manifest. Build the universal package from a clean tree with the other version's original helper:

```powershell
.\scripts\Test-StaticLayouts.ps1 -GameDir "<game folder>" -ManifestPath release/beta/manifest.xml
.\scripts\Package-FullRelease.ps1 -GameDir "<game folder>" -ManifestPath release/universal/manifest.xml `
  -AdditionalLayoutPaths "<stable original helper>" -OutputRoot "<new output folder>"
.\scripts\Deploy-FullRelease.ps1 -GameDir "<game folder>" -PackageDirectory "<output>\ProbablyStolenTools"
```

Current development candidate: 0 warnings / 0 errors; **469** reference tests and **22** optional-mod tests pass in this round. The installed beta passes **102** Harmony targets, **76** fields, 16 protected-owner checks and the official scanner. Vanilla and real-VBE game-Mono offline fixtures pass. Earlier passing records cover 13 save-action tests, 14 native history/settings IO fixtures and 11 player-tool assertions. Shift-middle-click timing and pinned full details need live acceptance; original stable assemblies were not retested. The single-pass entity backend ships with 0.9.0.

`Snapshot.SemanticDevelopmentChecks` gates duplicate development snapshots and defaults to false for release. Enabling it adds time and memory to the primary and extra manual sample. Disabling it preserves complete preflight, verification, repair and necessary diagnostics. `Snapshot.SemanticEntityBlocks` controls the new backend. Restart the game after changing these settings.

---

## Versioning

- The single source of truth is `ModVersion` in `release/manifest.xml`.
- Rule: **major and minor versions are manual; the patch version increments before every `git push`** (for example 0.9.4 → 0.9.5).
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
