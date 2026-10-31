using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ProbablyStolenTools
{
    // Snapshot real managed fields and array elements; reflection preserves GC write barriers.
    internal sealed class ManagedStateAccess : IStateAccess
    {
        // 每个槽位一份；快照里有几十万条，必须是结构体，否则光是分配就够卡一下。
        private struct Storage
        {
            internal int Index;
            internal FieldInfo[] Path;
            internal bool Anchor;
            internal object Target;
        }

        private readonly Dictionary<IntPtr, object> _retained = new();
        private readonly Dictionary<IntPtr, HashSet<string>> _roots = new();
        private readonly Dictionary<IntPtr, List<Storage>> _storage = new();
        private readonly Dictionary<object, long> _values = new();
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
        private readonly Type _unityObject;
        private readonly string _gameAssembly;

        internal ManagedStateAccess(Type unityObject = null, string gameAssembly = "Assembly-CSharp")
        {
            _unityObject = unityObject;
            _gameAssembly = gameAssembly;
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
            var slots = new List<StateSlot>();
            var stores = new List<Storage>();
            _storage[id] = stores;
            bool root = _roots.TryGetValue(id, out var selected);
            if (!root)
            {
                if (!_traversable.TryGetValue(type, out var traversable))
                {
                    traversable = Traversable(type);
                    _traversable[type] = traversable;
                }
                if (!traversable) return slots;
            }
            if (value is Array array)
            {
                if (array.Rank != 1 || array.GetLowerBound(0) != 0 || array.Length > 262144)
                    throw new InvalidOperationException("Unsupported snapshot array.");
                // 字典/列表内部都是大数组，逐元素重跑 Flatten 会把反射开销放大几十万倍；
                // 这里按元素类型缓存一次展开结果，再按元素下标复制。
                var elementType = type.GetElementType();
                var elementSchema = elementType == null ? null : ElementSchema(elementType);
                if (elementSchema == null)
                    for (int i = 0; i < array.Length; i++)
                        Flatten(type.GetElementType(), new List<FieldInfo>(), i, type.FullName + "[" + i + "]", slots, stores, 0);
                else
                {
                    int baseOffset = stores.Count;
                    for (int i = 0; i < array.Length; i++)
                    {
                        foreach (var slot in elementSchema.Slots)
                            slots.Add(new StateSlot(baseOffset + slot.Offset, slot.Size, slot.IsReference, slot.Name));
                        foreach (var store in elementSchema.Stores)
                            stores.Add(new Storage { Index = i, Path = store.Path });
                    }
                }
            }
            else if (value is Delegate action)
            {
                // Delegates are immutable; visit every invocation target without rewriting code/identity.
                foreach (var handler in action.GetInvocationList())
                    if (handler.Target != null)
                    {
                        slots.Add(new StateSlot(stores.Count, 8, true, type.FullName + ".target"));
                        stores.Add(new Storage { Index = -1, Anchor = true, Target = handler.Target });
                    }
            }
            else if (!root)
            {
                // 普通对象：直接用按类型缓存的展开结果，避免每个对象重跑反射。
                var schema = SchemaFor(type);
                slots.AddRange(schema.Slots);
                stores.AddRange(schema.Stores);
            }
            else
            {
                for (var current = type; current != null && !IsUnity(current); current = current.BaseType)
                    foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (selected != null && !selected.Contains(field.Name)) continue;
                        if (TransientLocks.Contains(current.FullName + "." + field.Name)) continue;
                        if (Visual(field.FieldType)) continue;
                        Flatten(field.FieldType, new List<FieldInfo> { field }, -1,
                            current.FullName + "." + field.Name, slots, stores, 0);
                    }
                if (root && selected != null)
                    foreach (string name in selected)
                        if (!slots.Any(s => s.Name.EndsWith("." + name, StringComparison.Ordinal)
                            || s.Name.Contains("." + name + ".", StringComparison.Ordinal)))
                            throw new InvalidOperationException("Required snapshot field missing: " + name);
            }
            return slots;
        }

        // 按类型缓存“字段 → 槽位”的展开结果。每个快照要处理四万多个对象，
        // 之前每个对象都重跑一遍反射 GetFields + 逐个分配 Storage，占了 800ms 里的大头。
        private sealed class TypeSchema
        {
            internal StateSlot[] Slots;
            internal Storage[] Stores;
        }

        private readonly Dictionary<Type, TypeSchema> _schemas = new Dictionary<Type, TypeSchema>();
        private readonly Dictionary<Type, bool> _traversable = new Dictionary<Type, bool>();

        private readonly Dictionary<Type, TypeSchema> _elementSchemas = new Dictionary<Type, TypeSchema>();

        private TypeSchema ElementSchema(Type element)
        {
            if (_elementSchemas.TryGetValue(element, out var cached)) return cached;
            var slots = new List<StateSlot>();
            var stores = new List<Storage>();
            Flatten(element, new List<FieldInfo>(), -1, element.FullName, slots, stores, 0);
            var schema = new TypeSchema { Slots = slots.ToArray(), Stores = stores.ToArray() };
            _elementSchemas[element] = schema;
            return schema;
        }
        private TypeSchema SchemaFor(Type type)
        {
            if (_schemas.TryGetValue(type, out var cached)) return cached;
            var slots = new List<StateSlot>();
            var stores = new List<Storage>();
            var path = new List<FieldInfo>();
            for (var current = type; current != null && !IsUnity(current); current = current.BaseType)
                foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (TransientLocks.Contains(current.FullName + "." + field.Name)) continue;
                    if (Visual(field.FieldType)) continue;
                    path.Add(field);
                    Flatten(field.FieldType, path, -1, current.FullName + "." + field.Name, slots, stores, 0);
                    path.RemoveAt(path.Count - 1);
                }
            var schema = new TypeSchema { Slots = slots.ToArray(), Stores = stores.ToArray() };
            _schemas[type] = schema;
            return schema;
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
            stores.Add(new Storage { Index = index, Path = path });
        }

        private object Read(IntPtr id, StateSlot slot)
        {
            var storage = _storage[id][slot.Offset];
            if (storage.Anchor) return storage.Target;
            object value = _retained[id];
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
            if (!_values.TryGetValue(key, out long token))
            {
                token = ++_nextValue;
                _values.Add(key, token);
                _savedValues.Add(token, value);
            }
            return BitConverter.GetBytes(token);
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
        }
    }
}
