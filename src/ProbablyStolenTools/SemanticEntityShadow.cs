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
        private readonly int _items, _grids, _slots, _shapes, _windows, _unsupported, _legacySlots, _projectedSlots;
        private readonly int _runtimeSlots, _visualFields;
        private SemanticEntityShadow(SemanticEntitySnapshot snapshot, int items, int grids, int slots,
            int shapes, int windows, int unsupported, int legacySlots, int projectedSlots, int runtimeSlots, int visualFields)
        {
            _snapshot = snapshot; _items = items; _grids = grids; _slots = slots;
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
            var expected = new Dictionary<IntPtr, SemanticSlotSchema.Coverage>();
            int items = 0, grids = 0, slots = 0, shapes = 0, windows = 0, unsupported = 0, legacySlots = 0, projectedSlots = 0;
            int runtimeSlots = 0, visualFields = 0;
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
                expected.Add(id, new SemanticSlotSchema.Coverage(index));
            }
            // Every selected hierarchy is exhaustive; no default partition for unknown fields or subclasses.
            graph.VisitCapturedSlots((owner, slot, reference) =>
            {
                if (!expected.TryGetValue(owner, out var entry)) return;
                var role = entry.Observe(slot.Name, slot.IsReference);
                if (role == SemanticFieldRole.Entity) projectedSlots++;
                else { runtimeSlots++; legacySlots++; }
            });
            if (expected.Values.Any(entry => !entry.Complete))
                throw new InvalidOperationException("Entity projection is not fully covered by the legacy checkpoint.");
            var snapshot = SemanticEntitySnapshot.Capture(projections, ManagedObjects.Id, graph.Contains, maxObjects, maxSlots, maxBytes);
            try
            {
                if (snapshot.Differences(out var sample) != 0)
                    throw new InvalidOperationException("Entity projection changed during capture: " + string.Join(", ", sample));
                return new SemanticEntityShadow(snapshot, items, grids, slots, shapes, windows, unsupported,
                    legacySlots, projectedSlots, runtimeSlots, visualFields);
            }
            catch { snapshot.Dispose(); throw; }
        }

        internal string Summary => "scope=exact-item-hierarchy+inventory-fields+window-links+exact-shapes unknownTypes=legacy-only " +
            $"pruningAllowed=false restoreAllowed=false records={_snapshot.RecordCount} items={_items} grid={_grids} slot={_slots} " +
            $"shapes={_shapes} windows={_windows} unsupported={_unsupported} sequences={_snapshot.SequenceCount} " +
            $"projectedOwnerSlots={_projectedSlots} legacyOwnerSlots={_legacySlots} " +
            $"classifiedRuntimeSlots={_runtimeSlots} excludedVisualFields={_visualFields} " +
            $"logicalSlots={_snapshot.LogicalSlots} logicalBytes={_snapshot.LogicalBytes} " +
            "collectionStorage=legacy-only callbacks=legacy-only tags=legacy-hybrid";

        internal void ObserveRestore(long id)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                int count = _snapshot.Differences(out var sample);
                EventLog.Write($"[SemanticEntities] RECORD RESTORE {(count == 0 ? "MATCH" : "VALIDATION FAILED")} " +
                    $"id={id} readOnly=true diff={count} verifyMs={clock.ElapsedMilliseconds} {Summary}" +
                    (count == 0 ? "" : " fields=" + string.Join(", ", sample)));
            }
            catch (Exception e)
            {
                EventLog.Write($"[SemanticEntities] RECORD RESTORE VALIDATION FAILED id={id} readOnly=true " +
                    $"verifyMs={clock.ElapsedMilliseconds}: " + e);
            }
        }
        public void Dispose() => _snapshot.Dispose();
    }
}
#endif
