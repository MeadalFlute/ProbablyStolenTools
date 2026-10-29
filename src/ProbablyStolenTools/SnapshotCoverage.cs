using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    // Bounded, per-capture evidence. A clean field diff never implies coverage of mod statics.
    internal sealed class SnapshotCoverage
    {
        private readonly HashSet<Type> _types = new();
        private readonly List<string> _details = new();
        internal int ModObjects { get; private set; }
        internal int ModTypes => _types.Count;
        internal int Skipped { get; private set; }
        internal IReadOnlyList<string> Details => _details;
        internal string Summary => $"scope=reachable-instance-fields modTypes={ModTypes} " +
            $"modObjects={ModObjects} skipped={Skipped} externalStatics=untracked";

        internal void Visit(Type type)
        {
            _types.Add(type);
            ModObjects++;
        }

        internal void Skip(string path, string reason)
        {
            Skipped++;
            if (_details.Count < 32) _details.Add(path + ": " + reason);
        }
    }
}
