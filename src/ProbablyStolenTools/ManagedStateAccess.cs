using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ProbablyStolenTools
{
    // Snapshot real managed fields and array elements; reflection preserves GC write barriers.
    internal sealed class ManagedStateAccess : IStateAccess
    {
        private sealed class Storage
        {
            internal int Index = -1;
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
            if (!root && !Traversable(type)) return slots;
            if (value is Array array)
            {
                if (array.Rank != 1 || array.GetLowerBound(0) != 0 || array.Length > 262144)
                    throw new InvalidOperationException("Unsupported snapshot array.");
                for (int i = 0; i < array.Length; i++)
                    Flatten(type.GetElementType(), new List<FieldInfo>(), i, type.FullName + "[" + i + "]", slots, stores, 0);
            }
            else if (value is Delegate action)
            {
                // Delegates are immutable; visit every invocation target without rewriting code/identity.
                foreach (var handler in action.GetInvocationList())
                    if (handler.Target != null)
                    {
                        slots.Add(new StateSlot(stores.Count, 8, true, type.FullName + ".target"));
                        stores.Add(new Storage { Anchor = true, Target = handler.Target });
                    }
            }
            else
            {
                for (var current = type; current != null && !IsUnity(current); current = current.BaseType)
                    foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (root && selected != null && !selected.Contains(field.Name)) continue;
                        // 这些是“当前正在转场/被锁”的计数器，不是业务状态：
                        // 快照期间若恰好非 0，恢复后会把锁一起带回去（例如闭店一直提示“请稍候”）。
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
