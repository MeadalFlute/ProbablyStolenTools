using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class OfflineMonoFixture
{
    public static void Run(string path)
    {
        var assembly = Assembly.LoadFrom(path);
        if (assembly.GetType("ProbablyStolenTools.ManagedStateAccess") != null) { CheckGraph(assembly); return; }
        var layouts = assembly.GetType("ProbablyStolenTools.Generated.SnapshotLayouts");
        if (layouts != null) { CheckLayouts(layouts); return; }
        var generated = assembly.GetType("LiteralTypeTokens", true);
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

    private static void CheckLayouts(Type layouts)
    {
        var types = (Type[])layouts.GetMethod("KnownTypes").Invoke(null, null);
        var factories = (MethodInfo[])layouts.GetMethod("KnownFactories").Invoke(null, null);
        var modules = (string[])layouts.GetMethod("ExpectedModules").Invoke(null, null);
        if (types.Length != factories.Length) throw new Exception("Registry length mismatch.");
        var expectedModules = new Dictionary<string, string>();
        for (int i = 0; i < modules.Length; i += 2) expectedModules.Add(modules[i], modules[i + 1]);
        var unique = new HashSet<Type>();
        int count = 0, fieldsTotal = 0, generic = 0;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (int i = 0; i < types.Length; i++)
        {
            var definition = types[i];
            if (!unique.Add(definition)) throw new Exception("Duplicate type: " + definition);
            if (definition.Module.ModuleVersionId.ToString("D") != expectedModules[definition.Module.ScopeName])
                throw new Exception("Target module fingerprint mismatch: " + definition.Module.ScopeName);
            var type = definition;
            var factory = factories[i];
            if (definition.IsGenericTypeDefinition)
            {
                generic++;
                var arguments = CloseArguments(definition);
                type = definition.MakeGenericType(arguments);
                factory = factory.MakeGenericMethod(arguments);
            }
            var expected = type.GetFields(flags);
            var actual = (FieldInfo[])factory.Invoke(null, null);
            if (actual.Length != expected.Length) throw new Exception("Field count mismatch: " + type);
            for (int j = 0; j < expected.Length; j++)
                if (actual[j] == null || actual[j].Name != expected[j].Name
                    || actual[j].FieldType != expected[j].FieldType
                    || actual[j].DeclaringType != expected[j].DeclaringType
                    || actual[j].Attributes != expected[j].Attributes)
                    throw new Exception("Field metadata mismatch: " + type + " at " + j);
            count++;
            fieldsTotal += actual.Length;
        }
        Console.WriteLine("PASS actual game Mono static registry: types=" + count + " genericTemplates=" + generic
            + " declaredFields=" + fieldsTotal + " exact-order/type/attributes/module-MVID match reflection.");
    }

    private static Type[] CloseArguments(Type definition)
    {
        var parameters = definition.GetGenericArguments();
        var choices = new List<Type> { typeof(object), typeof(int), typeof(string), typeof(DayOfWeek), typeof(Action) };
        foreach (var parameter in parameters)
            foreach (var constraint in parameter.GetGenericParameterConstraints())
                if (!constraint.ContainsGenericParameters) choices.Add(constraint);
        var result = new Type[parameters.Length];
        bool Try(int index)
        {
            if (index == result.Length)
            {
                try { definition.MakeGenericType(result); return true; }
                catch (ArgumentException) { return false; }
            }
            foreach (var candidate in choices)
            {
                result[index] = candidate;
                if (Try(index + 1)) return true;
            }
            return false;
        }
        if (!Try(0)) throw new Exception("No fixture generic arguments found: " + definition);
        return result;
    }

    private static void CheckGraph(Assembly mod)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags statics = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var game = Assembly.Load("Assembly-CSharp");
        var unity = Assembly.Load("UnityEngine.CoreModule");
        object Data(string name) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType(name, true));
        var accessType = mod.GetType("ProbablyStolenTools.ManagedStateAccess", true);
        var graphType = mod.GetType("ProbablyStolenTools.StateGraph", true);
        var idMethod = mod.GetType("ProbablyStolenTools.ManagedObjects", true).GetMethod("Id", statics);
        IntPtr Id(object value) => (IntPtr)idMethod.Invoke(null, new[] { value });
        var root = Data("PlayerStore");
        var item = Data("GameItemElement");
        CheckItemLayout(mod, game, item);
        var tag = Data("TagState");
        var tagType = tag.GetType();
        var unit = item.GetType().GetProperty("unitCount");
        var cash = root.GetType().GetField("playerCash");
        var current = root.GetType().GetField("currentNegociatedItem");
        unit.SetValue(item, 2);
        cash.SetValue(root, 135);
        current.SetValue(root, item);
        var shape = Data("GridShapeBuilder");
        var shapeData = new byte[] { 1, 0, 1, 1, 0, 1, 0, 1 };
        var shapeDataField = shape.GetType().GetField("data", instance);
        shapeDataField.SetValue(shape, shapeData);
        item.GetType().GetProperty("modifiedShape").SetValue(item, shape);
        var floatField = tagType.GetField("valueFloat", instance);
        floatField.SetValue(tag, BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)));
        var list = new List<object> { item, tag, item };
        var dictionary = new Dictionary<string, object> { ["item"] = item, ["tag"] = tag, ["list"] = list };
        var hashSet = new HashSet<object> { item, tag, list };
        var pairType = typeof(KeyValuePair<,>).MakeGenericType(tagType, typeof(float));
        var mapType = typeof(Dictionary<,>).MakeGenericType(typeof(string), pairType);
        var pairMap = (IDictionary)Activator.CreateInstance(mapType);
        pairMap["pair"] = Activator.CreateInstance(pairType, new object[] { tag,
            BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)) });
        var random = new Random(7169);
        var closureType = game.GetType("GameItem+<>c__DisplayClass187_0", true);
        var closure = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(closureType);
        var closureField = closureType.GetField("modId", instance);
        closureField.SetValue(closure, "before");
        Action callback = NeverInvoke;
        var tuple = Tuple.Create(item, tag, list);
        var tupleItem = tuple.GetType().GetField("m_Item1", instance);
        var values = new object[] { root, list, dictionary, hashSet, pairMap, random, closure, callback, tuple };
        var roots = new[] { Id(values) };
        object Access(bool blocks = true)
        {
            var access = accessType.GetConstructor(instance, null,
                new[] { typeof(Type), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }, null)
                .Invoke(new object[] { unity.GetType("UnityEngine.Object"), "Assembly-CSharp", true, true, true, blocks });
            accessType.GetMethod("AddRoot", instance).Invoke(access, new object[] { Id(values), Array.Empty<string>() });
            if (blocks)
            {
                mod.GetType("ProbablyStolenTools.SemanticTagBlock").GetMethod("Enable", statics).Invoke(null, new[] { access });
                mod.GetType("ProbablyStolenTools.SemanticEntityBlocks").GetMethod("Enable", statics).Invoke(null, new[] { access });
            }
            return access;
        }
        object Capture() => graphType.GetMethod("Capture", statics)
            .Invoke(null, new object[] { Access(), roots, 50000, 1000000, 64L * 1024 * 1024 });
        void Verify(object graph)
        {
            var differences = (ICollection)graphType.GetMethod("Differences", instance).Invoke(graph, new object[] { 12 });
            if (differences.Count != 0) throw new Exception("Actual Mono graph differences: " + differences.Count);
        }
        void Restore(object graph)
        {
            graphType.GetMethod("Restore", instance).Invoke(graph, null);
            Verify(graph);
        }
        object[] Sequence()
        {
            var sequence = new object[12];
            for (int i = 0; i < sequence.Length; i++) sequence[i] = random.Next();
            return sequence;
        }
        var first = Capture();
        object second = null;
        var originalEntries = typeof(Dictionary<string, object>).GetField("_entries", instance).GetValue(dictionary);
        var originalList = typeof(List<object>).GetField("_items", instance).GetValue(list);
        var originalSlots = typeof(HashSet<object>).GetField("_slots", instance).GetValue(hashSet);
        try
        {
            Verify(first);
            var capturedAccess = graphType.GetField("_access", instance).GetValue(first);
            var entityFactory = accessType.GetProperty("EntityBlocks", instance).GetValue(capturedAccess);
            int entityBlocks = (int)entityFactory.GetType().GetProperty("BlockCount", instance).GetValue(entityFactory);
            int intBlocks = (int)accessType.GetProperty("IntArrayBlockCount", instance).GetValue(capturedAccess);
            if (entityBlocks < 2 || intBlocks == 0 || (int)graphType.GetProperty("BlockCount", instance).GetValue(first) <= entityBlocks + intBlocks)
                throw new Exception("Actual Mono entity/tag/int blocks were not all exercised.");
            var ordinary = graphType.GetMethod("Capture", statics)
                .Invoke(null, new object[] { Access(false), roots, 50000, 1000000, 64L * 1024 * 1024 });
            try
            {
                foreach (string name in new[] { "ObjectCount", "SlotCount", "ByteCount" })
                    if (!Equals(graphType.GetProperty(name, instance).GetValue(first), graphType.GetProperty(name, instance).GetValue(ordinary)))
                        throw new Exception("Actual Mono ordinary/block coverage mismatch: " + name);
            }
            finally { ((IDisposable)ordinary).Dispose(); }
            int objectCount = (int)graphType.GetProperty("ObjectCount", instance).GetValue(first);
            int slotCount = (int)graphType.GetProperty("SlotCount", instance).GetValue(first);
            long byteCount = (long)graphType.GetProperty("ByteCount", instance).GetValue(first);
            var exact = graphType.GetMethod("Capture", statics)
                .Invoke(null, new object[] { Access(), roots, objectCount, slotCount, byteCount });
            ((IDisposable)exact).Dispose();
            foreach (int budget in new[] { 0, 1, 2 })
            {
                try
                {
                    graphType.GetMethod("Capture", statics).Invoke(null, new object[] { Access(), roots,
                        objectCount - (budget == 0 ? 1 : 0), slotCount - (budget == 1 ? 1 : 0),
                        byteCount - (budget == 2 ? 1 : 0) });
                    throw new Exception("Actual Mono snapshot budget was not enforced.");
                }
                catch (TargetInvocationException e)
                {
                    if (!e.ToString().Contains("SnapshotLimitException")) throw;
                }
            }
            var expectedSequence = Sequence();
            cash.SetValue(root, 999);
            unit.SetValue(item, 71);
            floatField.SetValue(tag, BitConverter.Int32BitsToSingle(0x7fc01234));
            closureField.SetValue(closure, "after");
            tupleItem.SetValue(tuple, tag);
            shapeData[0] = 99;
            shapeDataField.SetValue(shape, new byte[] { 7, 8, 9 });
            list.Clear();
            for (int i = 0; i < 130; i++) list.Add(tag);
            dictionary.Remove("item");
            for (int i = 0; i < 130; i++) dictionary["added" + i] = list;
            hashSet.Remove(tag);
            for (int i = 0; i < 130; i++) hashSet.Add(new int[] { i });
            pairMap["pair"] = Activator.CreateInstance(pairType, new object[] { null, 3f });
            second = Capture();
            for (int round = 0; round < 6; round++)
            {
                Restore(first);
                if ((int)cash.GetValue(root) != 135 || (int)unit.GetValue(item) != 2
                    || !ReferenceEquals(list[0], item) || !ReferenceEquals(list[2], item)
                    || !ReferenceEquals(dictionary["list"], list) || hashSet.Count != 3 || !hashSet.Contains(tag)
                    || !ReferenceEquals(typeof(Dictionary<string, object>).GetField("_entries", instance).GetValue(dictionary), originalEntries)
                    || !ReferenceEquals(typeof(List<object>).GetField("_items", instance).GetValue(list), originalList)
                    || !ReferenceEquals(typeof(HashSet<object>).GetField("_slots", instance).GetValue(hashSet), originalSlots)
                    || (string)closureField.GetValue(closure) != "before"
                    || !ReferenceEquals(tuple.Item1, item) || !ReferenceEquals(shapeDataField.GetValue(shape), shapeData)
                    || shapeData[0] != 1
                    || !ReferenceEquals(pairType.GetProperty("Key").GetValue(pairMap["pair"]), tag)
                    || BitConverter.SingleToInt32Bits((float)pairType.GetProperty("Value").GetValue(pairMap["pair"])) != unchecked((int)0x80000000)
                    || BitConverter.SingleToInt32Bits((float)floatField.GetValue(tag)) != unchecked((int)0x80000000)
                    || !Sequence().SequenceEqual(expectedSequence))
                    throw new Exception("Actual Mono past-state content/identity/RNG mismatch.");
                Restore(first);
                var census = graphType.GetMethod("Census", statics)
                    .Invoke(null, new object[] { Access(), roots, 50000, 1000000, 64L * 1024 * 1024 });
                try
                {
                    foreach (string name in new[] { "ObjectCount", "SlotCount", "ByteCount" })
                        if (!Equals(graphType.GetProperty(name, instance).GetValue(first), graphType.GetProperty(name, instance).GetValue(census)))
                            throw new Exception("Actual Mono census parity mismatch: " + name);
                }
                finally { ((IDisposable)census).Dispose(); }
                Restore(second);
                if ((int)cash.GetValue(root) != 999 || (int)unit.GetValue(item) != 71 || list.Count != 130
                    || dictionary.Count != 132 || hashSet.Count != 132 || (string)closureField.GetValue(closure) != "after"
                    || !ReferenceEquals(tuple.Item1, tag) || ((byte[])shapeDataField.GetValue(shape))[0] != 7
                    || pairType.GetProperty("Key").GetValue(pairMap["pair"]) != null
                    || (float)pairType.GetProperty("Value").GetValue(pairMap["pair"]) != 3f
                    || BitConverter.SingleToInt32Bits((float)floatField.GetValue(tag)) != 0x7fc01234)
                    throw new Exception("Actual Mono future-state mismatch.");
            }
            Restore(first);
            cash.SetValue(root, 246);
            var repair = graphType.GetMethod("PrepareReconciliation", instance).Invoke(first, new object[] { true, 128, 8192 });
            repair.GetType().GetMethod("Apply", instance).Invoke(repair, null);
            Verify(first);
            cash.SetValue(root, 358);
            var full = graphType.GetMethod("PrepareReconciliation", instance).Invoke(first, new object[] { false, 128, 8192 });
            if ((string)full.GetType().GetProperty("Mode", instance).GetValue(full) != "full")
                throw new Exception("Actual Mono full-repair path was not exercised.");
            full.GetType().GetMethod("Apply", instance).Invoke(full, null);
            Verify(first);

            var unknown = new UnknownMutable { Value = 17 };
            var unknownAccess = accessType.GetConstructor(instance, null, new[] { typeof(Type), typeof(string) }, null)
                .Invoke(new object[] { unity.GetType("UnityEngine.Object"), "Assembly-CSharp" });
            accessType.GetMethod("AddRoot", instance).Invoke(unknownAccess, new object[] { Id(unknown), Array.Empty<string>() });
            try
            {
                graphType.GetMethod("Capture", statics)
                    .Invoke(null, new object[] { unknownAccess, new[] { Id(unknown) }, 50000, 1000000, 64L * 1024 * 1024 });
                throw new Exception("Unknown static root was silently accepted.");
            }
            catch (TargetInvocationException e)
            {
                if (!e.ToString().Contains("Uncovered static snapshot layout")) throw;
            }
            if (unknown.Value != 17) throw new Exception("Rejected root was mutated.");
            Restore(second);
            Restore(first);
            if (_callbackCount != 0) throw new Exception("A business callback ran during restoration.");
            Console.WriteLine("PASS actual game Mono complete static graph: entity/tag/int blocks and ordinary parity, exact budgets/three limit failures, six bidirectional cycles, Dictionary/HashSet/List/shape original storage, readonly tuple/nested structs, shared identities, closure, NaN/signed-zero, RNG, census, owner/full repair, unknown-root rejection and retained-node survival.");
        }
        finally
        {
            if (second != null) ((IDisposable)second).Dispose();
            ((IDisposable)first).Dispose();
        }
    }

    private sealed class UnknownMutable { public int Value; }
    private static void CheckItemLayout(Assembly mod, Assembly game, object item)
    {
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var itemType = game.GetType("GameItemElement", true);
        var validate = itemType.GetField("validateLock", instance);
        var grid = itemType.GetField("appliedGridSize", instance);
        var minX = itemType.GetField("appliedMinX", instance);
        var originX = itemType.GetProperty("modifiedXOrigin");
        var originY = itemType.GetProperty("modifiedYOrigin");
        var reset = mod.GetType("ProbablyStolenTools.GameFields", true)
            .GetMethod("ResetItemLayout", BindingFlags.Static | BindingFlags.NonPublic);
        validate.SetValue(item, true);
        grid.SetValue(item, 32);
        minX.SetValue(item, 71);
        originX.SetValue(item, 3);
        originY.SetValue(item, -7);
        reset.Invoke(null, new[] { item });
        if ((bool)validate.GetValue(item) || (int)grid.GetValue(item) != -1
            || (int)minX.GetValue(item) != 71 || (int)originX.GetValue(item) != 3
            || (int)originY.GetValue(item) != -7)
            throw new Exception("Fixed item layout reset changed the wrong state.");

        // Measure the same four game-field operations, without Unity rendering or save access.
        var officialGet = game.GetType("ModReflect").GetMethods()
            .Single(method => method.Name == "Get" && !method.IsGenericMethod);
        var officialSet = game.GetType("ModReflect").GetMethod("Set");
        const int count = 1000;
        var resetArgs = new[] { item };
        var lockArgs = new object[] { item, "validateLock", false };
        var gridArgs = new object[] { item, "appliedGridSize", -1 };
        var xArgs = new object[] { item, "modifiedXOrigin" };
        var yArgs = new object[] { item, "modifiedYOrigin" };
        officialSet.Invoke(null, lockArgs);
        officialGet.Invoke(null, xArgs);
        var timing = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
        {
            officialSet.Invoke(null, lockArgs);
            officialSet.Invoke(null, gridArgs);
            officialGet.Invoke(null, xArgs);
            officialGet.Invoke(null, yArgs);
        }
        long officialTicks = timing.ElapsedTicks;
        timing.Restart();
        for (int i = 0; i < count; i++)
        {
            reset.Invoke(null, resetArgs);
            originX.GetValue(item);
            originY.GetValue(item);
        }
        long fixedTicks = timing.ElapsedTicks;
        Console.WriteLine("PASS actual game Mono fixed layout reset: only two cache fields changed, origins retained; "
            + "LAYOUT_ACCESS_BENCH items=" + count + " officialMs="
            + (officialTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F2",
                System.Globalization.CultureInfo.InvariantCulture)
            + " fixedMs=" + (fixedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency).ToString("F2",
                System.Globalization.CultureInfo.InvariantCulture)
            + " runtime=game-Mono-offline-reflection-harness-not-Unity");
    }

    public static void RunVersionMismatch(string path)
    {
        var mod = Assembly.LoadFrom(path);
        var schema = mod.GetType("ProbablyStolenTools.SnapshotStaticLayouts", true);
        try
        {
            schema.GetMethod("Fields", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new[] { Assembly.Load("Assembly-CSharp").GetType("PlayerStore", true) });
            throw new Exception("Mismatched layout version was silently accepted.");
        }
        catch (TargetInvocationException e)
        {
            if (!e.ToString().Contains("Snapshot layout build does not match loaded module:")) throw;
        }
        Console.WriteLine("PASS actual game Mono rejects mismatched generated-module MVID before returning any snapshot fields.");
    }
    private static int _callbackCount;
    private static void NeverInvoke() { _callbackCount++; throw new Exception("Callback must not run."); }
}
