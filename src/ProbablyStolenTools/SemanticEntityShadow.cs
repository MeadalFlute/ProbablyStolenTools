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

        private static bool Native(Type type, Type baseType)
        {
            for (var current = type; current != baseType; current = current.BaseType)
                if (current == null || current.Assembly != baseType.Assembly) return false;
            return true;
        }

        internal static SemanticEntityShadow Capture(StateGraph graph, int maxObjects, int maxSlots, long maxBytes)
        {
            var itemFields = new List<(string, Type)>
            {
                ("itemFeatures", typeof(List<ItemFeature>)), ("itemTypes", typeof(List<string>))
            };
            void Properties(Type type, params string[] names)
            {
                foreach (string name in names) itemFields.Add(("<" + name + ">k__BackingField", type));
            }
            Properties(typeof(GameInventory), "parentInventory");
            Properties(typeof(GridShape), "shape", "modifiedShape");
            Properties(typeof(TagSystem), "state", "modifiedState");
            Properties(typeof(int), "modifiedXOrigin", "modifiedYOrigin", "uniqueId", "unitCount", "bonusAccuracy");
            Properties(typeof(long), "unitValue", "lateUnitValue", "unitBaseValue", "backupUnitValue");
            Properties(typeof(string), "identifier", "identifierName", "name", "shortDescription", "longDescription",
                "flavorText", "customText", "spritePath", "spriteAtlasPath", "soundDragStart", "soundDragEnd",
                "soundContainerOpen", "soundContainerClose", "soundSelect", "soundExamineOpen", "soundToggle", "soundActivate", "soundUse");
            Properties(typeof(bool), "forceDisableActivate", "forceDisableUse", "activateDefault", "isCombatBackpack",
                "spriteChanged", "useSpriteFromMod", "isDebugMenu", "triggerOverwatch", "canUseOutsideCombat");
            Properties(typeof(RecipeManager), "recipeManager");
            Properties(typeof(PixelWindow), "contentWindow", "examineWindow");
            Properties(typeof(List<CombatAbilityEffect>), "effects");
            var itemSchema = SemanticEntitySnapshot.Schema(typeof(GameItem), true, itemFields.ToArray());
            var plans = SemanticInventorySchemas.Create().ToDictionary(plan => plan.Type);
            SemanticEntityFieldPlan[] Chain(Type type)
                => type == typeof(PixelWindow) ? new[] { plans[type] } :
                    new[] { plans[type], plans[typeof(GameInventory)], plans[typeof(GameInventoryFunc)], plans[typeof(TagSystem)] };
            var gridPlans = Chain(typeof(GameGridInventory));
            var slotPlans = Chain(typeof(GameSlotInventory));
            var windowPlans = Chain(typeof(PixelWindow));
            FieldInfo[] EntityFields(SemanticEntityFieldPlan[] chain) => chain.SelectMany(plan => plan.EntityFields).ToArray();
            var gridSchema = EntityFields(gridPlans);
            var slotSchema = EntityFields(slotPlans);
            var windowSchema = EntityFields(windowPlans);
            var shapeSchema = SemanticEntitySnapshot.Schema(typeof(GridShapeBuilder), true,
                ("data", typeof(byte[])), ("outside", typeof(byte)), ("<minX>k__BackingField", typeof(int)),
                ("<minY>k__BackingField", typeof(int)), ("<flipped>k__BackingField", typeof(bool)),
                ("<orientation>k__BackingField", typeof(int)), ("<width>k__BackingField", typeof(int)), ("<height>k__BackingField", typeof(int)));
            var projections = new List<SemanticEntityProjection>();
            Dictionary<string, (bool Reference, ulong Bit, SemanticFieldRole Role)> Index(
                FieldInfo[] fields, SemanticEntityFieldPlan[] chain = null)
            {
                var result = new Dictionary<string, (bool, ulong, SemanticFieldRole)>(StringComparer.Ordinal);
                int bit = 0;
                foreach (var field in chain == null ? fields : chain.SelectMany(plan => plan.Fields))
                {
                    string name = field.DeclaringType.FullName + "." + field.Name;
                    var role = chain == null ? SemanticFieldRole.Entity : plans[field.DeclaringType].RoleOf(name);
                    ulong mask = 0;
                    if (role != SemanticFieldRole.Visual)
                    {
                        if (bit >= 63) throw new InvalidOperationException("Entity projection field mask budget exceeded.");
                        mask = 1UL << bit++;
                    }
                    result.Add(name, (!field.FieldType.IsValueType, mask, role));
                }
                return result;
            }
            var indexes = new Dictionary<FieldInfo[], Dictionary<string, (bool Reference, ulong Bit, SemanticFieldRole Role)>>
            {
                [itemSchema] = Index(itemSchema), [gridSchema] = Index(gridSchema, gridPlans),
                [slotSchema] = Index(slotSchema, slotPlans), [shapeSchema] = Index(shapeSchema),
                [windowSchema] = Index(windowSchema, windowPlans)
            };
            var expected = new Dictionary<IntPtr, (Dictionary<string, (bool Reference, ulong Bit, SemanticFieldRole Role)> Fields,
                ulong Seen, ulong Required, bool Strict)>();
            int items = 0, grids = 0, slots = 0, shapes = 0, windows = 0, unsupported = 0, legacySlots = 0, projectedSlots = 0;
            int runtimeSlots = 0, visualFields = 0;
            long indexedSlots = 0;
            foreach (var id in graph.Objects)
            {
                var target = ManagedObjects.Get(id);
                var type = target.GetType();
                FieldInfo[] fields = null;
                if (target is GameItem && Native(type, typeof(GameItem))) { fields = itemSchema; items++; }
                else if (type == typeof(GameGridInventory)) { fields = gridSchema; grids++; }
                else if (type == typeof(GameSlotInventory)) { fields = slotSchema; slots++; }
                else if (type == typeof(GridShapeBuilder)) { fields = shapeSchema; shapes++; }
                else if (type == typeof(PixelWindow)) { fields = windowSchema; windows++; }
                else if (target is GameItem || target is GameInventory || target is GridShape || target is PixelWindow) unsupported++;
                if (fields == null) continue;
                if (projections.Count >= maxObjects) throw new InvalidOperationException("Entity projection index budget exceeded.");
                var index = indexes[fields];
                indexedSlots += index.Count;
                if (indexedSlots > maxSlots) throw new InvalidOperationException("Entity projection slot index budget exceeded.");
                projections.Add(new SemanticEntityProjection(target, fields));
                ulong required = 0;
                foreach (var field in index.Values)
                {
                    required |= field.Bit;
                    if (field.Role == SemanticFieldRole.Visual) visualFields++;
                }
                expected.Add(id, (index, 0, required, !(target is GameItem)));
            }
            // Exact inventory/window hierarchies have no unclassified slots; runtime fields stay in the legacy graph.
            graph.VisitCapturedSlots((owner, slot, reference) =>
            {
                if (!expected.TryGetValue(owner, out var entry)) return;
                if (entry.Fields.TryGetValue(slot.Name, out var field))
                {
                    if (field.Role == SemanticFieldRole.Visual || field.Reference != slot.IsReference || (entry.Seen & field.Bit) != 0)
                        throw new InvalidOperationException("Entity projection slot kind/uniqueness changed: " + slot.Name);
                    expected[owner] = (entry.Fields, entry.Seen | field.Bit, entry.Required, entry.Strict);
                    if (field.Role == SemanticFieldRole.Entity) projectedSlots++;
                    else { runtimeSlots++; legacySlots++; }
                }
                else if (entry.Strict) throw new InvalidOperationException("Unclassified entity owner slot: " + slot.Name);
                else legacySlots++;
            });
            if (expected.Values.Any(entry => entry.Seen != entry.Required))
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

        internal string Summary => "scope=item-base+inventory-fields+window-links+exact-shapes extraFields=legacy-only " +
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
