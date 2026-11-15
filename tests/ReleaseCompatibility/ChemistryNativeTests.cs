using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

internal static class ChemistryNativeTests
{
    internal static void Run(Assembly mod, Assembly game)
    {
        const BindingFlags methods = BindingFlags.NonPublic | BindingFlags.Static;
        var projection = mod.GetType("ProbablyStolenTools.FacilityProjection", true);
        var consume = projection.GetMethod("Consume", methods);
        var split = game.GetType("WaterHelper", true).GetMethod("SplitProportionally");
        var random = new Random(147);
        for (int sample = 0; sample < 500; sample++)
        {
            var parts = Enumerable.Range(0, random.Next(1, 40)).Select(_ => random.Next(0, 100000)).ToArray();
            int ml = random.Next(0, 1000);
            var native = (int[])split.Invoke(null, new object[] { parts, ml * 1000 });
            var preview = (int[])consume.Invoke(null, new object[] { parts, ml });
            if (!preview.SequenceEqual(parts.Select((part, i) => part - native[i])))
                throw new Exception("Hydroponic forecast differs from native proportional consumption at sample " + sample);
        }
        Console.WriteLine("PASS 500 native WaterHelper proportional-consumption comparisons (exact component remainder parity)");

        var itemType = game.GetType("GameItemElement", true);
        var item = RuntimeHelpers.GetUninitializedObject(itemType);
        var systemType = game.GetType("TagSystem", true);
        var system = Activator.CreateInstance(systemType);
        itemType.GetProperty("modifiedState").SetValue(item, system);
        itemType.GetProperty("identifier").SetValue(item, "med_bottle_small_pink");
        var map = (IDictionary)systemType.GetProperty("dict").GetValue(system);
        var version = map.GetType().GetField("_version", BindingFlags.Instance | BindingFlags.NonPublic);
        var data = mod.GetType("ProbablyStolenTools.ChemistryData", true);
        long before = Convert.ToInt64(version.GetValue(map));
        if ((int)data.GetMethod("Int", methods).Invoke(null, new[] { item, "missing" }) != 0
            || data.GetMethod("Tag", methods).Invoke(null, new[] { item, "missing" }) != null
            || map.Count != 0 || before != Convert.ToInt64(version.GetValue(map)))
            throw new Exception("Chemistry read-only lookup inserted or changed native tags.");

        object Tag(int value)
        {
            var tag = RuntimeHelpers.GetUninitializedObject(game.GetType("TagState", true));
            tag.GetType().GetField("valueEnabled").SetValue(tag, true);
            tag.GetType().GetField("valueInt").SetValue(tag, value);
            return tag;
        }
        var capacity = Tag(150000);
        map.Add("LIQUID_CONTAINER_CAPACITY", capacity);
        var composition = mod.GetType("ProbablyStolenTools.LiquidComposition", true);
        bool NoRename() => (bool)composition.GetMethod("NoRename", methods).Invoke(null, new[] { item });
        bool Included() => (bool)composition.GetMethod("IsSmallEnough", methods).Invoke(null, new[] { item });
        if (NoRename() || !Included()) throw new Exception("Native 150mL bottle excluded.");
        capacity.GetType().GetField("valueInt").SetValue(capacity, 149000);
        if (!NoRename() || Included()) throw new Exception("Native sub-150mL threshold ignored.");
        capacity.GetType().GetField("valueInt").SetValue(capacity, 50000000);
        if (NoRename() || !Included()) throw new Exception("Large tank still has a chemistry maximum.");
        map.Add("GRADUATED_CONTAINER_TAG", Tag(0));
        itemType.GetProperty("identifier").SetValue(item, "water_tank");
        if (NoRename()) throw new Exception("Native graduated water tank treated as laboratory vessel.");
        itemType.GetProperty("identifier").SetValue(item, "water_pitcher");
        if (NoRename()) throw new Exception("Native graduated water pitcher treated as laboratory vessel.");
        itemType.GetProperty("identifier").SetValue(item, "measuring_cup");
        if (!NoRename()) throw new Exception("Laboratory graduated vessel renamed.");
        map.Add("LIQUID_CONTAINER_DROPPER_TAG", Tag(0));
        itemType.GetProperty("identifier").SetValue(item, "water_tank");
        if (!NoRename()) throw new Exception("Dropper exclusion overridden.");
        before = Convert.ToInt64(version.GetValue(map));
        for (int i = 0; i < 50; i++) { NoRename(); Included(); }
        if (before != Convert.ToInt64(version.GetValue(map))) throw new Exception("Capacity reads mutated the native dictionary.");
        Console.WriteLine("PASS native read-only tag lookup, 149/150mL boundary, 50L capacity and graduated/dropper naming exclusions");
    }
}
