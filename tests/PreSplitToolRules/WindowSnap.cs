using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    internal static class WindowGestureRules
    {
        internal static string PinLabel(string locale) => LocalePolicy.Text(locale, "[置顶]", "[PIN]");
        internal static bool TogglePin<T>(List<T> pins, T entry)
        {
            if (pins.Remove(entry)) return false;
            pins.Add(entry);
            return true;
        }
        internal static bool PinInteractionBusy(bool leftHeld, bool singleDragging, bool groupDragging, bool uiDragging) =>
            leftHeld || singleDragging || groupDragging || uiDragging;
        internal static bool PinClick(bool shift, bool middleButton, int clickCount, bool busy, bool surface) =>
            shift && middleButton && clickCount == 1 && !busy && surface;
    }

    internal static class PinnedTooltipLayout
    {
        internal static PanelBounds Place(PanelBounds item, float width, float height, float parentWidth, float parentHeight)
        {
            float x = item.X + item.Width + 6;
            if (x + width > parentWidth) x = item.X - width - 6;
            x = Math.Max(0, Math.Min(x, Math.Max(0, parentWidth - width)));
            float y = Math.Max(0, Math.Min(item.Y, Math.Max(0, parentHeight - height)));
            return new PanelBounds(x, y, width, height);
        }
    }

    internal static class WindowSnap
    {
        internal static PanelBounds Align(PanelBounds moving, IEnumerable<PanelBounds> others,
            float parentWidth, float parentHeight, float distance = 10)
        {
            float dx = 0, dy = 0, bestX = distance + 1, bestY = distance + 1;
            foreach (var other in others)
            {
                bool nearY = moving.Y <= other.Y + other.Height + distance
                    && moving.Y + moving.Height >= other.Y - distance;
                bool nearX = moving.X <= other.X + other.Width + distance
                    && moving.X + moving.Width >= other.X - distance;
                if (nearY)
                {
                    TryX(other.X - moving.Width);
                    TryX(other.X + other.Width);
                }
                if (nearX)
                {
                    TryY(other.Y - moving.Height);
                    TryY(other.Y + other.Height);
                }
                // Align shared edges only when the windows are adjacent on the other axis.
                if (Math.Abs(moving.X + moving.Width - other.X) <= distance
                    || Math.Abs(moving.X - other.X - other.Width) <= distance)
                {
                    TryY(other.Y);
                    TryY(other.Y + other.Height - moving.Height);
                }
                if (Math.Abs(moving.Y + moving.Height - other.Y) <= distance
                    || Math.Abs(moving.Y - other.Y - other.Height) <= distance)
                {
                    TryX(other.X);
                    TryX(other.X + other.Width - moving.Width);
                }
            }
            return new PanelBounds(moving.X + dx, moving.Y + dy, moving.Width, moving.Height);

            void TryX(float x)
            {
                float delta = x - moving.X, gap = Math.Abs(delta);
                if (gap <= distance && gap < bestX && x >= 0 && x + moving.Width <= parentWidth)
                { dx = delta; bestX = gap; }
            }
            void TryY(float y)
            {
                float delta = y - moving.Y, gap = Math.Abs(delta);
                if (gap <= distance && gap < bestY && y >= 0 && y + moving.Height <= parentHeight)
                { dy = delta; bestY = gap; }
            }
        }
    }
}
