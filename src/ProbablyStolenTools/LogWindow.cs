using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 游戏内日志查看窗：读取 PSTools.log 尾部若干行，可滚轮翻阅。
    /// 不使用 Process.Start 或 Application.OpenURL —— 官方 ModScanner 会把它们记为 process-start 硬违规。
    /// </summary>
    internal static class LogWindow
    {
        private const int MaxLines = 400;
        private static GameObject _root;
        private static TextMeshProUGUI _text;
        private static RectTransform _content, _viewRect;
        private static ScrollRect _scroll;
        private static CustomUIWindow _window;

        internal static void Toggle()
        {
            try
            {
                if (_root == null || _text == null) Build();
                if (_root == null)
                {
                    PanelNotice.Show("无法创建日志窗口\n路径：" + EventLog.Path, true);
                    return;
                }
                bool show = !_root.activeSelf;
                _root.SetActive(show);
                if (show)
                {
                    Reload();
                    try { _window.Rect.SetAsLastSibling(); } catch { }
                }
            }
            catch (Exception e)
            {
                PanelNotice.Show("无法打开日志窗口：" + e.Message + "\n路径：" + EventLog.Path, true);
            }
        }

        private static void Build()
        {
            var manager = CustomUIManager.Instance;
            if (manager == null) return;
            var builder = NativeUi.Create("ProbablyStolenTools.log", "运行日志 / Log", 1120f, 800f, true, out var window);
            _window = window;
            NativeUi.FixedSize(window, 1120f, 800f);
            try
            {
                var overlay = manager.GetLayerRoot("overlay");
                if (overlay != null)
                {
                    var bounds = new PanelBounds(90, 70, 1120f, 800f)
                        .Constrain(overlay.rect.width, overlay.rect.height);
                    NativeUi.Place(window, overlay, bounds);
                }
            }
            catch { }
            try
            {
                if (window.frame.closeButton != null)
                {
                    window.frame.closeButton.gameObject.SetActive(true);
                    window.frame.closeButton.onClick.RemoveAllListeners();
                    window.frame.closeButton.onClick.AddListener(
                        (UnityEngine.Events.UnityAction)(() => { if (_root != null) _root.SetActive(false); }));
                }
            }
            catch { }

            var host = window.ContentRoot;

            // 手排容器：一个 RectTransform 只能有一个外部布局控制者，所以设 ignoreLayout。
            var view = new GameObject("log-view", typeof(RectTransform));
            view.transform.SetParent(host, false);
            _viewRect = view.GetComponent<RectTransform>();
            _viewRect.anchorMin = Vector2.zero;
            _viewRect.anchorMax = Vector2.one;
            _viewRect.offsetMin = new Vector2(4, 4);
            _viewRect.offsetMax = new Vector2(-4, -4);
            var viewImage = view.AddComponent<Image>();
            viewImage.color = new Color(0, 0, 0, 0.001f);   // 需要可接收滚轮事件
            viewImage.raycastTarget = true;
            view.AddComponent<RectMask2D>();
            view.AddComponent<LayoutElement>().ignoreLayout = true;

            var content = new GameObject("log-content", typeof(RectTransform));
            content.transform.SetParent(view.transform, false);
            _content = content.GetComponent<RectTransform>();
            _content.anchorMin = new Vector2(0, 1);
            _content.anchorMax = new Vector2(1, 1);
            _content.pivot = new Vector2(0.5f, 1);
            _content.anchoredPosition = Vector2.zero;
            _content.sizeDelta = Vector2.zero;

            var go = new GameObject("log-text", typeof(RectTransform));
            go.transform.SetParent(content.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.zero;

            _text = go.AddComponent<TextMeshProUGUI>();
            _text.fontSize = 13;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.enableWordWrapping = true;
            _text.overflowMode = TextOverflowModes.Overflow;
            _text.raycastTarget = false;
            _text.color = new Color(0.82f, 0.9f, 0.86f);
            var font = FontLoader.Instance?.simplifiedChineseFont;
            if (font != null) { _text.font = font; _text.fontSharedMaterial = font.material; }

            _scroll = view.AddComponent<ScrollRect>();
            _scroll.viewport = _viewRect;
            _scroll.content = _content;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 45f;
            _scroll.inertia = false;

            _root = window.Rect.gameObject;
            _root.SetActive(false);
        }

        private static void Reload()
        {
            string path = EventLog.Path;
            if (!File.Exists(path))
            {
                _text.text = "日志还没有生成：\n" + path;
                ApplySize();
                return;
            }
            var lines = new System.Collections.Generic.List<string>(MaxLines);
            try
            {
                // 只读尾部，避免整份日志读入内存。
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = Math.Max(0, stream.Length - 512 * 1024);
                    stream.Seek(start, SeekOrigin.Begin);
                    using (var reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            lines.Add(line);
                            if (lines.Count > MaxLines) lines.RemoveAt(0);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _text.text = "读取日志失败：" + e.Message + "\n" + path;
                ApplySize();
                return;
            }
            var sb = new StringBuilder();
            sb.Append("日志文件：").Append(path).Append('\n').Append('\n');
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line.Length > 200) line = line.Substring(0, 200) + "…";
                sb.Append(line).Append('\n');
            }
            _text.text = sb.ToString();
            ApplySize();
            // 打开时先看最新几行（内容底部）。
            try { _scroll.verticalNormalizedPosition = 0f; } catch { }
            try
            {
                EventLog.Write($"[Log] viewer text lines={lines.Count} chars={sb.Length} " +
                    $"text={_text.rectTransform.rect.width:0}x{_text.rectTransform.rect.height:0} " +
                    $"view={_viewRect.rect.width:0}x{_viewRect.rect.height:0} " +
                    $"active={_root.activeInHierarchy}");
            }
            catch { }
        }

        private static void ApplySize()
        {
            try
            {
                _text.ForceMeshUpdate();
                float height = Mathf.Max(1f, _text.preferredHeight);
                _text.rectTransform.sizeDelta = new Vector2(0f, height);
                _content.sizeDelta = new Vector2(0f, height);
            }
            catch { }
        }
    }
}
