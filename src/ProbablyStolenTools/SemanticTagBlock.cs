#if MONO
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ProbablyStolenTools
{
    // Exact native TagState only: no collection reconstruction, setters, callbacks or new identities.
    internal sealed class SemanticTagBlock : IStateBlock, IStateOwnerRepairBlock
    {
        private static readonly string[] Names = { "valueEnabled", "internalValueString", "valueInt",
            "valueFloat", "valueLong", "valueDouble", "valueBool",
            "<identifier>k__BackingField", "<identifierName>k__BackingField" };
        private static readonly Type[] Types = { typeof(bool), typeof(string), typeof(int), typeof(float),
            typeof(long), typeof(double), typeof(bool), typeof(string), typeof(string) };
        private static readonly FieldInfo[] Fields = Schema();
        private readonly TagState _target;
        private readonly SemanticTagValue _value;
        private readonly IntPtr _text, _identifier, _displayName;

        private static FieldInfo[] Schema()
        {
            var fields = typeof(TagState).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fields.Length != Names.Length || typeof(TagState).BaseType != typeof(object)) return null;
            var ordered = new FieldInfo[Names.Length];
            for (int i = 0; i < Names.Length; i++)
            {
                ordered[i] = Array.Find(fields, field => field.Name == Names[i] && field.FieldType == Types[i]);
                if (ordered[i] == null || ordered[i].IsInitOnly) return null;
            }
            return ordered;
        }

        internal static void Enable(ManagedStateAccess access)
        {
            access.ConfigureIndependentBlocks = Enable;
            access.BlockMode = Fields == null ? "legacy-tag-schema-mismatch" : "hybrid-exact-tag";
            if (Fields != null) access.BlockCapture = new Factory().Capture;
        }

        internal static void EnableCensus(ManagedStateAccess access)
        {
            if (Fields != null) access.CensusValuesProbe = new Factory().Probe;
        }

        private sealed class Factory
        {
            private IReadOnlyList<StateSlot> _checked;
            private bool _valid;
            internal IStateBlock Capture(object target, IReadOnlyList<StateSlot> slots)
                => Valid(target, slots) ? new SemanticTagBlock((TagState)target) : null;

            internal bool Probe(object target, IReadOnlyList<StateSlot> slots)
            {
                if (!Valid(target, slots)) return false;
                // Consume all nine exact native fields once; no snapshot values/blocks survive the call.
                var tag = (TagState)target;
                GC.KeepAlive(new SemanticTagValue(tag.valueEnabled, tag.valueString, tag.valueInt,
                    BitConverter.SingleToInt32Bits(tag.valueFloat), tag.valueLong,
                    BitConverter.DoubleToInt64Bits(tag.valueDouble), tag.valueBool, tag.identifier, tag.identifierName));
                return true;
            }

            private bool Valid(object target, IReadOnlyList<StateSlot> slots)
            {
                if (target.GetType() != typeof(TagState)) return false;
                // Each access owns its schema. Validate it once, not once per tag.
                if (!ReferenceEquals(_checked, slots))
                {
                    _checked = slots;
                    _valid = slots.Count == Names.Length;
                    var seen = new bool[Names.Length];
                    foreach (var slot in slots)
                    {
                        int index = -1;
                        for (int i = 0; i < Names.Length; i++)
                            if (slot.Name == "TagState." + Names[i]) { index = i; break; }
                        if (index < 0 || seen[index] || slot.IsReference != (Types[index] == typeof(string)))
                        { _valid = false; break; }
                        seen[index] = true;
                    }
                }
                return _valid;
            }
        }

        private SemanticTagBlock(TagState target)
        {
            _target = target;
            _value = SemanticTagShadow.Read(target);
            _text = ManagedObjects.Id(_value.Text);
            _identifier = ManagedObjects.Id(_value.Identifier);
            _displayName = ManagedObjects.Id(_value.DisplayName);
        }

        public IntPtr ReferenceAt(StateSlot slot) => slot.Name switch
        {
            "TagState.internalValueString" => _text,
            "TagState.<identifier>k__BackingField" => _identifier,
            "TagState.<identifierName>k__BackingField" => _displayName,
            _ => throw new InvalidOperationException("Unknown compact tag reference: " + slot.Name)
        };

        public void RestoreValues()
        {
            _target.valueEnabled = _value.Enabled;
            _target.valueInt = _value.Integer;
            _target.valueFloat = BitConverter.Int32BitsToSingle(_value.SingleBits);
            _target.valueLong = _value.Long;
            _target.valueDouble = BitConverter.Int64BitsToDouble(_value.DoubleBits);
            _target.valueBool = _value.Boolean;
        }

        public void RestoreReferences()
        {
            // valueString's public setter normalizes null. Restore real backing fields with GC barriers.
            Fields[1].SetValue(_target, _value.Text);
            Fields[7].SetValue(_target, _value.Identifier);
            Fields[8].SetValue(_target, _value.DisplayName);
        }

        public int CollectDifferences(List<string> result, int max)
        {
            // Evaluate every field even after the diagnostic cap.
            int changed = 0;
            changed += Add(_target.valueEnabled == _value.Enabled, 0, result, max);
            changed += Add(ReferenceEquals(_target.valueString, _value.Text), 1, result, max);
            changed += Add(_target.valueInt == _value.Integer, 2, result, max);
            changed += Add(BitConverter.SingleToInt32Bits(_target.valueFloat) == _value.SingleBits, 3, result, max);
            changed += Add(_target.valueLong == _value.Long, 4, result, max);
            changed += Add(BitConverter.DoubleToInt64Bits(_target.valueDouble) == _value.DoubleBits, 5, result, max);
            changed += Add(_target.valueBool == _value.Boolean, 6, result, max);
            changed += Add(ReferenceEquals(_target.identifier, _value.Identifier), 7, result, max);
            changed += Add(ReferenceEquals(_target.identifierName, _value.DisplayName), 8, result, max);
            return changed;
        }

        private static int Add(bool same, int index, List<string> result, int max)
        {
            if (!same && result.Count < max) result.Add("TagState." + Names[index]);
            return same ? 0 : 1;
        }
    }
}
#endif
