using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace ProbablyStolenTools
{
    // Evidence within the captured graph only. Opaque objects/statics/unreachable references remain unknown.
    internal sealed class SemanticTagOwnershipAudit
    {
        internal int Maps, SharedMaps, StorageObjects, CachedViews, StoredComparers, SyncRoots;
        internal int MapExternalEdges, StorageExternalEdges, ViewExternalEdges, RootAliases;
        internal int StateExternalEdges, StandaloneStates, UnknownMapSlots;
        internal long MapSlots, StorageSlots, StateSlots, ElapsedMs;
        internal readonly List<string> Samples = new();
        internal string Summary => "scope=captured-graph-only pruningAllowed=false " +
            $"maps={Maps} sharedMaps={SharedMaps} storageObjects={StorageObjects} cachedViews={CachedViews} " +
            $"storedComparers={StoredComparers} comparerState=unproven syncRoots={SyncRoots} " +
            $"mapExternalEdges={MapExternalEdges} storageExternalEdges={StorageExternalEdges} " +
            $"viewExternalEdges={ViewExternalEdges} rootAliases={RootAliases} " +
            $"stateExternalEdges={StateExternalEdges} standaloneStates={StandaloneStates} " +
            $"unknownMapSlots={UnknownMapSlots} " +
            $"legacySlots(maps={MapSlots},storage={StorageSlots},linkedStates={StateSlots}) " +
            $"auditMs={ElapsedMs}" + (Samples.Count == 0 ? "" : " samples=[" + string.Join(";", Samples) + "]");

        internal static SemanticTagOwnershipAudit Capture<TSystem, TTag>(StateGraph graph,
            SemanticTagSnapshot<TSystem, TTag> snapshot, string ownerField)
            where TSystem : class where TTag : class
        {
            var clock = Stopwatch.StartNew();
            var result = new SemanticTagOwnershipAudit();
            var owners = new Dictionary<IntPtr, IntPtr>();
            var maps = new Dictionary<IntPtr, int>();
            var tags = new HashSet<IntPtr>();
            var storage = new HashSet<IntPtr>();
            var entries = new HashSet<IntPtr>();
            var views = new HashSet<IntPtr>();
            var comparers = new HashSet<IntPtr>();
            var syncRoots = new HashSet<IntPtr>();
            // Exact owner/slot/target pairs distinguish legitimate dictionary links from other aliases.
            var mapLinks = new HashSet<(IntPtr Owner, string Slot, IntPtr Target)>();
            snapshot.VisitCapturedIdentities((system, map) =>
            {
                var id = ManagedObjects.Id(system);
                var mapId = ManagedObjects.Id(map);
                owners.Add(id, mapId);
                if (mapId != IntPtr.Zero)
                {
                    maps.TryGetValue(mapId, out int count);
                    maps[mapId] = count + 1;
                }
            }, tag => tags.Add(ManagedObjects.Id(tag)));
            result.Maps = maps.Count;
            result.SharedMaps = maps.Count(pair => pair.Value > 1);
            foreach (var id in graph.Objects)
                if (ManagedObjects.Get(id) is TTag && !tags.Contains(id)) result.StandaloneStates++;

            // First pass finds captured collection storage, not Dictionary properties/lazy view getters.
            graph.VisitCapturedSlots((owner, slot, target) =>
            {
                if (tags.Contains(owner)) result.StateSlots++;
                if (!maps.ContainsKey(owner)) return;
                result.MapSlots++;
                int dot = slot.Name.LastIndexOf('.');
                string name = slot.Name.Substring(dot + 1);
                bool known = name == "_count" || name == "_freeList" || name == "_freeCount"
                    || name == "_version" || name == "_fastModMultiplier";
                if (slot.IsReference)
                {
                    HashSet<IntPtr> group = null;
                    if (name == "_buckets" || name == "_entries") group = storage;
                    else if (name == "_keys" || name == "_values") group = views;
                    else if (name == "_comparer") group = comparers;
                    else if (name == "_syncRoot") group = syncRoots;
                    known = group != null;
                    if (group != null && target != IntPtr.Zero)
                    {
                        group.Add(target);
                        mapLinks.Add((owner, slot.Name, target));
                        if (name == "_entries") entries.Add(target);
                    }
                }
                if (!known) result.UnknownMapSlots++;
            });
            result.StorageObjects = storage.Count;
            result.CachedViews = views.Count;
            result.StoredComparers = comparers.Count;
            result.SyncRoots = syncRoots.Count;
            void Sample(string kind, IntPtr owner, string slot)
            {
                if (result.Samples.Count >= 6) return;
                string path = graph.SourceOf(owner) + " -> " + slot;
                if (path.Length > 280) path = "..." + path.Substring(path.Length - 277);
                result.Samples.Add(kind + ":" + path);
            }
            var rootTargets = new HashSet<IntPtr>(maps.Keys);
            rootTargets.UnionWith(storage); rootTargets.UnionWith(views);
            foreach (var id in rootTargets)
                if (graph.IsCapturedRoot(id))
                {
                    result.RootAliases++;
                    Sample("explicit-root", id, ManagedObjects.Get(id).GetType().Name);
                }
            // Second pass scans all captured references, including embedded enumerators and delegate targets.
            graph.VisitCapturedSlots((owner, slot, target) =>
            {
                if (storage.Contains(owner)) result.StorageSlots++;
                if (!slot.IsReference || target == IntPtr.Zero) return;
                if (maps.ContainsKey(target)
                    && !(slot.Name == ownerField && owners.TryGetValue(owner, out var map) && map == target))
                {
                    result.MapExternalEdges++;
                    Sample("map-alias", owner, slot.Name);
                }
                if (storage.Contains(target) && !mapLinks.Contains((owner, slot.Name, target)))
                {
                    result.StorageExternalEdges++;
                    Sample("storage-alias", owner, slot.Name);
                }
                if (views.Contains(target) && !mapLinks.Contains((owner, slot.Name, target)))
                {
                    result.ViewExternalEdges++;
                    Sample("view-alias", owner, slot.Name);
                }
                if (tags.Contains(target) && !entries.Contains(owner))
                {
                    result.StateExternalEdges++;
                    Sample("state-alias", owner, slot.Name);
                }
            });
            result.ElapsedMs = clock.ElapsedMilliseconds;
            return result;
        }
    }
}
