using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ProbablyStolenTools
{
    // Snapshot real managed fields and array elements; reflection preserves GC write barriers.
    internal sealed class ManagedStateAccess : IStateAccess, IStateValueComparer
    {
        private readonly struct Storage
        {
            internal readonly int Index;
            internal readonly FieldInfo[] Path;
            internal readonly object Target;
            internal readonly SnapshotFieldReader Reader;
            internal bool Anchor => Target != null;
            internal Storage(int index, FieldInfo[] path, object target = null, SnapshotFieldReader reader = null)
            { Index = index; Path = path; Target = target; Reader = reader; }
        }

        // Only immutable field descriptions are shared; values and array indices remain per capture.
        private sealed class TypeSchema
        {
            internal readonly StateSlot[] Slots;
            internal readonly Storage[] Stores;
            internal TypeSchema(List<StateSlot> slots, List<Storage> stores)
            { Slots = slots.ToArray(); Stores = stores.ToArray(); }
        }

        private readonly Dictionary<IntPtr, object> _retained = new();
        private readonly Dictionary<IntPtr, HashSet<string>> _roots = new();
        private readonly Dictionary<IntPtr, Storage[]> _storage = new();
        private readonly Dictionary<Type, TypeSchema> _schemas = new();
        private readonly Dictionary<Type, TypeSchema> _elementSchemas = new();
        private readonly Dictionary<Type, bool> _traversable = new();
        private readonly Dictionary<object, byte[]> _values = new();
        private readonly Dictionary<long, object> _savedValues = new();
        private long _nextValue;
        private static readonly object NullValue = new();

        // 纯运行时锁/转场计数：只由游戏自己维护，快照既不该保存也不该恢复。
        private static readonly HashSet<string> TransientLocks = new(StringComparer.Ordinal)
        {
            "PlayerStore.lockShutterButtonFlag",
            "PlayerStore.lockAllFlag",
            "PlayerStore.lockAllInventoryFlag",
        };
        // These describe native RectTransforms, not business state. Restoring them
        // without those transforms makes UpdateRectTransform skip a required layout.
        private static readonly HashSet<string> ItemLayoutCache = new(StringComparer.Ordinal)
        {
            "appliedMinX", "appliedMinY", "appliedOrientation",
            "appliedGridSize", "appliedFlipped", "validateLock",
        };
        private readonly Type _unityObject;
        private readonly string _gameAssembly;
        private readonly bool _fastReads;

        internal ManagedStateAccess(Type unityObject = null, string gameAssembly = "Assembly-CSharp")
            : this(unityObject, gameAssembly, true) { }

        internal ManagedStateAccess(Type unityObject, string gameAssembly, bool fastReads)
        {
            _unityObject = unityObject;
            _gameAssembly = gameAssembly;
            _fastReads = fastReads;
        }

        internal void AddRoot(IntPtr id, params string[] fields)
        {
            if (id == IntPtr.Zero) throw new InvalidOperationException("Required snapshot root missing.");
            _roots[id] = fields.Length == 0 ? null : new HashSet<string>(fields);
        }

        public void Retain(IntPtr id)
        {
            if (!_retained.ContainsKey(id)) _retained[id] = ManagedObjects.Get(id);
        }

        public IReadOnlyList<StateSlot> Describe(IntPtr id)
        {
            var value = _retained[id];
            var type = value.GetType();
            bool root = _roots.TryGetValue(id, out var selected);
            if (!root)
            {
                if (!_traversable.TryGetValue(type, out bool traversable))
                    _traversable.Add(type, traversable = Traversable(type));
                if (!traversable)
                {
                    _storage[id] = Array.Empty<Storage>();
                    return Array.Empty<StateSlot>();
                }
            }
            if (value is Array array)
            {
                if (array.Rank != 1 || array.GetLowerBound(0) != 0 || array.Length > 262144)
                    throw new InvalidOperationException("Unsupported snapshot array.");
                // Empty arrays have no fields to flatten, even for otherwise unsupported element types.
                if (array.Length == 0)
                {
                    _storage[id] = Array.Empty<Storage>();
                    return Array.Empty<StateSlot>();
                }
                var elementType = type.GetElementType();
                if (!_elementSchemas.TryGetValue(elementType, out var element))
                {
                    var elementSlots = new List<StateSlot>();
                    var elementStores = new List<Storage>();
                    Flatten(elementType, new List<FieldInfo>(), -1, "", elementSlots, elementStores, 0);
                    PrepareReaders(elementType.MakeArrayType(), elementSlots, elementStores, true);
                    _elementSchemas.Add(elementType, element = new TypeSchema(elementSlots, elementStores));
                }
                bool vector = type == elementType.MakeArrayType();
                int stride = element.Stores.Length;
                var slots = new StateSlot[checked(array.Length * stride)];
                var stores = new Storage[slots.Length];
                for (int i = 0; i < array.Length; i++)
                {
                    string prefix = type.FullName + "[" + i + "]";
                    for (int j = 0; j < stride; j++)
                    {
                        int offset = i * stride + j;
                        var slot = element.Slots[j];
                        slots[offset] = new StateSlot(offset, slot.Size, slot.IsReference, prefix + slot.Name);
                        var storage = element.Stores[j];
                        stores[offset] = new Storage(i, storage.Path, reader: vector ? storage.Reader : null);
                    }
                }
                _storage[id] = stores;
                return slots;
            }
            if (value is Delegate action)
            {
                var slots = new List<StateSlot>();
                var stores = new List<Storage>();
                // Delegates are immutable; visit every invocation target without rewriting code/identity.
                foreach (var handler in action.GetInvocationList())
                    if (handler.Target != null)
                    {
                        slots.Add(new StateSlot(stores.Count, 8, true, type.FullName + ".target"));
                        stores.Add(new Storage(-1, null, handler.Target));
                    }
                _storage[id] = stores.ToArray();
                return slots;
            }
            TypeSchema schema;
            if (root && selected != null) schema = ObjectSchema(type, selected);
            else if (!_schemas.TryGetValue(type, out schema))
                _schemas.Add(type, schema = ObjectSchema(type, null));
            _storage[id] = schema.Stores;
            return schema.Slots;
        }

        private TypeSchema ObjectSchema(Type type, HashSet<string> selected)
        {
            var slots = new List<StateSlot>();
            var stores = new List<Storage>();
            for (var current = type; current != null && !IsUnity(current); current = current.BaseType)
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (selected != null && !selected.Contains(field.Name)) continue;
                    if (TransientLocks.Contains(current.FullName + "." + field.Name)) continue;
                    if (current.FullName == "GameItemElement" && ItemLayoutCache.Contains(field.Name)) continue;
                    if (Visual(field.FieldType)) continue;
                    Flatten(field.FieldType, new List<FieldInfo> { field }, -1,
                        current.FullName + "." + field.Name, slots, stores, 0);
                }
            if (selected != null)
                foreach (string name in selected)
                    if (!slots.Any(s => s.Name.EndsWith("." + name, StringComparison.Ordinal)
                        || s.Name.Contains("." + name + ".", StringComparison.Ordinal)))
                        throw new InvalidOperationException("Required snapshot field missing: " + name);
            PrepareReaders(type, slots, stores, false);
            return new TypeSchema(slots, stores);
        }

        private void PrepareReaders(Type owner, List<StateSlot> slots, List<Storage> stores, bool element)
        {
            if (!_fastReads) return;
            for (int i = 0; i < stores.Count; i++)
            {
                var storage = stores[i];
                stores[i] = new Storage(storage.Index, storage.Path, reader:
                    SnapshotFieldReader.Get(owner, slots[i].Name, storage.Path, element));
            }
        }

        private bool IsUnity(Type type) => _unityObject != null && type == _unityObject
            || type.Namespace == "UnityEngine";

        private bool Traversable(Type type)
        {
            if (type == typeof(string) || type == typeof(object) || type.FullName.Contains("Comparer")) return false;
            if (_unityObject != null && _unityObject.IsAssignableFrom(type)) return false;
            if (type.IsArray || typeof(Delegate).IsAssignableFrom(type) || type.IsValueType
                || type.Assembly.GetName().Name == _gameAssembly || type == typeof(Random)
                || type.FullName.StartsWith("System.Random+", StringComparison.Ordinal)
                || type.FullName.StartsWith("System.Collections.Generic.", StringComparison.Ordinal)
                || type.FullName.StartsWith("System.Tuple`", StringComparison.Ordinal)) return true;

            // 第三方 mod 的类型（例如 BetterExperience.Game.TerminalItemAdapter）不在白名单里。
            // 它们通常只是包装器/UI 适配器，把引用还原回去即可；为了不让整次捕获失败，
            // 这类类型按“不透明对象”处理：保留对象身份，但不展开它的字段。
            string assembly = type.Assembly.GetName().Name ?? "";
            bool framework = assembly == "mscorlib" || assembly == "System" || assembly == "System.Core"
                || assembly == "netstandard" || assembly.StartsWith("UnityEngine", StringComparison.Ordinal)
                || assembly.StartsWith("Unity.", StringComparison.Ordinal);
            if (!framework)
            {
                Opaque(type);
                return false;
            }

            throw new InvalidOperationException("Uncovered mutable type: " + type.FullName);
        }

        private static readonly HashSet<string> _opaqueWarned = new HashSet<string>(StringComparer.Ordinal);

        private static void Opaque(Type type)
        {
            string name = type.FullName ?? type.Name;
            lock (_opaqueWarned)
            {
                if (!_opaqueWarned.Add(name)) return;
            }
            Plugin.LogSource?.LogWarning("[Snapshot] third-party type treated as opaque (identity kept, fields not restored): " + name);
        }

        private static bool Visual(Type type)
        {
            string name = type.FullName ?? type.Name;
            return name.Contains("TreeNodeRender") || name.Contains("UnityEngine.")
                && !name.StartsWith("UnityEngine.Vector", StringComparison.Ordinal)
                && !name.StartsWith("UnityEngine.Color", StringComparison.Ordinal)
                && !name.StartsWith("UnityEngine.Rect", StringComparison.Ordinal)
                && !name.StartsWith("UnityEngine.Quaternion", StringComparison.Ordinal);
        }

        private static void Flatten(Type type, List<FieldInfo> path, int index, string name,
            List<StateSlot> slots, List<Storage> stores, int depth)
        {
            if (depth > 16 || type.IsPointer || type.IsByRef || type == typeof(IntPtr) || type == typeof(UIntPtr))
                throw new InvalidOperationException("Unsupported managed snapshot field: " + name);
            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null && HasReferences(nullable))
                throw new InvalidOperationException("Nullable snapshot structs containing references are unsupported: " + name);
            if (!type.IsValueType || type.IsPrimitive || type.IsEnum || type == typeof(decimal)
                || nullable != null)
            {
                Add(type, path.ToArray(), index, name, slots, stores);
                return;
            }
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (Visual(field.FieldType)) continue;
                path.Add(field);
                Flatten(field.FieldType, path, index, name + "." + field.Name, slots, stores, depth + 1);
                path.RemoveAt(path.Count - 1);
            }
        }

        private static bool HasReferences(Type type)
            => !type.IsValueType || !type.IsPrimitive && !type.IsEnum
                && type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(field => HasReferences(field.FieldType));

        private static void Add(Type type, FieldInfo[] path, int index, string name,
            List<StateSlot> slots, List<Storage> stores)
        {
            slots.Add(new StateSlot(stores.Count, 8, !type.IsValueType, name));
            stores.Add(new Storage(index, path));
        }

        private object Read(IntPtr id, StateSlot slot)
        {
            var storage = _storage[id][slot.Offset];
            if (storage.Anchor) return storage.Target;
            object value = _retained[id];
            if (storage.Reader != null && storage.Reader.TryRead(value, storage.Index, out var fast)) return fast;
            if (storage.Index >= 0) value = ((Array)value).GetValue(storage.Index);
            foreach (var field in storage.Path) value = field.GetValue(value);
            return value;
        }

        private void Write(IntPtr id, StateSlot slot, object value)
        {
            var storage = _storage[id][slot.Offset];
            if (storage.Anchor)
            {
                if (!ReferenceEquals(storage.Target, value))
                    throw new InvalidOperationException("Immutable delegate target changed.");
                return;
            }
            object owner = _retained[id];
            object element = storage.Index < 0 ? owner : ((Array)owner).GetValue(storage.Index);
            object Set(object target, int at)
            {
                if (at == storage.Path.Length) return value;
                var field = storage.Path[at];
                object previous = field.GetValue(target);
                object restored = Set(previous, at + 1);
                if (!ReferenceEquals(previous, restored) || field.FieldType.IsValueType)
                    field.SetValue(target, restored);
                return target;
            }
            object result = Set(element, 0);
            if (storage.Index >= 0) ((Array)owner).SetValue(result, storage.Index);
        }

        public byte[] ReadValue(IntPtr id, StateSlot slot)
        {
            object value = Read(id, slot) ?? NullValue;
            object key = value is float single ? (object)(typeof(float), BitConverter.SingleToInt32Bits(single))
                : value is double number ? (object)(typeof(double), BitConverter.DoubleToInt64Bits(number)) : value;
            if (!_values.TryGetValue(key, out var saved))
            {
                long token = ++_nextValue;
                saved = BitConverter.GetBytes(token);
                _values.Add(key, saved);
                _savedValues.Add(token, value);
            }
            // StateGraph treats returned bytes as immutable; equal captured values share a token.
            return saved;
        }

        public bool ValueEquals(IntPtr id, StateSlot slot, byte[] captured)
        {
            object expected = _savedValues[BitConverter.ToInt64(captured, 0)];
            object actual = Read(id, slot) ?? NullValue;
            // Match capture's value interning, including distinct signed zero and NaN bit patterns.
            if (expected is float single)
                return actual is float otherSingle
                    && BitConverter.SingleToInt32Bits(single) == BitConverter.SingleToInt32Bits(otherSingle);
            if (expected is double number)
                return actual is double otherNumber
                    && BitConverter.DoubleToInt64Bits(number) == BitConverter.DoubleToInt64Bits(otherNumber);
            return Equals(expected, actual);
        }

        public IntPtr ReadReference(IntPtr id, StateSlot slot) => ManagedObjects.Id(Read(id, slot));
        public void WriteValue(IntPtr id, StateSlot slot, byte[] bytes)
        {
            var value = _savedValues[BitConverter.ToInt64(bytes, 0)];
            Write(id, slot, ReferenceEquals(value, NullValue) ? null : value);
        }
        public void WriteReference(IntPtr id, StateSlot slot, IntPtr value)
            => Write(id, slot, value == IntPtr.Zero ? null : _retained[value]);
        public void Validate()
        {
            foreach (var pair in _retained)
                if (!ReferenceEquals(pair.Value, ManagedObjects.Get(pair.Key)))
                    throw new InvalidOperationException("Managed snapshot identity changed.");
        }
        public void Dispose()
        {
            _retained.Clear(); _storage.Clear(); _roots.Clear(); _values.Clear(); _savedValues.Clear();
            _schemas.Clear(); _elementSchemas.Clear(); _traversable.Clear();
        }
    }
}
