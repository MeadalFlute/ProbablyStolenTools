using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace ProbablyStolenTools
{
    /// <summary>
    /// 游戏内日志查看窗。按模组制作指南改用官方 CustomUIManager.Window(...) 构建器：
    /// 原生外观、自带滚动，不再手排窗框与 RectMask2D。
    /// </summary>
    internal static class LogWindow
    {
        private const string WindowId = "ProbablyStolenTools.log";
        private const int MaxLines = 400;
        private static CustomUIWindow _window;

        internal static void Toggle()
        {
            try
            {
                if (_window != null && _window.IsAlive)
                {
                    if (_window.IsVisible) { _window.Hide(); return; }
                    Reload();
                    _window.Show();
                    _window.BringToFront();
                    return;
                }
                Build();
            }
            catch (Exception e)
            {
                PanelNotice.Show("无法打开日志窗口：" + e.Message + "\n路径：" + EventLog.Path, true);
            }
        }

        private static void Build()
        {
            var builder = CustomUIManager.Window(WindowId, "运行日志 / Log", CustomUIManager.LAYER_OVERLAY);
            if (builder == null)
            {
                PanelNotice.Show("无法创建日志窗口\n路径：" + EventLog.Path, true);
                return;
            }
            _window = builder
                .SetSize(1120f, 760f)
                .SetDraggable(true)
                .SetCloseOnEscape(false)
                .Center()
                .BeginScrollFlexible(600f)
                    .AddLabel(ReadTail(), "log-text")
                .End()
                .BeginRow(8f, TextAnchor.MiddleRight)
                    .AddFlexibleSpace()
                    .AddButton("关闭", Close, "close")
                .End()
                .Show();
        }

        private static void Reload()
        {
            try { _window?.Get("log-text")?.SetText(ReadTail()); } catch { }
        }

        private static void Close()
        {
            try { _window?.Close(); } catch { }
            _window = null;
        }

        private static string ReadTail()
        {
            string path = EventLog.Path;
            if (!File.Exists(path)) return "日志还没有生成：\n" + path;
            var lines = new System.Collections.Generic.List<string>(MaxLines);
            try
            {
                // 只读尾部，避免把整份日志读进内存。
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
                return "读取日志失败：" + e.Message + "\n" + path;
            }
            var sb = new StringBuilder();
            sb.Append("日志文件：").Append(path).Append('\n').Append('\n');
            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line.Length > 200) line = line.Substring(0, 200) + "…";
                sb.Append(line).Append('\n');
            }
            return sb.ToString();
        }
    }
}
