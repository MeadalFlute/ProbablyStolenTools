using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace ProbablyStolenTools
{
    // IL2CPP proxies expose private fields; Mono requires explicit field access.
    internal static class GameFields
    {
#if MONO
        private static readonly FieldInfo ItemValidateLock = typeof(GameItemElement).GetField("validateLock",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new MissingFieldException("GameItemElement", "validateLock");
        private static readonly FieldInfo ItemAppliedGridSize = typeof(GameItemElement).GetField("appliedGridSize",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
            ?? throw new MissingFieldException("GameItemElement", "appliedGridSize");
#endif

        internal static void ResetItemLayout(GameItemElement item)
        {
#if MONO
            // Fixed game fields avoid per-item ModReflect caller-stack authorization.
            ItemValidateLock.SetValue(item, false);
            ItemAppliedGridSize.SetValue(item, -1);
#else
            Set(item, "validateLock", false);
#endif
        }

#if STATIC_SNAPSHOT_LAYOUTS
        internal static T Get<T>(object owner, string name) => ModReflect.Get<T>(owner, name);
        internal static void Set<T>(object owner, string name, T value) => ModReflect.Set(owner, name, value);
#else
        private static readonly Dictionary<(Type, string), MemberInfo> Fields = new();
        private static MemberInfo Field(object owner, string name)
        {
            var key = (owner.GetType(), name);
            if (!Fields.TryGetValue(key, out var field))
            {
                field = (MemberInfo)AccessTools.Field(key.Item1, name) ?? AccessTools.Property(key.Item1, name)
                    ?? throw new MissingFieldException(key.Item1.FullName, name);
                Fields.Add(key, field);
            }
            return field;
        }
        internal static T Get<T>(object owner, string name)
        {
            var member = Field(owner, name);
            return (T)(member is FieldInfo field ? field.GetValue(owner) : ((PropertyInfo)member).GetValue(owner));
        }
        internal static void Set<T>(object owner, string name, T value)
        {
            var member = Field(owner, name);
            if (member is FieldInfo field) field.SetValue(owner, value);
            else ((PropertyInfo)member).SetValue(owner, value);
        }
#endif
        internal static bool PhoneBusy(PhoneUIManager phone) => phone != null
            && (Get<bool>(phone, "isCalling") || Get<bool>(phone, "isTalking"));
    }
}
