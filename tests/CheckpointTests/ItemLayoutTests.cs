using ProbablyStolenTools;

internal static class ItemLayoutTests
{
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        StateGraph Capture(GameItemElement item, bool legacy = false)
        {
            string assembly = typeof(GameItemElement).Assembly.GetName().Name;
            var id = ManagedObjects.Id(item);
            if (legacy)
            {
                var access = new LegacyManagedStateAccess(gameAssembly: assembly);
                access.AddRoot(id);
                return StateGraph.Capture(access, new[] { id });
            }
            var current = new ManagedStateAccess(gameAssembly: assembly);
            current.AddRoot(id);
            return StateGraph.Capture(current, new[] { id });
        }

        test("item business state restores without rewinding native layout cache", () =>
        {
            var item = new GameItemElement { X = 4, Y = 6, Orientation = 1, Flipped = true, Units = 2 };
            item.UpdateLayout();
            using var graph = Capture(item);
            item.X = 9; item.Y = 0; item.Orientation = 2; item.Flipped = false; item.Units = 0;
            item.UpdateLayout();
            item.validateLock = true;
            graph.Restore();
            check(item.X == 4 && item.Y == 6 && item.Orientation == 1 && item.Flipped && item.Units == 2);
            check(item.appliedMinX == 9 && item.appliedMinY == 0 && item.appliedOrientation == 2
                && item.appliedGridSize == 16 && !item.appliedFlipped && item.validateLock);
            item.appliedGridSize = -1; item.validateLock = false;
            item.UpdateLayout();
            check(item.View.X == 64 && item.View.Y == -96 && item.View.Orientation == 1 && item.View.Flipped);
            check(graph.Differences().Count == 0);
        });

        test("modeled stale-cache failure reproduces and final layout survives graph reconciliation", () =>
        {
            var item = new GameItemElement { X = 4, Y = 6 };
            item.UpdateLayout();
            using (var old = Capture(item, legacy: true))
            {
                item.X = 6; item.Y = 0; item.UpdateLayout();
                old.Restore();
                item.UpdateLayout();
                check(item.X == 4 && item.Y == 6 && item.View.X == 96 && item.View.Y == 0);
            }
            item.appliedGridSize = -1; item.UpdateLayout();
            using var earlier = Capture(item);
            item.X = 10; item.Y = 3; item.Orientation = 3; item.UpdateLayout();
            using var later = Capture(item);
            for (int i = 0; i < 4; i++)
            {
                earlier.Restore();
                // Model a UI callback repacking the item before the second restore.
                item.X = 0; item.Y = 0; item.UpdateLayout();
                earlier.Restore();
                item.appliedGridSize = -1; item.UpdateLayout();
                check(item.View.X == 64 && item.View.Y == -96 && earlier.Differences().Count == 0);
                later.Restore();
                item.appliedGridSize = -1; item.UpdateLayout();
                check(item.View.X == 160 && item.View.Y == -48 && item.View.Orientation == 3);
                check(later.Differences().Count == 0);
            }
        });
    }
}

// These model the game's managed cache and an excluded native view, not Unity rendering.
internal sealed class GameItemElement
{
    internal int X, Y, Orientation, Units = 1;
    internal bool Flipped;
    internal int appliedMinX, appliedMinY, appliedOrientation, appliedGridSize = -1;
    internal bool appliedFlipped, validateLock;
    internal TreeNodeRenderFixture View = new();

    internal void UpdateLayout()
    {
        if (appliedMinX == X && appliedMinY == Y && appliedOrientation == Orientation
            && appliedFlipped == Flipped && appliedGridSize == 16) return;
        appliedMinX = X; appliedMinY = Y; appliedOrientation = Orientation;
        appliedFlipped = Flipped; appliedGridSize = 16;
        View.X = X * 16; View.Y = -Y * 16; View.Orientation = Orientation; View.Flipped = Flipped;
    }
}

internal sealed class TreeNodeRenderFixture
{
    internal int X, Y, Orientation;
    internal bool Flipped;
}
