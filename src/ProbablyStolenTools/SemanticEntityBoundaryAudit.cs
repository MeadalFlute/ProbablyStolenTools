using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProbablyStolenTools
{
    internal enum SemanticEntityRole { None, Item, GridInventory, SlotInventory, OtherInventory, Callback, Array }
    internal readonly record struct SemanticEntityObject(IntPtr Id, SemanticEntityRole Role, bool Native = true);

    // Captured topology only. This cannot certify exclusive ownership or authorize pruning/restoration.
    internal sealed class SemanticEntityBoundaryAudit
    {
        internal int Items, GridInventories, SlotInventories, OtherInventories, ExternalEntities;
        internal int RootEntities, SharedEntities, UnreferencedEntities;
        internal long ScannedSlots, EntitySlots, EntityLinks, InternalLinks, OutgoingRuntimeLinks;
        internal long IncomingRuntimeLinks, CallbackIncomingLinks, ArrayIncomingLinks, CallbackOutgoingLinks;
        internal long ElapsedMs;
        internal readonly List<string> CallbackSamples = new(), OutgoingCallbackSamples = new(), BoundarySamples = new();
        internal int EntityCount => Items + GridInventories + SlotInventories + OtherInventories;
        internal string Summary => "scope=captured-graph-only readOnly=true pruningAllowed=false restoreAllowed=false " +
            $"entities={EntityCount} items={Items} inventories(grid={GridInventories},slot={SlotInventories},other={OtherInventories}) " +
            $"externalEntities={ExternalEntities} rootEntities={RootEntities} sharedEntities={SharedEntities} " +
            $"unreferencedEntities={UnreferencedEntities} scannedSlots={ScannedSlots} entitySlots={EntitySlots} " +
            $"entityLinks={EntityLinks} internalLinks={InternalLinks} outgoingRuntimeLinks={OutgoingRuntimeLinks} " +
            $"incomingRuntimeLinks={IncomingRuntimeLinks} callbackIncomingLinks={CallbackIncomingLinks} " +
            $"arrayIncomingLinks={ArrayIncomingLinks} otherIncomingLinks={IncomingRuntimeLinks - CallbackIncomingLinks - ArrayIncomingLinks} " +
            $"callbackOutgoingLinks={CallbackOutgoingLinks} auditMs={ElapsedMs} " +
            "callbackClassification=metadata-heuristic opaqueAndStaticReferences=unproven " +
            $"callbackSamples=[{string.Join(";", CallbackSamples)}] " +
            $"outgoingCallbackSamples=[{string.Join(";", OutgoingCallbackSamples)}] " +
            $"boundarySamples=[{string.Join(";", BoundarySamples)}]";

        private static bool IsEntity(SemanticEntityRole role) => role == SemanticEntityRole.Item
            || role == SemanticEntityRole.GridInventory || role == SemanticEntityRole.SlotInventory
            || role == SemanticEntityRole.OtherInventory;

        internal static SemanticEntityBoundaryAudit Capture(StateGraph graph, IEnumerable<SemanticEntityObject> objects,
            int maxIndexedObjects = 200000)
        {
            if (maxIndexedObjects < 0) throw new ArgumentOutOfRangeException(nameof(maxIndexedObjects));
            var clock = Stopwatch.StartNew();
            var result = new SemanticEntityBoundaryAudit();
            var index = new Dictionary<IntPtr, SemanticEntityObject>();
            var incoming = new Dictionary<IntPtr, int>();
            foreach (var entry in objects)
            {
                if (entry.Id == IntPtr.Zero || !graph.Contains(entry.Id)
                    || entry.Role <= SemanticEntityRole.None || entry.Role > SemanticEntityRole.Array)
                    throw new InvalidOperationException("Entity audit identity/role is not covered by the captured graph.");
                if (index.TryGetValue(entry.Id, out var previous))
                {
                    if (previous != entry) throw new InvalidOperationException("Conflicting entity audit identity roles.");
                    continue;
                }
                if (index.Count >= maxIndexedObjects)
                    throw new InvalidOperationException($"Entity audit index limit exceeded: indexed={index.Count}/{maxIndexedObjects}.");
                index.Add(entry.Id, entry);
                if (!IsEntity(entry.Role)) continue;
                incoming.Add(entry.Id, 0);
                if (!entry.Native) result.ExternalEntities++;
                if (graph.IsCapturedRoot(entry.Id)) result.RootEntities++;
                switch (entry.Role)
                {
                    case SemanticEntityRole.Item: result.Items++; break;
                    case SemanticEntityRole.GridInventory: result.GridInventories++; break;
                    case SemanticEntityRole.SlotInventory: result.SlotInventories++; break;
                    case SemanticEntityRole.OtherInventory: result.OtherInventories++; break;
                }
            }
            void Sample(List<string> samples, int limit, string kind, IntPtr owner, StateSlot slot)
            {
                if (samples.Count >= limit) return;
                string Tail(string text, int length)
                {
                    if (string.IsNullOrEmpty(text)) return "unknown";
                    if (text.Length > length) text = "..." + text.Substring(text.Length - length + 3);
                    return text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
                }
                samples.Add(kind + " owner=0x" + owner.ToInt64().ToString("X")
                    + " field=" + Tail(slot.Name, 96) + " source=" + Tail(graph.SourceOf(owner), 240));
            }
            graph.VisitCapturedSlots((owner, slot, target) =>
            {
                result.ScannedSlots++;
                bool entityOwner = index.TryGetValue(owner, out var source) && IsEntity(source.Role);
                if (entityOwner) result.EntitySlots++;
                if (!slot.IsReference || target == IntPtr.Zero) return;
                bool entityTarget = index.TryGetValue(target, out var destination) && IsEntity(destination.Role);
                if (entityTarget) incoming[target]++;
                if (entityOwner)
                {
                    result.EntityLinks++;
                    if (entityTarget) result.InternalLinks++;
                    else
                    {
                        result.OutgoingRuntimeLinks++;
                        if (destination.Role == SemanticEntityRole.Callback)
                        {
                            result.CallbackOutgoingLinks++;
                            Sample(result.OutgoingCallbackSamples, 1, "entity-to-callback", owner, slot);
                        }
                    }
                }
                else if (entityTarget)
                {
                    result.IncomingRuntimeLinks++;
                    if (source.Role == SemanticEntityRole.Callback)
                    {
                        result.CallbackIncomingLinks++;
                        Sample(result.CallbackSamples, 3, "callback-to-entity", owner, slot);
                    }
                    else
                    {
                        if (source.Role == SemanticEntityRole.Array) result.ArrayIncomingLinks++;
                        Sample(result.BoundarySamples, 2, source.Role == SemanticEntityRole.Array
                            ? "array-to-entity" : "runtime-to-entity", owner, slot);
                    }
                }
            });
            foreach (int count in incoming.Values)
            {
                if (count == 0) result.UnreferencedEntities++;
                else if (count > 1) result.SharedEntities++;
            }
            result.ElapsedMs = clock.ElapsedMilliseconds;
            return result;
        }
    }
}
