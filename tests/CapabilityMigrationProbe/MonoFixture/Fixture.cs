using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static partial class OfflineMonoFixture
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
        CheckVectorReaders(mod);
        CheckScalarComparisons(mod);
        CheckReadPassAndSharing(mod);
        CheckProtectedBoundary(mod, game);
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
            var preflight = graphType.GetMethod("ValidateRestore", instance);
            if (preflight != null)
            {
                long typed = (long)graphType.GetProperty("TypedComparisonCount", instance).GetValue(graph);
                long references = (long)graphType.GetProperty("ReferenceComparisonCount", instance).GetValue(graph);
                if ((string)preflight.Invoke(graph, null) != "retained-storage"
                    || typed != (long)graphType.GetProperty("TypedComparisonCount", instance).GetValue(graph)
                    || references != (long)graphType.GetProperty("ReferenceComparisonCount", instance).GetValue(graph))
                    throw new Exception("Actual Mono stable preflight performed a discarded value scan.");
            }
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
            var stablePreflight = graphType.GetMethod("ValidateRestore", instance);
            if (stablePreflight != null)
            {
                var retained = (IDictionary)accessType.GetField("_retained", instance).GetValue(capturedAccess);
                var retainedOwner = retained[roots[0]];
                retained[roots[0]] = new object();
                try
                {
                    stablePreflight.Invoke(first, null);
                    throw new Exception("Actual Mono stable preflight accepted changed retained identity.");
                }
                catch (TargetInvocationException e)
                {
                    if (!e.ToString().Contains("Managed snapshot identity changed")) throw;
                }
                finally { retained[roots[0]] = retainedOwner; }
                var disposedGraph = Capture();
                try
                {
                    ((IDisposable)graphType.GetField("_access", instance).GetValue(disposedGraph)).Dispose();
                    try
                    {
                        stablePreflight.Invoke(disposedGraph, null);
                        throw new Exception("Actual Mono stable preflight accepted a disposed access.");
                    }
                    catch (TargetInvocationException e)
                    {
                        if (!(e.InnerException is ObjectDisposedException)) throw;
                    }
                }
                finally { ((IDisposable)disposedGraph).Dispose(); }
            }
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
            var restoreCensus = graphType.GetMethod("CensusForRestore", statics);
            if (restoreCensus != null)
            {
                var exactTopology = restoreCensus.Invoke(null,
                    new object[] { Access(), roots, objectCount, slotCount, byteCount });
                ((IDisposable)exactTopology).Dispose();
                foreach (int budget in new[] { 0, 1, 2 })
                {
                    try
                    {
                        restoreCensus.Invoke(null, new object[] { Access(), roots,
                            objectCount - (budget == 0 ? 1 : 0), slotCount - (budget == 1 ? 1 : 0),
                            byteCount - (budget == 2 ? 1 : 0) });
                        throw new Exception("Actual Mono topology census budget was not enforced.");
                    }
                    catch (TargetInvocationException e)
                    {
                        if (!e.ToString().Contains("SnapshotLimitException")) throw;
                    }
                }
            }
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
                var topologyMethod = graphType.GetMethod("CensusForRestore", statics);
                if (topologyMethod != null)
                {
                    var topologyAccess = Access();
                    mod.GetType("ProbablyStolenTools.SemanticTagBlock").GetMethod("EnableCensus", statics)
                        .Invoke(null, new[] { topologyAccess });
                    var topology = topologyMethod.Invoke(null,
                        new object[] { topologyAccess, roots, 50000, 1000000, 64L * 1024 * 1024 });
                    try
                    {
                        foreach (string name in new[] { "ObjectCount", "SlotCount", "ByteCount" })
                            if (!Equals(graphType.GetProperty(name, instance).GetValue(first),
                                graphType.GetProperty(name, instance).GetValue(topology)))
                                throw new Exception("Actual Mono topology census budget parity mismatch: " + name);
                        if ((string)graphType.GetProperty("CensusMode", instance).GetValue(topology) != "topology"
                            || (int)accessType.GetProperty("CensusValueObjectCount", instance).GetValue(topologyAccess) != 0)
                            throw new Exception("Actual Mono topology census invoked discarded scalar probes.");
                        var objects = ((IEnumerable)graphType.GetProperty("Objects", instance).GetValue(first)).Cast<IntPtr>().ToArray();
                        var currentObjects = ((IEnumerable)graphType.GetProperty("Objects", instance).GetValue(topology)).Cast<IntPtr>().ToArray();
                        if (!new HashSet<IntPtr>(objects).SetEquals(currentObjects))
                            throw new Exception("Actual Mono topology census lost an identity.");
                        foreach (var identity in objects)
                            if (!Equals(graphType.GetMethod("SourceOf", instance).Invoke(first, new object[] { identity }),
                                graphType.GetMethod("SourceOf", instance).Invoke(topology, new object[] { identity })))
                                throw new Exception("Actual Mono topology census changed a source path.");
                    }
                    finally { ((IDisposable)topology).Dispose(); }
                }
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
            if (restoreCensus != null)
            {
                var unknownTopologyAccess = accessType.GetConstructor(instance, null, new[] { typeof(Type), typeof(string) }, null)
                    .Invoke(new object[] { unity.GetType("UnityEngine.Object"), "Assembly-CSharp" });
                accessType.GetMethod("AddRoot", instance).Invoke(unknownTopologyAccess,
                    new object[] { Id(unknown), Array.Empty<string>() });
                try
                {
                    restoreCensus.Invoke(null, new object[] { unknownTopologyAccess, new[] { Id(unknown) },
                        50000, 1000000, 64L * 1024 * 1024 });
                    throw new Exception("Actual Mono topology census silently accepted an unknown root.");
                }
                catch (TargetInvocationException e)
                {
                    if (!e.ToString().Contains("Uncovered static snapshot layout")) throw;
                }
            }
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

    private delegate bool VectorRead(object owner, int index, out object value);
    private delegate bool VectorEquals(object owner, int index, object expected, out bool same);

    private sealed class ComparisonSample
    {
        public int Number = 17;
        public bool Flag = true;
        public float Single = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        public double Double = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000001));
        public decimal Money = 17.25m;
        public DayOfWeek Enum = DayOfWeek.Friday;
        public int? Nullable = 17;
        public float? NullableSingle = BitConverter.Int32BitsToSingle(unchecked((int)0x80000000));
        public object Reference = new object();
        public IntPtr Pointer = new IntPtr(17);
        public UIntPtr UnsignedPointer = new UIntPtr(17);
        public RejectEquals Custom = new RejectEquals();
    }

    private struct RejectEquals
    {
        public override bool Equals(object other) => throw new Exception("Custom Equals must not run in typed comparisons.");
        public override int GetHashCode() => 0;
    }

    private static void CheckScalarComparisons(Assembly mod)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        var type = mod.GetType("ProbablyStolenTools.SnapshotFieldReader", true);
        var sample = new ComparisonSample();
        VectorEquals Compare(string name)
        {
            var field = typeof(ComparisonSample).GetField(name);
            var reader = type.GetMethod("Get", flags).Invoke(null, new object[] {
                typeof(ComparisonSample), "scalar-test:" + name, new[] { field }, false });
            return (VectorEquals)Delegate.CreateDelegate(typeof(VectorEquals), reader, type.GetMethod("TryEquals", flags));
        }
        foreach (var entry in new (string Name, object Changed)[]
        {
            ("Number", 18), ("Flag", false), ("Single", 0f),
            ("Double", BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000002))),
            ("Money", 18.25m), ("Enum", DayOfWeek.Monday), ("Nullable", null), ("NullableSingle", 0f)
        })
        {
            var field = typeof(ComparisonSample).GetField(entry.Name);
            var compare = Compare(entry.Name);
            object expected = field.GetValue(sample);
            if (!compare(sample, -1, expected, out bool same) || !same
                || !compare(sample, -1, entry.Changed, out same) || same)
                throw new Exception("Scalar comparison semantics changed: " + entry.Name);
            field.SetValue(sample, entry.Changed);
            if (!compare(sample, -1, expected, out same) || same
                || !compare(sample, -1, entry.Changed, out same) || !same)
                throw new Exception("Changed/null scalar comparison failed: " + entry.Name);
            field.SetValue(sample, expected);
        }
        foreach (string name in new[] { "Reference", "Pointer", "UnsignedPointer", "Custom" })
        {
            object value = typeof(ComparisonSample).GetField(name).GetValue(sample);
            if (Compare(name)(sample, -1, value, out _))
                throw new Exception("Unsupported scalar comparer was enabled: " + name);
        }
        foreach (string name in new[] { "Number", "Single", "Nullable" })
        {
            var compare = Compare(name);
            object expected = typeof(ComparisonSample).GetField(name).GetValue(sample);
            double Measure()
            {
                int matched = 0;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int i = 0; i < 262144; i++)
                    if (compare(sample, -1, expected, out bool same) && same) matched++;
                watch.Stop();
                if (matched != 262144) throw new Exception("Scalar benchmark failed equality.");
                return watch.ElapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
            Measure();
            var times = new double[5];
            for (int i = 0; i < times.Length; i++) times[i] = Measure();
            Array.Sort(times);
            Console.WriteLine("SCALAR_GATE_BENCH runtime=game-Mono-offline-not-Unity field=" + name
                + " comparisons=262144 samples=5 medianMs=" + times[2].ToString("F2",
                    System.Globalization.CultureInfo.InvariantCulture));
        }
        Console.WriteLine("PASS actual game Mono scalar comparison gates: primitive/enum/decimal/nullable, null transitions, float bits; reference/pointer/custom struct refused without custom Equals.");
    }

    private static void CheckVectorReaders(Assembly mod)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        var type = mod.GetType("ProbablyStolenTools.SnapshotFieldReader", true);
        var get = type.GetMethod("Get", flags);
        var readMethod = type.GetMethod("TryRead", flags);
        var equalsMethod = type.GetMethod("TryEquals", flags);
        var direct = type.GetField("_readElement", flags);
        object Reader(Array array, FieldInfo[] path) => get.Invoke(null,
            new object[] { array.GetType(), "vector-test:" + string.Join(".", path.Select(field => field.Name)), path, true });
        object shared = new object();
        var arrays = new Array[]
        {
            new byte[] { 0, 1, 255 }, new bool[] { false, true, false },
            new int[] { int.MinValue, 0, int.MaxValue }, new uint[] { 0, 7, uint.MaxValue },
            new long[] { long.MinValue, 0, long.MaxValue }, new ulong[] { 0, 7, ulong.MaxValue },
            new short[] { short.MinValue, 0, short.MaxValue }, new ushort[] { 0, 7, ushort.MaxValue },
            new sbyte[] { sbyte.MinValue, 0, sbyte.MaxValue }, new char[] { '\0', 'a', '\uffff' },
            new float[] { BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)), 0,
                BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00001)),
                BitConverter.Int32BitsToSingle(unchecked((int)0x7fc00002)), float.PositiveInfinity },
            new double[] { BitConverter.Int64BitsToDouble(unchecked((long)0x8000000000000000)), 0,
                BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000001)),
                BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8000000000002)), double.NegativeInfinity },
            new decimal[] { decimal.MinValue, 0, decimal.MaxValue },
            new object[] { null, shared, shared }, new string[] { null, "one", "two" },
            new byte[][] { null, new byte[] { 1 }, new byte[] { 2, 3 } },
            new int?[] { null, 0, int.MaxValue }, new DayOfWeek[] { DayOfWeek.Sunday, DayOfWeek.Monday }
        };
        foreach (var array in arrays)
        {
            var reader = Reader(array, Array.Empty<FieldInfo>());
            var read = (VectorRead)Delegate.CreateDelegate(typeof(VectorRead), reader, readMethod);
            var compare = (VectorEquals)Delegate.CreateDelegate(typeof(VectorEquals), reader, equalsMethod);
            bool fallback = array.GetType() == typeof(int?[]) || array.GetType() == typeof(DayOfWeek[]);
            if ((direct.GetValue(reader) == null) != fallback)
                throw new Exception("Wrong vector reader selection: " + array.GetType());
            for (int i = 0; i < array.Length; i++)
            {
                object expected = array.GetValue(i);
                if (!read(array, i, out var actual) || !Exact(expected, actual))
                    throw new Exception("Vector reader mismatch: " + array.GetType() + " index=" + i);
                bool scalar = array.GetType().GetElementType().IsValueType;
                bool compared = compare(array, i, expected, out bool same);
                if (compared != scalar || scalar && !same)
                    throw new Exception("Vector comparison mismatch: " + array.GetType());
                if (scalar)
                {
                    object changed = array.GetValue((i + 1) % array.Length);
                    if (!compare(array, i, changed, out same) || same != Exact(expected, changed))
                        throw new Exception("Vector changed comparison mismatch: " + array.GetType());
                }
            }
        }
        bool Exact(object left, object right)
        {
            if (left is float f) return right is float other && BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits(other);
            if (left is double d) return right is double other && BitConverter.DoubleToInt64Bits(d) == BitConverter.DoubleToInt64Bits(other);
            if (left == null || !left.GetType().IsValueType) return ReferenceEquals(left, right);
            return Equals(left, right);
        }
        var pairs = new[] { new KeyValuePair<int, object>(1, shared), new KeyValuePair<int, object>(2, null) };
        var path = new[] { typeof(KeyValuePair<int, object>).GetField("value", BindingFlags.Instance | BindingFlags.NonPublic) };
        var nestedReader = Reader(pairs, path);
        var nested = (VectorRead)Delegate.CreateDelegate(typeof(VectorRead), nestedReader, readMethod);
        if (direct.GetValue(nestedReader) != null || !nested(pairs, 0, out var first) || !ReferenceEquals(first, shared)
            || !nested(pairs, 1, out var second) || second != null)
            throw new Exception("Struct vector reflective path changed.");
        var nonvector = Array.CreateInstance(typeof(int), new[] { 2 }, new[] { 1 });
        var nonvectorReader = Reader(nonvector, Array.Empty<FieldInfo>());
        var nonvectorRead = (VectorRead)Delegate.CreateDelegate(typeof(VectorRead), nonvectorReader, readMethod);
        nonvector.SetValue(17, 1);
        if (direct.GetValue(nonvectorReader) != null || !nonvectorRead(nonvector, 1, out var nonvectorValue)
            || !Equals(nonvectorValue, 17))
            throw new Exception("Nonvector array did not retain reflective indexing.");

        var vectorRoots = arrays.Cast<object>().Concat(new object[] { pairs }).ToArray();
        var allArrays = arrays.Concat(new Array[] { pairs }).ToArray();
        var originals = allArrays.Select(array => (Array)array.Clone()).ToArray();
        var accessType = mod.GetType("ProbablyStolenTools.ManagedStateAccess", true);
        var graphType = mod.GetType("ProbablyStolenTools.StateGraph", true);
        var id = (IntPtr)mod.GetType("ProbablyStolenTools.ManagedObjects", true).GetMethod("Id", flags)
            .Invoke(null, new object[] { vectorRoots });
        object Capture(bool fast)
        {
            var access = accessType.GetConstructor(flags, null,
                new[] { typeof(Type), typeof(string), typeof(bool) }, null)
                .Invoke(new object[] { Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object"), "Assembly-CSharp", fast });
            accessType.GetMethod("AddRoot", flags).Invoke(access, new object[] { id, Array.Empty<string>() });
            return graphType.GetMethod("Capture", flags).Invoke(null,
                new object[] { access, new[] { id }, 10000, 100000, 16L * 1024 * 1024 });
        }
        var ordinary = Capture(false);
        object fastGraph = null;
        try
        {
            fastGraph = Capture(true);
            foreach (string name in new[] { "ObjectCount", "SlotCount", "ByteCount" })
                if (!Equals(graphType.GetProperty(name, flags).GetValue(ordinary),
                    graphType.GetProperty(name, flags).GetValue(fastGraph)))
                    throw new Exception("Vector graph budget parity failed: " + name);
            for (int cycle = 0; cycle < 3; cycle++)
                foreach (var graph in new[] { ordinary, fastGraph })
                {
                    foreach (var array in allArrays)
                    {
                        var element = array.GetType().GetElementType();
                        object zero = element.IsValueType ? Activator.CreateInstance(element) : null;
                        for (int i = 0; i < array.Length; i++) array.SetValue(zero, i);
                    }
                    var before = (ICollection)graphType.GetMethod("Differences", flags).Invoke(graph, new object[] { 12 });
                    if (before.Count == 0) throw new Exception("Vector graph mutations were not detected.");
                    graphType.GetMethod("Restore", flags).Invoke(graph, null);
                    var after = (ICollection)graphType.GetMethod("Differences", flags).Invoke(graph, new object[] { 12 });
                    if (after.Count != 0) throw new Exception("Vector graph did not restore completely.");
                    for (int a = 0; a < allArrays.Length; a++)
                        for (int i = 0; i < allArrays[a].Length; i++)
                            if (!Exact(originals[a].GetValue(i), allArrays[a].GetValue(i)))
                                throw new Exception("Restored vector bits/identities mismatch.");
                }
        }
        finally
        {
            if (fastGraph != null) ((IDisposable)fastGraph).Dispose();
            ((IDisposable)ordinary).Dispose();
        }

        foreach (var array in new Array[] { new byte[4096], new object[4096] })
        {
            var reader = Reader(array, Array.Empty<FieldInfo>());
            var read = (VectorRead)Delegate.CreateDelegate(typeof(VectorRead), reader, readMethod);
            bool Reflect(object owner, int index, out object value) { value = ((Array)owner).GetValue(index); return true; }
            double Measure(VectorRead operation)
            {
                object sink = null;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                for (int repeat = 0; repeat < 64; repeat++)
                    for (int i = 0; i < array.Length; i++) operation(array, i, out sink);
                watch.Stop();
                GC.KeepAlive(sink);
                return watch.ElapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
            Measure(read);
            Measure(Reflect);
            var baseline = new double[5];
            var optimized = new double[5];
            for (int i = 0; i < 5; i++) { baseline[i] = Measure(Reflect); optimized[i] = Measure(read); }
            Array.Sort(baseline);
            Array.Sort(optimized);
            Console.WriteLine("VECTOR_READ_BENCH runtime=game-Mono-offline-not-Unity type=" + array.GetType().Name
                + " elements=262144 samples=5 reflectiveMs=" + baseline[2].ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                + " vectorMs=" + optimized[2].ToString("F2", System.Globalization.CultureInfo.InvariantCulture));
        }
        Console.WriteLine("PASS actual game Mono vector readers: primitives, nullable/enum fallbacks, float bits, null/shared/covariant/jagged references, nested struct fields; ordinary/fast graph budget parity and six full mutation restores.");
    }

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
