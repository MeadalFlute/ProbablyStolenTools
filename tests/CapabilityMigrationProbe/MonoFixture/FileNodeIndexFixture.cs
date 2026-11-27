using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
public static partial class OfflineMonoFixture {
    private static void CheckFileNodeIndex(Assembly mod, Assembly game, Func<bool, object> setting)
    {
        var rewind = mod.GetType("ProbablyStolenTools.Rewind", true);
        var points = (IList)rewind.GetField("Points", All).GetValue(null);
        var previousPoints = new object[points.Count];
        points.CopyTo(previousPoints, 0);
        string[] stateFields = { "_indexedStore", "_indexedDay", "<PointRevision>k__BackingField" };
        var oldState = new object[stateFields.Length];
        for (int i = 0; i < stateFields.Length; i++) oldState[i] = rewind.GetField(stateFields[i], All).GetValue(null);
        var preference = mod.GetType("ProbablyStolenTools.Plugin", true).GetField("CfgKeepPreviousDay", All);
        var oldPreference = preference.GetValue(null);
        var catalog = rewind.GetField("Catalog", All).GetValue(null);
        var entries = catalog.GetType().GetField("_entries", All);
        var oldEntries = entries.GetValue(catalog);
        string root = Path.Combine(Path.GetDirectoryName(mod.Location), "qol-index-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType("PlayerStore", true));
            store.GetType().GetField("saveSlotId", All).SetValue(store, 2);
            store.GetType().GetField("runID", All).SetValue(store, "qol-fixture");
            void Add(int stamp, int day, int slot = 2, string run = "qol-fixture", int state = 2)
            {
                File.WriteAllText(Path.Combine(root, "202611" + stamp.ToString("D2") + "_120000_0000000_day.es3"),
                    "{\"storeStation\":{\"value\":{\"dayCounter\":" + day + "}},\"playerStore\":{\"value\":{\"playerCash\":100,\"storeState\":"
                    + state + ",\"saveSlotId\":" + slot + ",\"runID\":\"" + run + "\"}}}");
            }
            Add(1, 95); Add(2, 94); Add(3, 93); Add(4, 95, slot: 3); Add(5, 95, run: "foreign"); Add(6, 95, state: 1);
            string largePath = Path.Combine(root, "20261101_120000_0000000_day.es3");
            string largeText = File.ReadAllText(largePath);
            File.WriteAllText(largePath, "{\"padding\":\"" + new string('x', 21 * 1024 * 1024) + "\"," + largeText.Substring(1));
            var index = rewind.GetMethod("IndexDayStartPoints", All);
            points.Clear();
            preference.SetValue(null, setting(true));
            index.Invoke(null, new[] { store, (object)95, root });
            if (points.Count != 2) throw new Exception("Disabled-memory file index did not isolate current and retained previous morning.");
            foreach (var point in points)
            {
                var type = point.GetType();
                if (!(bool)type.GetField("IsDayStart", All).GetValue(point) || (int)type.GetField("State", All).GetValue(point) != 2
                    || (string)type.GetField("Label", All).GetValue(point) != "开业前") throw new Exception("Indexed morning file lost display metadata.");
            }
            index.Invoke(null, new[] { store, (object)95, root });
            if (points.Count != 2 || Directory.GetFiles(root).Length != 6) throw new Exception("File indexing duplicated nodes or wrote saves.");
            points.Clear();
            preference.SetValue(null, setting(false));
            index.Invoke(null, new[] { store, (object)95, root });
            if (points.Count != 1 || (int)points[0].GetType().GetField("Day", All).GetValue(points[0]) != 95)
                throw new Exception("File index ignored disabled previous-day retention.");
            Console.WriteLine("PASS actual game Mono QoL: existing morning files indexed read-only; day/slot/run/state boundaries, retained yesterday opt-out, display metadata and repeat deduplication.");
        }
        finally
        {
            points.Clear();
            foreach (var point in previousPoints) points.Add(point);
            for (int i = 0; i < stateFields.Length; i++) rewind.GetField(stateFields[i], All).SetValue(null, oldState[i]);
            preference.SetValue(null, oldPreference);
            entries.SetValue(catalog, oldEntries);
            // Only remove files from the unique fixture-owned directory created above.
            Directory.Delete(root, true);
        }
    }
}
