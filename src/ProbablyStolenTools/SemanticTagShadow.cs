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
        private readonly int _derivedSystems, _derivedStates;
        private readonly string _systemTypes, _stateTypes;
        private readonly string _ownership;
        internal string OwnershipSummary => _ownership;
        private SemanticTagShadow(SemanticTagSnapshot<TagSystem, TagState> snapshot,
            int derivedSystems, int derivedStates, string systemTypes, string stateTypes, string ownership)
        {
            _snapshot = snapshot;
            _derivedSystems = derivedSystems; _derivedStates = derivedStates;
            _systemTypes = systemTypes; _stateTypes = stateTypes;
            _ownership = ownership;
        }

        private static void CheckNativeType(Type type, Type baseType)
        {
            for (var current = type; current != baseType; current = current.BaseType)
                if (current == null || current.Assembly != baseType.Assembly)
                    throw new InvalidOperationException("Semantic tag base-field projection does not cover external type: " +
                        type.AssemblyQualifiedName);
        }

        internal static SemanticTagValue Read(TagState tag)
        {
            CheckNativeType(tag.GetType(), typeof(TagState));
            // These nonvirtual getters read only TagState storage, even for native subclasses.
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
            int derivedSystems = 0, derivedStates = 0;
            var systemTypes = new Dictionary<string, int>(StringComparer.Ordinal);
            var stateTypes = new Dictionary<string, int>(StringComparer.Ordinal);
            void Count(Dictionary<string, int> types, Type type)
            {
                string name = type.FullName;
                types.TryGetValue(name, out int count);
                types[name] = count + 1;
            }
            string Types(Dictionary<string, int> types) => string.Join(",", types
                .OrderByDescending(pair => pair.Value).ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Take(6).Select(pair => pair.Key + ":" + pair.Value)) +
                (types.Count > 6 ? ",otherTypes:" + (types.Count - 6) : "");
            IEnumerable<TagSystem> Systems()
            {
                foreach (var id in graph.Objects)
                {
                    if (!(ManagedObjects.Get(id) is TagSystem system)) continue;
                    try { CheckNativeType(system.GetType(), typeof(TagSystem)); }
                    catch (Exception e)
                    {
                        throw new InvalidOperationException(e.Message + " source=" + graph.SourceOf(id), e);
                    }
                    if (system.GetType() != typeof(TagSystem)) derivedSystems++;
                    Count(systemTypes, system.GetType());
                    yield return system;
                }
            }
            bool capturing = true;
            SemanticTagValue ReadState(TagState tag)
            {
                SemanticTagValue value;
                try { value = Read(tag); }
                catch (Exception e)
                {
                    throw new InvalidOperationException(e.Message + " source=" + graph.SourceOf(ManagedObjects.Id(tag)), e);
                }
                if (capturing)
                {
                    if (tag.GetType() != typeof(TagState)) derivedStates++;
                    Count(stateTypes, tag.GetType());
                }
                return value;
            }
            var snapshot = SemanticTagSnapshot<TagSystem, TagState>.Capture(Systems(), system => system.dict,
                ReadState, maxObjects, maxSlots, maxBytes);
            capturing = false;
            try
            {
                snapshot.ValidateCoverage(value => graph.Contains(ManagedObjects.Id(value)));
                var diff = snapshot.Differences();
                if (diff.Count != 0) throw new InvalidOperationException("Semantic tag capture changed: " + string.Join(", ", diff));
                var ownership = SemanticTagOwnershipAudit.Capture(graph, snapshot, "TagSystem.<dict>k__BackingField");
                return new SemanticTagShadow(snapshot, derivedSystems, derivedStates, Types(systemTypes), Types(stateTypes),
                    ownership.Summary);
            }
            catch { snapshot.Dispose(); throw; }
        }

        internal string Summary => "scope=tag-base-fields extraFields=legacy-only " +
            $"systems={_snapshot.SystemCount} maps={_snapshot.MapCount} " +
            $"entries={_snapshot.EntryCount} states={_snapshot.TagCount} " +
            $"logicalSlots={_snapshot.LogicalSlots} logicalBytes={_snapshot.LogicalBytes} " +
            $"derivedSystems={_derivedSystems} derivedStates={_derivedStates} " +
            $"systemTypes=[{_systemTypes}] stateTypes=[{_stateTypes}]";

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
