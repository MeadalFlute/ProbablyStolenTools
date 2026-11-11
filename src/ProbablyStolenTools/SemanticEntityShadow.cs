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
        private readonly int _items, _grids, _slots, _shapes, _unsupported, _legacySlots, _projectedSlots;
        private SemanticEntityShadow(SemanticEntitySnapshot snapshot, int items, int grids, int slots,
            int shapes, int unsupported, int legacySlots, int projectedSlots)
        {
            _snapshot = snapshot; _items = items; _grids = grids; _slots = slots;
            _shapes = shapes; _unsupported = unsupported; _legacySlots = legacySlots; _projectedSlots = projectedSlots;
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
            var gridSchema = SemanticEntitySnapshot.Schema(typeof(GameGridInventory), false, ("items", typeof(List<GameItem>)));
            var slotSchema = SemanticEntitySnapshot.Schema(typeof(GameSlotInventory), false, ("currentItem", typeof(GameItem)));
            var shapeSchema = SemanticEntitySnapshot.Schema(typeof(GridShapeBuilder), true,
                ("data", typeof(byte[])), ("outside", typeof(byte)), ("<minX>k__BackingField", typeof(int)),
                ("<minY>k__BackingField", typeof(int)), ("<flipped>k__BackingField", typeof(bool)),
                ("<orientation>k__BackingField", typeof(int)), ("<width>k__BackingField", typeof(int)), ("<height>k__BackingField", typeof(int)));
            var projections = new List<SemanticEntityProjection>();
            Dictionary<string, (bool Reference, ulong Bit)> Index(FieldInfo[] fields)
            {
                if (fields.Length > 63) throw new InvalidOperationException("Entity projection field mask budget exceeded.");
                return fields.Select((field, i) => (field, i)).ToDictionary(
                    pair => pair.field.DeclaringType.FullName + "." + pair.field.Name,
                    pair => (!pair.field.FieldType.IsValueType, 1UL << pair.i), StringComparer.Ordinal);
            }
            var indexes = new Dictionary<FieldInfo[], Dictionary<string, (bool Reference, ulong Bit)>>
            {
                [itemSchema] = Index(itemSchema), [gridSchema] = Index(gridSchema),
                [slotSchema] = Index(slotSchema), [shapeSchema] = Index(shapeSchema)
            };
            var expected = new Dictionary<IntPtr, (Dictionary<string, (bool Reference, ulong Bit)> Fields, ulong Seen)>();
            int items = 0, grids = 0, slots = 0, shapes = 0, unsupported = 0, legacySlots = 0, projectedSlots = 0;
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
                else if (target is GameItem || target is GameInventory || target is GridShape) unsupported++;
                if (fields == null) continue;
                if (projections.Count >= maxObjects) throw new InvalidOperationException("Entity projection index budget exceeded.");
                indexedSlots += fields.Length;
                if (indexedSlots > maxSlots) throw new InvalidOperationException("Entity projection slot index budget exceeded.");
                projections.Add(new SemanticEntityProjection(target, fields));
                expected.Add(id, (indexes[fields], 0));
            }
            // Every projected field must exist as exactly one legacy slot. Other owner fields remain legacy-only.
            graph.VisitCapturedSlots((owner, slot, reference) =>
            {
                if (!expected.TryGetValue(owner, out var entry)) return;
                if (entry.Fields.TryGetValue(slot.Name, out var field))
                {
                    if (field.Reference != slot.IsReference || (entry.Seen & field.Bit) != 0)
                        throw new InvalidOperationException("Entity projection slot kind/uniqueness changed: " + slot.Name);
                    expected[owner] = (entry.Fields, entry.Seen | field.Bit);
                    projectedSlots++;
                }
                else legacySlots++;
            });
            if (expected.Values.Any(entry => entry.Seen != (1UL << entry.Fields.Count) - 1))
                throw new InvalidOperationException("Entity projection is not fully covered by the legacy checkpoint.");
            var snapshot = SemanticEntitySnapshot.Capture(projections, ManagedObjects.Id, graph.Contains, maxObjects, maxSlots, maxBytes);
            try
            {
                if (snapshot.Differences(out var sample) != 0)
                    throw new InvalidOperationException("Entity projection changed during capture: " + string.Join(", ", sample));
                return new SemanticEntityShadow(snapshot, items, grids, slots, shapes, unsupported, legacySlots, projectedSlots);
            }
            catch { snapshot.Dispose(); throw; }
        }

        internal string Summary => "scope=item-base+inventory-members+exact-shapes extraFields=legacy-only " +
            $"pruningAllowed=false restoreAllowed=false records={_snapshot.RecordCount} items={_items} grid={_grids} slot={_slots} " +
            $"shapes={_shapes} unsupported={_unsupported} sequences={_snapshot.SequenceCount} " +
            $"projectedOwnerSlots={_projectedSlots} legacyOwnerSlots={_legacySlots} " +
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
