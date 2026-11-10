using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolenTools
{
    internal sealed class SnapshotLimitException : InvalidOperationException
    {
        internal SnapshotLimitException(string kind, int objects, int slots, long bytes,
            int maxObjects, int maxSlots, long maxBytes, int pending, string source)
            : base($"Snapshot {kind} limit exceeded: objects={objects}/{maxObjects}, slots={slots}/{maxSlots}, " +
                $"bytes={bytes}/{maxBytes}, pending={pending}, source={source}. " +
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

    internal sealed class StateGraph : IDisposable
    {
        private readonly record struct Entry(IntPtr Owner, StateSlot Slot, byte[] Bytes, IntPtr Reference);
        private readonly IStateAccess _access;
        private readonly List<Entry> _entries = new();
        private readonly HashSet<IntPtr> _objects = new();
        private readonly Dictionary<IntPtr, (IntPtr Parent, string Field)> _sources = new();
        private bool _disposed;
        internal int ObjectCount => _objects.Count;
        internal int SlotCount => _entries.Count;
        internal long ByteCount { get; private set; }
        internal IEnumerable<IntPtr> Objects => _objects;
        internal bool Contains(IntPtr id) => _objects.Contains(id);

        private StateGraph(IStateAccess access) { _access = access; }

        internal static StateGraph Capture(IStateAccess access, IEnumerable<IntPtr> roots,
            int maxObjects = 200000, int maxSlots = 3000000, long maxBytes = 256L * 1024 * 1024)
        {
            var graph = new StateGraph(access);
            try
            {
                var pending = new Queue<IntPtr>();
                // Register all roots first so an earlier root's links cannot replace their root paths.
                foreach (var root in roots)
                    if (root != IntPtr.Zero && graph._sources.TryAdd(root, (IntPtr.Zero, "root")))
                        pending.Enqueue(root);
                while (pending.Count != 0)
                {
                    var id = pending.Dequeue();
                    graph._objects.Add(id);
                    if (graph._objects.Count > maxObjects)
                        throw new SnapshotLimitException("object", graph.ObjectCount, graph.SlotCount, graph.ByteCount,
                            maxObjects, maxSlots, maxBytes, pending.Count, graph.SourceOf(id));
                    access.Retain(id);
                    foreach (var slot in access.Describe(id))
                    {
                        if (graph._entries.Count >= maxSlots)
                            throw new SnapshotLimitException("slot", graph.ObjectCount, graph.SlotCount, graph.ByteCount,
                                maxObjects, maxSlots, maxBytes, pending.Count, graph.SourceOf(id) + " -> " + slot.Name);
                        if (slot.IsReference)
                        {
                            var value = access.ReadReference(id, slot);
                            graph._entries.Add(new Entry(id, slot, null, value));
                            if (value != IntPtr.Zero && graph._sources.TryAdd(value, (id, slot.Name)))
                                pending.Enqueue(value);
                            graph.ByteCount += IntPtr.Size;
                        }
                        else
                        {
                            var value = access.ReadValue(id, slot);
                            graph._entries.Add(new Entry(id, slot, value, IntPtr.Zero));
                            graph.ByteCount += value.Length;
                        }
                        if (graph.ByteCount > maxBytes)
                            throw new SnapshotLimitException("byte", graph.ObjectCount, graph.SlotCount, graph.ByteCount,
                                maxObjects, maxSlots, maxBytes, pending.Count, graph.SourceOf(id) + " -> " + slot.Name);
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
            if (_disposed) throw new ObjectDisposedException(nameof(StateGraph));
            _access.Validate();
            // All original objects/arrays are retained. Restore their contents first and links second.
            foreach (var e in _entries)
                if (!e.Slot.IsReference) _access.WriteValue(e.Owner, e.Slot, e.Bytes);
            foreach (var e in _entries)
                if (e.Slot.IsReference) _access.WriteReference(e.Owner, e.Slot, e.Reference);
        }

        internal IReadOnlyList<string> Differences(int max = 12)
        {
            var result = new List<string>();
            _access.Validate();
            foreach (var e in _entries)
            {
                bool same;
                if (e.Slot.IsReference) same = _access.ReadReference(e.Owner, e.Slot) == e.Reference;
                else
                {
                    var value = _access.ReadValue(e.Owner, e.Slot);
                    same = ReferenceEquals(value, e.Bytes) || value.SequenceEqual(e.Bytes);
                }
                if (!same && result.Count < max) result.Add(e.Slot.Name);
            }
            return result;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _entries.Clear();
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
