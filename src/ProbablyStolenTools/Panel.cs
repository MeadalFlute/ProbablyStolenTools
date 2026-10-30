using System;
using System.Collections.Generic;
#if !MONO
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
#endif
#if MONO
using TMPro;
#else
using Il2CppTMPro;
#endif
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolenTools
{
    internal static class PanelNotice
    {
        internal static string Message;
        internal static float Until;
        internal static bool Warning;
        internal static void Show(string message, bool warning = false)
        {
            Message = message;
            Warning = warning;
            Until = Time.unscaledTime + (warning ? 3f : 2.2f);
        }
    }

    public class CheckpointPanel : MonoBehaviour
    {
        private GameObject _canvasGo, _panelGo, _body, _toast, _settingsGo;
        private CustomUIManager _manager;
        private CustomUIWindow _window;
        private const string WindowId = "ProbablyStolenTools.checkpoints";
        private readonly List<Il2CppSystem.Object> _callbacks = new();
        private RectTransform _nativeContent, _settingsViewport, _settingsContent;
        private Vector2 _lastWindowPosition;
        private float _retryAt;
        private RectTransform _settingsButton, _foldButton, _headerControls;
        private bool _settingsOpen;
        private readonly List<(RectTransform Rect, Toggle Toggle, ConfigEntry<bool> Config)> _settings = new();
        private readonly List<(CustomUIElement Element, Plugin.HotkeyBinding Binding)> _keyRows = new();
        private Plugin.HotkeyBinding _captureTarget;
        private static readonly KeyCode[] AllKeys = (KeyCode[])Enum.GetValues(typeof(KeyCode));

        /// <summary>正在等待玩家按新键：此时所有 mod 热键都必须让路。</summary>
        internal static bool CapturingKey { get; private set; }
        private RectTransform _canvasRect, _panelRect, _viewport, _content;
        private RawImage _resize;
        private TextMeshProUGUI _title, _hint, _fold, _empty, _toastText;
        private CanvasGroup _toastAlpha;
        private ScrollRect _scroll;
        private readonly List<Row> _rows = new();
        private readonly List<TextMeshProUGUI> _text = new();
        private readonly NodeDoubleClick _click = new();
        private readonly List<TextMeshProUGUI> _columns = new();
        private PanelBounds _bounds;
        private bool _built, _visible = true, _folded, _dragging, _resizing;
        private Vector2 _pointerStart;
        private PanelBounds _gestureStart;
        private int _revision = -1, _fileCount = -1, _day = -1;
        private long _activeId = -1;
        private long _closedId = -1;
        private bool _closedUsable;
        private float _screenW, _screenH;
        private int _shown;
        private const float HeaderH = PanelHeader.Height, BodyTop = 44f, RowH = 28f;

        private sealed class Row
        {
            internal GameObject Go;
            internal Graphic Bg;
            internal Color IdleColor;
            internal Button Button;
            internal TextMeshProUGUI Time, Label, Cash, State;
            internal object Node;
        }

#if !MONO
        public CheckpointPanel(IntPtr ptr) : base(ptr) { }
        public CheckpointPanel() : base(ClassInjector.DerivedConstructorPointer<CheckpointPanel>())
        { ClassInjector.DerivedConstructorBody(this); }
#endif

        private void Start()
        {
            try { BuildNotice(); }
            catch (Exception e) { Plugin.LogSource.LogWarning("[Panel] build failed: " + e); }
        }

#if MONO
        private void OnDestroy()
        {
            try { if (_built) EndGesture(); ReleaseWindow(); }
            finally
            {
                if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            }
        }
#endif

        private void Update()
        {
            if (CapturingKey) { CaptureStep(); return; }
            try
            {
                bool inGame = IsInGame();
                if (Plugin.Down(Plugin.CfgKeyPanel)) _visible = !_visible;
                bool stale = _built && (_manager == null || _window == null || !_window.IsAlive
                    || Id(CustomUIManager.Instance) != Id(_manager));
                if (stale) ReleaseWindow();
                if (!_built && inGame && CustomUIManager.Instance != null && Time.unscaledTime >= _retryAt)
                    Build();
                UpdateNotice(inGame);
                if (!_built) return;
                _panelGo.SetActive(inGame && _visible);
                if (_canvasRect.rect.width != _screenW || _canvasRect.rect.height != _screenH)
                {
                    EndGesture();
                    Layout();
                }
                if (_panelGo.activeSelf) { HandleGeometry(); PlaceHeaderButtons(); }
                else if (_dragging || _resizing) EndGesture();
                int day = CurrentDay();
                if (_revision != CustomerCheckpoint.Revision || _fileCount != Rewind.Points.Count
                    || _day != day || _activeId != CustomerCheckpoint.ActiveId
                    || _closedId != (CustomerCheckpoint.ClosedNode?.Id ?? 0)
                    || _closedUsable != (CustomerCheckpoint.ClosedNode?.Graph != null))
                    Refresh(day);
                string hint = (CustomerCheckpoint.CustomerCount > 0
                    ? $"顾客节点 {CustomerCheckpoint.CustomerCount}/10"
                        : ((int?)Plugin.Store()?.storeState == 1 ? "等待顾客节点"
                            : (int?)Plugin.Store()?.storeState == 0 ? "已闭店" : "尚未开业"));
                if (_hint.text != hint) _hint.text = hint;
                if (_settingsOpen)
                {
                    foreach (var setting in _settings)
                    {
                        setting.Toggle.SetIsOnWithoutNotify(setting.Config.Value);
                        setting.Toggle.interactable = setting.Config != Plugin.CfgAutoSubmit
                            && setting.Config != Plugin.CfgAlwaysOverwrite || Plugin.CfgAutoAppraisal.Value;
                    }
                }
            }
            catch (Exception e)
            {
                ReleaseWindow();
                _retryAt = Time.unscaledTime + 2f;
                Plugin.LogSource.LogWarning("[Panel] update failed: " + e);
            }
        }

        private static bool IsInGame()
        {
            if (Plugin.CfgPreviewPanel.Value) return true;
            if (MainMenuUIController.Instance != null) return false;
            return Plugin.Store() != null;
        }

        private static int CurrentDay() => Plugin.Store() == null ? -1 : StoreStation.GetDayCounter();

        private void Build()
        {
            _bounds = new PanelBounds(Plugin.CfgPanelX.Value, Plugin.CfgPanelY.Value,
                Plugin.CfgPanelWidth.Value, Plugin.CfgPanelHeight.Value);
            _folded = Plugin.CfgPanelFolded.Value;
            var builder = NativeUi.Create(WindowId, "今日回溯", _bounds.Width, _bounds.Height, true, out _window);
            if (builder == null) return;
            _manager = CustomUIManager.Instance;
            _panelGo = _window.root;
            _panelRect = _window.Rect;
            _canvasRect = _panelRect.parent.Cast<RectTransform>();
            _nativeContent = _window.ContentRoot;
            // The native frame owns its header and padding; only the content area is positioned manually.
            DisableLayout(_nativeContent);
            _title = _window.frame.titleText;
            try { if (_title != null) _title.color = new Color(0.55f, 0.85f, 1.0f); } catch { }
            _headerControls = NewObject("commands", _nativeContent).GetComponent<RectTransform>();
            // 把 设置/收起 挪到标题栏，与日期同一行，省掉内容区的一整行高度。
            try
            {
                if (_panelRect != null) _headerControls.SetParent(_panelRect, false);
            }
            catch { }
            var fold = NativeButton(_headerControls, "收起", () =>
            {
                _folded = !_folded; _settingsOpen = false; _click.Reset(); Layout(); Persist();
            });
            _foldButton = fold.Rect;
            _fold = fold.text;
            var settingsButton = NativeButton(_headerControls, "设置", () =>
            {
                _settingsOpen = !_settingsOpen;
                _folded = false;
                _click.Reset();
                Layout();
                Persist();
            });
            _settingsButton = settingsButton.Rect;
            _body = NewObject("body", _nativeContent);
            _hint = Text("status", _body.transform, 12, new Color(0.45f, 0.72f, 0.90f));
            foreach (string label in new[] { "时间", "节点", "现金", "状态" })
            {
                var text = Text("column", _body.transform, 12, new Color(0.42f, 0.62f, 0.82f));
                text.text = label;
                _columns.Add(text);
            }
            var viewportGo = NewObject("viewport", _body.transform);
            _viewport = viewportGo.GetComponent<RectTransform>();
            var hit = Raw("scrollHit", viewportGo.transform, Color.clear, true);
            Stretch(hit.rectTransform);
            viewportGo.AddComponent<RectMask2D>();
            var contentGo = NewObject("content", viewportGo.transform);
            _content = contentGo.GetComponent<RectTransform>();
            Place(_content, 0, 0, 0, 0);
            _scroll = viewportGo.AddComponent<ScrollRect>();
            _scroll.viewport = _viewport;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = RowH;
            _scroll.inertia = false;
            for (int i = 0; i < 12; i++) _rows.Add(NewRow(i));
            _empty = Text("empty", viewportGo.transform, 13, new Color(0.55f, 0.70f, 0.85f));
            _empty.text = "暂无节点";
            _settingsGo = NewObject("settings", _nativeContent);
            _settingsViewport = _settingsGo.GetComponent<RectTransform>();
            var settingsHit = Raw("scrollHit", _settingsViewport, Color.clear, true);
            Stretch(settingsHit.rectTransform);
            _settingsGo.AddComponent<RectMask2D>();
            _settingsContent = NewObject("content", _settingsViewport).GetComponent<RectTransform>();
            var settingsScroll = _settingsGo.AddComponent<ScrollRect>();
            settingsScroll.viewport = _settingsViewport;
            settingsScroll.content = _settingsContent;
            settingsScroll.horizontal = false;
            settingsScroll.inertia = false;
            settingsScroll.movementType = ScrollRect.MovementType.Clamped;
            settingsScroll.scrollSensitivity = RowH;
            NewSetting("自动鉴定报价", Plugin.CfgAutoAppraisal);
            NewSetting("自动提交鉴定", Plugin.CfgAutoSubmit);
            NewSetting("始终覆盖鉴定报价", Plugin.CfgAlwaysOverwrite);
            NewSetting("跳过顾客对话", Plugin.CfgSkipCustomerDialogue);
            NewSetting(InspectionLocale.Text("双击物品快速鉴定", "Double-click to inspect"), Plugin.CfgDoubleClickInspection);
            NewSetting(InspectionLocale.Text("显示快速鉴定摘要", "Show quick inspection summary"), Plugin.CfgQuickInspection);
            NewSetting(InspectionLocale.Text("整日回退需确认", "Confirm whole-day rewind"), Plugin.CfgConfirmWholeDay);
            _keyRows.Clear();
            foreach (var binding in Plugin.Hotkeys) NewHotkeyRow(binding);
            var resetKeys = NativeButton(_settingsContent, InspectionLocale.Text("恢复默认快捷键", "Reset hotkeys"),
                () => { Plugin.ResetHotkeys(); RefreshKeyLabels(); });
            resetKeys.root.name = "hotkey-reset";
            StackSettingsRow(resetKeys.Rect, 28f);
            _resize = Raw("resize", _panelGo.transform, new Color(1, 1, 1, 0.04f), true);
            var grip = Text("grip", _resize.transform, 16, new Color(0.6f, 0.68f, 0.7f), TextAlignmentOptions.Center);
            grip.text = "\u25E2";
            Place(grip.rectTransform, 0, 0, 20, 20);
            // Keep header commands above the scrolling body and settings in draw/hit order.
            _resize.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            _headerControls.SetAsLastSibling();
            builder.Show();
            _built = true;
            Refresh(CurrentDay());
            try
            {
                var parts = new List<string>();
                foreach (var row in _keyRows)
                    if (row.Element != null)
                        parts.Add(row.Binding.Id + "@" + row.Element.Rect.anchoredPosition.y.ToString("0"));
                Plugin.LogSource.LogInfo($"[Panel] checkpoint panel built (v6). settings h={_settingsContent.sizeDelta.y:0} " +
                                          $"keyRows={_keyRows.Count} [{string.Join(", ", parts)}]");
            }
            catch { }
            Plugin.LogSource.LogInfo("[Panel] checkpoint panel built with native CustomUIManager (v6).");
        }

        private void BuildNotice()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("PSTools_Notice");
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            _toast = NewObject("notice", _canvasGo.transform);
            var toastRect = _toast.GetComponent<RectTransform>();
            toastRect.anchorMin = toastRect.anchorMax = new Vector2(0.5f, 1);
            toastRect.pivot = new Vector2(0.5f, 1);
            toastRect.anchoredPosition = new Vector2(0, -30);
            toastRect.sizeDelta = new Vector2(420, 30);
            var toastBg = Raw("noticeBg", _toast.transform, new Color(0.035f, 0.05f, 0.045f, 0.72f));
            Stretch(toastBg.rectTransform);
            _toastText = NewObject("noticeText", _toast.transform).AddComponent<TextMeshProUGUI>();
            _toastText.fontSize = 14;
            _toastText.alignment = TextAlignmentOptions.Center;
            _toastText.raycastTarget = false;
            Stretch(_toastText.rectTransform);
            _toastAlpha = _toast.AddComponent<CanvasGroup>();
            _toastAlpha.interactable = false;
            _toastAlpha.blocksRaycasts = false;
            _toast.SetActive(false);
        }

        private void UpdateNotice(bool inGame)
        {
            if (_toast == null) BuildNotice();
            bool notice = inGame && Time.unscaledTime < PanelNotice.Until;
            _toast.SetActive(notice);
            if (!notice) return;
            _toastText.text = PanelNotice.Message;
            var font = FontLoader.Instance?.simplifiedChineseFont;
            if (font != null) { _toastText.font = font; _toastText.fontSharedMaterial = font.material; }
            _toastText.color = PanelNotice.Warning ? new Color(1, 0.8f, 0.65f) : new Color(0.78f, 0.94f, 0.86f);
            _toastAlpha.alpha = Mathf.Clamp01((PanelNotice.Until - Time.unscaledTime) / 0.55f);
        }

#if !MONO
        [HideFromIl2Cpp]
#endif
        private void NewSetting(string label, ConfigEntry<bool> config)
        {
            Il2CppSystem.Action<bool> callback = new Action<bool>(value =>
            {
                config.Value = value;
                Plugin.Instance.Config.Save();
                if (config == Plugin.CfgAutoAppraisal && value)
                    AppraisalPatch.Postfix(InspectionUIManager.Instance);
            });
            _callbacks.Add(callback);
            var builder = new CustomUIBuilder(_manager, _window, _settingsContent);
            builder.AddToggle(label, config.Value, callback, null);
            var element = builder.Last;
            _settings.Add((element.Rect, element.toggle, config));
        }

        // ---------------- 快捷键改键 ----------------

        private string HotkeyText(Plugin.HotkeyBinding binding)
            => binding.Label + "   [" + (binding.Config.Value == KeyCode.None
                ? InspectionLocale.Text("已停用", "disabled")
                : Plugin.KeyName(binding.Config.Value)) + "]";

#if !MONO
        [HideFromIl2Cpp]
#endif
        private void NewHotkeyRow(Plugin.HotkeyBinding binding)
        {
            var element = NativeButton(_settingsContent, HotkeyText(binding), () => BeginCapture(binding));
            element.root.name = "hotkey-" + binding.Id;
            StackSettingsRow(element.Rect, 28f);
            _keyRows.Add((element, binding));
        }

        /// <summary>
        /// 设置区的开关行是预制体自己纵向排的，而 AddButton 用的按钮预制体不会参与这套排版
        /// （直接叠在左上角、还会露到可视区外）。这里按现有行的最低点手动堆叠，
        /// 并标记 ignoreLayout，避免和任何布局组打架。
        /// </summary>
        private void StackSettingsRow(RectTransform rect, float height)
        {
            if (rect == null || _settingsContent == null) return;
            float bottom = 0f, lastHeight = 0f;
            for (int i = 0; i < _settingsContent.childCount; i++)
            {
                var child = _settingsContent.GetChild(i) as RectTransform;
                if (child == null || child == rect || !child.gameObject.activeSelf) continue;
                float y = child.anchoredPosition.y;
                if (y <= bottom) { bottom = y; lastHeight = child.rect.height; }
            }
            float top = bottom - (lastHeight > 0 ? lastHeight : height) - 2f;

            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.sizeDelta = new Vector2(_settingsContent.rect.width, height);
            rect.anchoredPosition = new Vector2(0f, top);

            var layout = rect.GetComponent<LayoutElement>() ?? rect.gameObject.AddComponent<LayoutElement>();
            layout.ignoreLayout = true;

            float needed = -top + height + 8f;
            if (_settingsContent.sizeDelta.y < needed)
                _settingsContent.sizeDelta = new Vector2(_settingsContent.sizeDelta.x, needed);
        }

        private void BeginCapture(Plugin.HotkeyBinding binding)
        {
            if (binding == null) return;
            _captureTarget = binding;
            CapturingKey = true;
            var row = _keyRows.Find(r => r.Binding == binding);
            if (row.Element != null && row.Element.text != null)
                row.Element.SetText(InspectionLocale.Text(
                    "按下新的按键…（Esc 取消，Backspace 恢复默认，Delete 清除）",
                    "Press a new key… (Esc cancels, Backspace resets, Delete clears)"));
        }

        private void CaptureStep()
        {
            try
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { EndCapture(null); return; }
                if (Input.GetKeyDown(KeyCode.Backspace))
                {
                    EndCapture(_captureTarget != null ? _captureTarget.Default : (KeyCode?)null);
                    return;
                }
                if (Input.GetKeyDown(KeyCode.Delete)) { EndCapture(KeyCode.None); return; }
                for (int i = 0; i < AllKeys.Length; i++)
                {
                    var key = AllKeys[i];
                    if (key == KeyCode.None || IsMouseKey(key)) continue;
                    if (Input.GetKeyDown(key)) { EndCapture(key); return; }
                }
            }
            catch (Exception e)
            {
                Plugin.LogSource?.LogWarning("[Keys] capture failed: " + e.Message);
                EndCapture(null);
            }
        }

        private void EndCapture(KeyCode? key)
        {
            var binding = _captureTarget;
            _captureTarget = null;
            CapturingKey = false;
            if (binding != null && key.HasValue)
            {
                if (key.Value == KeyCode.None && binding.Id == "panel")
                {
                    // 面板键不能清空：没有它就没有任何入口再打开设置。
                    PanelNotice.Show(InspectionLocale.Text(
                        "面板快捷键不能设为空，否则无法再打开设置",
                        "The panel hotkey cannot be disabled - it is the only way back into settings"));
                }
                else
                {
                    if (key.Value != binding.Config.Value) Plugin.SetHotkey(binding.Config, key.Value);
                    PanelNotice.Show(InspectionLocale.Text(
                        key.Value == KeyCode.None
                            ? $"{binding.Label} 已停用"
                            : $"{binding.Label} 已改为 {Plugin.KeyName(key.Value)}",
                        key.Value == KeyCode.None
                            ? $"{binding.Label} disabled"
                            : $"{binding.Label} is now {Plugin.KeyName(key.Value)}"));
                }
            }
            RefreshKeyLabels();
        }

        private void RefreshKeyLabels()
        {
            for (int i = 0; i < _keyRows.Count; i++)
            {
                var row = _keyRows[i];
                if (row.Element != null && row.Element.text != null)
                    row.Element.SetText(HotkeyText(row.Binding));
            }
        }

        private static bool IsMouseKey(KeyCode key)
            => key >= KeyCode.Mouse0 && key <= KeyCode.Mouse6;

