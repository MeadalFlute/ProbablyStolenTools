using System;
using System.Collections.Generic;
using System.Reflection;

public static class OfflineMonoFixture
{
    public static void Run(string path)
    {
        var generated = Assembly.LoadFrom(path).GetType("LiteralTypeTokens", true);
        var valueField = (FieldInfo)generated.GetMethod("DictionaryValue")
            .MakeGenericMethod(typeof(string), typeof(object)).Invoke(null, null);
        var nextField = (FieldInfo)generated.GetMethod("DictionaryNext")
            .MakeGenericMethod(typeof(string), typeof(object)).Invoke(null, null);
        var dict = new Dictionary<string, object> { ["key"] = new object() };
        var entries = (Array)typeof(Dictionary<string, object>)
            .GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(dict);
        object entry = entries.GetValue(0);
        object expected = new object();
        valueField.SetValue(entry, expected);
        entries.SetValue(entry, 0);
        if (!ReferenceEquals(dict["key"], expected) || (int)nextField.GetValue(entry) != -1)
            throw new Exception("Literal Entry field read/write failed.");
        var slotField = (FieldInfo)generated.GetMethod("HashSetValue")
            .MakeGenericMethod(typeof(object)).Invoke(null, null);
        var set = new HashSet<object> { expected };
        var slots = (Array)typeof(HashSet<object>)
            .GetField("_slots", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(set);
        if (!ReferenceEquals(slotField.GetValue(slots.GetValue(0)), expected))
            throw new Exception("Literal Slot read failed.");
        Console.WriteLine("PASS actual game Mono: generic private Entry/Slot type tokens, field reads, boxed value writeback.");
    }
}
