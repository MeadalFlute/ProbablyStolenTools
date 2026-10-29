using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

namespace ProbablyStolenTools
{
    internal sealed class ConfigEntry<T>
    {
        private T _value;
        private readonly ModConfig _owner;
        private readonly string _key;
        internal ConfigEntry(T value, ModConfig owner, string key)
        { _value = value; _owner = owner; _key = key; }
        internal T Value
        {
            get => _value;
            set
            {
                if (EqualityComparer<T>.Default.Equals(_value, value)) return;
                _value = value;
                _owner.Set(_key, Convert.ToString(value, CultureInfo.InvariantCulture));
                SettingChanged?.Invoke(this, EventArgs.Empty);
                if (_owner.SaveOnConfigSet) _owner.Save();
            }
        }
        internal event EventHandler SettingChanged;
    }

    internal sealed class ModConfig
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);
        private readonly string _path;
        internal bool SaveOnConfigSet { get; set; }
        internal ModConfig() : this(Path.Combine(Application.persistentDataPath, "PSTools.settings.xml")) { }
        internal ModConfig(string path)
        {
            _path = path;
            if (File.Exists(_path))
            {
                try
                {
                    foreach (var entry in XDocument.Load(_path).Root.Elements("Entry"))
                        _values[(string)entry.Attribute("key")] = (string)entry.Attribute("value");
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[PSTools] Configuration unreadable; preserving original: " + e.Message);
                    File.Copy(_path, _path + ".unreadable-" + DateTime.Now.ToString("yyyyMMddHHmmss"), false);
                }
            }
        }
        internal void Set(string key, string value) => _values[key] = value;
        internal ConfigEntry<T> Bind<T>(string section, string key, T defaultValue, string description)
        {
            string fullKey = section + "." + key;
            T value = defaultValue;
            if (_values.TryGetValue(fullKey, out string raw))
            {
                try
                {
                    value = (T)(typeof(T).IsEnum ? Enum.Parse(typeof(T), raw, true)
                        : Convert.ChangeType(raw, typeof(T), CultureInfo.InvariantCulture));
                }
                catch { value = defaultValue; }
            }
            _values[fullKey] = Convert.ToString(value, CultureInfo.InvariantCulture);
            return new ConfigEntry<T>(value, this, fullKey);
        }
        internal void Save()
        {
            var doc = new XDocument(new XElement("ProbablyStolenTools",
                _values.OrderBy(pair => pair.Key).Select(pair => new XElement("Entry",
                    new XAttribute("key", pair.Key), new XAttribute("value", pair.Value)))));
            string temp = _path + ".tmp";
            doc.Save(temp);
            if (File.Exists(_path)) File.Replace(temp, _path, _path + ".bak");
            else File.Move(temp, _path);
        }
    }

    internal sealed class ModLogger
    {
        internal void LogInfo(string message) => Debug.Log("[PSTools] " + message);
        internal void LogWarning(string message) => Debug.LogWarning("[PSTools] " + message);
        internal void LogError(string message) => Debug.LogError("[PSTools] " + message);
    }
}
