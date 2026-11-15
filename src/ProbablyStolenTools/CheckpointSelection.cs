using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    internal sealed class CheckpointTimelineEntry
    {
        internal object Node { get; set; }
        internal int Day { get; set; }
        internal int Cash { get; set; }
        internal bool DayStart { get; set; }
        internal DateTime At { get; set; }
        internal string Label { get; set; }
        internal string State { get; set; }

        internal static void Sort(List<CheckpointTimelineEntry> entries)
        {
            entries.Sort((a, b) =>
            {
                int day = b.Day.CompareTo(a.Day);
                if (day != 0) return day;
                int stage = a.DayStart.CompareTo(b.DayStart);
                return stage != 0 ? stage : b.At.CompareTo(a.At);
            });
        }
    }

    // Uses the panel's newest-first order. The active checkpoint is an origin,
    // not an additional entry; a live state without an origin starts at row zero.
    internal sealed class CheckpointSelection
    {
        private readonly List<object> _nodes = new();
        private int _index = -1;
        internal object Selected => _index >= 0 ? _nodes[_index] : null;
        internal bool Active => _index >= 0;
        internal int Revision { get; private set; }

        internal bool Begin(IEnumerable<object> nodes, object current, int direction = 1)
        {
            _nodes.Clear();
            _nodes.AddRange(nodes);
            int origin = _nodes.FindIndex(node => ReferenceEquals(node, current));
            _index = _nodes.Count == 0 ? -1 : origin < 0 ? 0
                : Math.Max(0, Math.Min(origin + direction, _nodes.Count - 1));
            Revision++;
            return Active;
        }

        internal void Move(int offset)
        {
            if (!Active) return;
            _index = Math.Max(0, Math.Min(_nodes.Count - 1, _index + offset));
            Revision++;
        }

        internal void Cancel()
        {
            if (!Active) return;
            _index = -1;
            _nodes.Clear();
            Revision++;
        }
    }
}
