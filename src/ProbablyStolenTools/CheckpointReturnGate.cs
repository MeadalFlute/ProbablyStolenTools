namespace ProbablyStolenTools
{
    internal sealed class CheckpointReturnGate
    {
        internal bool Outside { get; private set; }
        internal bool Pending { get; private set; }
        private float _stableSince = -1;
        internal void Leave() { Outside = true; CancelPending(); }
        internal void Return()
        {
            if (Outside) { Pending = true; _stableSince = -1; }
            Outside = false;
        }
        internal bool Poll(bool settled, float now)
        {
            if (!Pending || Outside) return false;
            if (!settled) { _stableSince = -1; return false; }
            if (_stableSince < 0) _stableSince = now;
            if (now - _stableSince < 0.5f) return false;
            CancelPending();
            return true;
        }
        internal void CancelPending() { Pending = false; _stableSince = -1; }
        internal void Reset() { Outside = false; CancelPending(); }
    }
}
