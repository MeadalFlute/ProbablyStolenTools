#if STATIC_SNAPSHOT_LAYOUTS
using System;
using System.Collections.Generic;
using System.Reflection;
using ProbablyStolenTools.Generated;

namespace ProbablyStolenTools
{
    // Tables contain metadata only. Unknown layouts never fall back to runtime enumeration.
    internal static class SnapshotStaticLayouts
    {
        private static readonly Lazy<Dictionary<Type, MethodInfo>> Factories = new(CreateFactories);
        private static readonly Dictionary<Type, FieldInfo[]> Closed = new();
        private const int ClosedCacheLimit = 8192;

        private static Dictionary<Type, MethodInfo> CreateFactories()
        {
            var modules = SnapshotLayouts.ExpectedModules();
            if (modules.Length == 0 || modules.Length % 2 != 0)
                throw new InvalidOperationException("Invalid generated snapshot layout registry.");
            var expected = new Dictionary<string, Guid>(StringComparer.Ordinal);
            for (int i = 0; i < modules.Length; i += 2) expected.Add(modules[i], new Guid(modules[i + 1]));
            // These stable anchors contain no generated game type tokens. Check
            // them BEFORE JITting KnownTypes, whose closure tokens may no longer exist.
            var loaded = new[] { typeof(PlayerStore).Module, typeof(object).Module,
                typeof(HashSet<>).Module, typeof(UnityEngine.Vector3).Module };
            var actual = new Dictionary<string, Guid>(StringComparer.Ordinal);
            foreach (var module in loaded) actual[module.ScopeName] = module.ModuleVersionId;
            foreach (var pair in expected)
                if (!actual.TryGetValue(pair.Key, out var version) || version != pair.Value)
                    throw new InvalidOperationException("Snapshot layout build does not match loaded module: " + pair.Key
                        + " expected=" + pair.Value + " actual=" + (actual.TryGetValue(pair.Key, out version) ? version.ToString() : "missing"));
            if (expected.Count != actual.Count)
                throw new InvalidOperationException("Invalid generated snapshot layout module coverage.");
            var types = SnapshotLayouts.KnownTypes();
            var methods = SnapshotLayouts.KnownFactories();
            if (types.Length != methods.Length)
                throw new InvalidOperationException("Invalid generated snapshot layout registry.");
            var result = new Dictionary<Type, MethodInfo>();
            var checkedModules = new HashSet<Module>();
            for (int i = 0; i < types.Length; i++)
            {
                var module = types[i].Module;
                if (checkedModules.Add(module)
                    && (!expected.TryGetValue(module.ScopeName, out var version) || module.ModuleVersionId != version))
                    throw new InvalidOperationException("Snapshot layout build does not match loaded module: " + module.ScopeName);
                result.Add(types[i], methods[i]);
            }
            return result;
        }

        internal static FieldInfo[] Fields(Type type)
        {
            lock (Closed)
            {
                if (Closed.TryGetValue(type, out var fields)) return fields;
                var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
                if (!Factories.Value.TryGetValue(definition, out var factory))
                    throw new InvalidOperationException("Uncovered static snapshot layout: " + type.FullName);
                if (factory.IsGenericMethodDefinition) factory = factory.MakeGenericMethod(type.GetGenericArguments());
                fields = (FieldInfo[])factory.Invoke(null, null);
                foreach (var field in fields)
                    if (field == null || field.DeclaringType != type || field.IsStatic)
                        throw new InvalidOperationException("Invalid static snapshot field: " + type.FullName);
                if (Closed.Count < ClosedCacheLimit) Closed.Add(type, fields);
                return fields;
            }
        }
    }
}
#endif
