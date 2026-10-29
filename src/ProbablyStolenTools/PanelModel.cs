using System;

namespace ProbablyStolenTools
{
    internal static class PanelHeader
    {
        internal const float Height = 34, ButtonWidth = 48, Gap = 8, Right = 10;
        internal static float FoldX(float width) => width - Right - ButtonWidth;
        internal static float SettingsX(float width) => FoldX(width) - Gap - ButtonWidth;
        internal static bool CanDrag(float x, float y, float width)
            => x >= 0 && x < SettingsX(width) - Gap && y >= 0 && y <= Height;
    }

    internal readonly record struct PanelBounds(float X, float Y, float Width, float Height)
    {
        internal PanelBounds Constrain(float screenWidth, float screenHeight)
        {
            float width = Math.Clamp(Finite(Width, 480), Math.Min(380, screenWidth), Math.Min(900, screenWidth));
            float height = Math.Clamp(Finite(Height, 400), Math.Min(180, screenHeight), Math.Min(720, screenHeight));
            return new PanelBounds(Math.Clamp(Finite(X, 16), 0, Math.Max(0, screenWidth - width)),
                Math.Clamp(Finite(Y, 16), 0, Math.Max(0, screenHeight - height)), width, height);
        }
        private static float Finite(float value, float fallback) => float.IsFinite(value) ? value : fallback;
    }

    internal readonly record struct InspectionWindowPosition(float X, float Y)
    {
        internal const float Width = 300, Height = 240, HeaderHeight = 30, Margin = 24;

        internal PanelBounds Resolve(float parentWidth, float parentHeight)
        {
            var area = Available(parentWidth, parentHeight);
            return new PanelBounds(area.X + Unit(X) * area.Width,
                area.Y + Unit(Y) * area.Height,
                Math.Min(Width, Extent(parentWidth)), Math.Min(Height, Extent(parentHeight)));
        }

        internal InspectionWindowPosition Drag(PanelBounds start, float dx, float dy,
            float parentWidth, float parentHeight)
        {
            var area = Available(parentWidth, parentHeight);
            return new InspectionWindowPosition(
                area.Width > 0 ? Unit((start.X + dx - area.X) / area.Width) : Unit(X),
                area.Height > 0 ? Unit((start.Y + dy - area.Y) / area.Height) : Unit(Y));
        }

        private static PanelBounds Available(float parentWidth, float parentHeight)
        {
            float x = Math.Max(0, Extent(parentWidth) - Width);
            float y = Math.Max(0, Extent(parentHeight) - Height);
            float marginX = Math.Min(Margin, x / 2), marginY = Math.Min(Margin, y / 2);
            return new PanelBounds(marginX, marginY, x - marginX * 2, y - marginY * 2);
        }

        private static float Unit(float value) => float.IsFinite(value) ? Math.Clamp(value, 0, 1) : 1;
        private static float Extent(float value) => float.IsFinite(value) ? Math.Max(1, value) : 1;
    }

    internal sealed class NodeDoubleClick
    {
        private object _last;
        private float _at;
        internal bool Click(object node, float time)
        {
            bool restore = ReferenceEquals(_last, node) && time >= _at && time - _at <= 0.35f;
            _last = restore ? null : node;
            _at = time;
            return restore;
        }
        internal void Reset() { _last = null; }
    }
}
