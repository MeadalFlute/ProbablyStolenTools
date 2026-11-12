using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static partial class OfflineMonoFixture
{
    private static void CheckProtectedBoundary(Assembly mod, Assembly game)
    {
        var accessType = mod.GetType("ProbablyStolenTools.ManagedStateAccess", true);
        var graphType = mod.GetType("ProbablyStolenTools.StateGraph", true);
        var boundary = mod.GetType("ProbablyStolenTools.SnapshotTraversalBoundary", true);
        var idMethod = mod.GetType("ProbablyStolenTools.ManagedObjects", true).GetMethod("Id", All);
        IntPtr Id(object value) => (IntPtr)idMethod.Invoke(null, new[] { value });
        object Access() => accessType.GetConstructor(All, null,
            new[] { typeof(Type), typeof(string) }, null).Invoke(new object[] { null, "Assembly-CSharp" });
        var protectedName = boundary.GetMethod("IsProtectedOwner", All);
        bool Protected(Type type)
        {
            while (type.DeclaringType != null) type = type.DeclaringType;
            return (bool)protectedName.Invoke(null, new object[] { type.Name });
        }
        // Metadata and uninitialized tokens only: never run registry/trust constructors or read their fields.
        var types = game.GetTypes().Where(Protected).ToArray();
        var closure = game.GetType("PaperBookRegistry+<>c", true);
        if (!types.Contains(closure)) throw new Exception("Reported registry closure is not protected.");
        var owners = types.Where(t => !t.IsAbstract && !t.ContainsGenericParameters && !t.IsValueType)
            .Select(t => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(t)).ToArray();
        var root = new List<object>(owners);
        var token = owners.First(o => o.GetType() == closure);
        var callback = Delegate.CreateDelegate(typeof(Action), token,
            typeof(OfflineMonoFixture).GetMethod("IgnoreProtectedOwner", All));
        root.Add(callback);
        var expected = root.ToArray();
        object Make(string method)
            => graphType.GetMethod(method, All).Invoke(null,
                new object[] { Access(), new[] { Id(root) }, 50000, 1000000, 64L * 1024 * 1024 });
        var captured = Make("Capture");
        try
        {
            foreach (var owner in owners)
            {
                var access = Access();
                try
                {
                    accessType.GetMethod("Retain", All).Invoke(access, new object[] { Id(owner) });
                    var slots = (IEnumerable)accessType.GetMethod("Describe", All)
                        .Invoke(access, new object[] { Id(owner) });
                    if (slots.Cast<object>().Any()) throw new Exception("Protected fields were described.");
                }
                finally { ((IDisposable)access).Dispose(); }
            }
            root.Clear();
            graphType.GetMethod("Restore", All).Invoke(captured, null);
            if (!root.SequenceEqual(expected)) throw new Exception("Protected reference identities were not restored.");
            if (((IEnumerable)graphType.GetMethod("Differences", All).Invoke(captured, new object[] { 12 }))
                .Cast<object>().Any()) throw new Exception("Protected graph differs after restore.");
            var census = Make("CensusForRestore");
            try
            {
                if (!Equals(graphType.GetProperty("ObjectCount", All).GetValue(captured),
                    graphType.GetProperty("ObjectCount", All).GetValue(census)))
                    throw new Exception("Protected census topology differs.");
            }
            finally { ((IDisposable)census).Dispose(); }

            var rejected = Access();
            try
            {
                accessType.GetMethod("AddRoot", All).Invoke(rejected,
                    new object[] { Id(token), Array.Empty<string>() });
                try
                {
                    graphType.GetMethod("Capture", All).Invoke(null,
                        new object[] { rejected, new[] { Id(token) }, 50000, 1000000, 64L * 1024 * 1024 });
                    throw new Exception("Protected explicit root accepted.");
                }
                catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException
                    && e.InnerException.Message.StartsWith("Protected snapshot root:", StringComparison.Ordinal)) { }
            }
            finally { ((IDisposable)rejected).Dispose(); }
        }
        finally { ((IDisposable)captured).Dispose(); }
        Console.WriteLine("PASS actual game Mono protected boundary: registry closure and protected nested owners opaque; "
            + "delegate target, identity roundtrip, topology census and explicit root rejection; no protected fields inspected.");
    }

    private static void IgnoreProtectedOwner(object owner) { }
}
