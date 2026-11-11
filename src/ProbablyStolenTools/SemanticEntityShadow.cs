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
            $"pruningAllowed=false restoreAllowed=false records={_snapshot.RecordCount} items={_items} grid={_grids} slot={_slots} " +
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
        public void Dispose() { _runtime.Dispose(); _boundary.Dispose(); _snapshot.Dispose(); }
    }
}
#endif
