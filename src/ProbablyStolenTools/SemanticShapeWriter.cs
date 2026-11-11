#if MONO
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ProbablyStolenTools
{
    // First entity writer pilot. Original shape/byte-array identities; no geometry or gameplay methods.
    internal sealed class SemanticShapeWriter : IStateEntityWriter, IDisposable
    {
        private readonly record struct Shape(IntPtr Id, GridShapeBuilder Target, byte[] Data, IntPtr DataId, byte Outside,
            int MinX, int MinY, bool Flipped, int Orientation, int Width, int Height);
        private static readonly FieldInfo[] Schema = SemanticEntitySnapshot.Schema(typeof(GridShapeBuilder), true,
            ("data", typeof(byte[])), ("outside", typeof(byte)), ("<minX>k__BackingField", typeof(int)),
            ("<minY>k__BackingField", typeof(int)), ("<flipped>k__BackingField", typeof(bool)),
            ("<orientation>k__BackingField", typeof(int)), ("<width>k__BackingField", typeof(int)), ("<height>k__BackingField", typeof(int)));
        private readonly List<Shape> _shapes = new();
        private bool _disposed;
        public IReadOnlyCollection<IntPtr> Owners { get; private set; }
        public IReadOnlyList<StateEntityField> Fields { get; } = Array.AsReadOnly(Schema
            .Select(field => new StateEntityField(field.DeclaringType.FullName + "." + field.Name, !field.FieldType.IsValueType)).ToArray());

        internal static SemanticShapeWriter Capture(StateGraph graph, int maxObjects, int maxSlots, long maxBytes)
        {
            var writer = new SemanticShapeWriter();
            try
            {
                foreach (var id in graph.Objects)
                {
                    var target = ManagedObjects.Get(id);
                    if (target.GetType() != typeof(GridShapeBuilder)) continue;
                    int count = writer._shapes.Count;
                    if (count >= maxObjects || ((long)count + 1) * 8 > maxSlots
                        || ((long)count + 1) * (7 * 8 + IntPtr.Size) > maxBytes)
                        throw new InvalidOperationException("Shape entity writer record budget exceeded.");
                    var shape = (GridShapeBuilder)target;
                    var data = (byte[])Schema[0].GetValue(shape);
                    var dataId = ManagedObjects.Id(data);
                    if (data != null && !graph.Contains(dataId))
                        throw new InvalidOperationException("Shape data is outside the checkpoint.");
                    writer._shapes.Add(new Shape(id, shape, data, dataId, shape.outside,
                        shape.minX, shape.minY, shape.flipped, shape.orientation, shape.width, shape.height));
                }
                writer.Owners = Array.AsReadOnly(writer._shapes.Select(shape => shape.Id).ToArray());
                if (graph.Differences(1).Count != 0)
                    throw new InvalidOperationException("Shape writer capture differs from original checkpoint.");
                return writer;
            }
            catch { writer.Dispose(); throw; }
        }
        public void Validate()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SemanticShapeWriter));
            foreach (var shape in _shapes)
            {
                if (!ReferenceEquals(ManagedObjects.Get(shape.Id), shape.Target)
                    || shape.Target.GetType() != typeof(GridShapeBuilder)
                    || (shape.Data != null && !ReferenceEquals(ManagedObjects.Get(shape.DataId), shape.Data)))
                    throw new InvalidOperationException("Shape writer retained identity changed.");
            }
        }
        public void RestoreValues()
        {
            Validate();
            foreach (var shape in _shapes)
            {
                var target = shape.Target;
                if (target.outside != shape.Outside) target.outside = shape.Outside;
                if (target.minX != shape.MinX) Schema[2].SetValue(target, shape.MinX);
                if (target.minY != shape.MinY) Schema[3].SetValue(target, shape.MinY);
                if (target.flipped != shape.Flipped) Schema[4].SetValue(target, shape.Flipped);
                if (target.orientation != shape.Orientation) Schema[5].SetValue(target, shape.Orientation);
                if (target.width != shape.Width) Schema[6].SetValue(target, shape.Width);
                if (target.height != shape.Height) Schema[7].SetValue(target, shape.Height);
            }
        }
        public void RestoreReferences()
        {
            Validate();
            foreach (var shape in _shapes)
                if (!ReferenceEquals(Schema[0].GetValue(shape.Target), shape.Data)) Schema[0].SetValue(shape.Target, shape.Data);
        }
        public void Dispose()
        {
            _disposed = true;
            _shapes.Clear();
            Owners = Array.Empty<IntPtr>();
        }
    }
}
#endif
