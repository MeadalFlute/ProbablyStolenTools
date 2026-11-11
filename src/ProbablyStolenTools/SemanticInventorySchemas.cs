#if MONO
using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    internal static class SemanticInventorySchemas
    {
        private static SemanticFieldRule Entity(string name, Type type) => new(name, type, SemanticFieldRole.Entity);
        private static SemanticFieldRule Runtime(string name, Type type) => new(name, type, SemanticFieldRole.Runtime);
        private static SemanticFieldRule Visual(string name, Type type) => new(name, type, SemanticFieldRole.Visual);
        private static SemanticFieldRule Property(string name, Type type) => Entity("<" + name + ">k__BackingField", type);

        internal static SemanticEntityFieldPlan[] Create()
        {
            var inventory = new SemanticEntityFieldPlan(typeof(GameInventory),
                Entity("identifier", typeof(string)), Entity("overrideLockInsert", typeof(bool)),
                Entity("overrideLockRemove", typeof(bool)), Property("parent", typeof(GraphNodeStorage)));
            var grid = new SemanticEntityFieldPlan(typeof(GameGridInventory),
                Visual("dynamicBackgroundBase", typeof(TreeNodeRender)), Visual("background", typeof(TreeNodeRender)),
                Visual("insideBorder", typeof(TreeNodeRender)), Entity("renderDynamicBackground", typeof(bool)),
                Visual("dynamicBackgrounds", typeof(List<TreeNodeRender>)), Visual("dynamicR0", typeof(List<TreeNodeRender>)),
                Visual("dynamicR1", typeof(List<TreeNodeRender>)), Visual("dynamicR2", typeof(List<TreeNodeRender>)),
                Visual("dynamicR3", typeof(List<TreeNodeRender>)), Entity("items", typeof(List<GameItem>)),
                Visual("highlightSquares", typeof(List<TreeNodeRender>)),
                Visual("cellBackdropCells", typeof(Dictionary<(int, int), UnityEngine.Sprite>)),
                Visual("cellBackdrops", typeof(List<TreeNodeRender>)),
                Runtime("_lastShapeWidth", typeof(int)), Runtime("_lastShapeHeight", typeof(int)),
                Runtime("_lastShapeHash", typeof(int)), Runtime("_lastRenderDynamicBackground", typeof(bool)),
                Entity("hideBorder", typeof(bool)), Entity("machineryStyle", typeof(bool)),
                Visual("solidSprite", typeof(UnityEngine.Sprite)),
                Property("widthPixels", typeof(int)), Property("heightPixels", typeof(int)),
                Visual("<handler>k__BackingField", typeof(TreeNodeRender)), Visual("<inner>k__BackingField", typeof(TreeNodeRender)),
                Property("transparency", typeof(float)), Property("inventoryShape", typeof(GridShape)));
            var slot = new SemanticEntityFieldPlan(typeof(GameSlotInventory),
                Entity("stealUI", typeof(bool)), Entity("isUIDown", typeof(bool)), Entity("stolenUI", typeof(PixelElement)),
                Entity("defaultOrientation", typeof(int)), Visual("background", typeof(TreeNodeRender)),
                Visual("insideBorder", typeof(TreeNodeRender)), Visual("stolenInner", typeof(TreeNodeRender)),
                Visual("backgroundImage", typeof(TreeNodeRender)), Entity("currentItem", typeof(GameItem)),
                Visual("highlight", typeof(TreeNodeRender)), Entity("defaultGridWidth", typeof(int)),
                Entity("defaultGridHeight", typeof(int)), Entity("tooltipName", typeof(string)),
                Visual("cellNode", typeof(TreeNodeRender)), Runtime("validateLock", typeof(bool)),
                Property("widthPixels", typeof(int)), Property("heightPixels", typeof(int)),
                Visual("<handler>k__BackingField", typeof(TreeNodeRender)), Visual("<inner>k__BackingField", typeof(TreeNodeRender)),
                Property("transparency", typeof(float)), Property("isHidden", typeof(bool)));
            var window = new SemanticEntityFieldPlan(typeof(PixelWindow),
                Entity("canMove", typeof(bool)), Entity("style", typeof(WindowStyle)),
                Visual("machineryHeader", typeof(TreeNodeRender)), Visual("machineryRule", typeof(TreeNodeRender)),
                Visual("machineryRuleBacking", typeof(TreeNodeRender)), Visual("machineryPlate", typeof(TreeNodeRender)),
                Visual("machineryIntake", typeof(TreeNodeRender)), Runtime("headerButtons", typeof(List<MachineryButton>)),
                Entity("linkedContainerStorage", typeof(List<GraphNodeStorage>)),
                Visual("background", typeof(TreeNodeRender)), Visual("outsideBorder", typeof(TreeNodeRender)),
                Visual("topDragBorder", typeof(TreeNodeRender)), Visual("topCloseButton", typeof(TreeNodeRender)),
                Visual("topTitleBackground", typeof(TreeNodeRender)), Visual("bottomResizeButton", typeof(TreeNodeRender)),
                Visual("titleText", typeof(TreeNodeRender)), Entity("forceRaycast", typeof(bool)), Entity("hideBorder", typeof(bool)),
                Visual("preDockParent", typeof(UnityEngine.Transform)), Entity("isLocked", typeof(bool)),
                Property("parentItems", typeof(List<GameItem>)), Property("widthPixels", typeof(int)), Property("heightPixels", typeof(int)),
                Visual("<handler>k__BackingField", typeof(TreeNodeRender)), Visual("<inner>k__BackingField", typeof(TreeNodeRender)),
                Property("childElement", typeof(PixelElement)), Property("titleHeight", typeof(int)),
                Property("xPadding", typeof(int)), Property("yPadding", typeof(int)), Property("titleString", typeof(string)),
                Property("transparency", typeof(float)), Property("isImportant", typeof(bool)),
                Runtime("<closeCallback>k__BackingField", typeof(Action)), Runtime("<openCallback>k__BackingField", typeof(Action)),
                Runtime("<focusStamp>k__BackingField", typeof(long)), Runtime("<stateChangeActionFunc>k__BackingField", typeof(Action)));

            var callbacks = new List<SemanticFieldRule>();
            void Callbacks(Type type, params string[] names)
            {
                foreach (string name in names) callbacks.Add(Runtime("<" + name + ">k__BackingField", type));
            }
            Callbacks(typeof(Func<GameItem, GameInventory, bool>), "mayInventoryAddItemFunc", "mayInventoryRemoveItemFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, int>), "maxSlotAddItemFunc", "maxSlotRemoveItemFunc",
                "maxSlotAmountItemFunc", "minSlotAmountItemFunc", "slotPriorityFunc");
            Callbacks(typeof(Action<GameItem, GameInventory, SlotMarker>), "onSlotAddItemFunc", "onSlotAddItemEndFunc",
                "onSlotAddItemLateFunc", "onSlotRemoveItemFunc", "onSlotRemoveItemEndFunc", "onSlotRemoveItemLateFunc",
                "onSlotChangeAmountItemFunc", "onActivateSlotItemFunc", "onToggleSlotItemFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, GameItem, bool>),
                "slotSearchPassthroughForItemFunc", "maySlotItemTargetItemFunc", "canSlotItemTargetItemFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, GameItem, GridShape>), "parentItemShapeModifierForItemFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, GameItem, List<ItemSpriteModifier>>), "parentItemSpriteModifierForItemFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, GameItem, List<ItemStateModifier>>), "parentItemStateModifierForItemFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, GridShape, TagSystem, bool>), "isItemStateValidFunc");
            Callbacks(typeof(Func<GameItem, GameInventory, SlotMarker, bool>), "maySelectSlotItemFunc", "canSelectSlotItemFunc",
                "mayActivateSlotItemFunc", "canActivateSlotItemFunc", "mayToggleSlotItemFunc", "canToggleSlotItemFunc");
            Callbacks(typeof(Action<GameItem, GameInventory, SlotMarker, GameItem>), "onSlotItemTargetItemFunc");
            Callbacks(typeof(Func<GameItem, GameItem, GameInventory, SlotMarker, bool>), "mayItemTargetSlotItemFunc", "canItemTargetSlotItemFunc");
            Callbacks(typeof(Action<GameItem, GameItem, GameInventory, SlotMarker>), "onItemTargetSlotItemFunc");
            var functions = new SemanticEntityFieldPlan(typeof(GameInventoryFunc), callbacks.ToArray());
            var tags = new SemanticEntityFieldPlan(typeof(TagSystem),
                Runtime("<dict>k__BackingField", typeof(Dictionary<string, TagState>)));
            return new[] { grid, slot, inventory, functions, tags, window };
        }
    }
}
#endif
