# QuickSL 0.9.10 Hotfix Release Receipt

Date: 2026-11-27 (Asia/Hong_Kong).

## Source And Package

- Runtime source commit: `b90ce4a2ab3d53d81fbaf6e144cd1aae9870ce55`.
- Branch: `hotfix/0.9.10-f9`, isolated old-release worktree `ProbablyStolenMod-099fix`.
- BuildDirty=false at package time. No Shift Toolbox split code included.
- Universal package: `out/quicksl-0.9.10-universal/ProbablyStolenTools.zip` (606118 bytes).
- ZIP SHA256: `B49247075F14531C20042171A0042B75343B06D39DC7B651CEA162B03C5FA9AA`.
- Main DLL SHA256: `CF7751B4DE3E3A1DA82272233A6B7AD78337EAC5EC8B48683004883C4FE0B5AE` (574464 bytes).
- Helper SHA256: `051D390E4309209CC246199C89A1CEA70DF65FC282D58006FB0D1CBB8EAC77D5` (808448 bytes).
- Stable profile: `911508a6-e0eb-4cc0-91e8-e14b16ea28d7`, generated for installed 0.46D.
- Beta profile: `d927a6e4-ae7a-4e65-a5e2-9f9624c131f3`, verified identical to the published
  0.9.9 single-beta profile (`509D8A98...`, 670208 bytes).
- Final merged helper preserves both profiles; the changed helper hash reflects merge order,
  not a new beta schema. Final package has only main/helper/manifest/preview.
- Bilingual Workshop changelog and an update-only VDF are beside the ZIP, outside mod content.

## Validation And Deployment

- 480 reference tests and 22 optional compatibility tests passed.
- 105 native hooks / 76 fields, full native contracts/graphs and official capability scan passed.
- Only files; HardFlags/Error empty. Existing files-related Pending is not claimed empty.
- Direct missing/disabled-signboard regression: published DLL fails with the original Api
  TypeInitializationException, hotfix passes. Known signboard busy/fault/identity refusal retained.
- Complete game-Mono suites passed without optional mods and with real signboard + VBE assemblies;
  complete graph suite passed again against the final packaged merged helper/main DLL.
- Deployed via the normal backup/verification script:
  `out/full-release-deploy-20261127-002531/deployment.json`.
- Deployed DLL/helper hashes match the package. 106 save-directory files unchanged; settings not
  imported; other mods and game assemblies unchanged. Game was not started.
- No fresh Unity gameplay acceptance and no beta switch/live test in this run.

## Publication Status

GitHub remote inspection failed with `Could not resolve host: github.com`. The configured Windows
proxy at 127.0.0.1:7890 is not listening either. Package and local commit are ready; do not interpret
a local commit or deployed DLL as a successful remote push or Workshop upload.

Workshop upload has not been executed. Use the prepared mod-content directory, not the whole
artifact directory (which includes test provenance and publishing materials).

This documentation receipt is written after packaging. Its later commit does not change the
runtime source; rebuild reproducibility must use the recorded build commit, not a later docs HEAD.
