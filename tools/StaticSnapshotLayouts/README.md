# Static Snapshot Layout Generator

Build-time .NET 8 tool. Mono.Cecil is a tool dependency only and must not be
packaged with the mod.

The main Mono build invokes this tool twice:

- Target table: `src/ProbablyStolenTools/bin/<Configuration>/Mono/ProbablyStolenTools.StaticLayouts.dll`.
  This is a declared, scanned, shipped mod assembly.
- Host test table: `out/static-layouts-net8/ProbablyStolenTools.StaticLayouts.dll`.
  This targets the .NET 8 test host's BCL and MUST NOT be shipped.

Each field factory contains an explicit type token and literal declared field
names, including private nested generic collection storage. Runtime code selects
only these factories. No runtime type enumeration or code generation is emitted.
Field factories return normal FieldInfo objects, not private direct-access IL.

The registry records target module MVIDs. Unknown type/layout builds fail closed.
The generated target table is byte-reproducible for the same input modules and
generator schema; timestamps are fixed and the table MVID is content-derived.
Only output build artifacts are written; writing into the game's Managed
directory is refused.

Manual commands:

```powershell
dotnet run -c Release --project tools/StaticSnapshotLayouts -- "<GAME_DIR>" "<OUTPUT_DLL>"
dotnet run -c Release --project tools/StaticSnapshotLayouts -- "<GAME_DIR>" "<HOST_OUTPUT_DLL>" --host
```

`--test-mismatched-version` generates a negative fixture, never a release table.
Run `scripts/Test-StaticLayouts.ps1` for target-Mono field coverage, actual graph
round trips, all shipped DLL capability scanning, reproducibility and rejection
tests. A successful build/scan is not Unity gameplay or future-version acceptance.
