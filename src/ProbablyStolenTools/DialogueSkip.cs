using System;
using UnityEngine;

namespace ProbablyStolenTools
{
    internal static class DialogueSkip
    {
        private static float _nextAdvance;
        private static float _nextLog;
        private static bool _failed;

        internal static bool Ordinary(StoreClient.ClientIntent intent)
            => intent == StoreClient.ClientIntent.BUY || intent == StoreClient.ClientIntent.SELL
                || intent == StoreClient.ClientIntent.SELLNBUY || intent == StoreClient.ClientIntent.BARTER
                || intent == StoreClient.ClientIntent.APPRAISAL_SERVICE
                // 正式版新增内容：枪械定制与远征同样是“接待流程”，对话应当能被快进/建立节点
                || intent == StoreClient.ClientIntent.GUNSMITH
                || intent == StoreClient.ClientIntent.EXPEDITION;

        private static void Log(string message)
        {
            if (Time.unscaledTime < _nextLog) return;
            _nextLog = Time.unscaledTime + 2f;
            EventLog.Write("[Skip] " + message);
        }

        internal static void Tick()
        {
            if (_failed || !Plugin.CfgSkipCustomerDialogue.Value || CustomerCheckpoint.Restoring
                || Rewind.Restoring || Time.timeScale <= 0 || Time.unscaledTime < _nextAdvance) return;
            _nextAdvance = Time.unscaledTime + 0.08f;
            try
            {
                var store = Plugin.Store();
                if (store == null || (int)store.storeState != 1 || store.isHandlingShootout
                    || store.isClientBeingArrested) return;

                // 正式版原生空格走 StoreUIManager.Update：
                //   DismissAdvanceDialogHint → DialogUIManager.OnAdvancePressed → DialoguePhoneUIManager.OnAdvancePressed
                var tutorial = TutorialUIManager.Instance;
                if (tutorial != null) tutorial.DismissAdvanceDialogHint();

                var ui = DialogUIManager.Instance;
                if (ui != null && ui.currentDialog != null)
                {
                    if (!store.isClientArrived || store.currentClientInstance == null) return;
                    if (!Ordinary(store.currentClientInstance.GetClientBlueprint().clientIntent))
                    {
                        Log("skip store dialogue: non-ordinary intent " +
                            store.currentClientInstance.GetClientBlueprint().clientIntent);
                        return;
                    }
                    if (GameFields.PhoneBusy(PhoneUIManager.Instance)) { Log("skip store: phone busy"); return; }
                    var line = ui.currentDialog;
                    if (ui.isSecMonologuePlaying || line.title == DialogUIManager.SEC
                        || line.priority >= Dialogue.IMPORTANT_PRIORITY || line.HasChoices())
                    {
                        Log($"skip store: held (sec={ui.isSecMonologuePlaying} title={line.title} " +
                            $"prio={line.priority} choices={line.HasChoices()})");
                        return;
                    }
                    ui.OnAdvancePressed();
                    return;
                }

                // 第二个对话管理器：电话式/新流程对话（枪械定制等）。
                var phoneDialog = DialoguePhoneUIManager.Instance;
                if (phoneDialog == null || phoneDialog.currentDialog == null) return;
                var phoneLine = phoneDialog.currentDialog;
                if (phoneLine.HasChoices())
                {
                    Log("skip phone: held for choices");
                    return;
                }
                phoneDialog.OnAdvancePressed();
                Log($"skip phone: advanced (title={phoneLine.title} prio={phoneLine.priority})");
            }
            catch (Exception e)
            {
                _failed = true;
                Plugin.LogSource.LogWarning("[Dialogue] auto advance disabled for this session: " + e);
                PanelNotice.Show("对话快进已暂停，请手动继续", true);
            }
        }
    }
}