#if !MONO
        [HideFromIl2Cpp]
#endif
        private Row NewRow(int index)
        {
            var row = new Row();
            var element = NativeButton(_content, "", () => ClickRow(row));
            row.Go = element.root;
            row.Button = element.button;
            row.Bg = row.Button.targetGraphic;
            row.IdleColor = row.Bg.color;
            if (element.text != null) element.text.enabled = false;
            DisableLayout(element.Rect);
            row.Time = Text("time", row.Go.transform, 13, new Color(0.52f, 0.82f, 0.96f));
            row.Label = Text("label", row.Go.transform, 13, new Color(0.82f, 0.72f, 1.0f));
            row.Cash = Text("cash", row.Go.transform, 13, new Color(0.45f, 0.92f, 0.68f), TextAlignmentOptions.Right);
            row.State = Text("state", row.Go.transform, 12, new Color(1.0f, 0.62f, 0.50f), TextAlignmentOptions.Right);
            return row;
        }

#if !MONO
        [HideFromIl2Cpp]
#endif
        private void ClickRow(Row row)
        {
            if (_dragging || _resizing || row.Node == null) return;
            if (!_click.Click(row.Node, Time.unscaledTime)) return;
            if (row.Node is CustomerCheckpoint.Snapshot snapshot) CustomerCheckpoint.Restore(snapshot);
            else if (row.Node is Rewind.Point point) Rewind.Restore(point);
        }

        private void Refresh(int day)
        {
            _revision = CustomerCheckpoint.Revision;
            _fileCount = Rewind.Points.Count;
            _activeId = CustomerCheckpoint.ActiveId;
            _closedId = CustomerCheckpoint.ClosedNode?.Id ?? 0;
            _closedUsable = CustomerCheckpoint.ClosedNode?.Graph != null;
            _day = day;
            _click.Reset();
            _shown = 0;
            var closed = CustomerCheckpoint.ClosedNode;
            if (closed != null && closed.Day == day)
                Bind(_rows[_shown++], closed, closed.At, "闭店", closed.Cash,
                    closed.Graph == null ? closed.ExpiredReason : closed.Id == _activeId ? "当前"
                        : CustomerCheckpoint.IsFuture(closed) ? "后续" : "", closed.Graph != null);
            var nodes = CustomerCheckpoint.Nodes;
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                var node = nodes[i];
                if (node.Day != day || node.IsClosed) continue;
                Bind(_rows[_shown++], node, node.At, CustomerCheckpoint.Name(node), node.Cash,
                    node.Id == _activeId ? "当前" : CustomerCheckpoint.IsFuture(node) ? "后续" : "", true);
            }
            for (int i = Rewind.Points.Count - 1; i >= 0; i--)
            {
                var p = Rewind.Points[i];
                if (p.Day != day || !p.IsDayStart || p.State != 2 || p.SlotId != Plugin.Store()?.saveSlotId
                    || p.RunId != Plugin.Store()?.runID) continue;
                Bind(_rows[_shown++], p, p.At, "开业前", p.Cash, "存档", true);
                break;
            }
            for (int i = _shown; i < _rows.Count; i++) { _rows[i].Node = null; _rows[i].Go.SetActive(false); }
            _title.text = day < 0 ? "今日回溯" : $"今日回溯 · 第 {day} 天";
            _empty.gameObject.SetActive(_shown == 0);
            Layout();
        }

