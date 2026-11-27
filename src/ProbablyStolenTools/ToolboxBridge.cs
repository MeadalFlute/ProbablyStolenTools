using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProbablyStolenTools
{
    internal static class CustomerIntent
    {
        internal static bool Ordinary(StoreClient.ClientIntent intent)
            => intent == StoreClient.ClientIntent.BUY || intent == StoreClient.ClientIntent.SELL
            || intent == StoreClient.ClientIntent.SELLNBUY || intent == StoreClient.ClientIntent.BARTER
            || intent == StoreClient.ClientIntent.APPRAISAL_SERVICE || intent == StoreClient.ClientIntent.GUNSMITH
            || intent == StoreClient.ClientIntent.EXPEDITION;
    }
    internal static class ToolboxBridge
    {
        internal static readonly IDictionary<string, object> Services = new Dictionary<string, object>
        {
            ["protocol"] = 1, ["modId"] = Plugin.Guid,
            ["busy"] = (Func<bool>)(() => CustomerCheckpoint.Restoring || Rewind.Restoring || Rewind.Settling),
            ["loading"] = (Func<bool>)(() => NativeLoadDiagnostics.Active || SaveManager.IsDecoding),
            ["capturing"] = (Func<bool>)(() => CheckpointPanel.SuppressHotkeys),
            ["keys"] = (Func<KeyCode[]>)Keys,
            ["panelKey"] = (Func<KeyCode>)(() => Plugin.CfgKeyPanel?.Value ?? KeyCode.None),
            ["actionKeys"] = (Func<KeyCode[]>)(() => ActionKeys()),
            ["openToolbox"] = (Action)CheckpointPanel.OpenToolbox,
            ["legacySettings"] = (Func<IDictionary<string, string>>)(() => Plugin.Instance?.Config?.Export(LegacyToolKey))
        };
        private static IDictionary<string, object> _peer;
        private static float _next;
        private static string _error;
        private static bool LegacyToolKey(string key) => key.StartsWith("AutoAppraisal.", StringComparison.Ordinal)
            || key == "Dialogue.SkipCustomerDialogue" || key.StartsWith("Inspection.", StringComparison.Ordinal)
            || key.StartsWith("WaterQuality.", StringComparison.Ordinal) || key.StartsWith("Chemistry.", StringComparison.Ordinal)
            || key.StartsWith("Machines.", StringComparison.Ordinal) || key.StartsWith("Windows.", StringComparison.Ordinal)
            || key.StartsWith("Wine.", StringComparison.Ordinal);
        private static KeyCode[] Keys()
        {
            var keys = new KeyCode[Plugin.Hotkeys.Count];
            for (int i = 0; i < keys.Length; i++) keys[i] = Plugin.Hotkeys[i].Config.Value;
            return keys;
        }
        private static KeyCode[] ActionKeys()
        {
            var keys = new List<KeyCode>();
            foreach (var binding in Plugin.Hotkeys)
                if (binding.Config != Plugin.CfgKeyPanel) keys.Add(binding.Config.Value);
            return keys.ToArray();
        }
        internal static bool Available => _peer != null;
        internal static bool PeerCapturing
        {
            get
            {
                try { Refresh(); return _peer != null && _peer.TryGetValue("capturing", out var f) && f is Func<bool> get && get(); }
                catch (Exception e) { if (_error != e.Message) EventLog.Write("[Bridge] input paused: " + e.Message); _error = e.Message; return true; }
            }
        }
        internal static bool PeerKeyUsed(KeyCode key, bool panel = false)
        {
            string service = panel && _peer != null && _peer.ContainsKey("actionKeys") ? "actionKeys" : "keys";
            if (key == KeyCode.None || _peer == null || !_peer.TryGetValue(service, out var f) || !(f is Func<KeyCode[]> get)) return false;
            foreach (var used in get())
                if (used == key || (used == KeyCode.LeftShift || used == KeyCode.RightShift) && (key == KeyCode.LeftShift || key == KeyCode.RightShift)) return true;
            return false;
        }
        internal static void Refresh(bool force = false)
        {
            if (!force && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;
            IDictionary<string, object> peer = null;
            foreach (var mod in ModLoader.Mods)
            {
                if (!mod.IsEnabled || mod.Manifest.ID != "shift.toolbox" || !(mod.Instance is IServiceProvider provider)) continue;
                var candidate = provider.GetService(typeof(IDictionary<string, object>)) as IDictionary<string, object>;
                if (candidate != null && candidate.TryGetValue("protocol", out var v) && v is int n && n == 1
                    && candidate.TryGetValue("modId", out var id) && Equals(id, "shift.toolbox")) peer = candidate;
            }
            if (!ReferenceEquals(_peer, peer))
            {
                Unmount(); _peer = peer;
                EventLog.Write("[Bridge] Shift Toolbox=" + (peer == null ? "absent" : "protocol-v1"));
            }
        }
        internal static void Reset()
        {
            Refresh(true);
            // Do not swallow errors before restore: a broken participant must not mutate restored state.
            if (_peer != null && _peer.TryGetValue("reset", out var action) && action is Action reset) reset();
        }
        internal static bool Mount(CustomUIWindow window, RectTransform parent)
        {
            Refresh(true);
            if (_peer == null || !_peer.TryGetValue("mount", out var action) || !(action is Action<CustomUIWindow, RectTransform> mount)) return false;
            mount(window, parent); return true;
        }
        internal static void Unmount()
        {
            try
            {
                if (_peer != null && _peer.TryGetValue("unmount", out var action) && action is Action unmount) unmount();
            }
            catch (Exception e)
            {
                if (_error != e.Message) EventLog.Write("[Bridge] toolbox UI cleanup failed: " + e.Message);
                _error = e.Message;
            }
        }
    }
}
