using System;
using HarmonyLib;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 用游戏原生的 ModHook.OnCreateTooltip 钩子追加悬浮说明行，
    /// 取代之前对 GameItemElement / WaterFeatureHelper 的 Harmony 补丁。
    /// 名称替换仍在 GetTooltipBasic 的补丁里完成（钩子触发时名称已经写入）。
    /// </summary>
    internal static class TooltipHooks
    {
        private static bool _enabled;

        internal static void Enable()
        {
#if MONO
            if (_enabled) return;
            try
            {
                ModHook.OnCreateTooltipLate += OnTooltip;
                _enabled = true;
                Plugin.LogSource?.LogInfo("[Tooltip] subscribed to ModHook.OnCreateTooltipLate");
            }
            catch (Exception e)
            {
                Plugin.LogSource?.LogWarning("[Tooltip] hook subscribe failed: " + e.Message);
            }
#endif
        }

        internal static void Disable()
        {
#if MONO
            if (!_enabled) return;
            try { ModHook.OnCreateTooltipLate -= OnTooltip; } catch { }
            _enabled = false;
#endif
        }

#if MONO
        private static void OnTooltip(RichTextBuilder builder, GameItem item)
        {
            if (builder == null || item == null) return;
            try { WineAppraisal.AddTooltip(builder, item); }
            catch (Exception e) { Warn("wine", e); }
            try { WaterQualityPatch.AddTooltip(builder, item); }
            catch (Exception e) { Warn("water", e); }
        }
#endif

        private static void Warn(string area, Exception e)
        {
            try { Plugin.LogSource?.LogWarning("[Tooltip] " + area + " failed: " + e.Message); } catch { }
        }
    }
}
