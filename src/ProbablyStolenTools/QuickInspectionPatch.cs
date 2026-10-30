using System;
using System.Collections.Generic;
using HarmonyLib;
#if MONO
using TMPro;
#else
using Il2CppTMPro;
#endif
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolenTools
{
    /// <summary>
    /// Adds a small, read-only summary to the native inspection page. It never
    /// changes item tags or invokes the game's inspection actions.
    /// </summary>
    internal static class QuickInspectionPatch
    {
        private static GameObject _go;
        private static CustomUIManager _manager;
        private static CustomUIWindow _window;
        private const string WindowId = "ProbablyStolenTools.inspection";
        private static TextMeshProUGUI _text;
        private static CustomUIElement _label;
        private static GameObject _ownerPanel;
        private static ScrollRect _scroll;
        private static RectTransform _rect, _parentRect;
        private static InspectionWindowPosition _position;
        private static Vector2 _parentSize, _lastPosition;
        private static bool _positionDirty;
        private static string _result;
        private static float _nextRefresh;
        private static bool _loggedFailure;

        internal static void Inspect(InspectionUIManager ui, GameItem item)
        {
            try
            {
                if (!Plugin.CfgQuickInspection.Value || ui == null || ui.panel == null || !ui.panel.activeInHierarchy
                    || item == null || Id(ui.currentInspectingItem) != Id(item))
                {
                    Close();
                    return;
                }
                _result = BuildResult(item);
                Render(ui);
                _nextRefresh = Time.unscaledTime + 0.25f;
            }
            catch (Exception e)
            {
                Close();
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Plugin.LogSource.LogWarning("[QuickInspection] refresh failed: " + e);
                }
            }
        }

        internal static void Open(InspectionUIManager ui)
        {
            Inspect(ui, ui?.currentInspectingItem);
        }

        internal static void Tick()
        {
            try
            {
                if (_result == null) return;
                if (!Plugin.CfgQuickInspection.Value || _ownerPanel == null || !_ownerPanel.activeInHierarchy
                    || (_window != null && (!_window.IsAlive || _manager == null
                        || Id(CustomUIManager.Instance) != Id(_manager))))
                {
                    Close();
                    return;
                }
                HandleGeometry();
                if (Time.unscaledTime >= _nextRefresh) Open(InspectionUIManager.Instance);
            }
            catch (Exception e)
            {
                Close();
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Plugin.LogSource.LogWarning("[QuickInspection] update failed: " + e);
                }
            }
        }

        internal static void SettingsChanged()
        {
            if (!Plugin.CfgQuickInspection.Value) Close();
            else Open(InspectionUIManager.Instance);
        }

        internal static void Close()
        {
            _result = null;
            HideAndRelease();
        }

        private static string BuildResult(GameItem item)
        {
            if (item == null) return null;
            var lines = new List<string> { InspectionText.Escape(item.GetDisplayName()) };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var features = item.itemFeatures;
            if (features != null)
            {
                for (int i = 0; i < features.Count; i++)
                {
                    var feature = features[i];
                    if (feature == null || feature.isDisabled
                        || feature.category == WaterQualityPatch.Category
                        || (feature.featureType != ItemFeature.FeatureType.Normale
                            && feature.featureType != ItemFeature.FeatureType.Special)
                        || (!feature.useCondition && !feature.isExposable)) continue;
                    // Use the native actual condition, never infer "genuine" from
                    // missing issue tags. Drugs can also be expired or unusable.
                    if (feature.useCondition && feature.realCondition == null) continue;
                    string actual = InspectionText.Plain(feature.GetBaseRealDisplay());
                    if (string.IsNullOrWhiteSpace(actual)) continue;
                    string category = null;
                    if (!string.IsNullOrEmpty(feature.category))
                        ItemFeatureList.CategoryNames?.TryGetValue(feature.category, out category);
                    if (string.IsNullOrWhiteSpace(category))
                        category = InspectionLocale.Text("实际状态", "Actual condition");
                    string line = InspectionText.Feature(category, actual);
                    if (seen.Add(line))
                    {
                        bool? passed = InspectionChecks.AuthenticityPass(feature.category,
                            feature.realCondition?.identifier);
                        lines.Add(passed.HasValue ? InspectionText.Color(line, passed.Value) : line);
                    }
                }
            }
            foreach (string tag in new[] { "CIGARETTE_TAG", "FOOD_STAMP_TAG", "INS_INJECTOR_TAG" })
            {
                if (!item.IsTag(tag)) continue;
                foreach (var check in InspectionChecks.For(tag))
                    AddCheck(lines, InspectionLocale.UI(check.LocalizationKey, check.Chinese, check.English),
                        !item.IsTag(check.IssueTag));
            }
            if (item.IsTag("RIBWICH_TAG"))
            {
                AddCheck(lines, InspectionLocale.Text("真伪", "Authenticity"), !RibwichHelper.IsCounterfeit(item));
                AddCheck(lines, InspectionLocale.Text("包装密封", "Sealed packaging"), !RibwichHelper.IsOpened(item));
                int? month = TagInt(item, "RIBWICH_PRODUCTION_MONTH_TAG");
                int? year = TagInt(item, "RIBWICH_PRODUCTION_YEAR_TAG");
                if (month.HasValue && year.HasValue)
                    AddCheck(lines, InspectionLocale.Text("生产日期", "Production date"),
                        RibwichHelper.IsValidProductionDate(month.Value, year.Value));
                int? facility = TagInt(item, "RIBWICH_FACILITY_NUMBER_TAG");
                if (facility.HasValue)
                    AddCheck(lines, InspectionLocale.Text("工厂编号", "Facility number"),
                        RibwichHelper.IsValidFacilityNumber(facility.Value));
            }
            var wine = WineAppraisal.Lines(item);
            if (wine != null) lines.AddRange(wine);
            int value = AppraisalHelper.GetCorrectBaseValue(item);
            if (value >= 0) lines.Add(InspectionLocale.Text("估值：", "Value: ") + value);
            return string.Join("\n", lines.ToArray());
        }

        private static void AddCheck(List<string> lines, string label, bool passed)
            => lines.Add(InspectionText.Color(InspectionText.Escape(label) + ": "
                + InspectionLocale.Verdict(passed), passed));

        private static int? TagInt(GameItem item, string tag) => item.GetTagReadonly(tag)?.valueInt;

        private static void Render(InspectionUIManager ui)
        {
            if (ui == null || ui.panel == null || string.IsNullOrEmpty(_result)) return;
            if (_go == null || _ownerPanel != ui.panel)
            {
                HideAndRelease();
                Build(ui.panel);
            }

            if (_text == null) return;
            bool fontChanged = ApplyFont();
            _window.SetTitle(InspectionLocale.Text("快速鉴定", "Quick inspection"));
            if (_text.text != _result || fontChanged)
            {
                _text.text = _result;
                FitText();
                if (_scroll != null) _scroll.verticalNormalizedPosition = 1f;
            }
        }

        private static void Build(GameObject panel)
        {
            var builder = NativeUi.Create(WindowId, InspectionLocale.Text("快速鉴定", "Quick inspection"),
                InspectionWindowPosition.Width, InspectionWindowPosition.Height, true, out _window);
            if (builder == null) return;
            _manager = CustomUIManager.Instance;
            _ownerPanel = panel;
            _go = _window.root;
            _rect = _window.Rect;
            _parentRect = _rect.parent.Cast<RectTransform>();
            _position = new InspectionWindowPosition(Plugin.CfgInspectionX.Value, Plugin.CfgInspectionY.Value);
            builder.BeginScrollFlexible(0);
            builder.BeginColumn(4, TextAnchor.UpperLeft);
            builder.AddLabel(_result, null);
            _label = builder.Last;
            _text = _label.text;
            _text.enableAutoSizing = false;
            _text.enableWordWrapping = true;
            _text.richText = true;
            _text.raycastTarget = false;
            builder.End().End();
            builder.Show();
            NativeUi.FixedSize(_window, InspectionWindowPosition.Width, InspectionWindowPosition.Height);
            _scroll = _window.ContentRoot.GetComponentInChildren<ScrollRect>();
            if (_scroll != null)
            {
                _scroll.horizontal = false;
                _scroll.inertia = false;
                _scroll.movementType = ScrollRect.MovementType.Clamped;
            }
            ApplyFont();
            Layout();
        }

        private static bool ApplyFont()
        {
            try
            {
                var font = FontLoader.Instance?.simplifiedChineseFont;
                if (font != null && Id(_text.font) != Id(font))
                {
                    _text.font = font;
                    _text.fontSharedMaterial = font.material;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static void Layout()
        {
            _parentSize = _parentRect.rect.size;
            var bounds = _position.Resolve(_parentSize.x, _parentSize.y);
            NativeUi.FixedSize(_window, bounds.Width, bounds.Height);
            NativeUi.Place(_window, _parentRect, bounds);
            _lastPosition = _rect.anchoredPosition;
            FitText();
        }

        private static void FitText()
        {
            if (_text == null) return;
            float width = Math.Max(1, (_scroll?.viewport?.rect.width ?? _rect.rect.width - 40f));
            var element = _label.root.GetComponent<LayoutElement>()
                ?? _label.root.AddComponent<LayoutElement>();
            element.minWidth = 0;
            element.preferredWidth = width;
            element.flexibleWidth = 1;
            element.preferredHeight = _text.GetPreferredValues(_text.text, width, float.PositiveInfinity).y + 4f;
            LayoutRebuilder.ForceRebuildLayoutImmediate(_window.ContentRoot);
        }

        private static void HandleGeometry()
        {
            if (_rect == null || _parentRect == null) return;
            if (_parentRect.rect.size != _parentSize)
            {
                SavePosition();
                Layout();
            }
            if (_rect.anchoredPosition != _lastPosition)
            {
                var bounds = NativeUi.Bounds(_rect, _parentRect);
                _position = _position.Drag(bounds, 0, 0, _parentSize.x, _parentSize.y);
                _positionDirty = true;
                _lastPosition = _rect.anchoredPosition;
            }
            if (_positionDirty && !Input.GetMouseButton(0))
            {
                Layout();
                SavePosition();
            }
        }

        private static void SavePosition()
        {
            if (!_positionDirty) return;
            _positionDirty = false;
            if (Plugin.CfgInspectionX.Value == _position.X && Plugin.CfgInspectionY.Value == _position.Y) return;
            var config = Plugin.Instance.Config;
            bool auto = config.SaveOnConfigSet;
            try
            {
                config.SaveOnConfigSet = false;
                Plugin.CfgInspectionX.Value = _position.X;
                Plugin.CfgInspectionY.Value = _position.Y;
                config.Save();
            }
            finally { config.SaveOnConfigSet = auto; }
        }

        internal static void HideAndRelease()
        {
            try { SavePosition(); }
            catch (Exception e) { Plugin.LogSource.LogWarning("[QuickInspection] position save failed: " + e.Message); }
            try
            {
                if (_manager != null && _window != null && _window.IsAlive) _manager.CloseWindow(WindowId);
            }
            catch { }
            _window = null;
            _manager = null;
            _go = null;
            _text = null;
            _label = null;
            _ownerPanel = null;
            _scroll = null;
            _rect = _parentRect = null;
        }
    }

    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.OnInspectItem))]
    internal static class QuickInspectionInspectPatch
    {
        [HarmonyPostfix]
        private static void Postfix(InspectionUIManager __instance, GameItem gameItem)
            => QuickInspectionPatch.Inspect(__instance, gameItem);
    }

    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.OpenUI))]
    internal static class QuickInspectionOpenPatch
    {
        [HarmonyPostfix]
        private static void Postfix(InspectionUIManager __instance)
            => QuickInspectionPatch.Open(__instance);
    }

    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.CloseUI))]
    internal static class QuickInspectionClosePatch
    {
        [HarmonyPrefix]
        private static void Prefix() => QuickInspectionPatch.Close();
    }

    [HarmonyPatch(typeof(InspectionUIManager), nameof(InspectionUIManager.ResetUI))]
    internal static class QuickInspectionResetPatch
    {
        [HarmonyPrefix]
        private static void Prefix() => QuickInspectionPatch.Close();
    }
}
