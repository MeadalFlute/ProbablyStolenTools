using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 只读观测层：在**真实业务入口**记录事件，不写用户存档、不改任何状态。
    /// 目的：确认钩子命中、真实调用顺序、以及每个操作会动到哪些对象。
    /// 这是“事务历史 + 快照”方案的第 1 步（见 research/2026-10-21-daily-undo.md 第 8 节）。
    /// </summary>
    internal static class HistoryObserver
    {
        internal static string LogPath => Path.Combine(Application.persistentDataPath, "PSTools_history.log");

        private static int _seq;

        internal static void Log(string kind, string detail)
        {
            if (Plugin.CfgHistory == null || !Plugin.CfgHistory.Value) return;

            // 限流：防止某个高频方法把主线程拖死（曾被子 OnMainInvAreaChange 刷过 31 万次）
            if (!AllowNow()) return;

            try
            {
                _seq++;
                string line = $"{DateTime.Now:HH:mm:ss.fff} #{_seq} [{kind}] {detail}";
                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { }

            // 事务分组：:pre 开/续事务，:post 记录事件，无后缀的（如 PlacedForSelling）只当噪声
            try
            {
                if (kind.EndsWith(":pre")) Transaction.Pre(kind.Substring(0, kind.Length - 4), detail);
                else if (kind.EndsWith(":post")) Transaction.Post(kind.Substring(0, kind.Length - 5), detail);
            }
            catch { }
        }

        // ---- 限流 ----
        private static int _windowSecond = -1;
        private static int _windowCount;
        private static int _dropped;
        private const int MaxPerSecond = 120;
        private const long MaxFileBytes = 8L * 1024 * 1024;

        private static bool AllowNow()
        {
            int sec = DateTime.Now.Second;
            if (sec != _windowSecond) { _windowSecond = sec; _windowCount = 0; }

            if (++_windowCount > MaxPerSecond)
            {
                if (_dropped++ % 1000 == 0)
                {
                    try { File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} [LOG:RATE-LIMIT] suppressed {_dropped} records{Environment.NewLine}"); }
                    catch { }
                }
                return false;
            }

            try
            {
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > MaxFileBytes) return false;
            }
            catch { }

            return true;
        }

        /// <summary>当前业务上下文摘要：现金 / 天数 / 店铺状态 / 顾客 / 队列。</summary>
        internal static string Ctx()
        {
            try
            {
                var s = Plugin.Store();
                if (s == null) return "store=null";

                int cash = -1, state = -1, queue = -1;
                string client = "-";
                try { cash = s.playerCash; } catch { }
                try { state = (int)s.storeState; } catch { }
                try
                {
                    var mgr = s.storeClientManager;
                    if (mgr != null && mgr.clientStack != null) queue = mgr.clientStack.Count;
                }
                catch { }
                try
                {
                    var inst = s.currentClientInstance;
                    if (inst != null)
                    {
                        var bp = inst.GetClientBlueprint();
                        if (bp != null) client = bp.identifier;
                    }
                }
                catch { }

                return $"cash={cash} state={state} queue={queue} client={client}";
            }
            catch (Exception e)
            {
                return "ctx-error:" + e.GetType().Name;
            }
        }

        internal static string Item(GameItem item)
        {
            if (item == null) return "item=null";
            try { return $"item={item.uniqueId}:{(string.IsNullOrEmpty(item.name) ? "?" : item.name)}"; }
            catch { return "item=<err>"; }
        }
    }

    /// <summary>
    /// 事务分组：把同一批操作（例如一次“全卖”触发的 3 次 SellItem）合并成**一步**。
    /// 只做分组与记账，不抓快照、不改状态——用于先验证“一步”的边界是否可靠。
    /// </summary>
    internal static class Transaction
    {
        private const double SettleSeconds = 0.35;

        private static bool _open;
        private static string _kind;
        private static int _id;
        private static DateTime _last;
        private static int _events;
        private static int _cashStart;
        private static int _queueStart;
        private static string _clientStart;
        private static readonly List<string> _items = new List<string>();

        /// <summary>把包装方法映射到真实业务动作，避免“一次卖出”被拆成两个 STEP。</summary>
        private static string Norm(string kind)
        {
            if (kind == "SellFromTable") return "SellItem";
            if (kind == "BuyFromTable") return "BuyItem";
            return kind;
        }

        internal static void Pre(string kind, string detail)
        {
            kind = Norm(kind);
            var now = DateTime.Now;
            bool extend = _open && _kind == kind && (now - _last).TotalSeconds <= SettleSeconds;

            if (!extend)
            {
                Commit();
                Open(kind, detail);
            }

            _events++;
            _last = now;
        }

        internal static void Post(string kind, string detail)
        {
            kind = Norm(kind);
            if (_open && _kind == kind)
            {
                CollectItem(detail);
                _last = DateTime.Now;
            }
        }

        internal static bool IsOpen => _open;

        internal static void Tick()
        {
            if (!_open) return;
            if ((DateTime.Now - _last).TotalSeconds < SettleSeconds) return;
            Commit();
        }

        private static void Open(string kind, string detail)
        {
            _open = true;
            _kind = kind;
            _id++;
            _last = DateTime.Now;
            _events = 0;
            _items.Clear();
            CollectItem(detail);

            try { _cashStart = Plugin.Store().playerCash; } catch { _cashStart = -1; }
            try
            {
                var mgr = Plugin.Store().storeClientManager;
                _queueStart = (mgr != null && mgr.clientStack != null) ? mgr.clientStack.Count : -1;
            }
            catch { _queueStart = -1; }
            try
            {
                var inst = Plugin.Store().currentClientInstance;
                _clientStart = inst != null ? inst.GetClientBlueprint().identifier : "-";
            }
            catch { _clientStart = "-"; }
        }

        private static void Commit()
        {
            if (!_open) return;
            _open = false;

            int cashEnd = -1, queueEnd = -1;
            string clientEnd = "-";
            try { cashEnd = Plugin.Store().playerCash; } catch { }
            try
            {
                var mgr = Plugin.Store().storeClientManager;
                queueEnd = (mgr != null && mgr.clientStack != null) ? mgr.clientStack.Count : -1;
            }
            catch { }
            try
            {
                var inst = Plugin.Store().currentClientInstance;
                clientEnd = inst != null ? inst.GetClientBlueprint().identifier : "-";
            }
            catch { }

            string summary =
                $"===== STEP #{_id} {_kind}  events={_events}  cash {_cashStart}->{cashEnd}  " +
                $"queue {_queueStart}->{queueEnd}  client {_clientStart}->{clientEnd}  items=[{string.Join(",", _items.ToArray())}]";

            try
            {
                File.AppendAllText(HistoryObserver.LogPath, summary + Environment.NewLine);
                Plugin.LogSource.LogInfo($"[Hist] {summary}");
            }
            catch { }
        }

        private static void CollectItem(string detail)
        {
            try
            {
                // detail 形如 "item=1471:金属锭 cash=..."，只取 id
                int i = detail.IndexOf("item=", StringComparison.Ordinal);
                if (i < 0) return;
                int start = i + 5;
                int end = detail.IndexOf(':', start);
                if (end < 0) end = detail.IndexOf(' ', start);
                if (end < 0) end = detail.Length;
                string id = detail.Substring(start, end - start);
                if (!string.IsNullOrEmpty(id) && !_items.Contains(id)) _items.Add(id);
            }
            catch { }
        }
    }

    // ---------------- 交易 / 物品 ----------------

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.BuyItem))]
    internal static class ObsBuyItem
    {
        [HarmonyPrefix]
        public static void Pre(GameItem item)
            => HistoryObserver.Log("BuyItem:pre", $"{HistoryObserver.Item(item)} {HistoryObserver.Ctx()}");

        [HarmonyPostfix]
        public static void Post(GameItem item)
            => HistoryObserver.Log("BuyItem:post", $"{HistoryObserver.Item(item)} {HistoryObserver.Ctx()}");
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.SellItem))]
    internal static class ObsSellItem
    {
        [HarmonyPrefix]
        public static void Pre(GameItem item)
            => HistoryObserver.Log("SellItem:pre", $"{HistoryObserver.Item(item)} {HistoryObserver.Ctx()}");

        [HarmonyPostfix]
        public static void Post(GameItem item)
            => HistoryObserver.Log("SellItem:post", $"{HistoryObserver.Item(item)} {HistoryObserver.Ctx()}");
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.BuyItemFromTable))]
    internal static class ObsBuyFromTable
    {
        [HarmonyPrefix] public static void Pre() => HistoryObserver.Log("BuyFromTable:pre", HistoryObserver.Ctx());
        [HarmonyPostfix] public static void Post() => HistoryObserver.Log("BuyFromTable:post", HistoryObserver.Ctx());
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.SellItemFromTable))]
    internal static class ObsSellFromTable
    {
        [HarmonyPrefix] public static void Pre() => HistoryObserver.Log("SellFromTable:pre", HistoryObserver.Ctx());
        [HarmonyPostfix] public static void Post() => HistoryObserver.Log("SellFromTable:post", HistoryObserver.Ctx());
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.AcceptBarter))]
    internal static class ObsBarter
    {
        [HarmonyPrefix] public static void Pre() => HistoryObserver.Log("Barter:pre", HistoryObserver.Ctx());
        [HarmonyPostfix] public static void Post() => HistoryObserver.Log("Barter:post", HistoryObserver.Ctx());
    }

    // ---------------- 物品移动 ----------------

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.PlacedItemForSelling))]
    internal static class ObsPlacedSelling
    {
        [HarmonyPrefix]
        public static void Pre(GameItem targetItem)
            => HistoryObserver.Log("PlacedForSelling", $"{HistoryObserver.Item(targetItem)} {HistoryObserver.Ctx()}");
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.PlacedItemForBuying))]
    internal static class ObsPlacedBuying
    {
        [HarmonyPrefix]
        public static void Pre(GameItem targetItem)
            => HistoryObserver.Log("PlacedForBuying", $"{HistoryObserver.Item(targetItem)} {HistoryObserver.Ctx()}");
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.PlacedItemForBarter))]
    internal static class ObsPlacedBarter
    {
        [HarmonyPrefix]
        public static void Pre(GameItem targetItem)
            => HistoryObserver.Log("PlacedForBarter", $"{HistoryObserver.Item(targetItem)} {HistoryObserver.Ctx()}");
    }

    // ---------------- 顾客流程 ----------------

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.GetNextClient))]
    internal static class ObsNextClient
    {
        [HarmonyPrefix] public static void Pre() => HistoryObserver.Log("GetNextClient:pre", HistoryObserver.Ctx());
        [HarmonyPostfix] public static void Post() => HistoryObserver.Log("GetNextClient:post", HistoryObserver.Ctx());
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.DismissCurrentClient))]
    internal static class ObsDismiss
    {
        [HarmonyPrefix] public static void Pre() => HistoryObserver.Log("Dismiss:pre", HistoryObserver.Ctx());
        [HarmonyPostfix] public static void Post() => HistoryObserver.Log("Dismiss:post", HistoryObserver.Ctx());
    }

    // ---------------- 鉴定 ----------------

    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.OnAppraisalServiceEstimated))]
    internal static class ObsAppraisal
    {
        [HarmonyPrefix] public static void Pre() => HistoryObserver.Log("AppraisalEstimate:pre", HistoryObserver.Ctx());
        [HarmonyPostfix] public static void Post() => HistoryObserver.Log("AppraisalEstimate:post", HistoryObserver.Ctx());
    }

    // ---------------- 议价（真实的业务动作，不是 UI 打开/关闭） ----------------

    [HarmonyPatch(typeof(BargainUIManager), nameof(BargainUIManager.OfferDiscount))]
    internal static class ObsBargainDiscount
    {
        [HarmonyPrefix] public static void Pre(int percent)
            => HistoryObserver.Log("Bargain:折扣:pre", $"percent={percent} {HistoryObserver.Ctx()}");
        [HarmonyPostfix] public static void Post(int percent)
            => HistoryObserver.Log("Bargain:折扣:post", $"percent={percent} {HistoryObserver.Ctx()}");
    }

    [HarmonyPatch(typeof(BargainUIManager), nameof(BargainUIManager.OfferMarkup))]
    internal static class ObsBargainMarkup
    {
        [HarmonyPrefix] public static void Pre(int percent)
            => HistoryObserver.Log("Bargain:加价:pre", $"percent={percent} {HistoryObserver.Ctx()}");
        [HarmonyPostfix] public static void Post(int percent)
            => HistoryObserver.Log("Bargain:加价:post", $"percent={percent} {HistoryObserver.Ctx()}");
    }

    [HarmonyPatch(typeof(BargainUIManager), nameof(BargainUIManager.Blackmail))]
    internal static class ObsBargainBlackmail
    {
        [HarmonyPrefix] public static void Pre() => HistoryObserver.Log("Bargain:勒索:pre", HistoryObserver.Ctx());
        [HarmonyPostfix] public static void Post() => HistoryObserver.Log("Bargain:勒索:post", HistoryObserver.Ctx());
    }

    [HarmonyPatch(typeof(BargainUIManager), nameof(BargainUIManager.OfferBuyingMarkup))]
    internal static class ObsBargainBuyMarkup
    {
        [HarmonyPrefix] public static void Pre(int percent)
            => HistoryObserver.Log("Bargain:收购加价:pre", $"percent={percent} {HistoryObserver.Ctx()}");
        [HarmonyPostfix] public static void Post(int percent)
            => HistoryObserver.Log("Bargain:收购加价:post", $"percent={percent} {HistoryObserver.Ctx()}");
    }

    [HarmonyPatch(typeof(BargainUIManager), nameof(BargainUIManager.DemandBuyingDiscount))]
    internal static class ObsBargainBuyDiscount
    {
        [HarmonyPrefix] public static void Pre(int percent)
            => HistoryObserver.Log("Bargain:收购压价:pre", $"percent={percent} {HistoryObserver.Ctx()}");
        [HarmonyPostfix] public static void Post(int percent)
            => HistoryObserver.Log("Bargain:收购压价:post", $"percent={percent} {HistoryObserver.Ctx()}");
    }

    [HarmonyPatch(typeof(BargainUIManager), nameof(BargainUIManager.ThreatenAug))]
    internal static class ObsBargainThreaten
    {
        [HarmonyPrefix] public static void Pre() => HistoryObserver.Log("Bargain:威胁:pre", HistoryObserver.Ctx());
        [HarmonyPostfix] public static void Post() => HistoryObserver.Log("Bargain:威胁:post", HistoryObserver.Ctx());
    }

    // ---------------- 使用道具（放大镜/香烟/注射器等工具） ----------------

    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.HandleCigarette))]
    internal static class ObsToolCigarette
    {
        [HarmonyPrefix] public static void Pre(GameItem gameItem)
            => HistoryObserver.Log("Tool:香烟:pre", $"{HistoryObserver.Item(gameItem)} {HistoryObserver.Ctx()}");
        [HarmonyPostfix] public static void Post(GameItem gameItem)
            => HistoryObserver.Log("Tool:香烟:post", $"{HistoryObserver.Item(gameItem)} {HistoryObserver.Ctx()}");
    }

    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.HandlePlasma))]
    internal static class ObsToolPlasma
    {
        [HarmonyPrefix] public static void Pre(GameItem gameItem)
            => HistoryObserver.Log("Tool:等离子:pre", $"{HistoryObserver.Item(gameItem)} {HistoryObserver.Ctx()}");
        [HarmonyPostfix] public static void Post(GameItem gameItem)
            => HistoryObserver.Log("Tool:等离子:post", $"{HistoryObserver.Item(gameItem)} {HistoryObserver.Ctx()}");
    }

    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.HandleStamp))]
    internal static class ObsToolStamp
    {
        [HarmonyPrefix] public static void Pre(GameItem gameItem)
            => HistoryObserver.Log("Tool:印章:pre", $"{HistoryObserver.Item(gameItem)} {HistoryObserver.Ctx()}");
        [HarmonyPostfix] public static void Post(GameItem gameItem)
            => HistoryObserver.Log("Tool:印章:post", $"{HistoryObserver.Item(gameItem)} {HistoryObserver.Ctx()}");
    }

    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.HandleInjector))]
    internal static class ObsToolInjector
    {
        [HarmonyPrefix] public static void Pre(GameItem gameItem)
            => HistoryObserver.Log("Tool:注射器:pre", $"{HistoryObserver.Item(gameItem)} {HistoryObserver.Ctx()}");
        [HarmonyPostfix] public static void Post(GameItem gameItem)
            => HistoryObserver.Log("Tool:注射器:post", $"{HistoryObserver.Item(gameItem)} {HistoryObserver.Ctx()}");
    }

    // ---------------- 电话 ----------------

    [HarmonyPatch(typeof(StorePhoneClient), nameof(StorePhoneClient.UseService))]
    internal static class ObsPhone
    {
        [HarmonyPrefix] public static void Pre() => HistoryObserver.Log("Phone:pre", HistoryObserver.Ctx());
        [HarmonyPostfix] public static void Post() => HistoryObserver.Log("Phone:post", HistoryObserver.Ctx());
    }

    [HarmonyPatch(typeof(StorePhoneObject), nameof(StorePhoneObject.OnClick))]
    internal static class ObsPhoneClick
    {
        [HarmonyPrefix] public static void Pre() => HistoryObserver.Log("Phone:点选:pre", HistoryObserver.Ctx());
        [HarmonyPostfix] public static void Post() => HistoryObserver.Log("Phone:点选:post", HistoryObserver.Ctx());
    }

    [HarmonyPatch(typeof(PhoneUIManager), nameof(PhoneUIManager.StartPhoneDialog))]
    internal static class ObsPhoneDialog
    {
        [HarmonyPrefix] public static void Pre(long currentNumber)
            => HistoryObserver.Log("Phone:通话:pre", $"number={currentNumber} {HistoryObserver.Ctx()}");
        [HarmonyPostfix] public static void Post(long currentNumber)
            => HistoryObserver.Log("Phone:通话:post", $"number={currentNumber} {HistoryObserver.Ctx()}");
    }

    // ---------------- 现金：所有花费的统一入口 ----------------

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.PayMoney))]
    internal static class ObsPayMoney
    {
        [HarmonyPrefix]
        public static void Pre(int amount)
            => HistoryObserver.Log("PayMoney:pre", $"amount={amount} {HistoryObserver.Ctx()}");

        [HarmonyPostfix]
        public static void Post(int amount)
            => HistoryObserver.Log("PayMoney:post", $"amount={amount} {HistoryObserver.Ctx()}");
    }

    [HarmonyPatch(typeof(PlayerStore), nameof(PlayerStore.ModCash))]
    internal static class ObsModCash
    {
        [HarmonyPrefix]
        public static void Pre(int value)
            => HistoryObserver.Log("ModCash:pre", $"value={value} {HistoryObserver.Ctx()}");

        [HarmonyPostfix]
        public static void Post(int value)
            => HistoryObserver.Log("ModCash:post", $"value={value} {HistoryObserver.Ctx()}");
    }

    // ---------------- 物品进出容器（从设备取出/放入） ----------------

    [HarmonyPatch(typeof(GameInventory), nameof(GameInventory.UncheckedAcceptAll))]
    internal static class ObsInvAcceptAll
    {
        [HarmonyPrefix] public static void Pre() => HistoryObserver.Log("Inv:acceptAll:pre", HistoryObserver.Ctx());
        [HarmonyPostfix] public static void Post() => HistoryObserver.Log("Inv:acceptAll:post", HistoryObserver.Ctx());
    }

    [HarmonyPatch(typeof(GameInventory), nameof(GameInventory.ExpelAll))]
    internal static class ObsInvExpelAll
    {
        [HarmonyPrefix] public static void Pre() => HistoryObserver.Log("Inv:expelAll:pre", HistoryObserver.Ctx());
        [HarmonyPostfix] public static void Post() => HistoryObserver.Log("Inv:expelAll:post", HistoryObserver.Ctx());
    }

    // ---------------- 现金看门狗 ----------------
    // 任何“钱变了但没有对应事务”的情况都记一笔，用来暴露还没被钩住的业务路径。

    internal static class CashWatchdog
    {
        private static int _last = int.MinValue;

        internal static void Tick()
        {
            if (Plugin.CfgHistory == null || !Plugin.CfgHistory.Value) return;
            try
            {
                var s = Plugin.Store();
                if (s == null) { _last = int.MinValue; return; }

                int cash = s.playerCash;
                if (_last == int.MinValue) { _last = cash; return; }
                if (cash == _last) return;

                if (!Transaction.IsOpen)
                    HistoryObserver.Log("CASH:unattributed", $"{_last}->{cash} {HistoryObserver.Ctx()}");

                _last = cash;
            }
            catch { }
        }
    }
}
