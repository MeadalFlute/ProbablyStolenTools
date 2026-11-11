using System;
using System.Collections.Generic;
using System.Reflection;
#if NEGATIVE_CONTROLS
using System.Linq.Expressions;
#endif

// Offline scanner fixtures only. This assembly must never be shipped with the mod.
public static class MigrationProbe
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    public static FieldInfo ListStorage<T>() => typeof(List<T>).GetField("_items", Instance);
    public static FieldInfo DictionaryStorage<K, V>() => typeof(Dictionary<K, V>).GetField("_entries", Instance);
    public static FieldInfo DictionaryKeys<K, V>()
        => typeof(Dictionary<K, V>.KeyCollection).GetField("_dictionary", Instance);
    public static FieldInfo DictionaryEnumerator<K, V>()
        => typeof(Dictionary<K, V>.Enumerator).GetField("_current", Instance);
    public static FieldInfo PairValue<K, V>() => typeof(KeyValuePair<K, V>).GetField("value", Instance);
    public static FieldInfo HashSetStorage<T>() => typeof(HashSet<T>).GetField("_slots", Instance);
    public static FieldInfo RandomCursor() => typeof(Random).GetField("_inext", Instance);
    public static FieldInfo RandomStorage() => typeof(Random).GetField("_seedArray", Instance);
    public static FieldInfo GameField() => typeof(GameItemElement).GetField("validateLock", Instance);
    public static FieldInfo GameClosure()
        => typeof(GameItem).Assembly.GetType("GameItem+<>c__DisplayClass187_0").GetField("modId", Instance);
    public static FieldInfo[] GameClosureFields()
        => typeof(GameItem).Assembly.GetType("GameItem+<>c__DisplayClass187_0").GetFields(Instance);
    public static object OfficialGame(object target) => ModReflect.Get(target, "validateLock");
    public static void OfficialWrite(object target, string field, object value) => ModReflect.Set(target, field, value);
    public static object ArrayElement(Array target, int index) => target.GetValue(index);
    public static void RestoreArray(Array source, Array destination) => Array.Copy(source, destination, source.Length);
    public static Delegate[] InvocationTargets(Delegate target) => target.GetInvocationList();

#if PRIVATE_LAYOUTS
    public static FieldInfo DictionaryEntryValue<K, V>()
        => typeof(Dictionary<K, V>).GetNestedType("Entry", BindingFlags.NonPublic)
            .MakeGenericType(typeof(K), typeof(V)).GetField("value", Instance);
    public static FieldInfo EntryFromStorage<K, V>()
        => typeof(Dictionary<K, V>).GetField("_entries", Instance).FieldType.GetElementType().GetField("value", Instance);
    public static FieldInfo HashSetSlotValue<T>()
        => typeof(HashSet<T>).GetNestedType("Slot", BindingFlags.NonPublic)
            .MakeGenericType(typeof(T)).GetField("value", Instance);
#endif

#if NEGATIVE_CONTROLS
    public static FieldInfo[] RuntimeSchema(object target) => target.GetType().GetFields(Instance);
    public static FieldInfo[] KnownSystemSchema() => typeof(Random).GetFields(Instance);
    public static Type LiteralSystemEntry()
        => Type.GetType("System.Collections.Generic.Dictionary`2+Entry");
    public static Delegate RuntimeCompilation()
        => Expression.Lambda<Func<int>>(Expression.Constant(1)).Compile();
#endif
}
