using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ProbablyStolenTools
{
    // One original owner, exhaustive field manifest, no gameplay properties or collection rebuilding.
    internal sealed class SemanticFieldBlockFactory
    {
        private sealed class Schema
        {
            internal readonly Type Type;
            internal readonly FieldInfo[] Fields;
            internal readonly SnapshotFieldReader[] Readers;
            internal readonly bool[] References;
            internal readonly bool TypedComparisons;
            internal readonly HashSet<string> Runtime;
            internal IReadOnlyList<StateSlot> CheckedSlots;
            internal Dictionary<StateSlot, int> Indices;
            internal Schema(Type type, SemanticEntityFieldPlan[] plans, bool fastReads, bool typedComparisons)
            {
                if (plans == null || plans.Length == 0 || plans.Any(plan => plan == null)
                    || !plans.Any(plan => plan.Type == type)
                    || plans.Any(plan => !plan.Type.IsAssignableFrom(type))
                    || plans.Select(plan => plan.Type).Distinct().Count() != plans.Length)
                    throw new InvalidOperationException("Invalid exact entity block manifest chain.");
                Type = type;
                Fields = plans.SelectMany(plan => plan.Fields.Where(field =>
                    plan.RoleOf(Name(field)) != SemanticFieldRole.Visual)).ToArray();
                if (Fields.Length == 0 || Fields.Any(field => field.FieldType.IsValueType && !Supported(field.FieldType)))
                    throw new InvalidOperationException("Unsupported exact entity block fields.");
                Readers = Fields.Select(field => fastReads
                    ? SnapshotFieldReader.Get(type, Name(field), new[] { field }, false) : null).ToArray();
                References = Fields.Select(field => !field.FieldType.IsValueType).ToArray();
                TypedComparisons = typedComparisons;
                Runtime = plans.SelectMany(plan => plan.Fields.Where(field =>
                    plan.RoleOf(Name(field)) == SemanticFieldRole.Runtime)).Select(Name).ToHashSet(StringComparer.Ordinal);
            }

            internal bool Match(IReadOnlyList<StateSlot> slots)
            {
                if (ReferenceEquals(CheckedSlots, slots)) return Indices != null;
                CheckedSlots = slots;
                Indices = null;
                if (slots.Count != Fields.Length) return false;
                var fields = Fields.Select((field, index) => (field, index))
                    .ToDictionary(pair => Name(pair.field), pair => pair.index, StringComparer.Ordinal);
                var indices = new Dictionary<StateSlot, int>();
                var seen = new HashSet<int>();
                foreach (var slot in slots)
                {
                    if (!fields.TryGetValue(slot.Name, out int index) || !seen.Add(index)
                        || slot.IsReference != References[index] || slot.Size != 8)
                        return false;
                    indices.Add(slot, index);
                }
                Indices = indices;
                return true;
            }
        }

        private readonly Dictionary<Type, Schema> _schemas = new();
        internal int BlockCount { get; private set; }
        internal int SlotCount { get; private set; }
        internal int RuntimeSlotCount { get; private set; }
        internal int FallbackCount { get; private set; }

        internal SemanticFieldBlockFactory(IEnumerable<(Type Type, SemanticEntityFieldPlan[] Plans)> manifests,
            bool fastReads = true, bool typedComparisons = true)
        {
            foreach (var manifest in manifests)
                _schemas.Add(manifest.Type, new Schema(manifest.Type, manifest.Plans, fastReads, typedComparisons));
        }

        internal IStateBlock Capture(object target, IReadOnlyList<StateSlot> slots)
        {
            if (!_schemas.TryGetValue(target.GetType(), out var schema)) return null;
            if (!schema.Match(slots)) { FallbackCount++; return null; }
            var block = new Block(target, schema);
            BlockCount++;
            SlotCount += slots.Count;
            RuntimeSlotCount += schema.Runtime.Count;
            return block;
        }

        private static string Name(FieldInfo field) => field.DeclaringType.FullName + "." + field.Name;
        private static bool Supported(Type type) => type == typeof(byte) || type == typeof(bool)
            || type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double) || type.IsEnum;
        private static bool Same(object actual, object saved)
            => actual is float f && saved is float sf
                ? BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits(sf)
                : actual is double d && saved is double sd
                    ? BitConverter.DoubleToInt64Bits(d) == BitConverter.DoubleToInt64Bits(sd)
                    : Equals(actual, saved);

        private sealed class Block : IStateBlock, IStateOwnerRepairBlock, IStateStableStorageBlock
        {
            private readonly object _target;
            private readonly Schema _schema;
            private readonly FieldInfo[] _fields;
            private readonly Dictionary<StateSlot, int> _indices;
            private readonly object[] _values;
            private readonly IntPtr[] _references;
            internal Block(object target, Schema schema)
            {
                _target = target;
                _schema = schema;
                _fields = schema.Fields;
                // Freeze this layout even if the factory later sees a different descriptor list.
                _indices = schema.Indices;
                _values = new object[_fields.Length];
                _references = new IntPtr[_fields.Length];
                for (int i = 0; i < _fields.Length; i++)
                {
                    _values[i] = Read(i);
                    if (_schema.References[i]) _references[i] = ManagedObjects.Id(_values[i]);
                }
            }
            private object Read(int index)
                => _schema.Readers[index] != null && _schema.Readers[index].TryRead(_target, -1, out var value)
                    ? value : _fields[index].GetValue(_target);
            private bool Matches(int index)
            {
                if (_schema.References[index]) return ReferenceEquals(Read(index), _values[index]);
                if (_schema.TypedComparisons && _schema.Readers[index] != null
                    && _schema.Readers[index].TryEquals(_target, -1, _values[index], out bool same)) return same;
                return Same(Read(index), _values[index]);
            }
            public IntPtr ReferenceAt(StateSlot slot)
            {
                if (!slot.IsReference || !_indices.TryGetValue(slot, out int index))
                    throw new InvalidOperationException("Unknown entity block reference: " + slot.Name);
                return _references[index];
            }
            public void RestoreValues()
            {
                for (int i = 0; i < _fields.Length; i++)
                    if (!_schema.References[i] && !Matches(i))
                        _fields[i].SetValue(_target, _values[i]);
            }
            public void RestoreReferences()
            {
                for (int i = 0; i < _fields.Length; i++)
                    if (_schema.References[i] && !Matches(i))
                        _fields[i].SetValue(_target, _values[i]);
            }
            public int CollectDifferences(List<string> result, int max)
            {
                int changed = 0;
                for (int i = 0; i < _fields.Length; i++)
                {
                    if (Matches(i)) continue;
                    changed++;
                    if (result.Count < max) result.Add(Name(_fields[i]));
                }
                return changed;
            }
        }
    }
}
