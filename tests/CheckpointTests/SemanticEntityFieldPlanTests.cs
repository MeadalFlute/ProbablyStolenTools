using System.Reflection;
using ProbablyStolenTools;

internal static class SemanticEntityFieldPlanTests
{
    private enum Style : long { Default, Docked }
    private class Inventory
    {
        public object Parent = null;
        public bool Locked = true;
        public Action Callback = null;
        public int Cache = 0;
        public object Visual = null;
    }
    private sealed class Slot : Inventory
    {
        public object Item = null;
        public object Borrowed = null;
    }
    private sealed class Window { public Style Style = Style.Default; }
    private sealed class ExtendedInventory
    {
        public object Parent = null;
        public bool Locked = true;
        public Action Callback = null;
        public int Cache = 0;
        public object Visual = null;
        public int Unknown = 1;
    }
    private static SemanticFieldRule[] Rules => new[]
    {
        new SemanticFieldRule("Parent", typeof(object), SemanticFieldRole.Entity),
        new SemanticFieldRule("Locked", typeof(bool), SemanticFieldRole.Entity),
        new SemanticFieldRule("Callback", typeof(Action), SemanticFieldRole.Runtime),
        new SemanticFieldRule("Cache", typeof(int), SemanticFieldRole.Runtime),
        new SemanticFieldRule("Visual", typeof(object), SemanticFieldRole.Visual)
    };
    private static SemanticEntityFieldPlan Plan() => new(typeof(Inventory), Rules);
    private static void Throws(Action action, Action<bool> check)
    {
        try { action(); throw new Exception("Expected rejection"); }
        catch (InvalidOperationException) { check(true); }
    }
    internal static void Run(Action<string, Action> test, Action<bool> check)
    {
        test("entity field plans require exhaustive explicit entity runtime and visual partitions", () =>
        {
            var plan = Plan();
            string prefix = typeof(Inventory).FullName + ".";
            check(plan.Type == typeof(Inventory) && plan.Fields.Length == 5 && plan.EntityFields.Length == 2
                && plan.RuntimeFields == 2 && plan.VisualFields == 1 && plan.RequiredSlots.Length == 4);
            check(plan.RoleOf(prefix + "Parent") == SemanticFieldRole.Entity
                && plan.RoleOf(prefix + "Callback") == SemanticFieldRole.Runtime
                && plan.RoleOf(prefix + "Visual") == SemanticFieldRole.Visual
                && !plan.RequiredSlots.Contains(prefix + "Visual"));
        });
        test("entity field plans reject unknown added renamed and mistyped fields", () =>
        {
            Throws(() => new SemanticEntityFieldPlan(typeof(ExtendedInventory), Rules), check);
            var rules = Rules;
            rules[0] = rules[0] with { Name = "Renamed" };
            Throws(() => new SemanticEntityFieldPlan(typeof(Inventory), rules), check);
            rules[0] = rules[0] with { Name = "Parent", Type = typeof(string) };
            Throws(() => new SemanticEntityFieldPlan(typeof(Inventory), rules), check);
            Throws(() => Plan().RoleOf(typeof(Inventory).FullName + ".Unknown"), check);
        });
        test("entity field plans reject duplicate and invalid classifications", () =>
        {
            var rules = Rules;
            rules[4] = rules[0];
            Throws(() => new SemanticEntityFieldPlan(typeof(Inventory), rules), check);
            rules[4] = Rules[4] with { Role = (SemanticFieldRole)123 };
            Throws(() => new SemanticEntityFieldPlan(typeof(Inventory), rules), check);
        });
        test("entity field plans separate declaring types across an inventory inheritance chain", () =>
        {
            var parent = Plan();
            var child = new SemanticEntityFieldPlan(typeof(Slot),
                new SemanticFieldRule("Item", typeof(object), SemanticFieldRole.Entity),
                new SemanticFieldRule("Borrowed", typeof(object), SemanticFieldRole.Entity));
            check(child.EntityFields.Length == 2 && child.RequiredSlots.All(name => name.StartsWith(typeof(Slot).FullName + "."))
                && parent.EntityFields.All(field => field.DeclaringType == typeof(Inventory)));
            Throws(() => child.RoleOf(typeof(Inventory).FullName + ".Parent"), check);
        });
        test("entity records omit runtime and visual values only when the old graph remains authoritative", () =>
        {
            var data = new Slot { Parent = new Window(), Borrowed = new Window() };
            var basePlan = Plan();
            var childPlan = new SemanticEntityFieldPlan(typeof(Slot),
                new SemanticFieldRule("Item", typeof(object), SemanticFieldRole.Entity),
                new SemanticFieldRule("Borrowed", typeof(object), SemanticFieldRole.Entity));
            var access = new ManagedStateAccess(null, typeof(Inventory).Assembly.GetName().Name);
            var id = ManagedObjects.Id(data);
            access.AddRoot(id);
            using var graph = StateGraph.Capture(access, new[] { id });
            using var records = SemanticEntitySnapshot.Capture(new[]
            {
                new SemanticEntityProjection(data, basePlan.EntityFields.Concat(childPlan.EntityFields).ToArray())
            }, ManagedObjects.Id, graph.Contains);
            data.Cache = 9;
            data.Visual = new object();
            data.Callback = () => throw new Exception("Callback invoked");
            check(records.Differences(out _) == 0 && graph.Differences().Count == 3);
            data.Borrowed = null; data.Locked = false; data.Parent = null;
            check(records.Differences(out _) == 3);
            graph.Restore();
            check(records.Differences(out _) == 0 && graph.Differences().Count == 0);
        });
        test("entity enum records preserve named and unnamed full width enum values", () =>
        {
            var window = new Window { Style = (Style)long.MinValue };
            var plan = new SemanticEntityFieldPlan(typeof(Window),
                new SemanticFieldRule("Style", typeof(Style), SemanticFieldRole.Entity));
            using var record = SemanticEntitySnapshot.Capture(new[] { new SemanticEntityProjection(window, plan.EntityFields) },
                ManagedObjects.Id, _ => true);
            window.Style = Style.Docked;
            check(record.Differences(out _) == 1);
            window.Style = (Style)long.MinValue;
            check(record.Differences(out _) == 0);
        });
        test("entity field plans do not retain mutable caller rule arrays or provide writes", () =>
        {
            var rules = Rules;
            var plan = new SemanticEntityFieldPlan(typeof(Inventory), rules);
            rules[0] = rules[0] with { Role = SemanticFieldRole.Visual };
            check(plan.RoleOf(typeof(Inventory).FullName + ".Parent") == SemanticFieldRole.Entity
                && !typeof(SemanticEntityFieldPlan).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    .Any(method => method.Name.Contains("Restore") || method.Name.Contains("Write")));
        });
    }
}
