#if MONO
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace ProbablyStolenTools
{
    internal sealed class SemanticTagShadow : IDisposable
    {
        private readonly SemanticTagSnapshot<TagSystem, TagState> _snapshot;
        private SemanticTagShadow(SemanticTagSnapshot<TagSystem, TagState> snapshot) { _snapshot = snapshot; }

        internal static SemanticTagValue Read(TagState tag)
        {
            if (tag.GetType() != typeof(TagState))
                throw new InvalidOperationException("Semantic tags do not cover TagState subclasses.");
            return new SemanticTagValue(tag.valueEnabled, tag.valueString, tag.valueInt,
                BitConverter.SingleToInt32Bits(tag.valueFloat), tag.valueLong,
                BitConverter.DoubleToInt64Bits(tag.valueDouble), tag.valueBool, tag.identifier, tag.identifierName);
        }

        private static void CheckSchema(Type type, params string[] names)
        {
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (!fields.Select(field => field.Name).OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual(names.OrderBy(name => name, StringComparer.Ordinal)))
                throw new InvalidOperationException("Semantic tag schema changed: " + type.FullName);
        }

        internal static SemanticTagShadow Capture(StateGraph graph, int maxObjects, int maxSlots, long maxBytes)
        {
            CheckSchema(typeof(TagSystem), "<dict>k__BackingField");
            CheckSchema(typeof(TagState), "valueEnabled", "internalValueString", "valueInt", "valueFloat",
                "valueLong", "valueDouble", "valueBool", "<identifier>k__BackingField", "<identifierName>k__BackingField");
            IEnumerable<TagSystem> Systems()
            {
                foreach (var id in graph.Objects)
                    if (ManagedObjects.Get(id) is TagSystem system) yield return system;
            }
            var snapshot = SemanticTagSnapshot<TagSystem, TagState>.Capture(Systems(), system =>
            {
                if (system.GetType() != typeof(TagSystem))
                    throw new InvalidOperationException("Semantic tags do not cover TagSystem subclasses.");
                return system.dict;
            }, Read, maxObjects, maxSlots, maxBytes);
            try
            {
                snapshot.ValidateCoverage(value => graph.Contains(ManagedObjects.Id(value)));
                var diff = snapshot.Differences();
                if (diff.Count != 0) throw new InvalidOperationException("Semantic tag capture changed: " + string.Join(", ", diff));
                return new SemanticTagShadow(snapshot);
            }
            catch { snapshot.Dispose(); throw; }
        }

        internal string Summary => $"systems={_snapshot.SystemCount} maps={_snapshot.MapCount} " +
            $"entries={_snapshot.EntryCount} states={_snapshot.TagCount} " +
            $"logicalSlots={_snapshot.LogicalSlots} logicalBytes={_snapshot.LogicalBytes}";

        internal void ObserveRestore(long id)
        {
            var clock = Stopwatch.StartNew();
            try
            {
                var diff = _snapshot.Differences();
                EventLog.Write($"[SemanticTags] {(diff.Count == 0 ? "RESTORE MATCH" : "RESTORE VALIDATION FAILED")} " +
                    $"id={id} readOnly=true diff={diff.Count} verifyMs={clock.ElapsedMilliseconds} " +
                    Summary + (diff.Count == 0 ? "" : " fields=" + string.Join(", ", diff)));
            }
            catch (Exception e)
            {
                EventLog.Write($"[SemanticTags] RESTORE VALIDATION FAILED id={id} readOnly=true " +
                    $"verifyMs={clock.ElapsedMilliseconds}: " + e);
            }
        }

        public void Dispose() => _snapshot.Dispose();
    }
}
#endif
