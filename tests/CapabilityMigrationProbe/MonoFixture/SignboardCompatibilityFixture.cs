using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

public static partial class OfflineMonoFixture
{
    public static void RunSignboardProbe(string path)
    {
        var mod = Assembly.LoadFrom(path);
        var game = Assembly.Load("Assembly-CSharp");
        CheckSignboardCompatibility(mod, game);
    }

    private static void CheckSignboardCompatibility(Assembly mod, Assembly game)
    {
        var bridge = mod.GetType("ProbablyStolenTools.SignboardCompatibility", true);
        void CheckInactive()
        {
            // CaptureReady, unlike Root, directly references Api fields and triggered Mono's eager initializer.
            if (!(bool)bridge.GetMethod("CaptureReady", All).Invoke(null, new object[] { null })
                || !(bool)mod.GetType("ProbablyStolenTools.CustomerCheckpoint", true)
                    .GetMethod("OptionalCaptureReady", All).Invoke(null, new object[] { null })
                || bridge.GetMethod("Root", All).Invoke(null, null) != null
                || bridge.GetMethod("Capture", All).Invoke(null, new object[] { null }) != null)
                throw new Exception("Inactive signboard mod must leave capture readiness and vanilla roots unchanged.");
            bridge.GetMethod("Tick", All).Invoke(null, null);
        }
        CheckInactive();
        var mods = (IList)game.GetType("ModLoader").GetField("mods", All).GetValue(null);
        var optional = mod.GetType("ProbablyStolenTools.OptionalModCompatibility", true);
        var cachedMods = optional.GetField("_mods", All);
        object oldCache = cachedMods.GetValue(null);
        var fake = Activator.CreateInstance(game.GetType("LoadedMod"));
        var manifest = Activator.CreateInstance(game.GetType("ModManifest"));
        manifest.GetType().GetField("ID", All).SetValue(manifest, "PawnshopBillboards");
        fake.GetType().GetField("Manifest", All).SetValue(fake, manifest);
        fake.GetType().GetField("IsEnabled", All).SetValue(fake, false);
        mods.Add(fake);
        cachedMods.SetValue(null, null);
        try { CheckInactive(); }
        finally { mods.Remove(fake); cachedMods.SetValue(null, oldCache); }
        var api = bridge.GetNestedType("Api", All);
        if ((bool)api.GetField("_probed", All).GetValue(null))
            throw new Exception("Absent/disabled signboards must never resolve optional types.");
        string path = Environment.GetEnvironmentVariable("PSTOOLS_TEST_SIGNBOARDS_DLL");
        if (string.IsNullOrEmpty(path))
        {
            // Probe missing types too: a bad optional API must be unsupported, never a poisoned type initializer.
            for (int i = 0; i < 2; i++)
                if ((bool)api.GetProperty("SupportedBuild", All).GetValue(null))
                    throw new Exception("Absent signboard API cannot be supported.");
            CheckInactive();
            Console.WriteLine("PASS absent/disabled signboards: direct readiness, capture gate, root, capture, tick; no eager probe; repeated missing-API probe is safe.");
            return;
        }
        var external = Assembly.LoadFrom(path);
        var manager = external.GetType("PawnshopBillboards.BillboardManager", true);
        var storage = external.GetType("PawnshopBillboards.HangingStorage", true);
        var placement = external.GetType("PawnshopBillboards.Placement", true);
        object Data(string name) => System.Runtime.Serialization.FormatterServices.GetUninitializedObject(game.GetType(name, true));
        fake.GetType().GetField("IsEnabled", All).SetValue(fake, true);
        var saved = new Dictionary<FieldInfo, object>();
        void Set(Type type, string name, object value)
        {
            var field = type.GetField(name, All);
            saved[field] = field.GetValue(null);
            field.SetValue(null, value);
        }
        mods.Add(fake);
        cachedMods.SetValue(null, null);
        try
        {
            var owner = Data("PlayerStore");
            game.GetType("PlayerStore").GetField("runID", All).SetValue(owner, "signboard-fixture");
            var grid = Data("GameGridInventory");
            Set(storage, "Inventory", grid);
            Set(manager, "owner", owner);
            Set(manager, "run", "signboard-fixture");
            Set(manager, "busy", false);
            Set(manager, "fault", false);
            Set(manager, "initialized", true);
            Set(manager, "dragged", null);
            Set(manager, "heightOffset", -42);
            Set(manager, "expanded", true);
            Set(manager, "snapshotDirty", false);
            Set(manager, "refreshAt", 0f);
            var records = (IDictionary)manager.GetField("records", All).GetValue(null);
            var unavailable = (IDictionary)manager.GetField("unavailable", All).GetValue(null);
            var unresolved = (IDictionary)manager.GetField("unresolved", All).GetValue(null);
            if (records.Count != 0 || unavailable.Count != 0 || unresolved.Count != 0)
                throw new Exception("Isolated signboard fixture unexpectedly has live records.");
            var record = Activator.CreateInstance(placement);
            placement.GetField("uid").SetValue(record, 101);
            placement.GetField("id").SetValue(record, "sign_water");
            placement.GetField("x").SetValue(record, 3);
            placement.GetField("brightness").SetValue(record, 2);
            records.Add(101, record);
            unavailable.Add(202, null);
            var snapshot = bridge.GetMethod("Capture", All).Invoke(null, new[] { owner });
            bool CaptureReady() => (bool)bridge.GetMethod("CaptureReady", All).Invoke(null, new[] { owner });
            if (!CaptureReady()) throw new Exception("Ready signboard fixture must allow capture.");
            if (!ReferenceEquals(bridge.GetMethod("Root", All).Invoke(null, null), grid))
                throw new Exception("Detached signboard inventory was not supplied as a root.");
            for (int pass = 0; pass < 3; pass++)
            {
                records.Clear();
                records.Add(303, Activator.CreateInstance(placement));
                unavailable.Clear();
                unresolved[404] = null;
                placement.GetField("x").SetValue(record, 17);
                placement.GetField("brightness").SetValue(record, 0);
                bridge.GetMethod("Validate", All).Invoke(snapshot, null);
                bridge.GetMethod("RestoreRecords", All).Invoke(snapshot, null);
                if (records.Count != 1 || !ReferenceEquals(records[101], record)
                    || (int)placement.GetField("x").GetValue(record) != 3
                    || (int)placement.GetField("brightness").GetValue(record) != 2
                    || unavailable.Count != 1 || !unavailable.Contains(202) || unresolved.Count != 0)
                    throw new Exception("Signboard membership/placement/brightness roundtrip failed.");
            }
            foreach (string field in new[] { "busy", "fault" })
            {
                manager.GetField(field, All).SetValue(null, true);
                if (CaptureReady()) throw new Exception("Busy/faulted signboards must defer capture without consuming its boundary.");
                bool rejected = false;
                try { bridge.GetMethod("Validate", All).Invoke(snapshot, null); }
                catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejected = true; }
                manager.GetField(field, All).SetValue(null, false);
                if (!rejected) throw new Exception("Signboard preflight failed to reject " + field);
            }
            manager.GetField("initialized", All).SetValue(null, false);
            if (CaptureReady()) throw new Exception("Uninitialized signboards must defer capture.");
            manager.GetField("initialized", All).SetValue(null, true);
            storage.GetField("Inventory", All).SetValue(null, null);
            if (CaptureReady()) throw new Exception("Missing hanging inventory must defer capture.");
            storage.GetField("Inventory", All).SetValue(null, grid);
            if (!CaptureReady()) throw new Exception("Capture must resume after signboard initialization.");
            var supportedField = bridge.GetField("Supported", All);
            object supported = supportedField.GetValue(null);
            try
            {
                supportedField.SetValue(null, Guid.Empty);
                if (!CaptureReady() || bridge.GetMethod("Root", All).Invoke(null, null) != null
                    || bridge.GetMethod("Capture", All).Invoke(null, new[] { owner }) != null)
                    throw new Exception("Unknown signboard build must skip its adapter without blocking ordinary checkpoints.");
            }
            finally { supportedField.SetValue(null, supported); }
            fake.GetType().GetField("IsEnabled", All).SetValue(fake, false);
            bool disabledRejected = false;
            try { bridge.GetMethod("Validate", All).Invoke(snapshot, null); }
            catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { disabledRejected = true; }
            if (!disabledRejected) throw new Exception("Disabled signboards must reject old memory restores.");
            records.Clear(); unavailable.Clear(); unresolved.Clear();
            Console.WriteLine("PASS actual Proper Signboards 1.0.48: detached inventory root, three metadata cycles preserving original record identity, membership/position/brightness/unavailable maps, busy/fault/disabled preflight, unknown build skips adapter without blocking capture. No Unity UI, save serialization or business callbacks invoked.");
        }
        finally
        {
            foreach (var pair in saved) pair.Key.SetValue(null, pair.Value);
            mods.Remove(fake);
            cachedMods.SetValue(null, oldCache);
        }
    }
}
