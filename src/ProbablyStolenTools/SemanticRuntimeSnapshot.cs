using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolenTools
{
    // Independent value payload for the remaining runtime partition. No restore API.
    internal sealed class SemanticRuntimeSnapshot : IDisposable
    {
        private readonly StateGraph _graph;
        private readonly RuntimeAccess _access;
        internal int ObjectCount => _graph.ObjectCount;
        internal int SlotCount => _graph.SlotCount;
        internal long ByteCount => _graph.ByteCount;
        internal int BlockCount => _graph.BlockCount;
        internal int BoundEntities => _access.BoundEntities;

        private SemanticRuntimeSnapshot(StateGraph graph, RuntimeAccess access)
        { _graph = graph; _access = access; }

        internal static SemanticRuntimeSnapshot Capture(StateGraph oracle, SemanticEntityBoundaryPlan boundary,
            int maxObjects, int maxSlots, long maxBytes)
        {
            if (boundary.RuntimeSlots > maxSlots)
                throw new InvalidOperationException("Runtime partition exceeds remaining combined slot budget.");
            var access = new RuntimeAccess(oracle.CreateIndependentAccess(), boundary, oracle.Contains);
            StateGraph graph = null;
            try
            {
                graph = StateGraph.Capture(access, boundary.RuntimeSeeds(), maxObjects, maxSlots, maxBytes);
                if (graph.ObjectCount != oracle.ObjectCount || oracle.Objects.Any(id => !graph.Contains(id)))
                    throw new InvalidOperationException("Independent runtime payload lost original objects.");
                // Exact owner/descriptor/reference coverage, not a count-only certificate.
                var slots = new HashSet<(IntPtr Owner, StateSlot Slot, IntPtr Reference)>();
                graph.VisitCapturedSlots((owner, slot, reference) =>
                {
                    if (!slots.Add((owner, slot, reference)))
                        throw new InvalidOperationException("Duplicate independent runtime slot.");
                });
                long bytes = 0;
                oracle.VisitCapturedSlots((owner, slot, reference) =>
                {
                    if (!boundary.IsRuntimeSlot(owner, slot.Name)) return;
                    if (!slots.Remove((owner, slot, reference)))
                        throw new InvalidOperationException("Independent runtime topology differs: " + slot.Name);
                    bytes += slot.IsReference ? IntPtr.Size : 8;
                });
                if (slots.Count != 0 || graph.SlotCount != boundary.RuntimeSlots || graph.ByteCount != bytes)
                    throw new InvalidOperationException("Independent runtime payload coverage/byte count differs.");
                if (graph.Differences(1).Count != 0)
                    throw new InvalidOperationException("Independent runtime payload changed during capture.");
                if (oracle.Differences(1).Count != 0)
                    throw new InvalidOperationException("Original checkpoint changed during independent runtime capture.");
                access.CompleteCapture();
                return new SemanticRuntimeSnapshot(graph, access);
            }
            catch
            {
                if (graph != null) graph.Dispose();
                else access.Dispose();
                throw;
            }
        }

        internal IReadOnlyList<string> Differences(int max = 8)
        {
            if (max < 1) throw new ArgumentOutOfRangeException(nameof(max));
            return _graph.Differences(max);
        }
        public void Dispose() => _graph.Dispose();

        private sealed class RuntimeAccess : IStateAccess, IBudgetedStateDescription,
            IStateValueComparer, IStateReferenceComparer, IStateBlockAccess
        {
            private readonly IStateAccess _inner;
            private readonly SemanticEntityBoundaryPlan _boundary;
            private Func<IntPtr, bool> _covered;
            private readonly Dictionary<IntPtr, SemanticEntityBoundaryPlan.Handle> _handles = new();
            internal int BoundEntities => _handles.Count;
            internal RuntimeAccess(IStateAccess inner, SemanticEntityBoundaryPlan boundary, Func<IntPtr, bool> covered)
            { _inner = inner; _boundary = boundary; _covered = covered; }
            internal void CompleteCapture() => _covered = null;
            public void Retain(IntPtr id)
            {
                if (!_covered(id)) throw new InvalidOperationException("Runtime discovery left the original checkpoint.");
                _inner.Retain(id);
                if (_boundary.TryBind(id, out var handle))
                {
                    _boundary.ValidateHandle(handle, ManagedObjects.Get(id));
                    _handles.Add(id, handle);
                }
            }
            public IReadOnlyList<StateSlot> Describe(IntPtr id) => Describe(id, int.MaxValue, long.MaxValue);
            public IReadOnlyList<StateSlot> Describe(IntPtr id, int remainingSlots, long remainingBytes)
            {
                bool entity = _handles.ContainsKey(id);
                // Registered entity descriptors are bounded scalar/reference fields, never arrays.
                // Budgeted array expansion for all runtime owners still happens before allocation.
                var full = _inner is IBudgetedStateDescription budgeted
                    ? budgeted.Describe(id, entity ? int.MaxValue : remainingSlots, entity ? long.MaxValue : remainingBytes)
                    : _inner.Describe(id);
                if (!entity) return full;
                return full.Where(slot => _boundary.IsRuntimeSlot(id, slot.Name)).ToArray();
            }
            public byte[] ReadValue(IntPtr id, StateSlot slot) => _inner.ReadValue(id, slot);
            public IntPtr ReadReference(IntPtr id, StateSlot slot) => _inner.ReadReference(id, slot);
            public bool ValueEquals(IntPtr id, StateSlot slot, byte[] captured)
                => _inner is IStateValueComparer comparer ? comparer.ValueEquals(id, slot, captured)
                    : _inner.ReadValue(id, slot).SequenceEqual(captured);
            public bool ReferenceMatches(IntPtr id, StateSlot slot, IntPtr captured)
                => _inner is IStateReferenceComparer comparer ? comparer.ReferenceMatches(id, slot, captured)
                    : _inner.ReadReference(id, slot) == captured;
            public long ReferenceComparisonCount => (_inner as IStateReferenceComparer)?.ReferenceComparisonCount ?? 0;
            public IStateBlock CaptureBlock(IntPtr id, IReadOnlyList<StateSlot> slots)
            {
                // A full-owner payload must never bypass entity field exclusion.
                if (_handles.ContainsKey(id) || _inner is not IStateBlockAccess blocks) return null;
                var block = blocks.CaptureBlock(id, slots);
                return block == null ? null : new ReadOnlyBlock(block);
            }
            public void Validate()
            {
                _inner.Validate();
                foreach (var pair in _handles)
                    _boundary.ValidateHandle(pair.Value, ManagedObjects.Get(pair.Key));
            }
            public void WriteValue(IntPtr id, StateSlot slot, byte[] value)
                => throw new InvalidOperationException("Diagnostic runtime payload is read-only.");
            public void WriteReference(IntPtr id, StateSlot slot, IntPtr value)
                => throw new InvalidOperationException("Diagnostic runtime payload is read-only.");
            public void Dispose()
            {
                _handles.Clear();
                _covered = null;
                _inner.Dispose();
            }
        }
        private sealed class ReadOnlyBlock : IStateBlock
        {
            private readonly IStateBlock _inner;
            internal ReadOnlyBlock(IStateBlock inner) => _inner = inner;
            public IntPtr ReferenceAt(StateSlot slot) => _inner.ReferenceAt(slot);
            public int CollectDifferences(List<string> result, int max) => _inner.CollectDifferences(result, max);
            public void RestoreValues() => throw new InvalidOperationException("Diagnostic runtime block is read-only.");
            public void RestoreReferences() => throw new InvalidOperationException("Diagnostic runtime block is read-only.");
        }
    }
}
