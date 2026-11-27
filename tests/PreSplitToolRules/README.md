# Pre-split Reference Fixtures

Frozen utility rule sources from QuickSL 0.9.9 (`5726538`), retained so the existing
reference suite and optional-mod fixture remain runnable without a sibling repo.
These files are not compiled into the production QuickSL DLL and are not current
Shift Toolbox coverage. Do not implement utility features here.

Current utility code and tests live in the independent ShiftToolbox repository.
When removing historical fixture tests, remove their source includes together.
