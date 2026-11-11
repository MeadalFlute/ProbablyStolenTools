using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolenTools
{
    internal sealed class SnapshotLimitException : InvalidOperationException
    {
        internal SnapshotLimitException(string kind, int objects, int slots, long bytes,
            int maxObjects, int maxSlots, long maxBytes, int pending, string source, int? discovered = null)
            : base($"Snapshot {kind} limit exceeded: objects={objects}/{maxObjects}, slots={slots}/{maxSlots}, " +
                $"bytes={bytes}/{maxBytes}, pending={pending}, discovered={discovered ?? objects}, source={source}. " +
                "This checkpoint was skipped; existing checkpoints were retained. No partial snapshot is usable.") { }
    }

    // The backend exposes real storage slots, never computed/gameplay properties.
    internal readonly record struct StateSlot(int Offset, int Size, bool IsReference, string Name);

    internal interface IStateAccess : IDisposable
    {
        void Retain(IntPtr id);
        IReadOnlyList<StateSlot> Describe(IntPtr id);
        byte[] ReadValue(IntPtr id, StateSlot slot);
        IntPtr ReadReference(IntPtr id, StateSlot slot);
        void WriteValue(IntPtr id, StateSlot slot, byte[] value);
        void WriteReference(IntPtr id, StateSlot slot, IntPtr value);
        void Validate();
    }

    internal interface IStateValueComparer
    {
        bool ValueEquals(IntPtr id, StateSlot slot, byte[] captured);
    }

    internal interface IStateValueProbe
    {
        int ProbeValue(IntPtr id, StateSlot slot);
    }

    internal interface IBudgetedStateDescription
    {
        IReadOnlyList<StateSlot> Describe(IntPtr id, int remainingSlots, long remainingBytes);
    }

    internal sealed class SnapshotDescriptionLimitException : InvalidOperationException
    {
        internal readonly string Kind;
        internal readonly long RequiredSlots;
        internal readonly long RequiredBytes;
        internal SnapshotDescriptionLimitException(string kind, long slots, long bytes)
            : base($"Snapshot array {kind} limit exceeded before expansion: required slots={slots}, bytes={bytes}.")
        { Kind = kind; RequiredSlots = slots; RequiredBytes = bytes; }
    }

    internal sealed class StateGraph : IDisposable
    {
        private readonly record struct Entry(IntPtr Owner, StateSlot Slot, byte[] Bytes, IntPtr Reference);
        private readonly IStateAccess _access;
        private readonly bool _captureValues;
        private readonly List<Entry> _entries = new();
        private readonly HashSet<IntPtr> _objects = new();
        private readonly Dictionary<IntPtr, (IntPtr Parent, string Field)> _sources = new();
        private bool _disposed;
        internal int ObjectCount => _objects.Count;
        internal int SlotCount { get; private set; }
        internal long ByteCount { get; private set; }
        internal IEnumerable<IntPtr> Objects => _objects;
        internal bool Contains(IntPtr id) => _objects.Contains(id);

        // Inspect captured topology, never live fields; diagnostic consumers cannot write or prune entries.
        internal void VisitCapturedSlots(Action<IntPtr, StateSlot, IntPtr> visit)
        {
            RequireCheckpoint();
            foreach (var entry in _entries) visit(entry.Owner, entry.Slot, entry.Reference);
        }

        internal bool IsCapturedRoot(IntPtr id)
        {
            RequireCheckpoint();
            return _sources.TryGetValue(id, out var source) && source.Parent == IntPtr.Zero;
        }

        private StateGraph(IStateAccess access, bool captureValues)
        { _access = access; _captureValues = captureValues; }

        internal static StateGraph Capture(IStateAccess access, IEnumerable<IntPtr> roots,
            int maxObjects = 200000, int maxSlots = 3000000, long maxBytes = 256L * 1024 * 1024)
            => Walk(access, roots, maxObjects, maxSlots, maxBytes, true);

        // A census still reads every field, but is never a restorable checkpoint.
        internal static StateGraph Census(IStateAccess access, IEnumerable<IntPtr> roots,
            int maxObjects = 200000, int maxSlots = 3000000, long maxBytes = 256L * 1024 * 1024)
            => Walk(access, roots, maxObjects, maxSlots, maxBytes, false);

        private static StateGraph Walk(IStateAccess access, IEnumerable<IntPtr> roots,
            int maxObjects, int maxSlots, long maxBytes, bool captureValues)
        {
            var graph = new StateGraph(access, captureValues);
            try
            {
                var pending = new Queue<IntPtr>();
                var budgeted = access as IBudgetedStateDescription;
                var probe = captureValues ? null : access as IStateValueProbe;
                void Discover(IntPtr id, IntPtr parent, string field)
                {
                    if (id == IntPtr.Zero || graph._sources.ContainsKey(id)) return;
                    if (graph._sources.Count >= maxObjects)
                        throw new SnapshotLimitException("object", graph.ObjectCount, graph.SlotCount, graph.ByteCount,
                            maxObjects, maxSlots, maxBytes, pending.Count,
                            parent == IntPtr.Zero ? field : graph.SourceOf(parent) + " -> " + field,
                            graph._sources.Count + 1);
                    graph._sources.Add(id, (parent, field));
                    pending.Enqueue(id);
                }
                // Register all roots first so an earlier root's links cannot replace their root paths.
                foreach (var root in roots)
                    Discover(root, IntPtr.Zero, "root");
                while (pending.Count != 0)
                {
                    var id = pending.Dequeue();
                    graph._objects.Add(id);
                    access.Retain(id);
                    IReadOnlyList<StateSlot> description;
                    try
                    {
                        description = budgeted == null ? access.Describe(id)
                            : budgeted.Describe(id, maxSlots - graph.SlotCount, maxBytes - graph.ByteCount);
                    }
                    catch (SnapshotDescriptionLimitException e)
                    {
                        throw new SnapshotLimitException(e.Kind, graph.ObjectCount, graph.SlotCount, graph.ByteCount,
                            maxObjects, maxSlots, maxBytes, pending.Count, graph.SourceOf(id) +
                            $" -> array requires slots={e.RequiredSlots}, bytes={e.RequiredBytes} before expansion",
                            graph._sources.Count);
                    }
                    foreach (var slot in description)
                    {
                        if (graph.SlotCount >= maxSlots)
                            throw new SnapshotLimitException("slot", graph.ObjectCount, graph.SlotCount, graph.ByteCount,
                                maxObjects, maxSlots, maxBytes, pending.Count, graph.SourceOf(id) + " -> " + slot.Name,
                                graph._sources.Count);
                        if (slot.IsReference)
                        {
                            var value = access.ReadReference(id, slot);
                            if (captureValues) graph._entries.Add(new Entry(id, slot, null, value));
                            graph.SlotCount++;
                            graph.ByteCount += IntPtr.Size;
                            Discover(value, id, slot.Name);
                        }
                        else
                        {
                            if (probe != null)
                                graph.ByteCount += probe.ProbeValue(id, slot);
                            else
                            {
                                var value = access.ReadValue(id, slot);
                                if (captureValues) graph._entries.Add(new Entry(id, slot, value, IntPtr.Zero));
                                graph.ByteCount += value.Length;
                            }
                            graph.SlotCount++;
                        }
                        if (graph.ByteCount > maxBytes)
                            throw new SnapshotLimitException("byte", graph.ObjectCount, graph.SlotCount, graph.ByteCount,
                                maxObjects, maxSlots, maxBytes, pending.Count, graph.SourceOf(id) + " -> " + slot.Name,
                                graph._sources.Count);
                    }
                }
                access.Validate();
                return graph;
            }
            catch { graph.Dispose(); throw; }
        }

        internal string SourceOf(IntPtr id)
        {
            var path = new List<string>();
            while (_sources.TryGetValue(id, out var source))
            {
                path.Add(source.Field);
                if (source.Parent == IntPtr.Zero) break;
                if (path.Count >= 12) { path.Add("..."); break; }
                id = source.Parent;
            }
            path.Reverse();
            return string.Join(" -> ", path);
        }

        internal void Restore()
        {
            RequireCheckpoint();
            _access.Validate();
            // All original objects/arrays are retained. Restore their contents first and links second.
            foreach (var e in _entries)
                if (!e.Slot.IsReference) _access.WriteValue(e.Owner, e.Slot, e.Bytes);
            foreach (var e in _entries)
                if (e.Slot.IsReference) _access.WriteReference(e.Owner, e.Slot, e.Reference);
        }

        internal IReadOnlyList<string> Differences(int max = 12)
        {
            RequireCheckpoint();
            var result = new List<string>();
            var comparer = _access as IStateValueComparer;
            _access.Validate();
            foreach (var e in _entries)
            {
                bool same;
                if (e.Slot.IsReference) same = _access.ReadReference(e.Owner, e.Slot) == e.Reference;
                else if (comparer != null) same = comparer.ValueEquals(e.Owner, e.Slot, e.Bytes);
                else
                {
                    var value = _access.ReadValue(e.Owner, e.Slot);
                    same = ReferenceEquals(value, e.Bytes) || value.SequenceEqual(e.Bytes);
                }
                if (!same && result.Count < max) result.Add(e.Slot.Name);
            }
            return result;
        }

        private void RequireCheckpoint()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StateGraph));
            if (!_captureValues) throw new InvalidOperationException("An object census is not a checkpoint.");
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _entries.Clear();
            SlotCount = 0;
            _objects.Clear();
            _sources.Clear();
            _access.Dispose();
        }
    }

    internal sealed class CustomerBoundary
    {
        internal IntPtr Store { get; private set; }
        internal IntPtr Client { get; private set; }
        internal int Day { get; private set; }
        internal int Scene { get; private set; }
        internal bool Ready { get; private set; }
        internal bool Attempted { get; private set; }

        internal bool Observe(IntPtr store, IntPtr client, int day, int scene)
        {
            if (Store == store && Client == client && Day == day && Scene == scene) return false;
            Store = store; Client = client; Day = day; Scene = scene;
            Ready = false; Attempted = false;
            return true;
        }

        internal void MarkAttempt() { Attempted = true; }
        internal void MarkReady() { Attempted = true; Ready = true; }
        internal void Invalidate() { Ready = false; Attempted = true; }
        internal void Reset()
        {
            Store = IntPtr.Zero;
            Client = IntPtr.Zero;
            Day = -1;
            Scene = -1;
            Ready = false;
            Attempted = false;
        }
    }
}
