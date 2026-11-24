using System;

namespace ProbablyStolenTools
{
    internal static class CompatibilityNotice
    {
        private static CustomUIWindow _window;
        private static string _lastError;

        internal static bool Show(string message)
        {
            if (CustomUIManager.Instance == null) return false;
            try
            {
                if (_window != null && _window.IsAlive) return true;
                var builder = CustomUIManager.Window("ProbablyStolenTools.compatibility",
                    InspectionLocale.Text("Mod兼容提示", "Mod compatibility"), CustomUIManager.LAYER_OVERLAY);
                if (builder == null) return false;
                builder.SetSize(480f, 120f).SetDraggable(true).SetCloseOnEscape(false).Center()
                    .OnClose(() => _window = null).AddLabel(message, "message");
                var text = builder.Last?.text;
                string sourceLabel = InspectionLocale.Text("快速SL", "Quick SL");
                builder.AddLabel(sourceLabel, "source");
                var source = builder.Last?.text;
                float sourceHeight = 0;
                if (source != null)
                {
                    source.enableAutoSizing = false;
                    source.fontSize = (text != null ? text.fontSize : source.fontSize) * 0.8f;
                    source.fontStyle = Il2CppTMPro.FontStyles.Italic;
                    source.color = RenderHandler.GetColorFromPalette(RenderHandler.ColorPalette.LightBlue);
                    source.alignment = Il2CppTMPro.TextAlignmentOptions.BottomLeft;
                    sourceHeight = source.GetPreferredValues(sourceLabel).y + 4f;
                }
                if (text != null)
                {
                    text.enableWordWrapping = true;
                    float width = Math.Clamp(text.GetPreferredValues(message).x + 40f, 340f, 540f);
                    float height = text.GetPreferredValues(message, width - 40f, 0f).y + 72f + sourceHeight;
                    builder.SetSize(width, Math.Max(110f, height));
                }
                // The native title bar already provides the only close button.
                _window = builder.Show();
                _lastError = null;
                return _window != null && _window.IsAlive;
            }
            catch (Exception e)
            {
                string error = e.GetType().Name + ": " + e.Message;
                if (_lastError != error) EventLog.Write("[Compatibility] notice unavailable: " + error);
                _lastError = error;
                return false;
            }
        }

    }
}
