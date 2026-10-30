using System;
using HarmonyLib;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 记录每次点击卷帘门的现场状态。
    /// 玩家报告“无法闭店”时，这一行能直接说明是被哪个条件挡住的：
    /// storeState 不是 1、锁计数没归零、还是 CanEndDay() 返回 false。
    /// </summary>
    [HarmonyPatch(typeof(StoreShutterButton), "OnClick")]
    internal static class ShutterClickPatch
    {
        [HarmonyPrefix]
        private static void Prefix(StoreShutterButton __instance)
        {
            try
            {
                var store = Plugin.Store();
                if (store == null) return;
                bool isClosed = false;
                try { isClosed = GameFields.Get<bool>(__instance, "isClosed"); } catch { }
                bool canEnd = false;
                try { canEnd = store.CanEndDay(); } catch { }
                EventLog.Write($"[Shutter] click state={(int)store.storeState} lock={store.lockShutterButtonFlag} " +
                               $"isClosed={(isClosed ? 1 : 0)} canEndDay={(canEnd ? 1 : 0)} " +
                               $"arrest={(store.isClientBeingArrested ? 1 : 0)} shoot={(store.isHandlingShootout ? 1 : 0)} " +
                               $"client={(store.currentClientInstance != null ? 1 : 0)} arrived={(store.isClientArrived ? 1 : 0)}");
            }
            catch (Exception e) { EventLog.Write("[Shutter] click log failed: " + e.Message); }
        }
    }
}
