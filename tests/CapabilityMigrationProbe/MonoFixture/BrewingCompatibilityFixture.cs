using System;
using System.Collections;
using System.Reflection;

public static partial class OfflineMonoFixture
{
    private static void CheckBrewingCompatibility(Assembly mod, Assembly game)
    {
        object Data(string type) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType(type, true));
        var tagType = game.GetType("TagState", true);
        var systemType = game.GetType("TagSystem", true);
        var itemType = game.GetType("GameItem", true);
        var mapType = typeof(System.Collections.Generic.Dictionary<,>).MakeGenericType(typeof(string), tagType);
        var items = new[] { Data("GameItemElement"), Data("GameItemElement") };
        var maps = new IDictionary[2];
        string[] names = { "VBE_STATE", "VBE_PROGRESS", "VBE_SCORE", "VBE_RECIPE", "VBE_VESSEL",
            "VBE_POWER", "VBE_TIER", "VBE_CONTRABAND", "VBE_WATER_SCORE", "VBE_FRUIT_SCORE",
            "WINE_CONTAINER_TAG", "WINE_CONTAINER_STATE", "BLENDABLE_TAG" };
        for (int i = 0; i < items.Length; i++)
        {
            var system = Data("TagSystem");
            maps[i] = (IDictionary)Activator.CreateInstance(mapType);
            foreach (string name in names)
            {
                var tag = Data("TagState");
                tagType.GetField("valueEnabled", All).SetValue(tag, true);
                tagType.GetField("valueInt", All).SetValue(tag, 1);
                tagType.GetField("valueDouble", All).SetValue(tag, 3.5);
                maps[i][name] = tag;
            }
            systemType.GetField("<dict>k__BackingField", All).SetValue(system, maps[i]);
            itemType.GetProperty("state", All).SetValue(items[i], system);
            itemType.GetProperty("modifiedState", All).SetValue(items[i], system);
        }

        // Optional real third-party callback wrappers. No entry point, Harmony
        // patching, game lifecycle, file saving or business callback is invoked.
        string external = Environment.GetEnvironmentVariable("PSTOOLS_TEST_BREWING_DLL");
        if (!string.IsNullOrEmpty(external))
        {
            var brewing = Assembly.LoadFrom(external);
            brewing.GetType("VanillaBrewingExpansion.Brew.WineBottlePatches", true)
                .GetMethod("WrapOnLoaded", All).Invoke(null, new[] { items[0] });
            brewing.GetType("VanillaBrewingExpansion.Brew.BucketBrew", true)
                .GetMethod("WrapOnLoaded", All).Invoke(null, new[] { items[1] });
        }
        var loadedField = game.GetType("GameItemFunc", true).GetField("<onLoaded>k__BackingField", All)
            ?? throw new Exception("Native onLoaded backing field missing.");
        if ((bool)mod.GetType("ProbablyStolenTools.ChemistryAutomation", true).GetMethod("Compatible", All)
            .Invoke(null, new object[] { items[0], items[1], Array.Empty<string>() }))
            throw new Exception("Automatic blending must leave marked brewing outputs alone.");
        object[] callbacks = { loadedField.GetValue(items[0]), loadedField.GetValue(items[1]) };
        var accessType = mod.GetType("ProbablyStolenTools.ManagedStateAccess", true);
        var graphType = mod.GetType("ProbablyStolenTools.StateGraph", true);
        var idMethod = mod.GetType("ProbablyStolenTools.ManagedObjects", true).GetMethod("Id", All);
        var access = accessType.GetConstructor(All, null,
            new[] { typeof(Type), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }, null)
            .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"),
                "Assembly-CSharp", true, true, true, true });
        mod.GetType("ProbablyStolenTools.SemanticTagBlock", true).GetMethod("Enable", All).Invoke(null, new[] { access });
        mod.GetType("ProbablyStolenTools.SemanticEntityBlocks", true).GetMethod("Enable", All).Invoke(null, new[] { access });
        var root = (IntPtr)idMethod.Invoke(null, new object[] { items });
        accessType.GetMethod("AddRoot", All).Invoke(access, new object[] { root, Array.Empty<string>() });
        var graph = graphType.GetMethod("Capture", All).Invoke(null,
            new object[] { access, new[] { root }, 50000, 1000000, 64L * 1024 * 1024 });
        try
        {
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < items.Length; i++)
                {
                    foreach (string name in names)
                    {
                        tagType.GetField("valueInt", All).SetValue(maps[i][name], 20 + pass);
                        tagType.GetField("valueDouble", All).SetValue(maps[i][name], 9.25);
                    }
                    maps[i]["new-future-tag"] = Data("TagState");
                    loadedField.SetValue(items[i], null);
                }
                graphType.GetMethod("Restore", All).Invoke(graph, null);
                if (((ICollection)graphType.GetMethod("Differences", All).Invoke(graph, new object[] { 12 })).Count != 0)
                    throw new Exception("Brewing tag graph did not restore exactly.");
                for (int i = 0; i < items.Length; i++)
                {
                    if (maps[i].Contains("new-future-tag") || !ReferenceEquals(callbacks[i], loadedField.GetValue(items[i])))
                        throw new Exception("Brewing storage/callback identity changed.");
                    foreach (string name in names)
                        if ((int)tagType.GetField("valueInt", All).GetValue(maps[i][name]) != 1
                            || (double)tagType.GetField("valueDouble", All).GetValue(maps[i][name]) != 3.5)
                            throw new Exception("Brewing tag lost: " + name);
                    if (!(bool)mod.GetType("ProbablyStolenTools.LiquidComposition", true)
                        .GetMethod("NoRename", All).Invoke(null, new[] { items[i] }))
                        throw new Exception("Marked brewing item was renamed.");
                }
            }
            Console.WriteLine("PASS actual game Mono: VBE tag/dictionary identities, scores and progress, exact three-cycle hybrid restore, rename exclusion; real expansion callbacks="
                + !string.IsNullOrEmpty(external) + " (retained, never invoked).");
        }
        finally { ((IDisposable)graph).Dispose(); }
    }
}
