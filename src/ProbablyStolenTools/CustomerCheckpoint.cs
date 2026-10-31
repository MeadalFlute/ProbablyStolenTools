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
        private static readonly CheckpointHistory<Snapshot> History = new(10, node => !node.IsClosed);
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
        internal static bool Restoring { get; private set; }
        internal static bool PatchesReady;
        internal static bool Enabled => Plugin.CfgCustomerCheckpoint?.Value == true && PatchesReady;
        internal static string Status { get; private set; } = "Waiting for a new customer";
        internal static IReadOnlyList<Snapshot> Nodes => History.Items;
        internal static int CustomerCount => Nodes.Count(node => !node.IsClosed);
        internal static bool IsFuture(Snapshot node) => History.IsFuture(node);
        internal static int Revision => History.Revision;
        internal static long ActiveId => _active?.Id ?? 0;

        internal sealed class Snapshot : IDisposable
        {
            internal long Id;
            internal string Label;
            internal string DisplayName;
            internal bool IsClosed;
            internal string ExpiredReason;
            internal int Day, Cash, Scene;
            internal IntPtr Store, Client;
            internal DateTime At;
            internal StateGraph Graph;
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
                Invalidate("Unsupported customer type: " + intent);
                return;
            }
            _armed = true;
            _arrivedAt = Time.unscaledTime;
            Status = "Waiting for the customer's opening dialogue";
            TryCapture();
        }

        internal static void Tick()
        {
            if (Restoring) return;
            Observe();
            if (!Enabled) { if (History.Items.Count > 0 || ClosedNode?.Graph != null) Clear("已禁用"); return; }
            if (_armed) TryCapture();
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
            if (store == null || (int)store.storeState != 1 || store.lockShutterButtonFlag == 0)
            {
                _shutterSince = 0;
                return;
            }

            var dialog = DialogUIManager.Instance;
            var mono = StoreClientMono.Instance;
            var ui = StoreUIManager.Instance;
            bool legitimatelyBusy = (dialog != null && (dialog.currentDialog != null || dialog.isConversationActiveFlag != 0))
                || (mono != null && mono.IsMoving())
                || store.isHandlingShootout || store.isClientBeingArrested || store.isGeneScannerScanning
                || (ui != null && (ui.isAboutToStartMainDialog || ui.isShootingMode))
                || (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem)
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

            if (held < 10f) return;
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
                EventLog.Write($"[Customer] CONTEXT CHANGED scene={Boundary.Scene}->{scene} day={Boundary.Day}->{day} " +
                    $"store={Boundary.Store}->{Ptr(store)} nodes={History.Items.Count} -> cleared");
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
            if (node.IsClosed) return "闭店";
            // displayName is the exact resolved name used by the game's dialogue/recorder.
            return string.IsNullOrWhiteSpace(node.DisplayName) ? "顾客" : node.DisplayName;
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
                && !store.isClientBeingArrested && !store.isGeneScannerScanning
                && StoreUIManager.Instance != null && !StoreUIManager.Instance.isAboutToStartMainDialog
                && (ItemMouseDragHandler.current == null || !ItemMouseDragHandler.current.IsDraggingItem)
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
            return store != null && store.currentClientInstance != null && store.isClientArrived
                && (int)store.storeState == 1 && !store.isClientBeingArrested && !store.isHandlingShootout
                && ui != null && !ui.isAboutToStartMainDialog && !ui.isShootingMode
                && StoreClientMono.Instance != null && !StoreClientMono.Instance.IsMoving()
                && dialog != null && dialog.currentDialog == null && dialog.isConversationActiveFlag == 0;
        }

        private static NativeStateAccess Access(PlayerStore store, out List<IntPtr> roots)
        {
#if MONO
            var access = new NativeStateAccess(typeof(UnityEngine.Object));
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
                "defaultIdleAmplitude", "defaultIdleFrequency", "ignoreOnLeftFlag", "fadeMode", "walkingIn");
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

        internal static void TryCapture()
        {
            if (!_armed || !Enabled || Restoring || Rewind.Restoring || Boundary.Attempted || _faulted) return;
            var store = Plugin.Store();
            if (!IsSettled(store)) { WatchSettle(store); return; }
            Boundary.MarkAttempt();
            _armed = false;
            Capture(store, false);
        }

        private static float _settleLogAt;

        /// <summary>节点迟迟建不起来时，把每个判定条件打出来，便于定位到底卡在哪一步。</summary>
        private static void WatchSettle(PlayerStore store)
        {
            if (Time.unscaledTime < _settleLogAt) return;
            _settleLogAt = Time.unscaledTime + 2f;
            try
            {
                var ui = StoreUIManager.Instance;
                var dialog = DialogUIManager.Instance;
                var mono = StoreClientMono.Instance;
                string B(bool v) => v ? "1" : "0";
                EventLog.Write("[Customer] settle wait: " +
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
                    " conv=" + (dialog == null ? -1 : dialog.isConversationActiveFlag));
            }
            catch (Exception e) { EventLog.Write("[Customer] settle watch failed: " + e.Message); }
        }

        private static void Capture(PlayerStore store, bool closed)
        {
            Snapshot candidate = null;
            try
            {
                var timing = System.Diagnostics.Stopwatch.StartNew();
                var access = Access(store, out var roots);
                long tAccess = timing.ElapsedMilliseconds;
                candidate = new Snapshot
                {
                    Id = ++_nextId, Label = closed ? "closed" : store.currentClientInstance.GetClientBlueprint().identifier,
                    DisplayName = closed ? null : store.currentClientInstance.GetClientBlueprint().displayName,
                    IsClosed = closed,
                    Day = Boundary.Day, Scene = Boundary.Scene, Store = Boundary.Store, Client = Boundary.Client,
                    Cash = store.GetCash(), At = DateTime.Now, Random = RNG.random
                };
                candidate.Graph = StateGraph.Capture(access, roots);
                long tWalk = timing.ElapsedMilliseconds;
                candidate.Customer = new CustomerVisual();
                candidate.Shop = new ShopPhaseVisual();
                if (candidate.Graph.SlotCount == 0) throw new InvalidOperationException("Empty business-state graph.");
                UnityRandomState.Capture(out candidate.UnityRandom);
                foreach (var id in candidate.Graph.Objects)
                {
                    if (Is<GameItemElement>(id))
                    {
#if MONO
                        var item = (GameItemElement)ManagedObjects.Get(id);
#else
                        var item = new GameItemElement(id);
#endif
                        candidate.Items.Add(id, item);
                        if (item.IsDestroyed())
                        {
                            bool member = IsInventoryMember(item);
                            EventLog.Write($"[Customer] RENDERLESS item={item.identifier} uniqueId={item.uniqueId} " +
                                $"inventoryMember={member} source={candidate.Graph.SourceOf(id)}");
                            // 正式版存在没有渲染体、却仍是背包成员的物品（如 cassette_player）。
                            // 它们只参与业务状态，不参与视图恢复；此处跳过而不是中止整次捕获，
                            // 否则 F6 节点会一直存不下来（表现为"回溯只有旧节点"）。
                            if (member)
                                EventLog.Write($"[Customer] RENDERLESS inventory member skipped: {item.identifier}");
                            continue;
                        }
                        candidate.RenderedItems.Add(id, item);
                        AddVisual(candidate, item.handler);
                        AddVisual(candidate, item.fakeMiddle);
                    }
                    if (Is<PixelWindow>(id))
                    {
#if MONO
                        var window = (PixelWindow)ManagedObjects.Get(id);
#else
                        var window = new PixelWindow(id);
#endif
                        candidate.Windows.Add(id, window);
                        AddVisual(candidate, window.handler);
                    }
                }
                if (candidate.RenderedItems.Count == 0) throw new InvalidOperationException("No live inventory items reached from snapshot roots.");
                long tItems = timing.ElapsedMilliseconds;
                // 捕获整段都在同一帧内完成，理论上不会有人并发改状态；再做一次全量比对等于把
                // 40k 对象/30 万槽位再走一遍，是建节点卡顿的主要来源之一。默认关闭，排查时可开。
                if (Plugin.CfgVerifyCapture.Value)
                {
                    var diff = candidate.Graph.Differences();
                    if (diff.Count != 0) throw new InvalidOperationException("Capture changed while reading: " + string.Join(", ", diff));
                }
                long tVerify = timing.ElapsedMilliseconds;
                if (closed)
                {
                    History.Remove(ClosedNode);
                    ClosedNode = candidate;
                }
                History.Add(candidate, commitBranch: !closed);
                if (ClosedNode?.Graph == null) ClosedNode = null;
                _active = candidate;
                candidate = null;
                RebuildRetainedItems();
                Boundary.MarkReady();
                Status = "Checkpoint: " + Name(_active);
                EventLog.Write($"[Customer] CAPTURE id={_active.Id} client={_active.Label} history={CustomerCount}/10 " +
                    $"objects={_active.Graph.ObjectCount} slots={_active.Graph.SlotCount} bytes={_active.Graph.ByteCount} " +
                    $"items={_active.Items.Count} renderedItems={_active.RenderedItems.Count} windows={_active.Windows.Count} " +
                    $"after={(Time.unscaledTime - _arrivedAt):0.00}s " +
                    $"ms(access={tAccess},walk={tWalk - tAccess},items={tItems - tWalk},verify={tVerify - tItems},total={timing.ElapsedMilliseconds})");
                TraceBargain("CAPTURE");
            }
            catch (Exception e)
            {
                candidate?.Dispose();
                Boundary.Invalidate();
                Status = "Capture failed: " + e.Message;
                EventLog.Write("[Customer] CAPTURE FAILED: " + e);
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
                PanelNotice.Show("回溯失败后需先重新载入游戏", true);
                return;
            }
            if (!Enabled || target?.Graph == null || (!History.Items.Contains(target) && target != ClosedNode))
            {
                EventLog.Write("[Customer] F6 unavailable: " + Status + ". No save-file fallback.");
                PanelNotice.Show("暂无可用节点", true);
                return;
            }
            var store = Plugin.Store();
            var ui = StoreUIManager.Instance;
            var phone = PhoneUIManager.Instance;
            if (store == null || target.Store != Ptr(store) || target.Scene != Boundary.Scene || target.Day != Boundary.Day
                || _outside || ((int)store.storeState != 0 && (int)store.storeState != 1) || store.isClientBeingArrested
                || store.isHandlingShootout || store.isGeneScannerScanning
                || ui == null || ui.isShootingMode || ui.isAboutToStartMainDialog || _closeArmed
                || StoreClientMono.Instance == null || StoreClientMono.Instance.IsMoving()
                || (ItemMouseDragHandler.current != null && ItemMouseDragHandler.current.IsDraggingItem)
                || GameFields.PhoneBusy(phone))
            {
                EventLog.Write("[Customer] F6 refused during dragging, a phone call, scanning, or a customer transition.");
                PanelNotice.Show("请先结束当前操作", true);
                return;
            }
            bool changingPhase = ((int)store.storeState == 0) != target.IsClosed;
            var phaseDialog = DialogUIManager.Instance;
            if (changingPhase && (PendingCallbacks.Count != 0
                || phaseDialog == null || phaseDialog.currentDialog != null
                || phaseDialog.isConversationActiveFlag != 0 || phaseDialog.isWaitingForManualAdvance
                || !ShopPhaseVisual.IsSettled()))
            {
                PanelNotice.Show("请等待当前对话和关门动画结束", true);
                return;
            }

            StateGraph current = null;
            bool wrote = false;
            Restoring = true;
            float scale = Time.timeScale;
            try
            {
                // Preflight before any mutation, including current objects created since the checkpoint.
                foreach (var view in target.Visuals) view.Validate();
                target.Customer.Validate();
                target.Shop.Validate();
                var access = Access(store, out var roots);
                current = StateGraph.Capture(access, roots);
                target.Graph.Differences();
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
                int countersBefore = CounterCount();
                target.Graph.Restore();
                RNG.random = target.Random;
                foreach (var id in target.RenderedItems.Keys) Deleted.Remove(id);
                foreach (var view in target.Visuals) view.Restore();
                target.Shop.Restore();
                target.Customer.Restore();
                foreach (var item in target.RenderedItems.Values)
                {
                    GameFields.Set(item, "validateLock", false);
                    item.Validate();
                    // Validate only reloads the image when spriteChanged is true. That flag
                    // is normally false in a settled snapshot, even if the live image changed.
                    item.ValidatePartialSprite();
                }
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
                var diff = target.Graph.Differences();
                if (diff.Count != 0)
                {
                    // Validate()/ValidatePartialSprite() 会重建 TagSystem 的字典，导致字典内部
                    // 布局与快照不一致。再写一遍业务图把标签状态压回去，然后重新校验。
                    target.Graph.Restore();
                    diff = target.Graph.Differences();
                }
                if (diff.Count != 0)
                    EventLog.Write("[Customer] RESTORE WARN diff=" + string.Join(", ", diff));
                // Only discard abandoned views after the restored business graph passes validation.
                foreach (var w in newWindows)
                    if (w.handler != null) w.handler.gameObject.SetActive(false);
                foreach (var item in newItems) DiscardRenderer(item);
                int countersAfter = CounterCount();
                if (countersBefore >= 0 && countersAfter != countersBefore)
                    EventLog.Write($"[Customer] restore counter items {countersBefore} -> {countersAfter} (discarded views={newItems.Count})");
                // Hidden retained objects may no longer be reachable from the current business graph.
                foreach (var pair in RetainedItems)
                    if (!target.RenderedItems.ContainsKey(pair.Key)) DiscardRenderer(pair.Value);
                // 没有归属容器、又不在当前节点里的物品视图是“幽灵”：数据被回退掉了，
                // 只剩一个看得见、点得动但买不了的壳，还会和别的物品重叠。统一扫掉。
                int staleFakes = HideStaleFakeViews(target);
                if (staleFakes > 0)
                    EventLog.Write($"[Customer] hid {staleFakes} stale fake view node(s) after restore");
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
                EventLog.Write($"[Customer] RESTORE OK id={target.Id} client={target.Label} history={CustomerCount}/10 diff={diff.Count}");
                TraceBargain("AFTER RESTORE");
            }
            catch (Exception e)
            {
                EventLog.Write("[Customer] RESTORE FAILED" + (wrote ? " AFTER WRITE" : " (preflight)") + ": " + e);
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
                current?.Dispose();
                Time.timeScale = scale;
                Restoring = false;
            }
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
        /// <summary>
        /// 恢复后把残留的“假视图”（拖拽/展示用的 fake 节点）关掉。
        /// 它们没有业务数据支撑，表现为柜台上一件买不了、还能和别的物品重叠的幽灵物品。
        /// </summary>
        private static int HideStaleFakeViews(Snapshot target)
        {
            int hidden = 0, orphanLogged = 0;
            try
            {
                foreach (var item in target.RenderedItems.Values)
                {
                    if (item == null) continue;
                    hidden += HideNode(item.fakeMiddle);
                    hidden += HideNode(item.fakeInner);
                    hidden += HideNode(item.fakeRotator);
                    hidden += HideNode(item.fakeBackground);
                    // 记录“有视图但不在任何容器里”的物品，方便定位幽灵来源。
                    bool parented = false;
                    try { parented = item.parentInventory != null; } catch { parented = true; }
                    if (!parented && orphanLogged < 12 && !item.IsDestroyed())
                    {
                        orphanLogged++;
                        EventLog.Write($"[Customer] orphan item view id={item.uniqueId} identifier={item.identifier}");
                    }
                }
            }
            catch (Exception e) { Plugin.LogSource?.LogWarning("[Customer] fake view cleanup failed: " + e.Message); }
            return hidden;
        }

        private static int HideNode(TreeNodeRender node)
        {
            try
            {
                if (node == null || node.gameObject == null || !node.gameObject.activeSelf) return 0;
                node.gameObject.SetActive(false);
                return 1;
            }
            catch { return 0; }
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
            if (!Enabled || _active?.Graph == null) return;
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

        private static void AddVisual(Snapshot snapshot, TreeNodeRender node)
        {
            if (node != null && !snapshot.Visuals.Any(v => Id(v.Node) == Id(node)))
                snapshot.Visuals.Add(new VisualState(node));
        }

        internal sealed class CustomerVisual
        {
            private readonly StoreClientMono _mono = StoreClientMono.Instance;
            private readonly Sprite _sprite;
            private readonly Color _color;
            private readonly Vector3 _position;
            private readonly bool _active, _parentActive;
            internal CustomerVisual()
            {
                Validate();
                _sprite = _mono.image.sprite;
                _color = _mono.image.color;
                _position = _mono.transform.localPosition;
                _active = _mono.gameObject.activeSelf;
                _parentActive = _mono.transform.parent.gameObject.activeSelf;
            }
            internal void Validate()
            {
                if (_mono == null || _mono.image == null || _mono.transform.parent == null)
                    throw new InvalidOperationException("Customer view no longer exists.");
            }
            internal void Restore()
            {
                Validate();
                _mono.transform.parent.gameObject.SetActive(_parentActive);
                _mono.gameObject.SetActive(_active);
                _mono.transform.localPosition = _position;
                _mono.image.sprite = _sprite;
                _mono.image.color = _color;
#if MONO
                AccessTools.Method(typeof(StoreClientMono), "SetSolidBlend").Invoke(_mono, new object[] { 0f });
#else
                _mono.SetSolidBlend(0f);
#endif
            }
        }

        internal sealed class VisualState
        {
            internal readonly TreeNodeRender Node;
            private readonly Transform _parent;
            private readonly TreeNodeRender _nodeParent;
            private readonly Vector3 _position, _scale;
            private readonly Quaternion _rotation;
            private readonly bool _active;
            internal VisualState(TreeNodeRender node)
            {
                Node = node;
                var t = node.transform;
                _parent = t.parent; _position = t.localPosition; _scale = t.localScale;
                _nodeParent = node.parent;
                _rotation = t.localRotation; _active = node.gameObject.activeSelf;
            }
            internal void Validate()
            {
                if (Node == null || _parent == null)
                    throw new InvalidOperationException("A checkpoint view or its parent was destroyed.");
            }
            internal void Restore()
            {
                Validate();
                var t = Node.transform;
#if MONO
                AccessTools.PropertySetter(typeof(TreeNodeRender), "parent").Invoke(Node, new object[] { _nodeParent });
#else
                Node.parent = _nodeParent;
#endif
                t.SetParent(_parent, false); t.localPosition = _position;
                t.localScale = _scale; t.localRotation = _rotation;
                Node.gameObject.SetActive(_active);
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
