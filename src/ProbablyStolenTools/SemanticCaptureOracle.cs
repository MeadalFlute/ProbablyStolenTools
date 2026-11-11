using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolenTools
{
    // Development-only independent old payload. It never writes or silently repairs the candidate.
    internal sealed class SemanticCaptureOracle : IDisposable
    {
        private StateGraph _graph;
        internal SemanticCaptureOracle(StateGraph candidate, StateGraph oracle)
        {
            try
            {
                if (ReferenceEquals(candidate, oracle) || candidate.ObjectCount != oracle.ObjectCount
                    || candidate.SlotCount != oracle.SlotCount || candidate.ByteCount != oracle.ByteCount
                    || !candidate.Objects.ToHashSet().SetEquals(oracle.Objects))
                    throw new InvalidOperationException("Single-pass candidate object/budget coverage differs from legacy oracle.");
                var slots = new HashSet<(IntPtr Owner, StateSlot Slot, IntPtr Reference)>();
                oracle.VisitCapturedSlots((owner, slot, reference) =>
                {
                    if (!slots.Add((owner, slot, reference)))
                        throw new InvalidOperationException("Duplicate legacy oracle slot.");
                });
                candidate.VisitCapturedSlots((owner, slot, reference) =>
                {
                    if (!slots.Remove((owner, slot, reference)))
                        throw new InvalidOperationException("Single-pass candidate slot/reference differs from legacy oracle.");
                });
                if (slots.Count != 0 || candidate.Differences(1).Count != 0 || oracle.Differences(1).Count != 0)
                    throw new InvalidOperationException("Single-pass candidate capture differs from legacy oracle.");
                _graph = oracle;
            }
            catch { if (!ReferenceEquals(candidate, oracle)) oracle.Dispose(); throw; }
        }

        internal void VerifyRestore()
        {
            if (_graph == null) throw new ObjectDisposedException(nameof(SemanticCaptureOracle));
            var differences = _graph.DifferencesWithSources(12);
            if (differences.Count != 0)
                throw new InvalidOperationException("Single-pass restore differs from independent legacy oracle: "
                    + string.Join(" | ", differences));
        }
        internal void ValidateRetention()
        {
            if (_graph == null) throw new ObjectDisposedException(nameof(SemanticCaptureOracle));
            // Differences performs the old backend's complete retention validation and scan.
            _graph.Differences(1);
        }
        public void Dispose()
        {
            var graph = _graph;
            _graph = null;
            graph?.Dispose();
        }
    }
}
