using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;

namespace ProbablyStolenTools
{
    // Field-level IL2CPP access: no object headers, GC descriptors, or code pointers are copied.
    internal sealed class NativeStateAccess : IStateAccess
    {
        private readonly Dictionary<IntPtr, nint> _handles = new();
        private readonly Dictionary<IntPtr, IReadOnlyList<StateSlot>> _layouts = new();
        private readonly Dictionary<IntPtr, HashSet<string>> _rootFields = new();
        private readonly IntPtr _unityObject = IL2CPP.GetIl2CppClass("UnityEngine.CoreModule.dll", "UnityEngine", "Object");
        private readonly IntPtr _delegate = IL2CPP.GetIl2CppClass("mscorlib.dll", "System", "Delegate");
        private const int Header = 16;
        private const int ArrayHeader = 32;

        internal void AddRoot(IntPtr obj, params string[] fields)
        {
            if (obj == IntPtr.Zero) throw new InvalidOperationException("Required snapshot root missing.");
            _rootFields[obj] = fields.Length == 0 ? null : new HashSet<string>(fields);
        }

        internal static string ClassName(IntPtr klass)
        {
            var names = new List<string>();
            string ns = "";
            for (var c = klass; c != IntPtr.Zero; c = IL2CPP.il2cpp_class_get_declaring_type(c))
            {
                if (names.Count >= 64) throw new InvalidOperationException("Invalid declaring-type chain.");
                names.Add(IL2CPP.il2cpp_class_get_name_(c));
                ns = IL2CPP.il2cpp_class_get_namespace_(c);
            }
            names.Reverse();
            // IL2CPP nested classes can have an empty namespace; qualify through their outer class.
            return (string.IsNullOrEmpty(ns) ? "" : ns + ".") + string.Join("+", names);
        }

        internal static string FieldName(IntPtr field) => IL2CPP.il2cpp_field_get_name_(field);

        public void Retain(IntPtr id)
        {
            if (_handles.ContainsKey(id)) return;
            var handle = IL2CPP.il2cpp_gchandle_new(id, true);
            if (handle == 0) throw new InvalidOperationException("Cannot retain IL2CPP snapshot object.");
            _handles.Add(id, handle);
        }

        public IReadOnlyList<StateSlot> Describe(IntPtr id)
        {
            var klass = IL2CPP.il2cpp_object_get_class(id);
            bool root = _rootFields.TryGetValue(id, out var selected);
            if (!root && !Traversable(klass)) return Array.Empty<StateSlot>();
            if (IL2CPP.il2cpp_class_get_rank(klass) != 0) return ArraySlots(id, klass);
            if (!root && _layouts.TryGetValue(klass, out var cached)) return cached;

            var slots = new List<StateSlot>();
            bool isDelegate = IL2CPP.il2cpp_class_is_assignable_from(_delegate, klass);
            for (var c = klass; c != IntPtr.Zero; c = IL2CPP.il2cpp_class_get_parent(c))
            {
                if (c == _unityObject) break;
                string ns = IL2CPP.il2cpp_class_get_namespace_(c);
                if (ns == "UnityEngine") break;
                var iter = IntPtr.Zero;
                IntPtr f;
                while ((f = IL2CPP.il2cpp_class_get_fields(c, ref iter)) != IntPtr.Zero)
                {
                    if ((IL2CPP.il2cpp_field_get_flags(f) & 0x50) != 0) continue; // static/literal
                    string name = FieldName(f);
                    if (root && selected != null && !selected.Contains(name)) continue;
                    // Delegates retain their original executable identity; only follow captured targets.
                    if (isDelegate && name != "m_target" && name != "_target" && name != "delegates") continue;
                    var type = IL2CPP.il2cpp_field_get_type(f);
                    if (VisualField(IL2CPP.il2cpp_type_get_name_(type))) continue;
                    Flatten(type, checked((int)IL2CPP.il2cpp_field_get_offset(f)), slots,
                        ClassName(c) + "." + name, 0);
                }
            }
            if (root && selected != null)
                foreach (var name in selected)
                    if (!slots.Exists(s => s.Name.EndsWith("." + name, StringComparison.Ordinal)
                        || s.Name.Contains("." + name + ".", StringComparison.Ordinal)))
                        throw new InvalidOperationException("Required snapshot field missing: " + name);
            ValidateSlots(slots, checked((int)IL2CPP.il2cpp_object_get_size(id)));
            if (!root) _layouts[klass] = slots;
            return slots;
        }

        private bool Traversable(IntPtr klass)
        {
            if (IL2CPP.il2cpp_class_is_assignable_from(_unityObject, klass)) return false;
            string name = ClassName(klass);
            if (name == "System.String" || name == "System.Object" || name.Contains("Comparer")) return false;
            if (IL2CPP.il2cpp_class_get_rank(klass) != 0) return true;
            if (IL2CPP.il2cpp_class_is_assignable_from(_delegate, klass)) return true;
            if (name.StartsWith("System.Collections.Generic.", StringComparison.Ordinal)
                || name.StartsWith("System.Tuple`", StringComparison.Ordinal)
                || name == "System.Random") return true;
            string image = IL2CPP.il2cpp_image_get_name_(IL2CPP.il2cpp_class_get_image(klass));
            if (image == "Assembly-CSharp.dll") return true;
            if (IL2CPP.il2cpp_class_is_valuetype(klass)) return true;
            throw new InvalidOperationException("Uncovered mutable type: " + name);
        }

