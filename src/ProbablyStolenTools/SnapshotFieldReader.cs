using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Security;
using System.Diagnostics;
using System.Threading;

#if STATIC_SNAPSHOT_LAYOUTS
namespace ProbablyStolenTools
{
    // Static field descriptors only; no runtime compilation, code loading or business getters.
    internal sealed class SnapshotFieldReader
    {
        private const int CacheLimit = 4096;
        private static readonly Dictionary<(Type Owner, string Name), SnapshotFieldReader> Cache = new();
        private readonly FieldInfo[] _path;
        private readonly bool _element;
        private readonly Func<object, int, object> _readElement;
        private readonly bool _canCompare;
        private SnapshotFieldReader(Type owner, FieldInfo[] path, bool element)
        {
            _path = path;
            _element = element;
            _readElement = element ? VectorReader(owner) : null;
            var scalar = path.Length == 0 ? (element ? owner.GetElementType() : owner) : path[path.Length - 1].FieldType;
            scalar = Nullable.GetUnderlyingType(scalar) ?? scalar;
            _canCompare = (scalar.IsPrimitive || scalar.IsEnum || scalar == typeof(decimal))
                && scalar != typeof(IntPtr) && scalar != typeof(UIntPtr);
        }
        private static object ReadVector<T>(object owner, int index) => ((T[])owner)[index];
        private static Func<object, int, object> VectorReader(Type owner)
        {
            // Only SZ vectors enter this path. Keep struct/enum and nonvector arrays reflective.
            var element = owner.GetElementType();
            if (element == null || owner != element.MakeArrayType()) return null;
            if (!element.IsValueType) return ReadVector<object>;
            if (owner == typeof(byte[])) return ReadVector<byte>;
            if (owner == typeof(bool[])) return ReadVector<bool>;
            if (owner == typeof(int[])) return ReadVector<int>;
            if (owner == typeof(float[])) return ReadVector<float>;
            if (owner == typeof(double[])) return ReadVector<double>;
            if (owner == typeof(long[])) return ReadVector<long>;
            if (owner == typeof(uint[])) return ReadVector<uint>;
            if (owner == typeof(ulong[])) return ReadVector<ulong>;
            if (owner == typeof(short[])) return ReadVector<short>;
            if (owner == typeof(ushort[])) return ReadVector<ushort>;
            if (owner == typeof(sbyte[])) return ReadVector<sbyte>;
            if (owner == typeof(char[])) return ReadVector<char>;
            if (owner == typeof(decimal[])) return ReadVector<decimal>;
            return null;
        }
        internal static SnapshotFieldReader Get(Type ownerType, string name, FieldInfo[] path, bool element)
        {
            lock (Cache)
            {
                var key = (ownerType, name);
                if (Cache.TryGetValue(key, out var cached)) return cached;
                if (Cache.Count >= CacheLimit) return null;
                var result = new SnapshotFieldReader(ownerType, path, element);
                Cache.Add(key, result);
                return result;
            }
        }
        internal bool TryRead(object owner, int index, out object value)
        {
            value = !_element ? owner : _readElement != null
                ? _readElement(owner, index) : ((Array)owner).GetValue(index);
            foreach (var field in _path) value = field.GetValue(value);
            return true;
        }
        internal bool TryEquals(object owner, int index, object expected, out bool same)
        {
            same = false;
            if (!_canCompare) return false;
            TryRead(owner, index, out var actual);
            if (actual is float single)
                same = expected is float other && BitConverter.SingleToInt32Bits(single) == BitConverter.SingleToInt32Bits(other);
            else if (actual is double number)
                same = expected is double other && BitConverter.DoubleToInt64Bits(number) == BitConverter.DoubleToInt64Bits(other);
            else same = Equals(actual, expected);
            return true;
        }
        internal bool Compiled => false;
        internal bool ComparerCompiled => false;
        internal static long CompileTicks => 0;
        internal static int CompileAttempts => 0;
        internal static long CompareCompileTicks => 0;
        internal static int CompareCompileAttempts => 0;
        internal static long CompileMilliseconds(long ticks) => 0;
        internal static void ClearCache() { lock (Cache) Cache.Clear(); }
        internal static string Summary => $"readers(mode=static-fields,cached={Cache.Count},compiled=0,compileAttemptsTotal=0,compileMsTotal=0)";
    }
}
#else
namespace ProbablyStolenTools
{
    // Cache code and field metadata only. Never bind a reader to a business object or snapshot.
    internal sealed class SnapshotFieldReader
    {
        private const int CacheLimit = 4096;
        private const int CompileAfterReads = 32;
        private static readonly Dictionary<(Type Owner, string Name), SnapshotFieldReader> Cache = new();
        private static long _compileTicks;
        private static int _compileAttempts;
        private static long _compareCompileTicks;
        private static int _compareCompileAttempts;
        internal static long CompileTicks => Interlocked.Read(ref _compileTicks);
        internal static int CompileAttempts => Volatile.Read(ref _compileAttempts);
        internal static long CompareCompileTicks => Interlocked.Read(ref _compareCompileTicks);
        internal static int CompareCompileAttempts => Volatile.Read(ref _compareCompileAttempts);
        internal static long CompileMilliseconds(long ticks) => (long)(ticks * (1000.0 / Stopwatch.Frequency));
        private readonly Type _ownerType;
        private readonly FieldInfo[] _path;
        private readonly bool _element;
        private Func<object, int, object> _read;
        private int _reads;
        private bool _attempted;
        private Func<object, int, object, bool> _equals;
        private int _comparisons;
        private bool _compareAttempted;
        internal bool Compiled => _read != null;
        internal bool ComparerCompiled => _equals != null;

