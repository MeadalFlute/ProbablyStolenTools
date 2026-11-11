#if MONO
using System;
using System.Linq;
using System.Reflection;

namespace ProbablyStolenTools
{
    // Exact native storage only. Callbacks, focus stamps and collection internals stay on the old graph.
    internal sealed class SemanticWindowWriter : SemanticFieldEntityWriter
    {
        private static readonly SemanticEntityFieldPlan Schema = SemanticInventorySchemas.Create()
            .Single(plan => plan.Type == typeof(PixelWindow));
        private static readonly FieldInfo[] EntityFields = Schema.EntityFields;
        private SemanticWindowWriter() : base(typeof(PixelWindow), new[] { Schema }) { }

        internal static SemanticWindowWriter Capture(StateGraph graph, int maxObjects, int maxSlots, long maxBytes)
        {
            var writer = new SemanticWindowWriter();
            writer.CaptureRecords(graph, maxObjects, maxSlots, maxBytes);
            return writer;
        }
    }
}
#endif
