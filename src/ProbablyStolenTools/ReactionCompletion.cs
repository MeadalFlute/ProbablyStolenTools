using System;

namespace ProbablyStolenTools
{
    internal static class ReactionCompletion
    {
        internal const int MaxSteps = 512;

        // Reuse the native recipe and rounding, but never run the thermal dispatcher.
        internal static bool Finish(Func<int> react, Func<int[]> read, out int steps, Action refresh = null)
        {
            refresh?.Invoke();
            var before = read();
            for (steps = 0; steps < MaxSteps; steps++)
            {
                if (react() <= 0) return true;
                refresh?.Invoke();
                var after = read();
                bool changed = before.Length != after.Length;
                for (int i = 0; !changed && i < before.Length; i++) changed = before[i] != after[i];
                if (!changed) return true;
                before = after;
            }
            return false;
        }
    }
}
