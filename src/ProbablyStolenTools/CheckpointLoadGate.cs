namespace ProbablyStolenTools
{
    // A store singleton can exist before native LoadGame has decoded its contents.
    internal sealed class CheckpointLoadGate
    {
        private SaveMetadata _expected;
        internal bool Completed { get; private set; }
        internal void Begin(SaveMetadata expected) { _expected = expected; Completed = false; }
        internal bool Loaded(int day, int slot, string runId)
        {
            Completed = _expected != null && day == _expected.Day && slot == _expected.Slot
                && runId == _expected.RunId;
            return Completed;
        }
        internal void Reset() { _expected = null; Completed = false; }
    }
}
