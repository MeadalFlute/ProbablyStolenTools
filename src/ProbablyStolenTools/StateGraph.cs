using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

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

    // Opt-in eight-byte scalar tokens only: read every scalar without retaining values or replacing reference discovery.
    internal interface IStateCensusValueProbe
    {
        bool TryProbeValues(IntPtr id, IReadOnlyList<StateSlot> slots);
    }

    internal interface IStateObjectDescription
    {
        string ObjectTypeName(IntPtr id);
    }

    // Opt-in only for real storage writes without setters/callbacks or cross-owner side effects.
    internal interface IStateOwnerRepairAccess { }
    internal interface IStateOwnerRepairBlock { }

    internal interface IBudgetedStateDescription
    {
        IReadOnlyList<StateSlot> Describe(IntPtr id, int remainingSlots, long remainingBytes);
    }

    // Optional lossless managed-field payload: scalars retain the eight-byte logical token budget.
    // Topology, discovery, ownership and all budget enforcement still belong to StateGraph.
    internal interface IStateBlockAccess
    {
        IStateBlock CaptureBlock(IntPtr id, IReadOnlyList<StateSlot> slots);
    }

    internal interface IStateBlock
    {
        IntPtr ReferenceAt(StateSlot slot);
        void RestoreValues();
        void RestoreReferences();
        int CollectDifferences(List<string> result, int max);
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
        private readonly List<(IntPtr Owner, IReadOnlyList<StateSlot> Slots, IStateBlock Payload)> _blocks = new();
        private readonly HashSet<IntPtr> _objects = new();
        private readonly Dictionary<IntPtr, (IntPtr Parent, string Field)> _sources = new();
        private bool _disposed;
        private long _revision;
        internal int ObjectCount => _objects.Count;
        internal int SlotCount { get; private set; }
        internal long ByteCount { get; private set; }
        internal int BlockCount => _blocks.Count;
        internal IEnumerable<IntPtr> Objects => _objects;
        internal bool Contains(IntPtr id) => _objects.Contains(id);

        internal sealed class Reconciliation
        {
            internal readonly StateGraph Graph;
            internal readonly long Revision;
            internal readonly HashSet<IntPtr> Owners = new();
            internal readonly List<int> Entries = new(), Blocks = new();
            internal readonly int MaxOwners;
            internal bool Used;
            internal long AppliedRevision = -1;
            internal string Reason { get; set; }
            internal int ChangedSlots { get; private set; }
            internal int PlannedSlots { get; set; }
            internal IReadOnlyList<string> Differences { get; set; }
            internal string FallbackSummary { get; set; } = "";
            internal string Mode => ChangedSlots == 0 ? "none" : Reason.Length == 0 ? "owners" : "full";
            internal int TrackedOwners => Owners.Count;

            internal Reconciliation(StateGraph graph, long revision, string reason, int maxOwners)
            { Graph = graph; Revision = revision; Reason = reason; MaxOwners = maxOwners; }

            internal void Changed(IntPtr owner, int slots)
            {
                ChangedSlots += slots;
                if (Reason.Length != 0 || Owners.Contains(owner)) return;
                if (Owners.Count >= MaxOwners) { Reason = "owner-limit"; return; }
                Owners.Add(owner);
            }

            // Single-use, synchronous only; any intervening graph scan/write invalidates the plan.
            internal void Apply() => Graph.ApplyReconciliation(this);
        }

        // Inspect captured topology, never live fields; diagnostic consumers cannot write or prune entries.
        internal void VisitCapturedSlots(Action<IntPtr, StateSlot, IntPtr> visit)
        {
            RequireCheckpoint();
            foreach (var entry in _entries) visit(entry.Owner, entry.Slot, entry.Reference);
            foreach (var block in _blocks)
                foreach (var slot in block.Slots)
                    visit(block.Owner, slot, slot.IsReference ? block.Payload.ReferenceAt(slot) : IntPtr.Zero);
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
                var censusValues = captureValues ? null : access as IStateCensusValueProbe;
                var blockAccess = captureValues ? access as IStateBlockAccess : null;
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
                    var block = blockAccess?.CaptureBlock(id, description);
                    bool valuesProbed = censusValues?.TryProbeValues(id, description) == true;
                    foreach (var slot in description)
                    {
                        if (graph.SlotCount >= maxSlots)
                            throw new SnapshotLimitException("slot", graph.ObjectCount, graph.SlotCount, graph.ByteCount,
                                maxObjects, maxSlots, maxBytes, pending.Count, graph.SourceOf(id) + " -> " + slot.Name,
                                graph._sources.Count);
                        if (slot.IsReference)
                        {
                            var value = block == null ? access.ReadReference(id, slot) : block.ReferenceAt(slot);
                            if (captureValues && block == null) graph._entries.Add(new Entry(id, slot, null, value));
                            graph.SlotCount++;
                            graph.ByteCount += IntPtr.Size;
                            Discover(value, id, slot.Name);
                        }
                        else
                        {
                            if (block != null || valuesProbed)
                                graph.ByteCount += sizeof(long);
                            else if (probe != null)
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
                    if (block != null) graph._blocks.Add((id, description, block));
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
            _revision++;
            _access.Validate();
            // All original objects/arrays are retained. Restore their contents first and links second.
            foreach (var e in _entries)
                if (!e.Slot.IsReference) _access.WriteValue(e.Owner, e.Slot, e.Bytes);
            foreach (var block in _blocks) block.Payload.RestoreValues();
            foreach (var e in _entries)
                if (e.Slot.IsReference) _access.WriteReference(e.Owner, e.Slot, e.Reference);
            foreach (var block in _blocks) block.Payload.RestoreReferences();
        }

        internal IReadOnlyList<string> Differences(int max = 12)
            => ScanDifferences(max, false);

        internal IReadOnlyList<string> DifferencesWithSources(int max = 12)
            => ScanDifferences(max, true);

        internal Reconciliation PrepareReconciliation(bool allowOwners = true, int maxOwners = 128, int maxSlots = 8192)
        {
            RequireCheckpoint();
            if (maxOwners < 0 || maxSlots < 0) throw new ArgumentOutOfRangeException();
            string reason = !allowOwners ? "disabled" : _access is not IStateOwnerRepairAccess ? "access"
                : _blocks.Any(b => b.Payload is not IStateOwnerRepairBlock) ? "block" : "";
            var plan = new Reconciliation(this, _revision + 1, reason, maxOwners);
            plan.Differences = ScanDifferences(12, true, plan);
            if (plan.Mode == "owners")
            {
                for (int i = 0; i < _entries.Count; i++)
                    if (plan.Owners.Contains(_entries[i].Owner))
                    {
                        plan.Entries.Add(i);
                        if (++plan.PlannedSlots > maxSlots) { plan.Reason = "slot-limit"; break; }
                    }
                if (plan.Reason.Length == 0)
                    for (int i = 0; i < _blocks.Count; i++)
                        if (plan.Owners.Contains(_blocks[i].Owner))
                        {
                            plan.Blocks.Add(i);
                            if (_blocks[i].Slots.Count > maxSlots - plan.PlannedSlots)
                            { plan.Reason = "slot-limit"; break; }
                            plan.PlannedSlots += _blocks[i].Slots.Count;
                        }
            }
            if (plan.Mode == "full")
            {
                plan.Entries.Clear(); plan.Blocks.Clear();
                plan.PlannedSlots = SlotCount;
            }
            return plan;
        }

        private void ApplyReconciliation(Reconciliation plan)
        {
            RequireCheckpoint();
            if (plan.Graph != this || plan.Used || plan.Revision != _revision)
                throw new InvalidOperationException("Reconciliation plan is stale or already consumed.");
            plan.Used = true;
            _revision++;
            if (plan.Mode == "full")
            {
                Restore();
                plan.AppliedRevision = _revision;
                return;
            }
            _access.Validate();
            foreach (int i in plan.Entries)
            {
                var e = _entries[i];
                if (!e.Slot.IsReference) _access.WriteValue(e.Owner, e.Slot, e.Bytes);
            }
            foreach (int i in plan.Blocks) _blocks[i].Payload.RestoreValues();
            foreach (int i in plan.Entries)
            {
                var e = _entries[i];
                if (e.Slot.IsReference) _access.WriteReference(e.Owner, e.Slot, e.Reference);
            }
            foreach (int i in plan.Blocks) _blocks[i].Payload.RestoreReferences();
            plan.AppliedRevision = _revision;
        }

        internal IReadOnlyList<string> VerifyReconciliation(Reconciliation plan, Action afterFullRestore,
            out bool fullFallback)
        {
            RequireCheckpoint();
            fullFallback = false;
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            if (plan.Graph != this || !plan.Used || plan.AppliedRevision != _revision)
                throw new InvalidOperationException("Reconciliation verification is stale or not applied.");
            if (afterFullRestore == null) throw new ArgumentNullException(nameof(afterFullRestore));
            var differences = Differences();
            if (differences.Count != 0 && plan.Mode == "owners")
            {
                // Only a completed mismatch can retry. Read/write/visual exceptions still fail closed.
                fullFallback = true;
                plan.FallbackSummary = string.Join(" | ", differences.Select(s => DiagnosticText(s, 128, true)));
                Restore();
                afterFullRestore();
                differences = Differences();
            }
            return differences;
        }

        private IReadOnlyList<string> ScanDifferences(int max, bool sources, Reconciliation plan = null)
        {
            RequireCheckpoint();
            _revision++;
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
                if (!same)
                {
                    plan?.Changed(e.Owner, 1);
                    if (result.Count < max)
                        result.Add(sources ? DescribeDifference(e.Owner, e.Slot.Name, false) : e.Slot.Name);
                }
            }
            foreach (var block in _blocks)
            {
                int start = result.Count;
                int changed = block.Payload.CollectDifferences(result, max);
                if (changed < 0 || changed > block.Slots.Count)
                    throw new InvalidOperationException("Invalid block difference count.");
                if (changed != 0) plan?.Changed(block.Owner, changed);
                if (sources)
                    for (int i = start; i < result.Count; i++)
                        result[i] = DescribeDifference(block.Owner, result[i], true);
            }
            return result;
        }

        private string DescribeDifference(IntPtr owner, string field, bool block)
        {
            // Metadata only: never call business getters/ToString or reread the changed field.
            string type = (_access as IStateObjectDescription)?.ObjectTypeName(owner);
            return $"owner=0x{owner.ToInt64():X} type={DiagnosticText(type, 128)} " +
                $"storage={(block ? "block" : "field")} field={DiagnosticText(field, 128, true)} " +
                $"source={DiagnosticText(SourceOf(owner), 240, true)}";
        }

        private static string DiagnosticText(string value, int max, bool keepTail = false)
        {
            if (string.IsNullOrEmpty(value)) return "unknown";
            value = CompactDiagnosticName(value);
            if (value.Length > max)
                value = keepTail ? "..." + value.Substring(value.Length - max + 3)
                    : value.Substring(0, max - 3) + "...";
            return value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        }

        // FullName embeds assembly-qualified generic arguments in [[...]]. Omit only that
        // balanced metadata group; keep array indices and all following field/path segments.
        private static string CompactDiagnosticName(string value)
        {
            int start = value.IndexOf("[[", StringComparison.Ordinal);
            if (start < 0) return value;
            var result = new StringBuilder();
            int copied = 0;
            while (start >= 0)
            {
                int depth = 0, end = start;
                for (; end < value.Length; end++)
                {
                    if (value[end] == '[') depth++;
                    else if (value[end] == ']' && --depth == 0) { end++; break; }
                }
                if (depth != 0) break;
                result.Append(value, copied, start - copied).Append("<...>");
                copied = end;
                start = value.IndexOf("[[", copied, StringComparison.Ordinal);
            }
            return result.Append(value, copied, value.Length - copied).ToString();
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
            _blocks.Clear();
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
