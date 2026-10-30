using System;
using System.Collections.Generic;
using HarmonyLib;
#if !MONO
using MelonLoader;
#endif
using UnityEngine;

#if !MONO
[assembly: MelonInfo(typeof(ProbablyStolenTools.Plugin), "Probably Stolen Tools", "0.6.0", "Probably Stolen Tools contributors")]
[assembly: MelonLoader.HarmonyDontPatchAll]
#endif

namespace ProbablyStolenTools
{
    public class Plugin :
#if MONO
        IMod
#else
        MelonMod
#endif
    {
        public const string Guid = "dsh.probablystolen.tools";
        public const string Version = "0.7.0";

        internal static ModLogger LogSource;
        internal static Plugin Instance;
        internal ModConfig Config { get; private set; }

        // ---- auto appraisal ----
        internal static ConfigEntry<bool> CfgAutoAppraisal;
        internal static ConfigEntry<bool> CfgAutoSubmit;
        internal static ConfigEntry<bool> CfgAlwaysOverwrite;
        internal static ConfigEntry<bool> CfgSkipCustomerDialogue;
        internal static ConfigEntry<bool> CfgDoubleClickInspection;
        internal static ConfigEntry<bool> CfgQuickInspection;
        internal static ConfigEntry<float> CfgInspectionX, CfgInspectionY;

        // ---- rewind / checkpoints ----
        internal static ConfigEntry<bool> CfgRewind;
        internal static ConfigEntry<int> CfgMaxPoints;
        internal static ConfigEntry<KeyCode> CfgKeyPrev;
        internal static ConfigEntry<KeyCode> CfgKeyDay;
        internal static ConfigEntry<KeyCode> CfgKeyPrevDay;
        internal static ConfigEntry<bool> CfgConfirmWholeDay;
        internal static ConfigEntry<bool> CfgRepairStuckShutter;
        internal static ConfigEntry<KeyCode> CfgKeyPanel;
        internal static ConfigEntry<string> CfgRestoreMode;
        internal static ConfigEntry<bool> CfgAllowOpen;
        internal static ConfigEntry<bool> CfgQueueRebuild;
        internal static ConfigEntry<bool> CfgCaptureMidDay;
        internal static ConfigEntry<bool> CfgAutoOpenAfterRestore;

        // ---- hotkeys ----
        internal sealed class HotkeyBinding
        {
            internal readonly string Id;
            internal readonly string Label;
            internal readonly ConfigEntry<KeyCode> Config;
            internal readonly KeyCode Default;

            internal HotkeyBinding(string id, string label, ConfigEntry<KeyCode> config, KeyCode fallback)
            {
                Id = id; Label = label; Config = config; Default = fallback;
            }
        }

        internal static List<HotkeyBinding> Hotkeys = new();

        internal static string KeyName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.None: return "<未设置>";
                case KeyCode.Escape: return "Esc";
                case KeyCode.Return: return "Enter";
                case KeyCode.LeftShift: return "L-Shift";
                case KeyCode.RightShift: return "R-Shift";
                case KeyCode.LeftControl: return "L-Ctrl";
                case KeyCode.RightControl: return "R-Ctrl";
                case KeyCode.LeftAlt: return "L-Alt";
                case KeyCode.RightAlt: return "R-Alt";
                case KeyCode.BackQuote: return "`";
                case KeyCode.Space: return "Space";
            }

            string name = key.ToString();
            if (name.StartsWith("Alpha", StringComparison.Ordinal)) return name.Substring(5);
            if (name.StartsWith("Keypad", StringComparison.Ordinal)) return "Num" + name.Substring(6);
            if (name.StartsWith("Joystick", StringComparison.Ordinal)) return "Pad";
            return name;
        }

        /// <summary>改键：若目标键已被本 mod 的另一动作占用，两者交换，避免两个功能共用一键。</summary>
        internal static void SetHotkey(ConfigEntry<KeyCode> target, KeyCode key)
        {
            HotkeyBinding self = null;
            HotkeyBinding clash = null;
            foreach (var binding in Hotkeys)
            {
                if (binding.Config == target) { self = binding; continue; }
                if (binding.Config.Value == key) clash = binding;
            }
            if (self == null) return;

            KeyCode previous = target.Value;
            if (key == KeyCode.None)
            {
                target.Value = KeyCode.None;
                Instance.Config.Save();
                LogSource?.LogInfo($"[Keys] {self.Label} disabled (no hotkey bound)");
                return;
            }
            if (clash != null)
            {
                clash.Config.Value = previous;
                LogSource?.LogInfo($"[Keys] {clash.Label} swapped to {KeyName(previous)}");
            }
            target.Value = key;
            Instance.Config.Save();
            LogSource?.LogInfo($"[Keys] {self.Label} -> {KeyName(key)}");
        }

        internal static void ResetHotkeys()
        {
            foreach (var binding in Hotkeys) binding.Config.Value = binding.Default;
            Instance.Config.Save();
            LogSource?.LogInfo("[Keys] all hotkeys reset to defaults");
        }

        internal static bool HotkeyInUse(KeyCode key, ConfigEntry<KeyCode> except)
        {
            if (key == KeyCode.None) return false;
            foreach (var binding in Hotkeys)
                if (binding.Config != except && binding.Config.Value == key) return true;
            return false;
        }


        // ---- diagnostics ----
        internal static ConfigEntry<bool> CfgDiag;
        internal static ConfigEntry<bool> CfgSelfTest;
        internal static ConfigEntry<bool> CfgPreviewPanel;
        internal static ConfigEntry<bool> CfgHistory;
        internal static ConfigEntry<bool> CfgCustomerCheckpoint;
        internal static ConfigEntry<float> CfgPanelX, CfgPanelY, CfgPanelWidth, CfgPanelHeight;
        internal static ConfigEntry<bool> CfgPanelFolded;