#if !MONO
        [HideFromIl2Cpp]
#endif
        private void Bind(Row row, object node, DateTime time, string label, int cash, string state, bool usable)
        {
            row.Node = node;
            row.Go.SetActive(true);
            row.Time.text = time.ToString("HH:mm:ss");
            row.Label.text = label;
            row.Cash.text = cash.ToString();
            row.State.text = state;
            row.Bg.color = state == "当前" ? new Color(0.28f, 0.45f, 0.85f, row.IdleColor.a) : row.IdleColor;
            row.Button.interactable = usable;
        }

        private void Layout()
        {
            _screenW = Math.Max(1, _canvasRect.rect.width);
            _screenH = Math.Max(1, _canvasRect.rect.height);
            _bounds = _bounds.Constrain(_screenW, _screenH);
            float h = _folded ? PanelHeader.Height + 10f : _bounds.Height;
            NativeUi.FixedSize(_window, _bounds.Width, h);
            NativeUi.Place(_window, _canvasRect, _bounds);
            _lastWindowPosition = _panelRect.anchoredPosition;
            float w = Math.Max(1, _nativeContent.rect.width);
            float contentH = Math.Max(1, _nativeContent.rect.height);
            Place(_headerControls, 0, 0, w, HeaderH);
            Place(_settingsButton, PanelHeader.SettingsX(w), -4, PanelHeader.ButtonWidth, 26);
            Place(_foldButton, PanelHeader.FoldX(w), -4, PanelHeader.ButtonWidth, 26);
            _fold.text = _folded ? "展开" : "收起";
            Place(_body.GetComponent<RectTransform>(), 0, 0, w, contentH);
            _body.SetActive(!_folded && !_settingsOpen);
            _settingsGo.SetActive(!_folded && _settingsOpen);
            Place(_settingsViewport, 0, -HeaderH, w, Math.Max(1, contentH - HeaderH));
            Place(_settingsContent, 0, 0, w, _settings.Count * 32f);
            for (int i = 0; i < _settings.Count; i++)
            {
                var setting = _settings[i];
                Place(setting.Rect, 0, -i * 32, w - 8, 30);
                setting.Toggle.interactable = i != 1 && i != 2 || Plugin.CfgAutoAppraisal.Value;
            }
            Place(_hint.rectTransform, 4, -2, Math.Max(1, w - 16), 20);
            float labelW = w - 76 - 72 - 54 - 32;
            float[] xs = { 12, 88, 88 + labelW, w - 66 };
            float[] ws = { 76, labelW, 72, 54 };
            for (int i = 0; i < 4; i++) Place(_columns[i].rectTransform, xs[i], -24, ws[i], 18);
            _columns[2].alignment = _columns[3].alignment = TextAlignmentOptions.Right;
            Place(_viewport, 0, -BodyTop, w, Math.Max(1, contentH - BodyTop - 12));
            _content.sizeDelta = new Vector2(w, _shown * RowH);
            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                Place(row.Go.GetComponent<RectTransform>(), 0, -i * RowH, w, RowH);
                var texts = new[] { row.Time, row.Label, row.Cash, row.State };
                for (int c = 0; c < 4; c++) Place(texts[c].rectTransform, xs[c], 0, ws[c], RowH);
            }
            Place(_empty.rectTransform, 12, -6, w - 24, 24);
            Place(_resize.rectTransform, _bounds.Width - 24, -_bounds.Height + 24, 20, 20);
            _resize.gameObject.SetActive(!_folded);
            PlaceHeaderButtons();
        }

        // 按钮跟着标题文本走：世界坐标对齐，避免被原生布局组推走
        private void PlaceHeaderButtons()
        {
            try
            {
                if (_headerControls == null || _title == null || _panelRect == null) return;
                var titleRt = _title.rectTransform;
                if (titleRt == null) return;
                float boxW = PanelHeader.ButtonWidth * 2 + PanelHeader.Gap;
                _headerControls.anchorMin = _headerControls.anchorMax = new Vector2(1f, 1f);
                _headerControls.pivot = new Vector2(1f, 0.5f);
                _headerControls.sizeDelta = new Vector2(boxW, 22f);
                // 贴窗口右侧内缘；纵向对齐标题文本中心（相对顶边，向下为负）
                var worldCenter = titleRt.TransformPoint(new Vector3(titleRt.rect.center.x, titleRt.rect.center.y, 0f));
                float localY = _panelRect.InverseTransformPoint(worldCenter).y;
                float offsetFromTop = localY - _panelRect.rect.yMax;
                _headerControls.anchoredPosition = new Vector2(-10f, offsetFromTop);
                Place(_settingsButton, 0f, 0f, PanelHeader.ButtonWidth, 22f);
                Place(_foldButton, PanelHeader.ButtonWidth + PanelHeader.Gap, 0f, PanelHeader.ButtonWidth, 22f);
            }
            catch { }
        }

        private void HandleGeometry()
        {
            Vector2 point;
            var canvas = _canvasRect.GetComponentInParent<Canvas>();
            var camera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, Input.mousePosition, camera, out point)) return;
            var rect = _canvasRect.rect;
            point = new Vector2(point.x - rect.xMin, rect.yMax - point.y);
            if (Input.GetMouseButtonDown(0))
            {
                float x = point.x - _bounds.X, y = point.y - _bounds.Y;
                _resizing = !_folded && x >= _bounds.Width - 24 && x <= _bounds.Width
                    && y >= _bounds.Height - 24 && y <= _bounds.Height && NativeUi.Topmost(_resize.rectTransform);
                if (_resizing) { _pointerStart = point; _gestureStart = _bounds; _click.Reset(); }
            }
            if (!_resizing && _panelRect.anchoredPosition != _lastWindowPosition)
            {
                var bounds = NativeUi.Bounds(_panelRect, _canvasRect);
                _bounds = _bounds with { X = bounds.X, Y = bounds.Y };
                _lastWindowPosition = _panelRect.anchoredPosition;
                _dragging = true;
                _click.Reset();
            }
            if (_resizing)
            {
                var delta = point - _pointerStart;
                _bounds = _gestureStart with { Width = _gestureStart.Width + delta.x, Height = _gestureStart.Height + delta.y };
                Layout();
            }
            if ((_dragging || _resizing) && !Input.GetMouseButton(0)) { Layout(); EndGesture(); }
        }

        private void EndGesture() { _dragging = _resizing = false; Persist(); }

        private void Persist()
        {
            bool auto = Plugin.Instance.Config.SaveOnConfigSet;
            try
            {
                Plugin.Instance.Config.SaveOnConfigSet = false;
                Plugin.CfgPanelX.Value = _bounds.X;
                Plugin.CfgPanelY.Value = _bounds.Y;
                Plugin.CfgPanelWidth.Value = _bounds.Width;
                Plugin.CfgPanelHeight.Value = _bounds.Height;
                Plugin.CfgPanelFolded.Value = _folded;
                Plugin.Instance.Config.Save();
            }
            finally { Plugin.Instance.Config.SaveOnConfigSet = auto; }
        }

        private void ReleaseWindow()
        {
            _built = false;
            _click.Reset();
            _dragging = _resizing = false;
            try
            {
                if (_manager != null && _window != null && _window.IsAlive) _manager.CloseWindow(WindowId);
            }
            catch { }
            _window = null;
            _manager = null;
            _panelGo = null;
            _rows.Clear();
            _text.Clear();
            _columns.Clear();
            _settings.Clear();
            _callbacks.Clear();
            _settingsOpen = false;
        }

        private static void DisableLayout(RectTransform rect)
        {
            var group = rect.GetComponent<LayoutGroup>();
            if (group != null) group.enabled = false;
            var fitter = rect.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;
        }

