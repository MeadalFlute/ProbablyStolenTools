using System.Reflection;
using System.Runtime.Serialization;
using MelonLoader;
using Tomlet;
using Tomlet.Models;

// Exercise the installed loader's actual insert/read methods without starting
// its native host or touching UserData. Only the filesystem watcher is bypassed.
sealed class MelonPreferenceFixture
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly Type FileType = typeof(MelonPreferences_Category).Assembly
        .GetType("MelonLoader.Preferences.IO.File", throwOnError: true);
    private readonly object _file = FormatterServices.GetUninitializedObject(FileType);

    internal MelonPreferenceFixture(string source = "")
        => FileType.GetField("document", Hidden).SetValue(_file, new TomlParser().Parse(source));

    internal string Serialize() => ((TomlDocument)FileType.GetField("document", Hidden)
        .GetValue(_file)).SerializedValue;

    internal void Put<T>(string category, string key, T value)
    {
        var entry = new MelonPreferences_Entry<T> { Value = value };
        FileType.GetMethod("InsertIntoDocument", Hidden)
            .Invoke(_file, new object[] { category, key, entry.Save(), false });
    }

    internal T Read<T>(string categoryId, string key, T fallback = default)
    {
        var category = (MelonPreferences_Category)FormatterServices.GetUninitializedObject(typeof(MelonPreferences_Category));
        typeof(MelonPreferences_Category).GetProperty("Identifier").SetValue(category, categoryId);
        var entry = new MelonPreferences_Entry<T> { Value = fallback };
        typeof(MelonPreferences_Entry).GetProperty("Identifier").SetValue(entry, key);
        typeof(MelonPreferences_Entry).GetProperty("Category").SetValue(entry, category);
        FileType.GetMethod("SetupEntryFromRawValue", Hidden).Invoke(_file, new object[] { entry });
        return entry.Value;
    }
}
