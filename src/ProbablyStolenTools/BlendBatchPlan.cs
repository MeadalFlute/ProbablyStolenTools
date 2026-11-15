using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    internal sealed class BlendBatchPlan<T> where T : class
    {
        private sealed class MachinePlan
        {
            internal string Product;
            internal readonly Dictionary<T, long> Inputs = new();
        }
        private readonly Dictionary<T, MachinePlan> _machines = new();

        internal bool Reserve(T machine, T input, string product, long output, long capacity,
            long contents, long residentOutput, Predicate<T> isResident)
        {
            _machines.TryGetValue(machine, out var plan);
            if (plan != null && plan.Product != product) return false;
            long reserved = residentOutput;
            if (plan != null)
                foreach (var pair in plan.Inputs)
                    if (!ReferenceEquals(pair.Key, input) && !isResident(pair.Key)) reserved += pair.Value;
            if (output <= 0 || contents + reserved + output > capacity) return false;
            if (plan == null) _machines.Add(machine, plan = new MachinePlan { Product = product });
            plan.Inputs[input] = output;
            return true;
        }

        // A completed native transfer has already blended its inputs into the bottle.
        // Keep the product lock, but do not reserve their volume a second time.
        internal void BatchBoundary()
        {
            foreach (var plan in _machines.Values) plan.Inputs.Clear();
        }
        internal void Clear() => _machines.Clear();
    }
}