        private static bool VisualField(string type)
            => type.Contains("TreeNodeRender") || type.Contains("UnityEngine.")
                && !type.StartsWith("UnityEngine.Vector", StringComparison.Ordinal)
                && !type.StartsWith("UnityEngine.Color", StringComparison.Ordinal)
                && !type.StartsWith("UnityEngine.Rect", StringComparison.Ordinal)
                && !type.StartsWith("UnityEngine.Quaternion", StringComparison.Ordinal);

        private IReadOnlyList<StateSlot> ArraySlots(IntPtr id, IntPtr klass)
        {
            int length = checked((int)IL2CPP.il2cpp_array_length(id));
            int stride = IL2CPP.il2cpp_array_element_size(klass);
            if (length > 262144 || stride <= 0 || (long)length * stride > 32 * 1024 * 1024)
                throw new InvalidOperationException("Snapshot array exceeds validated limits.");
            var element = IL2CPP.il2cpp_class_get_element_class(klass);
            var type = IL2CPP.il2cpp_class_get_type(element);
            var slots = new List<StateSlot>();
            for (int i = 0; i < length; i++)
                Flatten(type, checked(ArrayHeader + i * stride), slots, ClassName(klass) + "[" + i + "]", 0);
            ValidateSlots(slots, checked(ArrayHeader + length * stride));
            return slots;
        }

        private static void ValidateSlots(IEnumerable<StateSlot> slots, int objectSize)
        {
            foreach (var slot in slots)
                if (slot.Offset < Header || slot.Size <= 0 || (long)slot.Offset + slot.Size > objectSize)
                    throw new InvalidOperationException("Field outside native object bounds: " + slot.Name);
        }

        private void Flatten(IntPtr type, int offset, List<StateSlot> slots, string name, int depth)
        {
            if (depth > 16) throw new InvalidOperationException("Recursive value type: " + name);
            int code = IL2CPP.il2cpp_type_get_type(type);
            int size = code switch
            {
                0x02 or 0x04 or 0x05 => 1,
                0x03 or 0x06 or 0x07 => 2,
                0x08 or 0x09 or 0x0c => 4,
                0x0a or 0x0b or 0x0d or 0x18 or 0x19 => 8,
                _ => 0
            };
            if (size != 0) { slots.Add(new StateSlot(offset, size, false, name)); return; }
            if (code == 0x0f || code == 0x10 || code == 0x1b)
                throw new InvalidOperationException("Pointer/byref storage not supported: " + name);
            var klass = IL2CPP.il2cpp_class_from_type(type);
            if (klass == IntPtr.Zero) throw new InvalidOperationException("Unknown native field type: " + name);
            if (!IL2CPP.il2cpp_class_is_valuetype(klass))
            {
                slots.Add(new StateSlot(offset, IntPtr.Size, true, name));
                return;
            }
            if (!IL2CPP.il2cpp_class_has_references(klass))
            {
                uint alignment = 0;
                size = IL2CPP.il2cpp_class_value_size(klass, ref alignment);
                if (size <= 0 || size > 65536) throw new InvalidOperationException("Invalid value layout: " + name);
                slots.Add(new StateSlot(offset, size, false, name));
                return;
            }
            var iter = IntPtr.Zero;
            IntPtr f;
            while ((f = IL2CPP.il2cpp_class_get_fields(klass, ref iter)) != IntPtr.Zero)
            {
                if ((IL2CPP.il2cpp_field_get_flags(f) & 0x50) != 0) continue;
                // IL2CPP struct field offsets include the boxed object header.
                int relative = checked((int)IL2CPP.il2cpp_field_get_offset(f)) - Header;
                if (relative < 0) throw new InvalidOperationException("Invalid struct field offset: " + name);
                Flatten(IL2CPP.il2cpp_field_get_type(f), checked(offset + relative), slots,
                    name + "." + FieldName(f), depth + 1);
            }
        }

        public byte[] ReadValue(IntPtr id, StateSlot slot)
        {
            var data = new byte[slot.Size];
            Marshal.Copy(IntPtr.Add(id, slot.Offset), data, 0, data.Length);
            return data;
        }

        public IntPtr ReadReference(IntPtr id, StateSlot slot) => Marshal.ReadIntPtr(id, slot.Offset);
        public void WriteValue(IntPtr id, StateSlot slot, byte[] value)
            => Marshal.Copy(value, 0, IntPtr.Add(id, slot.Offset), value.Length);
        public void WriteReference(IntPtr id, StateSlot slot, IntPtr value)
            => IL2CPP.il2cpp_gc_wbarrier_set_field(id, IntPtr.Add(id, slot.Offset), value);

        public void Validate()
        {
            if (IntPtr.Size != 8) throw new PlatformNotSupportedException("Only this game's x64 IL2CPP layout is supported.");
            foreach (var pair in _handles)
                if (IL2CPP.il2cpp_gchandle_get_target(pair.Value) != pair.Key)
                    throw new InvalidOperationException("A retained snapshot object moved or expired.");
        }

        public void Dispose()
        {
            foreach (var h in _handles.Values) IL2CPP.il2cpp_gchandle_free(h);
            _handles.Clear();
        }
    }
}
