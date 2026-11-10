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
                PanelNotice.Show(InspectionLocale.Text("无法打开日志窗口：", "Cannot open log: ") + e.Message + "\n" + EventLog.Path, true);
            }
        }

        private static void Build()
        {
            var builder = CustomUIManager.Window(WindowId, "运行日志 / Log", CustomUIManager.LAYER_OVERLAY);
            if (builder == null)
            {
                PanelNotice.Show(InspectionLocale.Text("无法创建日志窗口\n", "Cannot create log window\n") + EventLog.Path, true);
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
                    .AddButton(InspectionLocale.Text("关闭", "Close"), Close, "close")
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
            if (!File.Exists(path)) return InspectionLocale.Text("日志还没有生成：\n", "Log not created yet:\n") + path;
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
                return InspectionLocale.Text("读取日志失败：", "Cannot read log: ") + e.Message + "\n" + path;
            }
            var sb = new StringBuilder();
            sb.Append(InspectionLocale.Text("日志文件：", "Log: ")).Append(path).Append('\n');
            sb.Append(InspectionLocale.Text(
                "回报问题请附上 PSTools.log 及 .1-.3、PSTools_errors.log 及 .1-.2、设置文件和 Player.log。\n\n",
                "Reports: include PSTools.log and .1-.3, PSTools_errors.log and .1-.2, settings and Player.log.\n\n"));
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
