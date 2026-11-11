#if MONO
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProbablyStolenTools
{
    internal static class SemanticEntityBlocks
    {
        private static readonly List<(Type Type, SemanticEntityFieldPlan[] Plans)> Manifests = Create();
        internal static string SchemaFailure { get; private set; }

        private static List<(Type, SemanticEntityFieldPlan[])> Create()
        {
            try
            {
                var items = SemanticItemSchemas.Create();
                var inventories = SemanticInventorySchemas.Create().ToDictionary(plan => plan.Type);
                SemanticEntityFieldPlan Rule(Type type) => inventories[type];
                var shape = new SemanticEntityFieldPlan(typeof(GridShapeBuilder),
                    new("data", typeof(byte[]), SemanticFieldRole.Entity),
                    new("outside", typeof(byte), SemanticFieldRole.Entity),
                    new("<minX>k__BackingField", typeof(int), SemanticFieldRole.Entity),
                    new("<minY>k__BackingField", typeof(int), SemanticFieldRole.Entity),
                    new("<flipped>k__BackingField", typeof(bool), SemanticFieldRole.Entity),
                    new("<orientation>k__BackingField", typeof(int), SemanticFieldRole.Entity),
                    new("<width>k__BackingField", typeof(int), SemanticFieldRole.Entity),
                    new("<height>k__BackingField", typeof(int), SemanticFieldRole.Entity));
                var manifests = new List<(Type, SemanticEntityFieldPlan[])>
                {
                    (typeof(GameItemElement), items),
                    (typeof(GameGridInventory), new[] { Rule(typeof(GameGridInventory)), Rule(typeof(GameInventory)),
                        Rule(typeof(GameInventoryFunc)), Rule(typeof(TagSystem)) }),
                    (typeof(GameSlotInventory), new[] { Rule(typeof(GameSlotInventory)), Rule(typeof(GameInventory)),
                        Rule(typeof(GameInventoryFunc)), Rule(typeof(TagSystem)) }),
                    (typeof(PixelWindow), new[] { Rule(typeof(PixelWindow)) }),
                    (typeof(GridShapeBuilder), new[] { shape })
                };
                // Validate unsupported scalar/storage changes before installing any native factory.
                _ = new SemanticFieldBlockFactory(manifests, false, false);
                return manifests;
            }
            catch (Exception e)
            {
                SchemaFailure = e.GetType().Name + ": " + e.Message;
                return new();
            }
        }

        internal static void Enable(ManagedStateAccess access)
        {
            var previous = access.BlockCapture;
            var configure = access.ConfigureIndependentBlocks;
            var factory = new SemanticFieldBlockFactory(Manifests, access.FastReads, access.TypedScalarComparisons);
            access.EntityBlocks = factory;
            access.BlockCapture = (target, slots) => factory.Capture(target, slots) ?? previous?.Invoke(target, slots);
            access.ConfigureIndependentBlocks = independent =>
            {
                configure?.Invoke(independent);
                Enable(independent);
            };
            access.BlockMode += SchemaFailure == null ? "+exact-entity" : "+legacy-entity-schema-mismatch";
        }
    }
}
#endif
