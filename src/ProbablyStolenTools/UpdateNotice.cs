#if MONO
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolenTools
{
    internal static class UpdateNotice
    {
        private static readonly UpdateNoticeSchedule Schedule = new();
        private static CustomUIWindow _banner, _details;
        private static RectTransform _countdown;
        private const float Duration = 5f;
        private const float CountdownHeight = 6f;
        private static float _until, _retryAt;
        private static bool _savePending;
        private static long _saveRetry;
        private static string _error, _saveError;
        private static string T(string zh, string en) => InspectionLocale.Text(zh, en);
        private static string Title => T("QuickSL更新简报", "QuickSL update brief");
        private static string NoticeId => Plugin.Version + ":20261125-qol-r4";

        internal static void Tick()
        {
            try
            {
                SavePending();
                if (_countdown != null)
                    _countdown.anchorMax = new Vector2(UpdateNoticeContent.Progress(Time.unscaledTime, _until, Duration), 1);
                if (_banner != null && _banner.IsAlive && (Time.unscaledTime >= _until || !InShop()))
                    CloseBanner();
                if (Plugin.CfgUpdateSeen == null || Time.unscaledTime < _retryAt) return;
                if (Schedule.Acknowledged(NoticeId, Plugin.CfgUpdateSeen.Value)) return;
                if (!Schedule.Due(NoticeId, Plugin.CfgUpdateSeen.Value, Ready(), Time.unscaledTime)) return;
                _retryAt = Time.unscaledTime + 1f;
                ShowBanner();
            }
            catch (Exception e)
            {
                string error = e.GetType().Name + ": " + e.Message;
                if (_error != error) EventLog.Write("[Updates] notice unavailable: " + error);
                _error = error;
                _retryAt = Time.unscaledTime + 5f;
            }
        }

        private static bool InShop() => Plugin.Store() != null && MainMenuUIController.Instance == null
            && StoreUIManager.Instance != null;

        private static bool Ready() => InShop() && CustomUIManager.Instance != null && InspectionLocale.AnnotationsReady
            && !SaveManager.IsDecoding && !NativeLoadDiagnostics.Active && !CurrentConverterHelper.isNightCycle
            && !Rewind.Restoring && !Rewind.Settling && !CustomerCheckpoint.Restoring
            && StartOfDayUIManager.Instance?.UIPanel?.activeSelf != true
            && AdvCalendarUIManager.Instance?.UIPanel?.activeSelf != true
            && EndOfDayUIManager.Instance?.IsOpen != true
            && StoreUIManager.Instance.blackscreen?.gameObject.activeSelf != true;

        private static void ShowBanner()
        {
            var builder = CustomUIManager.Window("ProbablyStolenTools.update-banner", Title, CustomUIManager.LAYER_OVERLAY);
            if (builder == null) return;
            builder.SetSize(380f, 100f).SetDraggable(false).SetCloseOnEscape(false);
            builder.AddLabel(T("磁吸窗口、窗口置顶、酿酒回调、机器模组槽解锁",
                "Window snapping and pinning, wine labels restored, working-machine module access."), "summary");
            var summary = builder.Last;
            builder.AddLabel(T("点此查看详情", "Click to view details"), "view-details");
            var footer = builder.Last;
            if (footer?.text != null)
                footer.text.color = RenderHandler.GetColorFromPalette(RenderHandler.ColorPalette.LightBlue);
            // A standalone layout row avoids separator-prefab masks and nested sizing.
            var track = new GameObject("countdown-track", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
            track.transform.SetParent(builder.CurrentContainer, false);
            var trackLayout = track.GetComponent<LayoutElement>();
            trackLayout.minHeight = trackLayout.preferredHeight = CountdownHeight;
            trackLayout.flexibleHeight = 0;
            trackLayout.flexibleWidth = 1;
            var trackImage = track.GetComponent<Image>();
            trackImage.color = new Color32(128, 128, 128, 255);
            trackImage.raycastTarget = false;
            var fill = new GameObject("countdown-fill", typeof(RectTransform), typeof(Image));
            _countdown = (RectTransform)fill.transform;
            _countdown.SetParent(track.transform, false);
            _countdown.anchorMin = Vector2.zero;
            _countdown.anchorMax = new Vector2(0, 1);
            _countdown.offsetMin = _countdown.offsetMax = Vector2.zero;
            var fillImage = fill.GetComponent<Image>();
            fillImage.color = RenderHandler.GetColorFromPalette(RenderHandler.ColorPalette.LightBlue);
            fillImage.raycastTarget = false;
            _banner = builder.Show();
            if (_banner == null || !_banner.IsAlive) return;
            float width = _banner.Rect.parent is RectTransform parent
                ? Math.Min(380f, Math.Max(200f, parent.rect.width - 24f)) : 380f;
            var group = _banner.ContentRoot.GetComponent<VerticalLayoutGroup>();
            if (group != null)
            {
                group.childForceExpandHeight = false;
                group.spacing = 3f;
                group.padding.top = group.padding.bottom = 6;
            }
            NativeUi.FixedSize(_banner, width, 100f);
            float chrome = Math.Max(28f, _banner.Rect.rect.height - _banner.ContentRoot.rect.height);
            float contentWidth = Math.Max(100f, _banner.ContentRoot.rect.width - (group?.padding.horizontal ?? 16));
            float contentHeight = FitLabel(summary, contentWidth, 14f) + FitLabel(footer, contentWidth, 12f)
                + (group?.padding.vertical ?? 12) + CountdownHeight + 2f * (group?.spacing ?? 3f);
            NativeUi.FixedSize(_banner, width, chrome + contentHeight);
            CustomUIBuilder.RebuildLayout(_banner.ContentRoot);
            if (_banner.frame?.closeButton != null) _banner.frame.closeButton.gameObject.SetActive(false);
            var click = _banner.root.AddComponent<Button>();
            click.targetGraphic = _banner.root.GetComponent<Graphic>();
            click.navigation = new Navigation { mode = Navigation.Mode.None };
            click.onClick.AddListener(OpenDetails);
            PlaceBanner();
            _until = Time.unscaledTime + Duration;
            // Display, not click, consumes this version's one-time notice.
            Schedule.Shown(NoticeId);
            var config = Plugin.Instance.Config;
            bool autoSave = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try { Plugin.CfgUpdateSeen.Value = NoticeId; }
            finally { config.SaveOnConfigSet = autoSave; }
            _savePending = true;
            _saveRetry = 0;
            SavePending();
        }

        private static float FitLabel(CustomUIElement element, float width, float fontSize)
        {
            if (element?.text == null) return 0;
            var text = element.text;
            text.enableAutoSizing = false;
            text.enableWordWrapping = true;
            text.fontSize = fontSize;
            text.paragraphSpacing = 0;
            text.lineSpacing = 0;
            float height = (float)Math.Ceiling(text.GetPreferredValues(text.text, width, 0).y) + 2f;
            var layout = element.root.GetComponent<LayoutElement>() ?? element.root.AddComponent<LayoutElement>();
            layout.minHeight = layout.preferredHeight = height;
            layout.flexibleHeight = 0;
            return height;
        }

        private static void PlaceBanner()
        {
            var parent = _banner.Rect.parent as RectTransform;
            if (parent == null) return;
            float width = _banner.Rect.rect.width, height = _banner.Rect.rect.height;
            float left = (parent.rect.width - width) / 2f, top = 16f;
            // Best-effort stacking with compact top notifications from other mods.
            for (int pass = 0; pass < 3; pass++)
                for (int i = 0; i < parent.childCount; i++)
                {
                    var other = parent.GetChild(i) as RectTransform;
                    if (other == null || other == _banner.Rect || !other.gameObject.activeInHierarchy) continue;
                    var bounds = NativeUi.Bounds(other, parent);
                    if (bounds.Y < 0 || bounds.Y > parent.rect.height * 0.3f || bounds.Height > 180f) continue;
                    if (left < bounds.X + bounds.Width && left + width > bounds.X
                        && top < bounds.Y + bounds.Height && top + height > bounds.Y)
                        top = bounds.Y + bounds.Height + 8f;
                }
            top = Math.Min(top, Math.Max(8f, parent.rect.height - height - 8f));
            NativeUi.Place(_banner, parent, new PanelBounds(left, top, width, height));
        }

        private static void SavePending()
        {
            if (!_savePending || DateTime.UtcNow.Ticks < _saveRetry) return;
            _saveRetry = DateTime.UtcNow.Ticks + 5 * TimeSpan.TicksPerSecond;
            try { Plugin.Instance.Config.Save(); _savePending = false; _saveError = null; }
            catch (Exception e)
            {
                string error = e.GetType().Name + ": " + e.Message;
                if (_saveError != error) EventLog.Write("[Updates] acknowledgement save failed; retry in 5s: " + error);
                _saveError = error;
            }
        }

        private static string DetailsText()
        {
            return T(
                "磁吸窗口：\n按住Shift拖动窗口时，会尝试将窗口对齐。\n---\n"
                + "窗口置顶：\n按住Shift并用鼠标中键点击窗口标题将其置顶，再次点击取消置顶。\n---\n"
                + "酿酒回调：\n荧光莓标注和酿酒品质预测不再要求按住Shift。\n---\n"
                + "机器模组槽解锁：\n解锁运行中的机器（比如水培系统）的模组槽，允许你拆卸模组。\n"
                + "默认关闭，需要在设置中开启。\n---\n"
                + "化学辅助：\n水质详情现在支持小容器，比如量筒和滴管。\n"
                + "按住Shift并用鼠标中键点击物品，可以固定或取消固定详情，包括水质、化学成分和机器产物预测。\n"
                + "按住鼠标左键或拖拽期间不会触发固定，原版中键翻转不受影响。\n"
                + "机器产物预测中的警报信息现在会在常规tooltips中显示。\n---\n"
                + "更新公告：\n更新公告现在会显示进度条。\n---\n"
                + "Vanilla Brewing Expansion适配：\n安装有原版酿酒拓展 | Vanilla Brewing Expansion模组时，QuickSL会阻止营养果溶解进桶装水的操作。\n"
                + "但在此提一嘴，这是原版机制，不是我们的BUG…",
                "Window snapping:\nHold Shift while dragging a window to align it with nearby windows.\n---\n"
                + "Window pinning:\nShift-middle-click a window title to pin it; repeat to unpin.\n---\n"
                + "Wine labels restored:\nGlowberry labels and wine quality predictions no longer require Shift.\n---\n"
                + "Machine module slots:\nUnlock module slots on running machines (such as hydroponic systems) so you can remove modules.\n"
                + "Disabled by default; enable it in settings.\n---\n"
                + "Chemistry assistance:\nLiquid details now support small vessels such as measuring cylinders and droppers.\n"
                + "Shift-middle-click an item to pin or unpin its details, including water quality, composition and machine projections.\n"
                + "Pinning is disabled while holding the left button or dragging; native middle-button flipping is preserved.\n"
                + "Machine projection warnings now appear in ordinary tooltips.\n---\n"
                + "Update notices:\nUpdate notices now include a countdown progress bar.\n---\n"
                + "Vanilla Brewing Expansion compatibility:\nWhen Vanilla Brewing Expansion is installed, QuickSL prevents nutrifruit from dissolving into water jugs.\n"
                + "A quick note: this is a vanilla game mechanic, not a QuickSL bug...");
        }

        private static void OpenDetails()
        {
            CloseBanner();
            if (_details != null && _details.IsAlive) { _details.Show(); return; }
            var builder = CustomUIManager.Window("ProbablyStolenTools.update-details", Title, CustomUIManager.LAYER_OVERLAY);
            if (builder == null) return;
            builder.SetSize(580f, 560f).SetDraggable(true).SetCloseOnEscape(true);
            builder.BeginScrollFlexible();
            var content = builder.CurrentContainer.GetComponent<VerticalLayoutGroup>();
            if (content != null) { content.childForceExpandHeight = false; content.spacing = 4f; }
            var sections = UpdateNoticeContent.Sections(DetailsText());
            for (int i = 0; i < sections.Length; i++)
            {
                if (i > 0) builder.AddSeparator("divider-" + i, 2);
                builder.AddLabel(sections[i], "details-" + i);
                if (builder.Last?.text != null)
                {
                    builder.Last.text.enableWordWrapping = true;
                    builder.Last.text.enableAutoSizing = false;
                    builder.Last.text.fontSize = 15f;
                    builder.Last.text.paragraphSpacing = 0;
                    builder.Last.text.lineSpacing = 0;
                }
            }
            builder.End();
            _details = builder.Show();
            if (_details?.Rect?.parent is RectTransform parent)
            {
                float width = Math.Min(580f, Math.Max(240f, parent.rect.width - 24f));
                float height = Math.Min(560f, Math.Max(180f, parent.rect.height - 40f));
                NativeUi.FixedSize(_details, width, height);
                NativeUi.Place(_details, parent, new PanelBounds((parent.rect.width - width) / 2,
                    (parent.rect.height - height) / 2, width, height));
            }
        }

        private static void CloseBanner() { _banner?.Close(); _banner = null; _countdown = null; }
        internal static void Disable()
        {
            SavePending();
            CloseBanner();
            _details?.Close();
            _details = null;
        }
    }
}
#endif
