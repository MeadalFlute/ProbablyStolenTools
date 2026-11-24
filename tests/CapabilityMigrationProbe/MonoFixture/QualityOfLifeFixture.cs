using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

public static partial class OfflineMonoFixture
{
    private static void CheckQualityOfLife(Assembly mod, Assembly game)
    {
        object Data(string name) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType(name, true));
        var plugin = mod.GetType("ProbablyStolenTools.Plugin", true);
        var boolType = mod.GetType("ProbablyStolenTools.ConfigEntry`1", true).MakeGenericType(typeof(bool));
        object Setting(bool value) => boolType.GetConstructors(All)[0].Invoke(new object[] { value, null, "qol-fixture" });
        string[] fields = { "CfgUnlockMachineModules", "CfgCompositionCurrent", "CfgCompositionExpected", "CfgCompositionGrade" };
        var old = new object[fields.Length];
        for (int i = 0; i < fields.Length; i++) old[i] = plugin.GetField(fields[i], All).GetValue(null);
        void Set(string field, bool value) => plugin.GetField(field, All).SetValue(null, Setting(value));
        try
        {
            var component = mod.GetType("ProbablyStolenTools.InspectionText", true).GetMethod("Component", All);
            for (int mask = 0; mask < 8; mask++)
            {
                Set("CfgCompositionCurrent", (mask & 1) != 0);
                Set("CfgCompositionExpected", (mask & 2) != 0);
                Set("CfgCompositionGrade", (mask & 4) != 0);
                string text = (string)component.Invoke(null, new object[] { "test-component", 12.34, 98.76, "test-grade", "#123456" });
                if (!text.Contains("test-component") || text.Contains("12.34%") != ((mask & 1) != 0)
                    || text.Contains("98.76%") != ((mask & 2) != 0) || text.Contains("test-grade") != ((mask & 4) != 0)
                    || (mask == 0 && text.Contains("("))) throw new Exception("Composition option combination failed: " + mask);
            }
            var machine = Data("GameItemElement");
            var itemType = game.GetType("GameItem", true);
            itemType.GetField("itemTypes", All).SetValue(machine, new List<string> { "MACHINE" });
            var tags = Data("TagSystem");
            var tag = Data("TagState");
            var tagType = game.GetType("TagState", true);
            tagType.GetField("valueEnabled", All).SetValue(tag, true);
            var dict = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), tagType));
            dict["LOCKED_WORKING_TAG"] = tag;
            tags.GetType().GetField("<dict>k__BackingField", All).SetValue(tags, dict);
            itemType.GetProperty("modifiedState", All).SetValue(machine, tags);
            var window = Data("PixelWindow");
            var grid = Data("GridPixelElement");
            var bay = Data("GameGridInventory");
            var battery = Data("GameSlotInventory");
            var inventoryType = game.GetType("GameInventory", true);
            inventoryType.GetProperty("parent", All).SetValue(bay, grid);
            inventoryType.GetProperty("parent", All).SetValue(battery, grid);
            grid.GetType().GetProperty("parent", All).SetValue(grid, window);
            var parents = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
            parents.Add(machine);
            window.GetType().GetProperty("parentItems", All).SetValue(window, parents);
            window.GetType().GetProperty("childElement", All).SetValue(window, grid);
            itemType.GetProperty("contentWindow", All).SetValue(machine, window);
            grid.GetType().GetField("gridWidth", All).SetValue(grid, 2);
            var elements = Array.CreateInstance(game.GetType("PixelElement", true), 4);
            elements.SetValue(battery, 2);
            elements.SetValue(bay, 3);
            grid.GetType().GetField("childElements", All).SetValue(grid, elements);
            inventoryType.GetField("overrideLockInsert", All).SetValue(bay, true);
            inventoryType.GetField("overrideLockRemove", All).SetValue(bay, true);
            var allows = mod.GetType("ProbablyStolenTools.MachineModuleUnlock", true).GetMethod("Allows", All);
            bool Allowed(object inv) => (bool)allows.Invoke(null, new[] { inv });
            Set("CfgUnlockMachineModules", false);
            if (Allowed(bay)) throw new Exception("Module unlock must be opt-in.");
            Set("CfgUnlockMachineModules", true);
            if (!Allowed(bay) || Allowed(battery) || Allowed(null)) throw new Exception("Module unlock escaped bay identity boundary.");
            foreach (string patch in new[] { "MachineModuleInsertUnlockPatch", "MachineModuleRemoveUnlockPatch" })
            {
                var method = mod.GetType("ProbablyStolenTools." + patch, true).GetMethod("Postfix", All);
                object[] arguments = { bay, true };
                method.Invoke(null, arguments);
                if ((bool)arguments[1]) throw new Exception("Module lock query was not bypassed.");
                arguments = new object[] { battery, true };
                method.Invoke(null, arguments);
                if (!(bool)arguments[1]) throw new Exception("Battery lock was bypassed.");
            }
            object[] result = { machine };
            mod.GetType("ProbablyStolenTools.MachineModuleDragUnlockPatch", true).GetMethod("Postfix", All).Invoke(null, result);
            if (result[0] != null) throw new Exception("Explicit native module drag blocker was not bypassed.");
            CheckModuleAdmission(mod, game, bay);
            tagType.GetField("valueEnabled", All).SetValue(tag, false);
            if (Allowed(bay)) throw new Exception("Non-working lock was bypassed.");
            tagType.GetField("valueEnabled", All).SetValue(tag, true);
            Set("CfgUnlockMachineModules", false);
            if (Allowed(bay) || !(bool)inventoryType.GetField("overrideLockInsert", All).GetValue(bay)
                || !(bool)inventoryType.GetField("overrideLockRemove", All).GetValue(bay)
                || !(bool)tagType.GetField("valueEnabled", All).GetValue(tag) || dict.Count != 1)
                throw new Exception("Module query override mutated native lock state.");
            Console.WriteLine("PASS actual game Mono QoL: all eight composition display combinations; exact working-module bay identity, insertion/removal/drag bypass, battery and non-working exclusion, immediate disable and unchanged native lock fields/tags.");
            CheckFileNodeIndex(mod, game, Setting);
            CheckFaucetTooltip(mod, game, Setting);
            CheckRapidBoiling(mod, game);
            CheckItemDetailsInput(mod, game);
            CheckMagnifierBoundary(mod, game);
            CheckSmallContainerDetails(mod, game);
            CheckMiddlePinRejections(mod);
            CheckPinnedCompositionLifecycle(mod, game);
        }
        finally
        {
            for (int i = 0; i < fields.Length; i++) plugin.GetField(fields[i], All).SetValue(null, old[i]);
        }
    }

    private static void CheckMiddlePinRejections(Assembly mod)
    {
        var gestures = mod.GetType("ProbablyStolenTools.NativeWindowGestures", true);
        var pressed = gestures.GetMethod("PointerPressed", All);
        var clicked = gestures.GetMethod("ConsumePinClick", All);
        var armed = gestures.GetField("_pinPressAllowed", All);
        var pointerId = gestures.GetField("_pinPointerId", All);
        object oldArmed = armed.GetValue(null), oldPointer = pointerId.GetValue(null);
        var pointerType = pressed.GetParameters()[0].ParameterType;
        var button = pointerType.GetProperty("button", All);
        var pointer = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(pointerType);
        void Button(string name) => button.SetValue(pointer, Enum.Parse(button.PropertyType, name));
        try
        {
            armed.SetValue(null, false);
            if ((bool)clicked.Invoke(null, new object[] { null })) throw new Exception("Null pin click accepted.");
            Button("Left");
            if ((bool)clicked.Invoke(null, new[] { pointer })) throw new Exception("Left click still triggered pinning.");
            Button("Right");
            if ((bool)clicked.Invoke(null, new[] { pointer })) throw new Exception("Right click triggered pinning.");
            Button("Middle");
            if ((bool)clicked.Invoke(null, new[] { pointer })) throw new Exception("Unarmed middle click triggered pinning.");
            armed.SetValue(null, true);
            pointerId.SetValue(null, 10);
            pointerType.GetProperty("pointerId", All).SetValue(pointer, 11);
            if ((bool)clicked.Invoke(null, new[] { pointer }) || (bool)armed.GetValue(null))
                throw new Exception("Mismatched pointer was accepted or retained its pin gesture.");
            armed.SetValue(null, true);
            Button("Left");
            pressed.Invoke(null, new[] { pointer });
            if ((bool)armed.GetValue(null)) throw new Exception("Left-button press did not cancel pending pinning.");
            Console.WriteLine("PASS actual game Mono middle-pin rejection: left/right/null/unarmed/mismatched pointer excluded, pending gesture consumed and left press cancels; live modifier/drag sampling requires Unity.");
        }
        finally { armed.SetValue(null, oldArmed); pointerId.SetValue(null, oldPointer); }
    }

    private static void CheckPinnedCompositionLifecycle(Assembly mod, Assembly game)
    {
        var pinned = mod.GetType("ProbablyStolenTools.PinnedCompositionTooltip", true);
        var entries = (IList)pinned.GetField("Entries", All).GetValue(null);
        if (entries.Count != 0) throw new Exception("Pinned-composition fixture requires initially empty UI state.");
        var type = pinned.GetNestedType("Entry", All);
        object Window() => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType("PixelWindow", true));
        var owner = Window();
        var outer = Window();
        var unrelated = Window();
        void Add(params object[] windows)
        {
            var entry = Activator.CreateInstance(type, true);
            var dependencies = (IList)type.GetField("Containers", All).GetValue(entry);
            foreach (var window in windows) dependencies.Add(window);
            entries.Add(entry);
        }
        try
        {
            Add(owner, outer);
            Add(unrelated);
            pinned.GetMethod("Closed", All).Invoke(null, new[] { outer });
            if (entries.Count != 1) throw new Exception("Outer container close did not remove only its pinned details.");
            pinned.GetMethod("Closed", All).Invoke(null, new[] { owner });
            if (entries.Count != 1) throw new Exception("Unrelated pinned details were removed.");
            pinned.GetMethod("Disable", All).Invoke(null, null);
            if (entries.Count != 0) throw new Exception("Pinned UI reset retained references.");
        }
        finally { entries.Clear(); }
        Console.WriteLine("PASS actual game Mono pinned-composition ownership: outer close, unrelated-window isolation and reset release; live placement/raycast/rendering require Unity.");
    }

    private static bool RejectModule(object item, object inventory) => false;
    private static bool AcceptModule(object item, object inventory) => true;

    private static void CheckModuleAdmission(Assembly mod, Assembly game, object bay)
    {
        object Data(string name) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType(name, true));
        object Shape(int w, int h)
        {
            var shape = Data("GridShapeBuilder");
            shape.GetType().GetProperty("width", All).SetValue(shape, w);
            shape.GetType().GetProperty("height", All).SetValue(shape, h);
            return shape;
        }
        var item = Data("GameItemElement");
        var tags = Data("TagSystem");
        var tagType = game.GetType("TagState", true);
        tags.GetType().GetField("<dict>k__BackingField", All).SetValue(tags,
            Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), tagType)));
        var itemType = game.GetType("GameItem", true);
        itemType.GetProperty("modifiedState", All).SetValue(item, tags);
        itemType.GetProperty("modifiedShape", All).SetValue(item, Shape(2, 1));
        bay.GetType().GetProperty("inventoryShape", All).SetValue(bay, Shape(1, 2));
        var gridFilter = game.GetType("GameInventoryFunc", true).GetProperty("mayInventoryAddItemFunc", All);
        var itemFilter = game.GetType("GameItemFunc", true).GetProperty("mayInventoryAddItemFunc", All);
        Delegate Filter(Type type, string name) => Delegate.CreateDelegate(type,
            typeof(OfflineMonoFixture).GetMethod(name, All));
        var patch = mod.GetType("ProbablyStolenTools.MachineModuleAdmissionUnlockPatch", true).GetMethod("Postfix", All);
        bool Apply(object inventory, object candidate)
        {
            object[] args = { inventory, candidate, false };
            patch.Invoke(null, args);
            return (bool)args[2];
        }
        if (!Apply(bay, item) || Apply(bay, null)) throw new Exception("Working bay admission did not allow a rotated fit or rejected null incorrectly.");
        gridFilter.SetValue(bay, Filter(gridFilter.PropertyType, "RejectModule"));
        if (Apply(bay, item)) throw new Exception("Working bay bypassed module-type filter.");
        gridFilter.SetValue(bay, Filter(gridFilter.PropertyType, "AcceptModule"));
        itemFilter.SetValue(item, Filter(itemFilter.PropertyType, "RejectModule"));
        if (Apply(bay, item)) throw new Exception("Working bay bypassed item filter.");
        itemFilter.SetValue(item, null);
        if (!Apply(bay, item)) throw new Exception("Working bay rejected an allowed module.");
        itemType.GetProperty("modifiedShape", All).SetValue(item, Shape(3, 3));
        if (Apply(bay, item)) throw new Exception("Working bay bypassed size admission.");
        if (Apply(null, item)) throw new Exception("Working bay admission escaped inventory identity.");
        gridFilter.SetValue(bay, null);
        Console.WriteLine("PASS actual game Mono module admission: rotated fit, module/item filters, oversized/null rejection; native lock fields unchanged.");
    }

    private static void CheckSmallContainerDetails(Assembly mod, Assembly game)
    {
        object Data(string name) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType(name, true));
        var item = Data("GameItemElement");
        var itemType = game.GetType("GameItem", true);
        var tagType = game.GetType("TagState", true);
        var tags = Data("TagSystem");
        var dict = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), tagType));
        void Tag(string key, int value)
        {
            var tag = Data("TagState");
            tagType.GetField("valueEnabled", All).SetValue(tag, true);
            tagType.GetField("valueInt", All).SetValue(tag, value);
            dict[key] = tag;
        }
        tags.GetType().GetField("<dict>k__BackingField", All).SetValue(tags, dict);
        itemType.GetProperty("modifiedState", All).SetValue(item, tags);
        Tag("LIQUID_CONTAINER_TAG", 1);
        var supports = mod.GetType("ProbablyStolenTools.LiquidComposition", true).GetMethod("SupportsDetails", All);
        var noRename = mod.GetType("ProbablyStolenTools.LiquidComposition", true).GetMethod("NoRename", All);
        foreach (string marker in new[] { "LIQUID_CONTAINER_DROPPER_TAG", "GRADUATED_CONTAINER_TAG" })
        {
            Tag(marker, 1);
            foreach (int capacity in new[] { 1, 1000, 10000, 100000, 149999 })
            {
                Tag("LIQUID_CONTAINER_CAPACITY", capacity);
                if (!(bool)supports.Invoke(null, new[] { item }) || !(bool)noRename.Invoke(null, new[] { item }))
                    throw new Exception("Small vessel details inherited rename limits.");
            }
            dict.Remove(marker);
        }
        Tag("LIQUID_CONTAINER_CAPACITY", 0);
        if ((bool)supports.Invoke(null, new[] { item })) throw new Exception("Invalid-capacity vessel admitted.");
        Tag("LIQUID_CONTAINER_CAPACITY", 1000);
        Tag("VBE_STATE", 1);
        if ((bool)supports.Invoke(null, new[] { item })) throw new Exception("VBE details exclusion lost.");
        dict.Remove("VBE_STATE");
        dict.Remove("LIQUID_CONTAINER_TAG");
        if ((bool)supports.Invoke(null, new[] { item })) throw new Exception("Non-liquid vessel admitted.");
        Console.WriteLine("PASS actual game Mono small vessel details: dropper/graduated 1uL-149999uL, no renaming, invalid capacity/non-liquid/VBE exclusion; live localized rows require Unity.");
    }

    private static void CheckItemDetailsInput(Assembly mod, Assembly game)
    {
        var input = mod.GetType("ProbablyStolenTools.ItemDetailsInput", true).GetMethod("Matches", All);
        var keyType = input.GetParameters()[0].ParameterType;
        bool Held(string key, bool primary, bool alternate) => (bool)input.Invoke(null,
            new object[] { Enum.Parse(keyType, key), primary, alternate });
        foreach (string key in new[] { "LeftShift", "RightShift" })
        {
            if (!Held(key, true, false) || !Held(key, false, true) || Held(key, false, false))
                throw new Exception("Either Shift must activate Shift-bound details.");
        }
        foreach (string key in new[] { "F10", "Mouse2", "Mouse3", "LeftControl" })
            if (!Held(key, true, false) || Held(key, false, true) || Held(key, false, false))
                throw new Exception("Rebound details key fell back to Shift: " + key);
        if (Held("None", true, true)) throw new Exception("Unbound details key remained active.");
        Console.WriteLine("PASS actual game Mono item-details binding: either Shift, rebound keyboard/mouse/modifier, release and disabled states.");
    }

    private static void CheckMagnifierBoundary(Assembly mod, Assembly game)
    {
        object Data(string name) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType(name, true));
        var itemType = game.GetType("GameItem", true);
        var tagType = game.GetType("TagState", true);
        var tool = Data("GameItemElement");
        var target = Data("GameItemElement");
        var tags = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), tagType));
        var enabled = Data("TagState");
        tagType.GetField("valueEnabled", All).SetValue(enabled, true);
        tags["MAGNIFYING_GLASS"] = enabled;
        tags["IS_OWNED_TAG"] = enabled;
        var state = Data("TagSystem");
        state.GetType().GetField("<dict>k__BackingField", All).SetValue(state, tags);
        itemType.GetProperty("modifiedState", All).SetValue(tool, state);
        var candidates = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
        candidates.Add(tool);
        var find = mod.GetType("ProbablyStolenTools.DoubleClickInspectionPatch", true).GetMethod("FindMagnifier", All);
        object Find(object item) => find.Invoke(null, new[] { candidates, item });
        // Native labelers also carry MAGNIFYING_GLASS. These rejections must not
        // reach MayTarget/CanTarget (which need a live GameMaster in Unity).
        foreach (string id in new[] { "labeler", "sec_keycard", "custom_tool" })
        {
            itemType.GetProperty("identifier", All).SetValue(tool, id);
            if (Find(target) != null) throw new Exception("Double click accepted a non-magnifier: " + id);
        }
        itemType.GetProperty("identifier", All).SetValue(tool, "magnifier");
        if (Find(tool) != null) throw new Exception("Magnifier targeted itself.");
        itemType.GetProperty("forceDisableUse", All).SetValue(tool, true);
        if (Find(target) != null) throw new Exception("Disabled magnifier was accepted.");
        itemType.GetProperty("forceDisableUse", All).SetValue(tool, false);
        tags.Remove("IS_OWNED_TAG");
        if (Find(target) != null) throw new Exception("Unowned magnifier was accepted.");
        Console.WriteLine("PASS actual game Mono magnifier boundary: labeler/keycard/custom tool, self, disabled and unowned exclusions before targeting; live close/open ordering requires gameplay.");
    }

    private static void CheckFaucetTooltip(Assembly mod, Assembly game, Func<bool, object> setting)
    {
        object Data(string name) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType(name, true));
        var playerType = game.GetType("PlayerStore", true);
        var stationType = game.GetType("StoreStation", true);
        var playerField = playerType.GetField("instance", All);
        var stationField = stationType.GetField("instance", All);
        var option = mod.GetType("ProbablyStolenTools.Plugin", true).GetField("CfgPipeComposition", All);
        object oldPlayer = playerField.GetValue(null), oldStation = stationField.GetValue(null), oldOption = option.GetValue(null);
        try
        {
            var player = Data("PlayerStore");
            var station = Data("StoreStation");
            playerField.SetValue(null, player);
            stationField.SetValue(null, station);
            stationType.GetField("todayRustwaterPurity", All).SetValue(station, 9123);
            stationType.GetField("todayGutterflowPurity", All).SetValue(station, 8345);
            var upgradeType = game.GetType("NetworkUpgrade", true);
            var upgrades = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), upgradeType));
            playerType.GetField("networkUpgrade", All).SetValue(player, upgrades);
            var faucet = Data("GameItemElement");
            game.GetType("GameItem", true).GetProperty("identifier", All).SetValue(faucet, "faucet");
            var tagType = game.GetType("TagState", true);
            var hidden = Data("TagState");
            tagType.GetField("valueEnabled", All).SetValue(hidden, true);
            var tags = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), tagType));
            tags["IS_TOOLTIP_DISABLED_TAG"] = hidden;
            var tagSystem = Data("TagSystem");
            tagSystem.GetType().GetField("<dict>k__BackingField", All).SetValue(tagSystem, tags);
            game.GetType("GameItem", true).GetProperty("modifiedState", All).SetValue(faucet, tagSystem);
            if ((bool)faucet.GetType().GetMethod("HasTooltip", All).Invoke(faucet, null))
                throw new Exception("Native hidden faucet regression setup did not reproduce.");
            var details = mod.GetType("ProbablyStolenTools.DetailedCompositionTooltip", true);
            var purity = details.GetMethod("TryPipePurity", All);
            int Read()
            {
                object[] args = { faucet, 0 };
                return (bool)purity.Invoke(null, args) ? (int)args[1] : -1;
            }
            if (Read() != -1) throw new Exception("Unconnected faucet received invented water quality.");
            var gutter = Data("NetworkUpgrade");
            upgradeType.GetField("state", All).SetValue(gutter, 2);
            upgrades["GUTTERFLOW_TAP"] = gutter;
            if (Read() != 8345) throw new Exception("Faucet did not read today's gutterflow quality with absent rust upgrade.");
            var rust = Data("NetworkUpgrade");
            upgradeType.GetField("state", All).SetValue(rust, 2);
            upgrades["RUSTWATER_TAP"] = rust;
            if (Read() != 9123) throw new Exception("Faucet did not prefer native rustwater fill path.");
            var visible = details.GetMethod("PipeTooltipVisible", All);
            option.SetValue(null, setting(true));
            if (!(bool)visible.Invoke(null, new object[] { faucet, false, true }))
                throw new Exception("Shift-upgraded faucet retained native hidden-tooltip query.");
            if ((bool)visible.Invoke(null, new object[] { faucet, false, false }))
                throw new Exception("Unshifted faucet changed native hidden-tooltip query.");
            if (!(bool)visible.Invoke(null, new object[] { faucet, true, false }))
                throw new Exception("Unshifted faucet suppressed an originally visible tooltip.");
            option.SetValue(null, setting(false));
            if ((bool)visible.Invoke(null, new object[] { faucet, false, true }))
                throw new Exception("Disabled composition option changed native tooltip visibility.");
            var rich = game.GetType("RichText", true);
            object Rich(string s, bool truncated = false) => Activator.CreateInstance(rich, new object[] { s, "s:" + s, "u:" + s,
                "f:" + s, "b:" + s, 20, 2, truncated });
            var original = Rich("connected to native supply", true);
            var joined = details.GetMethod("Append", All).Invoke(null, new[] { original, Rich("water share") });
            foreach (string field in new[] { "nakedText", "richTextStrikethrough", "richTextUnderline", "richTextForeground", "richTextBackground" })
                if (!((string)rich.GetProperty(field).GetValue(joined)).StartsWith((string)rich.GetProperty(field).GetValue(original) + "\n"))
                    throw new Exception("Faucet final append lost native tooltip layer: " + field);
            if (!(bool)rich.GetProperty("isTruncated").GetValue(joined) || (int)rich.GetProperty("recommendedHeight").GetValue(joined) != 4)
                throw new Exception("Faucet append lost native tooltip dimensions or truncation state.");
            if (upgrades.Count != 2 || (int)upgradeType.GetField("state", All).GetValue(rust) != 2
                || (int)stationType.GetField("todayRustwaterPurity", All).GetValue(station) != 9123
                || !(bool)tagType.GetField("valueEnabled", All).GetValue(hidden) || tags.Count != 1)
                throw new Exception("Faucet description mutated runtime quality or upgrades.");
            Console.WriteLine("PASS actual game Mono faucet: missing upgrades/unconnected excluded, native water source priority and today's quality, opt-in hidden-query override; final append preserves all five original tooltip layers and metadata without mutation.");
        }
        finally
        {
            playerField.SetValue(null, oldPlayer);
            stationField.SetValue(null, oldStation);
            option.SetValue(null, oldOption);
        }
    }

    private static void CheckFileNodeIndex(Assembly mod, Assembly game, Func<bool, object> setting)
    {
        var rewind = mod.GetType("ProbablyStolenTools.Rewind", true);
        var points = (IList)rewind.GetField("Points", All).GetValue(null);
        var previousPoints = new object[points.Count];
        points.CopyTo(previousPoints, 0);
        string[] stateFields = { "_indexedStore", "_indexedDay", "<PointRevision>k__BackingField" };
        var oldState = new object[stateFields.Length];
        for (int i = 0; i < stateFields.Length; i++) oldState[i] = rewind.GetField(stateFields[i], All).GetValue(null);
        var preference = mod.GetType("ProbablyStolenTools.Plugin", true).GetField("CfgKeepPreviousDay", All);
        var oldPreference = preference.GetValue(null);
        var catalog = rewind.GetField("Catalog", All).GetValue(null);
        var entries = catalog.GetType().GetField("_entries", All);
        var oldEntries = entries.GetValue(catalog);
        string root = Path.Combine(Path.GetDirectoryName(mod.Location), "qol-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType("PlayerStore", true));
            store.GetType().GetField("saveSlotId", All).SetValue(store, 2);
            store.GetType().GetField("runID", All).SetValue(store, "qol-fixture");
            void Add(int stamp, int day, int slot = 2, string run = "qol-fixture", int state = 2)
            {
                File.WriteAllText(Path.Combine(root, "202611" + stamp.ToString("D2") + "_120000_0000000_day.es3"),
                    "{\"storeStation\":{\"value\":{\"dayCounter\":" + day + "}},\"playerStore\":{\"value\":{\"playerCash\":100,\"storeState\":"
                    + state + ",\"saveSlotId\":" + slot + ",\"runID\":\"" + run + "\"}}}");
            }
            Add(1, 95); Add(2, 94); Add(3, 93); Add(4, 95, slot: 3); Add(5, 95, run: "foreign"); Add(6, 95, state: 1);
            string largePath = Path.Combine(root, "20261101_120000_0000000_day.es3");
            string largeText = File.ReadAllText(largePath);
            File.WriteAllText(largePath, "{\"padding\":\"" + new string('x', 21 * 1024 * 1024) + "\"," + largeText.Substring(1));
            var index = rewind.GetMethod("IndexDayStartPoints", All);
            points.Clear();
            preference.SetValue(null, setting(true));
            index.Invoke(null, new[] { store, (object)95, root });
            if (points.Count != 2) throw new Exception("Disabled-memory file index did not isolate current and retained previous morning.");
            foreach (var point in points)
            {
                var type = point.GetType();
                if (!(bool)type.GetField("IsDayStart", All).GetValue(point) || (int)type.GetField("State", All).GetValue(point) != 2
                    || (string)type.GetField("Label", All).GetValue(point) != "开业前") throw new Exception("Indexed morning file lost display metadata.");
            }
            index.Invoke(null, new[] { store, (object)95, root });
            if (points.Count != 2 || Directory.GetFiles(root).Length != 6) throw new Exception("File indexing duplicated nodes or wrote saves.");
            points.Clear();
            preference.SetValue(null, setting(false));
            index.Invoke(null, new[] { store, (object)95, root });
            if (points.Count != 1 || (int)points[0].GetType().GetField("Day", All).GetValue(points[0]) != 95)
                throw new Exception("File index ignored disabled previous-day retention.");
            Console.WriteLine("PASS actual game Mono QoL: existing morning files indexed read-only; day/slot/run/state boundaries, retained yesterday opt-out, display metadata and repeat deduplication.");
        }
        finally
        {
            points.Clear();
            foreach (var point in previousPoints) points.Add(point);
            for (int i = 0; i < stateFields.Length; i++) rewind.GetField(stateFields[i], All).SetValue(null, oldState[i]);
            preference.SetValue(null, oldPreference);
            entries.SetValue(catalog, oldEntries);
            // Only remove files from the unique fixture-owned directory created above.
            Directory.Delete(root, true);
        }
    }
}
