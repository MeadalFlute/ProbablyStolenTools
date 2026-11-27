# QuickSL

[中文](README.md) | [English]()

Real-time rewind for **Probably Stolen**: restore the pawnshop to any snapshot checkpoint — the moment you walked in, right after closing, or a point you marked yourself.

## Features

- A checkpoint is created automatically once the customer's opening dialogue ends; manual, closing-time and same-scene return checkpoints are also available.
- Keep today and yesterday; up to 20 regular checkpoints per day plus a pre-opening anchor.
- Double-click a checkpoint or pick one with the keyboard. Browsing old checkpoints never deletes the future; it is discarded only after you branch off.
- Discarded checkpoints stay folded and cannot be restored. Loading a save invalidates in-memory checkpoints.
- Turn automatic checkpoints off, disable in-memory checkpoints, or route full-day rollbacks through the vanilla load instead.
- Quickly reload the latest save on disk and reroll upcoming randomness (unbound by default).
- Never saves for you and never overwrites your save. Vanilla saves get a size-limited backup.

## Install

- **Workshop**: subscribe to [Probably Stolen Tools / QuickSL](https://steamcommunity.com/sharedfiles/filedetails/?id=3829188627). The Workshop build is currently the 0.9.x full version, which still bundles the utilities.
- **Manual**: copy `ProbablyStolenTools.dll`, `ProbablyStolenTools.StaticLayouts.dll` and `manifest.xml` from the archive into `<game folder>\Mods\ProbablyStolenTools\`, enable it in the in-game mod list and restart.
- **GitHub builds**: from 1.0.0 the utilities live in [Shift Toolbox](https://github.com/MeadalFlute/ShiftToolbox). Install it separately for appraisal, labels, chemistry assistance and window gestures; both mods work alone or together.

Do not mix an old 0.9.x package that still bundles the utilities with the new split build.

## Keys

| Default | Action |
|---|---|
| F5 | Back to the previous day's opening |
| F6 | Select a checkpoint; ↑/W, ↓/S to move, Enter to confirm |
| F7 | Back to today's opening |
| F8 | Show or hide the panel |
| F9 | Create a manual checkpoint |
| unbound | Quick reload save, reroll randomness |

Keys are rebindable, including mouse middle and side buttons. Esc cancels capture, Backspace restores the default, Delete clears the binding. Keys already used by Shift Toolbox are refused rather than stolen.

## Panel

The panel can be dragged, resized and collapsed. Open it from Main menu → Mods → QuickSL → gear → **Open QuickSL panel**, even before loading a save.
With Shift Toolbox installed, one panel shows the “Rewind / QuickSL settings / Shift Toolbox” tabs and each mod keeps its own settings.

## Compatibility

- **Proper Signboards 1.0.48**: saved positions and brightness are handled; nothing happens when it is absent. Unknown builds only get a notice.
- **Claims & Uprisings**: safe coexistence through file loads only. Detection enables “disable in-memory checkpoints”.
- Unknown game layouts are rejected with a reason instead of silently dropping fields.
- Very large third-party object graphs can hit snapshot limits; the limits are adjustable in the mod settings.

## Reporting issues

Attach `PSTools.log`, `PSTools_errors.log`, `PSTools_history.log` and `Player.log`, plus game and mod versions and your mod list. The settings panel shows the log tail and can copy its path.
For utility problems attach `ShiftToolbox.log`; for interaction problems between the two mods, attach both.

## Development

Requires the .NET 8 SDK and the game assemblies; the game path never enters Git. Configure `src/ProbablyStolenTools/local.props` or set `PROBABLY_STOLEN_FULL_DIR`.

```powershell
dotnet build src\ProbablyStolenTools -c Release
dotnet run --project tests\CheckpointTests -c Release
dotnet run --project tests\OptionalModCompatibilityTests -c Release
.\scripts\Test-SplitMono.ps1 -GameDir "<game folder>" -QuickOnly
.\scripts\Test-SplitMono.ps1 -GameDir "<game folder>" -ToolboxDll "<ShiftToolbox.dll>"
```

Release packages must come from unmodified source and merge both game-version layout tables:

```powershell
.\scripts\Package-FullRelease.ps1 -GameDir "<game folder>" -ManifestPath release/universal/manifest.xml `
  -AdditionalLayoutPaths "<stable layout helper>" -OutputRoot "<output folder>"
.\scripts\Deploy-FullRelease.ps1 -GameDir "<game folder>" -PackageDirectory "<output folder>\ProbablyStolenTools"
```

`release/manifest.xml` is the version source of truth; all three manifests and `Plugin.Version` must match.
No shared prerequisite DLLs, no shipping of build tools or test hosts. Offline passes do not prove in-game acceptance.
Maintenance boundaries, the cross-mod protocol and further rules live in `AGENT.md`.

## License

Released under the [GNU Affero General Public License v3.0](LICENSE). You may use, modify and redistribute it; if you distribute a modified version, or offer one over a network, you must publish the corresponding source under the same license.
