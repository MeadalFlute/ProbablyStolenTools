using System;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 免操作诊断：在主菜单跑一次，验证回退功能依赖的几个底层 API 是否可用。
    /// 默认关闭，只在 Debug.Enabled = true 时执行。
    /// </summary>
    internal static class Diagnostics
    {
        private static bool _done;

        internal static void RunOnce()
        {
            if (_done) return;
            _done = true;
            var log = Plugin.LogSource;

            try { log.LogInfo($"[Diag] IsInstanceExist = {PlayerStore.IsInstanceExist()}"); }
            catch (Exception e) { log.LogWarning($"[Diag] IsInstanceExist threw: {e.GetType().Name}: {e.Message}"); }

            try { log.LogInfo($"[Diag] Slot(0) = {SaveFiles.Slot(0) ?? "<null>"}"); }
            catch (Exception e) { log.LogWarning($"[Diag] Slot(0) threw: {e.GetType().Name}: {e.Message}"); }

            try { log.LogInfo($"[Diag] persistentDataPath = {Application.persistentDataPath}"); }
            catch { }

            PlayerStore store = null;
            try
            {
                store = PlayerStore.PreLoadStore();
                if (store == null)
                {
                    log.LogInfo("[Diag] PreLoadStore() = null");
                }
                else
                {
                    log.LogInfo($"[Diag] PreLoadStore() ok: cash={store.playerCash} slot={store.saveSlotId} " +
                                $"startType={store.startType} dayUntilRent={store.dayUntilRent} ver={store.saveVersion}");
                }
            }
            catch (Exception e)
            {
                log.LogWarning($"[Diag] PreLoadStore threw: {e.GetType().Name}: {e.Message}");
            }

            // ES3 泛型往返：验证 interop 是否支持泛型方法调用
            if (store != null)
            {
                string tmp = Path.Combine(Application.persistentDataPath, "PSTools_diag.es3");
                try
                {
                    ES3.Save<object>("playerStore", store, tmp);
                    log.LogInfo($"[Diag] ES3.Save<object> ok, file={(File.Exists(tmp) ? new FileInfo(tmp).Length + " bytes" : "MISSING")}");

                    var back = ES3.Load<object>("playerStore", tmp);
                    log.LogInfo($"[Diag] ES3.Load<object> ok, type={back?.GetType().Name ?? "null"}");
                }
                catch (Exception e)
                {
                    log.LogWarning($"[Diag] ES3 roundtrip threw: {e.GetType().Name}: {e.Message}");
                }
            }

            log.LogInfo("[Diag] done.");
            UiProbe.Run(log);
        }
    }

    /// <summary>探测 uGUI + TMP 能否在运行时动态创建（IMGUI 已被裁剪）。</summary>
    internal static class UiProbe
    {
        internal static void Run(ModLogger log)
        {
            try
            {
                var font = Il2CppTMPro.TMP_Settings.defaultFontAsset;
                log.LogInfo($"[UiProbe] TMP default font = {(font == null ? "null" : font.name)}");
            }
            catch (Exception e)
            {
                log.LogWarning($"[UiProbe] TMP_Settings threw: {e.GetType().Name}: {e.Message}");
            }

            try
            {
                var go = new GameObject("PSTools_ProbeCanvas");
                UnityEngine.Object.DontDestroyOnLoad(go);

                var canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 32000;

                var scaler = go.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                log.LogInfo("[UiProbe] Canvas + CanvasScaler ok");

                var child = new GameObject("PSTools_ProbeText");
                child.transform.SetParent(go.transform, false);

                var rt = child.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 1f);
                rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0f, 1f);
                rt.anchoredPosition = new Vector2(40f, -40f);
                rt.sizeDelta = new Vector2(900f, 120f);
                log.LogInfo("[UiProbe] RectTransform ok");

                var tmp = child.AddComponent<Il2CppTMPro.TextMeshProUGUI>();
                tmp.fontSize = 34f;
                tmp.color = Color.red;
                tmp.text = "PSTools UI probe: if you can read this, uGUI+TMP works";
                log.LogInfo("[UiProbe] TextMeshProUGUI ok");
            }
            catch (Exception e)
            {
                log.LogWarning($"[UiProbe] failed: {e.GetType().Name}: {e.Message}");
            }
        }
    }

    [HarmonyPatch(typeof(MainMenuUIController), "Awake")]
    internal static class DiagPatch
    {
        [HarmonyPostfix]
        public static void Post()
        {
            try
            {
                if (Plugin.CfgDiag != null && Plugin.CfgDiag.Value)
                    Diagnostics.RunOnce();
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Diag] patch failed: {e.Message}");
            }
        }
    }
}
