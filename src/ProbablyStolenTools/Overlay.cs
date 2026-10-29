using System;
using HarmonyLib;
#if !MONO
using Il2CppInterop.Runtime.Injection;
#endif
using UnityEngine;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 覆盖层懒创建：在插件加载时创建的对象会在首个场景加载时丢失，
    /// 所以在主菜单 / 场景内 UI 的 Awake 之后再创建。
    /// </summary>
    internal static class OverlayBootstrap
    {
        private static GameObject _go;
#if !MONO
        private static bool _registered;
#endif

        internal static void Ensure()
        {
            try
            {
#if !MONO
                if (!_registered)
                {
                    ClassInjector.RegisterTypeInIl2Cpp<RewindOverlay>();
                    ClassInjector.RegisterTypeInIl2Cpp<CheckpointPanel>();
                    _registered = true;
                }
#endif

                if (_go != null) return;

                _go = new GameObject("PSTools_Overlay");
                UnityEngine.Object.DontDestroyOnLoad(_go);
                _go.AddComponent<RewindOverlay>();
                _go.AddComponent<CheckpointPanel>();
                Plugin.LogSource.LogInfo("[Overlay] created in scene.");

                try
                {
                    bool probe = UnityEngine.Input.GetKeyDown(KeyCode.None);
                    Plugin.LogSource.LogInfo($"[Input] legacy Input available (probe={probe}).");
                }
                catch (Exception ie)
                {
                    Plugin.LogSource.LogWarning($"[Input] legacy Input NOT available: {ie.Message}");
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource.LogWarning($"[Overlay] ensure failed: {e.GetType().Name}: {e.Message}");
            }
        }

#if MONO
        internal static void Release()
        {
            CustomerCheckpoint.Shutdown();
            QuickInspectionPatch.HideAndRelease();
            if (_go != null) UnityEngine.Object.Destroy(_go);
            _go = null;
            CustomerCheckpoint.PatchesReady = false;
        }
#endif
    }

    [HarmonyPatch(typeof(MainMenuUIController), "Awake")]
    internal static class OverlayMenuPatch
    {
        [HarmonyPostfix]
        public static void Post()
        {
            OverlayBootstrap.Ensure();
        }
    }

    [HarmonyPatch(typeof(InspectionUIManager), "Awake")]
    internal static class OverlayInspectionPatch
    {
        [HarmonyPostfix]
        public static void Post()
        {
            OverlayBootstrap.Ensure();
        }
    }
}
