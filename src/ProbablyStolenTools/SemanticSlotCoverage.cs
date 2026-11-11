using System;
using System.Collections.Generic;

namespace ProbablyStolenTools
{
    // Schema is shared by owners; only the bounded bit vector is per owner.
    internal sealed class SemanticSlotSchema
    {
        private readonly Dictionary<string, (bool Reference, int Index, SemanticFieldRole Role)> _fields = new(StringComparer.Ordinal);
        internal int RequiredCount { get; }
        internal int FieldCount => _fields.Count;
        internal int VisualCount => FieldCount - RequiredCount;

        internal SemanticSlotSchema(IEnumerable<(string Name, bool Reference, SemanticFieldRole Role)> fields)
        {
            int required = 0;
            foreach (var field in fields)
            {
                if (!Enum.IsDefined(typeof(SemanticFieldRole), field.Role))
                    throw new InvalidOperationException("Unknown entity field partition.");
                int index = field.Role == SemanticFieldRole.Visual ? -1 : required++;
                if (_fields.ContainsKey(field.Name))
                    throw new InvalidOperationException("Duplicate entity field: " + field.Name);
                _fields.Add(field.Name, (field.Reference, index, field.Role));
            }
            RequiredCount = required;
        }

        internal sealed class Coverage
        {
            private readonly SemanticSlotSchema _schema;
            private readonly ulong[] _seen;
            private int _count;
            internal bool Complete => _count == _schema.RequiredCount;
            internal Coverage(SemanticSlotSchema schema)
            {
                _schema = schema;
                _seen = new ulong[(schema.RequiredCount + 63L) / 64];
            }
            internal SemanticFieldRole Observe(string name, bool reference)
            {
                if (!_schema._fields.TryGetValue(name, out var field))
                    throw new InvalidOperationException("Unclassified entity owner slot: " + name);
                if (field.Role == SemanticFieldRole.Visual || field.Reference != reference)
                    throw new InvalidOperationException("Entity projection slot kind/exclusion changed: " + name);
                int word = field.Index / 64;
                ulong bit = 1UL << (field.Index % 64);
                if ((_seen[word] & bit) != 0)
                    throw new InvalidOperationException("Duplicate entity projection slot: " + name);
                _seen[word] |= bit;
                _count++;
                return field.Role;
            }
        }
    }
}
