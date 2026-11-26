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
        private const float Duration = UpdateNoticeContent.Duration;
        private const float CountdownHeight = 6f;
        private static float _until, _retryAt;
        private static bool _savePending;
        private static long _saveRetry;
        private static string _error, _saveError;
        private static string _loggedNotice;
        private static string T(string zh, string en) => InspectionLocale.Text(zh, en);
        private static string Title => T("QuickSL更新简报", "QuickSL update brief");
        private static string NoticeId => Plugin.Version + UpdateNoticeContent.Revision;

        internal static void Tick()
        {
            try
            {
                SavePending();
                if (_banner != null)
                {
                    bool visible = _banner.IsAlive && _banner.root != null && _banner.root.activeInHierarchy && Ready();
                    if (!visible)
                    {
                        CloseBanner();
                        Schedule.Due(NoticeId, Plugin.CfgUpdateSeen?.Value, false, Time.unscaledTime);
                        _retryAt = Time.unscaledTime + .5f;
                        EventLog.Write("[Updates] banner interrupted notice=" + NoticeId + "; retry if unacknowledged");
                    }
                    else
                    {
                        if (_countdown != null)
                            _countdown.anchorMax = new Vector2(UpdateNoticeContent.Progress(Time.unscaledTime, _until, Duration), 1);
                        if (Schedule.ConfirmVisible(true, Time.unscaledTime)) Acknowledge("active-for-2s");
                        if (Time.unscaledTime >= _until) CloseBanner();
                    }
                    return;
                }
                if (Plugin.CfgUpdateSeen == null || Time.unscaledTime < _retryAt) return;
                if (_loggedNotice != NoticeId)
                {
                    _loggedNotice = NoticeId;
                    EventLog.Write("[Updates] notice=" + NoticeId + " persisted=" + Plugin.CfgUpdateSeen.Value
                        + " acknowledged=" + Schedule.Acknowledged(NoticeId, Plugin.CfgUpdateSeen.Value));
                }
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
            builder.AddLabel(T(UpdateNoticeContent.SummaryChinese, UpdateNoticeContent.SummaryEnglish), "summary");
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
            Schedule.ConfirmVisible(false, Time.unscaledTime);
            if (_banner.root.activeInHierarchy && Ready()) Schedule.ConfirmVisible(true, Time.unscaledTime);
            EventLog.Write("[Updates] created notice=" + NoticeId + " durationSeconds=" + Duration
                + " morningReportClosed=true; awaiting active-for-2s or details click");
        }

        private static void Acknowledge(string reason)
        {
            if (Plugin.CfgUpdateSeen == null || Schedule.Acknowledged(NoticeId, Plugin.CfgUpdateSeen.Value)) return;
            Schedule.Shown(NoticeId);
            EventLog.Write("[Updates] acknowledged notice=" + NoticeId + " reason=" + reason);
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

        private static string DetailsText() => T(UpdateNoticeContent.DetailsChinese, UpdateNoticeContent.DetailsEnglish);

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
            if (_details != null && _details.IsAlive && _details.root != null && _details.root.activeInHierarchy)
                Acknowledge("details-clicked");
            if (_details?.Rect?.parent is RectTransform parent)
            {
                float width = Math.Min(580f, Math.Max(240f, parent.rect.width - 24f));
                float height = Math.Min(560f, Math.Max(180f, parent.rect.height - 40f));
                NativeUi.FixedSize(_details, width, height);
                NativeUi.Place(_details, parent, new PanelBounds((parent.rect.width - width) / 2,
                    (parent.rect.height - height) / 2, width, height));
            }
        }

        private static void CloseBanner()
        {
            _banner?.Close(); _banner = null; _countdown = null;
            Schedule.ConfirmVisible(false, Time.unscaledTime);
        }
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
