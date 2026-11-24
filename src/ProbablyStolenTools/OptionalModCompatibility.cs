namespace ProbablyStolenTools
{
    internal static class OptionalModCompatibility
    {
        internal const string BrewingId = "vanilla.brewing.expansion";
        internal const string ClaimsId = "yexiaoyang.InteriorExpansion";
#if MONO
        private static System.Collections.Generic.IReadOnlyList<LoadedMod> _mods;
#endif
        private static float _nextRefreshAt;
        private static string _handledValue;
        private static bool _claimsHandled;
        private static ModConfig _pendingConfig;
        private static long _retryAfterUtcTicks;
        private static string _writeError;

        internal static void Refresh()
        {
#if MONO
            // Rev16 returns a copy; poll slowly and refresh explicitly at action boundaries.
            _mods = ModLoader.Mods;
#endif
            ApplyConflictDefault();
        }

        private static void ApplyConflictDefault()
        {
            if (Plugin.CfgDisableMemoryCheckpoints == null || Plugin.CfgHandledMemoryConflicts == null) return;
            string handled = Plugin.CfgHandledMemoryConflicts.Value ?? "";
            if (_handledValue != handled)
            {
                _handledValue = handled;
                _claimsHandled = false;
                foreach (string id in handled.Split(','))
                    if (id.Trim() == ClaimsId) { _claimsHandled = true; break; }
            }
            if (HasMemoryConflict && !_claimsHandled)
            {
                var config = Plugin.Instance?.Config;
                bool autoSave = config?.SaveOnConfigSet == true;
                if (config != null) config.SaveOnConfigSet = false;
                try
                {
                    // Apply both in memory before attempting IO. A failed save must
                    // neither break hotkeys nor repeatedly undo a player's override.
                    Plugin.CfgDisableMemoryCheckpoints.Value = true;
                    Plugin.CfgHandledMemoryConflicts.Value = handled.Length == 0 ? ClaimsId : handled + "," + ClaimsId;
                }
                finally { if (config != null) config.SaveOnConfigSet = autoSave; }
                _handledValue = Plugin.CfgHandledMemoryConflicts.Value;
                _claimsHandled = true;
                _pendingConfig = config;
                _retryAfterUtcTicks = 0;
                EventLog.Write("[Compatibility] first conflict detected mod=" + ClaimsId
                    + "; DisableMemory=true; manual override will be preserved across restarts");
            }
            SavePendingDefault();
        }

        private static void SavePendingDefault()
        {
            if (_pendingConfig == null || System.DateTime.UtcNow.Ticks < _retryAfterUtcTicks) return;
            _retryAfterUtcTicks = System.DateTime.UtcNow.Ticks + 5 * System.TimeSpan.TicksPerSecond;
            try
            {
                _pendingConfig.Save();
                _pendingConfig = null;
                if (_writeError != null) EventLog.Write("[Compatibility] conflict settings save recovered");
                _writeError = null;
            }
            catch (System.Exception e)
            {
                string error = e.GetType().Name + ": " + e.Message;
                if (_writeError != error)
                    EventLog.Write("[Compatibility] conflict settings save failed; session policy retained; retry in 5s: " + error);
                _writeError = error;
            }
        }

        // Read-only public registry access also works on 0.46D, which predates
        // ModHelper.IsModRunning. Never reference either optional mod's assembly.
        internal static bool IsRunning(string id)
        {
#if MONO
            var mods = _mods ?? ModLoader.Mods;
            for (int i = 0; i < mods.Count; i++)
            {
                var mod = mods[i];
                if (mod.Manifest.ID == id && mod.IsEnabled) return true;
            }
#endif
            return false;
        }

        internal static bool HasMemoryConflict => IsRunning(ClaimsId);
        internal static bool MemoryCheckpointsAllowed => Plugin.CfgDisableMemoryCheckpoints?.Value != true;
        internal static string MemoryCheckpointWarning => HasMemoryConflict ? InspectionLocale.Text(
            "存在mod冲突，已禁用内存节点。\n可在设置中强行启用，但不受理因此导致的BUG。",
            "A mod conflict was detected; memory checkpoints are disabled.\nYou can force-enable them in settings, but resulting bugs are unsupported.") : InspectionLocale.Text(
            "已在设置中禁用内存节点。", "Memory checkpoints are disabled in settings.");
        internal static string Status => HasMemoryConflict && MemoryCheckpointsAllowed ? InspectionLocale.Text(
            "已强行启用内存节点（mod冲突，BUG不受理）",
            "Memory forced on (mod conflict; bugs unsupported)") : InspectionLocale.Text(
            "内存节点已禁用（仅文件读档）", "Memory checkpoints disabled (file reload only)");

        internal static bool BrewingOwns(GameItem item)
        {
            if (item == null) return false;
            // Preserve marked products even if the expansion has been disabled.
            if (item.IsTag("VBE_STATE")) return true;
            if (!IsRunning(BrewingId)
                || !(item.IsTag("WINE_CONTAINER_TAG") || item.identifier == "water_jug")) return false;
            var contents = item.FindAllChildItems();
            if (contents != null)
                foreach (var child in contents)
                    if (child != null && (child.identifier == "nutrifruit" || child.identifier == "dream_cap"))
                        return true;
            return false;
        }

        private static int _lastState = -1;
        private static float _noticeRetryAt;

        private static bool NoticeRecorded(string id)
        {
            foreach (string recorded in (Plugin.CfgNotifiedMemoryConflicts?.Value ?? "").Split(','))
                if (recorded.Trim() == id) return true;
            return false;
        }

        private static void RecordNotice(string id)
        {
            if (Plugin.CfgNotifiedMemoryConflicts == null || NoticeRecorded(id)) return;
            var config = Plugin.Instance?.Config;
            bool autoSave = config?.SaveOnConfigSet == true;
            if (config != null) config.SaveOnConfigSet = false;
            try
            {
                string ids = Plugin.CfgNotifiedMemoryConflicts.Value ?? "";
                Plugin.CfgNotifiedMemoryConflicts.Value = ids.Length == 0 ? id : ids + "," + id;
            }
            finally { if (config != null) config.SaveOnConfigSet = autoSave; }
            // Reuse the throttled save retry; retain the in-memory acknowledgement on IO failure.
            if (_pendingConfig == null) _retryAfterUtcTicks = 0;
            _pendingConfig = config;
            SavePendingDefault();
        }

        internal static void Tick()
        {
            float now = UnityEngine.Time.unscaledTime;
            if (now >= _nextRefreshAt)
            {
                _nextRefreshAt = now + 1f;
                Refresh();
            }
            else ApplyConflictDefault();
            bool claims = HasMemoryConflict;
            bool memory = MemoryCheckpointsAllowed;
            int state = (claims ? 2 : 0) | (IsRunning(BrewingId) ? 1 : 0) | (memory ? 0 : 4);
            if (state != _lastState)
            {
                _lastState = state;
                EventLog.Write("[Compatibility] brewing=" + ((state & 1) != 0)
                    + " claims=" + claims + " memoryCheckpoints=" + memory
                    + " fileRestore=" + (claims ? "native-menu" : "configured"));
            }
            if (!claims) _noticeRetryAt = 0;
            else if (!memory && !NoticeRecorded(ClaimsId) && Plugin.Store() != null && now >= _noticeRetryAt)
            {
                _noticeRetryAt = now + 1f;
                if (CompatibilityNotice.Show(MemoryCheckpointWarning)) RecordNotice(ClaimsId);
            }
        }
    }
}
