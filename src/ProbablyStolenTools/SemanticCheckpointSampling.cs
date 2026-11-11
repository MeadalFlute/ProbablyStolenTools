namespace ProbablyStolenTools
{
    // Consume attempts, not successes: failed diagnostics must not retry on every customer.
    internal sealed class SemanticCheckpointSampling
    {
        private bool _primaryAttempted, _manualAttempted;

        internal bool TryBegin(bool manual, bool additionalManual, out string sample)
        {
            sample = "none";
            if (!_primaryAttempted)
            {
                _primaryAttempted = true;
                sample = "primary";
                return true;
            }
            if (!manual || !additionalManual || _manualAttempted) return false;
            _manualAttempted = true;
            sample = "manual-probe";
            return true;
        }

        internal void Reset() { _primaryAttempted = false; _manualAttempted = false; }
    }
}
