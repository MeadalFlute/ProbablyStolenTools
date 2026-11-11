#if MONO
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace ProbablyStolenTools
{
    internal sealed class SemanticEntityShadow : IDisposable
    {
        private readonly SemanticEntitySnapshot _snapshot;
        private readonly SemanticEntityBoundaryPlan _boundary;
        private readonly SemanticRuntimeSnapshot _runtime;
        private readonly long _runtimeCaptureMs;
        private SemanticShapeWriter _shapeWriter;
        private SemanticWindowWriter _windowWriter;
        private SemanticItemWriter _itemWriter;
        private SemanticInventoryWriter _gridWriter, _slotWriter;
        private readonly Dictionary<int, IStateEntityWriter[]> _writers = new();
        private readonly Dictionary<int, StateGraph.EntityRestorePlan> _restorePlans = new();
        internal int ShapePilotOwners => _shapeWriter?.Owners.Count ?? 0;
        internal int WindowPilotOwners => _windowWriter?.Owners.Count ?? 0;
        internal int ItemPilotOwners => _itemWriter?.Owners.Count ?? 0;
        internal int GridPilotOwners => _gridWriter?.Owners.Count ?? 0;
        internal int SlotPilotOwners => _slotWriter?.Owners.Count ?? 0;
        internal int InventoryPilotOwners => GridPilotOwners + SlotPilotOwners;
        private long PilotSlots => ShapePilotOwners * 8L + (_windowWriter?.LogicalSlots ?? 0) + (_itemWriter?.LogicalSlots ?? 0)
            + (_gridWriter?.LogicalSlots ?? 0) + (_slotWriter?.LogicalSlots ?? 0);
        private long PilotBytes => ShapePilotOwners * (7 * 8L + IntPtr.Size)
            + (_windowWriter?.LogicalBytes ?? 0) + (_itemWriter?.LogicalBytes ?? 0)
            + (_gridWriter?.LogicalBytes ?? 0) + (_slotWriter?.LogicalBytes ?? 0);
        private bool RegisterWriters(StateGraph graph, int bit, IStateEntityWriter[] writers, int maxObjects, int maxSlots)
        {
            if (_writers.ContainsKey(bit)) throw new InvalidOperationException("Entity writer already prepared.");
            var admitted = writers.Where(writer => writer.Owners.Count != 0).ToArray();
            if (admitted.Length == 0) return false;
            var pending = new Dictionary<int, StateGraph.EntityRestorePlan>();
            foreach (int mask in _restorePlans.Keys.Concat(new[] { 0 }))
            {
                var group = _writers.OrderBy(pair => pair.Key).Where(pair => (pair.Key & mask) != 0)
                    .SelectMany(pair => pair.Value).Concat(admitted);
                pending.Add(mask | bit, graph.PrepareEntityRestores(group, maxObjects, maxSlots));
            }
            // Publish only after every combination passes; a failed candidate cannot damage existing plans.
            _writers.Add(bit, admitted);
            foreach (var pair in pending) _restorePlans.Add(pair.Key, pair.Value);
            return true;
        }
        internal void EnableShapeRestore(StateGraph graph, int maxObjects, int maxSlots, long maxBytes)
        {
            if (_shapeWriter != null) throw new InvalidOperationException("Shape writer already prepared.");
            var writer = SemanticShapeWriter.Capture(graph, maxObjects,
                checked((int)(maxSlots - _snapshot.LogicalSlots - _runtime.SlotCount - PilotSlots)),
                maxBytes - _snapshot.LogicalBytes - _runtime.ByteCount - PilotBytes);
            try
            {
                if (!RegisterWriters(graph, 1, new IStateEntityWriter[] { writer }, maxObjects, maxSlots)) { writer.Dispose(); return; }
                _shapeWriter = writer;
            }
            catch { writer.Dispose(); throw; }
        }
        internal bool TryRestoreShapes(StateGraph graph)
        {
            return TryRestoreEntities(graph, true, false, false, false);
        }
        internal void EnableWindowRestore(StateGraph graph, int maxObjects, int maxSlots, long maxBytes)
        {
            if (_windowWriter != null) throw new InvalidOperationException("Window writer already prepared.");
            var writer = SemanticWindowWriter.Capture(graph, maxObjects,
                checked((int)(maxSlots - _snapshot.LogicalSlots - _runtime.SlotCount - PilotSlots)),
                maxBytes - _snapshot.LogicalBytes - _runtime.ByteCount - PilotBytes);
            try
            {
                if (!RegisterWriters(graph, 2, new IStateEntityWriter[] { writer }, maxObjects, maxSlots)) { writer.Dispose(); return; }
                _windowWriter = writer;
            }
            catch { writer.Dispose(); throw; }
        }
        internal void EnableItemRestore(StateGraph graph, int maxObjects, int maxSlots, long maxBytes)
        {
            if (_itemWriter != null) throw new InvalidOperationException("Item writer already prepared.");
            var writer = SemanticItemWriter.Capture(graph, maxObjects,
                checked((int)(maxSlots - _snapshot.LogicalSlots - _runtime.SlotCount - PilotSlots)),
                maxBytes - _snapshot.LogicalBytes - _runtime.ByteCount - PilotBytes);
            try
            {
                if (!RegisterWriters(graph, 4, new IStateEntityWriter[] { writer }, maxObjects, maxSlots)) { writer.Dispose(); return; }
                _itemWriter = writer;
            }
            catch { writer.Dispose(); throw; }
        }
        internal void EnableInventoryRestore(StateGraph graph, int maxObjects, int maxSlots, long maxBytes)
        {
            if (_gridWriter != null || _slotWriter != null)
                throw new InvalidOperationException("Inventory writers already prepared.");
            SemanticInventoryWriter grid = null, slot = null;
            try
            {
                int remainingSlots = checked((int)(maxSlots - _snapshot.LogicalSlots - _runtime.SlotCount - PilotSlots));
                long remainingBytes = maxBytes - _snapshot.LogicalBytes - _runtime.ByteCount - PilotBytes;
                grid = SemanticInventoryWriter.Capture(graph, typeof(GameGridInventory), maxObjects, remainingSlots, remainingBytes);
                slot = SemanticInventoryWriter.Capture(graph, typeof(GameSlotInventory), maxObjects - grid.Owners.Count,
                    remainingSlots - grid.LogicalSlots, remainingBytes - grid.LogicalBytes);
                if (!RegisterWriters(graph, 8, new IStateEntityWriter[] { grid, slot }, maxObjects, maxSlots))
                { grid.Dispose(); slot.Dispose(); return; }
                if (grid.Owners.Count == 0) grid.Dispose(); else _gridWriter = grid;
                if (slot.Owners.Count == 0) slot.Dispose(); else _slotWriter = slot;
            }
            catch { slot?.Dispose(); grid?.Dispose(); throw; }
        }
        internal bool TryRestoreEntities(StateGraph graph, bool shapes, bool windows, bool items, bool inventories)
        {
            int mask = (shapes && _shapeWriter != null ? 1 : 0) | (windows && _windowWriter != null ? 2 : 0)
                | (items && _itemWriter != null ? 4 : 0)
                | (inventories && (_gridWriter != null || _slotWriter != null) ? 8 : 0);
            if (!_restorePlans.TryGetValue(mask, out var plan)) return false;
            plan.Apply(graph);
            return true;
        }
        private readonly int _items, _grids, _slots, _shapes, _windows, _unsupported, _legacySlots, _projectedSlots;
        private readonly int _runtimeSlots, _visualFields;
        private SemanticEntityShadow(SemanticEntitySnapshot snapshot, SemanticEntityBoundaryPlan boundary,
            SemanticRuntimeSnapshot runtime, long runtimeCaptureMs, int items, int grids, int slots,
            int shapes, int windows, int unsupported, int legacySlots, int projectedSlots, int runtimeSlots, int visualFields)
        {
            _snapshot = snapshot; _boundary = boundary; _runtime = runtime; _runtimeCaptureMs = runtimeCaptureMs;
            _items = items; _grids = grids; _slots = slots;
            _shapes = shapes; _windows = windows; _unsupported = unsupported; _legacySlots = legacySlots; _projectedSlots = projectedSlots;
            _runtimeSlots = runtimeSlots; _visualFields = visualFields;
        }

        internal static SemanticEntityShadow Capture(StateGraph graph, int maxObjects, int maxSlots, long maxBytes)
        {
            var plans = SemanticInventorySchemas.Create().Concat(SemanticItemSchemas.Create()).ToDictionary(plan => plan.Type);
            SemanticEntityFieldPlan[] Chain(Type type)
                => type == typeof(PixelWindow) ? new[] { plans[type] } :
                    new[] { plans[type], plans[typeof(GameInventory)], plans[typeof(GameInventoryFunc)], plans[typeof(TagSystem)] };
            var gridPlans = Chain(typeof(GameGridInventory));
            var slotPlans = Chain(typeof(GameSlotInventory));
            var windowPlans = Chain(typeof(PixelWindow));
            var itemPlans = new[] { plans[typeof(GameItemElement)], plans[typeof(GameItem)], plans[typeof(GameItemFunc)] };
            FieldInfo[] EntityFields(SemanticEntityFieldPlan[] chain) => chain.SelectMany(plan => plan.EntityFields).ToArray();
            var itemSchema = EntityFields(itemPlans);
            var gridSchema = EntityFields(gridPlans);
            var slotSchema = EntityFields(slotPlans);
            var windowSchema = EntityFields(windowPlans);
            var shapeSchema = SemanticEntitySnapshot.Schema(typeof(GridShapeBuilder), true,
                ("data", typeof(byte[])), ("outside", typeof(byte)), ("<minX>k__BackingField", typeof(int)),
                ("<minY>k__BackingField", typeof(int)), ("<flipped>k__BackingField", typeof(bool)),
                ("<orientation>k__BackingField", typeof(int)), ("<width>k__BackingField", typeof(int)), ("<height>k__BackingField", typeof(int)));
            var projections = new List<SemanticEntityProjection>();
            SemanticSlotSchema Index(
                FieldInfo[] fields, SemanticEntityFieldPlan[] chain = null)
            {
                return new SemanticSlotSchema((chain == null ? fields : chain.SelectMany(plan => plan.Fields)).Select(field =>
                {
                    string name = field.DeclaringType.FullName + "." + field.Name;
                    var role = chain == null ? SemanticFieldRole.Entity : plans[field.DeclaringType].RoleOf(name);
                    return (name, !field.FieldType.IsValueType, role);
                }));
            }
            var indexes = new Dictionary<FieldInfo[], SemanticSlotSchema>
            {
                [itemSchema] = Index(itemSchema, itemPlans), [gridSchema] = Index(gridSchema, gridPlans),
                [slotSchema] = Index(slotSchema, slotPlans), [shapeSchema] = Index(shapeSchema),
                [windowSchema] = Index(windowSchema, windowPlans)
            };
            int items = 0, grids = 0, slots = 0, shapes = 0, windows = 0, unsupported = 0;
            int visualFields = 0;
            long indexedSlots = 0;
            foreach (var id in graph.Objects)
            {
                var target = ManagedObjects.Get(id);
                var type = target.GetType();
                FieldInfo[] fields = null;
                if (type == typeof(GameItemElement)) { fields = itemSchema; items++; }
                else if (type == typeof(GameGridInventory)) { fields = gridSchema; grids++; }
                else if (type == typeof(GameSlotInventory)) { fields = slotSchema; slots++; }
                else if (type == typeof(GridShapeBuilder)) { fields = shapeSchema; shapes++; }
                else if (type == typeof(PixelWindow)) { fields = windowSchema; windows++; }
                else if (target is GameItem || target is GameInventory || target is GridShape || target is PixelWindow) unsupported++;
                if (fields == null) continue;
                if (projections.Count >= maxObjects) throw new InvalidOperationException("Entity projection index budget exceeded.");
                var index = indexes[fields];
                indexedSlots += index.FieldCount;
                if (indexedSlots > maxSlots) throw new InvalidOperationException("Entity projection slot index budget exceeded.");
                projections.Add(new SemanticEntityProjection(target, fields));
                visualFields += index.VisualCount;
            }
            var boundary = SemanticEntityBoundaryPlan.Capture(graph,
                projections.Select(projection => (ManagedObjects.Id(projection.Target), indexes[projection.Fields])),
                ManagedObjects.Get, ManagedObjects.Id, maxObjects, maxSlots);
            SemanticEntitySnapshot snapshot = null;
            SemanticRuntimeSnapshot runtime = null;
            try
            {
                snapshot = SemanticEntitySnapshot.Capture(projections, ManagedObjects.Id, graph.Contains, maxObjects, maxSlots, maxBytes);
                if (snapshot.Differences(out var sample) != 0)
                    throw new InvalidOperationException("Entity projection changed during capture: " + string.Join(", ", sample));
                var runtimeClock = Stopwatch.StartNew();
                runtime = SemanticRuntimeSnapshot.Capture(graph, boundary, maxObjects,
                    maxSlots - snapshot.LogicalSlots, maxBytes - snapshot.LogicalBytes);
                return new SemanticEntityShadow(snapshot, boundary, runtime, runtimeClock.ElapsedMilliseconds,
                    items, grids, slots, shapes, windows, unsupported,
                    boundary.EntityRuntimeSlots, boundary.EntitySlots, boundary.EntityRuntimeSlots, visualFields);
            }
            catch { runtime?.Dispose(); boundary.Dispose(); snapshot?.Dispose(); throw; }
        }

        internal string Summary => "scope=exact-item-hierarchy+inventory-fields+window-links+exact-shapes unknownTypes=legacy-only " +
            $"pruningAllowed=false recordRestoreAllowed=false runtimeRestoreAllowed=false records={_snapshot.RecordCount} items={_items} grid={_grids} slot={_slots} " +
            $"shapes={_shapes} windows={_windows} unsupported={_unsupported} sequences={_snapshot.SequenceCount} " +
            $"projectedOwnerSlots={_projectedSlots} legacyOwnerSlots={_legacySlots} " +
            $"classifiedRuntimeSlots={_runtimeSlots} excludedVisualFields={_visualFields} " +
            $"logicalSlots={_snapshot.LogicalSlots} logicalBytes={_snapshot.LogicalBytes} " +
            $"handles={_boundary.EntityCount} rootHandles={_boundary.RootEntities} " +
            $"runtimeHandleLinks={_boundary.RuntimeHandleLinks} entityHandleLinks={_boundary.EntityHandleLinks} " +
            $"partition(entity={_boundary.EntitySlots},runtime={_boundary.RuntimeSlots},total={_boundary.TotalSlots}) " +
            $"runtimeSeeds={_boundary.RuntimeSeedCount} runtimeReachable={_boundary.RuntimeReachableObjects} " +
            "runtimeCut=readonly-shadow productionRuntimeCut=false " +
            $"runtimePayload=independent-readonly runtimeObjects={_runtime.ObjectCount} runtimeSlots={_runtime.SlotCount} " +
            $"runtimeBytes={_runtime.ByteCount} runtimeBlocks={_runtime.BlockCount} runtimeBoundHandles={_runtime.BoundEntities} " +
            $"runtimeCaptureVerifyMs={_runtimeCaptureMs} combinedSlots={_snapshot.LogicalSlots + _runtime.SlotCount} " +
            $"combinedBytes={_snapshot.LogicalBytes + _runtime.ByteCount} " +
            $"shapePilotOwners={ShapePilotOwners} shapePilotSlots={ShapePilotOwners * 8L} " +
            $"windowPilotOwners={WindowPilotOwners} windowPilotSlots={_windowWriter?.LogicalSlots ?? 0} " +
            $"itemPilotOwners={ItemPilotOwners} itemPilotSlots={_itemWriter?.LogicalSlots ?? 0} " +
            $"inventoryPilotOwners={InventoryPilotOwners} gridPilotOwners={GridPilotOwners} slotPilotOwners={SlotPilotOwners} " +
            $"inventoryPilotSlots={(_gridWriter?.LogicalSlots ?? 0) + (_slotWriter?.LogicalSlots ?? 0)} " +
            $"pilotCombinedSlots={_snapshot.LogicalSlots + _runtime.SlotCount + PilotSlots} " +
            $"pilotCombinedBytes={_snapshot.LogicalBytes + _runtime.ByteCount + PilotBytes} " +
            "collectionStorage=legacy-only callbacks=legacy-only tags=legacy-hybrid";

        internal void ObserveRestore(long id)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                _boundary.Validate(ManagedObjects.Get);
                int count = _snapshot.Differences(out var sample);
                long entityVerifyMs = clock.ElapsedMilliseconds;
                var runtimeDiff = _runtime.Differences();
                EventLog.Write($"[SemanticEntities] RECORD RESTORE {(count == 0 && runtimeDiff.Count == 0 ? "MATCH" : "VALIDATION FAILED")} " +
                    $"id={id} readOnly=true diff={count} runtimeDiffSamples={runtimeDiff.Count} verifyMs={clock.ElapsedMilliseconds} " +
                    $"entityVerifyMs={entityVerifyMs} runtimeVerifyMs={clock.ElapsedMilliseconds - entityVerifyMs} {Summary}" +
                    (count == 0 ? "" : " fields=" + string.Join(", ", sample)) +
                    (runtimeDiff.Count == 0 ? "" : " runtimeFields=" + string.Join(", ", runtimeDiff)));
            }
            catch (Exception e)
            {
                EventLog.Write($"[SemanticEntities] RECORD RESTORE VALIDATION FAILED id={id} readOnly=true " +
                    $"verifyMs={clock.ElapsedMilliseconds}: " + e);
            }
        }
        public void Dispose()
        {
            _restorePlans.Clear(); _writers.Clear();
            _shapeWriter?.Dispose(); _shapeWriter = null;
            _windowWriter?.Dispose(); _windowWriter = null;
            _itemWriter?.Dispose(); _itemWriter = null;
            _gridWriter?.Dispose(); _gridWriter = null;
            _slotWriter?.Dispose(); _slotWriter = null;
            _runtime.Dispose(); _boundary.Dispose(); _snapshot.Dispose();
        }
    }
}
#endif