#if MONO
        public void Init(ModManifest manifest) { }
        public void OnEnable() => Initialize();
        public void OnDisable() => OverlayBootstrap.Release();
        private void Initialize()
#else
        public override void OnInitializeMelon()
#endif
        {
            Instance = this;
            LogSource = new ModLogger();
            Config = new ModConfig();

            CfgAutoAppraisal = Config.Bind("AutoAppraisal", "Enabled", true,
                "Auto-fill the appraisal quote with the game's correct value.");
            CfgAutoSubmit = Config.Bind("AutoAppraisal", "AutoSubmit", false,
                "Submit the quote automatically (no confirm click needed).");
            CfgAlwaysOverwrite = Config.Bind("AutoAppraisal", "AlwaysOverwrite", false,
                "Rewrite the quote on every panel refresh (otherwise only once per item).");
            CfgSkipCustomerDialogue = Config.Bind("Dialogue", "SkipCustomerDialogue", false,
                "Advance ordinary customer dialogue through the original recorder path. Choices and special encounters are not skipped.");
            CfgDoubleClickInspection = Config.Bind("Inspection", "DoubleClickToInspect", true,
                "Double-click an inspectable item to apply an owned magnifying glass. Other items keep their original double-click actions.");
            CfgQuickInspection = Config.Bind("Inspection", "ShowQuickInspection", true,
                "Show the draggable read-only quick inspection summary on the native inspection page.");
            CfgInspectionX = Config.Bind("Inspection", "SummaryPositionX", 1f,
                "Quick inspection horizontal position within its available travel, from 0 (left) to 1 (right).");
            CfgInspectionY = Config.Bind("Inspection", "SummaryPositionY", 1f,
                "Quick inspection vertical position within its available travel, from 0 (top) to 1 (bottom).");
            CfgQuickInspection.SettingChanged += (_, _) => QuickInspectionPatch.SettingsChanged();
            CfgAutoAppraisal.SettingChanged += (_, _) => AppraisalPatch.Reset();
            CfgAutoSubmit.SettingChanged += (_, _) => AppraisalPatch.Reset();
            CfgAlwaysOverwrite.SettingChanged += (_, _) => AppraisalPatch.Reset();

            CfgRewind = Config.Bind("Rewind", "Enabled", true,
                "Record a checkpoint before opening the shop and whenever a new client arrives.");
            CfgMaxPoints = Config.Bind("Rewind", "MaxCheckpoints", 20,
                "How many checkpoints to keep.");
            CfgKeyPrev = Config.Bind("Rewind", "KeyPrevCheckpoint", KeyCode.F6,
                "Reset the current customer to their arrival checkpoint (in memory, no save-file fallback).");
            CfgKeyDay = Config.Bind("Rewind", "KeyDayStart", KeyCode.F7,
                "Rewind to the start of the current day.");
            CfgKeyPrevDay = Config.Bind("Rewind", "KeyPreviousDay", KeyCode.F5,
                "回到上一天 / back to previous day");
            CfgConfirmWholeDay = Config.Bind("Rewind", "ConfirmWholeDayRewind", true,
                "Ask for confirmation before the whole-day rewinds (F5 / F7), since they overwrite the current save slot.");
            CfgRepairStuckShutter = Config.Bind("Rewind", "RepairStuckShutter", true,
                "If the shutter stays locked for 10s with no dialog, movement or shootout in progress, clear the lock so the shop can be closed again.");
            CfgKeyPanel = Config.Bind("Rewind", "KeyTogglePanel", KeyCode.F8,
                "Show / hide the checkpoint panel.");

            Hotkeys = new List<HotkeyBinding>
            {
                new HotkeyBinding("prev", "回到本顾客到店", CfgKeyPrev, KeyCode.F6),
                new HotkeyBinding("day", "回到本日开业前", CfgKeyDay, KeyCode.F7),
                new HotkeyBinding("prevday", "回到上一天", CfgKeyPrevDay, KeyCode.F5),
                new HotkeyBinding("panel", "显示 / 隐藏面板", CfgKeyPanel, KeyCode.F8),
            };

            CfgRestoreMode = Config.Bind("Rewind", "RestoreMode", "menu",
                "How to apply a checkpoint: menu | auto | loadgame | scene. 'menu' (default) writes the slot, returns to the main menu and auto-loads it - the only path proven to work in this build.");
            CfgAllowOpen = Config.Bind("Rewind", "AllowOpenRestore", false,
                "Allow restoring checkpoints taken while the shop is OPEN. DISABLED by default: the game does not save the current client, so rebuilding the queue can spawn a broken (textureless) customer.");
            CfgQueueRebuild = Config.Bind("Rewind", "QueueRebuild", false,
                "After restoring, re-create waiting clients from our saved ids. DISABLED by default - it produced a broken first customer in testing.");
            CfgCaptureMidDay = Config.Bind("Rewind", "CaptureMidDay", false,
                "Also record checkpoints while the shop is open. Off = only one checkpoint per day (before opening), which is what the game's save format actually supports.");
            CfgAutoOpenAfterRestore = Config.Bind("Rewind", "AutoOpenAfterRestore", true,
                "After restoring a mid-day checkpoint, automatically open the shutter so the day can continue.");

            CfgDiag = Config.Bind("Debug", "Diagnostics", false,
                "Run one-shot API diagnostics at the main menu (log only).");
            CfgSelfTest = Config.Bind("Debug", "SelfTest", false,
                "Automated end-to-end test: continue save -> capture checkpoint -> restore. Logs to PSTools.log.");
            CfgPreviewPanel = Config.Bind("Debug", "PreviewPanel", false,
                "Show the checkpoint panel at the main menu with fake rows (UI preview only).");
            CfgHistory = Config.Bind("History", "Enabled", true,
                "Observation-only business event log (no save writes, no state changes). Writes PSTools_history.log.");
            CfgCustomerCheckpoint = Config.Bind("CustomerCheckpoint", "Enabled", true,
                "Keep the latest 10 customer checkpoints within the current day. F6 resets the current customer; F7 keeps day-start restore.");
            CfgPanelX = Config.Bind("Panel", "X", 16f, "Panel left position in reference canvas units.");
            CfgPanelY = Config.Bind("Panel", "Y", 16f, "Panel top position in reference canvas units.");
            CfgPanelWidth = Config.Bind("Panel", "Width", 480f, "Panel width in reference canvas units.");
            CfgPanelHeight = Config.Bind("Panel", "Height", 400f, "Expanded panel height in reference canvas units.");
            CfgPanelFolded = Config.Bind("Panel", "Folded", false, "Remember panel collapsed state.");
            Config.SaveOnConfigSet = true;
            Instance.Config.Save();

            LogSource.LogInfo($"Probably Stolen Tools {Version} loading...");
            LogSource.LogInfo($"persistentDataPath = {Application.persistentDataPath}");

            try
            {
#if !MONO
                var harmony = new HarmonyLib.Harmony(Guid);
                harmony.PatchAll(typeof(Plugin).Assembly);
#endif
                CustomerCheckpoint.PatchesReady = true;
                LogSource.LogInfo("Harmony patches applied.");
            }
            catch (Exception e)
            {
                LogSource.LogError($"Harmony patch failed: {e}");
            }

            try
            {
                // 真正的创建时机在场景里（见 OverlayBootstrap / Overlay.cs）。
                OverlayBootstrap.Ensure();
            }
            catch (Exception e)
            {
                LogSource.LogError($"Overlay bootstrap failed: {e}");
            }

            LogSource.LogInfo("Loaded.");
        }

        internal static bool Down(ConfigEntry<KeyCode> entry)
        {
            try
            {
                if (CheckpointPanel.SuppressHotkeys || entry.Value == KeyCode.None) return false;
                return Input.GetKeyDown(entry.Value);
            }
            catch { return false; }
        }

        /// <summary>
        /// 安全取 PlayerStore：**不要**在菜单期直接碰 PlayerStore.Instance，
        /// 它的 getter 会惰性 new 一个新实例（日志里会刷 “A new PlayerStore has been created”）。
        /// </summary>
        internal static PlayerStore Store()
        {
            try { return PlayerStore.IsInstanceExist() ? PlayerStore.Instance : null; }
            catch { return null; }
        }
    }
}
