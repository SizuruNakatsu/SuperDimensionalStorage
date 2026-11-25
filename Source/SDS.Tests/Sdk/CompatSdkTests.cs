using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    public class CompatSdkTests
    {
        private sealed class Probe
        {
            public int value;
            public bool flag;

            public int Value
            {
                get { return value; }
            }

            public bool Flag
            {
                get { return flag; }
            }

            public int Echo(int input)
            {
                return input;
            }
        }

        private static class PatchTarget
        {
            public static int Counter;

            public static int Value()
            {
                return Counter;
            }

            public static void Prefix()
            {
                Counter += 10;
            }
        }

        [Fact]
        public void Tools_ResolvesReflectionMembers()
        {
            Assert.NotNull(SDS_CompatTools.Method(typeof(Probe), "Echo", typeof(int)));
            Assert.NotNull(SDS_CompatTools.Field(typeof(Probe), "value"));
            Assert.NotNull(SDS_CompatTools.Property(typeof(Probe), "Value"));
            Assert.NotNull(SDS_CompatTools.PropertyGetter(typeof(Probe), "Flag"));
            Assert.Null(SDS_CompatTools.Method(null, "Echo"));

            Probe probe = new Probe { flag = true };
            Assert.True(SDS_CompatTools.GetBool(SDS_CompatTools.Property(typeof(Probe), "Flag"), probe));
        }

        [Fact]
        public void Tools_TryPatch_AppliesPrefixAndReports()
        {
            Harmony harmony = new Harmony("sds.test.sdk.patch");
            SDS_CompatPatchReport report = new SDS_CompatPatchReport("test");
            PatchTarget.Counter = 0;
            try
            {
                MethodInfo target = SDS_CompatTools.Method(typeof(PatchTarget), "Value");
                Assert.True(SDS_CompatTools.TryPatch(harmony, report, typeof(PatchTarget), "Value", target,
                    "Prefix", null, "missing target"));
                Assert.Equal(1, report.AppliedCount);
                Assert.False(report.HasFailures);
                Assert.Equal(10, PatchTarget.Value());
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        [Fact]
        public void Tools_TryPatch_MissingTarget_ReportsFailure()
        {
            Harmony harmony = new Harmony("sds.test.sdk.missing");
            SDS_CompatPatchReport report = new SDS_CompatPatchReport("test");
            try
            {
                Assert.False(SDS_CompatTools.TryPatch(harmony, report, typeof(PatchTarget), "Value", null,
                    "Prefix", null, "missing target"));
                Assert.Equal(0, report.AppliedCount);
                Assert.True(report.HasFailures);
            }
            finally
            {
                harmony.UnpatchAll(harmony.Id);
            }
        }

        [Fact]
        public void Endpoint_HasAnyEndpoint_ForwardsHostFacts()
        {
            RulesTestEnv env = ApiTestKit.Create();
            Map map = ApiTestKit.NewMap();

            Assert.True(SDS_CompatEndpoint.HasAnyEndpoint(map));

            env.Ports.Candidates.Clear();
            Assert.False(SDS_CompatEndpoint.HasAnyEndpoint(map));

            env.Ports.LocalUsable = false;
            Assert.False(SDS_CompatEndpoint.HasAnyEndpoint(map));
            Assert.False(SDS_CompatEndpoint.HasAnyEndpoint(null));
        }

        [Fact]
        public void Endpoint_HasUsableBuildingEndpointWithin_RespectsRadius()
        {
            RulesTestEnv env = ApiTestKit.Create();
            Map map = ApiTestKit.NewMap();
            env.Port.Distance = 25f;

            Assert.False(SDS_CompatEndpoint.HasUsableBuildingEndpointWithin(map, IntVec3.Zero, 1f));
            Assert.False(SDS_CompatEndpoint.HasUsableBuildingEndpointWithin(map, IntVec3.Invalid, 10f));
            Assert.False(SDS_CompatEndpoint.HasUsableBuildingEndpointWithin(null, IntVec3.Zero, 10f));
        }

        [Fact]
        public void Stock_CountsAndWithdrawsVirtualByDef()
        {
            RulesTestEnv env = ApiTestKit.Create();
            ThingDef def = CoreTestKit.Def("SdkVirtual");
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 5), env.Port, out _));

            Assert.Equal(5L, SDS_CompatStock.CountAvailable(def));
            Assert.True(SDS_CompatStock.TryWithdrawVirtualByDef(def, 3, null, out List<Thing> made));
            Assert.Single(made);
            Assert.Equal(3, made[0].stackCount);
            Assert.Equal(2L, env.Ledger.GetCount(key));

            Assert.False(SDS_CompatStock.TryWithdrawVirtualByDef(def, 10, null, out _));
            Assert.Equal(2L, env.Ledger.GetCount(key));
        }

        [Fact]
        public void Stock_WithdrawsAndReturnsPreservedByDef()
        {
            RulesTestEnv env = ApiTestKit.Create();
            ThingDef def = CoreTestKit.Def("SdkPreserved");
            SDS_PreserveHooks.Register(thing => thing.def != null && thing.def.defName == "SdkPreserved");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 1), env.Port, out _));
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 1), env.Port, out _));
            SDS_StockKey key = new SDS_StockKey(def, null, -1, true);

            Assert.Equal(2L, SDS_API.GetPreservedAvailableCount(def));
            // CountAvailable 已是「虚拟 + 保真」合计；调用方不得再叠加保真量（防重复计数）。
            Assert.Equal(2L, SDS_CompatStock.CountAvailable(def));
            Assert.True(SDS_CompatStock.TryWithdrawPreservedByDef(def, 1, null, out List<Thing> made));
            Assert.Single(made);
            Assert.Equal(1L, env.Ledger.GetPreservedCount(key));

            SDS_CompatStock.ReturnPreserved(made, null);
            Assert.Equal(2L, env.Ledger.GetPreservedCount(key));

            Assert.False(SDS_CompatStock.TryWithdrawPreservedByDef(def, 5, null, out _));
            Assert.Equal(2L, env.Ledger.GetPreservedCount(key));
        }

        [Fact]
        public void BillSupply_EarlyOuts()
        {
            RulesTestEnv env = ApiTestKit.Create();
            Map map = ApiTestKit.NewMap();
            Assert.False(SDS_CompatBillSupply.TrySupplyPreservedBill(null, map, IntVec3.Zero, null, out _));

            RecipeDef recipe = new RecipeDef { defName = "SdkRecipe" };
            recipe.ingredients = new List<IngredientCount> { new IngredientCount() };
            Bill bill = new Bill_Production { recipe = recipe };
            Assert.False(SDS_CompatBillSupply.TrySupplyPreservedBill(bill, map, IntVec3.Zero, null, out _));

            RulesTestEnv off = RulesTestEnv.Create(capacity: 30);
            Assert.False(SDS_CompatBillSupply.TrySupplyPreservedBill(bill, map, IntVec3.Zero, null, out _));
        }

        [Fact]
        public void CompatGates_TransportLoadInjection_DefaultAllowAndFailClosed()
        {
            SDS_CompatGates.Reset();
            Assert.True(SDS_CompatGates.AllowsTransportLoadInjection(null));

            Assert.True(SDS_CompatGates.RegisterTransportLoadInjection(_ => false));
            Assert.False(SDS_CompatGates.AllowsTransportLoadInjection(null));
            // 先登记者为准，重复登记不覆盖。
            Assert.False(SDS_CompatGates.RegisterTransportLoadInjection(_ => true));
            Assert.False(SDS_CompatGates.AllowsTransportLoadInjection(null));

            SDS_CompatGates.Reset();
            Assert.True(SDS_CompatGates.AllowsTransportLoadInjection(null));
        }
    }
}
