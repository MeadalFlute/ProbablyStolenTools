using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 开业前文件检查点：只复制游戏自己写好的存档，F7 通过主菜单重载。
    /// 顾客级 F6 内存回退独立实现于 CustomerCheckpoint，不使用这里的文件恢复。
    /// </summary>
    internal static class Rewind
    {
        internal sealed class Point
        {
            public string Label;
            public string Path;
            public int Cash;
            public int SlotId;
            public int State;      // 0=CLOSED 1=OPEN 2=MORNING；只有非 OPEN 的存档能被游戏正确载入
            public List<string> ClientIds;   // storeClientManager.clientStack 里顾客的 identifier（存档不保存，需自己补）
            public int Day = -1;
            public DateTime At;
            public bool IsDayStart;
            public string RunId;
        }

        internal static readonly List<Point> Points = new List<Point>();
        internal static bool Busy;
        internal static bool Restoring => Busy || PendingMenuReload || PendingPoint != null;
        private static IntPtr _captureStore;
        private static int _captureDay = -1;
        private static float _nextCapture;
        internal static void Saved() { _captureDay = -1; _nextCapture = Time.unscaledTime + 1f; }

        /// <summary>
        /// F7 reloads the slot through the menu, which can leave the persistent overlay
        /// alive while replacing the PlayerStore underneath it. Start a fresh capture
        /// identity without writing another save file or duplicating the day-start node.
        /// </summary>
        internal static void ResetAfterFileReload()
        {
            Busy = false;
            var store = Plugin.Store();
            _captureStore = Id(store);
            _captureDay = store == null ? -1 : StoreStation.GetDayCounter();
            _nextCapture = Time.unscaledTime + 2f;
        }

        internal static void Tick()
        {
            if (!Plugin.CfgRewind.Value || Restoring
                || Time.unscaledTime < _nextCapture) return;
            _nextCapture = Time.unscaledTime + 2f;
            var store = Plugin.Store();
            if (store == null || MainMenuUIController.Instance != null || StoreUIManager.Instance == null) return;
            int day = StoreStation.GetDayCounter();
            if (_captureStore == Id(store) && _captureDay == day) return;
            if (Capture("开业前", true))
            {
                _captureStore = Id(store);
                _captureDay = day;
            }
        }

        internal static string Root => Path.Combine(Application.persistentDataPath, "PSTools_Checkpoints");

        // ---------- 槽位文件定位 ----------

        internal static string ActiveSlotPath(out int slotId)
        {
            slotId = -1;
            PlayerStore store = null;
            store = Plugin.Store();
            if (store == null) return null;

            try { slotId = store.saveSlotId; } catch { }

            if (slotId >= 0)
            {
                string p = Path.Combine(Application.persistentDataPath, "save_" + slotId + ".es3");
                if (File.Exists(p)) return p;
            }

            // 兜底：找最近的 save_*.es3
            try
            {
                string best = null;
                DateTime bestTime = DateTime.MinValue;
                foreach (var f in System.IO.Directory.GetFiles(Application.persistentDataPath, "save_*.es3"))
                {
                    var t = File.GetLastWriteTimeUtc(f);
                    if (t > bestTime) { bestTime = t; best = f; }
                }
                return best;
            }
            catch { return null; }
        }

        // ---------- 记录 ----------

        internal static bool Capture(string label, bool isDayStart = true)
        {
            if (!Plugin.CfgRewind.Value || Busy) return false;
            try
            {
                if (Plugin.Store() == null) return false;

                // 不再自己调 SaveGame()：那会把 CLOSED/OPEN 的中间态写进用户存档。
                // 只复制自然存档，并按文件内容验证日期、阶段与槽位。
                int slot;
                string active = ActiveSlotPath(out slot);
                if (active == null || !File.Exists(active))
                {
                    Plugin.LogSource.LogWarning($"[Rewind] active slot file not found (slot={slot}).");
                    return false;
                }

                var bytes = File.ReadAllBytes(active);
                var metadata = SaveMetadata.Read(System.Text.Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF'));
                // Metadata must describe the bytes copied, never the live (possibly later) state.
                if (metadata.Day != StoreStation.GetDayCounter() || metadata.State != 2 || metadata.Slot != slot
                    || string.IsNullOrEmpty(metadata.RunId) || metadata.RunId != Plugin.Store().runID)
                    return false;
                System.IO.Directory.CreateDirectory(Root);
                string safe = Sanitize(label);
                string dst = Path.Combine(Root, $"{DateTime.Now:yyyyMMdd_HHmmss_fffffff}_{safe}.es3");
                File.WriteAllBytes(dst, bytes);

                var p = new Point
                {
                    Label = label,
                    Path = dst,
                    Cash = metadata.Cash,
                    SlotId = slot,
                    State = metadata.State,
                    ClientIds = SnapshotClientIds(),
                    Day = metadata.Day,
                    At = DateTime.Now,
                    IsDayStart = isDayStart,
                    RunId = metadata.RunId,
                };
                Points.Add(p);

                int max = Math.Max(1, Plugin.CfgMaxPoints.Value);
                while (Points.Count > max)
                {
                    var old = Points[0];
                    Points.RemoveAt(0);
                    try { if (File.Exists(old.Path)) File.Delete(old.Path); } catch { }
                }

                Plugin.LogSource.LogInfo($"[Rewind] checkpoint '{label}' saved ({Points.Count} total, cash={p.Cash}).");
                return true;
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Rewind] capture failed: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        // ---------- 回退 ----------

        internal static void RestoreLast()
        {
            for (int i = Points.Count - 1; i >= 0; i--)
            {
                if (Points[i].State != 1) { Restore(Points[i]); return; }
            }
            EventLog.Write("[Rewind] 没有可回退的节点（营业中的节点游戏无法恢复）。");
        }

        internal static void RestoreDayStart()
        {
            var store = Plugin.Store();
            if (store == null) return;
            for (int i = Points.Count - 1; i >= 0; i--)
            {
                if (Points[i].IsDayStart && Points[i].State == 2 && Points[i].SlotId == store.saveSlotId
                    && Points[i].RunId == store.runID
                    && Points[i].Day == StoreStation.GetDayCounter()) { Restore(Points[i]); return; }
            }
            EventLog.Write("[Rewind] 还没有“开业前”的可用节点。");
            PanelNotice.Show("暂无今日开业前存档节点", true);
        }

        /// <summary>
        /// 回到上一天：从 PSTools_Checkpoints 里挑出「更早的、最近一次开业前」的存档文件。
        /// 与 F7 的区别只在于允许跨天（文件本身就是整份存档，跨天恢复没有额外风险）。
        /// </summary>
        internal static void RestorePreviousDay()
        {
            var store = Plugin.Store();
            if (store == null)
            {
                PanelNotice.Show("还没进店，无法回到上一天", true);
                return;
            }
            int today;
            try { today = StoreStation.GetDayCounter(); }
            catch { return; }

            Point best = null;
            try
            {
                if (!System.IO.Directory.Exists(Root)) { PanelNotice.Show("还没有任何存档节点", true); return; }
                foreach (var file in System.IO.Directory.GetFiles(Root, "*.es3"))
                {
                    SaveMetadata meta;
                    try
                    {
                        meta = SaveMetadata.Read(File.ReadAllText(file).TrimStart('\uFEFF'));
                    }
                    catch { continue; }
                    if (meta.State != 2) continue;                       // 只接受开业前(MORNING)
                    if (meta.Slot != store.saveSlotId) continue;
                    if (meta.Day >= today) continue;                     // 只要更早的天
                    if (!string.IsNullOrEmpty(meta.RunId) && meta.RunId != store.runID) continue;
                    var at = File.GetLastWriteTime(file);
                    if (best != null && (meta.Day < best.Day || (meta.Day == best.Day && at <= best.At))) continue;
                    best = new Point
                    {
                        Label = "上一天",
                        Path = file,
                        Cash = meta.Cash,
                        SlotId = meta.Slot,
                        State = meta.State,
                        Day = meta.Day,
                        At = at,
                        IsDayStart = true,
                        RunId = meta.RunId
                    };
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning("[Rewind] previous-day scan failed: " + e.Message);
            }

            if (best == null)
            {
                EventLog.Write($"[Rewind] 没有更早的开业前节点（today={today}）。");
                PanelNotice.Show("暂无更早的开业前存档", true);
                return;
            }

            EventLog.Write($"[Rewind] 回到上一天 day={best.Day}（today={today}）file={Path.GetFileName(best.Path)}");
            PanelNotice.Show($"回到第 {best.Day} 天");
            Restore(best, allowDayChange: true);
        }

        // ---------- 整日回退的二次确认 ----------

        internal enum WholeDayAction { None, DayStart, PreviousDay }

        internal static WholeDayAction PendingWholeDay = WholeDayAction.None;
        private static float _pendingUntil;

        internal static void RequestDayStart() => Request(WholeDayAction.DayStart);
        internal static void RequestPreviousDay() => Request(WholeDayAction.PreviousDay);

        private static void Request(WholeDayAction action)
        {
            if (action == WholeDayAction.None) return;
            if (!Plugin.CfgConfirmWholeDay.Value) { Execute(action); return; }
            PendingWholeDay = action;
            _pendingUntil = Time.unscaledTime + 20f;

            var ui = StoreUIManager.Instance;
            var panel = ui == null ? null : ui.confirmPanelUIManager;
            if (panel == null)
            {
                EventLog.Write("[Rewind] confirm panel unavailable; executing immediately.");
                Execute(action);
                PendingWholeDay = WholeDayAction.None;
                return;
            }

            string other = action == WholeDayAction.DayStart
                ? Plugin.CfgKeyPrevDay.Value.ToString()
                : Plugin.CfgKeyDay.Value.ToString();
            string what = action == WholeDayAction.DayStart ? "回到本日开业前" : "回到上一天";
            string body = $"{what} 会覆盖当前存档槽位（覆盖前会保留一份 .pre_restore 备份）。\n\n" +
                          $"点击下方按钮确认，或直接再按一次 {other} 确认。";
            try
            {
                panel.OpenPanel(body, ConfirmPending, CancelPending, false);
                EventLog.Write($"[Rewind] confirm requested: {what} (confirm key {other})");
            }
            catch (Exception e)
            {
                EventLog.Write($"[Rewind] confirm panel failed: {e.GetType().Name}: {e.Message}");
                Execute(action);
                PendingWholeDay = WholeDayAction.None;
            }
        }

        internal static void ConfirmPending()
        {
            var action = PendingWholeDay;
            PendingWholeDay = WholeDayAction.None;
            if (action == WholeDayAction.None) return;
            EventLog.Write("[Rewind] confirm accepted.");
            Execute(action);
        }

        internal static void CancelPending()
        {
            if (PendingWholeDay == WholeDayAction.None) return;
            PendingWholeDay = WholeDayAction.None;
            EventLog.Write("[Rewind] confirm cancelled.");
        }

        private static void Execute(WholeDayAction action)
        {
            if (action == WholeDayAction.DayStart) RestoreDayStart();
            else if (action == WholeDayAction.PreviousDay) RestorePreviousDay();
        }

        /// <summary>确认框被外部关掉或超时，清掉待确认状态。</summary>
        internal static void TickPending()
        {
            if (PendingWholeDay == WholeDayAction.None) return;
            var ui = StoreUIManager.Instance;
            var panel = ui == null ? null : ui.confirmPanelUIManager;
            bool visible = false;
            try { visible = panel != null && panel.panel != null && panel.panel.activeInHierarchy; } catch { }
            if (!visible || Time.unscaledTime > _pendingUntil)
            {
                PendingWholeDay = WholeDayAction.None;
                EventLog.Write("[Rewind] confirm dismissed.");
            }
        }


        internal static void Restore(Point p, bool allowDayChange = false)
        {
            if (Busy || PendingMenuReload || PendingPoint != null || CustomerCheckpoint.Restoring) return;
            if (p == null || !File.Exists(p.Path))
            {
                Plugin.LogSource.LogWarning("[Rewind] checkpoint file missing.");
                return;
            }

            // 游戏存档不保存“当前顾客”，营业中(OPEN)的状态写回去会让当天变成 0 顾客并直接结算。
            if (p.State == 1 && !Plugin.CfgAllowOpen.Value)
            {
                EventLog.Write($"[Rewind] 跳过 '{p.Label}'：该节点处于营业中(OPEN)，游戏存档不支持中途恢复。");
                return;
            }

            Busy = true;
            try
            {
                int slot;
                string active = ActiveSlotPath(out slot);
                if (active == null || slot != p.SlotId
                    || (!allowDayChange && p.Day != StoreStation.GetDayCounter())
                    || p.RunId != Plugin.Store()?.runID)
                {
                    Plugin.LogSource.LogError("[Rewind] cannot locate active slot file.");
                    return;
                }

                EventLog.Write($"[Rewind] restoring '{p.Label}' -> {Path.GetFileName(active)} (mode={Plugin.CfgRestoreMode.Value})");

                // 先给当前状态留个后路
                try { File.Copy(active, active + ".pre_restore", true); } catch { }

                File.Copy(p.Path, active, true);

                string mode = Plugin.CfgRestoreMode.Value;
                PendingPoint = p;
                if (mode == "menu")
                {
                    GoToMenuAndAutoLoad(slot);
                    return;
                }
                if (mode == "scene")
                {
                    ReloadScene(slot);
                    return;
                }

                bool loaded = false;
                try
                {
                    PlayerStore.Instance.LoadGame();
                    loaded = true;
                    Plugin.LogSource.LogInfo("[Rewind] LoadGame() returned.");
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogWarning($"[Rewind] LoadGame() threw: {e.GetType().Name}: {e.Message}");
                }

                // 自动模式：LoadGame 之后如果现金还是回退前那个值，说明内存没被覆盖，改用重载场景
                if (mode == "auto")
                {
                    int now = SafeCash();
                    if (!loaded || (now != p.Cash && now >= 0 && p.Cash >= 0))
                    {
                        EventLog.Write($"[Rewind] LoadGame() did not apply (cash now={now}, expect={p.Cash}); reloading scene.");
                        ReloadScene(slot);
                    }
                    else
                    {
                        EventLog.Write("[Rewind] LoadGame() applied (cash matched checkpoint).");
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Rewind] restore failed: {e.GetType().Name}: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                Busy = false;
            }
        }

        // ---------- 回主菜单再自动载入（唯一被实测证明可用的路径） ----------

        internal static bool PendingMenuReload;
        internal static int PendingSlot = -1;
        internal static Point PendingPoint;

        private static void GoToMenuAndAutoLoad(int slotId)
        {
            PendingMenuReload = true;
            PendingSlot = slotId;
            try
            {
                try { PlayerPrefs.SetInt("activeSaveSlotId", slotId); PlayerPrefs.Save(); } catch { }
                EventLog.Write($"[Rewind] loading menu scene (index 0) to reload slot {slotId} ...");
                UnityEngine.SceneManagement.SceneManager.LoadScene(0);
            }
            catch (Exception e)
            {
                EventLog.Write($"[Rewind] menu reload failed ({e.GetType().Name}: {e.Message}); falling back to scene reload.");
                ReloadScene(slotId);
            }
        }

        private static void ReloadScene(int slotId)
        {
            try
            {
                try { PlayerPrefs.SetInt("activeSaveSlotId", slotId); PlayerPrefs.Save(); } catch { }
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                UnityEngine.SceneManagement.SceneManager.LoadScene(scene.name);
                EventLog.Write($"[Rewind] reloaded scene '{scene.name}'.");
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogError($"[Rewind] scene reload failed: {e.GetType().Name}: {e.Message}");
                EventLog.Write("[Rewind] save file restored; quit to main menu and load the slot manually.");
            }
        }

        // ---------- 工具 ----------

        private static int SafeCash()
        {
            try { return PlayerStore.Instance.GetCash(); } catch { return -1; }
        }

        private static int SafeState()
        {
            try { return (int)PlayerStore.Instance.storeState; } catch { return -1; }
        }

        /// <summary>读出当前等待队列里的顾客 id（存档不会保存这些，只能我们自己记）。</summary>
        internal static List<string> SnapshotClientIds()
        {
            var ids = new List<string>();
            try
            {
                var store = Plugin.Store();
                if (store == null) return ids;
                var mgr = store.storeClientManager;
                if (mgr == null) return ids;

                // 当前正在接待的顾客（不在 clientStack 里，回退后就会“少一个”）
                try
                {
                    var inst = store.currentClientInstance;
                    if (inst != null)
                    {
                        var bp = inst.GetClientBlueprint();
                        if (bp != null && !string.IsNullOrEmpty(bp.identifier))
                        {
                            ids.Add(bp.identifier);
                            Plugin.LogSource.LogInfo($"[Rewind] snapshot: current client = {bp.identifier}");
                        }
                    }
                }
                catch (Exception e)
                {
                    Plugin.LogSource.LogInfo($"[Rewind] currentClientInstance unavailable: {e.GetType().Name}");
                }

                var stack = mgr.clientStack;
                if (stack != null)
                {
                    int n = stack.Count;
                    for (int i = 0; i < n && i < 30; i++)
                    {
                        var c = stack[i];
                        if (c == null) continue;
                        var id = c.identifier;
                        if (!string.IsNullOrEmpty(id)) ids.Add(id);
                    }
                }
                Plugin.LogSource.LogInfo($"[Rewind] snapshot: {ids.Count} waiting client(s) [{string.Join(",", ids.ToArray())}]");
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Rewind] SnapshotClientIds failed: {e.GetType().Name}: {e.Message}");
            }
            return ids;
        }
        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "cp";
            foreach (char c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            return s.Length > 40 ? s.Substring(0, 40) : s;
        }
    }

    internal static class EventLog
    {
        internal static string Path => System.IO.Path.Combine(Application.persistentDataPath, "PSTools.log");

        internal static void Write(string msg)
        {
            try
            {
                System.IO.File.AppendAllText(Path, $"[{DateTime.Now:HH:mm:ss}] {msg}{Environment.NewLine}");
            }
            catch { }
            Plugin.LogSource.LogInfo(msg);
        }
    }

    /// <summary>游戏自然保存后安排只读检查，等待其延迟写盘完成。</summary>
    internal static class RewindPatches
    {
        [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.SaveGame))]
        [HarmonyPostfix]
        public static void AfterSaveGame()
        {
            OverlayBootstrap.Ensure();
            Rewind.Saved();
        }
    }

    /// <summary>热键 + 事件日志。IMGUI 在这个 build 里被裁掉了，UI 用日志替代。</summary>
    public class RewindOverlay : MonoBehaviour
    {
        private bool _inputBroken;

        // 自测状态机（Debug.SelfTest）
        private int _testStage;
        private float _testT;
        private bool _testOpened;

        // 回退：回菜单后自动载入
        private int _menuStage;
        private float _menuT;

        // 载入后的收尾（随机数 / 补顾客）
        private int _resumeStage;
        private float _resumeT;
        // 载入期间保留节点信息；PendingPoint 会提前清空，避免 Restoring 挡住新顾客的捕获
        private Rewind.Point _resumePoint;

        // 进店检查点
        private bool _bootDone;
        private float _bootTimer;

        // 轮询式检查点
        private int _lastState = -1;
        private bool _lastArrived;
        private int _lastStackCount = -1;

#if !MONO
        public RewindOverlay() : base(Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorPointer<RewindOverlay>())
        {
            Il2CppInterop.Runtime.Injection.ClassInjector.DerivedConstructorBody(this);
        }

        public RewindOverlay(IntPtr ptr) : base(ptr) { }
#endif

        private void Update()
        {
            try
            {
                RunSelfTest();
                MenuReloadStep();
                PostLoadResume();
                Rewind.Tick();
                Rewind.TickPending();
                QuickInspectionPatch.Tick();
                DialogueSkip.Tick();
                CustomerCheckpoint.Tick();
                Transaction.Tick();
                CashWatchdog.Tick();
            }
            catch (Exception e)
            {
                if (!_inputBroken)
                {
                    _inputBroken = true;
                    Plugin.LogSource.LogWarning($"[Overlay] update failed: {e}");
                }
            }

            if (_inputBroken) return;
            try
            {
                if (Plugin.Down(Plugin.CfgKeyPrev))
                    CustomerCheckpoint.Restore();

                // 整日回退会覆盖存档槽位，默认先弹确认；确认框未关时，
                // 再按“相反”的那个热键直接确认（同键则取消），避免连按误触。
                bool dayKey = Plugin.Down(Plugin.CfgKeyDay);
                bool prevDayKey = Plugin.CfgKeyPrevDay != null && Plugin.Down(Plugin.CfgKeyPrevDay);

                if (Rewind.PendingWholeDay != Rewind.WholeDayAction.None)
                {
                    if (Rewind.PendingWholeDay == Rewind.WholeDayAction.DayStart && prevDayKey)
                    {
                        prevDayKey = false; dayKey = false;
                        Rewind.ConfirmPending();
                    }
                    else if (Rewind.PendingWholeDay == Rewind.WholeDayAction.PreviousDay && dayKey)
                    {
                        prevDayKey = false; dayKey = false;
                        Rewind.ConfirmPending();
                    }
                    else if (dayKey || prevDayKey)
                    {
                        prevDayKey = false; dayKey = false;
                        Rewind.CancelPending();
                    }
                }

                if (dayKey) Rewind.RequestDayStart();
                if (prevDayKey) Rewind.RequestPreviousDay();
            }
            catch (Exception e)
            {
                _inputBroken = true;
                Plugin.LogSource.LogWarning($"[Overlay] input broken: {e}");
            }
        }

        /// <summary>
        /// 观察式“进店”检查点：不依赖具体加载方法，等 PlayerStore 出现并稳定几秒后抓一次。
        /// </summary>
        private void BootCapture()
        {
            if (_bootDone) return;
            if (!Plugin.CfgRewind.Value) return;

            PlayerStore store = null;
            store = Plugin.Store();

            if (store == null)
            {
                _bootTimer = 0f;
                return;
            }

            _bootTimer += Time.deltaTime;
            if (_bootTimer < 3f) return;

            _bootDone = true;
            Rewind.Capture("进店", true);
        }

        /// <summary>
        /// 轮询式检查点：不依赖具体方法名，直接盯 PlayerStore 的状态变化。
        ///   storeState 进入 MORNING → 开业前
        ///   storeState 进入 OPEN    → 开门
        ///   isClientArrived 由 false 变 true → 某位顾客到来
        /// </summary>
        private void PollCheckpoints()
        {
            if (!Plugin.CfgRewind.Value) return;

            PlayerStore s = null;
            s = Plugin.Store();

            if (s == null)
            {
                _lastState = -1;
                _lastArrived = false;
                return;
            }

            if (Time.frameCount % 5 != 0) return; // 每 5 帧检查一次就够

            int state = -1;
            bool arrived = false;
            try { state = (int)s.storeState; } catch { }
            try { arrived = s.isClientArrived; } catch { }

            if (state != _lastState)
            {
                if (state == 2) Rewind.Capture("开业前", true);   // MORNING
                else if (state == 1 && Plugin.CfgCaptureMidDay.Value)
                    Rewind.Capture("开门", false);                 // OPEN（默认不记：存档恢复不了）
                _lastState = state;
            }

            if (!Plugin.CfgCaptureMidDay.Value)
            {
                _lastArrived = arrived;
                return;
            }

            if (arrived && !_lastArrived)
                Rewind.Capture("顾客到来");

            // 队列人数变化也记一个点（这种时刻才带着顾客 id）
            int stackCount = -1;
            try
            {
                var mgr = s.storeClientManager;
                if (mgr != null && mgr.clientStack != null) stackCount = mgr.clientStack.Count;
            }
            catch { }
            if (stackCount != _lastStackCount)
            {
                if (stackCount > 0) Rewind.Capture($"队列{stackCount}人");
                _lastStackCount = stackCount;
            }

            _lastArrived = arrived;        }

        /// <summary>
        /// 存档载入之后：把当时的随机数状态塞回去，营业中(OPEN)的节点再补一位顾客，
        /// 让当天能继续跑而不是被判定为 0 顾客直接结算。
        /// </summary>
        private void PostLoadResume()
        {
            var p = _resumePoint ?? Rewind.PendingPoint;
            if (p == null) { _resumeStage = 0; return; }

            if (_resumeStage == 0)
            {
                if (Rewind.PendingMenuReload) return;
                PlayerStore s = null;
                s = Plugin.Store();
                if (s == null) return;
                // 新存档已经就位：立刻清掉“恢复中”标记并重置节点系统，
                // 否则接下来几秒内到店的顾客会被 Restoring 挡掉、或稍后被延迟的重置清空。
                _resumePoint = p;
                Rewind.PendingPoint = null;
                Rewind.ResetAfterFileReload();
                ResetPollingAfterFileReload();
                CustomerCheckpoint.ResetAfterFileReload();
                _resumeStage = 1;
                _resumeT = Time.time;
                return;
            }

            if (_resumeStage == 1)
            {
                if (Time.time < _resumeT + 3f) return;

                // 随机数状态：这个 build 的 interop 没有暴露 Random.State，先跳过。

                // 队列恢复：默认关闭。实测 SetupWaitingClient 会造出一个没有贴图的坏顾客，
                // 所以默认只做“回到当天开业前”的存档恢复。
                if (Plugin.CfgQueueRebuild.Value)
                {
                    int restoredN = 0;
                    if (p.ClientIds != null && p.ClientIds.Count > 0)
                    {
                        foreach (var id in p.ClientIds)
                        {
                            try
                            {
                                PlayerStore.Instance.SetupWaitingClient(id);
                                restoredN++;
                            }
                            catch (Exception e)
                            {
                                EventLog.Write($"[Rewind] SetupWaitingClient('{id}') failed: {e.GetType().Name}: {e.Message}");
                            }
                        }
                        EventLog.Write($"[Rewind] re-queued {restoredN}/{p.ClientIds.Count} client(s).");
                    }

                    try
                    {
                        PlayerStore.Instance.HandleFutureClientQueue();
                        EventLog.Write("[Rewind] HandleFutureClientQueue() called.");
                    }
                    catch (Exception e)
                    {
                        EventLog.Write($"[Rewind] HandleFutureClientQueue failed: {e.GetType().Name}: {e.Message}");
                    }

                    try
                    {
                        var mgr = PlayerStore.Instance.storeClientManager;
                        int n = (mgr != null && mgr.clientStack != null) ? mgr.clientStack.Count : -1;
                        EventLog.Write($"[Rewind] clientStack count after rebuild = {n}");
                    }
                    catch { }
                }
                else
                {
                    EventLog.Write("[Rewind] queue rebuild disabled - restored as a plain day-start save.");
                }

                _resumeStage = 2;
                _resumeT = Time.time;
                return;
            }

            if (_resumeStage == 2)
            {
                if (Time.time < _resumeT + 3f) return;
                try
                {
                    EventLog.Write($"[Rewind] post-load state: arrived={PlayerStore.Instance.isClientArrived}, state={(int)PlayerStore.Instance.storeState}");
                    if (!p.IsDayStart && p.State == 1 && Plugin.CfgAutoOpenAfterRestore.Value)
                    {
                        PlayerStore.Instance.OpenShutter();
                        EventLog.Write("[Rewind] OpenShutter() called after restore.");
                    }
                }
                catch (Exception e)
                {
                    EventLog.Write($"[Rewind] post-load action failed: {e.GetType().Name}: {e.Message}");
                }
                // 重置已经在载入完成的瞬间做过，这里不能再做一次：会清掉期间刚捕获的顾客节点。
                _resumeStage = 3;
                _resumeT = Time.time;
                return;
            }

            if (_resumeStage == 3)
            {
                if (Time.time < _resumeT + 5f) return;
                try
                {
                    EventLog.Write($"[Rewind] +5s: arrived={PlayerStore.Instance.isClientArrived}, state={(int)PlayerStore.Instance.storeState}, cash={PlayerStore.Instance.playerCash}");
                }
                catch { }
                _resumeStage = 4;
                _resumeT = Time.time;
                return;
            }

            if (_resumeStage == 4)
            {
                if (Time.time < _resumeT + 15f) return;
                try
                {
                    EventLog.Write($"[Rewind] +20s: arrived={PlayerStore.Instance.isClientArrived}, state={(int)PlayerStore.Instance.storeState}, cash={PlayerStore.Instance.playerCash}");
                }
                catch { }
                _resumeStage = 0;
                _resumePoint = null;
            }
        }

        internal void ResetPollingAfterFileReload()
        {
            _lastState = -1;
            _lastArrived = false;
            _lastStackCount = -1;
            _bootDone = false;
            _bootTimer = 0f;
        }

        /// <summary>
        /// 回退的第二步：回到主菜单后自动 打开存档列表 → 载入目标槽位。
        /// </summary>
        private void MenuReloadStep()
        {
            if (!Rewind.PendingMenuReload)
            {
                _menuStage = 0;
                return;
            }

            if (_menuStage == 0)
            {
                var mm = MainMenuUIController.Instance;
                if (mm == null) return;
                EventLog.Write("[Rewind] menu reached, loading slot directly (skipping save UI).");
                if (DirectLoad(Rewind.PendingSlot))
                {
                    Rewind.PendingMenuReload = false;
                    _menuStage = 0;
                    return;
                }
                // 直接载入失败时退回原生读档面板流程
                try { mm.OnLoadGameClick(); }
                catch (Exception e) { EventLog.Write($"[Rewind] OnLoadGameClick failed: {e.GetType().Name}: {e.Message}"); }
                _menuStage = 1;
                _menuT = Time.time;
            }
            else if (_menuStage == 1)
            {
                if (Time.time < _menuT + 1.5f) return;

                var slots = FindSlots();
                var target = slots?.Find(s => s.slotId == Rewind.PendingSlot);
                if (target != null)
                {
                    EventLog.Write($"[Rewind] auto-loading slot {target.slotId}.");
                    try { target.OnLoadClick(); }
                    catch (Exception e) { EventLog.Write($"[Rewind] OnLoadClick failed: {e.GetType().Name}: {e.Message}"); }
                    Rewind.PendingMenuReload = false;
                    _menuStage = 0;
                    return;
                }

                // 正式版读档面板由 SaveUIManager 异步 PopulateSlots，且会混入按天历史条目；
                // 槽位元素可能还没生成或 slotId 对不上。等到超时仍找不到时，
                // 直接走 LoadSlot() 的内部路径（写 NewGameData.saveSlot → StartNewGameClick）。
                if (Time.time > _menuT + 6f)
                {
                    EventLog.Write($"[Rewind] slot element not found (pending={Rewind.PendingSlot}, elements={slots?.Count ?? 0}); using direct load path.");
                    if (DirectLoad(Rewind.PendingSlot))
                    {
                        Rewind.PendingMenuReload = false;
                        _menuStage = 0;
                    }
                    else if (Time.time > _menuT + 12f)
                    {
                        EventLog.Write("[Rewind] direct load path failed, abort.");
                        Rewind.PendingMenuReload = false;
                        Rewind.PendingPoint = null;
                        _menuStage = 0;
                    }
                }
            }
        }

        /// <summary>复刻 SaveSlotElement.LoadSlot()：设置目标槽位后由主菜单自己加载。</summary>
        private static bool DirectLoad(int slot)
        {
            try
            {
                var data = NewGameData.Instance;
                var mm = MainMenuUIController.Instance;
                if (data == null || mm == null) return false;
                data.saveSlot = slot;
                try { mm.saveUIManager?.CloseUI(); } catch { }
                mm.StartNewGameClick();
                EventLog.Write($"[Rewind] StartNewGameClick() issued for slot {slot}.");
                return true;
            }
            catch (Exception e)
            {
                EventLog.Write($"[Rewind] direct load failed: {e.GetType().Name}: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// 自动走一遍：载入存档 → 等检查点 → 回退 → 再等一会。
        /// 只在 Debug.SelfTest = true 时运行，用来做无人值守验证。
        /// </summary>
        private void RunSelfTest()
        {
            if (Plugin.CfgSelfTest == null || !Plugin.CfgSelfTest.Value) return;
            if (_testStage >= 3) return;

            if (_testStage == 0)
            {
                if (Time.time < 6f) return;
                var mm = MainMenuUIController.Instance;
                if (mm == null) return;

                EventLog.Write($"[SelfTest] menu found, previousSave={mm.IsPreviousSaveExist()} -> opening load panel.");
                mm.OnLoadGameClick();
                _testStage = 1;
                _testT = Time.time;
            }
            else if (_testStage == 1)
            {
                if (Time.time < _testT + 1.5f) return;

                var slots = FindSlots();
                if (slots == null || slots.Count == 0)
                {
                    if (Time.time > _testT + 12f)
                    {
                        EventLog.Write("[SelfTest] no save slots found, abort.");
                        _testStage = 4;
                    }
                    return;
                }

                var target = slots[0];
                var ids = new List<string>();
                foreach (var s in slots) ids.Add(s.slotId.ToString());
                EventLog.Write($"[SelfTest] slots=[{string.Join(",", ids.ToArray())}] loading slot {target.slotId}.");

                target.OnLoadClick();
                _testStage = 2;
                _testT = Time.time;
            }
            else if (_testStage == 2)
            {
                if (Time.time < _testT + 8f) return;

                if (!_testOpened)
                {
                    try
                    {
                        PlayerStore.Instance.OpenShutter();
                        EventLog.Write("[SelfTest] OpenShutter() called.");
                    }
                    catch (Exception e)
                    {
                        EventLog.Write($"[SelfTest] OpenShutter failed: {e.GetType().Name}: {e.Message}");
                    }
                    _testOpened = true;
                }

                // 等一个营业中(OPEN)的检查点出现，然后回退到它
                for (int i = Rewind.Points.Count - 1; i >= 0; i--)
                {
                    if (Rewind.Points[i].State != 1) continue;
                    var target = Rewind.Points[i];
                    EventLog.Write($"[SelfTest] OPEN checkpoint '{target.Label}' found; restoring it.");
                    _testStage = 3;
                    _testT = Time.time;
                    Rewind.Restore(target);
                    return;
                }

                if (Time.time > _testT + 60f)
                {
                    EventLog.Write("[SelfTest] no OPEN checkpoint after 60s, abort.");
                    _testStage = 4;
                }
            }
            else if (_testStage == 3)
            {
                if (Time.time < _testT + 12f) return;
                EventLog.Write("[SelfTest] finished.");
                _testStage = 4;
            }
        }

        private static List<SaveSlotElement> FindSlots()
        {
            var result = new List<SaveSlotElement>();
            try
            {
#if MONO
                var type = typeof(SaveSlotElement);
#else
                var type = Il2CppInterop.Runtime.Il2CppType.Of<SaveSlotElement>();
#endif
                var objs = Resources.FindObjectsOfTypeAll(type);
                if (objs == null) return result;
                foreach (var o in objs)
                {
                    var s = o.TryCast<SaveSlotElement>();
                    if (s != null) result.Add(s);
                }
            }
            catch (Exception e)
            {
                EventLog.Write($"[SelfTest] FindSlots failed: {e.GetType().Name}: {e.Message}");
            }
            return result;
        }
    }
}
