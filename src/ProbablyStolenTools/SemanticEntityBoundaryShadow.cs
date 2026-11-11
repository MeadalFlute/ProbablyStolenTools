#if MONO
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ProbablyStolenTools
{
    internal static class SemanticEntityBoundaryShadow
    {
        internal static SemanticEntityBoundaryAudit Capture(StateGraph graph, int maxObjects)
        {
            var clock = Stopwatch.StartNew();
            var types = new Dictionary<Type, (SemanticEntityRole Role, bool Native)>();
            bool Native(Type type, Type baseType)
            {
                for (var current = type; current != baseType; current = current.BaseType)
                    if (current == null || current.Assembly != baseType.Assembly) return false;
                return true;
            }
            (SemanticEntityRole, bool) Classify(Type type)
            {
                if (typeof(GameItem).IsAssignableFrom(type)) return (SemanticEntityRole.Item, Native(type, typeof(GameItem)));
                if (typeof(GameGridInventory).IsAssignableFrom(type)) return (SemanticEntityRole.GridInventory, Native(type, typeof(GameInventory)));
                if (typeof(GameSlotInventory).IsAssignableFrom(type)) return (SemanticEntityRole.SlotInventory, Native(type, typeof(GameInventory)));
                if (typeof(GameInventory).IsAssignableFrom(type)) return (SemanticEntityRole.OtherInventory, Native(type, typeof(GameInventory)));
                if (type.IsArray) return (SemanticEntityRole.Array, true);
                // Metadata heuristic only: ordinary object methods used as callbacks can remain unclassified.
                if (typeof(Delegate).IsAssignableFrom(type) || type.IsDefined(typeof(CompilerGeneratedAttribute), false))
                    return (SemanticEntityRole.Callback, true);
                return (SemanticEntityRole.None, true);
            }
            IEnumerable<SemanticEntityObject> Objects()
            {
                foreach (var id in graph.Objects)
                {
                    var type = ManagedObjects.Get(id).GetType();
                    if (!types.TryGetValue(type, out var entry))
                    {
                        entry = Classify(type);
                        if (types.Count < 4096) types.Add(type, entry);
                    }
                    if (entry.Role != SemanticEntityRole.None)
                        yield return new SemanticEntityObject(id, entry.Role, entry.Native);
                }
            }
            var result = SemanticEntityBoundaryAudit.Capture(graph, Objects(), maxObjects);
            result.ElapsedMs = clock.ElapsedMilliseconds;
            return result;
        }
    }
}
#endif
