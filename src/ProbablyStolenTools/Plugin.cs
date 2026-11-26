using System;
using System.Collections.Generic;
using HarmonyLib;
#if !MONO
using MelonLoader;
#endif
using UnityEngine;

#if !MONO
[assembly: MelonInfo(typeof(ProbablyStolenTools.Plugin), "QuickSL", "0.9.9", "Probably Stolen Tools contributors")]
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
        public const string Version = "0.9.9";

        internal static ModLogger LogSource;
        internal static Plugin Instance;
#if MONO
        internal static ModManifest Manifest;
#endif
        internal ModConfig Config { get; private set; }

        // ---- auto appraisal ----
        internal static ConfigEntry<bool> CfgAutoAppraisal;
        internal static ConfigEntry<bool> CfgAutoSubmit;
        internal static ConfigEntry<bool> CfgAlwaysOverwrite;
        internal static ConfigEntry<bool> CfgSkipCustomerDialogue;
        internal static ConfigEntry<bool> CfgDoubleClickInspection;
        internal static ConfigEntry<bool> CfgQuickInspection;
        internal static ConfigEntry<bool> CfgWaterQuality;
        internal static ConfigEntry<bool> CfgChemistry;
        internal static ConfigEntry<bool> CfgChemistryLabels, CfgContainerAutoRename;
        internal static ConfigEntry<bool> CfgCompositionCurrent, CfgCompositionExpected, CfgCompositionGrade;
        internal static ConfigEntry<bool> CfgPipeComposition, CfgShiftComposition, CfgUnlockMachineModules;
        internal static ConfigEntry<bool> CfgWindowSnap, CfgWindowPin;
        internal static ConfigEntry<bool> CfgChemistryExtendedContainers, CfgEmptyContainerNames;
        internal static ConfigEntry<bool> CfgAutoBlend, CfgAutoExtraction, CfgRapidReactions, CfgFacilityPreview, CfgRapidBoiling, CfgSmartExtrusion;
        internal static ConfigEntry<bool> CfgWineAppraisal;
        internal static ConfigEntry<float> CfgInspectionX, CfgInspectionY;

        // ---- rewind / checkpoints ----
        internal static ConfigEntry<bool> CfgRewind;
        internal static ConfigEntry<int> CfgMaxPoints;
        internal static ConfigEntry<int> CfgMaxDiskMb;
        internal static ConfigEntry<int> CfgMaxObjects, CfgMaxSlots, CfgMaxBytesMb;
        internal static ConfigEntry<bool> CfgSemanticTagShadow;
        internal static ConfigEntry<bool> CfgSemanticTagBlocks;
        internal static ConfigEntry<bool> CfgOwnerReconcile;
        internal static ConfigEntry<bool> CfgIntArrayBlocks;
        internal static ConfigEntry<bool> CfgSemanticTagCensus;
        internal static ConfigEntry<bool> CfgSkipUnchangedScalars;
        internal static ConfigEntry<bool> CfgTypedScalarComparisons;
        internal static ConfigEntry<bool> CfgDirectReferenceComparisons;
        internal static ConfigEntry<bool> CfgSemanticEntityAudit;
        internal static ConfigEntry<bool> CfgSemanticEntityRecords;
        internal static ConfigEntry<bool> CfgSemanticShapeRestore;
        internal static ConfigEntry<bool> CfgSemanticWindowRestore;
        internal static ConfigEntry<bool> CfgSemanticItemRestore;
        internal static ConfigEntry<bool> CfgSemanticInventoryRestore;
        internal static ConfigEntry<bool> CfgSemanticRuntimeRestore;
        internal static ConfigEntry<bool> CfgSemanticManualSample;
        internal static ConfigEntry<bool> CfgSemanticEntityBlocks;
        internal static ConfigEntry<bool> CfgSemanticDevelopmentChecks;
        internal static ConfigEntry<KeyCode> CfgKeyPrev;
        internal static ConfigEntry<KeyCode> CfgKeyNewer, CfgKeyNewerAlt, CfgKeyOlder, CfgKeyOlderAlt, CfgKeyRewindConfirm;
        internal static ConfigEntry<KeyCode> CfgKeyManual;
        internal static ConfigEntry<KeyCode> CfgKeyDay;
        internal static ConfigEntry<KeyCode> CfgKeyPrevDay;
        internal static ConfigEntry<KeyCode> CfgKeyQuickReload, CfgKeyReroll;
        internal static ConfigEntry<KeyCode> CfgKeyDetails;
        internal static ConfigEntry<string> CfgUpdateSeen;
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
                case KeyCode.UpArrow: return "↑";
                case KeyCode.DownArrow: return "↓";
                case KeyCode.LeftShift: return "L-Shift";
                case KeyCode.RightShift: return "R-Shift";
                case KeyCode.LeftControl: return "L-Ctrl";
                case KeyCode.RightControl: return "R-Ctrl";
                case KeyCode.LeftAlt: return "L-Alt";
                case KeyCode.RightAlt: return "R-Alt";
                case KeyCode.BackQuote: return "`";
                case KeyCode.Space: return "Space";
                case KeyCode.Mouse2: return InspectionLocale.Text("鼠标中键", "Mouse middle");
                case KeyCode.Mouse3: return InspectionLocale.Text("鼠标侧键1", "Mouse side 1");
                case KeyCode.Mouse4: return InspectionLocale.Text("鼠标侧键2", "Mouse side 2");
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
                if (binding.Config.Value == key || (target == CfgKeyDetails || binding.Config == CfgKeyDetails)
                    && IsShift(key) && IsShift(binding.Config.Value)) clash = binding;
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

        /// <summary>设置页的“查看日志”：在游戏内打开日志窗口，同时把路径复制到剪贴板。</summary>
        /// <summary>游戏开发工具（F8 控制台 / F9 开发菜单）是否启用。</summary>
        internal static bool DevToolsPresent()
        {
            try
            {
                return System.IO.File.Exists(System.IO.Path.Combine(Application.dataPath, "..", "dev.txt"));
            }
            catch { return false; }
        }
        internal static void OpenLog()
        {
            string path = EventLog.Path;
            try { GUIUtility.systemCopyBuffer = path; } catch { }
            LogWindow.Toggle();
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
                if (binding.Config != except && (binding.Config.Value == key
                    || (except == CfgKeyDetails || binding.Config == CfgKeyDetails) && IsShift(key) && IsShift(binding.Config.Value))) return true;
            return false;
        }

        private static bool IsShift(KeyCode key) => key == KeyCode.LeftShift || key == KeyCode.RightShift;


        // ---- diagnostics ----
        internal static ConfigEntry<bool> CfgDiag;
        internal static ConfigEntry<bool> CfgVerboseLog;
        internal static ConfigEntry<bool> CfgSelfTest;
        internal static ConfigEntry<bool> CfgPreviewPanel;
        internal static ConfigEntry<bool> CfgHistory;
        internal static ConfigEntry<bool> CfgCustomerCheckpoint;
        internal static ConfigEntry<bool> CfgDisableMemoryCheckpoints, CfgWholeDayMemory;
        internal static ConfigEntry<string> CfgHandledMemoryConflicts, CfgNotifiedMemoryConflicts;
        internal static ConfigEntry<bool> CfgAutoCheckpoints;
        internal static ConfigEntry<bool> CfgKeepPreviousDay;
        internal static ConfigEntry<float> CfgPanelX, CfgPanelY, CfgPanelWidth, CfgPanelHeight;
        internal static ConfigEntry<bool> CfgPanelFolded;

