using System;
using System.Collections.Generic;
using System.Reflection;
using Verse;
using Xunit;

namespace SuperDimensionalStorage
{
    public class TransactionTests
    {
        private static int Units(List<Thing> things)
        {
            int units = 0;
            for (int i = 0; i < things.Count; i++)
            {
                if (things[i] != null && !things[i].Destroyed)
                {
                    units += things[i].stackCount;
                }
            }
            return units;
        }

        [Fact]
        public void Transaction_ReservedMaterializedDelivered()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("TxA");
            SDS_StockKey key = new SDS_StockKey(a, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 5), env.Port, out _));

            Type txType = RulesTestKit.TransactionType;
            MethodInfo begin = txType.GetMethod("BeginSingle", BindingFlags.Public | BindingFlags.Static);
            object tx = begin.Invoke(null, new object[] { key, 5, false, null, SDS_WithdrawMode.Hand });
            Assert.NotNull(tx);
            Assert.Equal(SDS_WithdrawEndState.Reserved, RulesTestKit.TransactionState(tx));

            MethodInfo execute = txType.GetMethod("ExecuteVirtual");
            object[] args = { null, false };
            Assert.True((bool)execute.Invoke(tx, args));
            Assert.Equal(SDS_WithdrawEndState.Materialized, RulesTestKit.TransactionState(tx));
            Assert.Equal(5, Units((List<Thing>)args[0]));

            txType.GetMethod("Deliver").Invoke(tx, null);
            Assert.Equal(SDS_WithdrawEndState.Delivered, RulesTestKit.TransactionState(tx));
            Assert.Equal(0L, env.Ledger.GetReserved(key));
            Assert.Equal(0L, env.Ledger.GetCount(key));
        }

        [Fact]
        public void Transaction_RolledBack_WhenExecuteFails()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("TxB");
            SDS_StockKey key = new SDS_StockKey(a, null, -1);
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 5), env.Port, out _));

            Type txType = RulesTestKit.TransactionType;
            object tx = txType.GetMethod("BeginSingle", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new object[] { key, 5, false, null, SDS_WithdrawMode.Hand });
            CoreTestKit.DebitVirtual(env.Ledger, key, 5);

            object[] args = { null, false };
            Assert.False((bool)txType.GetMethod("ExecuteVirtual").Invoke(tx, args));
            Assert.Equal(SDS_WithdrawEndState.Reserved, RulesTestKit.TransactionState(tx));

            txType.GetMethod("Rollback").Invoke(tx, null);
            Assert.Equal(SDS_WithdrawEndState.RolledBack, RulesTestKit.TransactionState(tx));
            Assert.Equal(0L, env.Ledger.GetCount(key));
            Assert.Equal(0L, env.Ledger.GetReserved(key));
        }

        [Fact]
        public void ReservedJob_PartialSuccess_ReturnsUndeliveredBack()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("TxC", stackLimit: 1);
            SDS_StockKey key = new SDS_StockKey(a, null, -1);
            for (int i = 0; i < 5; i++)
            {
                Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 1), env.Port, out _));
            }
            Assert.Equal(5L, env.Ledger.GetCount(key));
            Assert.True(SDS_Rules.TryReserveForJob(77, a, 5, null, key, out _, out _));

            Assert.True(SDS_Rules.TryWithdrawReservedForJob(77, out List<Thing> made));
            Assert.Equal(5, Units(made));
            Assert.Equal(0L, env.Ledger.GetCount(key));
            Assert.Equal(0L, env.Ledger.GetReserved(key));

            // Vanilla 只拿得下 2 件，其余 3 件回充虚拟通道
            SDS_Rules.ReturnThings(made.GetRange(0, 3), SDS_StockChannel.Virtual, null, null,
                SDS_ReturnFallback.None);
            Assert.Equal(3L, env.Ledger.GetCount(key));
        }
        [Fact]
        public void PreservedExact_EndToEnd_ReservesAndWithdraws()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("PreservedA");
            SDS_PreserveHooks.Register(thing => thing.def != null && thing.def.defName == "PreservedA");
            for (int i = 0; i < 3; i++)
            {
                Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 1), env.Port, out _));
            }

            SDS_StockKey virtualKey = new SDS_StockKey(a, null, -1);
            SDS_StockKey preservedKey = new SDS_StockKey(a, null, -1, true);
            Assert.Equal(0L, env.Ledger.GetCount(virtualKey));
            Assert.Equal(3L, env.Ledger.GetPreservedCount(preservedKey));

            List<Thing> stored = new List<Thing>();
            env.Ledger.AppendPreservedThings(stored);
            Assert.Equal(3, stored.Count);

            List<ThingCount> plan = new List<ThingCount>();
            for (int i = 0; i < stored.Count; i++)
            {
                plan.Add(new ThingCount(stored[i], 1));
            }

            // 门面建保真预留 → 释放
            Assert.True(SDS_Rules.TryReservePreservedForJob(55, plan, null));
            Assert.Equal(3L, env.Ledger.GetReserved(preservedKey));
            SDS_Rules.ReleaseJobReservation(55);
            Assert.Equal(0L, env.Ledger.GetReserved(preservedKey));

            Assert.True(SDS_Rules.TryWithdrawPreservedExact(plan, null, out List<Thing> made));
            Assert.Equal(3, Units(made));
            Assert.Equal(0L, env.Ledger.GetPreservedCount(preservedKey));
            Assert.Equal(0L, env.Ledger.GetReserved(preservedKey));
        }

        [Fact]
        public void PreservedExact_OverCountPlan_FailsWholeTransaction()
        {
            RulesTestEnv env = RulesTestEnv.Create(capacity: 30);
            env.AddStructure(SDS_StructureKind.Core);
            ThingDef a = CoreTestKit.Def("PreservedOver");
            SDS_PreserveHooks.Register(thing => thing.def != null && thing.def.defName == "PreservedOver");
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 3), env.Port, out _));
            Assert.True(SDS_Rules.TryDeposit(CoreTestKit.Make(a, 2), env.Port, out _));

            List<Thing> stored = new List<Thing>();
            env.Ledger.AppendPreservedThings(stored);
            Assert.Equal(2, stored.Count);
            SDS_StockKey preservedKey = new SDS_StockKey(a, null, -1, true);
            Assert.Equal(5L, env.Ledger.GetPreservedCount(preservedKey));

            // ThingCount 构造时会按当时件数夹取；这里先建计划、再把真身缩小，模拟「计划过期」。
            Thing first = stored[0];
            List<ThingCount> plan = new List<ThingCount> { new ThingCount(first, first.stackCount) };
            first.stackCount = 1;
            long before = env.Ledger.GetPreservedCount(preservedKey);

            // 足额不足必须整笔失败，不得静默少给后照常 Deliver。
            Assert.False(SDS_Rules.TryWithdrawPreservedExact(plan, null, out List<Thing> made));
            Assert.Null(made);
            Assert.Equal(before, env.Ledger.GetPreservedCount(preservedKey));
            Assert.Equal(0L, env.Ledger.GetReserved(preservedKey));
        }
    }
}
