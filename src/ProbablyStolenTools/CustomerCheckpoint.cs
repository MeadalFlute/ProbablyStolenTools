using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using HarmonyLib;
#if !MONO
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProbablyStolenTools
{
    internal static class CustomerCheckpoint
    {
        private static readonly CustomerBoundary Boundary = new();
        private static readonly CheckpointHistory<Snapshot> History = new(20, node => !node.IsClosed);
        private static Snapshot _active;
        internal static Snapshot ClosedNode { get; private set; }
        private static bool _closeArmed, _outside;
        private static float _arrivedAt;
        private static float _closeAfter, _settledSince;
        private static long _nextId;
        private static readonly Dictionary<IntPtr, GameItemElement> RetainedItems = new();
        private static readonly Dictionary<IntPtr, GameItemElement> Deleted = new();
        private static readonly HashSet<long> PendingCallbacks = new();
        private static readonly HashSet<long> DialogueCallbacks = new();
        private static bool _armed;
        private static bool _faulted;
        private static long _epoch;
        private static long _callbackId;
        private static Snapshot _visualProbe;
        private static int _visualProbeFrame;
        private static long _visualProbeEpoch;
#if MONO
        private static bool _semanticShadowAttempted;
        private static bool _entityBoundaryAttempted;
        private static readonly SemanticCheckpointSampling EntitySamples = new();
#endif
        internal static bool Restoring { get; private set; }
        internal static bool PatchesReady;
        internal static bool Enabled => Plugin.CfgCustomerCheckpoint?.Value == true && PatchesReady;
        internal static string Status { get; private set; } = "Waiting for a new customer";
        internal static IReadOnlyList<Snapshot> Nodes => History.Items;
        internal static int CustomerCount => Nodes.Count(node => !node.IsClosed);
        internal static bool IsFuture(Snapshot node) => History.IsFuture(node);
        private static int _failureRevision;
        private static string _failureNoticeKey;
        internal static string LastCaptureError { get; private set; }
        internal static int Revision => unchecked(History.Revision + _failureRevision);
        internal static long ActiveId => _active?.Id ?? 0;

        /// <summary>一次捕获失败的记录：面板里以“损坏”节点显示，悬停看原因。</summary>
        internal sealed class Failure
        {
            internal string Label;
            internal string Reason;
            internal DateTime At;
            internal int Day;
        }

        internal static readonly List<Failure> Failures = new List<Failure>();

        private static void RecordFailure(bool closed, bool manual, Exception e)
        {
            try
            {
                string label = manual ? InspectionLocale.Text("手动", "Manual")
                    : closed ? InspectionLocale.Text("闭店", "Closed") : InspectionLocale.Text("顾客", "Customer");
                if (!closed && !manual)
                {
                    try
                    {
                        var client = Plugin.Store()?.currentClientInstance;
                        if (client != null) label = client.GetClientBlueprint().identifier ?? label;
                    }
                    catch { }
                }
                string reason = e == null ? "unknown" : e.GetType().Name + ": " + (e.Message ?? "").Trim();
                if (reason.Length > 400) reason = reason.Substring(0, 400) + "…";
                Failures.Insert(0, new Failure { Label = label, Reason = reason, At = DateTime.Now, Day = Boundary.Day });
                LastCaptureError = reason;
                _failureRevision++;
                string noticeKey = e is SnapshotLimitException ? "snapshot-limit" : reason;
                if (manual || noticeKey != _failureNoticeKey)
                    PanelNotice.Show(InspectionLocale.Text("节点建立失败，已保留旧节点：", "Checkpoint skipped; previous checkpoints retained: ")
                        + label + "\n" + InspectionLocale.Text("详情请查看日志", "See the log for details"), true);
                _failureNoticeKey = noticeKey;
                while (Failures.Count > 5) Failures.RemoveAt(Failures.Count - 1);
            }
            catch { }
        }
        internal sealed class Snapshot : IDisposable
        {
            internal long Id;
            internal string Label;
            internal string DisplayName;
            internal bool IsClosed;
            internal bool IsManual;
            internal int StoreState;
            internal string ExpiredReason;
            internal int Day, Cash, Scene;
            internal IntPtr Store, Client;
            internal DateTime At;
            internal StateGraph Graph;
#if MONO
            internal SemanticTagShadow SemanticTags;
            internal SemanticEntityShadow SemanticEntities;
            internal SemanticCaptureOracle BlockOracle;
            internal int EntityBlockOwners, EntityBlockSlots, EntityRuntimeSlots;
#endif
            internal readonly Dictionary<IntPtr, GameItemElement> Items = new();
            internal readonly Dictionary<IntPtr, GameItemElement> RenderedItems = new();
            internal readonly Dictionary<IntPtr, PixelWindow> Windows = new();
            internal readonly List<VisualState> Visuals = new();
            internal Il2CppSystem.Random Random;
            internal UnityRandomState.State UnityRandom;
            internal CustomerVisual Customer;
            internal ShopPhaseVisual Shop;
            public void Dispose()
            {
                Graph?.Dispose(); Graph = null;
#if MONO
                SemanticTags?.Dispose(); SemanticTags = null;
                SemanticEntities?.Dispose(); SemanticEntities = null;
                BlockOracle?.Dispose(); BlockOracle = null;
#endif
                Items.Clear(); RenderedItems.Clear(); Windows.Clear(); Visuals.Clear();
                Random = null; Customer = null; Shop = null;
            }
        }

        internal static void Arrival()
        {
            if (!Enabled || Restoring || Rewind.Restoring || _faulted) return;
            Observe();
            if (Boundary.Client == IntPtr.Zero || Boundary.Attempted) return;
            var intent = Plugin.Store().currentClientInstance.GetClientBlueprint().clientIntent;
            if (!DialogueSkip.Ordinary(intent))
            {
                // 对话/剧情类顾客不建立节点，但不能连累当天已经存在的节点：
                // 之前这里走 Invalidate() → Clear()，一来剧情顾客就把整天的节点清空了，
                // 表现就是“突然没有节点可用”。
                _armed = false;
                Boundary.MarkAttempt();
                Status = "Unsupported customer type: " + intent;
                EventLog.Write("[Customer] unsupported customer type " + intent
                    + ": no checkpoint for this client, existing nodes kept.");
                return;
            }
            _armed = true;
            _captureSince = 0;
            SettleLog.Reset();
            _arrivedAt = Time.unscaledTime;
            Status = "Waiting for the customer's opening dialogue";
            TryCapture();
        }

        internal static void Tick()
        {
            if (Restoring) return;
            if (Plugin.Store() != null) InspectionLocale.LogState();
            Observe();
            if (_visualProbe != null && Time.frameCount >= _visualProbeFrame)
            {
                var probe = _visualProbe;
                _visualProbe = null;
                if (_visualProbeEpoch == _epoch && ReferenceEquals(probe, _active))
                    probe.Customer.Log(probe.Id, "next-frame");
            }
            if (!Enabled) { if (History.Items.Count > 0 || ClosedNode?.Graph != null) Clear("已禁用"); return; }
            if (_armed && Time.unscaledTime >= _nextCapturePoll)
            {
                _nextCapturePoll = Time.unscaledTime + 0.1f;
                TryCapture();
            }
            if (_closeArmed) TryCaptureClosed();
            ShutterWatchdog();
        }

        // ---------- 卷帘门锁看门狗 ----------
        // 正式版里闭店按钮的提示由 StoreShutterButton.GetTooltip 决定：
        // storeState == 1 且 lockShutterButtonFlag != 0 时显示“请稍候”。
        // 这个计数由游戏在对话/转场里自增自减，一旦某条中断路径漏了递减，
        // 玩家就会永远闭不了店。这里只在“没有任何正当理由”时兜底清零。
        private static float _shutterSince, _shutterLogAt;
        private static int _shutterRepairs;

        private static void ShutterWatchdog()
        {
            var store = Plugin.Store();
            int state = store == null ? -1 : (int)store.storeState;
            // 早市(2) 与营业(1) 都可能被残留锁挡住：早市时按卷帘门不会有任何反应。
            if (store == null || (state != 1 && state != 2) || store.lockShutterButtonFlag == 0)
            {
                _shutterSince = 0;
                return;
            }

            var dialog = DialogUIManager.Instance;
            var mono = StoreClientMono.Instance;
            var ui = StoreUIManager.Instance;
            bool legitimatelyBusy = (dialog != null && (dialog.currentDialog != null || dialog.isConversationActiveFlag != 0))
                || (mono != null && mono.IsMoving())
                || store.isHandlingShootout || store.isClientBeingArrested
                || (ui != null && (ui.isAboutToStartMainDialog || ui.isShootingMode))
                || ItemInteractionBusy()
                || GameFields.PhoneBusy(PhoneUIManager.Instance);
            if (legitimatelyBusy)
            {
                _shutterSince = 0;
                return;
            }

            if (_shutterSince == 0) { _shutterSince = Time.unscaledTime; return; }
            float held = Time.unscaledTime - _shutterSince;

            // 每 3 秒留一条证据，方便报告者直接把这个数字发回来。
            if (held >= 3f && Time.unscaledTime - _shutterLogAt >= 3f)
            {
                _shutterLogAt = Time.unscaledTime;
                EventLog.Write($"[Shutter] still locked lockShutterButtonFlag={store.lockShutterButtonFlag} " +
                               $"held={held:0.0}s state={(int)store.storeState} " +
                               $"client={(store.currentClientInstance != null ? 1 : 0)}");
            }

            if (held < (state == 2 ? 15f : 10f)) return;
            if (!Plugin.CfgRepairStuckShutter.Value) return;

            int before = store.lockShutterButtonFlag;
            store.lockShutterButtonFlag = 0;
            _shutterSince = 0;
            _shutterRepairs++;
            EventLog.Write($"[Shutter] cleared stale lock after {held:0.0}s (was {before}, repairs={_shutterRepairs})");
        }

        private static void Observe()
        {
            var store = Plugin.Store();
            var client = store?.currentClientInstance;
            int day = store == null ? -1 : StoreStation.GetDayCounter();
            int scene = SceneManager.GetActiveScene().handle;
            bool contextChanged = scene != Boundary.Scene || day != Boundary.Day || Ptr(store) != Boundary.Store;
            if (contextChanged)
            {
                string context = $"[Customer] CONTEXT CHANGED scene={Boundary.Scene}->{scene} day={Boundary.Day}->{day} " +
                    $"store={Boundary.Store}->{Ptr(store)} nodes={History.Items.Count} -> cleared";
                if (History.Items.Count > 0) EventLog.Write(context);
                else EventLog.Debug(context);
                ++_epoch;
                PendingCallbacks.Clear(); DialogueCallbacks.Clear();
                Clear();
                if (ClosedNode?.Day != day) ClosedNode = null;
                _outside = false;
                AppraisalPatch.Reset();
                _faulted = false;
            }
            if (Boundary.Observe(Ptr(store), Ptr(client), day, scene))
            {
                _active = null;
                _armed = false;
                Status = "Waiting for a new customer";
            }
        }

        internal static string Name(Snapshot node)
        {
            if (node.IsManual) return InspectionLocale.Text("手动", "Manual");
            if (node.IsClosed) return InspectionLocale.Text("闭店", "Closed");
            // displayName is the exact resolved name used by the game's dialogue/recorder.
            return string.IsNullOrWhiteSpace(node.DisplayName) ? InspectionLocale.Text("顾客", "Customer") : node.DisplayName;
        }

        internal static void Closing()
        {
            if (!Enabled || Restoring) return;
            Observe();
            _active = null;
            _armed = false;
            _closeArmed = true;
            _closeAfter = Time.unscaledTime + 1f;
            _settledSince = 0;
        }

        internal static void Leaving()
        {
            if (Restoring) return;
            Clear("已离店");
            _outside = true;
            Status = "Left the shop; in-memory checkpoints expired.";
        }

        internal static void Returned() { _outside = false; }

#if MONO
        internal static void Shutdown()
        {
            Clear("Mod disabled");
            SnapshotFieldReader.ClearCache();
            ClosedNode = null;
            PendingCallbacks.Clear();
            DialogueCallbacks.Clear();
            Boundary.Reset();
            ++_epoch;
        }
#endif

        /// <summary>
        /// F7 reloads the save through a menu scene while this overlay survives.
        /// All old object references belong to the previous runtime and must not
        /// be allowed to block the first customer of the newly loaded day.
        /// </summary>
        internal static void ResetAfterFileReload()
        {
            if (Restoring) return;
#if MONO
            _semanticShadowAttempted = false;
            _entityBoundaryAttempted = false;
            EntitySamples.Reset();
#endif
            Failures.Clear();
            LastCaptureError = null;
            _failureNoticeKey = null;
            _failureRevision++;
            _captureSince = 0;
            SettleLog.Reset();
            ReleaseAllSnapshotViews();
            History.Clear();
            if (ClosedNode != null)
            {
                ClosedNode.Dispose();
                ClosedNode = null;
            }
            RetainedItems.Clear();
            Deleted.Clear();
            _active = null;
            _armed = false;
            _closeArmed = false;
            _closeAfter = 0;
            _settledSince = 0;
            _outside = false;
            _faulted = false;
            ++_epoch;
            PendingCallbacks.Clear();
            DialogueCallbacks.Clear();
            Boundary.Reset();
            AppraisalPatch.Reset();
            Status = "Waiting for a new customer";
        }

        private static void TryCaptureClosed()
        {
            if (!_closeArmed || _outside || _faulted || Rewind.Restoring || Time.unscaledTime < _closeAfter) return;
            var store = Plugin.Store();
            var dialog = DialogUIManager.Instance;
            var mono = StoreClientMono.Instance;
            var shutter = StoreShutterButton.Instance;
            bool settled = store != null && (int)store.storeState == 0 && dialog != null
                && dialog.currentDialog == null && dialog.isConversationActiveFlag == 0
                && !dialog.isWaitingForManualAdvance && PendingCallbacks.Count == 0
                && shutter != null && GameFields.Get<bool>(shutter, "isClosed")
                && GameFields.Get<float>(shutter, "elapsedTime") >= GameFields.Get<float>(shutter, "duration")
                && mono != null && !mono.IsMoving() && !store.isHandlingShootout
                && !store.isClientBeingArrested
                && StoreUIManager.Instance != null && !StoreUIManager.Instance.isAboutToStartMainDialog
                && !ItemInteractionBusy()
                && !GameFields.PhoneBusy(PhoneUIManager.Instance);
            if (!settled) { _settledSince = 0; return; }
            if (_settledSince == 0) { _settledSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _settledSince < 0.5f) return;
            _closeArmed = false;
            Capture(store, true);
        }

        private static bool IsSettled(PlayerStore store)
        {
            var ui = StoreUIManager.Instance;
            var dialog = DialogUIManager.Instance;
            // 不再要求 !isAboutToStartMainDialog：那是“0.25 秒后开主对话”的瞬态标志，
            // 一旦游戏自己的延时回调没跑完（mod 环境里并不罕见），它会一直为 true，
            // 导致节点永远建不起来。真正要等的是“没有对话、顾客没在移动”。
            return store != null && store.currentClientInstance != null && store.isClientArrived
                && (int)store.storeState == 1 && !store.isClientBeingArrested && !store.isHandlingShootout
                && ui != null && !ui.isShootingMode
                && !ItemInteractionBusy()
                && StoreClientMono.Instance != null && !StoreClientMono.Instance.IsMoving()
                && dialog != null && dialog.currentDialog == null && dialog.isConversationActiveFlag == 0;
        }

        private static float _captureSince;
        private static float _nextCapturePoll;

        /// <summary>玩家手动建立节点（默认 F9）：复用顾客节点的捕获逻辑，不依赖是否有顾客在场。</summary>
        internal static void CaptureManual()
        {
            if (!Enabled) { PanelNotice.Show("无法建立节点：回溯功能已关闭", true); return; }
            if (Restoring || Rewind.Restoring || Rewind.Busy)
            { PanelNotice.Show("无法建立节点：正在恢复存档或节点", true); return; }
            if (_faulted) { PanelNotice.Show("无法建立节点：上次恢复失败，请重新载入游戏", true); return; }
            var store = Plugin.Store();
            if (store == null) { PanelNotice.Show("无法建立节点：商店数据未就绪", true); return; }
            if (ItemInteractionBusy())
            { PanelNotice.Show("无法建立节点：正在框选或拖拽物品", true); return; }
            if (Time.unscaledTime - _lastManualAt < 1.5f) return;
            _lastManualAt = Time.unscaledTime;
            Observe();
            bool closed = (int)store.storeState == 0;
            Capture(store, closed, true);
        }

        private static float _lastManualAt;
        internal static void TryCapture()
        {
            if (!_armed || !Enabled || Restoring || Rewind.Restoring || Boundary.Attempted || _faulted) return;
            var store = Plugin.Store();
            if (!IsSettled(store)) { _captureSince = 0; WatchSettle(store); return; }
            // 节点必须建立在“开场对话已经播完”之后：卖货顾客的商品是那段对话里才摆上柜台的，
            // 抢在对话前落盘，回溯后就会只剩一个空柜台（商品全部消失）。
            if (!OpeningDialogueDone(store) && Time.unscaledTime - _arrivedAt < 8f)
            {
                _captureSince = 0;
                WatchSettle(store);
                return;
            }
            // 判定条件需要连续稳定一小段时间，避免在到店动画/对话切换的那一帧落盘。
            if (_captureSince == 0) { _captureSince = Time.unscaledTime; return; }
            if (Time.unscaledTime - _captureSince < 0.25f) return;
            _captureSince = 0;
            Boundary.MarkAttempt();
            _armed = false;
            Capture(store, false);
        }

        private static bool OpeningDialogueDone(PlayerStore store)
        {
            try
            {
                var blueprint = store?.currentClientInstance?.GetClientBlueprint();
                return blueprint == null || blueprint.isMainDialogueStarted;
            }
            catch { return true; }
        }

        private static NativeStateAccess Access(PlayerStore store, out List<IntPtr> roots, bool entityBlocks = true)
        {
#if MONO
            var access = new NativeStateAccess(typeof(UnityEngine.Object), "Assembly-CSharp", true, true, true,
                Plugin.CfgIntArrayBlocks?.Value == true);
            access.SkipUnchangedScalars = Plugin.CfgSkipUnchangedScalars?.Value == true;
            access.TypedScalarComparisons = Plugin.CfgTypedScalarComparisons?.Value == true;
            access.DirectReferenceComparisons = Plugin.CfgDirectReferenceComparisons?.Value == true;
            if (Plugin.CfgSemanticTagBlocks?.Value == true) SemanticTagBlock.Enable(access);
            if (entityBlocks && Plugin.CfgSemanticEntityBlocks?.Value == true) SemanticEntityBlocks.Enable(access);
#else
            var access = new NativeStateAccess();
#endif
            var rootList = new List<IntPtr>();
            roots = rootList;
#if MONO
            void Root(object obj, params string[] fields)
#else
            void Root(Il2CppObjectBase obj, params string[] fields)
#endif
            {
                var p = Ptr(obj);
                access.AddRoot(p, fields);
                rootList.Add(p);
            }
            Root(store);
            Root(StoreStation.Instance);
            // These MonoBehaviours contain business roots; Unity's native component state is excluded.
            Root(EmporiumEntry.Instance);
            Root(BargainUIManager.Instance, "trackedClient", "baseTradeRepMultiplier", "discountOffered",
                "discountPercent", "markupOffered", "markupPercent", "markupApplied", "blackmailOffered",
                "blackmailApplied", "buyingMarkupOffered", "buyingMarkupPercent", "buyingDiscountOffered",
                "buyingDiscountPercent", "buyingDiscountApplied", "threatenOffered", "threatenApplied",
                "repPreviewActive", "repPreviewType", "repPreviewPercent");
            Root(DialogUIManager.Instance, "currentDialog", "isConversationActiveFlag", "isSkipCurrentDialog",
                "isWaitingForManualAdvance", "isAutoAdvancePending", "isEndPending",
                "displayingUncompletedDialog", "scheduledEndActionLines", "isSecMonologuePlaying");
            Root(InspectionUIManager.Instance, "currentInspectingItem", "pendingConfrontItemId",
                "pendingConfrontType", "isEditingAppraisalField");
            Root(NegociationUIManager.Instance, "currentNegociatedItem");
            Root(StoreUIManager.Instance, "isAboutToStartMainDialog", "isReportedAug", "isTyping");
            Root(StoreShutterButton.Instance, "isClosed", "elapsedTime");
            var mono = StoreClientMono.Instance;
            Root(mono, "idleDecisionClient", "idleSuppressed", "idleShakeHorizontal", "defaultIdleAxis",
                "defaultIdleAmplitude", "defaultIdleFrequency", "ignoreOnLeftFlag", "fadeMode", "walkingIn",
                "leaveTimeScale");
            var idleAnimator = GameFields.Get<IdleAnimator>(mono, "idleAnimator");
            if (idleAnimator != null)
                Root(idleAnimator, "amplitude", "frequency", "axis", "originalPosition",
                    "hasOriginal", "timer", "playing");
            Root(PhoneUIManager.Instance, "currentNumber", "isCalling", "callingTimer", "callTimerRoll",
                "isTalking", "sequenceTimer", "currentPhoneClient");
            if (GameMaster.current != null) Root(GameMaster.current, "worldTags");
            var random = RNG.random;
            if (random != null) rootList.Add(Id(random));
            return access;
        }

        /// <summary>把门控键翻译成玩家/维护者都看得懂的具体原因。</summary>
        private static string BlockerText(string key)
        {
            switch (key)
            {
                case "noStore": return "商店数据未就绪";
                case "storeChanged": return "商店实例已变化";
                case "sceneChanged": return "已切换场景";
                case "dayChanged": return "已经跨天";
                case "arrested": return "顾客正在被逮捕";
                case "shootout": return "正在发生枪战";
                case "outside": return "当前在店外";
                case "noUI": return "商店界面未就绪";
                case "shootingMode": return "处于射击模式";
                case "closeArmed": return "正在闭店";
                case "noClientMono": return "顾客模型未就绪";
                case "clientMoving": return "顾客正在移动";
                case "dragging": return "正在框选或拖拽物品";
                case "phoneBusy": return "正在通话";
                case "differentClient": return "当前顾客不是该节点记录的顾客";
                default:
                    if (key.StartsWith("state=", StringComparison.Ordinal))
                        return "当前不是营业/闭店状态（state=" + key.Substring(6) + "）";
                    return key;
            }
        }
        private static int MaxSnapshotObjects => Clamp(Plugin.CfgMaxObjects?.Value ?? 200000, 1000, 1000000);
        private static int MaxSnapshotSlots => Clamp(Plugin.CfgMaxSlots?.Value ?? 3000000, 10000, 8000000);
        private static long MaxSnapshotBytes => (long)Clamp(Plugin.CfgMaxBytesMb?.Value ?? 256, 16, 2048) * 1024 * 1024;
        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;

        private static float _settleLogAt;
        private static readonly DiagnosticGate SettleLog = new();

        /// <summary>节点迟迟建不起来时，把每个判定条件打出来，便于定位到底卡在哪一步。</summary>
        private static void WatchSettle(PlayerStore store)
        {
            if (!EventLog.Verbose) return;
            if (Time.unscaledTime < _settleLogAt) return;
            _settleLogAt = Time.unscaledTime + 1f;
            try
            {
                var ui = StoreUIManager.Instance;
                var dialog = DialogUIManager.Instance;
                var mono = StoreClientMono.Instance;
                string B(bool v) => v ? "1" : "0";
                string state =
                    "client=" + B(store != null && store.currentClientInstance != null) +
                    " arrived=" + B(store != null && store.isClientArrived) +
                    " state=" + (store == null ? -1 : (int)store.storeState) +
                    " arrest=" + B(store != null && store.isClientBeingArrested) +
                    " shoot=" + B(store != null && store.isHandlingShootout) +
                    " lockShutter=" + (store == null ? -1 : store.lockShutterButtonFlag) +
                    " ui=" + B(ui != null) +
                    " aboutStart=" + B(ui != null && ui.isAboutToStartMainDialog) +
                    " shootMode=" + B(ui != null && ui.isShootingMode) +
                    " mono=" + B(mono != null) +
                    " moving=" + (mono == null ? -1 : (mono.IsMoving() ? 1 : 0)) +
                    " dlg=" + B(dialog != null) +
                    " cur=" + B(dialog != null && dialog.currentDialog != null) +
                    " conv=" + (dialog == null ? -1 : dialog.isConversationActiveFlag) +
                    " openingDone=" + B(OpeningDialogueDone(store)) +
                    " pending=" + PendingCallbacks.Count +
                    " itemInteraction=" + B(ItemInteractionBusy());
                if (SettleLog.ShouldWrite(state, Time.unscaledTime, out int suppressed))
                    EventLog.Write("[Customer] settle wait: " + state + " repeatedSuppressed=" + suppressed);
            }
            catch (Exception e) { EventLog.Write("[Customer] settle watch failed: " + e.Message); }
        }

        private static void Capture(PlayerStore store, bool closed, bool manual = false)
        {
            Snapshot candidate = null;
            var timing = System.Diagnostics.Stopwatch.StartNew();
            string phase = "access";
            string entitySample = "none";
            try
            {
                var access = Access(store, out var roots);
                long tAccess = timing.ElapsedMilliseconds;
                candidate = new Snapshot
                {
                    Id = ++_nextId,
                    Label = manual ? "手动" : closed ? "closed" : store.currentClientInstance.GetClientBlueprint().identifier,
                    DisplayName = manual ? null : closed ? null : store.currentClientInstance.GetClientBlueprint().displayName,
                    IsClosed = closed,
                    IsManual = manual,
                    StoreState = (int)store.storeState,
                    Day = Boundary.Day, Scene = Boundary.Scene, Store = Boundary.Store, Client = Boundary.Client,
                    Cash = store.GetCash(), At = DateTime.Now, Random = RNG.random
                };
                phase = "walk";
#if MONO
                long readerCompileBefore = SnapshotFieldReader.CompileTicks;
#endif
                candidate.Graph = StateGraph.Capture(access, roots, MaxSnapshotObjects, MaxSnapshotSlots, MaxSnapshotBytes);
                long tWalk = timing.ElapsedMilliseconds;
#if MONO
                bool blockMode = access.EntityBlocks != null;
                bool developmentChecks = Plugin.CfgSemanticDevelopmentChecks?.Value == true;
                candidate.EntityBlockOwners = access.EntityBlocks?.BlockCount ?? 0;
                candidate.EntityBlockSlots = access.EntityBlocks?.SlotCount ?? 0;
                candidate.EntityRuntimeSlots = access.EntityBlocks?.RuntimeSlotCount ?? 0;
                if (blockMode && SemanticEntityBlocks.SchemaFailure != null)
                    EventLog.Write("[SemanticEntities] SINGLEPASS SCHEMA FALLBACK: " + SemanticEntityBlocks.SchemaFailure);
                long walkReaderCompileMs = SnapshotFieldReader.CompileMilliseconds(
                    SnapshotFieldReader.CompileTicks - readerCompileBefore);
#endif
                phase = "items";
                candidate.Customer = new CustomerVisual();
                candidate.Customer.Log(candidate.Id, "capture");
                candidate.Shop = new ShopPhaseVisual();
                var visualIds = new HashSet<IntPtr>();
                if (candidate.Graph.SlotCount == 0) throw new InvalidOperationException("Empty business-state graph.");
                UnityRandomState.Capture(out candidate.UnityRandom);
                foreach (var id in candidate.Graph.Objects)
                {
#if MONO
                    var instance = ManagedObjects.Get(id);
                    if (instance is GameItemElement)
#else
                    if (Is<GameItemElement>(id))
#endif
                    {
#if MONO
                        var item = (GameItemElement)instance;
#else
                        var item = new GameItemElement(id);
#endif
                        candidate.Items.Add(id, item);
                        if (item.IsDestroyed())
                        {
                            if (EventLog.Verbose)
                                EventLog.Write($"[Customer] RENDERLESS item={item.identifier} uniqueId={item.uniqueId} " +
                                    $"inventoryMember={IsInventoryMember(item)} source={candidate.Graph.SourceOf(id)}");
                            // 正式版存在没有渲染体、却仍是背包成员的物品（如 cassette_player）。
                            // 它们只参与业务状态，不参与视图恢复；此处跳过而不是中止整次捕获，
                            // 否则 F6 节点会一直存不下来（表现为"回溯只有旧节点"）。
                            continue;
                        }
                        candidate.RenderedItems.Add(id, item);
                        AddVisual(candidate, item.handler, visualIds);
                        AddVisual(candidate, item.middle, visualIds);
                        AddVisual(candidate, item.rotator, visualIds);
                        AddVisual(candidate, item.background, visualIds);
                        AddVisual(candidate, item.inner, visualIds);
                        AddVisual(candidate, item.fakeMiddle, visualIds);
                        AddVisual(candidate, item.fakeRotator, visualIds);
                        AddVisual(candidate, item.fakeBackground, visualIds);
                        AddVisual(candidate, item.fakeInner, visualIds);
                    }
#if MONO
                    else if (instance is PixelElement element)
                    {
                        AddVisual(candidate, element.handler, visualIds);
                        AddVisual(candidate, element.inner, visualIds);
                    }
#endif
#if MONO
                    if (instance is PixelWindow)
#else
                    if (Is<PixelWindow>(id))
#endif
                    {
#if MONO
                        var window = (PixelWindow)instance;
#else
                        var window = new PixelWindow(id);
#endif
                        candidate.Windows.Add(id, window);
                        AddVisual(candidate, window.handler, visualIds);
                        AddVisual(candidate, window.inner, visualIds);
                    }
                }
                if (candidate.RenderedItems.Count == 0) throw new InvalidOperationException("No live inventory items reached from snapshot roots.");
                long tItems = timing.ElapsedMilliseconds;
                long semanticCaptureVerifyMs = -1;
                phase = "semantic-shadow";
#if MONO
                if (developmentChecks && !blockMode && !_semanticShadowAttempted && Plugin.CfgSemanticTagShadow?.Value == true)
                {
                    _semanticShadowAttempted = true;
                    var shadowClock = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        candidate.SemanticTags = SemanticTagShadow.Capture(candidate.Graph,
                            MaxSnapshotObjects, MaxSnapshotSlots, MaxSnapshotBytes);
                        semanticCaptureVerifyMs = shadowClock.ElapsedMilliseconds;
                    }
                    catch (Exception e)
                    {
                        EventLog.Write($"[SemanticTags] CAPTURE FAILED id={candidate.Id} readOnly=true " +
                            $"legacyAuthoritative=true elapsedMs={shadowClock.ElapsedMilliseconds}: " + e);
                    }
                }
#endif
                long tSemantic = timing.ElapsedMilliseconds;
                phase = "entity-boundary-audit";
                string entityAudit = null;
#if MONO
                if (developmentChecks && !blockMode && !_entityBoundaryAttempted && Plugin.CfgSemanticEntityAudit?.Value == true)
                {
                    _entityBoundaryAttempted = true;
                    var auditClock = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        entityAudit = SemanticEntityBoundaryShadow.Capture(candidate.Graph, MaxSnapshotObjects).Summary;
                    }
                    catch (Exception e)
                    {
                        EventLog.Write($"[SemanticEntities] AUDIT FAILED id={candidate.Id} readOnly=true " +
                            $"legacyAuthoritative=true elapsedMs={auditClock.ElapsedMilliseconds}: " + e);
                    }
                }
#endif
                long tEntityAudit = timing.ElapsedMilliseconds;
                phase = "entity-records";
                long blockOracleMs = 0;
#if MONO
                if (developmentChecks && blockMode
                    && EntitySamples.TryBegin(manual, Plugin.CfgSemanticManualSample?.Value == true, out entitySample))
                {
                    phase = "entity-block-oracle";
                    var oracleClock = System.Diagnostics.Stopwatch.StartNew();
                    var oracleAccess = Access(store, out var oracleRoots, false);
                    candidate.BlockOracle = new SemanticCaptureOracle(candidate.Graph,
                        StateGraph.Capture(oracleAccess, oracleRoots, MaxSnapshotObjects, MaxSnapshotSlots, MaxSnapshotBytes));
                    blockOracleMs = oracleClock.ElapsedMilliseconds;
                }
                if (developmentChecks && !blockMode && Plugin.CfgSemanticEntityRecords?.Value == true
                    && EntitySamples.TryBegin(manual, Plugin.CfgSemanticManualSample?.Value == true, out entitySample))
                {
                    var recordClock = System.Diagnostics.Stopwatch.StartNew();
                    try
                    {
                        candidate.SemanticEntities = SemanticEntityShadow.Capture(candidate.Graph,
                            MaxSnapshotObjects, MaxSnapshotSlots, MaxSnapshotBytes);
                        if (Plugin.CfgSemanticShapeRestore?.Value == true)
                        {
                            try
                            {
                                candidate.SemanticEntities.EnableShapeRestore(candidate.Graph,
                                    MaxSnapshotObjects, MaxSnapshotSlots, MaxSnapshotBytes);
                            }
                            catch (Exception e)
                            {
                                EventLog.Write($"[SemanticEntities] SHAPE PILOT UNAVAILABLE id={candidate.Id} " +
                                    "legacyAuthoritative=true: " + e);
                            }
                        }
                        if (Plugin.CfgSemanticWindowRestore?.Value == true)
                        {
                            try
                            {
                                candidate.SemanticEntities.EnableWindowRestore(candidate.Graph,
                                    MaxSnapshotObjects, MaxSnapshotSlots, MaxSnapshotBytes);
                            }
                            catch (Exception e)
                            {
                                EventLog.Write($"[SemanticEntities] WINDOW PILOT UNAVAILABLE id={candidate.Id} " +
                                    "legacyAuthoritative=true: " + e);
                            }
                        }
                        if (Plugin.CfgSemanticItemRestore?.Value == true)
                        {
                            try
                            {
                                candidate.SemanticEntities.EnableItemRestore(candidate.Graph,
                                    MaxSnapshotObjects, MaxSnapshotSlots, MaxSnapshotBytes);
                            }
                            catch (Exception e)
                            {
                                EventLog.Write($"[SemanticEntities] ITEM PILOT UNAVAILABLE id={candidate.Id} " +
                                    "legacyAuthoritative=true: " + e);
                            }
                        }
                        if (Plugin.CfgSemanticInventoryRestore?.Value == true)
                        {
                            try
                            {
                                candidate.SemanticEntities.EnableInventoryRestore(candidate.Graph,
                                    MaxSnapshotObjects, MaxSnapshotSlots, MaxSnapshotBytes);
                            }
                            catch (Exception e)
                            {
                                EventLog.Write($"[SemanticEntities] INVENTORY PILOT UNAVAILABLE id={candidate.Id} " +
                                    "legacyAuthoritative=true: " + e);
                            }
                        }
                        if (Plugin.CfgSemanticRuntimeRestore?.Value == true)
                        {
                            try
                            {
                                candidate.SemanticEntities.EnableRuntimeRestore(candidate.Graph,
                                    MaxSnapshotObjects, MaxSnapshotSlots);
                            }
                            catch (Exception e)
                            {
                                EventLog.Write($"[SemanticEntities] RUNTIME PILOT UNAVAILABLE id={candidate.Id} " +
                                    "legacyAuthoritative=true: " + e);
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        EventLog.Write($"[SemanticEntities] RECORD CAPTURE FAILED id={candidate.Id} readOnly=true " +
                            $"sample={entitySample} legacyAuthoritative=true elapsedMs={recordClock.ElapsedMilliseconds}: " + e);
                    }
                }
#endif
                long tEntityRecords = timing.ElapsedMilliseconds;
                phase = "verify";
#if MONO
                long captureCompareCompileBefore = SnapshotFieldReader.CompareCompileTicks;
#endif
                var diff = candidate.Graph.Differences();
                if (diff.Count != 0) throw new InvalidOperationException("Capture changed while reading: " + string.Join(", ", diff));
                long tVerify = timing.ElapsedMilliseconds;
                if (entityAudit != null)
                    EventLog.Write($"[SemanticEntities] BOUNDARY id={candidate.Id} " + entityAudit);
#if MONO
                if (candidate.BlockOracle != null)
                    EventLog.Write($"[SemanticEntities] SINGLEPASS CAPTURE MATCH id={candidate.Id} sample={entitySample} " +
                        $"oracleMs={blockOracleMs} entityOwners={candidate.EntityBlockOwners} " +
                        $"entitySlots={candidate.EntityBlockSlots - candidate.EntityRuntimeSlots} runtimeOwnerSlots={candidate.EntityRuntimeSlots} " +
                        $"oldPayload=development-only actualWriter=single-pass fullCoverage=true");
                if (candidate.SemanticEntities != null)
                    EventLog.Write($"[SemanticEntities] RECORD CAPTURE MATCH id={candidate.Id} readOnly=true " +
                        $"sample={entitySample} captureVerifyMs={tEntityRecords - tEntityAudit} {candidate.SemanticEntities.Summary}");
                if (candidate.SemanticTags != null)
                {
                    EventLog.Write($"[SemanticTags] CAPTURE MATCH id={candidate.Id} readOnly=true " +
                        $"captureVerifyMs={semanticCaptureVerifyMs} {candidate.SemanticTags.Summary}");
                    EventLog.Write($"[SemanticTags] OWNERSHIP id={candidate.Id} readOnly=true " +
                        candidate.SemanticTags.OwnershipSummary);
                }
#endif
                phase = "history";
                if (closed)
                {
                    History.Remove(ClosedNode);
                    ClosedNode = candidate;
                }
                LogItemGeometry(candidate, "capture");
                long tBeforeHistory = timing.ElapsedMilliseconds;
                History.Add(candidate, commitBranch: !closed);
                long tHistory = timing.ElapsedMilliseconds;
                if (ClosedNode?.Graph == null) ClosedNode = null;
                _active = candidate;
                candidate = null;
                LastCaptureError = null;
                _failureNoticeKey = null;
                _failureRevision++;
                RebuildRetainedItems();
                long tRetain = timing.ElapsedMilliseconds;
                phase = "finish";
                // 手动节点不能改写“当前顾客已捕获”的边界，否则会挡掉该顾客稍后的自动节点。
                if (!manual) Boundary.MarkReady();
                Status = "Checkpoint: " + Name(_active);
                if (manual) PanelNotice.Show("已建立手动节点 #" + _active.Id);
#if MONO
                string readers = $" backend={access.BlockMode} tagBlocks={_active.Graph.BlockCount - access.IntArrayBlockCount - _active.EntityBlockOwners} " +
                    $"entityBlocks={_active.EntityBlockOwners} entityBlockSlots={_active.EntityBlockSlots} " +
                    $"entityRuntimeSlots={_active.EntityRuntimeSlots} entitySchemaFallbacks={access.EntityBlocks?.FallbackCount ?? 0} " +
                    $"developmentChecks={developmentChecks} blockOracleMs={blockOracleMs} " +
                    $"intArrayBlocks={access.IntArrayBlockCount} intArraySlots={access.IntArraySlotCount}";
                if (EventLog.Verbose)
                    readers += $" walkReaderCompileMs={walkReaderCompileMs} " +
                    $"typedComparisons={access.TypedComparisonCount} " +
                    $"referenceComparisons={access.ReferenceComparisonCount} " +
                    $"verifyCompareCompileMs={SnapshotFieldReader.CompileMilliseconds(SnapshotFieldReader.CompareCompileTicks - captureCompareCompileBefore)} " +
                    SnapshotFieldReader.Summary + " " + access.ArraySchemaSummary + " " + access.WorkloadSummary();
#else
                string readers = "";
#endif
                long tFinish = timing.ElapsedMilliseconds;
                EventLog.Write($"[Customer] CAPTURE id={_active.Id} client={_active.Label} history={CustomerCount}/20 " +
                    $"objects={_active.Graph.ObjectCount} slots={_active.Graph.SlotCount} bytes={_active.Graph.ByteCount} " +
                    $"items={_active.Items.Count} renderedItems={_active.RenderedItems.Count} windows={_active.Windows.Count} " +
                    $"arrivalWaitSeconds={(manual || closed ? 0 : Time.unscaledTime - _arrivedAt):0.00} " +
                    $"captureMs={tFinish} " +
                    $"ms(access={tAccess},walk={tWalk - tAccess},items={tItems - tWalk}," +
                    $"semanticShadow={tSemantic - tItems},entityAudit={tEntityAudit - tSemantic}," +
                    $"entityRecords={tEntityRecords - tEntityAudit},verify={tVerify - tEntityRecords},prepareHistory={tBeforeHistory - tVerify}," +
                    $"history={tHistory - tBeforeHistory},retain={tRetain - tHistory}," +
                    $"finish={tFinish - tRetain},total={tFinish})" + readers);
                TraceBargain("CAPTURE");
            }
            catch (Exception e)
            {
                candidate?.Dispose();
                Boundary.Invalidate();
                Status = "Capture failed: " + e.Message;
                EventLog.Write($"[Customer] CAPTURE FAILED phase={phase} elapsedMs={timing.ElapsedMilliseconds} " +
                    $"limits(objects={MaxSnapshotObjects},slots={MaxSnapshotSlots},bytes={MaxSnapshotBytes}): " + e);
                RecordFailure(closed, manual, e);
            }
        }

        internal static void Restore()
        {
            Observe();
            Restore(_active);
        }

        internal static void Restore(Snapshot target)
        {
            if (Restoring || Rewind.Restoring) return;
            Observe();
            if (_faulted)
            {
                PanelNotice.Show("回溯失败后需先重新载入游戏\n详情请查看日志（设置中的“查看日志”）", true);
                return;
            }
            if (!Enabled || target?.Graph == null || (!History.Items.Contains(target) && target != ClosedNode))
            {
                EventLog.Write("[Customer] F6 unavailable: " + Status + ". No save-file fallback.");
                string why = Failures.Count > 0 ? "（最近一次建立失败：" + Failures[0].Reason + "）" : "";
                PanelNotice.Show("暂无可用节点" + why + "\n详情请查看日志（设置中的“查看日志”）", true);
                return;
            }
            var store = Plugin.Store();
            var ui = StoreUIManager.Instance;
            var phone = PhoneUIManager.Instance;
            // 逐项记录阻塞原因：以前统一提示“请先结束当前操作”，出事时看不出是哪一条。
            // 这里不再看 isAboutToStartMainDialog —— 那个瞬态标志在 mod 环境里会卡住为 true，
            // 会让所有回溯无条件被拒绝。
            var blockers = new List<string>();
            bool crossClient = false;
            if (store == null) blockers.Add("noStore");
            else
            {
                if (target.Store != Ptr(store)) blockers.Add("storeChanged");
                if (target.Scene != Boundary.Scene) blockers.Add("sceneChanged");
                if (target.Day != Boundary.Day) blockers.Add("dayChanged");
                int state = (int)store.storeState;
                // 手动节点允许在同一营业状态下回溯（包括早市 state=2）；自动节点仍只支持营业/闭店。
                bool sameState = target.StoreState == state;
                if (state != 0 && state != 1 && !(target.IsManual && sameState)) blockers.Add("state=" + state);
                if (store.isClientBeingArrested) blockers.Add("arrested");
                if (store.isHandlingShootout) blockers.Add("shootout");
                // 允许回到更早的顾客节点：PlayerStore.currentClientInstance 在快照里，
                // 恢复后当前顾客会被换成节点记录的那一位，模型贴图也随 CustomerVisual 一起还原。
                // 这里只记录“跨顾客”，不再拦截。
                crossClient = !target.IsClosed && target.Client != IntPtr.Zero && store.currentClientInstance != null
                    && target.Client != Ptr(store.currentClientInstance);
            }
            if (_outside) blockers.Add("outside");
            if (ui == null) blockers.Add("noUI");
            else if (ui.isShootingMode) blockers.Add("shootingMode");
            if (_closeArmed) blockers.Add("closeArmed");
            if (StoreClientMono.Instance == null) blockers.Add("noClientMono");
            else if (StoreClientMono.Instance.IsMoving()) blockers.Add("clientMoving");
            if (ItemInteractionBusy()) blockers.Add("dragging");
            if (GameFields.PhoneBusy(phone)) blockers.Add("phoneBusy");
            if (blockers.Count != 0)
            {
                string detail = string.Join("、", blockers.Select(BlockerText));
                long currentClient = 0;
                try { currentClient = Ptr(store?.currentClientInstance).ToInt64(); } catch { }
                EventLog.Write("[Customer] F6 refused: " + string.Join(", ", blockers) + " (" + detail + ")"
                    + " target=" + Name(target) + " targetClient=0x" + target.Client.ToInt64().ToString("X")
                    + " currentClient=0x" + currentClient.ToString("X"));
                PanelNotice.Show("无法回溯：\n" + string.Join("\n", blockers.Select(BlockerText))
                    + "\n详情请查看日志（设置中的“查看日志”）", true);
                return;
            }
            if (crossClient)
            {
                long currentClient = 0;
                try { currentClient = Ptr(store.currentClientInstance).ToInt64(); } catch { }
                EventLog.Write("[Customer] F6 cross-client rewind: target=" + Name(target)
                    + " targetClient=0x" + target.Client.ToInt64().ToString("X")
                    + " currentClient=0x" + currentClient.ToString("X"));
            }
            bool changingPhase = ((int)store.storeState == 0) != target.IsClosed;
            var phaseDialog = DialogUIManager.Instance;
            if (changingPhase)
            {
                var phaseBlockers = new List<string>();
                if (PendingCallbacks.Count != 0) phaseBlockers.Add("还有 " + PendingCallbacks.Count + " 个延时回调未结束");
                if (phaseDialog == null) phaseBlockers.Add("对话界面未就绪");
                else
                {
                    if (phaseDialog.currentDialog != null) phaseBlockers.Add("对话进行中");
                    if (phaseDialog.isConversationActiveFlag != 0) phaseBlockers.Add("对话未结束");
                    if (phaseDialog.isWaitingForManualAdvance) phaseBlockers.Add("等待手动继续对话");
                }
                if (!ShopPhaseVisual.IsSettled()) phaseBlockers.Add("开关门动画未结束");
                if (phaseBlockers.Count != 0)
                {
                    string phaseDetail = string.Join("、", phaseBlockers);
                    EventLog.Write("[Customer] F6 refused (phase change): " + phaseDetail);
                    PanelNotice.Show("无法切换营业状态：\n" + string.Join("\n", phaseBlockers)
                        + "\n详情请查看日志（设置中的“查看日志”）", true);
                    return;
                }
            }

            StateGraph current = null;
            bool wrote = false;
            bool completed = false;
            string phase = "preflight";
            var timing = System.Diagnostics.Stopwatch.StartNew();
            long tValidate = 0, tCensus = 0, tPreflight = 0, tWrite = 0, tRefresh = 0;
            long tReconcile = 0, tVerify = 0, tSemanticVerify = 0, tCleanup = 0;
            long tReconcileScan = 0, tReconcileWrite = 0, tReconcileViews = 0, tGeometry = 0;
            bool reconcileRewrite = false;
            int reconcileReportedDiff = 0;
            string reconcileSources = "";
            StateGraph.Reconciliation reconciliation = null;
            bool finalRepairFallback = false;
            int censusObjects = 0, censusSlots = 0;
            int censusTagProbes = 0;
            int finalDifferences = -1;
            int shapePilotOwners = 0;
            int windowPilotOwners = 0;
            int itemPilotOwners = 0;
            int inventoryPilotOwners = 0;
            bool runtimePilot = false;
            int runtimePilotSlots = 0;
            (long Checked, long Skipped) runtimeScalarWrites = default;
            long censusBytes = 0;
            string workload = "";
            var scalarWritesBefore = target.Graph.ScalarWrites;
            long typedComparisonsBefore = target.Graph.TypedComparisonCount;
            long referenceComparisonsBefore = target.Graph.ReferenceComparisonCount;
#if MONO
            long compareCompileBefore = SnapshotFieldReader.CompareCompileTicks;
            var runtimeWritesBefore = target.SemanticEntities?.RuntimeScalarWrites ?? default;
#endif
            Restoring = true;
            float scale = Time.timeScale;
            try
            {
                // Preflight before any mutation, including current objects created since the checkpoint.
                foreach (var view in target.Visuals) view.Validate();
                target.Customer.Validate();
                target.Shop.Validate();
                target.Customer.Log(target.Id, "before-restore");
                tValidate = timing.ElapsedMilliseconds;
                phase = "census";
                var access = Access(store, out var roots, false);
#if MONO
                if (Plugin.CfgSemanticTagCensus?.Value == true) SemanticTagBlock.EnableCensus(access);
#endif
                current = StateGraph.Census(access, roots, MaxSnapshotObjects, MaxSnapshotSlots, MaxSnapshotBytes);
#if MONO
                censusTagProbes = access.CensusValueObjectCount;
#endif
                tCensus = timing.ElapsedMilliseconds;
                censusObjects = current.ObjectCount;
                censusSlots = current.SlotCount;
                censusBytes = current.ByteCount;
#if MONO
                if (Plugin.CfgVerboseLog?.Value == true) workload = " " + access.WorkloadSummary();
#endif
                phase = "target-preflight";
                target.Graph.Differences();
#if MONO
                // Validate independent retention before the first business write; live differences are expected here.
                target.BlockOracle?.ValidateRetention();
#endif
                TraceBargain("BEFORE RESTORE");
                var newItems = new List<GameItemElement>();
                var newWindows = new List<PixelWindow>();
                foreach (var id in current.Objects)
                {
                    if (Is<GameItemElement>(id) && !target.Items.ContainsKey(id))
#if MONO
                        newItems.Add((GameItemElement)ManagedObjects.Get(id));
#else
                        newItems.Add(new GameItemElement(id));
#endif
                    if (Is<PixelWindow>(id) && !target.Windows.ContainsKey(id))
#if MONO
                        newWindows.Add((PixelWindow)ManagedObjects.Get(id));
#else
                        newWindows.Add(new PixelWindow(id));
#endif
                }
                tPreflight = timing.ElapsedMilliseconds;
                phase = "selection-reset";
#if MONO
                // Native selectedHomes and ghost offsets belong to the abandoned timeline.
                // Active gestures were refused above; Cancel here only clears a static selection.
                var selection = ItemMultiSelectHandler.current;
                if (selection != null && selection.HasSelection)
                {
                    selection.Cancel();
                    EventLog.Debug("[Customer] cleared native selection before restore id=" + target.Id);
                }
#endif
                phase = "write";
                Time.timeScale = 0;
                wrote = true;
                ++_epoch;
                PendingCallbacks.Clear();
                DialogueCallbacks.Clear();
                var dialog = DialogUIManager.Instance;
                dialog.StopAllTrackedDialogueCoroutines();
                GameFields.Set(dialog, "conversationEpoch", GameFields.Get<int>(dialog, "conversationEpoch") + 1);
                GameFields.Set(dialog, "advanceSerial", GameFields.Get<int>(dialog, "advanceSerial") + 1);
                dialog.HideAllDialogUI();
                // Never call InterruptDialog: it can execute endAction on the abandoned timeline.
                int countersBefore = EventLog.Verbose ? CounterCount() : -1;
#if MONO
                runtimePilot = Plugin.CfgSemanticRuntimeRestore?.Value == true && target.SemanticEntities != null
                    && target.SemanticEntities.TryRestoreRuntime(target.Graph,
                        Plugin.CfgSemanticShapeRestore?.Value == true, Plugin.CfgSemanticWindowRestore?.Value == true,
                        Plugin.CfgSemanticItemRestore?.Value == true, Plugin.CfgSemanticInventoryRestore?.Value == true);
                if (runtimePilot || (target.SemanticEntities != null && target.SemanticEntities.TryRestoreEntities(target.Graph,
                    Plugin.CfgSemanticShapeRestore?.Value == true, Plugin.CfgSemanticWindowRestore?.Value == true,
                    Plugin.CfgSemanticItemRestore?.Value == true, Plugin.CfgSemanticInventoryRestore?.Value == true)))
                {
                    if (runtimePilot)
                    {
                        runtimePilotSlots = target.SemanticEntities.RuntimePilotSlots;
                        var writes = target.SemanticEntities.RuntimeScalarWrites;
                        runtimeScalarWrites = (writes.Checked - runtimeWritesBefore.Checked, writes.Skipped - runtimeWritesBefore.Skipped);
                    }
                    if (Plugin.CfgSemanticShapeRestore?.Value == true)
                        shapePilotOwners = target.SemanticEntities.ShapePilotOwners;
                    if (Plugin.CfgSemanticWindowRestore?.Value == true)
                        windowPilotOwners = target.SemanticEntities.WindowPilotOwners;
                    if (Plugin.CfgSemanticItemRestore?.Value == true)
                        itemPilotOwners = target.SemanticEntities.ItemPilotOwners;
                    if (Plugin.CfgSemanticInventoryRestore?.Value == true)
                        inventoryPilotOwners = target.SemanticEntities.InventoryPilotOwners;
                }
                else
#endif
                    target.Graph.Restore();
                RNG.random = target.Random;
                tWrite = timing.ElapsedMilliseconds;
                phase = "native-refresh";
                foreach (var id in target.RenderedItems.Keys) Deleted.Remove(id);
                foreach (var view in target.Visuals) view.Restore();
                target.Shop.Restore();
                target.Customer.Restore();
                // VisualState restores the actual captured render parent. A data
                // inventory may render through a window or another native host.
                LogItemGeometry(target, "restore-pre");
                foreach (var item in target.RenderedItems.Values)
                {
                    GameFields.Set(item, "validateLock", false);
#if MONO
                    GameFields.Set(item, "appliedGridSize", -1);
#endif
                    item.ValidatePartialSprite();
                    item.Validate();
                }
                try { OverlayHandler.current?.UpdateInventoryStateUIImmediately(); } catch { }
                var inspect = InspectionUIManager.Instance;
                if (inspect.panel != null) inspect.panel.SetActive(false);
                if (inspect.appraisalPanel != null) inspect.appraisalPanel.SetActive(false);
                if (inspect.confrontPanel != null) inspect.confrontPanel.SetActive(false);
                if (phone?.phonePanel != null) phone.phonePanel.SetActive(false);
                // Refresh only visual surfaces, never replay arrival/trade/inventory callbacks.
                NegociationUIManager.Instance.RefreshUI();
                // Update also recomputes Button.interactable, which is outside the business graph.
                // It uses the game's original per-customer eligibility and dialogue/transition guards.
#if MONO
                BargainUIManager.Instance.RefreshUI();
#else
                BargainUIManager.Instance.Update();
#endif
                UnityRandomState.Restore(target.UnityRandom);
                tRefresh = timing.ElapsedMilliseconds;
                phase = "reconcile-scan";
                reconciliation = target.Graph.PrepareReconciliation(Plugin.CfgOwnerReconcile?.Value == true);
                var diff = reconciliation.Differences;
                if (diff.Count != 0) reconcileSources = string.Join(" | ", diff);
                tReconcileScan = timing.ElapsedMilliseconds;
                reconcileReportedDiff = diff.Count;
                phase = "reconcile-write";
                reconcileRewrite = reconciliation.ChangedSlots != 0;
                // Restore complete changed owners, or the full graph when the plan cannot opt in.
                reconciliation.Apply();
                tReconcileWrite = timing.ElapsedMilliseconds;
                // A second business write above can change shape/origin again.
                // Restore native views and finish geometry only after that write.
                phase = "reconcile-views";
                foreach (var view in target.Visuals) view.Restore();
                tReconcileViews = timing.ElapsedMilliseconds;
                phase = "reconcile-geometry";
                RestoreItemGeometry(target);
                tGeometry = timing.ElapsedMilliseconds;
                // 旋转/翻转/锚点/尺寸以快照里的原生 RectTransform 为准：几何恢复之后再套一次，
                // 避免 UpdateRectTransform() 用被清零的 modifiedShape.orientation 覆盖旋转。
                phase = "reconcile-transforms";
                foreach (var view in target.Visuals) view.RestoreTransforms();
                target.Customer.RestoreTransforms();
                tReconcile = timing.ElapsedMilliseconds;
                phase = "final-verify";
                diff = target.Graph.VerifyReconciliation(reconciliation, () =>
                {
                    phase = "final-fallback-views";
                    foreach (var view in target.Visuals) view.Restore();
                    phase = "final-fallback-geometry";
                    RestoreItemGeometry(target);
                    phase = "final-fallback-transforms";
                    foreach (var view in target.Visuals) view.RestoreTransforms();
                    target.Customer.RestoreTransforms();
                    phase = "final-fallback-verify";
                }, out finalRepairFallback);
                finalDifferences = diff.Count;
                if (diff.Count != 0)
                    throw new InvalidOperationException("Final restore differs from checkpoint: " + string.Join(", ", diff));
                tVerify = timing.ElapsedMilliseconds;
#if MONO
                if (target.BlockOracle != null)
                {
                    phase = "entity-block-oracle-verify";
                    target.BlockOracle.VerifyRestore();
                    EventLog.Write($"[SemanticEntities] SINGLEPASS RESTORE MATCH id={target.Id} " +
                        $"entityOwners={target.EntityBlockOwners} actualWriter=single-pass oracle=independent-legacy diff=0");
                }
                target.SemanticTags?.ObserveRestore(target.Id);
                target.SemanticEntities?.ObserveRestore(target.Id);
#endif
                tSemanticVerify = timing.ElapsedMilliseconds;
                phase = "cleanup";
                LogItemGeometry(target, "restore-post");
                // Only discard abandoned views after the restored business graph passes validation.
                foreach (var w in newWindows)
                    if (w.handler != null) w.handler.gameObject.SetActive(false);
                foreach (var item in newItems) DiscardRenderer(item);
                if (countersBefore >= 0)
                {
                    int countersAfter = CounterCount();
                    if (countersAfter != countersBefore)
                        EventLog.Write($"[Customer] restore counter items {countersBefore} -> {countersAfter} (discarded views={newItems.Count})");
                }
                // Hidden retained objects may no longer be reachable from the current business graph.
                foreach (var pair in RetainedItems)
                    if (!target.RenderedItems.ContainsKey(pair.Key)) DiscardRenderer(pair.Value);
                foreach (var node in LiveNodes())
                    foreach (var w in node.Windows)
                        if (!target.Windows.ContainsKey(w.Key) && w.Value.handler != null)
                            w.Value.handler.gameObject.SetActive(false);
                History.Select(target);
                _active = target;
                _armed = false;
                _closeArmed = false;
                Boundary.Observe(target.Store, target.Client, target.Day, target.Scene);
                Boundary.MarkReady();
                RebuildRetainedItems();
                AppraisalPatch.Reset();
                // The recorder is a view of dialogHistory; refresh it without treating
                // an optional UI failure as a partially failed native restore.
                try { RecorderUIManager.Instance?.RefreshUI(); }
                catch (Exception e) { Plugin.LogSource.LogWarning("[Recorder] refresh failed: " + e.Message); }
                Status = "Restored checkpoint: " + Name(target);
                PanelNotice.Show("已回溯至 " + Name(target));
                TraceBargain("AFTER RESTORE");
                target.Customer.Log(target.Id, "restored");
                if (EventLog.Verbose)
                {
                    _visualProbe = target;
                    _visualProbeFrame = Time.frameCount + 2;
                    _visualProbeEpoch = _epoch;
                }
                tCleanup = timing.ElapsedMilliseconds;
                completed = true;
            }
            catch (Exception e)
            {
                EventLog.Write("[Customer] RESTORE FAILED" + (wrote ? " AFTER WRITE" : " (preflight)") +
                    $" phase={phase} elapsedMs={timing.ElapsedMilliseconds}: " + e);
                if (wrote)
                {
                    // A partial write may have relinked these items. Never destroy their retained views.
                    // Keep all pinned objects/views after a partial native write; release on scene reload.
                    _active = null;
                    _armed = false;
                    Boundary.Invalidate();
                    _faulted = true;
                    Status = "Restore failed; reload the game.";
                }
                PanelNotice.Show(wrote ? "回溯失败，请重新载入游戏" : "节点无法恢复，未写入状态", true);
            }
            finally
            {
                Exception releaseError = null;
                try { current?.Dispose(); }
                catch (Exception e) { releaseError = e; }
                finally
                {
                    Time.timeScale = scale;
                    Restoring = false;
                }
                if (completed)
                {
                    long tFinish = timing.ElapsedMilliseconds;
#if MONO
                    string comparisonDiagnostic = $"entityBlocks={target.EntityBlockOwners} entityBlockSlots={target.EntityBlockSlots} " +
                        $"entityRuntimeSlots={target.EntityRuntimeSlots} blockOracle={target.BlockOracle != null}";
#else
                    string comparisonDiagnostic = "";
#endif
                    string writeDiagnostic = "";
                    if (EventLog.Verbose)
                    {
                        writeDiagnostic = $"shapePilotOwners={shapePilotOwners} windowPilotOwners={windowPilotOwners} itemPilotOwners={itemPilotOwners} inventoryPilotOwners={inventoryPilotOwners} " +
                        $"runtimePilot={runtimePilot} runtimePilotSlots={runtimePilotSlots} " +
                        $"runtimeScalarWrites(checked={runtimeScalarWrites.Checked},skipped={runtimeScalarWrites.Skipped}) " +
                        $"typedComparisons={target.Graph.TypedComparisonCount - typedComparisonsBefore} " +
                        $"referenceComparisons={target.Graph.ReferenceComparisonCount - referenceComparisonsBefore} " +
                        $"ordinaryScalarWrites(checked={target.Graph.ScalarWrites.Checked - scalarWritesBefore.Checked}," +
                        $"skipped={target.Graph.ScalarWrites.Skipped - scalarWritesBefore.Skipped}) ";
#if MONO
                        writeDiagnostic += $"compareCompileMs={SnapshotFieldReader.CompileMilliseconds(SnapshotFieldReader.CompareCompileTicks - compareCompileBefore)} ";
#endif
                    }
                    EventLog.Write($"[Customer] RESTORE OK id={target.Id} client={target.Label} history={CustomerCount}/20 diff={finalDifferences} " +
                        $"releaseOk={releaseError == null} {comparisonDiagnostic} " + writeDiagnostic +
                        $"census(objects={censusObjects},slots={censusSlots},bytes={censusBytes},exactTagProbes={censusTagProbes}) " +
                        $"ms(validate={tValidate},census={tCensus - tValidate},preflight={tPreflight - tCensus}," +
                        $"write={tWrite - tPreflight},refresh={tRefresh - tWrite},reconcile={tReconcile - tRefresh}," +
                        $"finalVerify={tVerify - tReconcile},semanticVerify={tSemanticVerify - tVerify}," +
                        $"cleanup={tCleanup - tSemanticVerify}," +
                        $"release={tFinish - tCleanup},total={tFinish}) " +
                        $"reconcileDetail(rewrite={reconcileRewrite},reportedDiff={reconcileReportedDiff}," +
                        $"scan={tReconcileScan - tRefresh},write={tReconcileWrite - tReconcileScan}," +
                        $"views={tReconcileViews - tReconcileWrite},geometry={tGeometry - tReconcileViews}," +
                        $"transforms={tReconcile - tGeometry}) " +
                        $"repair(mode={reconciliation.Mode},reason={(reconciliation.Reason.Length == 0 ? "eligible" : reconciliation.Reason)}," +
                        $"changedSlots={reconciliation.ChangedSlots},trackedOwners={reconciliation.TrackedOwners}," +
                        $"plannedSlots={reconciliation.PlannedSlots},finalFallback={finalRepairFallback})" + workload);
                }
                if (releaseError != null)
                    EventLog.Write($"[Customer] CENSUS RELEASE FAILED restoreCompleted={completed} " +
                        $"id={target.Id} phase=release elapsedMs={timing.ElapsedMilliseconds}: " + releaseError);
                if (reconcileSources.Length != 0 && (EventLog.Verbose || !completed
                    || releaseError != null || finalRepairFallback || reconciliation?.Mode == "full"))
                    EventLog.Write($"[Customer] RECONCILE DIFF id={target.Id} restoreCompleted={completed} " +
                        $"reportedDiff={reconcileReportedDiff} sampleLimit=12 scanLimit=12 " +
                        $"scope=captured-graph-only values=omitted sample=[{reconcileSources}]");
                if (reconciliation != null && reconciliation.FallbackSummary.Length != 0)
                    EventLog.Write($"[Customer] REPAIR FULL FALLBACK id={target.Id} restoreCompleted={completed} " +
                        $"reason=final-diff values=omitted sample=[{reconciliation.FallbackSummary}]");
            }
        }

        private static bool ItemInteractionBusy()
        {
            bool single = ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem;
#if MONO
            var multi = ItemMultiSelectHandler.current;
            return CheckpointItemInteraction.IsBusy(single, multi != null && multi.IsGroupDragging,
                multi != null && multi.HasSelection, multi != null && Input.GetKey(multi.key));
#else
            return single;
#endif
        }

        internal static bool DeferDestroy(GameItemElement item)
        {
            if (!Enabled || Restoring || !RetainedItems.ContainsKey(Id(item))) return false;
            if (Deleted.ContainsKey(Id(item))) return true;
            // Equivalent to this build's Destroy business cleanup; retain only its two visual roots.
            Deleted.Add(Id(item), item);
            item.parentInventory?.Expel(item);
            item.examineWindow?.Hide();
            if (item.uniqueId != 0) Plugin.Store()?.contentWindowPositions?.Remove(item.uniqueId);
            if (item.fakeMiddle != null) item.fakeMiddle.gameObject.SetActive(false);
            if (item.handler != null) item.handler.gameObject.SetActive(false);
            return true;
        }

        internal static bool WasDeleted(IntPtr item) => Deleted.ContainsKey(item);

        internal static void NextCustomer(PlayerStore store, IntPtr previousClient)
        {
            if (!Enabled || Restoring || Rewind.Restoring || _faulted || store == null) return;
            var client = Ptr(store.currentClientInstance);
            if (client == IntPtr.Zero || client == previousClient) return;
            Observe();
            int removed = History.CommitBranch();
            if (ClosedNode?.Graph == null) ClosedNode = null;
            RebuildRetainedItems();
            if (removed > 0) EventLog.Write($"[Customer] BRANCH committed on next customer; removed={removed}");
        }

        private static void RestoreItemGeometry(Snapshot snapshot)
        {
            int grid = UISettings.current.gridSize;
            foreach (var item in snapshot.RenderedItems.Values)
            {
                if (item.IsDestroyed() || item.modifiedShape == null)
                    throw new InvalidOperationException("Checkpoint item view is unavailable: " + item.identifier);
                var shape = item.modifiedShape;
                var size = new Vector2(shape.width * grid, shape.height * grid);
                var origin = new Vector2(GameFields.Get<int>(item, "modifiedXOrigin") * grid,
                    GameFields.Get<int>(item, "modifiedYOrigin") * grid);
                item.handler.rectTransform.anchoredPosition = Vector2.zero;
                item.middle.rectTransform.sizeDelta = size;
                item.rotator.rectTransform.sizeDelta = size;
                item.fakeMiddle.rectTransform.sizeDelta = size;
                item.fakeRotator.rectTransform.sizeDelta = size;
#if MONO
                // A forced native layout restores flip/pivot/rotation as well as position.
                GameFields.Set(item, "appliedGridSize", -1);
#endif
                GameFields.Set(item, "validateLock", false);
                item.UpdateRectTransform();
                // Keep the final anchor write after all UI callbacks and graph restores.
                var position = new Vector2(shape.minX * grid, -shape.minY * grid);
                item.middle.rectTransform.anchoredPosition = position;
                item.fakeMiddle.rectTransform.anchoredPosition = position;
                item.background.rectTransform.anchoredPosition = origin;
                item.fakeBackground.rectTransform.anchoredPosition = origin;
                if ((item.middle.rectTransform.anchoredPosition - position).sqrMagnitude > 0.25f
                    || (item.background.rectTransform.anchoredPosition - origin).sqrMagnitude > 0.25f)
                    throw new InvalidOperationException("Item view rejected restored geometry: " + item.identifier);
            }
            EventLog.Debug($"[QuickSL/Geom] synchronized {snapshot.RenderedItems.Count} item view(s) after final graph restore");
        }

        /// <summary>诊断：只记录异常的物品视图（丢失 / 无贴图 / 锚点与格子不符）。</summary>
        private static void LogItemGeometry(Snapshot snapshot, string phase)
        {
            if (!EventLog.Verbose || snapshot == null) return;
            int logged = 0;
            try
            {
                int gridSize = UISettings.current != null ? UISettings.current.gridSize : 16;
                foreach (var item in snapshot.RenderedItems.Values)
                {
                    if (logged >= 30) break;
                    try
                    {
                        if (item == null) continue;
                        var shape = item.modifiedShape;
                        var handler = item.handler?.rectTransform;
                        var middle = item.middle?.rectTransform;
                        var back = item.background?.rectTransform;
                        bool destroyed = item.IsDestroyed();
                        bool missing = handler == null || middle == null || back == null || item.handler?.gameObject == null;
                        bool noSprite = item.background == null || item.background.image == null
                            || item.background.image.sprite == null;
                        bool hidden = item.unitCount > 0 && handler != null && handler.gameObject.activeInHierarchy
                            && (item.background == null || item.background.image == null
                                || !item.background.gameObject.activeInHierarchy || !item.background.image.enabled
                                || item.background.image.color.a <= 0);
                        float expectedX = shape == null ? 0 : shape.minX * gridSize;
                        float expectedY = shape == null ? 0 : -shape.minY * gridSize;
                        bool misplaced = middle != null && shape != null
                            && (Math.Abs(middle.anchoredPosition.x - expectedX) > 0.5f
                                || Math.Abs(middle.anchoredPosition.y - expectedY) > 0.5f);
                        if (!destroyed && !missing && !noSprite && !misplaced && !hidden) continue;
                        string shapeText = shape == null ? "null"
                            : $"({shape.minX},{shape.minY} {shape.width}x{shape.height} rot={shape.orientation} flip={shape.flipped})";
                        int xo = GameFields.Get<int>(item, "modifiedXOrigin");
                        int yo = GameFields.Get<int>(item, "modifiedYOrigin");
                        string parent = handler != null && handler.parent != null ? handler.parent.name : "-";
                        string inv = item.parentInventory == null ? "null" : item.parentInventory.GetType().Name;
                        string flags = (destroyed ? "DESTROYED " : "") + (missing ? "MISSING " : "")
                            + (noSprite ? "NOSPRITE " : "")
                            + (hidden ? "HIDDEN " : "")
                            + (misplaced ? "MISPLACED(expect " + expectedX.ToString("0.#") + "/" + expectedY.ToString("0.#") + ") " : "");
                        EventLog.Write($"[QuickSL/Geom] {phase} {flags}id={item.identifier} shape={shapeText} origin=({xo},{yo}) " +
                            $"handler={Fmt(handler)} middle={Fmt(middle)} bg={Fmt(back)} parent={parent} inv={inv}");
                        logged++;
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string Fmt(RectTransform rect)
            => rect == null ? "null" : $"{rect.anchoredPosition.x:0.#}/{rect.anchoredPosition.y:0.#} {rect.sizeDelta.x:0.#}x{rect.sizeDelta.y:0.#}";
        private static int CounterCount()
        {
            try
            {
                var entry = EmporiumEntry.Instance;
                if (entry == null) return -1;
                var list = entry.GetCountersItems();
                return list == null ? -1 : list.Count;
            }
            catch { return -1; }
        }
        private static void DestroyRenderer(GameItemElement item)
        {
            if (item.fakeMiddle != null) UnityEngine.Object.Destroy(item.fakeMiddle.gameObject);
            if (item.handler != null) UnityEngine.Object.Destroy(item.handler.gameObject);
        }

        private static void DiscardRenderer(GameItemElement item)
        {
            if (!RetainedItems.ContainsKey(Id(item))) { DestroyRenderer(item); return; }
            Deleted[Id(item)] = item;
            if (item.fakeMiddle != null) item.fakeMiddle.gameObject.SetActive(false);
            if (item.handler != null) item.handler.gameObject.SetActive(false);
        }

        internal static void Dismissed()
        {
            _active = null;
            _armed = false;
            Boundary.Invalidate();
            Status = "Waiting for a new customer";
        }

        internal static void Invalidate(string reason)
        {
            Clear();
            Boundary.Invalidate();
            Status = reason;
        }

        private static void Clear(string reason = "场景切换")
        {
            _visualProbe = null;
#if MONO
            _semanticShadowAttempted = false;
            _entityBoundaryAttempted = false;
            EntitySamples.Reset();
#endif
            Failures.Clear();
            LastCaptureError = null;
            _failureNoticeKey = null;
            _failureRevision++;
            _captureSince = 0;
            SettleLog.Reset();
            History.Clear();
            if (ClosedNode != null)
            {
                ClosedNode.Dispose();
                ClosedNode.ExpiredReason = reason;
            }
            foreach (var item in Deleted.Values) DestroyRenderer(item);
            Deleted.Clear();
            RetainedItems.Clear();
            _active = null;
            _armed = false;
            _closeArmed = false;
        }

        private static void ReleaseAllSnapshotViews()
        {
            var views = new Dictionary<IntPtr, GameItemElement>();
            foreach (var node in History.Items)
                foreach (var pair in node.RenderedItems)
                    if (!views.ContainsKey(pair.Key)) views.Add(pair.Key, pair.Value);
            if (ClosedNode != null)
                foreach (var pair in ClosedNode.RenderedItems)
                    if (!views.ContainsKey(pair.Key)) views.Add(pair.Key, pair.Value);
            foreach (var pair in RetainedItems)
                if (!views.ContainsKey(pair.Key)) views.Add(pair.Key, pair.Value);
            foreach (var pair in Deleted)
                if (!views.ContainsKey(pair.Key)) views.Add(pair.Key, pair.Value);

            foreach (var item in views.Values)
            {
                try { DestroyRenderer(item); }
                catch { }
            }
        }

        private static IEnumerable<Snapshot> LiveNodes()
        {
            foreach (var node in History.Items) yield return node;
        }

        private static void RebuildRetainedItems()
        {
            RetainedItems.Clear();
            foreach (var node in LiveNodes())
                foreach (var pair in node.RenderedItems) RetainedItems[pair.Key] = pair.Value;
            foreach (var id in Deleted.Keys.Where(id => !RetainedItems.ContainsKey(id)).ToArray())
            {
                DestroyRenderer(Deleted[id]);
                Deleted.Remove(id);
            }
        }

        internal static void GuardCallback(ref Il2CppSystem.Action action)
        {
            if (!Enabled || action == null || Plugin.Store() == null) return;
#if MONO
            // 只接管游戏自身排的延时回调。其他 mod 同样调用 MetaHelper.WaitAndDo，
            // 但它们的闭包目标类型在自己的程序集里；一并包进来会被下面的 epoch/场景检查丢掉，
            // 表现为“卷帘门今日事件”之类的 mod 在换场景或回溯后不再显示。
            var origin = action.Target;
            if (origin == null || origin.GetType().Assembly != typeof(GameItem).Assembly) return;
#endif
            long epoch = _epoch;
            int scene = SceneManager.GetActiveScene().handle;
            long token = ++_callbackId;
            PendingCallbacks.Add(token);
            var target = action.Target;
#if MONO
            for (var c = target?.GetType(); c != null; c = c.DeclaringType)
                if (c.Name == "DialogUIManager") DialogueCallbacks.Add(token);
#else
            if (target != null)
                for (var c = IL2CPP.il2cpp_object_get_class(target.Pointer); c != IntPtr.Zero;
                    c = IL2CPP.il2cpp_class_get_declaring_type(c))
                    if (IL2CPP.il2cpp_class_get_name_(c) == "DialogUIManager")
                        DialogueCallbacks.Add(token);
#endif
            var original = action;
#if MONO
            action = () =>
#else
            action = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)(() =>
#endif
            {
                PendingCallbacks.Remove(token);
                DialogueCallbacks.Remove(token);
                if (epoch != _epoch || Restoring || SceneManager.GetActiveScene().handle != scene) return;
                original.Invoke();
                TryCapture();
#if MONO
            };
#else
            }));
#endif
        }

        internal static void DialogueCoroutinesStopped()
        {
            foreach (var token in DialogueCallbacks) PendingCallbacks.Remove(token);
            DialogueCallbacks.Clear();
        }

        internal static void TraceBargain(string phase)
        {
            if (!EventLog.Verbose || !Enabled || _active?.Graph == null) return;
            try
            {
#if MONO
                var bargain = BargainUIManager.Instance;
                EventLog.Write("[Customer] BARGAIN " + phase + " " + string.Join(" ",
                    new[] { "discountOffered", "discountPercent", "markupOffered", "markupPercent",
                        "blackmailOffered", "buyingMarkupOffered", "buyingDiscountOffered", "threatenOffered" }
                    .Select(name => name + "=" + GameFields.Get<object>(bargain, name))));
#else
                var bargain = BargainUIManager.Instance;
                var dialog = DialogUIManager.Instance;
                var ui = StoreUIManager.Instance;
                var mono = StoreClientMono.Instance;
                string Buttons(Il2CppSystem.Collections.Generic.List<UnityEngine.UI.Button> buttons)
                {
                    int enabled = 0, effective = 0, count = 0;
                    if (buttons != null)
                        for (int i = 0; i < buttons.Count; i++)
                        {
                            var b = buttons[i];
                            if (b == null) continue;
                            count++;
                            if (b.interactable) enabled++;
                            if (b.IsInteractable()) effective++;
                        }
                    return $"{enabled}/{effective}/{count}";
                }
                EventLog.Write($"[Customer] BARGAIN {phase} tracked={bargain.trackedClient?.identifier} " +
                    $"discount={bargain.discountOffered}:{bargain.discountPercent} " +
                    $"markup={bargain.markupOffered}:{bargain.markupPercent} blackmail={bargain.blackmailOffered} " +
                    $"buyMarkup={bargain.buyingMarkupOffered}:{bargain.buyingMarkupPercent} " +
                    $"buyDiscount={bargain.buyingDiscountOffered}:{bargain.buyingDiscountPercent} " +
                    $"threaten={bargain.threatenOffered} dialog=0x{Ptr(dialog?.currentDialog).ToInt64():X} " +
                    $"conversation={dialog?.isConversationActiveFlag} moving={(mono != null && mono.IsMoving())} " +
                    $"tradeLocked={NegociationUIManager.IsTradeLocked()} opening={ui?.isAboutToStartMainDialog} " +
                    $"managerActive={bargain.isActiveAndEnabled} discountButtons={Buttons(bargain.discountButtons)} " +
                    $"markupButtons={Buttons(bargain.markupButtons)} " +
                    $"buyMarkupButtons={Buttons(bargain.buyingMarkupButtons)} " +
                    $"buyDiscountButtons={Buttons(bargain.buyingDiscountButtons)}");
#endif
            }
            catch (Exception e) { EventLog.Write("[Customer] BARGAIN diagnostics failed: " + e.Message); }
        }

#if MONO
        private static IntPtr Ptr(object obj) => Id(obj);
#else
        private static IntPtr Ptr(Il2CppObjectBase obj) => Id(obj);
#endif
        private static bool IsInventoryMember(GameItemElement item)
        {
            var children = item.parentInventory?.childItems;
            if (children == null) return false;
            for (int i = 0; i < children.Count; i++)
                if (Ptr(children[i]) == Id(item)) return true;
            return false;
        }
#if MONO
        private static bool Is<T>(IntPtr id) => ManagedObjects.Get(id) is T;
#else
        private static bool Is<T>(IntPtr id) => IL2CPP.il2cpp_class_is_assignable_from(
            Il2CppClassPointerStore<T>.NativeClassPtr, IL2CPP.il2cpp_object_get_class(id));
#endif

        private static void AddVisual(Snapshot snapshot, TreeNodeRender node, HashSet<IntPtr> ids)
        {
            if (node != null && ids.Add(Id(node)))
                snapshot.Visuals.Add(new VisualState(node));
        }

        internal sealed class CustomerVisual
        {
            private readonly StoreClientMono _mono = StoreClientMono.Instance;
            private readonly SpriteRenderer _image;
            private readonly Transform _parent;
            private readonly List<CustomerTransform> _transforms = new();
            private readonly Sprite _sprite;
            private readonly Color _color;
            private readonly MaterialPropertyBlock _properties = new();
            private readonly bool _enabled, _forceRenderingOff;
#if MONO
            private readonly CustomerPathState _mainPath, _rightPath;
#endif
            internal CustomerVisual()
            {
                if (_mono == null || _mono.image == null || _mono.transform.parent == null)
                    throw new InvalidOperationException("Customer view missing.");
                _image = _mono.image;
                _parent = _mono.transform.parent;
                var chain = new List<Transform>();
                var t = _image.transform;
                while (t != _mono.transform)
                {
                    if (t == null) throw new InvalidOperationException("Customer sprite is outside its view hierarchy.");
                    chain.Add(t);
                    t = t.parent;
                }
                chain.Add(_mono.transform);
                chain.Add(_parent);
                chain.Reverse();
                foreach (var transform in chain) _transforms.Add(new CustomerTransform(transform));
                _sprite = _image.sprite;
                _color = _image.color;
                _enabled = _image.enabled;
                _forceRenderingOff = _image.forceRenderingOff;
                _image.GetPropertyBlock(_properties);
#if MONO
                _mainPath = new CustomerPathState(GameFields.Get<DG.Tweening.DOTweenPath>(_mono, "dOTweenPath"));
                _rightPath = new CustomerPathState(_mono.pathRight);
#endif
                Validate();
            }
            internal void Validate()
            {
                if (_mono == null || StoreClientMono.Instance != _mono || _image == null
                    || _mono.image != _image || _mono.transform.parent != _parent)
                    throw new InvalidOperationException("Customer view no longer exists.");
                foreach (var transform in _transforms) transform.Validate();
#if MONO
                _mainPath.Validate();
                _rightPath.Validate();
#endif
            }
            internal void Restore()
            {
                Validate();
#if MONO
                _mainPath.Restore();
                _rightPath.Restore();
#endif
                // OnEnable can seed IdleAnimator from the current transform: position before activation.
                RestoreTransforms();
                foreach (var transform in _transforms) transform.RestoreActive();
                RestoreTransforms();
                _image.sprite = _sprite;
                _image.color = _color;
                _image.enabled = _enabled;
                _image.forceRenderingOff = _forceRenderingOff;
                _image.SetPropertyBlock(_properties);
            }

            internal void RestoreTransforms()
            {
                foreach (var transform in _transforms) transform.RestoreTransform();
            }

            internal void Log(long id, string phase)
            {
                if (!EventLog.Verbose) return;
                try
                {
                    string paths = "";
#if MONO
                    paths = $" main=({_mainPath.Describe()}) right=({_rightPath.Describe()})";
#endif
                    var properties = new MaterialPropertyBlock();
                    _image.GetPropertyBlock(properties);
                    EventLog.Write($"[CustomerVisual] {phase} id={id} sprite={_image.sprite?.name ?? "null"} " +
                        $"targetSprite={_sprite?.name ?? "null"} enabled={_image.enabled}/{_enabled} " +
                        $"forceOff={_image.forceRenderingOff}/{_forceRenderingOff} " +
                        $"active={_image.gameObject.activeSelf} hierarchy={_image.gameObject.activeInHierarchy} " +
                        $"alpha={_image.color.a:0.###} rootLocal={_mono.transform.localPosition} " +
                        $"imageLocal={_image.transform.localPosition} world={_image.transform.position} " +
                        $"solidBlend={properties.GetFloat(Shader.PropertyToID("_SolidBlend")):0.###} " +
                        $"transforms=[{string.Join(";", _transforms.Select(t => t.Describe()))}]" + paths);
                }
                catch (Exception e) { EventLog.Write("[CustomerVisual] diagnostic failed: " + e.Message); }
            }

            private sealed class CustomerTransform
            {
                private readonly Transform _transform, _parent;
                private readonly Vector3 _position, _scale;
                private readonly Quaternion _rotation;
                private readonly bool _active;
                internal CustomerTransform(Transform transform)
                {
                    _transform = transform; _parent = transform.parent;
                    _position = transform.localPosition; _scale = transform.localScale;
                    _rotation = transform.localRotation; _active = transform.gameObject.activeSelf;
                }
                internal void Validate()
                {
                    if (_transform == null || _transform.parent != _parent)
                        throw new InvalidOperationException("Customer transform was destroyed or reparented.");
                }
                internal void RestoreTransform()
                {
                    _transform.localPosition = _position;
                    _transform.localRotation = _rotation;
                    _transform.localScale = _scale;
                }
                internal void RestoreActive() => _transform.gameObject.SetActive(_active);
                internal string Describe() => $"{_transform.name}:local={_transform.localPosition}/target={_position}," +
                    $"active={_transform.gameObject.activeSelf}/{_active},scale={_transform.localScale}/{_scale}";
            }
        }

        internal sealed class VisualState
        {
            internal readonly TreeNodeRender Node;
            private readonly Transform _parent;
            private readonly TreeNodeRender _nodeParent;
            private readonly Vector3 _anchoredPosition, _scale;
            private readonly Vector2 _anchorMin, _anchorMax, _pivot, _size;
            private readonly Quaternion _rotation;
            private readonly bool _active, _hasImage, _imageEnabled, _raycastTarget;
            private readonly Color _color;
            private readonly Sprite _sprite;
            internal VisualState(TreeNodeRender node)
            {
                Node = node;
                var t = node.rectTransform;
                _parent = t.parent; _anchoredPosition = t.anchoredPosition3D; _scale = t.localScale;
                _nodeParent = node.parent;
                _rotation = t.localRotation; _active = node.gameObject.activeSelf;
                _anchorMin = t.anchorMin; _anchorMax = t.anchorMax;
                _pivot = t.pivot; _size = t.sizeDelta;
                _hasImage = node.image != null;
                if (_hasImage)
                {
                    _imageEnabled = node.image.enabled; _raycastTarget = node.image.raycastTarget;
                    _color = node.image.color; _sprite = node.image.sprite;
                }
            }
            internal void Validate()
            {
                if (Node == null || _parent == null || Node.rectTransform == null
                    || (_hasImage && Node.image == null))
                    throw new InvalidOperationException("A checkpoint view or its parent was destroyed.");
            }
            internal void Restore()
            {
                RestoreTransforms();
                if (_hasImage)
                {
                    Node.image.sprite = _sprite; Node.image.color = _color;
                    Node.image.enabled = _imageEnabled; Node.image.raycastTarget = _raycastTarget;
                }
                Node.gameObject.SetActive(_active);
            }

            /// <summary>
            /// 只恢复几何：父级、锚点、枢轴、尺寸、位置、缩放与旋转。用于在游戏做了一次
            /// 布局（UpdateRectTransform）之后，把快照中已旋转/翻转的朝向重新压回去。
            /// </summary>
            internal void RestoreTransforms()
            {
                Validate();
                var t = Node.rectTransform;
#if MONO
                AccessTools.PropertySetter(typeof(TreeNodeRender), "parent").Invoke(Node, new object[] { _nodeParent });
#else
                Node.parent = _nodeParent;
#endif
                t.SetParent(_parent, false);
                t.anchorMin = _anchorMin; t.anchorMax = _anchorMax;
                t.pivot = _pivot; t.sizeDelta = _size;
                t.anchoredPosition3D = _anchoredPosition;
                t.localScale = _scale; t.localRotation = _rotation;
            }
        }
    }

    internal static class UnityRandomState
    {
#if MONO
        internal struct State { internal UnityEngine.Random.State Value; }
        internal static void Capture(out State state) => state = new State { Value = UnityEngine.Random.state };
        internal static void Restore(State state) => UnityEngine.Random.state = state.Value;
#else
        [StructLayout(LayoutKind.Sequential)]
        internal struct State { internal int A, B, C, D; }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StateCall(ref State state);
        private static StateCall _get, _set;
        private static bool _verified;
        internal static void Capture(out State state)
        {
            if (_get == null)
            {
                var get = IL2CPP.il2cpp_resolve_icall("UnityEngine.Random::get_state_Injected");
                var set = IL2CPP.il2cpp_resolve_icall("UnityEngine.Random::set_state_Injected");
                if (get == IntPtr.Zero || set == IntPtr.Zero)
                    throw new InvalidOperationException("Unity Random state icalls unavailable.");
                _get = Marshal.GetDelegateForFunctionPointer<StateCall>(get);
                _set = Marshal.GetDelegateForFunctionPointer<StateCall>(set);
            }
            state = default;
            _get(ref state);
            if (!_verified)
            {
                try
                {
                    int first = UnityEngine.Random.Range(0, int.MaxValue);
                    Restore(state);
                    int repeated = UnityEngine.Random.Range(0, int.MaxValue);
                    if (first != repeated) throw new InvalidOperationException("Unity Random round-trip probe failed.");
                    _verified = true;
                }
                finally { Restore(state); }
            }
        }
        internal static void Restore(State state) { _set(ref state); }
#endif
    }

    [HarmonyPatch(typeof(StoreClientMono), nameof(StoreClientMono.OnArrived))]
    internal static class CustomerArrivalPatch
    {
        [HarmonyPostfix] private static void Postfix() => CustomerCheckpoint.Arrival();
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.GetNextClient))]
    internal static class CustomerNextBranchPatch
    {
        [HarmonyPrefix] private static void Prefix(PlayerStore __instance, out IntPtr __state)
            => __state = Id(__instance.currentClientInstance);
        [HarmonyPostfix] private static void Postfix(PlayerStore __instance, IntPtr __state)
            => CustomerCheckpoint.NextCustomer(__instance, __state);
    }

    [HarmonyPatch(typeof(MetaHelper), nameof(MetaHelper.WaitAndDo))]
    internal static class CustomerCallbackPatch
    {
        [HarmonyPrefix] private static void Prefix(ref Il2CppSystem.Action __1)
            => CustomerCheckpoint.GuardCallback(ref __1);
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.DismissCurrentClient))]
    internal static class CustomerDismissPatch
    {
        [HarmonyPrefix] private static void Prefix()
        {
            if (!CustomerCheckpoint.Restoring) CustomerCheckpoint.Dismissed();
        }
    }

    [HarmonyPatch(typeof(StoreShutterButton), nameof(StoreShutterButton.EndAndClose))]
    internal static class CustomerEndDayPatch
    {
        [HarmonyPostfix] private static void Postfix() => CustomerCheckpoint.Closing();
    }

    [HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.OnLeaveStore))]
    internal static class CustomerLeaveShopPatch
    {
        [HarmonyPrefix] private static void Prefix() => CustomerCheckpoint.Leaving();
    }

    [HarmonyPatch(typeof(EmporiumEntry), nameof(EmporiumEntry.OnArriveStore))]
    internal static class CustomerReturnShopPatch
    {
        [HarmonyPostfix] private static void Postfix() => CustomerCheckpoint.Returned();
    }

    [HarmonyPatch(typeof(DialogUIManager), nameof(DialogUIManager.StopAllTrackedDialogueCoroutines))]
    internal static class CustomerStopDialoguePatch
    {
        [HarmonyPostfix] private static void Postfix() => CustomerCheckpoint.DialogueCoroutinesStopped();
    }

    [HarmonyPatch(typeof(DialogUIManager), "FinishConversationNow")]
    internal static class CustomerGreetingFinishedPatch
    {
        [HarmonyPostfix] private static void Postfix() => CustomerCheckpoint.TryCapture();
    }

    [HarmonyPatch(typeof(BargainUIManager), nameof(BargainUIManager.OpenUI))]
    internal static class CustomerBargainOpenedPatch
    {
        [HarmonyPostfix] private static void Postfix() => CustomerCheckpoint.TraceBargain("OPEN");
    }

    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.Destroy))]
    internal static class CustomerDestroyPatch
    {
        [HarmonyPrefix] private static bool Prefix(GameItemElement __instance)
            => !CustomerCheckpoint.DeferDestroy(__instance);
    }

    [HarmonyPatch(typeof(GameItemElement), nameof(GameItemElement.IsDestroyed))]
    internal static class CustomerIsDestroyedPatch
    {
        [HarmonyPrefix] private static bool Prefix(GameItemElement __instance, ref bool __result)
        {
            if (!CustomerCheckpoint.WasDeleted(Id(__instance))) return true;
            __result = true;
            return false;
        }
    }
}
