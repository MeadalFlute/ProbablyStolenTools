using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ProbablyStolenTools
{
    // Original storage and identities only; never call gameplay setters or rebuild collections.
    internal class SemanticFieldEntityWriter : IStateEntityWriter, IStateEntityPartitionWriter, IDisposable
    {
        private sealed record Record(IntPtr Id, object Target, object[] Values, IntPtr[] References);
        private readonly Type _type;
        private readonly FieldInfo[] _entityFields;
        private readonly long _stride;
        private readonly List<Record> _records = new();
        private bool _disposed;
        private bool _captured;
        public IReadOnlyCollection<IntPtr> Owners { get; private set; } = Array.Empty<IntPtr>();
        public IReadOnlyList<StateEntityField> Fields { get; }
        public IReadOnlyList<StateEntityField> LegacyFields { get; }
        internal int LogicalSlots => _records.Count * _entityFields.Length;
        internal long LogicalBytes => _records.Count * _stride;

        protected SemanticFieldEntityWriter(Type type, SemanticEntityFieldPlan[] plans)
        {
            if (type == null || plans == null || plans.Length == 0 || plans.Any(plan => plan == null)
                || !plans.Any(plan => plan.Type == type)
                || plans.Select(plan => plan.Type).Distinct().Count() != plans.Length
                || plans.Any(plan => !plan.Type.IsAssignableFrom(type)))
                throw new InvalidOperationException("Invalid exact entity writer manifest chain.");
            _type = type;
            _entityFields = plans.SelectMany(plan => plan.EntityFields).ToArray();
            if (_entityFields.Length == 0 || _entityFields.Any(field => field.FieldType.IsValueType
                && !SupportedScalar(field.FieldType)))
                throw new InvalidOperationException("Unsupported entity writer scalar schema.");
            Fields = Array.AsReadOnly(_entityFields.Select(Describe).ToArray());
            LegacyFields = Array.AsReadOnly(plans.SelectMany(plan => plan.Fields.Where(field =>
                plan.RoleOf(field.DeclaringType.FullName + "." + field.Name) == SemanticFieldRole.Runtime))
                .Select(Describe).ToArray());
            _stride = _entityFields.Sum(field => field.FieldType.IsValueType ? 8 : IntPtr.Size);
        }

        private static bool SupportedScalar(Type type) => type == typeof(byte) || type == typeof(bool)
            || type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double) || type.IsEnum;
        private static StateEntityField Describe(FieldInfo field)
            => new(field.DeclaringType.FullName + "." + field.Name, !field.FieldType.IsValueType);

        protected void CaptureRecords(StateGraph graph, int maxObjects, int maxSlots, long maxBytes)
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            if (_captured) throw new InvalidOperationException("Entity writer capture is not fresh.");
            _captured = true;
            try
            {
                foreach (var id in graph.Objects)
                {
                    var target = ManagedObjects.Get(id);
                    if (target.GetType() != _type) continue;
                    long count = (long)_records.Count + 1;
                    if (count > maxObjects || count * _entityFields.Length > maxSlots || count * _stride > maxBytes)
                        throw new InvalidOperationException(_type.Name + " entity writer record budget exceeded.");
                    var values = new object[_entityFields.Length];
                    var references = new IntPtr[_entityFields.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        values[i] = _entityFields[i].GetValue(target);
                        if (_entityFields[i].FieldType.IsValueType) continue;
                        references[i] = ManagedObjects.Id(values[i]);
                        if (values[i] != null && !graph.Contains(references[i]))
                            throw new InvalidOperationException(_type.Name + " entity reference is outside the checkpoint.");
                    }
                    _records.Add(new Record(id, target, values, references));
                }
                Owners = Array.AsReadOnly(_records.Select(record => record.Id).ToArray());
                if (graph.Differences(1).Count != 0)
                    throw new InvalidOperationException(_type.Name + " entity writer capture differs from original checkpoint.");
            }
            catch { Dispose(); throw; }
        }

        public void Validate()
        {
            if (_disposed) throw new ObjectDisposedException(GetType().Name);
            foreach (var record in _records)
            {
                if (!ReferenceEquals(ManagedObjects.Get(record.Id), record.Target) || record.Target.GetType() != _type)
                    throw new InvalidOperationException("Entity writer retained owner identity changed.");
                for (int i = 0; i < _entityFields.Length; i++)
                    if (!_entityFields[i].FieldType.IsValueType && record.Values[i] != null
                        && !ReferenceEquals(ManagedObjects.Get(record.References[i]), record.Values[i]))
                        throw new InvalidOperationException("Entity writer retained reference identity changed.");
            }
        }

        public void RestoreValues()
        {
            Validate();
            foreach (var record in _records)
                for (int i = 0; i < _entityFields.Length; i++)
                {
                    var field = _entityFields[i];
                    if (!field.FieldType.IsValueType) continue;
                    var actual = field.GetValue(record.Target);
                    bool same = actual is float f && record.Values[i] is float saved
                        ? BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits(saved)
                        : actual is double d && record.Values[i] is double captured
                            ? BitConverter.DoubleToInt64Bits(d) == BitConverter.DoubleToInt64Bits(captured)
                            : Equals(actual, record.Values[i]);
                    if (!same) field.SetValue(record.Target, record.Values[i]);
                }
        }

        public void RestoreReferences()
        {
            Validate();
            foreach (var record in _records)
                for (int i = 0; i < _entityFields.Length; i++)
                    if (!_entityFields[i].FieldType.IsValueType
                        && !ReferenceEquals(_entityFields[i].GetValue(record.Target), record.Values[i]))
                        _entityFields[i].SetValue(record.Target, record.Values[i]);
        }

        public void Dispose()
        {
            _disposed = true;
            _records.Clear();
            Owners = Array.Empty<IntPtr>();
        }
    }
}