        private SnapshotFieldReader(Type owner, FieldInfo[] path, bool element)
        { _ownerType = owner; _path = path; _element = element; }

        internal static SnapshotFieldReader Get(Type ownerType, string name, FieldInfo[] path, bool element)
        {
            lock (Cache)
            {
                var key = (ownerType, name);
                if (Cache.TryGetValue(key, out var cached)) return cached;
                if (Cache.Count >= CacheLimit) return null;
                var result = new SnapshotFieldReader(ownerType, path, element);
                Cache.Add(key, result);
                return result;
            }
        }

        internal bool TryRead(object owner, int index, out object value)
        {
            if (!_attempted && ++_reads >= CompileAfterReads) Compile();
            if (_read != null)
            {
                try { value = _read(owner, index); return true; }
                catch (Exception e) when (Unavailable(e)) { _read = null; }
            }
            value = null;
            return false;
        }

        internal bool TryEquals(object owner, int index, object expected, out bool same)
        {
            if (!_compareAttempted && ++_comparisons >= CompileAfterReads) CompileComparer();
            if (_equals != null)
            {
                try { same = _equals(owner, index, expected); return true; }
                catch (Exception e) when (Unavailable(e)) { _equals = null; }
            }
            same = false;
            return false;
        }

        private Expression StorageExpression(ParameterExpression owner, ParameterExpression index)
        {
            Expression value = Expression.Convert(owner, _ownerType);
            if (_element) value = Expression.ArrayIndex(value, index);
            foreach (var field in _path) value = Expression.Field(value, field);
            return value;
        }

