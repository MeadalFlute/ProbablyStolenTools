using ProbablyStolenTools;

internal static class WindowSnapTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("pin markers follow Chinese locales and fall back to English", () =>
        {
            check(WindowGestureRules.PinLabel("zh-CN") == "[置顶]");
            check(WindowGestureRules.PinLabel("zh-TW") == "[置顶]");
            check(WindowGestureRules.PinLabel("en") == "[PIN]");
            check(WindowGestureRules.PinLabel("de") == "[PIN]");
            check(WindowGestureRules.PinLabel(null) == "[PIN]");
        });
        test("pinning toggles off and re-pinning places the window after existing pins", () =>
        {
            var first = new object();
            var second = new object();
            var pins = new List<object>();
            check(WindowGestureRules.TogglePin(pins, first));
            check(WindowGestureRules.TogglePin(pins, second));
            check(pins.SequenceEqual(new[] { first, second }));
            check(!WindowGestureRules.TogglePin(pins, first));
            check(pins.SequenceEqual(new[] { second }));
            check(WindowGestureRules.TogglePin(pins, first));
            check(pins.SequenceEqual(new[] { second, first }));
        });
        test("pinned composition follows item position and flips at the viewport edge", () =>
        {
            check(PinnedTooltipLayout.Place(new PanelBounds(100, 80, 30, 40), 200, 150, 800, 600)
                == new PanelBounds(136, 80, 200, 150));
            check(PinnedTooltipLayout.Place(new PanelBounds(750, 80, 30, 40), 200, 150, 800, 600)
                == new PanelBounds(544, 80, 200, 150));
            check(PinnedTooltipLayout.Place(new PanelBounds(750, 590, 30, 40), 200, 150, 800, 600).Y == 450);
        });
        test("pinned composition clamps negative coordinates without resizing its contents", () =>
        {
            check(PinnedTooltipLayout.Place(new PanelBounds(-90, -30, 20, 20), 200, 150, 800, 600)
                == new PanelBounds(0, 0, 200, 150));
        });
        test("inventory tooltip uses canvas-space item and scaled view bounds", () =>
        {
            // A fixed inventory can have a zero-sized intermediate root. Its origin is not a viewport.
            check(PinnedTooltipLayout.Place(new PanelBounds(350, 438, 30, 40), 200, 150, 800, 600)
                == new PanelBounds(386, 438, 200, 150));
            check(PinnedTooltipLayout.Place(new PanelBounds(700, 550, 45, 60), 300, 225, 800, 600)
                == new PanelBounds(394, 375, 300, 225));
        });
        test("pinning accepts only idle Shift-middle single clicks on eligible targets", () =>
        {
            check(WindowGestureRules.PinClick(true, true, 1, false, true));
            check(!WindowGestureRules.PinClick(false, true, 1, false, true));
            check(!WindowGestureRules.PinClick(true, false, 1, false, true));
            check(!WindowGestureRules.PinClick(true, true, 2, false, true));
            check(!WindowGestureRules.PinClick(true, true, 1, true, true));
            check(!WindowGestureRules.PinClick(true, true, 1, false, false));
        });
        test("pinning refuses left-held, single/group item drag and UI drag independently", () =>
        {
            for (int mask = 0; mask < 16; mask++)
            {
                bool busy = WindowGestureRules.PinInteractionBusy((mask & 1) != 0, (mask & 2) != 0,
                    (mask & 4) != 0, (mask & 8) != 0);
                check(busy == (mask != 0));
                check(WindowGestureRules.PinClick(true, true, 1, busy, true) == (mask == 0));
                check(!WindowGestureRules.PinClick(true, false, 1, busy, true));
            }
        });
        test("window snapping aligns adjacent sides and title edges", () =>
        {
            var result = WindowSnap.Align(new PanelBounds(205, 103, 100, 100),
                new[] { new PanelBounds(100, 100, 100, 100) }, 800, 600);
            check(result == new PanelBounds(200, 100, 100, 100));
        });
        test("window snapping aligns vertical neighbours and preserves size", () =>
        {
            var result = WindowSnap.Align(new PanelBounds(98, 208, 100, 100),
                new[] { new PanelBounds(100, 100, 100, 100) }, 800, 600);
            check(result == new PanelBounds(100, 200, 100, 100));
        });
        test("window snapping ignores distant windows", () =>
        {
            var moving = new PanelBounds(201, 400, 100, 100);
            check(WindowSnap.Align(moving, new[] { new PanelBounds(100, 100, 100, 100) }, 800, 600) == moving);
        });
        test("window snapping never introduces out-of-bounds edges", () =>
        {
            var moving = new PanelBounds(0, 0, 100, 100);
            check(WindowSnap.Align(moving, new[] { new PanelBounds(95, 0, 100, 100) }, 800, 600) == moving);
        });
        test("window snapping chooses nearest edge independent of enumeration order", () =>
        {
            var moving = new PanelBounds(205, 100, 100, 100);
            var near = new PanelBounds(100, 100, 100, 100);
            var far = new PanelBounds(98, 100, 100, 100);
            var result = WindowSnap.Align(moving, new[] { far, near }, 800, 600);
            check(result.X == 200 && result.Y == 100);
            check(result == WindowSnap.Align(moving, new[] { near, far }, 800, 600));
        });
    }
}
