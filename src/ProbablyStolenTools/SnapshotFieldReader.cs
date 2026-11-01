using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Security;

namespace ProbablyStolenTools
{
    // Cache code and field metadata only. Never bind a reader to a business object or snapshot.
    internal sealed class SnapshotFieldReader
    {
        private const int CacheLimit = 4096;
        private const int CompileAfterReads = 32;
        private static readonly Dictionary<(Type Owner, string Name), SnapshotFieldReader> Cache = new();
        private readonly Type _ownerType;
        private readonly FieldInfo[] _path;
        private readonly bool _element;
        private Func<object, int, object> _read;
        private int _reads;
        private bool _attempted;
        internal bool Compiled => _read != null;

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

        private void Compile()
        {
            _attempted = true;
            try
            {
                var owner = Expression.Parameter(typeof(object), "owner");
                var index = Expression.Parameter(typeof(int), "index");
                Expression value = Expression.Convert(owner, _ownerType);
                if (_element) value = Expression.ArrayIndex(value, index);
                foreach (var field in _path) value = Expression.Field(value, field);
                _read = Expression.Lambda<Func<object, int, object>>(
                    Expression.Convert(value, typeof(object)), owner, index).Compile();
            }
            catch (Exception e) when (Unavailable(e) || e is ArgumentException) { }
        }

        private static bool Unavailable(Exception e) => e is NotSupportedException
            || e is MemberAccessException || e is SecurityException || e is InvalidProgramException
            || e is TypeLoadException;

        internal static void ClearCache()
        {
            lock (Cache) Cache.Clear();
        }

        internal static string Summary
        {
            get
            {
                lock (Cache)
                {
                    int compiled = 0, fallback = 0;
                    foreach (var reader in Cache.Values)
                        if (reader.Compiled) compiled++;
                        else if (reader._attempted) fallback++;
                    return $"readers(compiled={compiled},fallback={fallback},cached={Cache.Count})";
                }
            }
        }
    }
}
