using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SnapshotFixtures
{
    public class ModData
    {
        public int Number;
        public bool Done;
        public ModData Link;
        public List<ModData> Items = new List<ModData>();
        public Dictionary<string, ModData> Map = new Dictionary<string, ModData>();
        public Action Callback;
        public object Payload;
        public RefValue Pair;
        public RefValue? Optional;
        public int? Count;
        public readonly ModData ReadonlyChild;
        public static ModData StaticCurrent;
        public IntPtr Handle;
        public IntPtr? OptionalHandle;
        public Task Pending;
        public Stream Stream;
        public IEnumerator Iterator;
        public Action ReadOnlyCallback => throw new Exception("Do not invoke properties.");
        public ModData() { }
        public ModData(ModData child) { ReadonlyChild = child; }
        public override int GetHashCode() => throw new Exception("Do not call mod hashing.");
        public override bool Equals(object obj) => throw new Exception("Do not call mod equality.");
    }

    public struct RefValue
    {
        public int Number;
        public ModData Link;
    }

    public unsafe class PointerData
    {
        public int Number;
        public int* Pointer;
    }

    public sealed class BusinessComparerData
    {
        public int Number;
    }

    public sealed class NoConstruction
    {
        static NoConstruction() { ConstructionEvidence.Initializations++; }
        private NoConstruction() { throw new Exception("Do not construct mod data."); }
        public static NeverInitialize UnusedStatic;
        public int Number;
        public int Property => throw new Exception("Do not invoke mod getters.");
    }

    public static class ConstructionEvidence
    {
        public static int Initializations;
    }

    public sealed class NeverInitialize
    {
        static NeverInitialize() { throw new Exception("Do not inspect unrelated mod statics."); }
    }

    public static class ClosureFactory
    {
        public static Action Once(ModData data)
        {
            bool done = false;
            return () =>
            {
                if (done) return;
                done = true;
                data.Number++;
            };
        }

        public static IEnumerable<int> Coroutine(ModData data)
        {
            data.Number++;
            yield return data.Number;
        }
    }
}
