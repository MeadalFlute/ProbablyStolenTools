namespace ProbablyStolenTools
{
    internal sealed class CheckpointReturnGate
    {
        internal bool Outside { get; private set; }
        internal bool Pending { get; private set; }
        private float _stableSince = -1;
        // CLOSED keeps the native shutter lock until the next morning.
        internal static bool ShutterReady(int state, int locks) =>
            state >= 0 && state <= 2 && (state == 0 || locks == 0);
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
