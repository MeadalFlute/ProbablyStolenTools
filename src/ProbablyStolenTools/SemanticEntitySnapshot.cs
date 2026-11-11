using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace ProbablyStolenTools
{
    internal readonly record struct SemanticEntityProjection(object Target, FieldInfo[] Fields);

    // Independent read-only records. No setters, gameplay getters or restoration API.
    internal sealed class SemanticEntitySnapshot : IDisposable
    {
        private sealed class Record
        {
            internal IntPtr Id;
            internal object Target;
            internal FieldInfo[] Fields;
            internal object[] Values;
        }
        private sealed class Sequence
        {
            internal IntPtr Id;
            internal IList Target;
            internal object[] Values;
            internal bool References;
        }
        private readonly List<Record> _records = new();
        private readonly List<Sequence> _sequences = new();
        private bool _disposed;
        internal int RecordCount => _records.Count;
        internal int SequenceCount => _sequences.Count;
        internal int LogicalSlots { get; private set; }
        internal long LogicalBytes { get; private set; }

        internal static FieldInfo[] Schema(Type type, bool complete, params (string Name, Type Type)[] fields)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            if (complete && type.GetFields(flags).Length != fields.Length)
                throw new InvalidOperationException("Entity schema field count changed: " + type.FullName);
            var result = new FieldInfo[fields.Length];
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < fields.Length; i++)
            {
                var field = type.GetField(fields[i].Name, flags);
                if (field == null || field.FieldType != fields[i].Type || !names.Add(field.Name))
                    throw new InvalidOperationException("Entity schema changed: " + type.FullName + "." + fields[i].Name);
                result[i] = field;
            }
            return result;
        }

        private static bool SupportedValue(Type type) => type == typeof(byte) || type == typeof(bool)
            || type == typeof(int) || type == typeof(long) || type == typeof(float) || type == typeof(double);

        private static bool Same(object actual, object captured, bool reference)
        {
            if (reference) return ReferenceEquals(actual, captured);
            if (actual is float f && captured is float c)
                return BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits(c);
            if (actual is double d && captured is double e)
                return BitConverter.DoubleToInt64Bits(d) == BitConverter.DoubleToInt64Bits(e);
            return actual != null && actual.Equals(captured);
        }

        internal static SemanticEntitySnapshot Capture(IEnumerable<SemanticEntityProjection> projections,
            Func<object, IntPtr> identity, Func<IntPtr, bool> covered,
            int maxObjects = 200000, int maxSlots = 3000000, long maxBytes = 256L * 1024 * 1024)
        {
            var result = new SemanticEntitySnapshot();
            var objects = new HashSet<IntPtr>();
            var owners = new HashSet<IntPtr>();
            var sequences = new HashSet<IntPtr>();
            try
            {
                IntPtr Require(object target)
                {
                    if (target == null) return IntPtr.Zero;
                    var id = identity(target);
                    if (id == IntPtr.Zero || !covered(id))
                        throw new InvalidOperationException("Entity projection reference is outside the legacy checkpoint: " + target.GetType().FullName);
                    if (!objects.Contains(id))
                    {
                        if (objects.Count >= maxObjects) throw new InvalidOperationException("Entity record object budget exceeded.");
                        objects.Add(id);
                    }
                    return id;
                }
                void Reserve(int count, bool references)
                {
                    long bytes = (long)count * (references ? IntPtr.Size : 8);
                    if (count < 0 || count > (long)maxSlots - result.LogicalSlots || bytes > maxBytes - result.LogicalBytes)
                        throw new InvalidOperationException("Entity record slot/byte budget exceeded.");
                    result.LogicalSlots += count;
                    result.LogicalBytes += bytes;
                }
                void Sequence(object target)
                {
                    if (target == null) return;
                    var type = target.GetType();
                    bool bytes = type == typeof(byte[]);
                    bool list = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)
                        && !type.GetGenericArguments()[0].IsValueType;
                    if (!bytes && !list) return;
                    var id = Require(target);
                    if (!sequences.Add(id)) return;
                    var data = (IList)target;
                    Reserve(data.Count, !bytes);
                    var values = new object[data.Count];
                    for (int i = 0; i < values.Length; i++)
                    {
                        values[i] = data[i];
                        if (!bytes) Require(values[i]);
                    }
                    result._sequences.Add(new Sequence { Id = id, Target = data, Values = values, References = !bytes });
                }
                foreach (var projection in projections)
                {
                    if (projection.Target == null || projection.Fields == null || projection.Fields.Length == 0)
                        throw new InvalidOperationException("Empty entity projection.");
                    var id = Require(projection.Target);
                    if (!owners.Add(id)) throw new InvalidOperationException("Duplicate entity record identity.");
                    var fields = (FieldInfo[])projection.Fields.Clone();
                    var names = new HashSet<FieldInfo>();
                    // Validate and reserve the whole record before allocating its value payload.
                    foreach (var field in fields)
                    {
                        if (field == null || field.IsStatic || !field.DeclaringType.IsInstanceOfType(projection.Target)
                            || !names.Add(field) || (field.FieldType.IsValueType && !SupportedValue(field.FieldType)))
                            throw new InvalidOperationException("Unsupported entity projection field.");
                        Reserve(1, !field.FieldType.IsValueType);
                    }
                    var values = new object[fields.Length];
                    for (int i = 0; i < fields.Length; i++)
                    {
                        values[i] = fields[i].GetValue(projection.Target);
                        if (!fields[i].FieldType.IsValueType)
                        {
                            Require(values[i]);
                            Sequence(values[i]);
                        }
                    }
                    result._records.Add(new Record { Id = id, Target = projection.Target, Fields = fields, Values = values });
                }
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        internal int Differences(out IReadOnlyList<string> sample, int max = 8)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SemanticEntitySnapshot));
            var messages = new List<string>();
            int count = 0;
            void Changed(IntPtr owner, string field, int index = -1)
            {
                count++;
                if (messages.Count < max) messages.Add("owner=0x" + owner.ToInt64().ToString("X") + " " +
                    field + (index < 0 ? "" : "[" + index + "]"));
            }
            foreach (var record in _records)
                for (int i = 0; i < record.Fields.Length; i++)
                {
                    var field = record.Fields[i];
                    if (!Same(field.GetValue(record.Target), record.Values[i], !field.FieldType.IsValueType))
                        Changed(record.Id, field.DeclaringType.Name + "." + field.Name);
                }
            foreach (var sequence in _sequences)
            {
                int length = sequence.Target.Count;
                if (length != sequence.Values.Length) Changed(sequence.Id, "sequence.length");
                for (int i = 0; i < Math.Min(length, sequence.Values.Length); i++)
                    if (!Same(sequence.Target[i], sequence.Values[i], sequence.References))
                        Changed(sequence.Id, "sequence", i);
            }
            sample = messages;
            return count;
        }

        public void Dispose()
        {
            _records.Clear(); _sequences.Clear();
            _disposed = true;
        }
    }
}
