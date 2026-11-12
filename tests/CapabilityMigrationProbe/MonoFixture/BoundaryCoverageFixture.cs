using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static partial class OfflineMonoFixture
{
    private static void CheckBoundaryCoverage(Assembly mod, Assembly game)
    {
        var registry = Assembly.Load("ProbablyStolenTools.StaticLayouts")
            .GetType("ProbablyStolenTools.Generated.SnapshotLayouts", true);
        var known = new HashSet<Type>((Type[])registry.GetMethod("KnownTypes").Invoke(null, null));
        var boundary = mod.GetType("ProbablyStolenTools.SnapshotTraversalBoundary", true);
        var protectedName = boundary.GetMethod("IsProtectedOwner", All);
        var accessType = mod.GetType("ProbablyStolenTools.ManagedStateAccess", true);
        var access = accessType.GetConstructor(All, null, new[] { typeof(Type), typeof(string) }, null)
            .Invoke(new object[] { null, "Assembly-CSharp" });
        var traversable = accessType.GetMethod("Traversable", All);
        var mismatches = new List<string>();
        int excluded = 0, serialization = 0;
        try
        {
            foreach (var type in game.GetTypes())
            {
                var outer = type;
                while (outer.DeclaringType != null) outer = outer.DeclaringType;
                bool protect = (bool)protectedName.Invoke(null, new object[] { outer.Name });
                bool serialize = outer.Name.StartsWith("ES3", StringComparison.Ordinal);
                if (protect || serialize)
                {
                    excluded++;
                    if (serialize) serialization++;
                    if (known.Contains(type)) throw new Exception("Excluded game type has generated layout: " + type);
                    if ((bool)traversable.Invoke(access, new object[] { type }))
                        mismatches.Add(type.FullName);
                }
                else if (!known.Contains(type))
                    throw new Exception("Game type missing from static registry without explicit exclusion: " + type);
            }
        }
        finally { ((IDisposable)access).Dispose(); }
        Console.WriteLine("BOUNDARY_AUDIT excluded=" + excluded + " serialization=" + serialization
            + " admittedWithoutLayout=" + mismatches.Count);
        if (mismatches.Count != 0)
            throw new Exception("Generator/runtime exclusion mismatch: " + string.Join(", ", mismatches.Take(12)));
        Console.WriteLine("PASS game metadata boundary coverage: every excluded owner/nested type is refused traversal; "
            + "all remaining game types have generated layouts.");
        CheckNullableScalarBoundary(mod);
        CheckBoxedScalarBoundary(mod);
    }

    private static void CheckNullableScalarBoundary(Assembly mod)
    {
        var accessType = mod.GetType("ProbablyStolenTools.ManagedStateAccess", true);
        var graphType = mod.GetType("ProbablyStolenTools.StateGraph", true);
        var idMethod = mod.GetType("ProbablyStolenTools.ManagedObjects", true).GetMethod("Id", All);
        IntPtr Id(object value) => (IntPtr)idMethod.Invoke(null, new[] { value });
        object Access() => accessType.GetConstructor(All, null, new[] { typeof(Type), typeof(string) }, null)
            .Invoke(new object[] { null, "Assembly-CSharp" });
        var money = new decimal?[] { null, -1.25m, decimal.MaxValue };
        var dates = new DateTime?[] { null, new DateTime(123456789, DateTimeKind.Utc) };
        var ids = new Guid?[] { null, new Guid("8399bb70-0a6b-4ffd-bbf7-cf802392ee5c") };
        var root = new List<object> { money, dates, ids };
        var graph = graphType.GetMethod("Capture", All).Invoke(null,
            new object[] { Access(), new[] { Id(root) }, 50000, 1000000, 64L * 1024 * 1024 });
        try
        {
            money[0] = 3m; money[1] = null; money[2] = 0m;
            dates[1] = null; ids[1] = null;
            graphType.GetMethod("Restore", All).Invoke(graph, null);
            if (money[0] != null || money[1] != -1.25m || money[2] != decimal.MaxValue
                || dates[1] != new DateTime(123456789, DateTimeKind.Utc)
                || ids[1] != new Guid("8399bb70-0a6b-4ffd-bbf7-cf802392ee5c"))
                throw new Exception("Nullable scalar boundary roundtrip failed.");
        }
        finally { ((IDisposable)graph).Dispose(); }
        var invalid = new KeyValuePair<int, object>?[] { new KeyValuePair<int, object>(1, new object()) };
        try
        {
            var unexpected = graphType.GetMethod("Capture", All).Invoke(null,
                new object[] { Access(), new[] { Id(invalid) }, 50000, 1000000, 64L * 1024 * 1024 });
            ((IDisposable)unexpected).Dispose();
            throw new Exception("Nullable struct containing references was silently accepted.");
        }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException
            && e.InnerException.Message.StartsWith("Nullable snapshot structs containing references", StringComparison.Ordinal)) { }
        Console.WriteLine("PASS actual game Mono nullable scalar boundaries: decimal/date/guid null transitions restore; "
            + "nullable structs containing references still rejected.");
    }

    private static void CheckBoxedScalarBoundary(Assembly mod)
    {
        var accessType = mod.GetType("ProbablyStolenTools.ManagedStateAccess", true);
        var graphType = mod.GetType("ProbablyStolenTools.StateGraph", true);
        var idMethod = mod.GetType("ProbablyStolenTools.ManagedObjects", true).GetMethod("Id", All);
        IntPtr Id(object value) => (IntPtr)idMethod.Invoke(null, new[] { value });
        var boxes = new object[] { true, (byte)17, (sbyte)-2, (short)-3, (ushort)4, 71, 72U, -73L, 74UL,
            'q', BitConverter.Int32BitsToSingle(unchecked((int)0x80000000)),
            BitConverter.Int64BitsToDouble(unchecked((long)0x7FF8000000000042UL)), -1.25m, DayOfWeek.Friday };
        var expected = (object[])boxes.Clone();
        var root = new List<object>(boxes);
        root.Add(boxes[5]); // Aliased boxed identity, not merely equal numeric values.
        var mutations = boxes.SelectMany(owner => owner.GetType()
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            .Select(field => (Owner: owner, Field: field, Value: field.GetValue(owner)))).ToArray();
        var access = accessType.GetConstructor(All, null, new[] { typeof(Type), typeof(string) }, null)
            .Invoke(new object[] { null, "Assembly-CSharp" });
        var graph = graphType.GetMethod("Capture", All).Invoke(null,
            new object[] { access, new[] { Id(root) }, 50000, 1000000, 64L * 1024 * 1024 });
        try
        {
            foreach (var mutation in mutations)
                mutation.Field.SetValue(mutation.Owner, Activator.CreateInstance(mutation.Field.FieldType));
            root[0] = new object();
            graphType.GetMethod("Restore", All).Invoke(graph, null);
            for (int i = 0; i < boxes.Length; i++)
                if (!ReferenceEquals(root[i], expected[i]))
                    throw new Exception("Boxed scalar owner identity changed.");
            if (!ReferenceEquals(root[5], root[root.Count - 1]))
                throw new Exception("Boxed scalar alias changed.");
            foreach (var mutation in mutations)
            {
                object actual = mutation.Field.GetValue(mutation.Owner);
                bool same = actual is float single
                    ? BitConverter.SingleToInt32Bits(single) == BitConverter.SingleToInt32Bits((float)mutation.Value)
                    : actual is double number
                        ? BitConverter.DoubleToInt64Bits(number) == BitConverter.DoubleToInt64Bits((double)mutation.Value)
                        : Equals(actual, mutation.Value);
                if (!same) throw new Exception("Boxed scalar field bits failed to restore.");
            }
            if (((System.Collections.IEnumerable)graphType.GetMethod("Differences", All)
                .Invoke(graph, new object[] { 12 })).Cast<object>().Any())
                throw new Exception("Boxed scalar graph differs after restore.");
        }
        finally { ((IDisposable)graph).Dispose(); }
        Console.WriteLine("PASS actual game Mono boxed scalar boundary: primitives/decimal/framework enum fields restored "
            + "with original identities, aliases and float bits.");
    }
}
