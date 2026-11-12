using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ProbablyStolenTools
{
    // Snapshot real managed fields and array elements; reflection preserves GC write barriers.
    internal sealed class ManagedStateAccess : IStateAccess, IIndependentStateAccess, IStateValueComparer, IStateReferenceComparer, IStateValueProbe, IBudgetedStateDescription, IStateBlockAccess, IStateObjectDescription, IStateOwnerRepairAccess, IStateCensusValueProbe, IStateScalarWriteDiagnostics, IStateComparisonDiagnostics, IStateReadPass
#if STATIC_SNAPSHOT_LAYOUTS
        , IStateStableStorageAccess
#endif
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

        // Only immutable field descriptions are shared; business values and ownership stay per capture.
        private sealed class TypeSchema
        {
            internal readonly StateSlot[] Slots;
            internal readonly Storage[] Stores;
            internal TypeSchema(List<StateSlot> slots, List<Storage> stores)
            { Slots = slots.ToArray(); Stores = stores.ToArray(); }
            internal TypeSchema(StateSlot[] slots, Storage[] stores)
            { Slots = slots; Stores = stores; }
        }

        private const int ArraySchemaLimit = 4096;
        private const int ArraySchemaSlotLimit = 65536;
        private const int ValueTokenBytes = sizeof(long);
#if STATIC_SNAPSHOT_LAYOUTS
        private const int SharedSchemaLimit = 4096;
        private const int SharedSchemaSlotLimit = 65536;
        private static readonly Dictionary<(Type Type, Type Unity, bool Fast, bool Element), TypeSchema> SharedTypes = new();
        private static readonly Dictionary<(Type Type, int Length, bool Fast), TypeSchema> SharedArrays = new();
        private static int _sharedTypeSlots, _sharedArraySlots;
        internal bool SharedDescriptions { get; set; } = true;
#endif
        private readonly Dictionary<IntPtr, object> _retained = new();
        private readonly Dictionary<IntPtr, HashSet<string>> _roots = new();
        private readonly Dictionary<IntPtr, Storage[]> _storage = new();
        private readonly Dictionary<Type, TypeSchema> _schemas = new();
        private readonly Dictionary<Type, TypeSchema> _elementSchemas = new();
        private readonly Dictionary<(Type Type, int Length), TypeSchema> _arraySchemas = new();
        private int _arraySchemaSlots;
        private int _arraySchemaHits;
        private long _arraySlotsReused;
        private readonly Dictionary<Type, bool> _traversable = new();
        private readonly Dictionary<object, byte[]> _values = new();
        private readonly Dictionary<long, object> _savedValues = new();
        private long _nextValue;
        private bool _disposed;
        private static readonly object NullValue = new();
#if STATIC_SNAPSHOT_LAYOUTS
        private int _readPassDepth;
        private Array _readArray;
        private int _readArrayIndex;
        private object _readArrayElement;
        private IntPtr _storageOwnerId;
        private object _storageOwner;
        private Storage[] _ownerStores;
        internal bool ScopedStructReads { get; set; } = true;
        internal long ArrayElementLoads { get; private set; }
        internal long ArrayElementReuses { get; private set; }
#endif

        void IStateReadPass.BeginReadPass()
        {
#if STATIC_SNAPSHOT_LAYOUTS
            _readPassDepth++;
#endif
            ResetReadObject();
        }
        void IStateReadPass.ResetReadObject() => ResetReadObject();
        void IStateReadPass.EndReadPass()
        {
            ResetReadObject();
#if STATIC_SNAPSHOT_LAYOUTS
            _readPassDepth--;
#endif
        }
        private void ResetReadObject()
        {
#if STATIC_SNAPSHOT_LAYOUTS
            _readArray = null;
            _readArrayElement = null;
#endif
        }
#if STATIC_SNAPSHOT_LAYOUTS
        private bool ReuseStructElement(Storage storage) => ScopedStructReads && _fastReads
            && _readPassDepth > 0 && storage.Index >= 0 && storage.Path.Length > 0 && storage.Reader != null;
        private object ReadStructElement(object owner, int index)
        {
            if (ReferenceEquals(_readArray, owner) && _readArrayIndex == index)
            {
                ArrayElementReuses++;
                return _readArrayElement;
            }
            var array = (Array)owner;
            var value = array.GetValue(index);
            ArrayElementLoads++;
            _readArray = array;
            _readArrayIndex = index;
            return _readArrayElement = value;
        }
        private bool MayRunValueCallback(Storage storage) => _readPassDepth > 0
            && (storage.Reader == null || !storage.Reader.SupportsComparison);
#else
        private bool MayRunValueCallback(Storage storage) => false;
#endif
        internal Func<object, IReadOnlyList<StateSlot>, IStateBlock> BlockCapture;
        internal SemanticFieldBlockFactory EntityBlocks { get; set; }
        internal Action<ManagedStateAccess> ConfigureIndependentBlocks;
        internal Func<object, IReadOnlyList<StateSlot>, bool> CensusValuesProbe;
        internal int CensusValueObjectCount { get; private set; }
        bool IStateCensusValueProbe.TryProbeValues(IntPtr id, IReadOnlyList<StateSlot> slots)
        {
            if (CensusValuesProbe == null || (_roots.TryGetValue(id, out var selected) && selected != null)) return false;
            bool probed = CensusValuesProbe(_retained[id], slots);
            if (probed) CensusValueObjectCount++;
            return probed;
        }
        internal string BlockMode = "legacy";
        internal int IntArrayBlockCount { get; private set; }
        internal int IntArraySlotCount { get; private set; }

        IStateBlock IStateBlockAccess.CaptureBlock(IntPtr id, IReadOnlyList<StateSlot> slots)
        {
            // Selected roots must retain exactly their requested surface.
            if (!_intArrayBlocks && BlockCapture == null) return null;
            if (_roots.TryGetValue(id, out var fields) && fields != null) return null;
            var target = _retained[id];
            if (_intArrayBlocks && target.GetType() == typeof(int[])
                && ((int[])target).Length != 0 && slots.Count == ((int[])target).Length)
            {
                IntArrayBlockCount++;
                IntArraySlotCount += slots.Count;
                return new SnapshotIntArrayBlock((int[])target, slots);
            }
            if (BlockCapture == null) return null;
            return BlockCapture(target, slots);
        }

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
        internal bool FastReads => _fastReads;
        private readonly bool _reuseArraySchemas;
        private readonly bool _directWrites;
        private readonly bool _intArrayBlocks;
        internal bool SkipUnchangedScalars { get; set; }
        internal bool TypedScalarComparisons { get; set; } = true;
        internal bool DirectReferenceComparisons { get; set; } = true;
        public long TypedComparisonCount { get; private set; }
        public long ReferenceComparisonCount { get; private set; }
        public (long Checked, long Skipped) ScalarWrites { get; private set; }
        internal string ArraySchemaSummary => $"arraySchemas(cached={_arraySchemas.Count}," +
            $"hits={_arraySchemaHits},slotsReused={_arraySlotsReused})";

        internal string WorkloadSummary()
        {
            var types = new Dictionary<Type, (int Objects, long Slots, long Capacity, int MaxArray)>();
            (int Objects, long Slots, long Capacity, int MaxArray) other = default;
            foreach (var pair in _retained)
            {
                var type = pair.Value.GetType();
                long slots = _storage.TryGetValue(pair.Key, out var stores) ? stores.Length : 0;
                int capacity = pair.Value is Array array ? array.Length : 0;
                if (!types.TryGetValue(type, out var counts) && types.Count >= 128)
                {
                    other = (other.Objects + 1, other.Slots + slots, other.Capacity + capacity,
                        Math.Max(other.MaxArray, capacity));
                    continue;
                }
                types[type] = (counts.Objects + 1, counts.Slots + slots, counts.Capacity + capacity,
                    Math.Max(counts.MaxArray, capacity));
            }
            var ordered = types.OrderByDescending(p => p.Value.Slots)
                .ThenBy(p => p.Key.FullName, StringComparer.Ordinal).ToArray();
            foreach (var pair in ordered.Skip(6))
                other = (other.Objects + pair.Value.Objects, other.Slots + pair.Value.Slots,
                    other.Capacity + pair.Value.Capacity, Math.Max(other.MaxArray, pair.Value.MaxArray));
            string Counts((int Objects, long Slots, long Capacity, int MaxArray) counts)
                => $"objects={counts.Objects},slots={counts.Slots},arrayCapacity={counts.Capacity},maxArray={counts.MaxArray}";
            string Name(Type type)
            {
                string name = type.Name;
                return name.Length <= 96 ? name : name.Substring(0, 93) + "...";
            }
            return "typesBySlots=[" + string.Join(";", ordered.Take(6)
                .Select(p => Name(p.Key) + "(" + Counts(p.Value) + ")")) + "] otherTypes(" + Counts(other) + ")";
        }

        internal ManagedStateAccess(Type unityObject = null, string gameAssembly = "Assembly-CSharp")
            : this(unityObject, gameAssembly, true) { }

        internal ManagedStateAccess(Type unityObject, string gameAssembly, bool fastReads)
            : this(unityObject, gameAssembly, fastReads, true) { }

        internal ManagedStateAccess(Type unityObject, string gameAssembly, bool fastReads, bool reuseArraySchemas)
            : this(unityObject, gameAssembly, fastReads, reuseArraySchemas, true) { }

        internal ManagedStateAccess(Type unityObject, string gameAssembly, bool fastReads, bool reuseArraySchemas,
            bool directWrites)
            : this(unityObject, gameAssembly, fastReads, reuseArraySchemas, directWrites, false) { }

        internal ManagedStateAccess(Type unityObject, string gameAssembly, bool fastReads, bool reuseArraySchemas,
            bool directWrites, bool intArrayBlocks)
        {
            _unityObject = unityObject;
            _gameAssembly = gameAssembly;
            _fastReads = fastReads;
            _reuseArraySchemas = reuseArraySchemas;
            _directWrites = directWrites;
            _intArrayBlocks = intArrayBlocks;
            SkipUnchangedScalars = directWrites;
        }

        internal void AddRoot(IntPtr id, params string[] fields)
        {
            if (id == IntPtr.Zero) throw new InvalidOperationException("Required snapshot root missing.");
            _roots[id] = fields.Length == 0 ? null : new HashSet<string>(fields);
        }

        IStateAccess IIndependentStateAccess.CreateIndependent()
        {
            var result = new ManagedStateAccess(_unityObject, _gameAssembly, _fastReads, _reuseArraySchemas,
                _directWrites, _intArrayBlocks)
            {
                SkipUnchangedScalars = SkipUnchangedScalars,
                TypedScalarComparisons = TypedScalarComparisons,
                DirectReferenceComparisons = DirectReferenceComparisons
            };
#if STATIC_SNAPSHOT_LAYOUTS
            result.ScopedStructReads = ScopedStructReads;
            result.SharedDescriptions = SharedDescriptions;
#endif
            try
            {
                foreach (var root in _roots)
                    result._roots.Add(root.Key, root.Value == null ? null : new HashSet<string>(root.Value));
                ConfigureIndependentBlocks?.Invoke(result);
                return result;
            }
            catch { result.Dispose(); throw; }
        }

        public void Retain(IntPtr id)
        {
            if (!_retained.ContainsKey(id)) _retained[id] = ManagedObjects.Get(id);
        }

        string IStateObjectDescription.ObjectTypeName(IntPtr id)
            => _retained.TryGetValue(id, out var target) ? target.GetType().FullName : null;

        public IReadOnlyList<StateSlot> Describe(IntPtr id)
            => DescribeWithinBudget(id, int.MaxValue, long.MaxValue);

        IReadOnlyList<StateSlot> IBudgetedStateDescription.Describe(IntPtr id, int remainingSlots, long remainingBytes)
            => DescribeWithinBudget(id, remainingSlots, remainingBytes);

        private IReadOnlyList<StateSlot> DescribeWithinBudget(IntPtr id, int remainingSlots, long remainingBytes)
        {
#if STATIC_SNAPSHOT_LAYOUTS
            ResetStorageOwner();
#endif
            var value = _retained[id];
            var type = value.GetType();
            bool root = _roots.TryGetValue(id, out var selected);
            if (root && ProtectedOwner(type))
                throw new InvalidOperationException("Protected snapshot root: " + type.FullName);
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
                    _elementSchemas.Add(elementType, element = ElementSchema(elementType));
                bool vector = type == elementType.MakeArrayType();
                int stride = element.Stores.Length;
                long requiredSlots = (long)array.Length * stride;
                long elementBytes = 0;
                foreach (var slot in element.Slots)
                    elementBytes += slot.IsReference ? IntPtr.Size : ValueTokenBytes;
                long bytes = array.Length * elementBytes;
                // Reject impossible arrays before allocating per-element descriptors and names.
                if (requiredSlots > remainingSlots)
                    throw new SnapshotDescriptionLimitException("slot", requiredSlots, bytes);
                if (bytes > remainingBytes)
                    throw new SnapshotDescriptionLimitException("byte", requiredSlots, bytes);
                int count = (int)requiredSlots;
                var key = (type, array.Length);
                if (_reuseArraySchemas && _arraySchemas.TryGetValue(key, out var cached))
                {
                    _storage[id] = cached.Stores;
                    _arraySchemaHits++;
                    _arraySlotsReused += count;
                    return cached.Slots;
                }
#if STATIC_SNAPSHOT_LAYOUTS
                if (_reuseArraySchemas && SharedDescriptions)
                    lock (SharedArrays)
                        if (SharedArrays.TryGetValue((type, array.Length, _fastReads), out cached))
                        {
                            _storage[id] = cached.Stores;
                            _arraySchemaHits++;
                            _arraySlotsReused += count;
                            CacheArraySchema(key, cached);
                            return cached.Slots;
                        }
#endif
                var slots = new StateSlot[count];
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
                // Only immutable offsets/paths/readers are shared, never an array or its values.
                var arraySchema = new TypeSchema(slots, stores);
                CacheArraySchema(key, arraySchema);
#if STATIC_SNAPSHOT_LAYOUTS
                if (_reuseArraySchemas && SharedDescriptions)
                    lock (SharedArrays)
                        if (SharedArrays.Count < SharedSchemaLimit && !SharedArrays.ContainsKey((type, array.Length, _fastReads))
                            && count <= SharedSchemaSlotLimit - _sharedArraySlots)
                        {
                            SharedArrays.Add((type, array.Length, _fastReads), arraySchema);
                            _sharedArraySlots += count;
                        }
#endif
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

        private void CacheArraySchema((Type Type, int Length) key, TypeSchema schema)
        {
            if (_reuseArraySchemas && _arraySchemas.Count < ArraySchemaLimit
                && schema.Slots.Length <= ArraySchemaSlotLimit - _arraySchemaSlots)
            {
                _arraySchemas.Add(key, schema);
                _arraySchemaSlots += schema.Slots.Length;
            }
        }

        private TypeSchema ElementSchema(Type type)
        {
#if STATIC_SNAPSHOT_LAYOUTS
            var key = (type, (Type)null, _fastReads, true);
            if (SharedDescriptions)
                lock (SharedTypes)
                    if (SharedTypes.TryGetValue(key, out var cached)) return cached;
#endif
            var slots = new List<StateSlot>();
            var stores = new List<Storage>();
            Flatten(type, new List<FieldInfo>(), -1, "", slots, stores, 0);
            PrepareReaders(type.MakeArrayType(), slots, stores, true);
            var result = new TypeSchema(slots, stores);
#if STATIC_SNAPSHOT_LAYOUTS
            if (SharedDescriptions) ShareType(key, result);
#endif
            return result;
        }

#if STATIC_SNAPSHOT_LAYOUTS
        private static void ShareType((Type Type, Type Unity, bool Fast, bool Element) key, TypeSchema schema)
        {
            lock (SharedTypes)
                if (SharedTypes.Count < SharedSchemaLimit && !SharedTypes.ContainsKey(key)
                    && schema.Slots.Length <= SharedSchemaSlotLimit - _sharedTypeSlots)
                {
                    SharedTypes.Add(key, schema);
                    _sharedTypeSlots += schema.Slots.Length;
                }
        }
#endif

        private TypeSchema ObjectSchema(Type type, HashSet<string> selected)
        {
#if STATIC_SNAPSHOT_LAYOUTS
            var key = (type, _unityObject, _fastReads, false);
            if (selected == null && SharedDescriptions)
                lock (SharedTypes)
                    if (SharedTypes.TryGetValue(key, out var cached)) return cached;
#endif
            var slots = new List<StateSlot>();
            var stores = new List<Storage>();
            for (var current = type; current != null && !IsUnity(current); current = current.BaseType)
                foreach (var field in DeclaredFields(current))
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
            var result = new TypeSchema(slots, stores);
#if STATIC_SNAPSHOT_LAYOUTS
            if (selected == null && SharedDescriptions) ShareType(key, result);
#endif
            return result;
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
            if (ProtectedOwner(type)) return false;
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

        private bool ProtectedOwner(Type type)
        {
            if (type.Assembly.GetName().Name != _gameAssembly) return false;
            while (type.DeclaringType != null) type = type.DeclaringType;
            return SnapshotTraversalBoundary.IsProtectedOwner(type.Name);
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
            foreach (var field in DeclaredFields(type))
            {
                if (Visual(field.FieldType)) continue;
                path.Add(field);
                Flatten(field.FieldType, path, index, name + "." + field.Name, slots, stores, depth + 1);
                path.RemoveAt(path.Count - 1);
            }
        }

        private static bool HasReferences(Type type)
            => !type.IsValueType || !type.IsPrimitive && !type.IsEnum
                && DeclaredFields(type)
                    .Any(field => HasReferences(field.FieldType));

        private static FieldInfo[] DeclaredFields(Type type)
        {
#if STATIC_SNAPSHOT_LAYOUTS
            return SnapshotStaticLayouts.Fields(type);
#else
            return type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
#endif
        }

        private static void Add(Type type, FieldInfo[] path, int index, string name,
            List<StateSlot> slots, List<Storage> stores)
        {
            slots.Add(new StateSlot(stores.Count, ValueTokenBytes, !type.IsValueType, name));
            stores.Add(new Storage(index, path));
        }

        private object Read(IntPtr id, StateSlot slot)
        {
            var stores = StoresFor(id, out var owner);
            return ReadStorage(owner, stores[slot.Offset]);
        }

        private Storage[] StoresFor(IntPtr id, out object owner)
        {
#if STATIC_SNAPSHOT_LAYOUTS
            if (_storageOwnerId == id && _ownerStores != null)
            {
                owner = _storageOwner;
                return _ownerStores;
            }
#endif
            var stores = _storage[id];
            owner = _retained[id];
#if STATIC_SNAPSHOT_LAYOUTS
            _storageOwnerId = id;
            _storageOwner = owner;
            _ownerStores = stores;
#endif
            return stores;
        }
#if STATIC_SNAPSHOT_LAYOUTS
        private void ResetStorageOwner()
        {
            _storageOwnerId = IntPtr.Zero;
            _storageOwner = null;
            _ownerStores = null;
        }
#endif

        private object ReadStorage(object value, Storage storage)
        {
            if (storage.Anchor) return storage.Target;
#if STATIC_SNAPSHOT_LAYOUTS
            if (ReuseStructElement(storage)
                && storage.Reader.TryReadFields(ReadStructElement(value, storage.Index), out var cached)) return cached;
#endif
            if (storage.Reader != null && storage.Reader.TryRead(value, storage.Index, out var fast)) return fast;
            if (storage.Index >= 0) value = ((Array)value).GetValue(storage.Index);
            foreach (var field in storage.Path) value = field.GetValue(value);
            return value;
        }

        private void Write(IntPtr id, StateSlot slot, object value)
        {
            ResetReadObject();
            var storage = StoresFor(id, out var owner)[slot.Offset];
            if (storage.Anchor)
            {
                if (!ReferenceEquals(storage.Target, value))
                    throw new InvalidOperationException("Immutable delegate target changed.");
                return;
            }
            if (_directWrites)
            {
                // Scalar/reference array elements have no nested fields to read or copy back.
                if (storage.Index >= 0 && storage.Path.Length == 0)
                {
                    ((Array)owner).SetValue(value, storage.Index);
                    return;
                }
                if (storage.Index < 0 && storage.Path.Length == 1)
                {
                    var field = storage.Path[0];
                    // Keep the old reference-equality guard (including unchanged readonly fields).
                    if (field.FieldType.IsValueType || !ReferenceEquals(field.GetValue(owner), value))
                        field.SetValue(owner, value);
                    return;
                }
            }
            WriteNested(owner, storage, value, _directWrites);
        }

        private static void WriteNested(object owner, Storage storage, object value, bool directWrites)
        {
            object element = storage.Index < 0 ? owner : ((Array)owner).GetValue(storage.Index);
            object Set(object target, int at)
            {
                if (at == storage.Path.Length) return value;
                var field = storage.Path[at];
                // The old recursion unconditionally writes scalar leaves. No old leaf value is needed.
                if (directWrites && at == storage.Path.Length - 1 && field.FieldType.IsValueType)
                {
                    field.SetValue(target, value);
                    return target;
                }
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
            var storage = StoresFor(id, out var owner)[slot.Offset];
            if (MayRunValueCallback(storage))
            {
                ResetReadObject();
                try { return InternValue(ReadStorage(owner, storage)); }
                finally { ResetReadObject(); }
            }
            return InternValue(ReadStorage(owner, storage));
        }

        private byte[] InternValue(object value)
        {
            value ??= NullValue;
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
            return ValueEquals(id, slot, expected);
        }

        private bool ValueEquals(IntPtr id, StateSlot slot, object expected)
        {
            var storage = StoresFor(id, out var owner)[slot.Offset];
            if (TypedScalarComparisons)
            {
                bool same;
                object comparable = ReferenceEquals(expected, NullValue) ? null : expected;
                bool compared = false;
                if (!storage.Anchor && storage.Reader != null)
                {
#if STATIC_SNAPSHOT_LAYOUTS
                    if (ReuseStructElement(storage))
                        compared = storage.Reader.TryEqualsFields(ReadStructElement(owner, storage.Index), comparable, out same);
                    else
#endif
                        compared = storage.Reader.TryEquals(owner, storage.Index, comparable, out same);
                }
                else same = false;
                if (compared)
                {
                    TypedComparisonCount++;
                    return same;
                }
            }
            if (MayRunValueCallback(storage))
            {
                ResetReadObject();
                try { return ScalarEquals(expected, ReadStorage(owner, storage) ?? NullValue); }
                finally { ResetReadObject(); }
            }
            return ScalarEquals(expected, ReadStorage(owner, storage) ?? NullValue);
        }

        private static bool ScalarEquals(object expected, object actual)
        {
            // Match capture's value interning, including distinct signed zero and NaN bit patterns.
            if (expected is float single)
                return actual is float otherSingle
                    && BitConverter.SingleToInt32Bits(single) == BitConverter.SingleToInt32Bits(otherSingle);
            if (expected is double number)
                return actual is double otherNumber
                    && BitConverter.DoubleToInt64Bits(number) == BitConverter.DoubleToInt64Bits(otherNumber);
            return Equals(expected, actual);
        }

        public int ProbeValue(IntPtr id, StateSlot slot)
        {
            Read(id, slot);
            return ValueTokenBytes; // Same logical token width as ReadValue, without retaining a value/token.
        }

        public IntPtr ReadReference(IntPtr id, StateSlot slot) => ManagedObjects.Id(Read(id, slot));
        public bool ReferenceMatches(IntPtr id, StateSlot slot, IntPtr captured)
        {
            if (!DirectReferenceComparisons) return ReadReference(id, slot) == captured;
            object actual = Read(id, slot);
            // A completed graph retains every captured non-null target, including opaque objects.
            object expected = captured == IntPtr.Zero ? null : _retained[captured];
            ReferenceComparisonCount++;
            return ReferenceEquals(actual, expected);
        }
        public void WriteValue(IntPtr id, StateSlot slot, byte[] bytes)
        {
            ResetReadObject();
            var value = _savedValues[BitConverter.ToInt64(bytes, 0)];
            // Only framework scalars: never introduce custom struct Equals callbacks during writes.
            if (SkipUnchangedScalars && (ReferenceEquals(value, NullValue)
                || value.GetType().IsPrimitive || value.GetType().IsEnum || value is decimal))
            {
                ScalarWrites = (ScalarWrites.Checked + 1, ScalarWrites.Skipped);
                if (ValueEquals(id, slot, value))
                {
                    ScalarWrites = (ScalarWrites.Checked, ScalarWrites.Skipped + 1);
                    return;
                }
            }
            Write(id, slot, ReferenceEquals(value, NullValue) ? null : value);
        }
        public void WriteReference(IntPtr id, StateSlot slot, IntPtr value)
            => Write(id, slot, value == IntPtr.Zero ? null : _retained[value]);
        public void Validate()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(ManagedStateAccess));
            foreach (var pair in _retained)
                if (!ReferenceEquals(pair.Value, ManagedObjects.Get(pair.Key)))
                    throw new InvalidOperationException("Managed snapshot identity changed.");
        }
        public void Dispose()
        {
            _disposed = true;
            ResetReadObject();
#if STATIC_SNAPSHOT_LAYOUTS
            ResetStorageOwner();
#endif
            _retained.Clear(); _storage.Clear(); _roots.Clear(); _values.Clear(); _savedValues.Clear();
            _schemas.Clear(); _elementSchemas.Clear(); _traversable.Clear();
            _arraySchemas.Clear();
            _arraySchemaSlots = 0; _arraySchemaHits = 0; _arraySlotsReused = 0;
            BlockCapture = null;
            ConfigureIndependentBlocks = null;
            CensusValuesProbe = null;
            CensusValueObjectCount = 0;
            IntArrayBlockCount = 0;
            IntArraySlotCount = 0;
            ScalarWrites = default;
            TypedComparisonCount = 0;
            ReferenceComparisonCount = 0;
        }
    }
}
