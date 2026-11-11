using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolenTools
{
    // Node-local identity protocol and captured-topology partition. Not a writable/pruned graph.
    internal sealed class SemanticEntityBoundaryPlan : IDisposable
    {
        internal readonly struct Handle
        {
            internal readonly SemanticEntityBoundaryPlan Scope;
            internal readonly IntPtr Id;
            internal Handle(SemanticEntityBoundaryPlan scope, IntPtr id) { Scope = scope; Id = id; }
        }
        private sealed class Entity
        {
            internal object Target;
            internal Type Type;
            internal SemanticSlotSchema Schema;
        }
        private readonly Dictionary<IntPtr, Entity> _entities = new();
        private readonly HashSet<IntPtr> _runtimeSeeds = new();
        private bool _disposed;
        internal int EntityCount => _entities.Count;
        internal int RootEntities { get; private set; }
        internal int EntitySlots { get; private set; }
        internal int RuntimeSlots { get; private set; }
        internal int EntityRuntimeSlots { get; private set; }
        internal int RuntimeHandleLinks { get; private set; }
        internal int EntityHandleLinks { get; private set; }
        internal int RuntimeSeedCount => _runtimeSeeds.Count;
        internal int RuntimeReachableObjects { get; private set; }
        internal int TotalSlots => checked(EntitySlots + RuntimeSlots);

        internal static SemanticEntityBoundaryPlan Capture(StateGraph graph,
            IEnumerable<(IntPtr Id, SemanticSlotSchema Schema)> entities, Func<IntPtr, object> target,
            Func<object, IntPtr> identity, int maxObjects, int maxSlots)
        {
            var result = new SemanticEntityBoundaryPlan();
            try
            {
                var coverage = new Dictionary<IntPtr, SemanticSlotSchema.Coverage>();
                var runtimeEdges = new Dictionary<IntPtr, List<IntPtr>>();
                long indexedSlots = 0;
                foreach (var entry in entities)
                {
                    if (entry.Id == IntPtr.Zero || entry.Schema == null || !graph.Contains(entry.Id)
                        || result._entities.ContainsKey(entry.Id))
                        throw new InvalidOperationException("Invalid/duplicate external entity identity or schema.");
                    if (result._entities.Count >= maxObjects)
                        throw new InvalidOperationException("Entity handle object budget exceeded.");
                    indexedSlots += entry.Schema.FieldCount;
                    if (indexedSlots > maxSlots)
                        throw new InvalidOperationException("Entity handle schema budget exceeded.");
                    object value = target(entry.Id);
                    if (value == null || identity(value) != entry.Id)
                        throw new InvalidOperationException("External entity target identity mismatch.");
                    bool root = graph.IsCapturedRoot(entry.Id);
                    result._entities.Add(entry.Id, new Entity
                    {
                        Target = value, Type = value.GetType(), Schema = entry.Schema
                    });
                    coverage.Add(entry.Id, new SemanticSlotSchema.Coverage(entry.Schema));
                    if (root) result.RootEntities++;
                }
                // Roots plus all outbound semantic references are needed after entity expansion is cut.
                foreach (var id in graph.Objects)
                    if (graph.IsCapturedRoot(id)) result.AddSeed(id, maxObjects);
                graph.VisitCapturedSlots((owner, slot, reference) =>
                {
                    if (result.TotalSlots >= maxSlots)
                        throw new InvalidOperationException("Entity boundary slot budget exceeded.");
                    var role = coverage.TryGetValue(owner, out var progress)
                        ? progress.Observe(slot.Name, slot.IsReference) : SemanticFieldRole.Runtime;
                    if (role == SemanticFieldRole.Entity) result.EntitySlots++;
                    else
                    {
                        result.RuntimeSlots++;
                        if (progress != null) result.EntityRuntimeSlots++;
                    }
                    if (!slot.IsReference || reference == IntPtr.Zero) return;
                    if (!graph.Contains(reference))
                        throw new InvalidOperationException("External entity link is outside captured topology.");
                    if (role == SemanticFieldRole.Entity) result.AddSeed(reference, maxObjects);
                    else
                    {
                        if (!runtimeEdges.TryGetValue(owner, out var edges))
                            runtimeEdges.Add(owner, edges = new List<IntPtr>());
                        edges.Add(reference);
                    }
                    if (!result._entities.TryGetValue(reference, out var linked)) return;
                    if (!ReferenceEquals(target(reference), linked.Target) || identity(linked.Target) != reference)
                        throw new InvalidOperationException("External entity alias identity mismatch.");
                    if (role == SemanticFieldRole.Entity) result.EntityHandleLinks++;
                    else result.RuntimeHandleLinks++;
                });
                if (coverage.Values.Any(progress => !progress.Complete) || result.TotalSlots != graph.SlotCount)
                    throw new InvalidOperationException("External entity boundary is not fully covered.");
                // Replay discovery using runtime edges only. Semantic edges have become explicit seeds.
                var reachable = new HashSet<IntPtr>();
                var pending = new Queue<IntPtr>();
                void Discover(IntPtr id)
                {
                    if (reachable.Contains(id)) return;
                    if (reachable.Count >= maxObjects)
                        throw new InvalidOperationException("Runtime boundary discovery object budget exceeded.");
                    reachable.Add(id);
                    pending.Enqueue(id);
                }
                foreach (var id in result._runtimeSeeds) Discover(id);
                while (pending.Count != 0)
                {
                    var owner = pending.Dequeue();
                    if (runtimeEdges.TryGetValue(owner, out var edges))
                        foreach (var reference in edges) Discover(reference);
                }
                result.RuntimeReachableObjects = reachable.Count;
                if (reachable.Count != graph.ObjectCount || graph.Objects.Any(id => !reachable.Contains(id)))
                    throw new InvalidOperationException("Runtime boundary roots lost captured objects.");
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        private void AddSeed(IntPtr id, int maxObjects)
        {
            if (_runtimeSeeds.Contains(id)) return;
            if (_runtimeSeeds.Count >= maxObjects)
                throw new InvalidOperationException("Runtime seed object budget exceeded.");
            _runtimeSeeds.Add(id);
        }

        private void RequireLive()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SemanticEntityBoundaryPlan));
        }
        internal Handle GetHandle(IntPtr id)
        {
            RequireLive();
            if (!_entities.ContainsKey(id)) throw new InvalidOperationException("Unknown external entity.");
            return new Handle(this, id);
        }
        internal object Resolve(Handle handle, Type exactType)
        {
            RequireLive();
            if (!ReferenceEquals(handle.Scope, this) || !_entities.TryGetValue(handle.Id, out var entity)
                || entity.Type != exactType)
                throw new InvalidOperationException("Wrong checkpoint, type or external entity handle.");
            return entity.Target;
        }
        internal SemanticFieldRole RoleOf(Handle handle, string slot)
        {
            RequireLive();
            if (!ReferenceEquals(handle.Scope, this) || !_entities.TryGetValue(handle.Id, out var entity))
                throw new InvalidOperationException("Wrong checkpoint or external entity handle.");
            return entity.Schema.RoleOf(slot);
        }
        internal IntPtr[] RuntimeSeeds()
        {
            RequireLive();
            return _runtimeSeeds.ToArray();
        }
        internal void Validate(Func<IntPtr, object> target)
        {
            RequireLive();
            foreach (var pair in _entities)
                if (!ReferenceEquals(target(pair.Key), pair.Value.Target) || pair.Value.Target.GetType() != pair.Value.Type)
                    throw new InvalidOperationException("Retained external entity identity changed.");
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _entities.Clear();
            _runtimeSeeds.Clear();
        }
    }
}
