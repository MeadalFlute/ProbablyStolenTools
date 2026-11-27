## What changed

## Why

## Testing
- [ ] `dotnet run --project tests\CheckpointTests -c Release` passes
- [ ] `dotnet run --project tests\OptionalModCompatibilityTests -c Release` passes
- [ ] `.\scripts\Test-SplitMono.ps1 -GameDir "<game folder>" -QuickOnly` passes
- [ ] `.\scripts\Test-SplitMono.ps1 -GameDir "<game folder>" -ToolboxDll "<ShiftToolbox.dll>"` passes when the shared protocol changed
- [ ] Verified in game (describe what was tested)

## Boundaries
- [ ] No new capability, player-save access or shared prerequisite
- [ ] Unknown game layouts still fail closed instead of dropping fields
