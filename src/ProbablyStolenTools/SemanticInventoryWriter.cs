#if MONO
using System;
using System.Linq;

namespace ProbablyStolenTools
{
    // Restore native inventory storage only; never Accept/Expel or rebuild member collections.
    internal sealed class SemanticInventoryWriter : SemanticFieldEntityWriter
    {
        private static SemanticEntityFieldPlan[] Chain(Type type)
        {
            if (type != typeof(GameGridInventory) && type != typeof(GameSlotInventory))
                throw new InvalidOperationException("Unsupported exact inventory writer type.");
            var plans = SemanticInventorySchemas.Create().ToDictionary(plan => plan.Type);
            return new[] { plans[type], plans[typeof(GameInventory)], plans[typeof(GameInventoryFunc)], plans[typeof(TagSystem)] };
        }

        private SemanticInventoryWriter(Type type) : base(type, Chain(type)) { }

        internal static SemanticInventoryWriter Capture(StateGraph graph, Type type, int maxObjects, int maxSlots, long maxBytes)
        {
            var writer = new SemanticInventoryWriter(type);
            writer.CaptureRecords(graph, maxObjects, maxSlots, maxBytes);
            return writer;
        }
    }
}
#endif