        private void CompileComparer()
        {
            _compareAttempted = true;
            var scalarType = _path.Length == 0
                ? (_element ? _ownerType.GetElementType() : _ownerType) : _path[_path.Length - 1].FieldType;
            if (scalarType == null) return;
            var type = Nullable.GetUnderlyingType(scalarType) ?? scalarType;
            if (!type.IsPrimitive && !type.IsEnum && type != typeof(decimal)) return;
            if (type == typeof(IntPtr) || type == typeof(UIntPtr)) return;
            long started = Stopwatch.GetTimestamp();
            try
            {
                var owner = Expression.Parameter(typeof(object), "owner");
                var index = Expression.Parameter(typeof(int), "index");
                var expected = Expression.Parameter(typeof(object), "expected");
                var actual = Expression.Variable(scalarType, "actual");
                var saved = Expression.Variable(scalarType, "saved");
                Expression Equal(Expression left, Expression right)
                {
                    if (type == typeof(float) || type == typeof(double))
                    {
                        var bits = typeof(BitConverter).GetMethod(type == typeof(float)
                            ? nameof(BitConverter.SingleToInt32Bits) : nameof(BitConverter.DoubleToInt64Bits));
                        return Expression.Equal(Expression.Call(bits, left), Expression.Call(bits, right));
                    }
                    return Expression.Equal(left, right);
                }
                Expression comparison;
                if (type == scalarType) comparison = Equal(actual, saved);
                else
                {
                    var actualHas = Expression.Property(actual, "HasValue");
                    var savedHas = Expression.Property(saved, "HasValue");
                    comparison = Expression.AndAlso(Expression.Equal(actualHas, savedHas),
                        Expression.OrElse(Expression.Not(actualHas),
                            Equal(Expression.Property(actual, "Value"), Expression.Property(saved, "Value"))));
                }
                var body = Expression.Block(new[] { actual, saved },
                    Expression.Assign(actual, StorageExpression(owner, index)),
                    Expression.Assign(saved, Expression.Convert(expected, scalarType)), comparison);
                _equals = Expression.Lambda<Func<object, int, object, bool>>(body, owner, index, expected).Compile();
            }
            catch (Exception e) when (Unavailable(e) || e is ArgumentException) { }
            finally
            {
                Interlocked.Add(ref _compareCompileTicks, Stopwatch.GetTimestamp() - started);
                Interlocked.Increment(ref _compareCompileAttempts);
            }
        }

        private void Compile()
        {
            _attempted = true;
            long started = Stopwatch.GetTimestamp();
            try
            {
                var owner = Expression.Parameter(typeof(object), "owner");
                var index = Expression.Parameter(typeof(int), "index");
                Expression value = StorageExpression(owner, index);
                _read = Expression.Lambda<Func<object, int, object>>(
                    Expression.Convert(value, typeof(object)), owner, index).Compile();
            }
            catch (Exception e) when (Unavailable(e) || e is ArgumentException) { }
            finally
            {
                Interlocked.Add(ref _compileTicks, Stopwatch.GetTimestamp() - started);
                Interlocked.Increment(ref _compileAttempts);
            }
        }

        private static bool Unavailable(Exception e) => e is NotSupportedException
            || e is MemberAccessException || e is SecurityException || e is InvalidProgramException
            || e is TypeLoadException;

        internal static void ClearCache()
        {
            lock (Cache)
            {
                Cache.Clear();
                Interlocked.Exchange(ref _compileTicks, 0);
                Interlocked.Exchange(ref _compileAttempts, 0);
                Interlocked.Exchange(ref _compareCompileTicks, 0);
                Interlocked.Exchange(ref _compareCompileAttempts, 0);
            }
        }

        internal static string Summary
        {
            get
            {
                lock (Cache)
                {
                    int compiled = 0, fallback = 0, comparers = 0;
                    foreach (var reader in Cache.Values)
                    {
                        if (reader.Compiled) compiled++;
                        else if (reader._attempted) fallback++;
                        if (reader.ComparerCompiled) comparers++;
                    }
                    return $"readers(compiled={compiled},fallback={fallback},cached={Cache.Count}," +
                        $"compileAttemptsTotal={CompileAttempts},compileMsTotal={CompileMilliseconds(CompileTicks)}," +
                        $"comparers={comparers},compareCompileAttemptsTotal={CompareCompileAttempts}," +
                        $"compareCompileMsTotal={CompileMilliseconds(CompareCompileTicks)})";
                }
            }
        }
    }
}
#endif
