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
        private static float _until, _retryAt;
        private static bool _savePending;
        private static long _saveRetry;
        private static string _error, _saveError;
        private static string T(string zh, string en) => InspectionLocale.Text(zh, en);
        private static string Title => T("QuickSL更新说明", "QuickSL update notes");

        internal static void Tick()
        {
            try
            {
                SavePending();
                if (_banner != null && _banner.IsAlive && (Time.unscaledTime >= _until || !InShop()))
                    CloseBanner();
                if (Plugin.CfgUpdateSeen == null || Time.unscaledTime < _retryAt) return;
                if (Schedule.Acknowledged(Plugin.Version, Plugin.CfgUpdateSeen.Value)) return;
                if (!Schedule.Due(Plugin.Version, Plugin.CfgUpdateSeen.Value, Ready(), Time.unscaledTime)) return;
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
            builder.AddLabel(T("新增Shift显示物品详情、化学辅助改为默认关闭、放大镜优化",
                "New hold-key item details, optional chemistry automation now off by default, improved magnifier."), "summary");
            var summary = builder.Last;
            builder.AddLabel(T("点此查看详情", "Click to view details"), "view-details");
            var footer = builder.Last;
            if (footer?.text != null)
                footer.text.color = RenderHandler.GetColorFromPalette(RenderHandler.ColorPalette.LightBlue);
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
                + (group?.padding.vertical ?? 12) + 3f;
            NativeUi.FixedSize(_banner, width, chrome + contentHeight);
            if (_banner.frame?.closeButton != null) _banner.frame.closeButton.gameObject.SetActive(false);
            var click = _banner.root.AddComponent<Button>();
            click.targetGraphic = _banner.root.GetComponent<Graphic>();
            click.navigation = new Navigation { mode = Navigation.Mode.None };
            click.onClick.AddListener(OpenDetails);
            PlaceBanner();
            _until = Time.unscaledTime + 3f;
            // Display, not click, consumes this version's one-time notice.
            Schedule.Shown(Plugin.Version);
            var config = Plugin.Instance.Config;
            bool autoSave = config.SaveOnConfigSet;
            config.SaveOnConfigSet = false;
            try { Plugin.CfgUpdateSeen.Value = Plugin.Version; }
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
            var binding = Plugin.CfgKeyDetails.Value;
            string key = binding == KeyCode.LeftShift || binding == KeyCode.RightShift ? "Shift" : Plugin.KeyName(binding);
            return T(
                "Shift详情：\n\n按住" + key + "，将鼠标指向物品，会替换Tooltips以展示物品详情，松开恢复。\n\n"
                + "此前的酿酒辅助、机器产物预览均已经并入Shift详情。另外追加查看水质详情的功能（包括水龙头）\n\n"
                + "---\n\n化学辅助调整：\n\n化学辅助现在默认关闭“一键萃取”和“反应加速”功能，有需要可以在设置中打开。\n\n"
                + "追加子功能“快速烧开水”：容器在加热板中温度达到阈值时，立即移除全部的脂合剂/酒精/水/微生物\n\n"
                + "---\n\n双击放大镜优化：\n\n在鉴定页打开时，双击可鉴定物品，会关闭当前鉴定页，然后打开新的。\n\n"
                + "---\n\nMod兼容：\n\n现在Mod遇到暂时不兼容的第三方Mod后，会禁用内存节点功能，并弹出警告。\n\n"
                + "会触发警告的mod列表由玩家反馈组成，虽然不兼容，但部分通用部分是可以正常工作的。\n\n"
                + "如果你愿意接受丢物品的风险，可以在设置中重新启用内存节点。",
                "Hold-key details:\n\nHold " + key + " while pointing at an item to replace its tooltip with details. Release to restore it.\n\n"
                + "Wine annotations and machine projections now appear in hold-key details, along with liquid composition (including the shop tap).\n\n"
                + "---\n\nChemistry assistance:\n\nRapid extraction and rapid reactions default to off. Enable them in settings if needed.\n\n"
                + "New optional rapid boiling: hotplate containers lose all lipobond agent/alcohol/water/microbes once their native temperature conditions are met.\n\n"
                + "---\n\nDouble-click magnifier:\n\nDouble-click another inspectable item with the inspection page open to close that page and inspect the new item.\n\n"
                + "---\n\nMod compatibility:\n\nTemporarily incompatible third-party mods disable memory checkpoints and trigger a warning.\n\n"
                + "The warning list comes from player reports. Some common functionality may still work.\n\n"
                + "You may re-enable memory checkpoints in settings if you accept the risk of losing items.");
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

        private static void CloseBanner() { _banner?.Close(); _banner = null; }
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
