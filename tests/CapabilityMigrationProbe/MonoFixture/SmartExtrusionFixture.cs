using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public static partial class OfflineMonoFixture
{
    private static void CheckSmartExtrusion(Assembly mod, Assembly game)
    {
        object Data(string name) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType(name, true));
        var itemType = game.GetType("GameItem", true);
        var inventoryType = game.GetType("GameInventory", true);
        var machine = Data("GameItemElement");
        itemType.GetProperty("identifier", All).SetValue(machine, "chem_finisher");
        var window = Data("PixelWindow");
        itemType.GetProperty("contentWindow", All).SetValue(machine, window);
        var parents = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType));
        parents.Add(machine);
        window.GetType().GetProperty("parentItems", All).SetValue(window, parents);
        var module = Data("GameGridInventory");
        var container = Data("GameSlotInventory");
        var output = Data("GameGridInventory");
        var unrelated = Data("GameSlotInventory");
        var row = Data("MachineryPanelRow");
        row.GetType().GetProperty("parent", All).SetValue(row, window);
        var panelType = game.GetType("MachineryPanel", true);
        var panels = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(panelType));
        foreach (var content in new[] { module, container, output })
        {
            var panel = Data("MachineryPanel");
            panelType.GetProperty("content", All).SetValue(panel, content);
            panels.Add(panel);
            inventoryType.GetProperty("parent", All).SetValue(content, row);
        }
        row.GetType().GetField("panels", All).SetValue(row, panels);
        window.GetType().GetProperty("childElement", All).SetValue(window, row);
        var automation = mod.GetType("ProbablyStolenTools.ChemistryAutomation", true);
        var accepts = automation.GetMethod("IsExtrusionSlot", All);
        bool Accept(object slot) => (bool)accepts.Invoke(null, new[] { machine, slot });
        var body = game.GetType("MachineHelper", true).GetMethod("GetBodyStorage", All);
        if (!ReferenceEquals(body.Invoke(null, new object[] { machine, 0 }), module)
            || !ReferenceEquals(body.Invoke(null, new object[] { machine, 1 }), container)
            || !ReferenceEquals(body.Invoke(null, new object[] { machine, 2 }), output)
            || !ReferenceEquals(inventoryType.GetMethod("GetParentItem", All).Invoke(container, null), machine)
            || !Accept(container) || Accept(unrelated) || Accept(null))
            throw new Exception("Smart extrusion must admit the finisher's index-1 container, not a blender index-2 slot.");
        itemType.GetProperty("identifier", All).SetValue(machine, "blender");
        if (Accept(container)) throw new Exception("Smart extrusion admitted another machine.");
        itemType.GetProperty("identifier", All).SetValue(machine, "chem_finisher");
        var legacy = Data("GridPixelElement");
        legacy.GetType().GetField("gridWidth", All).SetValue(legacy, 3);
        var elements = Array.CreateInstance(game.GetType("PixelElement", true), 6);
        elements.SetValue(module, 3); elements.SetValue(container, 4); elements.SetValue(output, 5);
        legacy.GetType().GetField("childElements", All).SetValue(legacy, elements);
        window.GetType().GetProperty("childElement", All).SetValue(window, legacy);
        if (!Accept(container) || Accept(unrelated)) throw new Exception("Legacy native finisher container identity failed.");
        Console.WriteLine("PASS actual game Mono smart extrusion admission: native MachineryPanelRow module=0/container=1/output=2, original parent identity, legacy GridPixelElement path, unrelated/null slot and other machine rejected. No Unity UI or production callback invoked.");
    }
}
