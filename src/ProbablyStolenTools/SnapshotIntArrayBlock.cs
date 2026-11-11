using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    // Complete physical storage, including unused dictionary buckets; never rebuild a collection.
    internal sealed class SnapshotIntArrayBlock : IStateBlock, IStateOwnerRepairBlock
    {
        private readonly int[] _target;
        private readonly int[] _values;
        private readonly IReadOnlyList<StateSlot> _slots;

        internal SnapshotIntArrayBlock(int[] target, IReadOnlyList<StateSlot> slots)
        {
            _target = target;
            _values = (int[])target.Clone();
            _slots = slots;
        }

        public IntPtr ReferenceAt(StateSlot slot)
            => throw new InvalidOperationException("An int array payload has no reference slots.");

        public void RestoreValues() => Array.Copy(_values, _target, _values.Length);
        public void RestoreReferences() { }

        public int CollectDifferences(List<string> result, int max)
        {
            int changed = 0;
            for (int i = 0; i < _values.Length; i++)
                if (_target[i] != _values[i])
                {
                    changed++;
                    if (result.Count < max) result.Add(_slots[i].Name);
                }
            return changed;
        }
    }
}
