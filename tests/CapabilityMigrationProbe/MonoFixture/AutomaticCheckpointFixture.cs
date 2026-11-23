using System;
using System.Collections;
using System.Reflection;

public static partial class OfflineMonoFixture
{
    private static void CheckAutomaticCheckpointGates(Assembly mod)
    {
        var plugin = mod.GetType("ProbablyStolenTools.Plugin", true);
        var checkpoint = mod.GetType("ProbablyStolenTools.CustomerCheckpoint", true);
        var config = mod.GetType("ProbablyStolenTools.ConfigEntry`1", true).MakeGenericType(typeof(bool));
        var constructor = config.GetConstructors(All)[0];
        var enabled = plugin.GetField("CfgCustomerCheckpoint", All);
        var automatic = plugin.GetField("CfgAutoCheckpoints", All);
        var disable = plugin.GetField("CfgDisableMemoryCheckpoints", All);
        var handled = plugin.GetField("CfgHandledMemoryConflicts", All);
        var wholeDay = plugin.GetField("CfgWholeDayMemory", All);
        var keepPrevious = plugin.GetField("CfgKeepPreviousDay", All);
        var patches = checkpoint.GetField("PatchesReady", All);
        object oldEnabled = enabled.GetValue(null), oldAutomatic = automatic.GetValue(null), oldPatches = patches.GetValue(null);
        object oldDisable = disable.GetValue(null), oldHandled = handled.GetValue(null), oldWholeDay = wholeDay.GetValue(null);
        object oldKeepPrevious = keepPrevious.GetValue(null);
        var configType = mod.GetType("ProbablyStolenTools.ModConfig", true);
        string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(mod.Location), "pstools-conflicts-" + Guid.NewGuid().ToString("N") + ".xml");
        var owner = Activator.CreateInstance(configType, All, null, new object[] { path }, null);
        object Entry(bool value) => constructor.Invoke(new object[] { value, owner, "offline" });
        object Bind(Type type, string section, string key, object value) => configType.GetMethod("Bind", All)
            .MakeGenericMethod(type).Invoke(owner, new[] { section, key, value, "offline" });
        bool Auto() => (bool)checkpoint.GetProperty("AutoCapture", All).GetValue(null);
        try
        {
            disable.SetValue(null, Bind(typeof(bool), "CustomerCheckpoint", "DisableMemory", false));
            handled.SetValue(null, Bind(typeof(string), "Compatibility", "HandledMemoryConflicts", ""));
            foreach (bool ready in new[] { false, true })
            foreach (bool on in new[] { false, true })
            foreach (bool auto in new[] { false, true })
            {
                patches.SetValue(null, ready);
                enabled.SetValue(null, Entry(on));
                automatic.SetValue(null, Entry(auto));
                if (Auto() != (ready && on && auto)) throw new Exception("Automatic capture gate mismatch.");
                if ((bool)checkpoint.GetProperty("MaintenanceEnabled", All).GetValue(null) != (ready && on))
                    throw new Exception("Maintenance readiness mismatch.");
            }
            patches.SetValue(null, true);
            enabled.SetValue(null, Entry(true));
            foreach (var setting in new[] { null, Entry(false) })
            {
                automatic.SetValue(null, setting);
                if (Auto() || !(bool)checkpoint.GetProperty("Enabled", All).GetValue(null))
                    throw new Exception("Disabling automation disabled manual checkpoint support.");
                foreach (string method in new[] { "Arrival", "TryCapture", "TryCaptureDayStart", "TryCaptureClosed", "TryCaptureReturn" })
                    checkpoint.GetMethod(method, All).Invoke(null, null);
                // A null store is intentional: every automatic mode must return before accessing the graph.
                foreach (var mode in new[] { new[] { false, false, false }, new[] { true, false, false },
                    new[] { false, true, false }, new[] { false, false, true } })
                    checkpoint.GetMethod("Capture", All).Invoke(null,
                        new object[] { null, mode[0], false, mode[1], mode[2] });
            }
            Console.WriteLine("PASS actual game Mono: automatic capture gates (8 combinations), missing/false config skips all automatic paths; manual backend remains enabled.");
            automatic.SetValue(null, Entry(true));
            var rewind = mod.GetType("ProbablyStolenTools.Rewind", true);
            foreach (bool disabled in new[] { false, true })
            foreach (bool memory in new[] { false, true })
            foreach (bool auto in new[] { false, true })
            {
                disable.SetValue(null, Entry(disabled));
                wholeDay.SetValue(null, Entry(memory));
                automatic.SetValue(null, Entry(auto));
                bool useMemory = (bool)rewind.GetProperty("UseWholeDayMemory", All).GetValue(null);
                if (useMemory != (!disabled && memory && auto)) throw new Exception("Whole-day memory policy mismatch.");
                if ((bool)checkpoint.GetProperty("Enabled", All).GetValue(null) != !disabled)
                    throw new Exception("Whole-day preference affected individual checkpoints.");
                if (!(bool)checkpoint.GetProperty("MaintenanceEnabled", All).GetValue(null))
                    throw new Exception("Memory disable must not disable shutter maintenance.");
                foreach (bool keep in new[] { false, true })
                foreach (bool anchor in new[] { false, true })
                foreach (bool nodes in new[] { false, true })
                foreach (string action in new[] { "DayStart", "PreviousDay", "QuickReload" })
                {
                    keepPrevious.SetValue(null, Entry(keep));
                    object kind = Enum.Parse(mod.GetType("ProbablyStolenTools.Rewind+WholeDayAction", true), action);
                    bool needsFile = (bool)rewind.GetMethod("RequiresFileReload", All).Invoke(null, new[] { kind, (object)anchor, nodes });
                    bool expected = action == "QuickReload" || !useMemory
                        || (action == "PreviousDay" ? !keep || (!anchor && !nodes) : !anchor);
                    if (needsFile != expected) throw new Exception("Whole-day confirmation path mismatch: " + action);
                }
            }
            disable.SetValue(null, Bind(typeof(bool), "CustomerCheckpoint", "DisableMemory", false));
            Console.WriteLine("PASS actual game Mono: whole-day memory gates (8 combinations), 192 confirmation paths, shutter maintenance independent of memory disable.");
            var game = Assembly.Load("Assembly-CSharp");
            // Rev16's public Mods getter returns a defensive copy. Only this
            // offline fixture writes the private registry, never production code.
            var loader = game.GetType("ModLoader", true);
            var mods = (IList)(loader.GetField("mods", All)?.GetValue(null)
                ?? loader.GetProperty("Mods").GetValue(null));
            var loadedType = game.GetType("LoadedMod", true);
            var manifestType = game.GetType("ModManifest", true);
            var fake = Activator.CreateInstance(loadedType);
            var manifest = Activator.CreateInstance(manifestType);
            manifestType.GetField("ID", All).SetValue(manifest, "yexiaoyang.InteriorExpansion");
            loadedType.GetField("Manifest", All).SetValue(fake, manifest);
            loadedType.GetField("IsEnabled", All).SetValue(fake, true);
            mods.Add(fake);
            try
            {
                patches.SetValue(null, true);
                enabled.SetValue(null, Entry(true));
                automatic.SetValue(null, Entry(true));
                mod.GetType("ProbablyStolenTools.OptionalModCompatibility", true).GetMethod("Refresh", All).Invoke(null, null);
                if (Auto() || (bool)checkpoint.GetProperty("Enabled", All).GetValue(null))
                    throw new Exception("Claims must block both manual and automatic memory checkpoints. count=" + mods.Count
                        + " id=" + manifestType.GetField("ID", All).GetValue(manifest)
                        + " enabled=" + loadedType.GetField("IsEnabled", All).GetValue(fake)
                        + " running=" + mod.GetType("ProbablyStolenTools.OptionalModCompatibility", true)
                            .GetMethod("IsRunning", All).Invoke(null, new object[] { "yexiaoyang.InteriorExpansion" }));
                // Null roots prove the final capture guard also protects direct/manual entry.
                checkpoint.GetMethod("Capture", All).Invoke(null, new object[] { null, false, true, false, false });
                config.GetProperty("Value", All).SetValue(disable.GetValue(null), false);
                mod.GetType("ProbablyStolenTools.OptionalModCompatibility", true).GetMethod("Refresh", All).Invoke(null, null);
                if (!Auto()) throw new Exception("Manual conflict override must enable checkpoints.");
                configType.GetMethod("Save", All).Invoke(owner, null);
                owner = Activator.CreateInstance(configType, All, null, new object[] { path }, null);
                disable.SetValue(null, Bind(typeof(bool), "CustomerCheckpoint", "DisableMemory", true));
                handled.SetValue(null, Bind(typeof(string), "Compatibility", "HandledMemoryConflicts", ""));
                mod.GetType("ProbablyStolenTools.OptionalModCompatibility", true).GetMethod("Refresh", All).Invoke(null, null);
                if (!Auto()) throw new Exception("Saved override was lost after config reload.");
                CheckConflictSaveFailure(mod, plugin, checkpoint, configType, config, disable, handled);
                loadedType.GetField("IsEnabled", All).SetValue(fake, false);
                if (!Auto() || !(bool)checkpoint.GetProperty("Enabled", All).GetValue(null))
                    throw new Exception("A disabled optional mod must not disable vanilla checkpoints.");
                Console.WriteLine("PASS actual game Mono: Claims first-detection guards, explicit override and XML reload persistence; no optional assembly loaded.");
            }
            finally { mods.Remove(fake); }
        }
        finally
        {
            enabled.SetValue(null, oldEnabled);
            automatic.SetValue(null, oldAutomatic);
            patches.SetValue(null, oldPatches);
            disable.SetValue(null, oldDisable);
            handled.SetValue(null, oldHandled);
            wholeDay.SetValue(null, oldWholeDay);
            keepPrevious.SetValue(null, oldKeepPrevious);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            if (System.IO.File.Exists(path + ".bak")) System.IO.File.Delete(path + ".bak");
        }
    }

    private static void CheckConflictSaveFailure(Assembly mod, Type plugin, Type checkpoint, Type configType,
        Type boolEntry, FieldInfo disable, FieldInfo handled)
    {
        var instanceField = plugin.GetField("Instance", All);
        object oldInstance = instanceField.GetValue(null), oldDisable = disable.GetValue(null), oldHandled = handled.GetValue(null);
        string root = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(mod.Location), "pstools-config-retry-" + Guid.NewGuid().ToString("N"));
        string path = System.IO.Path.Combine(root, "settings.xml");
        var compat = mod.GetType("ProbablyStolenTools.OptionalModCompatibility", true);
        try
        {
            var owner = Activator.CreateInstance(configType, All, null, new object[] { path }, null);
            object Bind(Type type, string section, string key, object value) => configType.GetMethod("Bind", All)
                .MakeGenericMethod(type).Invoke(owner, new[] { section, key, value, "offline" });
            var instance = Activator.CreateInstance(plugin);
            plugin.GetProperty("Config", All).SetValue(instance, owner);
            instanceField.SetValue(null, instance);
            configType.GetProperty("SaveOnConfigSet", All).SetValue(owner, true);
            disable.SetValue(null, Bind(typeof(bool), "CustomerCheckpoint", "DisableMemory", false));
            handled.SetValue(null, Bind(typeof(string), "Compatibility", "HandledMemoryConflicts", ""));
            // The parent is deliberately absent: Save must fail, not escape Refresh.
            compat.GetMethod("Refresh", All).Invoke(null, null);
            if ((bool)checkpoint.GetProperty("Enabled", All).GetValue(null)
                || !(bool)configType.GetProperty("SaveOnConfigSet", All).GetValue(owner))
                throw new Exception("Failed conflict persistence lost policy or autosave mode.");
            configType.GetProperty("SaveOnConfigSet", All).SetValue(owner, false);
            boolEntry.GetProperty("Value", All).SetValue(disable.GetValue(null), false);
            configType.GetProperty("SaveOnConfigSet", All).SetValue(owner, true);
            System.IO.Directory.CreateDirectory(root);
            compat.GetField("_retryAfterUtcTicks", All).SetValue(null, 0L);
            compat.GetMethod("Refresh", All).Invoke(null, null);
            if (!(bool)checkpoint.GetProperty("Enabled", All).GetValue(null) || !System.IO.File.Exists(path))
                throw new Exception("Retry did not save the latest explicit override.");
            owner = Activator.CreateInstance(configType, All, null, new object[] { path }, null);
            var saved = Bind(typeof(bool), "CustomerCheckpoint", "DisableMemory", true);
            if ((bool)boolEntry.GetProperty("Value", All).GetValue(saved))
                throw new Exception("Recovered XML saved stale disabled state.");
            Console.WriteLine("PASS actual game Mono: conflict Save failure contained, autosave restored, retry persists current override to real XML.");
        }
        finally
        {
            instanceField.SetValue(null, oldInstance);
            disable.SetValue(null, oldDisable);
            handled.SetValue(null, oldHandled);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            if (System.IO.File.Exists(path + ".bak")) System.IO.File.Delete(path + ".bak");
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root);
        }
    }
}
