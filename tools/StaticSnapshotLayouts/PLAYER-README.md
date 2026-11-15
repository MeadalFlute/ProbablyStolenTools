# Probably Stolen Tools: Manual Static Layout Generator

This is a separate Windows x64 program, NOT a game mod. Do not put this folder,
its executable or Mono.Cecil in Mods. The self-contained download needs no .NET
SDK/runtime installation. Extract the whole archive to a writable folder first.

## 中文使用方法

适用于游戏更新后，本 Mod 提示静态布局表版本不匹配的情况。

1. 退出游戏，解压整个工具压缩包。不要放进 `Mods`。
2. 在解压后的工具目录打开 PowerShell：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Generate-StaticLayouts.ps1 -GameDir "E:\SteamLibrary\steamapps\common\Probably Stolen"
```

将示例路径替换为包含 `Probably Stolen.exe` 的游戏目录。
命令只会在工具目录的 `generated-日期` 内生成
`ProbablyStolenTools.StaticLayouts.dll`，不修改任何游戏文件或存档。

3. 备份旧 helper 后，用生成的 DLL 替换实际 Mod 目录中的同名文件。
   默认路径是 `<游戏目录>\Mods\ProbablyStolenTools\`，创意工坊安装可能不同。
   只替换 helper，不要替换主 DLL、游戏程序集或 manifest。
   不要把旧 helper 的 `.dll` 备份留在 Mods 中，加载器会扫描它。

若使用上述默认 Mod 路径，可在命令末尾加 `-Install` 自动替换；
旧 helper 会保存在工具的 generated 目录里，名为 `previous-helper.dll`。
要回滚时退出游戏，把该备份复制回 Mod 目录并改名
`ProbablyStolenTools.StaticLayouts.dll`。

4. 启动游戏。如果加载器要求重新启用/批准模组，使用它的正常界面确认。
5. 测试建点、恢复与物品位置。仍失败时，提交屏幕上的具体异常、
   `PSTools.log`、游戏版本与 Mod 列表，等待 Mod 更新。

## English

Close the game, extract the complete package, then run the command above with
your own game directory. Copy only the generated helper DLL into the actual
mod folder, keeping an old copy outside Mods. Add `-Install` only for the default
`Mods\ProbablyStolenTools` installation. Restart and re-enable normally if asked.

## Limits / 限制

- This regenerates fixed layouts for the installed game's Mono assemblies.
  It does not generate code inside the game or modify saves, trust records,
  approval records, main mod DLL or game assemblies.
- It does NOT automatically support arbitrary third-party mod types.
- It does NOT repair changed gameplay APIs, unsupported fields, removed types,
  future loader-policy changes or a corrupt game installation.
- Generation is not proof of gameplay compatibility. If failures continue,
  wait for an update rather than bypassing validation.
- Steam Workshop updates may replace the helper; generate again if needed.
- Steam stable/beta branches require different module fingerprints. Generate
  for the currently installed branch, never merge two helper DLLs in Mods.
- Game-generated closures are retained because they can hold gameplay state.
  Only shared protected/serialization boundaries are excluded. Empty declared
  field factories are shared without dropping any type or field coverage.
- Project: https://github.com/MeadalFlute/ProbablyStolenTools
- Matching source is included in `source/`. To rebuild it with the .NET 8 SDK:
  `dotnet build .\source\StaticSnapshotLayouts.csproj -c Release -p:PlayerLayoutTool=true`.
