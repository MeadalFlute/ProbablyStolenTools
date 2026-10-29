using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

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
            internal bool ModContext;
        }

        private readonly Dictionary<IntPtr, object> _retained = new();
        private readonly Dictionary<IntPtr, HashSet<string>> _roots = new();
        private readonly Dictionary<IntPtr, List<Storage>> _storage = new();
        private readonly Dictionary<object, long> _values = new();
        private readonly Dictionary<long, object> _savedValues = new();
        private readonly HashSet<IntPtr> _modContext = new();
        private readonly Dictionary<Type, bool> _boundaries = new();
        private readonly Dictionary<Type, bool> _foreignTypes = new();
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
        internal SnapshotCoverage Coverage { get; } = new();

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
            bool foreign = Foreign(type);
            bool modContext = foreign || _modContext.Contains(id);
            if (!root && !Traversable(type, modContext)) return slots;
            if (foreign) Coverage.Visit(type);
            if (value is Array array)
            {
                if (array.Rank != 1 || array.GetLowerBound(0) != 0 || array.Length > 262144)
                {
                    if (modContext)
                    {
                        Coverage.Skip(type.FullName, "unsupported mod array shape");
                        return slots;
                    }
                    throw new InvalidOperationException("Unsupported snapshot array.");
                }
                for (int i = 0; i < array.Length; i++)
                    Flatten(type.GetElementType(), new List<FieldInfo>(), i, type.FullName + "[" + i + "]",
                        slots, stores, 0, modContext);
            }
            else if (value is Delegate action)
            {
                // Delegates are immutable; visit every invocation target without rewriting code/identity.
                foreach (var handler in action.GetInvocationList())
                    if (handler.Target != null)
                    {
                        if (Boundary(handler.Target.GetType()))
                        {
                            Coverage.Skip(handler.Target.GetType().FullName, "runtime delegate target");
                            continue;
                        }
                        slots.Add(new StateSlot(stores.Count, 8, true, type.FullName + ".target"));
                        stores.Add(new Storage { Anchor = true, Target = handler.Target, ModContext = modContext });
                    }
            }
            else
            {
                for (var current = type; current != null && !IsUnity(current); current = current.BaseType)
                    foreach (var field in Fields(current, Foreign(current), declaredOnly: true))
                    {
                        if (root && selected != null && !selected.Contains(field.Name)) continue;
                        // 这些是“当前正在转场/被锁”的计数器，不是业务状态：
                        // 快照期间若恰好非 0，恢复后会把锁一起带回去（例如闭店一直提示“请稍候”）。
                        if (TransientLocks.Contains(current.FullName + "." + field.Name)) continue;
                        if (Visual(field.FieldType))
                        {
                            if (foreign) Coverage.Skip(current.FullName + "." + field.Name, "visual field");
                            continue;
                        }
                        Flatten(field.FieldType, new List<FieldInfo> { field }, -1,
                            current.FullName + "." + field.Name, slots, stores, 0,
                            Foreign(current) || modContext && current.Assembly.GetName().Name != _gameAssembly);
                    }
                if (root && selected != null)
                    foreach (string name in selected)
                        if (!slots.Any(s => s.Name.EndsWith("." + name, StringComparison.Ordinal)
                            || s.Name.Contains("." + name + ".", StringComparison.Ordinal)))
                            throw new InvalidOperationException("Required snapshot field missing: " + name);
            }
            // Declared object/interface fields can hide tasks or handles. Inspect real storage only.
            int kept = 0;
            for (int i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var storage = stores[i];
                object referenced = slot.IsReference && !storage.Anchor ? Read(id, slot) : null;
                if (referenced != null && Boundary(referenced.GetType()))
                {
                    Coverage.Skip(slot.Name, "runtime reference " + referenced.GetType().FullName);
                    continue;
                }
                slots[kept] = slot with { Offset = kept };
                stores[kept++] = storage;
            }
            slots.RemoveRange(kept, slots.Count - kept);
            stores.RemoveRange(kept, stores.Count - kept);
            return slots;
        }

        private bool IsUnity(Type type) => _unityObject != null && type == _unityObject
            || type.Namespace == "UnityEngine";

        private bool Traversable(Type type, bool modContext)
        {
            if (type == typeof(string) || type == typeof(object)) return false;
            if (Boundary(type))
            {
                Coverage.Skip(type.FullName, "runtime object");
                return false;
            }
            if (Comparer(type))
            {
                if (Foreign(type)) Coverage.Skip(type.FullName, "comparer identity only");
                return false;
            }
            if (type.IsArray || typeof(Delegate).IsAssignableFrom(type) || type.IsValueType
                || type.Assembly.GetName().Name == _gameAssembly || type == typeof(Random)
                || type.FullName.StartsWith("System.Random+", StringComparison.Ordinal)
                || type.FullName.StartsWith("System.Collections.Generic.", StringComparison.Ordinal)
                || type.FullName.StartsWith("System.Tuple`", StringComparison.Ordinal)) return true;

            if (Foreign(type)) return true;
            if (modContext)
            {
                Coverage.Skip(type.FullName, "unsupported framework object reached through mod data");
                return false;
            }

            throw new InvalidOperationException("Uncovered mutable type: " + type.FullName);
        }

        private bool Foreign(Type type)
        {
            if (type.IsArray) return Foreign(type.GetElementType());
            if (_foreignTypes.TryGetValue(type, out bool foreign)) return foreign;
            string assembly = type.Assembly.GetName().Name ?? "";
            return _foreignTypes[type] = assembly != _gameAssembly && assembly != "mscorlib" && assembly != "netstandard"
                && assembly != "System" && !assembly.StartsWith("System.", StringComparison.Ordinal)
                && !assembly.StartsWith("Microsoft.", StringComparison.Ordinal)
                && !assembly.StartsWith("Unity", StringComparison.Ordinal);
        }

        private static bool Comparer(Type type)
            => typeof(IComparer).IsAssignableFrom(type) || typeof(IEqualityComparer).IsAssignableFrom(type)
                || type.GetInterfaces().Any(i => i.IsGenericType
                    && (i.GetGenericTypeDefinition() == typeof(IComparer<>)
                        || i.GetGenericTypeDefinition() == typeof(IEqualityComparer<>)));

        private bool Boundary(Type type)
        {
            if (_boundaries.TryGetValue(type, out bool boundary)) return boundary;
            return _boundaries[type] = IsBoundary(type);
        }

        private bool IsBoundary(Type type)
        {
            if (type == typeof(string) || type == typeof(object)) return false;
            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null) return Boundary(nullable);
            if (type.IsPointer || type.IsByRef || type.IsByRefLike
                || type == typeof(IntPtr) || type == typeof(UIntPtr)) return true;
            if (_unityObject != null && _unityObject.IsAssignableFrom(type)) return true;
            if (typeof(MemberInfo).IsAssignableFrom(type) || typeof(Assembly).IsAssignableFrom(type)
                || typeof(Module).IsAssignableFrom(type) || typeof(Task).IsAssignableFrom(type)
                || typeof(Thread).IsAssignableFrom(type) || typeof(WaitHandle).IsAssignableFrom(type)
                || typeof(SynchronizationContext).IsAssignableFrom(type)
                || typeof(CancellationTokenSource).IsAssignableFrom(type)
                || type == typeof(CancellationToken) || type == typeof(CancellationTokenRegistration)
                || type == typeof(RuntimeTypeHandle) || type == typeof(RuntimeMethodHandle)
                || type == typeof(RuntimeFieldHandle)
                || typeof(SafeHandle).IsAssignableFrom(type) || typeof(Stream).IsAssignableFrom(type)
                || typeof(MarshalByRefObject).IsAssignableFrom(type) || typeof(Exception).IsAssignableFrom(type)
                || typeof(IAsyncStateMachine).IsAssignableFrom(type)
                || typeof(WeakReference).IsAssignableFrom(type)) return true;
            if (typeof(IDisposable).IsAssignableFrom(type) && !typeof(Delegate).IsAssignableFrom(type))
                return true;
            string name = type.FullName ?? "";
            if (name.StartsWith("System.Collections.Concurrent.", StringComparison.Ordinal)
                || name.StartsWith("System.Runtime.CompilerServices.ConditionalWeakTable`", StringComparison.Ordinal)
                || name.StartsWith("System.WeakReference`", StringComparison.Ordinal)
                || name.StartsWith("System.Threading.", StringComparison.Ordinal)
                || name.StartsWith("System.Runtime.InteropServices.", StringComparison.Ordinal)
                || name.StartsWith("System.Diagnostics.", StringComparison.Ordinal)
                || name.StartsWith("System.Net.", StringComparison.Ordinal)) return true;
            if (typeof(IEnumerator).IsAssignableFrom(type)) return true;
            if (type.IsGenericType && type.Namespace == "System.Collections.Generic"
                && type.GetGenericArguments().Any(Boundary)) return true;
            return false;
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

        private FieldInfo[] Fields(Type type, bool foreign, bool declaredOnly = false)
        {
            try
            {
                return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                    | (declaredOnly ? BindingFlags.DeclaredOnly : 0));
            }
            catch (Exception e) when (foreign && (e is TypeLoadException || e is NotSupportedException
                || e is FileNotFoundException || e is FileLoadException))
            {
                Coverage.Skip(type.FullName, "unavailable mod field metadata: " + e.GetType().Name);
                return Array.Empty<FieldInfo>();
            }
        }

        private void Flatten(Type type, List<FieldInfo> path, int index, string name,
            List<StateSlot> slots, List<Storage> stores, int depth, bool foreign)
        {
            foreign |= Foreign(type);
            if (Visual(type) || Boundary(type))
            {
                // Keep native pointer failures strict; new mod fields degrade independently.
                if (!foreign && (type.IsPointer || type == typeof(IntPtr) || type == typeof(UIntPtr)))
                    throw new InvalidOperationException("Unsupported managed snapshot field: " + name);
                Coverage.Skip(name, "visual/runtime field " + type.FullName);
                return;
            }
            if (depth > 16 || type.IsPointer || type.IsByRef || type == typeof(IntPtr) || type == typeof(UIntPtr))
            {
                if (foreign) { Coverage.Skip(name, "unsupported mod field shape"); return; }
                throw new InvalidOperationException("Unsupported managed snapshot field: " + name);
            }
            var nullable = Nullable.GetUnderlyingType(type);
            if (nullable != null && (Foreign(nullable) || HasReferences(nullable)))
            {
                if (foreign || Foreign(nullable))
                {
                    Coverage.Skip(name, "unsupported nullable mod struct");
                    return;
                }
                throw new InvalidOperationException("Nullable snapshot structs containing references are unsupported: " + name);
            }
            if (!type.IsValueType || type.IsPrimitive || type.IsEnum || type == typeof(decimal)
                || nullable != null)
            {
                Add(type, path.ToArray(), index, name, slots, stores, foreign);
                return;
            }
            foreach (var field in Fields(type, foreign))
            {
                path.Add(field);
                Flatten(field.FieldType, path, index, name + "." + field.Name, slots, stores, depth + 1, foreign);
                path.RemoveAt(path.Count - 1);
            }
        }

        private static bool HasReferences(Type type)
            => !type.IsValueType || !type.IsPrimitive && !type.IsEnum
                && type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(field => HasReferences(field.FieldType));

        private static void Add(Type type, FieldInfo[] path, int index, string name,
            List<StateSlot> slots, List<Storage> stores, bool modContext)
        {
            slots.Add(new StateSlot(stores.Count, 8, !type.IsValueType, name));
            stores.Add(new Storage { Index = index, Path = path, ModContext = modContext });
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

        public IntPtr ReadReference(IntPtr id, StateSlot slot)
        {
            var reference = ManagedObjects.Id(Read(id, slot));
            if (reference != IntPtr.Zero && _storage[id][slot.Offset].ModContext)
                _modContext.Add(reference);
            return reference;
        }
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
            _modContext.Clear(); _boundaries.Clear(); _foreignTypes.Clear();
        }
    }
}
