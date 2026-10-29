using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MelonLoader;

namespace ProbablyStolenTools
{
    internal sealed class ConfigEntry<T>
    {
        private readonly MelonPreferences_Entry<T> _entry;
        private readonly ModConfig _owner;

        internal ConfigEntry(MelonPreferences_Entry<T> entry, ModConfig owner)
        {
            _entry = entry;
            _owner = owner;
            _entry.OnEntryValueChanged.Subscribe((_, _) => SettingChanged?.Invoke(this, EventArgs.Empty));
        }

        internal T Value
        {
            get => _entry.Value;
            set
            {
                _entry.Value = value;
                if (_owner.SaveOnConfigSet) _owner.Save();
            }
        }

        internal event EventHandler SettingChanged;
    }

    internal sealed class ModConfig
    {
        private readonly Dictionary<string, MelonPreferences_Category> _categories = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _legacyValues;

        internal bool SaveOnConfigSet { get; set; }

        internal ModConfig()
        {
            _legacyValues = ReadLegacyConfig();
        }

        // MelonLoader 0.7.3 passes category IDs through Tomlet's dotted-path
        // lookup; dotted IDs can silently save empty tables instead of entries.
        internal static string CategoryIdentifier(string section) => "ProbablyStolenTools_" + section;

        internal ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            if (!_categories.TryGetValue(section, out var category))
            {
                category = MelonPreferences.CreateCategory(CategoryIdentifier(section), section);
                _categories.Add(section, category);
            }

            if (!category.HasEntry(key) && TryGetLegacy(section, key, out T imported))
                defaultValue = imported;

            var entry = category.CreateEntry<T>(key, defaultValue, key, description, false, false, null);
            return new ConfigEntry<T>(entry, this);
        }

        internal void Save() => MelonPreferences.Save();

        private bool TryGetLegacy<T>(string section, string key, out T value)
        {
            value = default;
            if (!_legacyValues.TryGetValue($"{section}.{key}", out string raw)) return false;

            try
            {
                Type type = typeof(T);
                object parsed = type.IsEnum
                    ? Enum.Parse(type, raw, ignoreCase: true)
                    : Convert.ChangeType(raw, type, CultureInfo.InvariantCulture);
                value = (T)parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static Dictionary<string, string> ReadLegacyConfig()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string path = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "BepInEx", "config", "dsh.probablystolen.tools.cfg");
            if (!File.Exists(path)) return values;

            string section = "";
            foreach (string line in File.ReadLines(path))
            {
                string text = line.Trim();
                if (text.Length == 0 || text.StartsWith("#", StringComparison.Ordinal)
                    || text.StartsWith(";", StringComparison.Ordinal)) continue;
                if (text.StartsWith("[", StringComparison.Ordinal) && text.EndsWith("]", StringComparison.Ordinal))
                {
                    section = text.Substring(1, text.Length - 2).Trim();
                    continue;
                }

                int separator = text.IndexOf('=');
                if (separator <= 0 || section.Length == 0) continue;
                string key = text.Substring(0, separator).Trim();
                values[$"{section}.{key}"] = text.Substring(separator + 1).Trim();
            }

            return values;
        }
    }

    internal sealed class ModLogger
    {
        internal void LogInfo(string message) => MelonLogger.Msg(message);
        internal void LogWarning(string message) => MelonLogger.Warning(message);
        internal void LogError(string message) => MelonLogger.Error(message);
    }
}
