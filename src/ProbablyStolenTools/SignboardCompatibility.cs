#if MONO
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ProbablyStolenTools
{
    // Deliberately bounded to the inspected build. No optional assembly reference or general field enumeration.
    internal sealed class SignboardCompatibility
    {
        private const string ModId = "PawnshopBillboards";
        private static readonly Guid Supported = new("6a02d0ff-61d7-49db-9895-fe09c04b5741");
        private static bool _warned, _noticeShown;
        private static readonly HashSet<string> CaptureWaits = new();
        private static float _noticeRetryAt;
        private static class Api
        {
            internal static readonly FieldInfo Inventory = AccessTools.Field("PawnshopBillboards.HangingStorage:Inventory");
            internal static readonly FieldInfo Owner = AccessTools.Field("PawnshopBillboards.BillboardManager:owner");
            internal static readonly FieldInfo Run = AccessTools.Field("PawnshopBillboards.BillboardManager:run");
            internal static readonly FieldInfo Busy = AccessTools.Field("PawnshopBillboards.BillboardManager:busy");
            internal static readonly FieldInfo Fault = AccessTools.Field("PawnshopBillboards.BillboardManager:fault");
            internal static readonly FieldInfo Initialized = AccessTools.Field("PawnshopBillboards.BillboardManager:initialized");
            internal static readonly FieldInfo Dirty = AccessTools.Field("PawnshopBillboards.BillboardManager:snapshotDirty");
            internal static readonly FieldInfo Height = AccessTools.Field("PawnshopBillboards.BillboardManager:heightOffset");
            internal static readonly FieldInfo Expanded = AccessTools.Field("PawnshopBillboards.BillboardManager:expanded");
            internal static readonly FieldInfo RefreshAt = AccessTools.Field("PawnshopBillboards.BillboardManager:refreshAt");
            internal static readonly FieldInfo Dragged = AccessTools.Field("PawnshopBillboards.BillboardManager:dragged");
            internal static readonly FieldInfo[] Maps = {
            AccessTools.Field("PawnshopBillboards.BillboardManager:records"),
            AccessTools.Field("PawnshopBillboards.BillboardManager:unavailable"),
            AccessTools.Field("PawnshopBillboards.BillboardManager:unresolved") };
            internal static readonly FieldInfo[] Placement = {
            AccessTools.Field("PawnshopBillboards.Placement:uid"), AccessTools.Field("PawnshopBillboards.Placement:region"),
            AccessTools.Field("PawnshopBillboards.Placement:x"), AccessTools.Field("PawnshopBillboards.Placement:y"),
            AccessTools.Field("PawnshopBillboards.Placement:width"), AccessTools.Field("PawnshopBillboards.Placement:height"),
            AccessTools.Field("PawnshopBillboards.Placement:orientation"), AccessTools.Field("PawnshopBillboards.Placement:flipped"),
            AccessTools.Field("PawnshopBillboards.Placement:id"), AccessTools.Field("PawnshopBillboards.Placement:brightness"),
            AccessTools.Field("PawnshopBillboards.Placement:lastBrightness") };
            internal static readonly MethodInfo ClearViews = AccessTools.Method("PawnshopBillboards.BillboardManager:SceneUnloaded");
            internal static readonly MethodInfo Commit = AccessTools.Method("PawnshopBillboards.BillboardManager:Commit");
            internal static GameGridInventory Root()
            {
                ValidateBuild();
                return Inventory.GetValue(null) as GameGridInventory
                    ?? throw new InvalidOperationException("Proper Signboards hanging inventory is not initialized; retry after entering the shop.");
            }
            internal static bool SupportedBuild => !(Inventory == null || Inventory.Module.ModuleVersionId != Supported || Owner == null || Run == null
                    || Busy == null || Fault == null || Initialized == null || Dirty == null || Height == null
                    || Expanded == null || RefreshAt == null || Dragged == null || !NoArgumentsStatic(ClearViews) || !NoArgumentsStatic(Commit)
                    || Array.Exists(Maps, f => f == null) || Array.Exists(Placement, f => f == null));
            private static bool NoArgumentsStatic(MethodInfo method)
                => method != null && method.IsStatic && method.GetParameters().Length == 0;
            internal static void ValidateBuild()
            {
                if (!SupportedBuild)
                    throw new InvalidOperationException("Proper Signboards build changed after capture; this checkpoint cannot be restored.");
            }
        }
        private readonly GameGridInventory _inventory;
        private readonly PlayerStore _owner;
        private readonly string _run;
        private readonly object _height, _expanded;
        private readonly IDictionary[] _maps = new IDictionary[3];
        private readonly List<DictionaryEntry>[] _entries = new List<DictionaryEntry>[3];
        private readonly List<(object Record, object[] Values)> _placements = new();

        internal static GameGridInventory Root()
        {
            if (!OptionalModCompatibility.IsRunning(ModId)) return null;
            if (!Api.SupportedBuild) { WarnUnsupported(); return null; }
            return Api.Root();
        }

        private static void WarnUnsupported()
        {
            if (_warned) return;
            _warned = true;
            EventLog.Write("[Compatibility] Proper Signboards build is not verified 1.0.48; signboard adapter skipped, memory checkpoints and native saves remain available. Hanging signboard rewind is unverified.");
        }

        internal static void Tick()
        {
            if (_noticeShown || !OptionalModCompatibility.IsRunning(ModId) || Api.SupportedBuild) return;
            WarnUnsupported();
            float now = UnityEngine.Time.unscaledTime;
            if (Plugin.Store() == null || now < _noticeRetryAt) return;
            _noticeRetryAt = now + 1f;
            _noticeShown = CompatibilityNotice.Show(InspectionLocale.Text(
                "Proper Signboards 不是已适配的 1.0.48 构建。\n已跳过灯牌专项适配，不停用 QuickSL 或常规存档。\n此版本灯牌摘挂后的内存回溯未经验证，可能出现数据异常。",
                "This Proper Signboards build is not the verified 1.0.48 build.\nSignboard-specific adaptation is skipped; QuickSL and native saving remain available.\nMemory rewind after hanging or removing signs is unverified and may cause data errors."));
        }

        internal static SignboardCompatibility Capture(PlayerStore store)
        {
            var inventory = Root();
            return inventory == null ? null : new SignboardCompatibility(store, inventory);
        }

        internal static bool CaptureReady(PlayerStore store)
        {
            if (!OptionalModCompatibility.IsRunning(ModId) || !Api.SupportedBuild) return true;
            string reason;
            try
            {
                if (Api.Inventory.GetValue(null) == null || !(bool)Api.Initialized.GetValue(null)) reason = "not initialized";
                else if (!ReferenceEquals(Api.Owner.GetValue(null), store) || (string)Api.Run.GetValue(null) != store?.runID) reason = "owner/run mismatch";
                else if ((bool)Api.Fault.GetValue(null)) reason = "faulted";
                else if ((bool)Api.Busy.GetValue(null) || Api.Dragged.GetValue(null) != null) reason = "busy/dragging";
                else return true;
            }
            catch (Exception e) { reason = e.GetType().Name + ": " + e.Message; }
            if (CaptureWaits.Add(reason)) EventLog.Write("[Compatibility] checkpoint capture deferred until Proper Signboards is stable: " + reason);
            return false;
        }

        private SignboardCompatibility(PlayerStore store, GameGridInventory inventory)
        {
            _inventory = inventory;
            _owner = store;
            _run = store.runID;
            Validate();
            _height = Api.Height.GetValue(null);
            _expanded = Api.Expanded.GetValue(null);
            for (int i = 0; i < Api.Maps.Length; i++)
            {
                var map = Api.Maps[i].GetValue(null) as IDictionary;
                if (map == null || map.Count > 360) throw new InvalidOperationException("Proper Signboards record budget exceeded (360 per map).");
                _maps[i] = map;
                var entries = _entries[i] = new List<DictionaryEntry>(map.Count);
                foreach (DictionaryEntry entry in map)
                {
                    entries.Add(entry);
                    if (i != 0) continue;
                    var values = new object[Api.Placement.Length];
                    for (int j = 0; j < values.Length; j++) values[j] = Api.Placement[j].GetValue(entry.Value);
                    _placements.Add((entry.Value, values));
                }
            }
        }

        internal void Validate()
        {
            Api.ValidateBuild();
            if (!OptionalModCompatibility.IsRunning(ModId) || !ReferenceEquals(Api.Owner.GetValue(null), _owner)
                || !ReferenceEquals(Api.Inventory.GetValue(null), _inventory) || (string)Api.Run.GetValue(null) != _run
                || !(bool)Api.Initialized.GetValue(null) || (bool)Api.Busy.GetValue(null) || (bool)Api.Fault.GetValue(null)
                || Api.Dragged.GetValue(null) != null)
                throw new InvalidOperationException("Proper Signboards owner/inventory changed, is busy or faulted; reload before rewinding.");
            for (int i = 0; i < _maps.Length; i++)
                if (_maps[i] != null && !ReferenceEquals(Api.Maps[i].GetValue(null), _maps[i]))
                    throw new InvalidOperationException("Proper Signboards record identity changed; checkpoint cannot be restored.");
        }

        internal void RestoreMetadata()
        {
            Api.ClearViews.Invoke(null, null);
            RestoreRecords();
        }

        internal void RestoreRecords()
        {
            foreach (var record in _placements)
                for (int j = 0; j < Api.Placement.Length; j++) Api.Placement[j].SetValue(record.Record, record.Values[j]);
            for (int i = 0; i < _maps.Length; i++)
            {
                _maps[i].Clear();
                foreach (var entry in _entries[i]) _maps[i].Add(entry.Key, entry.Value);
            }
            Api.Height.SetValue(null, _height);
            Api.Expanded.SetValue(null, _expanded);
            Api.RefreshAt.SetValue(null, 0f);
            Api.Dirty.SetValue(null, true);
        }

        // Rebuild native save caches from restored originals, not stale serialized node references.
        internal void RefreshSaveCache()
        {
            const string key = "billboards.item.lighting.v1";
            var lighting = new List<(GameItem Item, TagState State, bool Present)>();
            foreach (var item in _inventory.childItems)
            {
                if (item?.state?.dict == null) continue;
                bool present = item.state.dict.TryGetValue(key, out var state);
                lighting.Add((item, state, present));
            }
            try { Api.Commit.Invoke(null, null); }
            catch (Exception e)
            {
                throw new InvalidOperationException("Native checkpoint graph was written, but Proper Signboards save-cache refresh failed. Reload before continuing; this is not a clean restore failure.", e);
            }
            finally
            {
                // Commit serializes fresh lighting but must not replace retained native TagState identities.
                foreach (var entry in lighting)
                    if (entry.Present) entry.Item.state.dict[key] = entry.State;
                    else entry.Item.state.dict.Remove(key);
            }
        }
    }
}
#endif
