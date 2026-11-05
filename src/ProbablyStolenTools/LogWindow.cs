using System;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 游戏内日志查看窗：读取 PSTools.log 尾部若干行显示。
    /// 不使用 Process.Start 或 Application.OpenURL —— 官方 ModScanner 会把它们记为 process-start 硬违规。
    /// </summary>
    internal static class LogWindow
    {
        private const int MaxLines = 40;
        private static GameObject _root;
        private static TextMeshProUGUI _text;
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
            // 窗口开大一些：日志行较长，小窗只能显示几行。
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
            // 先放一个带 RectMask2D 的容器，保证文字不会画出窗框外。
            var view = new GameObject("log-view", typeof(RectTransform));
            view.transform.SetParent(host, false);
            var viewRect = view.GetComponent<RectTransform>();
            viewRect.anchorMin = Vector2.zero;
            viewRect.anchorMax = Vector2.one;
            viewRect.offsetMin = new Vector2(4, 4);
            viewRect.offsetMax = new Vector2(-4, -4);
            var viewImage = view.AddComponent<Image>();
            viewImage.color = new Color(0, 0, 0, 0);
            viewImage.raycastTarget = false;
            view.AddComponent<RectMask2D>();

            var go = new GameObject("log-text", typeof(RectTransform));
            go.transform.SetParent(view.transform, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            _text = go.AddComponent<TextMeshProUGUI>();
            _text.fontSize = 13;
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.enableWordWrapping = true;
            _text.raycastTarget = false;
            _text.color = new Color(0.82f, 0.9f, 0.86f);
            var font = FontLoader.Instance?.simplifiedChineseFont;
            if (font != null) { _text.font = font; _text.fontSharedMaterial = font.material; }

            _root = window.Rect.gameObject;
            _root.SetActive(false);
        }

        private static void Reload()
        {
            string path = EventLog.Path;
            if (!File.Exists(path))
            {
                _text.text = "日志还没有生成：\n" + path;
                return;
            }
            var lines = new System.Collections.Generic.List<string>(MaxLines);
            try
            {
                // 只读尾部：先按大小截断，避免整份日志读入内存。
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = Math.Max(0, stream.Length - 64 * 1024);
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
                return;
            }
            var sb = new StringBuilder();
            sb.Append("日志文件：").Append(path).Append('\n').Append('\n');
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line.Length > 150) line = line.Substring(0, 150) + "…";
                sb.Append(line).Append('\n');
            }
            _text.text = sb.ToString();
        }
    }
}
