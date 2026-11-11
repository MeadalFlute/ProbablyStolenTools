#if MONO
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ProbablyStolenTools
{
    // Exact native storage only. Callbacks, focus stamps and collection internals stay on the old graph.
    internal sealed class SemanticWindowWriter : IStateEntityWriter, IStateEntityPartitionWriter, IDisposable
    {
        private sealed record Window(IntPtr Id, PixelWindow Target, object[] Values, IntPtr[] References);
        private static readonly SemanticEntityFieldPlan Schema = SemanticInventorySchemas.Create()
            .Single(plan => plan.Type == typeof(PixelWindow));
        private static readonly FieldInfo[] EntityFields = Schema.EntityFields;
        private readonly List<Window> _windows = new();
        private bool _disposed;
        public IReadOnlyCollection<IntPtr> Owners { get; private set; }
        public IReadOnlyList<StateEntityField> Fields { get; } = Array.AsReadOnly(EntityFields.Select(Describe).ToArray());
        public IReadOnlyList<StateEntityField> LegacyFields { get; } = Array.AsReadOnly(Schema.Fields
            .Where(field => Schema.RoleOf(field.DeclaringType.FullName + "." + field.Name) == SemanticFieldRole.Runtime)
            .Select(Describe).ToArray());
        internal int LogicalSlots => _windows.Count * EntityFields.Length;
        internal long LogicalBytes => (long)_windows.Count * EntityFields.Sum(member => member.FieldType.IsValueType ? 8 : IntPtr.Size);
        private static StateEntityField Describe(FieldInfo field)
            => new(field.DeclaringType.FullName + "." + field.Name, !field.FieldType.IsValueType);

        internal static SemanticWindowWriter Capture(StateGraph graph, int maxObjects, int maxSlots, long maxBytes)
        {
            var writer = new SemanticWindowWriter();
            try
            {
                long stride = EntityFields.Sum(field => field.FieldType.IsValueType ? 8 : IntPtr.Size);
                foreach (var id in graph.Objects)
                {
                    var target = ManagedObjects.Get(id);
                    if (target.GetType() != typeof(PixelWindow)) continue;
                    long count = (long)writer._windows.Count + 1;
                    if (count > maxObjects || count * EntityFields.Length > maxSlots || count * stride > maxBytes)
                        throw new InvalidOperationException("Window entity writer record budget exceeded.");
                    var values = new object[EntityFields.Length];
                    var references = new IntPtr[EntityFields.Length];
                    for (int i = 0; i < values.Length; i++)
                    {
                        values[i] = EntityFields[i].GetValue(target);
                        if (EntityFields[i].FieldType.IsValueType) continue;
                        references[i] = ManagedObjects.Id(values[i]);
                        if (values[i] != null && !graph.Contains(references[i]))
                            throw new InvalidOperationException("Window entity reference is outside the checkpoint.");
                    }
                    writer._windows.Add(new Window(id, (PixelWindow)target, values, references));
                }
                writer.Owners = Array.AsReadOnly(writer._windows.Select(window => window.Id).ToArray());
                if (graph.Differences(1).Count != 0)
                    throw new InvalidOperationException("Window writer capture differs from original checkpoint.");
                return writer;
            }
            catch { writer.Dispose(); throw; }
        }

        public void Validate()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SemanticWindowWriter));
            foreach (var window in _windows)
            {
                if (!ReferenceEquals(ManagedObjects.Get(window.Id), window.Target)
                    || window.Target.GetType() != typeof(PixelWindow))
                    throw new InvalidOperationException("Window writer retained owner identity changed.");
                for (int i = 0; i < EntityFields.Length; i++)
                    if (!EntityFields[i].FieldType.IsValueType && window.Values[i] != null
                        && !ReferenceEquals(ManagedObjects.Get(window.References[i]), window.Values[i]))
                        throw new InvalidOperationException("Window writer retained reference identity changed.");
            }
        }

        public void RestoreValues()
        {
            Validate();
            foreach (var window in _windows)
                for (int i = 0; i < EntityFields.Length; i++)
                {
                    var field = EntityFields[i];
                    if (!field.FieldType.IsValueType) continue;
                    var actual = field.GetValue(window.Target);
                    bool same = actual is float f && window.Values[i] is float saved
                        ? BitConverter.SingleToInt32Bits(f) == BitConverter.SingleToInt32Bits(saved)
                        : Equals(actual, window.Values[i]);
                    if (!same) field.SetValue(window.Target, window.Values[i]);
                }
        }

        public void RestoreReferences()
        {
            Validate();
            foreach (var window in _windows)
                for (int i = 0; i < EntityFields.Length; i++)
                    if (!EntityFields[i].FieldType.IsValueType
                        && !ReferenceEquals(EntityFields[i].GetValue(window.Target), window.Values[i]))
                        EntityFields[i].SetValue(window.Target, window.Values[i]);
        }

        public void Dispose()
        {
            _disposed = true;
            _windows.Clear();
            Owners = Array.Empty<IntPtr>();
        }
    }
}
#endif
