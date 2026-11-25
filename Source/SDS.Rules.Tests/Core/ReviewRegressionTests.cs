using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    /// <summary>
    /// Rules 审查回归：VirtualHandoff 拒绝、保真预留幂等边界、镜像物理件数不一致回充、
    /// 容量“同品类多件占一名额 / 通道分开计”口径。
    /// </summary>
    public class ReviewRegressionTests
    {
        [Fact]
        public void VirtualHandoff_RejectedAtAllEntryPoints()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("VH_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 3), env.Port, out _));

            SDS_WithdrawPlan plan = new SDS_WithdrawPlan
            {
                query = new SDS_WithdrawQuery { key = key, quantity = 1, preserved = false },
                mode = SDS_WithdrawMode.VirtualHandoff
            };
            Assert.False(SDS_Rules.TryWithdraw(plan, null, out List<Thing> made));
            Assert.Null(made);
            Assert.Equal(3L, env.Ledger.GetCount(key));
            Assert.Equal(0L, env.Ledger.GetReserved(key));

            Type txType = RulesTestKit.TransactionType;
            MethodInfo begin = txType.GetMethod("Begin", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(begin);
            Assert.Null(begin.Invoke(null, new object[]
            {
                new List<SDS_WithdrawPlan> { plan }, null
            }));

            MethodInfo adopt = txType.GetMethod("BeginJobAdopt", BindingFlags.Public | BindingFlags.Static);
            Assert.NotNull(adopt);
            Assert.Null(adopt.Invoke(null, new object[]
            {
                4242, new List<SDS_WithdrawPlan> { plan }, null
            }));
        }

        [Fact]
        public void ReservePreservedPlan_EmptyPlanIsNotIdempotent()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("ResIdem_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey preservedKey = new SDS_StockKey(def, null, -1, true);
            SDS_PreserveHooks.Register(thing => thing.def == def);
            try
            {
                Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 2), env.Port, out _));
                List<Thing> stored = new List<Thing>();
                env.Ledger.AppendPreservedThings(stored);
                Assert.Single(stored);
                List<ThingCount> plan = new List<ThingCount> { new ThingCount(stored[0], 1) };

                Assert.True(SDS_Rules.TryReservePreservedForJob(61, plan, null));
                Assert.Equal(1L, env.Ledger.GetReserved(preservedKey));

                // 参数无效（空计划）不享受幂等：返回 false，且不动既有预留。
                Assert.False(SDS_Rules.TryReservePreservedForJob(61, new List<ThingCount>(), null));
                Assert.Equal(1L, env.Ledger.GetReserved(preservedKey));

                // 参数有效且已有预留：幂等 true，不重复累加。
                Assert.True(SDS_Rules.TryReservePreservedForJob(61, plan, null));
                Assert.Equal(1L, env.Ledger.GetReserved(preservedKey));

                // 同 job 已有保真预留时，按 def 的虚拟预留不覆盖既有预留。
                Assert.False(SDS_Rules.TryReserveForJob(61, def, 1, null, null,
                    out SDS_StockKey key, out int count));
                Assert.Null(key);
                Assert.Equal(0, count);
                Assert.Equal(1L, env.Ledger.GetReserved(preservedKey));
            }
            finally
            {
                SDS_PreserveHooks.Reset();
            }
        }

        [Fact]
        public void Mirror_PhysicalCountMismatch_RollsBackLedger()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("MirrorMismatch_" + Guid.NewGuid().ToString("N"));
            SDS_StockKey key = new SDS_StockKey(def, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 5), env.Port, out _));

            Thing mirror = CoreTestKit.Make(def, 3);
            Assert.False(SDS_Rules.TryTakeMirror(mirror, 3, new WrongCountMirrorPhysical(), out Thing taken));
            Assert.Null(taken);
            Assert.Equal(5L, env.Ledger.GetCount(key));
            Assert.Equal(0L, env.Ledger.GetReserved(key));
        }

        [Fact]
        public void Capacity_SameCategoryMultipleThings_OneSlot()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("CapSame_" + Guid.NewGuid().ToString("N"));
            SDS_PreserveHooks.Register(thing => thing.def == def);
            try
            {
                Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 1), env.Port, out _));
                Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 1), env.Port, out _));

                Assert.Equal(1L, SDS_Core.UsedCapacity);
                Assert.Equal(2L, env.Ledger.GetPreservedCount(new SDS_StockKey(def, null, -1, true)));
            }
            finally
            {
                SDS_PreserveHooks.Reset();
            }
        }

        [Fact]
        public void Capacity_VirtualAndPreservedSameDef_TwoSlots()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef def = CoreTestKit.Def("CapChannel_" + Guid.NewGuid().ToString("N"));

            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 1), env.Port, out _));
            Assert.Equal(1L, SDS_Core.UsedCapacity);

            SDS_PreserveHooks.Register(thing => thing.def == def);
            try
            {
                Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(def, 1), env.Port, out _));
            }
            finally
            {
                SDS_PreserveHooks.Reset();
            }

            // CategoryKeyOf 含通道标记：同 def 的虚拟与保真各占一个品类名额（12.24）。
            Assert.Equal(2L, SDS_Core.UsedCapacity);
            Assert.Equal(1L, env.Ledger.GetCount(new SDS_StockKey(def, null, -1)));
            Assert.Equal(1L, env.Ledger.GetPreservedCount(new SDS_StockKey(def, null, -1, true)));
        }

        private sealed class WrongCountMirrorPhysical : ISDS_MirrorPhysical
        {
            public bool TryTakePhysical(SDS_StockKey key, Thing mirror, int count, out Thing made)
            {
                made = null;
                return false;
            }

            public bool TryTakeWholePhysical(SDS_StockKey key, Thing mirror, out Thing made)
            {
                // 故意少给 1 件，模拟物理层违反「按实际扣账件数产出」的契约。
                made = CoreTestKit.Make(key.thingDef, Math.Max(1, mirror.stackCount - 1));
                return true;
            }
        }
    }
}
