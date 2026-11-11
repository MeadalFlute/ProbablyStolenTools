# Loader capability migration probes

These projects are offline tools, not mod payloads. Never copy either output to
the game's Mods directory. The scanner project intentionally uses unrestricted
reflection to drive the official scanner outside the game; only Probe.dll is
the scan target.

Run from the repository root in PowerShell, replacing `<GAME_DIR>`:

```powershell
$game = "<GAME_DIR>"
$probe = "tests/CapabilityMigrationProbe/Probe"
$scanner = "tests/CapabilityMigrationProbe/Scanner/Scanner.csproj"

dotnet build "$probe/Probe.csproj" -c Release "-p:GameDir=$game"
dotnet run -c Release --project $scanner -- $game "$probe/bin/Release/netstandard2.1/Probe.dll" clean

dotnet build "$probe/Probe.csproj" -c Release "-p:GameDir=$game" -p:DefineConstants=PRIVATE_LAYOUTS
dotnet run -c Release --project $scanner -- $game "$probe/bin/Release/netstandard2.1/Probe.dll" private

dotnet build "$probe/Probe.csproj" -c Release "-p:GameDir=$game" -p:DefineConstants=NEGATIVE_CONTROLS
dotnet run -c Release --project $scanner -- $game "$probe/bin/Release/netstandard2.1/Probe.dll" negative
```

Check each command's exit status before continuing. Do not build these modes in
parallel: they share the output directory. The scanner fails on an unexpected
capability set, hard flag, pending item or scanner error.

Expected on the investigated scanner v4:

- `clean`: no capabilities/findings; fixed List/Dictionary/HashSet/RNG fields,
  public collection view/enumerator types, literal private game closure lookup,
  official ModReflect and public Array/Delegate APIs.
- `private`: reflection-external with exactly three findings; Dictionary Entry
  and HashSet Slot field lookup through a computed nested/array-element type.
- `negative`: reflection-external and dynamic-code with exactly four findings;
  dynamic enumeration, fixed System type bulk enumeration, literal System type
  name lookup and Expression.Compile.

The scanner also reads the target game's mscorlib/System.Core PE metadata,
prints relevant layouts, counts game closure/readonly definitions, and checks
that ModReflect.Set rejects a readonly field on a constructor-free offline game
fixture. No save, game process, loader policy or trust record is changed.

This does not execute the probe accessors inside Unity/Mono. A clean static scan
is not proof of complete graph coverage, private-field write safety, reference
topology preservation, performance or future scanner compatibility.

## Build-time literal type tokens

`LiteralTokens` uses Mono.Cecil only at build time to emit explicit generic
private nested type tokens followed by fixed `GetField("value"/"next")` calls.
The generated IL is equivalent to a literal `typeof(Dictionary<K,V>.Entry)`,
which C# syntax cannot compile because the nested type is private. No visibility
relaxation, dynamic assembly, runtime emission or scanner modification is used.

```powershell
$tokens = "out/capability-migration-probe/LiteralTypeTokens.dll"
# Use a new output path on each run; the generator refuses to overwrite files.
dotnet run -c Release --project tests/CapabilityMigrationProbe/LiteralTokens -- $game $tokens
dotnet run -c Release --project $scanner -- $game $tokens tokens
dotnet build tests/CapabilityMigrationProbe/MonoFixture -c Release
dotnet run -c Release --project tests/CapabilityMigrationProbe/MonoHost -- $game `
  tests/CapabilityMigrationProbe/MonoFixture/bin/Release/netstandard2.1/MonoFixture.dll $tokens
```

The token scan must be clean. MonoHost is a Windows x64 offline subprocess that
embeds the game's own Mono and loads only the fixture plus generated token
assembly, using fresh in-memory collections. It does not launch the game or
access saves. Its native calls and the fixture's dynamic reflection belong only
to test tools and MUST NOT be included in a mod package.

The target-specific token assembly is not a .NET 8 fixture: the host's private
BCL nested types do not resolve identically through mscorlib forwarding. Actual
game-Mono execution is required for this test. Passing it proves the narrow
Entry/Slot access path, not complete static schema coverage or Unity UI safety.
