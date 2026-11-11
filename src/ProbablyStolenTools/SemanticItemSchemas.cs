#if MONO
using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    internal static class SemanticItemSchemas
    {
        internal static SemanticEntityFieldPlan[] Create()
        {
            var fields = new List<SemanticFieldRule>
            {
                new("itemFeatures", typeof(List<ItemFeature>), SemanticFieldRole.Entity),
                new("itemTypes", typeof(List<string>), SemanticFieldRole.Entity)
            };
            void Properties(Type type, params string[] names)
            {
                foreach (string name in names)
                    fields.Add(new("<" + name + ">k__BackingField", type, SemanticFieldRole.Entity));
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
            var item = new SemanticEntityFieldPlan(typeof(GameItem), fields.ToArray());

            var element = new List<SemanticFieldRule>();
            void Element(Type type, SemanticFieldRole role, params string[] names)
            {
                foreach (string name in names) element.Add(new(name, type, role));
            }
            Element(typeof(TreeNodeRender), SemanticFieldRole.Visual, "stackCountText", "stackCountTextBackground",
                "fakeStackCountText", "fakeStackCountTextBackground", "markOverlayNode", "fakeMarkOverlayNode",
                "<handler>k__BackingField", "<inner>k__BackingField", "<fakeInner>k__BackingField",
                "<middle>k__BackingField", "<rotator>k__BackingField", "<background>k__BackingField",
                "<fakeMiddle>k__BackingField", "<fakeRotator>k__BackingField", "<fakeBackground>k__BackingField");
            Element(typeof(List<TreeNodeRender>), SemanticFieldRole.Visual, "pinOverlayNodes", "fakePinOverlayNodes",
                "spriteMods", "fakeSpriteMods", "innateSpriteMods", "innateFakeSpriteMods");
            Element(typeof(TreeNodeRender[]), SemanticFieldRole.Visual, "playingFrameNodes", "fakePlayingFrameNodes");
            // These layout caches must remain outside the business graph to force geometry refresh.
            Element(typeof(int), SemanticFieldRole.Visual, "appliedMinX", "appliedMinY", "appliedOrientation", "appliedGridSize");
            Element(typeof(bool), SemanticFieldRole.Visual, "appliedFlipped", "validateLock");
            Element(typeof(bool), SemanticFieldRole.Entity, "fakePinOverlayVisible", "markHiddenWithBackground", "destroyed");
            Element(typeof(GridShapeBuilder), SemanticFieldRole.Entity, "<tempShape>k__BackingField");

            var callbacks = new List<SemanticFieldRule>();
            void Callbacks(Type type, params string[] names)
            {
                foreach (string name in names)
                    callbacks.Add(new("<" + name + ">k__BackingField", type, SemanticFieldRole.Runtime));
            }
            Callbacks(typeof(Func<GameItem, GameInventory, bool>), "mayInventoryAddItemFunc", "mayInventoryRemoveItemFunc");
            Callbacks(typeof(Func<GameItem, GameItem, bool>), "mayStackItemFunc", "mayItemTargetItemFunc", "canItemTargetItemFunc",
                "mayThisTargetItemFunc", "canThisTargetItemFunc", "mayItemTargetThisFunc", "canItemTargetThisFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, int>), "maxSlotAddItemFunc", "maxSlotRemoveItemFunc",
                "maxSlotAmountItemFunc", "minSlotAmountItemFunc", "slotPriorityFunc");
            Callbacks(typeof(Action<GameItem, GameInventory, SlotMarker>), "onSlotAddItemFunc", "onSlotRemoveItemFunc",
                "onSlotChangeAmountItemFunc", "onActivateSlotItemFunc", "onToggleSlotItemFunc", "onConfigureSlotItemFunc",
                "onCycleEndEarlySlotItemFunc", "onCycleEndSlotItemFunc", "onCycleEndLateSlotItemFunc", "onTurnEndSlotItemFunc", "onOneSecondItemFunc");
            Callbacks(typeof(Action<GameItem>), "onChangeAmountItemFunc", "onLoaded", "onUnlockedFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, GameItem, bool>), "slotSearchPassthroughForItemFunc",
                "maySlotItemTargetItemFunc", "canSlotItemTargetItemFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, GameItem, GridShape>), "parentItemShapeModifierForItemFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, GameItem, List<ItemSpriteModifier>>), "parentItemSpriteModifierForItemFunc");
            Callbacks(typeof(Func<GameItem, List<ItemSpriteModifier>>), "getItemSpriteModifierForItemFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, GameItem, List<ItemStateModifier>>), "parentItemStateModifierForItemFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, bool>), "isItemStateValidFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, RichTextBuilder>), "tooltipRichTextFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, bool>), "maySelectSlotItemFunc", "canSelectSlotItemFunc",
                "mayActivateSlotItemFunc", "canActivateSlotItemFunc", "mayToggleSlotItemFunc", "canToggleSlotItemFunc");
            Callbacks(typeof(Action<GameItem, GameItem>), "onItemTargetItemFunc", "onThisTargetItemFunc", "onItemTargetThisFunc", "onUnloadChildItemFunc");
            Callbacks(typeof(Action<GameItem, GameInventory, SlotMarker, GameItem>), "onSlotItemTargetItemFunc");
            Callbacks(typeof(Func<GameItem, GameItem, GameInventory, SlotMarker, bool>), "mayItemTargetSlotItemFunc", "canItemTargetSlotItemFunc");
            Callbacks(typeof(Action<GameItem, GameItem, GameInventory, SlotMarker>), "onItemTargetSlotItemFunc");
            return new[] { item, new SemanticEntityFieldPlan(typeof(GameItemElement), element.ToArray()),
                new SemanticEntityFieldPlan(typeof(GameItemFunc), callbacks.ToArray()) };
        }
    }
}
#endif
