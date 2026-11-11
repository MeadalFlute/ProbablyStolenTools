namespace ProbablyStolenTools
{
    internal static class CheckpointItemInteraction
    {
        // A released, static selection is safe; its old homes must be cleared before rewind.
        internal static bool IsBusy(bool singleDragging, bool groupDragging, bool hasSelection, bool keyHeld)
            => singleDragging || groupDragging || (hasSelection && keyHeld);
    }
}