#if !MONO
        [HideFromIl2Cpp]
#endif
        private CustomUIElement NativeButton(RectTransform parent, string label, Action action)
        {
            Il2CppSystem.Action callback = action;
            _callbacks.Add(callback);
            var builder = new CustomUIBuilder(_manager, _window, parent);
            builder.AddButton(label, callback, null);
            return builder.Last;
        }

        private static GameObject NewObject(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<RectTransform>();
            return go;
        }

        private static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static RawImage Raw(string name, Transform parent, Color color, bool raycast = false)
        {
            var image = NewObject(name, parent).AddComponent<RawImage>();
            image.texture = Texture2D.whiteTexture;
            image.color = color; image.raycastTarget = raycast;
            return image;
        }

        private TextMeshProUGUI Text(string name, Transform parent, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.Left)
        {
            var builder = new CustomUIBuilder(_manager, _window, parent.Cast<RectTransform>());
            builder.AddLabel("", null);
            var element = builder.Last;
            var text = element.text;
            // Label prefabs may wrap TMP; retain the native typography without their automatic layout.
            if (text.gameObject != element.root)
            {
                text.transform.SetParent(parent, false);
                UnityEngine.Object.Destroy(element.root);
            }
            DisableLayout(text.rectTransform);
            text.gameObject.name = name;
            text.enableAutoSizing = false;
            text.fontSize = size; text.color = color; text.alignment = alignment;
            // 黑色描边，提升在杂乱背景上的可读性
            try
            {
                var baseMat = text.fontSharedMaterial;
                if (baseMat != null)
                {
                    var mat = new Material(baseMat);
                    mat.EnableKeyword("OUTLINE_ON");
                    mat.SetFloat("_OutlineWidth", 0.28f);
                    mat.SetColor("_OutlineColor", Color.black);
                    text.fontMaterial = mat;
                }
                text.fontStyle = Il2CppTMPro.FontStyles.Bold;
            }
            catch { }
            text.raycastTarget = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            _text.Add(text);
            return text;
        }
    }
}