#if MONO
        public void Init(ModManifest manifest)
        {
#if MONO
            Manifest = manifest;
#endif
        }
        public void OnEnable() => Initialize();
        public void OnDisable()
        {
            NativeSnapshotSettings.Disable();
            ChemistryAutomation.Reset();
            TooltipHooks.Disable();
            UpdateNotice.Disable();
            NativeWindowGestures.Disable();
            PinnedCompositionTooltip.Disable();
            ForcedItemTooltip.Clear();
            OverlayBootstrap.Release();
        }
        private void Initialize()
#else
        public override void OnInitializeMelon()
#endif
        {
            Instance = this;
            LogSource = new ModLogger();
#if MONO
            // 指南要求：日志走游戏的 ModLog，控制台里会带 [模组名] 前缀。
            if (Manifest != null)
            {
                try { ModLogger.Log = new ModLog(Manifest); } catch { }
            }
            LogSource.LogInfo("QuickSL " + Version + " loaded on game " + Application.version);
            if (DevToolsPresent())
            {
                LogSource.LogWarning("dev.txt found: the game's F8 console and F9 dev menu share our default keys. " +
                    "Rebind the panel / manual checkpoint keys in the settings if they clash.");
            }
#endif
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
            CfgWaterQuality = Config.Bind("WaterQuality", "Enabled", true,
                "Show water-quality names and mismatch highlights on liquid containers.");
            CfgWaterQuality.SettingChanged += (_, _) => WaterQualityPatch.SettingsChanged();
            CfgPipeComposition = Config.Bind("WaterQuality", "PipeComposition", true,
                "Hold the item-details key to show all nonzero tap-water constituents using today's native purity and distribution, without drawing random numbers.");
            CfgShiftComposition = Config.Bind("Chemistry", "ShiftComposition", true,
                "Hold the item-details key over a liquid container to replace its tooltip with read-only composition details.");
            CfgUnlockMachineModules = Config.Bind("Machines", "UnlockWorkingModules", false,
                "Bypass running-machine module-bay locks only. Does not change lock fields, work tags or ingredient/battery locks.");
            CfgWindowSnap = Config.Bind("Windows", "ShiftSnap", true,
                "Hold either physical Shift key while dragging native movable windows to align adjacent edges.");
            CfgWindowPin = Config.Bind("Windows", "ShiftPin", true,
                "Shift-middle-click a native movable window title/background to toggle pinning. Disabled while holding the left button or dragging; most recently pinned stays highest.");
            CfgChemistry = Config.Bind("Chemistry", "Enabled", true,
                "Master switch for chemistry annotations, tooltip naming, automatic blending, rapid extraction, rapid chemical reactions and facility previews.");
            CfgChemistry.SettingChanged += (_, _) => WaterQualityPatch.SettingsChanged();
            CfgChemistryLabels = Config.Bind("Chemistry", "CompositionLabels", true,
                "Show chemical composition, share, purity and grade annotations.");
            CfgChemistryLabels.SettingChanged += (_, _) => WaterQualityPatch.SettingsChanged();
            CfgCompositionCurrent = Config.Bind("Chemistry", "ShowCurrentShare", true,
                "Include current percentage in chemistry composition labels.");
            CfgCompositionExpected = Config.Bind("Chemistry", "ShowExpectedPurity", false,
                "Include predicted purified percentage in chemistry composition labels.");
            CfgCompositionGrade = Config.Bind("Chemistry", "ShowGrade", false,
                "Include predicted grade in chemistry composition labels.");
            CfgCompositionCurrent.SettingChanged += (_, _) => WaterQualityPatch.SettingsChanged();
            CfgCompositionExpected.SettingChanged += (_, _) => WaterQualityPatch.SettingsChanged();
            CfgCompositionGrade.SettingChanged += (_, _) => WaterQualityPatch.SettingsChanged();
            CfgContainerAutoRename = Config.Bind("Chemistry", "ContainerAutoRename", true,
                "Rename liquid containers and seeded hydroponic machines in tooltips only.");
            CfgChemistryExtendedContainers = Config.Bind("Chemistry", "ExtendedContainers", true,
                "Include containers from 150 mL with no upper capacity limit. Droppers and measuring vessels remain excluded from component naming.");
            CfgChemistryExtendedContainers.SettingChanged += (_, _) => WaterQualityPatch.SettingsChanged();
            CfgEmptyContainerNames = Config.Bind("Chemistry", "EmptyContainerNames", true,
                "Distinguish empty micro bottles, water pitchers, jugs and variable-capacity water tanks in tooltips.");
            CfgAutoBlend = Config.Bind("Chemistry", "AutoBlend", true,
                "Blend compatible ingredients after player placement. Mixed-product batches leave incompatible ingredients at their source; whole-item outputs must fit.");
            CfgSmartExtrusion = Config.Bind("Chemistry", "SmartExtrusion", true,
                "Select the chemical finisher product when an inserted container has at least 70% of one matching recipe ingredient. Does not start production.");
            CfgAutoExtraction = Config.Bind("Chemistry", "AutoExtraction", false,
                "Complete alcohol extraction of mushroom and coffee mush. Volume may exceed capacity; independent of RapidReactions.");
            CfgRapidReactions = Config.Bind("Chemistry", "RapidReactions", false,
                "Complete the nine supported non-thermal reagent reactions. Independent of AutoExtraction; preserve native conditions, bounded progress and thermal rates.");
            CfgRapidBoiling = Config.Bind("Chemistry", "RapidBoiling", false,
                "On a hotplate only, fully remove lipobond agent, alcohol, water and microbes once their native temperature conditions are met. Other thermal reactions are unchanged.");
            CfgFacilityPreview = Config.Bind("Chemistry", "FacilityPreview", true,
                "Show read-only next-cycle projections for aquaculture, purifiers, hydroponics and evaporators.");
            CfgWineAppraisal = Config.Bind("Wine", "Enabled", true,
                "Show glowberry condition and unfermented-bottle quality annotations.");
            CfgAutoAppraisal.SettingChanged += (_, _) => AppraisalPatch.Reset();
            CfgAutoSubmit.SettingChanged += (_, _) => AppraisalPatch.Reset();
            CfgAlwaysOverwrite.SettingChanged += (_, _) => AppraisalPatch.Reset();

            CfgRewind = Config.Bind("Rewind", "Enabled", true,
                "Record a checkpoint before opening the shop and whenever a new client arrives.");
            CfgMaxPoints = Config.Bind("Rewind", "MaxCheckpoints", 20,
                "Maximum file checkpoints across sessions. In-memory customer history keeps 20 nodes.");
            CfgMaxDiskMb = Config.Bind("Rewind", "MaxDiskMB", 256,
                "Total file-checkpoint budget in MiB. Only timestamped checkpoints in PSTools_Checkpoints are cleaned, least recently used first.");
            CfgMaxObjects = Config.Bind("Snapshot", "MaxObjects", 200000,
                "Snapshot object limit. Raise it if a heavily modded game fails with 'Snapshot object limit exceeded'.");
            CfgMaxSlots = Config.Bind("Snapshot", "MaxSlots", 3000000,
                "Snapshot field-slot limit. Raise it if capture fails with 'Snapshot slot limit exceeded'.");
            CfgMaxBytesMb = Config.Bind("Snapshot", "MaxBytesMB", 256,
                "Snapshot byte budget in MiB.");
            CfgSemanticTagShadow = Config.Bind("Snapshot", "SemanticTagShadow", true,
                "Experimental read-only tag comparison on one checkpoint per context. StateGraph remains authoritative; adds diagnostic capture/verification work, not a speedup.");
            CfgSemanticTagBlocks = Config.Bind("Snapshot", "SemanticTagBlocks", true,
                "Experimental lossless exact TagState field compression. Preserves identities, dictionary storage and full verification. Disable to capture new checkpoints with legacy field tokens.");
            CfgOwnerReconcile = Config.Bind("Snapshot", "OwnerReconcile", true,
                "Experimental synchronous second-pass repair of complete changed owners after a full scan. Falls back to full restore for unsupported or large repairs; final verification remains mandatory. Disable for legacy full second-pass writes.");
            CfgIntArrayBlocks = Config.Bind("Snapshot", "IntArrayBlocks", true,
                "Experimental lossless int[] payloads using complete array copies. Preserves original array identities, capacity, logical budgets and full element verification. Disable and restart for legacy per-element tokens.");
            CfgSemanticTagCensus = Config.Bind("Snapshot", "SemanticTagCensus", true,
                "Read exact native TagState scalar fields together during read-only pre-restore census. Keeps all fields, references and budgets; disable for legacy per-field census reads.");
            CfgSkipUnchangedScalars = Config.Bind("Snapshot", "SkipUnchangedScalars", true,
                "Compare ordinary framework scalar fields before writing; skip only exact unchanged values. Keeps full graph scans and final verification. Disable and restart for unconditional scalar writes.");
            CfgTypedScalarComparisons = Config.Bind("Snapshot", "TypedScalarComparisons", true,
                "Compile read-only hot scalar comparers to avoid boxing during full verification and unchanged-value checks. Keeps exact float bits and reflection fallback. Disable and restart for boxed comparisons.");
            CfgDirectReferenceComparisons = Config.Bind("Snapshot", "DirectReferenceComparisons", true,
                "Compare ordinary live references directly with retained checkpoint objects by identity. Keeps full scans and identity validation. Disable and restart for global ID comparisons.");
            CfgSemanticEntityAudit = Config.Bind("Snapshot", "SemanticEntityAudit", true,
                "Read-only item/inventory boundary audit on one checkpoint per context. Adds diagnostic work, not a speedup; never prunes the graph or writes entity state.");
            CfgSemanticEntityRecords = Config.Bind("Snapshot", "SemanticEntityRecords", true,
                "Prepare independent entity/runtime payloads on the primary sample and optionally one later manual sample per context. Read-only unless writer pilots are enabled; adds diagnostic cost and does not replace production capture.");
            CfgSemanticShapeRestore = Config.Bind("Snapshot", "SemanticShapeRestore", true,
                "Pilot exact GridShapeBuilder entity writes on the sampled entity checkpoint only. Keeps the full old graph, full verification, normal reconciliation and visual ordering. Disable for original first writes.");
            CfgSemanticWindowRestore = Config.Bind("Snapshot", "SemanticWindowRestore", true,
                "Pilot exact PixelWindow business-field writes on sampled checkpoints only. Callback/runtime storage uses the old graph or the paired runtime pilot; full verification and visual ordering are unchanged.");
            CfgSemanticItemRestore = Config.Bind("Snapshot", "SemanticItemRestore", true,
                "Pilot exact GameItemElement business-field writes on sampled checkpoints only. Keeps original identities and physical callback/collection storage, full verification and visual ordering.");
            CfgSemanticInventoryRestore = Config.Bind("Snapshot", "SemanticInventoryRestore", true,
                "Pilot exact grid and slot inventory business-field writes as one atomic group on sampled checkpoints. Keeps physical callbacks, caches, tags and collection storage via the old graph or paired runtime pilot, with full verification.");
            CfgSemanticRuntimeRestore = Config.Bind("Snapshot", "SemanticRuntimeRestore", true,
                "Pilot independent runtime storage writes only with a complete enabled entity partition on the sampled checkpoint. Keeps the full old capture, verification, repair and visual sequence.");
            CfgSemanticManualSample = Config.Bind("Snapshot", "SemanticManualSample", true,
                "Prepare one additional entity/runtime sample on the first later manual checkpoint per context. At most two attempts; retains the full old graph and diagnostics for independent past/future testing.");
            CfgSemanticEntityBlocks = Config.Bind("Snapshot", "SemanticEntityBlocks", true,
                "Single-pass exact entity payloads with original identities and complete Runtime/collection storage. Keeps full validation and owner/full repair. Disable and restart for the previous ordinary capture backend.");
            CfgSemanticDevelopmentChecks = Config.Bind("Snapshot", "SemanticDevelopmentChecks", false,
                "Development-only duplicate capture/verification. With entity blocks, retains an independent old graph on the primary and optional later manual sample. Without blocks, allows the earlier shadow/audit/writer pilots. Disable and restart after acceptance to remove diagnostic overhead; mandatory safety checks remain enabled.");
            CfgKeyManual = Config.Bind("Rewind", "KeyManualCheckpoint", KeyCode.F9,
                "Create a checkpoint immediately, without waiting for a customer.");
            CfgKeyPrev = Config.Bind("Rewind", "KeyPrevCheckpoint", KeyCode.F6,
                "Select the next older checkpoint in panel order; use navigation keys and confirm to rewind.");
            CfgKeyNewer = Config.Bind("Rewind", "KeyNewerCheckpoint", KeyCode.UpArrow, "Select one newer checkpoint in the chooser.");
            CfgKeyNewerAlt = Config.Bind("Rewind", "KeyNewerCheckpointAlt", KeyCode.W, "Alternative newer checkpoint key.");
            CfgKeyOlder = Config.Bind("Rewind", "KeyOlderCheckpoint", KeyCode.DownArrow, "Select one older checkpoint in the chooser.");
            CfgKeyOlderAlt = Config.Bind("Rewind", "KeyOlderCheckpointAlt", KeyCode.S, "Alternative older checkpoint key.");
            CfgKeyRewindConfirm = Config.Bind("Rewind", "KeyConfirmCheckpoint", KeyCode.Return, "Confirm the selected checkpoint.");
            CfgKeyDay = Config.Bind("Rewind", "KeyDayStart", KeyCode.F7,
                "Rewind to the start of the current day.");
            CfgKeyPrevDay = Config.Bind("Rewind", "KeyPreviousDay", KeyCode.F5,
                "回到上一天 / back to previous day");
            CfgKeyQuickReload = Config.Bind("Rewind", "KeyQuickReload", KeyCode.None,
                "Reload the active native save through the menu, discarding unsaved changes and memory checkpoints. Unbound by default.");
            CfgKeyReroll = Config.Bind("Rewind", "KeyRerollRandom", KeyCode.None,
                "Reseed the game and Unity random streams for future rolls, without reloading or regenerating existing content. Unbound by default.");
            CfgConfirmWholeDay = Config.Bind("Rewind", "ConfirmWholeDayRewind", true,
                "Ask for confirmation before whole-day rewinds and quick file reloads. File reloads discard unsaved progress and memory checkpoints; the active save is not overwritten.");
            CfgRepairStuckShutter = Config.Bind("Rewind", "RepairStuckShutter", true,
                "If the shutter stays locked for 10s with no dialog, movement or shootout in progress, clear the lock so the shop can be closed again.");
            CfgKeyPanel = Config.Bind("Rewind", "KeyTogglePanel", KeyCode.F8,
                "Show / hide the checkpoint panel.");
            CfgKeyDetails = Config.Bind("Inspection", "KeyItemDetails", KeyCode.LeftShift,
                "Hold to show item composition and machine projections. Wine annotations are always visible when enabled. Either Shift works when bound to Shift; None disables details.");
            CfgUpdateSeen = Config.Bind("Updates", "LastShownVersion", "",
                "QuickSL version whose update banner has already been displayed, independent of whether it was clicked.");

            Hotkeys = new List<HotkeyBinding>
            {
                new HotkeyBinding("manual", "立即建立节点", CfgKeyManual, KeyCode.F9),
                new HotkeyBinding("prev", "选择上一个节点", CfgKeyPrev, KeyCode.F6),
                new HotkeyBinding("day", "回到本日开业前", CfgKeyDay, KeyCode.F7),
                new HotkeyBinding("prevday", "回到上一天", CfgKeyPrevDay, KeyCode.F5),
                new HotkeyBinding("reload", "快速重载存档", CfgKeyQuickReload, KeyCode.None),
                new HotkeyBinding("reroll", "重掷随机数", CfgKeyReroll, KeyCode.None),
                new HotkeyBinding("panel", "显示 / 隐藏面板", CfgKeyPanel, KeyCode.F8),
                new HotkeyBinding("details", "展示物品详细信息", CfgKeyDetails, KeyCode.LeftShift),
                new HotkeyBinding("newer", "选择较新节点", CfgKeyNewer, KeyCode.UpArrow),
                new HotkeyBinding("newerAlt", "选择较新节点（备用）", CfgKeyNewerAlt, KeyCode.W),
                new HotkeyBinding("older", "选择较旧节点", CfgKeyOlder, KeyCode.DownArrow),
                new HotkeyBinding("olderAlt", "选择较旧节点（备用）", CfgKeyOlderAlt, KeyCode.S),
                new HotkeyBinding("confirmNode", "确认节点回溯", CfgKeyRewindConfirm, KeyCode.Return),
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
            CfgVerboseLog = Config.Bind("Logging", "Verbose", false,
                "Enable detailed wait, visual, geometry, bargain and input diagnostics. Errors, capture/restore timings and real repair fallbacks are always logged. Does not enable duplicate semantic development checks. PSTools logs rotate at 4 MiB with three archives.");
            CfgSelfTest = Config.Bind("Debug", "SelfTest", false,
                "Automated end-to-end test: continue save -> capture checkpoint -> restore. Logs to PSTools.log.");
            CfgPreviewPanel = Config.Bind("Debug", "PreviewPanel", false,
                "Show the checkpoint panel at the main menu with fake rows (UI preview only).");
            CfgHistory = Config.Bind("History", "Enabled", true,
                "Observation-only business event log (no save writes, no state changes). Writes PSTools_history.log.");
            CfgCustomerCheckpoint = Config.Bind("CustomerCheckpoint", "Enabled", true,
                "Keep the latest 20 customer/manual checkpoints per retained day, plus one morning anchor. F6 opens checkpoint selection.");
            CfgDisableMemoryCheckpoints = Config.Bind("CustomerCheckpoint", "DisableMemory", false,
                "Disable all memory checkpoint capture and restore. A newly detected blacklisted mod turns this on once; turning it off manually accepts unsupported conflict risks.");
            CfgHandledMemoryConflicts = Config.Bind("Compatibility", "HandledMemoryConflicts", "",
                "Mod IDs whose one-time memory-disable default has already been applied. Preserve this value to retain a manual override across restarts.");
            CfgNotifiedMemoryConflicts = Config.Bind("Compatibility", "NotifiedMemoryConflicts", "",
                "Mod IDs whose conflict notice has already been displayed. Each conflicting mod is shown only once across restarts.");
            CfgWholeDayMemory = Config.Bind("Rewind", "WholeDayUseMemory", true,
                "Prefer memory morning anchors for whole-day rewind when automatic capture is enabled. Off uses disk saves; individual memory nodes remain available.");
            CfgAutoCheckpoints = Config.Bind("CustomerCheckpoint", "AutoCapture", true,
                "Automatically capture morning, customer, closed-shop and return checkpoints (default on). Manual capture remains available when disabled; F5/F7 use file reloads. Native save backup copies remain available for previous-day reloads.");
            CfgKeepPreviousDay = Config.Bind("CustomerCheckpoint", "KeepPreviousDay", true,
                "Keep yesterday's checkpoints across normal sleep. F5/F7 prefer morning memory anchors to preserve history; scene changes and actual file reloads still expire object graphs.");
            CfgPanelX = Config.Bind("Panel", "X", 16f, "Panel left position in reference canvas units.");
            CfgPanelY = Config.Bind("Panel", "Y", 16f, "Panel top position in reference canvas units.");
            CfgPanelWidth = Config.Bind("Panel", "Width", 480f, "Panel width in reference canvas units.");
            CfgPanelHeight = Config.Bind("Panel", "Height", 400f, "Expanded panel height in reference canvas units.");
            CfgPanelFolded = Config.Bind("Panel", "Folded", false, "Remember panel collapsed state.");
            Config.SaveOnConfigSet = true;
            Instance.Config.Save();

#if MONO
            try { NativeSnapshotSettings.Register(); }
            catch (Exception e) { EventLog.Write("[Settings] native snapshot settings unavailable; XML retained: " + e); }
#endif
            LogSource.LogInfo($"QuickSL {Version} loading...");
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

            TooltipHooks.Enable();
#if STATIC_SNAPSHOT_LAYOUTS
            const string layoutBackend = "static-token-v1";
#else
            const string layoutBackend = "runtime-reflection";
#endif
            EventLog.Write($"[Session] version={Version} game={Application.version} locale=deferred " +
                $"layoutBackend={layoutBackend} " +
                $"snapshotLimits(objects={CfgMaxObjects.Value},slots={CfgMaxSlots.Value},MiB={CfgMaxBytesMb.Value}) " +
                $"verbose={CfgVerboseLog.Value} semanticTagShadow={CfgSemanticTagShadow.Value} semanticTagBlocks={CfgSemanticTagBlocks.Value} ownerReconcile={CfgOwnerReconcile.Value} intArrayBlocks={CfgIntArrayBlocks.Value} semanticTagCensus={CfgSemanticTagCensus.Value} skipUnchangedScalars={CfgSkipUnchangedScalars.Value} typedScalarComparisons={CfgTypedScalarComparisons.Value} directReferenceComparisons={CfgDirectReferenceComparisons.Value} semanticEntityAudit={CfgSemanticEntityAudit.Value} semanticEntityRecords={CfgSemanticEntityRecords.Value} semanticShapeRestore={CfgSemanticShapeRestore.Value}; " +
                $"semanticWindowRestore={CfgSemanticWindowRestore.Value} semanticItemRestore={CfgSemanticItemRestore.Value} semanticInventoryRestore={CfgSemanticInventoryRestore.Value} semanticRuntimeRestore={CfgSemanticRuntimeRestore.Value} semanticManualSample={CfgSemanticManualSample.Value} semanticEntityBlocks={CfgSemanticEntityBlocks.Value} semanticDevelopmentChecks={CfgSemanticDevelopmentChecks.Value}; arrivalWaitSeconds is not capture duration; use captureMs");
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
