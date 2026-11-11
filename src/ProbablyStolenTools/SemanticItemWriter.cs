#if MONO
namespace ProbablyStolenTools
{
    // Exact full item hierarchy; callbacks stay on the old graph, layout caches stay visual.
    internal sealed class SemanticItemWriter : SemanticFieldEntityWriter
    {
        private SemanticItemWriter() : base(typeof(GameItemElement), SemanticItemSchemas.Create()) { }

        internal static SemanticItemWriter Capture(StateGraph graph, int maxObjects, int maxSlots, long maxBytes)
        {
            var writer = new SemanticItemWriter();
            writer.CaptureRecords(graph, maxObjects, maxSlots, maxBytes);
            return writer;
        }
    }
}
#endif
