using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ProbablyStolenTools
{
    // Store bit patterns, not floating-point equality; null strings remain distinct from empty.
    internal readonly record struct SemanticTagValue(bool Enabled, string Text, int Integer, int SingleBits,
        long Long, long DoubleBits, bool Boolean, string Identifier, string DisplayName)
    {
        internal bool SameState(SemanticTagValue other) => Enabled == other.Enabled
            && ReferenceEquals(Text, other.Text) && Integer == other.Integer && SingleBits == other.SingleBits
            && Long == other.Long && DoubleBits == other.DoubleBits && Boolean == other.Boolean
            && ReferenceEquals(Identifier, other.Identifier) && ReferenceEquals(DisplayName, other.DisplayName);
    }

    // Experimental read-only representation. Deliberately has no Restore/Write API.
    internal sealed class SemanticTagSnapshot<TSystem, TTag> : IDisposable
        where TSystem : class where TTag : class
    {
        private sealed class Identity<T> : IEqualityComparer<T> where T : class
        {
            internal static readonly Identity<T> Instance = new();
            public bool Equals(T x, T y) => ReferenceEquals(x, y);
            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }

        private readonly record struct Owner(TSystem System, Dictionary<string, TTag> Map);
        private readonly record struct Map(Dictionary<string, TTag> Dictionary, IEqualityComparer<string> Comparer,
            KeyValuePair<string, TTag>[] Entries);
        private readonly record struct Tag(TTag Instance, SemanticTagValue Value);
        private readonly List<Owner> _owners = new();
        private readonly List<Map> _maps = new();
        private readonly List<Tag> _tags = new();
        private readonly Func<TSystem, Dictionary<string, TTag>> _readMap;
        private readonly Func<TTag, SemanticTagValue> _readTag;
        private bool _disposed;
        internal int SystemCount => _owners.Count;
        internal int MapCount => _maps.Count;
        internal int TagCount => _tags.Count;
        internal int EntryCount { get; private set; }
        // Logical payload only: each owner link, comparer link, key/value pair and nine tag fields.
        internal long LogicalSlots => SystemCount + MapCount + 2L * EntryCount + 9L * TagCount;
        internal long LogicalBytes => (SystemCount + MapCount + 2L * EntryCount + 3L * TagCount) * IntPtr.Size
            + 6L * TagCount * sizeof(long);

        private SemanticTagSnapshot(Func<TSystem, Dictionary<string, TTag>> readMap,
            Func<TTag, SemanticTagValue> readTag) { _readMap = readMap; _readTag = readTag; }

        internal static SemanticTagSnapshot<TSystem, TTag> Capture(IEnumerable<TSystem> systems,
            Func<TSystem, Dictionary<string, TTag>> readMap, Func<TTag, SemanticTagValue> readTag,
            int maxObjects = 200000, int maxSlots = 3000000, long maxBytes = 256L * 1024 * 1024)
        {
            var snapshot = new SemanticTagSnapshot<TSystem, TTag>(readMap, readTag);
            try
            {
                var owners = new HashSet<TSystem>(Identity<TSystem>.Instance);
                var maps = new HashSet<Dictionary<string, TTag>>(Identity<Dictionary<string, TTag>>.Instance);
                var tags = new HashSet<TTag>(Identity<TTag>.Instance);
                void Budget(long slots, long bytes, long objects)
                {
                    if (slots > maxSlots || bytes > maxBytes || objects > maxObjects)
                        throw new InvalidOperationException($"Semantic tag shadow budget exceeded: " +
                            $"objects={objects}/{maxObjects}, slots={slots}/{maxSlots}, bytes={bytes}/{maxBytes}.");
                }
                foreach (var system in systems)
                {
                    if (system == null || !owners.Add(system)) continue;
                    var dictionary = readMap(system);
                    snapshot._owners.Add(new Owner(system, dictionary));
                    Budget(snapshot.LogicalSlots, snapshot.LogicalBytes, owners.Count + maps.Count + tags.Count);
                    if (dictionary == null || !maps.Add(dictionary)) continue;
                    int count = dictionary.Count;
                    Budget(snapshot.LogicalSlots + 1L + 2L * count,
                        snapshot.LogicalBytes + (1L + 2L * count) * IntPtr.Size,
                        owners.Count + maps.Count + tags.Count);
                    var entries = new KeyValuePair<string, TTag>[count];
                    int index = 0;
                    foreach (var entry in dictionary)
                    {
                        if (index == count) throw new InvalidOperationException("Tag map changed while capturing.");
                        entries[index++] = entry;
                        if (entry.Value == null || !tags.Add(entry.Value)) continue;
                        Budget(snapshot.LogicalSlots + 1L + 2L * count + 9L,
                            snapshot.LogicalBytes + (1L + 2L * count + 3L) * IntPtr.Size + 6L * sizeof(long),
                            owners.Count + maps.Count + tags.Count);
                        snapshot._tags.Add(new Tag(entry.Value, readTag(entry.Value)));
                    }
                    if (index != count) throw new InvalidOperationException("Tag map changed while capturing.");
                    snapshot._maps.Add(new Map(dictionary, dictionary.Comparer, entries));
                    snapshot.EntryCount = checked(snapshot.EntryCount + count);
                }
                return snapshot;
            }
            catch { snapshot.Dispose(); throw; }
        }

        internal IReadOnlyList<string> Differences(int max = 12)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SemanticTagSnapshot<TSystem, TTag>));
            var result = new List<string>();
            void Difference(string name) { if (result.Count < max) result.Add(name); }
            foreach (var owner in _owners)
                if (!ReferenceEquals(_readMap(owner.System), owner.Map)) Difference("tag map identity");
            foreach (var map in _maps)
            {
                if (!ReferenceEquals(map.Dictionary.Comparer, map.Comparer)) Difference("tag comparer identity");
                if (map.Dictionary.Count != map.Entries.Length) Difference("tag entry count");
                int index = 0;
                // Preserve observable enumeration order and key spelling without invoking a custom comparer.
                foreach (var entry in map.Dictionary)
                {
                    if (index >= map.Entries.Length) Difference("extra tag entry");
                    else if (!ReferenceEquals(entry.Key, map.Entries[index].Key)
                        || !ReferenceEquals(entry.Value, map.Entries[index].Value)) Difference("tag entry identity/order");
                    index++;
                }
            }
            foreach (var tag in _tags)
                if (!tag.Value.SameState(_readTag(tag.Instance))) Difference("tag value/identity");
            return result;
        }

        internal void ValidateCoverage(Func<object, bool> contains)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SemanticTagSnapshot<TSystem, TTag>));
            void Check(object value)
            {
                if (value != null && !contains(value))
                    throw new InvalidOperationException("Semantic tag object was not covered by the legacy graph: " + value.GetType().FullName);
            }
            foreach (var owner in _owners) { Check(owner.System); Check(owner.Map); }
            foreach (var map in _maps)
            {
                // Dictionary.Comparer may expose a default/wrapped comparer absent from its stored fields.
                foreach (var entry in map.Entries) { Check(entry.Key); Check(entry.Value); }
            }
            foreach (var tag in _tags)
            {
                Check(tag.Value.Text); Check(tag.Value.Identifier); Check(tag.Value.DisplayName);
            }
        }

        internal void VisitCapturedIdentities(Action<TSystem, Dictionary<string, TTag>> owner, Action<TTag> tag)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(SemanticTagSnapshot<TSystem, TTag>));
            foreach (var value in _owners) owner(value.System, value.Map);
            foreach (var value in _tags) tag(value.Instance);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _owners.Clear(); _maps.Clear(); _tags.Clear(); EntryCount = 0;
        }
    }
}
