using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ProbablyStolenTools
{
    internal enum SemanticFieldRole { Entity, Runtime, Visual }
    internal readonly record struct SemanticFieldRule(string Name, Type Type, SemanticFieldRole Role);

    // Exhaustive metadata partition, not permission to omit runtime state or write entities.
    internal sealed class SemanticEntityFieldPlan
    {
        internal readonly Type Type;
        internal readonly FieldInfo[] Fields;
        internal readonly FieldInfo[] EntityFields;
        private readonly Dictionary<string, SemanticFieldRole> _roles;
        internal int RuntimeFields { get; }
        internal int VisualFields { get; }

        internal SemanticEntityFieldPlan(Type type, params SemanticFieldRule[] rules)
        {
            Type = type;
            if (rules.Any(rule => !Enum.IsDefined(typeof(SemanticFieldRole), rule.Role)))
                throw new InvalidOperationException("Unknown entity field partition.");
            var fields = SemanticEntitySnapshot.Schema(type, true, rules.Select(rule => (rule.Name, rule.Type)).ToArray());
            Fields = fields;
            _roles = new Dictionary<string, SemanticFieldRole>(StringComparer.Ordinal);
            var entities = new List<FieldInfo>();
            for (int i = 0; i < fields.Length; i++)
            {
                _roles.Add(type.FullName + "." + fields[i].Name, rules[i].Role);
                if (rules[i].Role == SemanticFieldRole.Entity) entities.Add(fields[i]);
            }
            EntityFields = entities.ToArray();
            RuntimeFields = rules.Count(rule => rule.Role == SemanticFieldRole.Runtime);
            VisualFields = rules.Count(rule => rule.Role == SemanticFieldRole.Visual);
        }

        internal SemanticFieldRole RoleOf(string slot)
        {
            if (!_roles.TryGetValue(slot, out var role))
                throw new InvalidOperationException("Unclassified entity owner slot: " + slot);
            return role;
        }

        internal string[] RequiredSlots => _roles.Where(pair => pair.Value != SemanticFieldRole.Visual)
            .Select(pair => pair.Key).ToArray();
    }
}
