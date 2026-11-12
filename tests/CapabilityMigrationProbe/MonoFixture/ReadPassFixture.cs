using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static partial class OfflineMonoFixture
{
    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

    private static void CheckReadPassAndSharing(Assembly mod)
    {
        var accessType = mod.GetType("ProbablyStolenTools.ManagedStateAccess", true);
        if (accessType.GetProperty("ScopedStructReads", All) == null) return;
        var unity = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object");
        var graphType = mod.GetType("ProbablyStolenTools.StateGraph", true);
        var passType = mod.GetType("ProbablyStolenTools.IStateReadPass", true);
        var independentType = mod.GetType("ProbablyStolenTools.IIndependentStateAccess", true);
        var idMethod = mod.GetType("ProbablyStolenTools.ManagedObjects", true).GetMethod("Id", All);
        IntPtr Id(object value) => (IntPtr)idMethod.Invoke(null, new[] { value });
        object Access(bool fast = true) => accessType.GetConstructor(All, null,
            new[] { typeof(Type), typeof(string), typeof(bool) }, null).Invoke(new object[] { unity, "Assembly-CSharp", fast });
        object Call(object access, string name, params object[] args) => accessType.GetMethod(name, All, null,
            args.Select(arg => arg.GetType()).ToArray(), null).Invoke(access, args);
        object[] Slots(object access, object owner)
        {
            Call(access, "Retain", Id(owner));
            return ((IEnumerable)Call(access, "Describe", Id(owner))).Cast<object>().ToArray();
        }
        string Name(object slot) => (string)slot.GetType().GetProperty("Name", All).GetValue(slot);
        void Pass(object access, string name) => passType.GetMethod(name).Invoke(access, null);
        void EmptyCache(object access)
        {
            if (accessType.GetField("_readArray", All).GetValue(access) != null
                || accessType.GetField("_readArrayElement", All).GetValue(access) != null)
                throw new Exception("Read pass retained a business array/box outside its boundary.");
        }
        var left = new[] { new KeyValuePair<int, object>(17, new object()), new KeyValuePair<int, object>(18, null) };
        var right = new[] { new KeyValuePair<int, object>(29, null), new KeyValuePair<int, object>(30, null) };
        var a = Access();
        var b = Access();
        var slow = Access(false);
        try
        {
            var ls = Slots(a, left);
            var rs = Slots(a, right);
            var bs = Slots(b, left);
            var ss = Slots(slow, left);
            var storage = accessType.GetField("_storage", All);
            object Stores(object access, object owner) => ((IDictionary)storage.GetValue(access))[Id(owner)];
            if (!ReferenceEquals(Stores(a, left), Stores(b, left)) || ReferenceEquals(Stores(a, left), Stores(slow, left)))
                throw new Exception("Immutable schema sharing or fast/slow isolation failed.");
            var key = ls.Single(slot => Name(slot).EndsWith("[0].key", StringComparison.Ordinal));
            var value = ls.Single(slot => Name(slot).EndsWith("[0].value", StringComparison.Ordinal));
            byte[] Read(object access, object owner, object slot) => (byte[])Call(access, "ReadValue", Id(owner), slot);
            var before = Read(a, left, key);
            Pass(a, "BeginReadPass");
            Read(a, left, key);
            Call(a, "ReadReference", Id(left), value);
            if ((long)accessType.GetProperty("ArrayElementReuses", All).GetValue(a) == 0)
                throw new Exception("Struct array reuse was not exercised.");
            Read(a, right, rs.Single(slot => Name(slot).EndsWith("[0].key", StringComparison.Ordinal)));
            if (!(bool)Call(a, "ValueEquals", Id(left), key, before))
                throw new Exception("Struct cache leaked across two arrays at the same index.");
            left[0] = new KeyValuePair<int, object>(71, left[0].Value);
            Call(a, "WriteValue", Id(left), key, before);
            if (left[0].Key != 17 || !(bool)Call(a, "ValueEquals", Id(left), key, before))
                throw new Exception("Write failed to invalidate a cached struct element.");
            Call(a, "WriteReference", Id(left), value, IntPtr.Zero);
            if ((IntPtr)Call(a, "ReadReference", Id(left), value) != IntPtr.Zero)
                throw new Exception("Reference write left a stale struct box.");
            Pass(a, "ResetReadObject");
            EmptyCache(a);
            Pass(a, "EndReadPass");
            EmptyCache(a);
            left[0] = new KeyValuePair<int, object>(81, null);
            if ((bool)Call(a, "ValueEquals", Id(left), key, before))
                throw new Exception("Individual reads reused a previous pass.");
            accessType.GetProperty("ScopedStructReads", All).SetValue(a, false);
            accessType.GetProperty("SharedDescriptions", All).SetValue(a, false);
            var independent = independentType.GetMethod("CreateIndependent").Invoke(a, null);
            try
            {
                foreach (string flag in new[] { "ScopedStructReads", "SharedDescriptions" })
                    if ((bool)accessType.GetProperty(flag, All).GetValue(independent))
                        throw new Exception("Independent access did not inherit " + flag);
            }
            finally { ((IDisposable)independent).Dispose(); }

            // Selected roots must never inherit a full schema from the global cache.
            var pair = Tuple.Create(17, 29);
            Slots(b, pair);
            var selected = Access();
            try
            {
                var full = Slots(selected, pair);
                var secondToken = Read(selected, pair, full.Single(slot => Name(slot).EndsWith(".m_Item2", StringComparison.Ordinal)));
                Call(selected, "AddRoot", Id(pair), new[] { "m_Item2" });
                var chosen = Slots(selected, pair);
                if (chosen.Length != 1 || !Name(chosen[0]).EndsWith(".m_Item2", StringComparison.Ordinal)
                    || !(bool)Call(selected, "ValueEquals", Id(pair), chosen[0], secondToken))
                    throw new Exception("Selected-root coverage widened through shared metadata.");
            }
            finally { ((IDisposable)selected).Dispose(); }
            // Failure cleanup must end the read pass even when Walk disposes the access first.
            var failing = Access();
            try
            {
                graphType.GetMethod("Capture", All).Invoke(null, new object[] { failing, new[] { Id(left) }, 100, 1, 1024L });
                throw new Exception("Expected budget rejection.");
            }
            catch (TargetInvocationException e)
            {
                if (!e.ToString().Contains("SnapshotLimitException")) throw;
            }
            EmptyCache(failing);
            if ((int)accessType.GetField("_readPassDepth", All).GetValue(failing) != 0)
                throw new Exception("Failed capture left an active read pass.");
            ((IDisposable)a).Dispose();
            foreach (string cache in new[] { "_storageOwner", "_ownerStores" })
                if (accessType.GetField(cache, All).GetValue(a) != null)
                    throw new Exception("Disposed access retained its last owner/storage cache.");
            // A disposed peer must not invalidate immutable descriptions retained by another node.
            if (Read(b, left, bs.Single(slot => Name(slot).EndsWith("[0].key", StringComparison.Ordinal))) == null)
                throw new Exception("Disposed peer invalidated shared metadata.");
            // This layout alone exceeds the shared slot budget and must remain node-local.
            if (Slots(b, new byte[65537]).Length != 65537)
                throw new Exception("Over-budget shared layout lost its ordinary description fallback.");
            foreach (string cacheName in new[] { "SharedTypes", "SharedArrays" })
            {
                var cache = (IDictionary)accessType.GetField(cacheName, All).GetValue(null);
                int slots = 0;
                foreach (var schema in cache.Values)
                {
                    var stores = (Array)schema.GetType().GetField("Stores", All).GetValue(schema);
                    slots += stores.Length;
                    foreach (object store in stores)
                        if (store.GetType().GetField("Target", All).GetValue(store) != null)
                            throw new Exception("Shared metadata retained a business target.");
                }
                if (cache.Count > 4096 || slots > 65536) throw new Exception("Shared metadata exceeded bounds.");
            }
            Console.WriteLine("PASS actual game Mono scoped reads/sharing: array-owner isolation, scalar/reference write barriers, pass reset/end/failure cleanup, direct mutation visibility, independent flags, partial roots, fast/slow isolation, peer disposal and bounded target-free metadata.");
        }
        finally
        {
            ((IDisposable)a).Dispose();
            ((IDisposable)b).Dispose();
            ((IDisposable)slow).Dispose();
        }
    }

    public static void RunBenchmark(string path)
    {
        var mod = Assembly.LoadFrom(path);
        var game = Assembly.Load("Assembly-CSharp");
        var unity = Assembly.Load("UnityEngine.CoreModule").GetType("UnityEngine.Object");
        var accessType = mod.GetType("ProbablyStolenTools.ManagedStateAccess", true);
        var graphType = mod.GetType("ProbablyStolenTools.StateGraph", true);
        var idMethod = mod.GetType("ProbablyStolenTools.ManagedObjects", true).GetMethod("Id", All);
        object Data(string name) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType(name, true));
        const int owners = 2000, tagsPerOwner = 16;
        var values = new object[owners];
        var tags = new List<object>();
        var dictionaries = new List<Dictionary<string, object>>();
        var tagInt = game.GetType("TagState", true).GetField("valueInt", All);
        var shapeData = game.GetType("GridShapeBuilder", true).GetField("data", All);
        var modifiedShape = game.GetType("GameItemElement", true).GetProperty("modifiedShape");
        for (int i = 0; i < owners; i++)
        {
            var dictionary = new Dictionary<string, object>();
            var item = Data("GameItemElement");
            var shape = Data("GridShapeBuilder");
            shapeData.SetValue(shape, new byte[] { 1, 0, 1, 0, 1, 1 });
            modifiedShape.SetValue(item, shape);
            dictionary["item"] = item;
            for (int j = 0; j < tagsPerOwner; j++)
            {
                var tag = Data("TagState");
                tagInt.SetValue(tag, j);
                dictionary["tag" + j] = tag;
                tags.Add(tag);
            }
            dictionaries.Add(dictionary);
            values[i] = new List<object> { dictionary, item, shape };
        }
        var id = (IntPtr)idMethod.Invoke(null, new object[] { values });
        object Access(bool optimized, bool census)
        {
            var access = accessType.GetConstructor(All, null,
                new[] { typeof(Type), typeof(string), typeof(bool), typeof(bool), typeof(bool), typeof(bool) }, null)
                .Invoke(new object[] { unity, "Assembly-CSharp", true, true, true, true });
            accessType.GetProperty("ScopedStructReads", All)?.SetValue(access, optimized);
            accessType.GetProperty("SharedDescriptions", All)?.SetValue(access, optimized);
            accessType.GetMethod("AddRoot", All).Invoke(access, new object[] { id, Array.Empty<string>() });
            mod.GetType("ProbablyStolenTools.SemanticTagBlock").GetMethod("Enable", All).Invoke(null, new[] { access });
            mod.GetType("ProbablyStolenTools.SemanticEntityBlocks").GetMethod("Enable", All).Invoke(null, new[] { access });
            if (census) mod.GetType("ProbablyStolenTools.SemanticTagBlock").GetMethod("EnableCensus", All).Invoke(null, new[] { access });
            return access;
        }
        var expectedCounts = new Dictionary<string, object>();
        for (int round = 0; round < 8; round++)
        {
            bool optimized = round % 2 != 0;
            // Keep discarded checkpoints from biasing whichever mode happens to run next.
            // These collections belong only to the offline benchmark, never the mod.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var times = new List<double>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            void Lap() { times.Add(watch.ElapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency); watch.Restart(); }
            var access = Access(optimized, false);
            var graph = graphType.GetMethod("Capture", All).Invoke(null,
                new object[] { access, new[] { id }, 200000, 3000000, 256L * 1024 * 1024 });
            Lap();
            try
            {
                foreach (string metric in new[] { "ObjectCount", "SlotCount", "ByteCount" })
                {
                    object actual = graphType.GetProperty(metric, All).GetValue(graph);
                    if (expectedCounts.TryGetValue(metric, out var expected) && !Equals(expected, actual))
                        throw new Exception("Large graph budget parity failed: " + metric);
                    expectedCounts[metric] = actual;
                }
                // Match restore ordering: current live values differ before census/preflight.
                for (int i = 0; i < tags.Count; i += tagsPerOwner) tagInt.SetValue(tags[i], 91);
                foreach (var dictionary in dictionaries) dictionary["item"] = null;
                watch.Restart();
                var census = (graphType.GetMethod("CensusForRestore", All) ?? graphType.GetMethod("Census", All)).Invoke(null,
                    new object[] { Access(optimized, true), new[] { id }, 200000, 3000000, 256L * 1024 * 1024 });
                string censusMode = (string)graphType.GetProperty("CensusMode", All)?.GetValue(census) ?? "full-read";
                Lap();
                try
                {
                    foreach (var metric in expectedCounts)
                        if (!Equals(metric.Value, graphType.GetProperty(metric.Key, All).GetValue(census)))
                            throw new Exception("Large graph census parity failed: " + metric.Key);
                }
                finally { ((IDisposable)census).Dispose(); }
                void Verify()
                {
                    if (((ICollection)graphType.GetMethod("Differences", All).Invoke(graph, new object[] { 12 })).Count != 0)
                        throw new Exception("Large graph full verification failed.");
                }
                watch.Restart();
                string preflightMode;
                var preflight = graphType.GetMethod("ValidateRestore", All);
                if (preflight != null) preflightMode = (string)preflight.Invoke(graph, null);
                else
                {
                    graphType.GetMethod("Differences", All).Invoke(graph, new object[] { 12 });
                    preflightMode = "full-read";
                }
                Lap();
                watch.Restart();
                graphType.GetMethod("Restore", All).Invoke(graph, null); Lap();
                Verify(); Lap();
                // Simulate a native refresh changing a small captured owner.
                tagInt.SetValue(tags[0], 91);
                watch.Restart();
                var repair = graphType.GetMethod("PrepareReconciliation", All).Invoke(graph, new object[] { true, 128, 8192 });
                repair.GetType().GetMethod("Apply", All).Invoke(repair, null); Lap();
                Verify();
                for (int i = 0; i < owners; i++)
                    if (dictionaries[i]["item"] == null || (int)tagInt.GetValue(tags[i * tagsPerOwner]) != 0)
                        throw new Exception("Large graph content restoration failed.");
                if (accessType.GetField("_readArray", All)?.GetValue(access) != null)
                    throw new Exception("Large graph verification retained a boxed array element.");
                Console.WriteLine("GRAPH_BATCH_BENCH runtime=game-Mono-offline-not-Unity round=" + round
                    + " optimized=" + optimized + " " + string.Join(" ", expectedCounts.Select(p => p.Key + "=" + p.Value))
                    + " ms(capture,census,preflight,write,verify,reconcile)="
                    + string.Join(",", times.Select(t => t.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)))
                    + " loads=" + accessType.GetProperty("ArrayElementLoads", All)?.GetValue(access)
                    + " reuses=" + accessType.GetProperty("ArrayElementReuses", All)?.GetValue(access) + " diff=0");
                Console.WriteLine("RESTORE_STAGE_MODES census=" + censusMode + " preflight=" + preflightMode);
            }
            finally { ((IDisposable)graph).Dispose(); }
        }
    }
}
